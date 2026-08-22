using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Ciclo de vida del pago: emisión por la CAT, pago por la planta y
/// verificación por la CAT. Las transiciones son de un solo sentido.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CicloPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    [Fact]
    public async Task UnPagoNuevoNaceEnEstadoPendiente()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        var pago = new Pago
        {
            ProductoraId = productora.Id,
            MontoUsd = 120m,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        var guardado = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == pago.Id);

        guardado.Estado.ShouldBe(EstadoPago.Pendiente);
        guardado.MontoPagadoUsd.ShouldBeNull();
        guardado.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnDescuentoNoPuedeRepetirLaMismaNovedadEnElMismoPago()
    {
        // Índice único, no solo validación de servicio: dos peticiones
        // simultáneas pasarían las dos por la validación y descontarían
        // el mismo defecto dos veces.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();

        var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
        {
            CodigoLote = $"PAT-{Guid.NewGuid():N}"[..12],
            CentroAcopio = CentroAcopio.PAT,
            ProductoraId = productora.Id,
            FechaRecepcion = DateTime.UtcNow,
            CantidadAnimales = 1
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var novedad = new CoopagcuyApi.Features.Recepcion.Models.Novedad
        {
            LoteId = lote.Id,
            Tipo = TipoNovedad.SignosClinicos,
            Descripcion = "lesión visible",
            RegistradoPor = "Operadora de prueba"
        };
        db.Novedades.Add(novedad);

        var pago = new Pago
        {
            ProductoraId = productora.Id,
            LoteId = lote.Id,
            MontoUsd = 120m,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        db.Descuentos.Add(new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = novedad.Id,
            Descripcion = "llegó muerto",
            MontoUsd = 8m,
            RegistradoPor = "Planta de prueba"
        });
        await db.SaveChangesAsync();

        db.Descuentos.Add(new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = novedad.Id,
            Descripcion = "segundo intento sobre el mismo defecto",
            MontoUsd = 5m,
            RegistradoPor = "Planta de prueba"
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // JPEG mínimo válido: SOI + APP0 + EOI
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    [Fact]
    public async Task DosPagosSimultaneosNoDescuadranElMonto()
    {
        // Dos /pagar a la vez sobre el MISMO ticket, citando novedades
        // DISTINTAS. Antes del token de concurrencia en Pago.Estado
        // (AppDbContext), las dos peticiones leían Estado == Pendiente antes
        // de que ninguna escribiera, así que las dos pasaban la guardia; el
        // índice único de (PagoId, NovedadCatId) no las frena porque citan
        // novedades DISTINTAS; y el UPDATE de Pagos era last-writer-wins sin
        // ningún WHERE que lo impidiera. Resultado: las DOS filas de
        // Descuentos quedaban guardadas, pero MontoPagadoUsd solo reflejaba
        // el descuento de la que escribió último — el monto persistido y su
        // propia justificación dejaban de cuadrar.
        //
        // Con el token, EF agrega el Estado ORIGINAL a la cláusula WHERE del
        // UPDATE: la segunda en llegar afecta cero filas, lanza
        // DbUpdateConcurrencyException (hereda de DbUpdateException, que
        // RegistrarPagoEfectivoAsync ya rescata) y responde 409 sin haber
        // escrito nada — su Descuento se revierte con el resto de la
        // transacción del SaveChangesAsync que falló.
        //
        // La aserción no asume CUÁL de las dos gana: verifica el invariante
        // que nunca puede romperse, sea cual sea el orden real de llegada.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "defecto-a" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "defecto-b" },
        };

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId;
        List<int> novedadIds;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();

            novedadIds = await db.Novedades
                .Where(n => n.LoteId == loteId
                    && n.CuyRegistro != null
                    && n.CuyRegistro.ProductoraId == productora.Id)
                .OrderBy(n => n.Id)
                .Select(n => n.Id)
                .ToListAsync();
        }
        // Pin de la propia siembra: si esto no da 2, la prueba no está
        // ejercitando "dos novedades distintas" y todo lo de abajo no prueba
        // nada.
        novedadIds.Count.ShouldBe(2);

        var ticket = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        ticket.EnsureSuccessStatusCode();

        int pagoId;
        await using (var db = api.NuevoDbContext())
        {
            pagoId = await db.Pagos
                .Where(p => p.ProductoraId == productora.Id)
                .Select(p => p.Id)
                .FirstAsync();
        }

        object CuerpoCon(int novedadId, decimal monto) => new
        {
            descuentos = new[] { new
            {
                novedadCatId = novedadId,
                descripcion = $"defecto de la novedad {novedadId}",
                montoUsd = monto
            }},
            comprobanteBase64 = Convert.ToBase64String(JpegMinimo),
            pagadoPor = "Operador de planta"
        };

        // Dos HttpClient DISTINTOS y no uno compartido: ColeccionApi
        // serializa las CLASES de prueba entre sí (comparten base), pero
        // dentro de un mismo método nada impide que dos peticiones concurran
        // — es justo lo que hay que forzar aquí.
        var clienteA = api.ComoOperadorFaenamiento();
        var clienteB = api.ComoOperadorFaenamiento();

        var tareaA = clienteA.PostAsJsonAsync(
            $"/api/pagos/{pagoId}/pagar", CuerpoCon(novedadIds[0], 5m));
        var tareaB = clienteB.PostAsJsonAsync(
            $"/api/pagos/{pagoId}/pagar", CuerpoCon(novedadIds[1], 6m));

        await Task.WhenAll(tareaA, tareaB);

        await using var dbFinal = api.NuevoDbContext();
        var pagoFinal = await dbFinal.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == pagoId);
        var sumaDescuentos = await dbFinal.Descuentos.AsNoTracking()
            .Where(d => d.PagoId == pagoId)
            .SumAsync(d => d.MontoUsd);

        // El invariante entero de la feature, en una línea: lo que quedó
        // registrado como pagado tiene que ser EXACTAMENTE el ticket menos
        // lo que sus propias filas de Descuentos dicen que se descontó. Da
        // igual si ganó A, ganó B, o si una de las dos terminó en 409 — eso
        // no se afirma aparte porque cualquier desacuerdo entre el monto y
        // su propia justificación ya rompe esta única aserción.
        pagoFinal.MontoPagadoUsd.ShouldBe(pagoFinal.MontoUsd - sumaDescuentos);
    }
}

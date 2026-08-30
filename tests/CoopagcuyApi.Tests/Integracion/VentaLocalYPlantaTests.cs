using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Una venta local no genera trabajo para la planta y no se deja tocar por
/// ella: el dinero ya lo recibió la CAT. Y lo que se vendió deja de contar
/// para el envío.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VentaLocalYPlantaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task UnaVentaLocalNoApareceEnLaColaDeLaPlanta()
    {
        // Feature 2 del pedido: el operador de faenamiento no debe enterarse.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldNotContain($"\"id\":{venta.PagoId}");
    }

    [Fact]
    public async Task UnPagoDeVentaLocalPendienteNoApareceEnLaColaAunqueElEstadoLoPermita()
    {
        // Pin del segundo "&& !p.EsVentaLocal" de ListarPorPagarAsync.
        // Sembrar por la vía normal (VenderAsync) nace en Estado.Recibido, y
        // el filtro de la cola ya exige Estado == Pendiente — así que borrar
        // "&& !p.EsVentaLocal" deja UnaVentaLocalNoApareceEnLaColaDeLaPlanta
        // en verde de todos modos: esa prueba no puede pinnear la segunda
        // defensa.
        //
        // Aquí se siembra directo en la base un Pago con EsVentaLocal = true
        // Y Estado = Pendiente — una combinación que el servicio normal
        // nunca produce, pero que la guarda tiene que cubrir igual, porque
        // es precisamente el caso donde el primer predicado (Estado ==
        // Pendiente) YA NO basta para excluirlo. Solo así quitar
        // "&& !p.EsVentaLocal" pone roja esta prueba. Misma técnica que
        // UnaJaulaHistoricaSinDetalleSigueApareciendoComoPendiente en este
        // mismo archivo.
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, "PAT");

        const string codigoLote = "PAT-20260822-901";
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = codigoLote,
                ProductoraId = productora.Id,
                CentroAcopio = "PAT",
                CantidadAnimales = 3,
                PesoTotalGramos = 3900,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();

            db.Pagos.Add(new CoopagcuyApi.Features.Pagos.Models.Pago
            {
                ProductoraId = productora.Id,
                LoteId = lote.Id,
                MontoUsd = 30m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Efectivo",
                Estado = EstadoPago.Pendiente,
                EsVentaLocal = true,
                Responsable = "Operadora de prueba"
            });
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldNotContain(codigoLote);
    }

    [Fact]
    public async Task LaPlantaNoPuedePagarUnaVentaLocal()
    {
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{venta.PagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // El 409 por sí solo no distingue la guarda de EsVentaLocal del
        // chequeo de Estado != Pendiente que ya existía: una venta local
        // nace Recibido, así que ese chequeo devolvería el mismo 409 aunque
        // se quitara la guarda. Lo que la guarda aporta de más — y lo único
        // que puede ponerla roja — es que el operador de planta lea que es
        // una venta local, no una frase sobre estados internos.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("venta local");

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == venta.PagoId);
        pago.ComprobanteUrl.ShouldBeNull();
        pago.PagadoPor.ShouldBeNull();
    }

    [Fact]
    public async Task LaCatNoPuedeVerificarUnaVentaLocal()
    {
        // No hay nada que verificar: no existe transferencia ni captura.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{venta.PagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Mismo motivo que en LaPlantaNoPuedePagarUnaVentaLocal: el 409 solo
        // lo daría igual el chequeo de Estado != Pagado que ya existía (una
        // venta local nace Recibido). Lo que la guarda de EsVentaLocal
        // protege — y lo que hace falta afirmar para que quitarla ponga roja
        // esta prueba — es que el mensaje le diga a la CAT que es una venta
        // local y no una frase sobre estados internos.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("venta local");

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == venta.PagoId);
        pago.VerificadoPor.ShouldBeNull();
        pago.FechaVerificacion.ShouldBeNull();
    }

    [Fact]
    public async Task ElEnvioSeLimitaALosCuyesQueQuedan()
    {
        // 5 entregados, 2 vendidos: la planta no puede recibir más de 3.
        var venta = await VenderAsync(2);
        var codigo = await CerrarYCodigoAsync(venta.LoteId);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 4,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConLoQueQuedaElEnvioSeAcepta()
    {
        var venta = await VenderAsync(2);
        var codigo = await CerrarYCodigoAsync(venta.LoteId);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 3,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });

        respuesta.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UnLoteVendidoEnteroYaNoSePuedeEnviar()
    {
        var venta = await VenderAsync(5);          // los cinco
        var codigo = await CerrarYCodigoAsync(venta.LoteId);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 1,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // El 409 por sí solo lo daría igual la comprobación de cantidad
        // (CantidadMovilizada > disponibles): con disponibles = 0 y el
        // validador exigiendo CantidadMovilizada >= 1, esa comprobación ya
        // dispara sola. Lo que esta guarda aporta de más —y lo único que
        // puede ponerla roja— es que la operadora lea que el lote se vendió
        // entero en la comunidad, en vez de una resta de cantidades que no
        // explica nada.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("completo en la comunidad");

        await using var db = api.NuevoDbContext();
        (await db.Movilizaciones.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task UnaVentaParcialNoDaElLotePorPagado()
    {
        // Vender 2 de 5 no salda lo que la planta debe pagar por los otros 3:
        // sin esto el lote desaparecía del selector de pago y esos animales
        // no se le cobraban a nadie.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{venta.ProductoraId}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain($"\"loteId\":{venta.LoteId}");
        // Y el conteo que ofrece es el de los que quedan, no el de la entrega.
        cuerpo.ShouldContain("\"cuyesEntregados\":3");
    }

    [Fact]
    public async Task UnLoteVendidoEnteroDejaDeEstarPendienteDePago()
    {
        var venta = await VenderAsync(5);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{venta.ProductoraId}");

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldNotContain($"\"loteId\":{venta.LoteId}");
    }

    [Fact]
    public async Task UnaJaulaHistoricaSinDetalleSigueApareciendoComoPendiente()
    {
        // Jaula histórica: cargada con CantidadAnimales a mano y sin ninguna
        // fila en CuyRegistros, igual que siembra AlcancePagosTests. Ahí
        // l.Cuyes está vacío, así que cualquier Any(c => ...) sobre esa
        // colección da false pase lo que pase. Si el filtro de "lote vendido
        // entero" no distingue este caso del de una jaula con detalle por
        // animal, el lote desaparece del selector aunque nadie haya vendido
        // nada, y la productora se queda sin poder cobrarlo.
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, "PAT");

        const string codigoLote = "PAT-20260822-900";
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = codigoLote,
                ProductoraId = productora.Id,
                CentroAcopio = "PAT",
                CantidadAnimales = 4,
                PesoTotalGramos = 5200,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{productora.Id}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain($"\"codigoLote\":\"{codigoLote}\"");
    }

    private async Task<string> CerrarYCodigoAsync(int loteId)
    {
        await using var db = api.NuevoDbContext();
        var lote = await db.Lotes.FirstAsync(l => l.Id == loteId);
        lote.Cerrado = true;
        await db.SaveChangesAsync();
        return lote.CodigoLote;
    }

    // ── Sembrado compartido con la Tarea 4 ────────────────────────────

    private sealed record Venta(int PagoId, int LoteId, int ProductoraId, int[] CuyIds);

    /// Entrega de 5 cuyes en PAT y venta local de los `vendidos` primeros.
    private async Task<Venta> VenderAsync(int vendidos)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, "PAT");

        var cuyes = Enumerable.Range(0, 5).Select(_ => new
        {
            pesoGramos = 1300m,
            colorPelaje = "Blanco",
            estadoOreja = "Blanda",
            tamanoAnimal = "Normal"
        }).ToArray();

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
        int[] ids;
        await using (var db = api.NuevoDbContext())
        {
            ids = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToArrayAsync();

            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0])
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids.Take(vendidos).ToArray(),
                montoUsd = 15m * vendidos,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoId = await db2.Pagos
            .Where(p => p.EsVentaLocal)
            .Select(p => p.Id)
            .FirstAsync();

        return new Venta(pagoId, loteId, productora.Id, ids);
    }
}

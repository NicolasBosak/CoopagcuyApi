using System.Net;
using System.Net.Http.Json;
using Azure.Storage.Blobs;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Un descuento solo puede apoyarse en una novedad que el CAT registró sobre
/// un cuy de ESA productora en ESE lote. Sin novedad de origen no hay
/// descuento: es la garantía entera de la feature.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class DescuentoTrazableTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaA = "0104576277";
    private const string CedulaB = "0102030405";

    // JPEG mínimo válido: SOI + APP0 + EOI
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    private static string Comprobante => Convert.ToBase64String(JpegMinimo);

    [Fact]
    public async Task ElMontoPagadoSaleDeLaRestaDelServidor()
    {
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "llegó con la lesión abierta",
                    montoUsd = 17m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);

        pago.MontoPagadoUsd.ShouldBe(103m);
        pago.Estado.ShouldBe(EstadoPago.Pagado);
        pago.ComprobanteUrl.ShouldNotBeNull();
        pago.PagadoPor.ShouldBe("Operador de planta");
    }

    [Fact]
    public async Task UnaNovedadDeOtraProductoraSeRechaza()
    {
        // El corazón de la trazabilidad: la planta no puede citar el defecto
        // de otra para descontarle a esta.
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);
        var (_, novedadAjena) = await TicketConNovedadAsync(CedulaB, 90m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadAjena,
                    descripcion = "defecto que no es de esta productora",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnaNovedadSinCuyAsociadoSeRechaza()
    {
        // Las novedades de entrega (SinAyuno) no pertenecen a ningún animal:
        // no se puede descontar un cuy que no existe.
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);

        int novedadDeEntrega;
        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
            var novedad = new CoopagcuyApi.Features.Recepcion.Models.Novedad
            {
                LoteId = pago.LoteId!.Value,
                Tipo = TipoNovedad.SinAyuno,
                Descripcion = "la entrega no venía en ayunas",
                RegistradoPor = "Operadora de prueba"
            };
            db.Novedades.Add(novedad);
            await db.SaveChangesAsync();
            novedadDeEntrega = novedad.Id;
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadDeEntrega,
                    descripcion = "descuento sin animal",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LaSumaDeDescuentosNoPuedeSuperarElTicket()
    {
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "descuento mayor que el ticket",
                    montoUsd = 500m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoSePuedePagarDosVecesElMismoTicket()
    {
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        object Cuerpo() => new
        {
            descuentos = new[] { new
            {
                novedadCatId = novedadId,
                descripcion = "lesión",
                montoUsd = 10m
            }},
            comprobanteBase64 = Comprobante,
            pagadoPor = "Operador de planta"
        };

        var primera = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", Cuerpo());
        primera.StatusCode.ShouldBe(HttpStatusCode.OK);

        var segunda = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", Cuerpo());
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnComprobanteInvalidoNoDejaBlobsHuerfanos()
    {
        // Se cuenta por DIFERENCIA DE BLOBS y no por filas: una prueba que
        // solo mira filas pasa con el fallo presente. Ya ocurrió una vez.
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);
        var (_, novedadAjena) = await TicketConNovedadAsync(CedulaB, 90m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "lesión",
                    montoUsd = 10m
                }},
                comprobanteBase64 = "esto-no-es-base64-valido!!!",
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ContarBlobsAsync()).ShouldBe(antes);

        // Segundo rechazo, con la captura BIEN formada y el descuento mal.
        // Sin este bloque la prueba solo fijaría que la captura se valida
        // antes de subir, y pasaría igual con la subida adelantada por
        // encima de la validación de descuentos —que es justo el fallo que
        // dejó blobs huérfanos—: el 400 del base64 salta antes y la subida
        // nunca llega a ejecutarse. Esta mitad es la que recorre ese tramo.
        var conDescuentoAjeno = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadAjena,
                    descripcion = "defecto que no es de esta productora",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        conDescuentoAjeno.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ContarBlobsAsync()).ShouldBe(antes);
    }

    [Fact]
    public async Task SinComprobanteNoSePuedeMarcarComoPagado()
    {
        // Un pago marcado sin su captura es peor que un error: la CAT no
        // tendría nada que verificar y el ticket quedaría bloqueado.
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = "",
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SinDescuentosSePagaElTicketCompleto()
    {
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.MontoPagadoUsd.ShouldBe(120m);
    }

    private static async Task<int> ContarBlobsAsync()
    {
        var cliente = new BlobServiceClient(ApiFactory.CadenaBlob);
        var contenedor = cliente.GetBlobContainerClient("comprobantes-pago");
        await contenedor.CreateIfNotExistsAsync();

        var total = 0;
        await foreach (var _ in contenedor.GetBlobsAsync()) total++;
        return total;
    }

    /// Entrega con un cuy con signos clínicos + ticket. Devuelve (pagoId, novedadId).
    private async Task<(int PagoId, int NovedadId)> TicketConNovedadAsync(
        string cedula, decimal monto)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "lesion-visible" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
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

        int loteId, novedadId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();

            novedadId = await db.Novedades
                .Where(n => n.LoteId == loteId
                    && n.CuyRegistro != null
                    && n.CuyRegistro.ProductoraId == productora.Id
                    && n.Tipo == TipoNovedad.SignosClinicos)
                .Select(n => n.Id)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoId = await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();

        return (pagoId, novedadId);
    }
}

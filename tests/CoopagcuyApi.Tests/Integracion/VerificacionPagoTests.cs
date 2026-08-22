using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Cierre del ciclo: la CAT abre la captura de la transferencia y confirma
/// que el dinero llegó. La imagen deja de servirse a los 5 días.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VerificacionPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    [Fact]
    public async Task LaCatDelPagoDescargaElComprobante()
    {
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await respuesta.Content.ReadAsByteArrayAsync()).ShouldBe(JpegMinimo);
    }

    [Fact]
    public async Task UnaCatAjenaRecibe404()
    {
        // 404 y no 403: confirmar que el pago existe filtraría datos de otro
        // centro.
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PasadaLaCaducidadElApiDejaDeServirla()
    {
        // El blob puede seguir existiendo —Azure barre cuando le toca— pero
        // el API deja de servirlo en el momento exacto. Mismo patrón que la
        // evidencia clínica.
        var pagoId = await TicketPagadoAsync();

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnTicketSinPagarNoTieneComprobante()
    {
        var pagoId = await TicketSinPagarAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// Ticket emitido y sin pagar. Devuelve el Id.
    private async Task<int> TicketSinPagarAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

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

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }

    /// Ticket ya pagado por la planta, con su captura subida.
    private async Task<int> TicketPagadoAsync()
    {
        var pagoId = await TicketSinPagarAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Convert.ToBase64String(JpegMinimo),
                pagadoPor = "Operador de planta"
            });
        respuesta.EnsureSuccessStatusCode();

        return pagoId;
    }

    [Fact]
    public async Task AlVerificarSeFijaLaCaducidadACincoDias()
    {
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);

        pago.Estado.ShouldBe(EstadoPago.Recibido);
        pago.VerificadoPor.ShouldBe("Operadora de prueba");
        pago.FechaVerificacion.ShouldNotBeNull();
        pago.ComprobanteExpiraEn.ShouldNotBeNull();

        // Cinco días desde la verificación, con holgura de un minuto para no
        // atarse al instante exacto del reloj.
        var esperado = pago.FechaVerificacion!.Value.AddDays(5);
        pago.ComprobanteExpiraEn!.Value
            .ShouldBe(esperado, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task NoSePuedeVerificarUnTicketQueNadieHaPagado()
    {
        var pagoId = await TicketSinPagarAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoSePuedeVerificarDosVeces()
    {
        var pagoId = await TicketPagadoAsync();

        object Cuerpo() => new { verificadoPor = "Operadora de prueba" };

        var primera = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", Cuerpo());
        primera.StatusCode.ShouldBe(HttpStatusCode.OK);

        var segunda = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", Cuerpo());
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LaPlantaNoPuedeVerificarSuPropioPago()
    {
        // Quien paga no confirma que pagó: la verificación existe justamente
        // para que sea otro quien lo diga.
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnaCatAjenaNoPuedeVerificar()
    {
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operadora de otro centro"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

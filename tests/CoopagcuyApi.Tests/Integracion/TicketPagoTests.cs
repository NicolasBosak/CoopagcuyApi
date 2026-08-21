using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Services;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Ticket imprimible del pago pendiente.
///
/// Del binario del PDF no se puede afirmar casi nada: QuestPDF comprime su
/// texto. Por eso las líneas cuyo contenido depende de una regla se componen
/// en métodos estáticos y se fijan por unidad, igual que hace la guía de
/// movilización con TextoDeclaracionSanitaria.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class TicketPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    [Theory]
    [InlineData(EstadoPago.Pendiente, "PENDIENTE DE PAGO")]
    [InlineData(EstadoPago.Pagado, "PAGADO — POR VERIFICAR")]
    [InlineData(EstadoPago.Recibido, "PAGO VERIFICADO")]
    public void ElEstadoSeImprimeEnCastellanoYEnMayusculas(
        EstadoPago estado, string esperado)
    {
        TicketPagoService.TextoEstado(estado).ShouldBe(esperado);
    }

    [Fact]
    public void LaLeyendaAclaraQueNoEsFactura()
    {
        // La productora se lleva este papel. Si parece una factura, lo será
        // para ella — y no lo es.
        TicketPagoService.LeyendaLegal()
            .ShouldContain("no es una factura", Case.Insensitive);
    }

    [Fact]
    public async Task ElTicketSeDescargaComoPdfNoVacio()
    {
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        // Cabecera de PDF: %PDF
        bytes[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task ElOperadorDeFaenamientoTambienPuedeDescargarlo()
    {
        // Es quien va a pagar: necesita ver el ticket que tiene delante la
        // productora.
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnOperadorDeOtroCentroRecibe404()
    {
        // 404 y no 403: confirmar que el pago existe ya filtraría información
        // de otro CAT.
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// Entrega real de 3 cuyes en PAT + su ticket de $120. Devuelve el Id.
    private async Task<int> PagoSembradoAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 3).Select(_ => new
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
}

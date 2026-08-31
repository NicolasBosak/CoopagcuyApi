using System.Net.Http.Json;
using Azure.Storage.Blobs;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El espacio se libera a los 5 días de verificar, no a los 30 de la política
/// de Azure. Como el contenedor del API se apaga sin tráfico, una tarea
/// programada dentro de ella no correría: el barrido se engancha al tráfico
/// que ya existe, la consulta de pagos de la CAT.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BarridoComprobantesTests(ApiFactory api) : IAsyncLifetime
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
    public async Task ConsultarLaListaBorraLosBlobsYaCaducados()
    {
        // Por diferencia y no por total: Azurite no se limpia entre pruebas
        // (solo la base lo hace, vía Respawn), así que el contenedor arrastra
        // blobs de otras clases que corrieron antes en la misma batería. Un
        // ShouldBe(0) a secas sería válido en aislamiento y frágil en la
        // batería completa — justo el vicio que ya le costó una prueba floja
        // a este repo.
        var antesDeSubir = await ContarBlobsAsync();

        var pagoId = await TicketPagadoAsync();

        // Caducado hace un minuto
        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        (await ContarBlobsAsync()).ShouldBe(antesDeSubir + 1);

        // La consulta normal de la CAT es la que dispara el barrido
        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        (await ContarBlobsAsync()).ShouldBe(antesDeSubir);

        // La fila sobrevive al binario: el pago no desaparece del historial
        await using var db2 = api.NuevoDbContext();
        var final = await db2.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        final.ComprobanteUrl.ShouldBeNull();
        final.MontoPagadoUsd.ShouldNotBeNull();
    }

    [Fact]
    public async Task UnComprobanteVigenteNoSeBorra()
    {
        var pagoId = await TicketPagadoAsync();

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn = DateTime.UtcNow.AddDays(3);
            await db.SaveChangesAsync();
        }

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        (await ContarBlobsAsync()).ShouldBe(antes);
    }

    [Fact]
    public async Task UnPagoSinVerificarQuedaConCaducidadA30DiasYNoSeBarreAntes()
    {
        // ComprobanteExpiraEn YA se fija al pagar (a 30 días, el mismo plazo
        // que la política de Azure sobre "comprobantes-pago"), no solo al
        // verificar. Antes se dejaba en null hasta la verificación, y un pago
        // que la CAT nunca verifica nunca cumplía el predicado del barrido
        // (exige ComprobanteExpiraEn no nulo): Azure borraba el blob a los 30
        // días por su cuenta, pero ComprobanteUrl y la fila se quedaban
        // apuntando a un blob que ya no existía, para siempre. Aquí se
        // verifica la otra mitad del arreglo: con la fecha ya puesta pero
        // TODAVÍA no vencida, la captura sigue viva — no se barre antes de
        // tiempo solo por dejar de ser null.
        var pagoId = await TicketPagadoAsync();

        DateTime? esperado;
        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn.ShouldNotBeNull();
            esperado = pago.FechaPagoEfectivo!.Value.AddDays(30);
            pago.ComprobanteExpiraEn!.Value.ShouldBe(esperado.Value, TimeSpan.FromMinutes(1));
        }

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        (await ContarBlobsAsync()).ShouldBe(antes);
    }

    [Fact]
    public async Task UnaUrlCorruptaSeLimpiaEnVezDeReintentarseSiempre()
    {
        // NombreDeBlob (PagoService) devuelve null ante cualquier cosa que no
        // sea una URI válida con contenedor y nombre, y el barrido limpia
        // ComprobanteUrl igual en ese caso, sin intentar tocar Blob. Esto es
        // lo que evita que una fila corrupta —una URL que quedó mal grabada—
        // se reintente en CADA consulta de /api/pagos para siempre: el
        // predicado del barrido exige ComprobanteUrl no nulo, así que en
        // cuanto se limpia, la fila deja de calificar. Se siembra directo,
        // sin ningún fake: no hace falta que el blob exista de verdad para
        // ejercitar esta rama.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, "PAT");

        int pagoId;
        await using (var db = api.NuevoDbContext())
        {
            var pago = new CoopagcuyApi.Features.Pagos.Models.Pago
            {
                ProductoraId = productora.Id,
                MontoUsd = 120m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Transferencia",
                Responsable = "Operadora de prueba",
                Estado = EstadoPago.Pagado,
                MontoPagadoUsd = 120m,
                FechaPagoEfectivo = DateTime.UtcNow,
                PagadoPor = "Operador de planta",
                ComprobanteUrl = "no-es-una-uri",
                ComprobanteExpiraEn = DateTime.UtcNow.AddMinutes(-1)
            };
            db.Pagos.Add(pago);
            await db.SaveChangesAsync();
            pagoId = pago.Id;
        }

        // La consulta normal de la CAT es la que dispara el barrido. Si
        // NombreDeBlob no tolerara la URL corrupta, esta petición devolvería
        // 500 en vez de la lista.
        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        await using var dbFinal = api.NuevoDbContext();
        var final = await dbFinal.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        final.ComprobanteUrl.ShouldBeNull();
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

    private async Task<int> TicketPagadoAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, "PAT");

        var cuyes = new object[]
        {
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

        var emision = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        emision.EnsureSuccessStatusCode();

        int pagoId;
        await using (var db = api.NuevoDbContext())
        {
            pagoId = await db.Pagos
                .Where(p => p.ProductoraId == productora.Id)
                .Select(p => p.Id)
                .FirstAsync();
        }

        var pagado = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Convert.ToBase64String(JpegMinimo),
                pagadoPor = "Operador de planta"
            });
        pagado.EnsureSuccessStatusCode();

        return pagoId;
    }
}

using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Emisión del ticket por la CAT: siempre transferencia, siempre con lote.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class EmisionTicketTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private static object Cuy(decimal peso) => new
    {
        pesoGramos = peso,
        colorPelaje = "Blanco",
        estadoOreja = "Blanda",
        tamanoAnimal = "Normal"
    };

    /// Registra una entrega real y devuelve (productoraId, loteId).
    private async Task<(int ProductoraId, int LoteId)> EntregaAsync(int cuantosCuyes)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, cuantosCuyes)
            .Select(_ => Cuy(1300m)).ToArray();

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var loteId = await db.CuyRegistros
            .Where(c => c.ProductoraId == productora.Id)
            .Select(c => c.LoteId)
            .FirstAsync();

        return (productora.Id, loteId);
    }

    [Fact]
    public async Task ElPagoSeGuardaSiempreComoTransferenciaYEnPendiente()
    {
        var (productoraId, loteId) = await EntregaAsync(3);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId,
                montoUsd = 120m,
                // El cliente manda basura a propósito: el servidor la ignora
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.ProductoraId == productoraId);

        pago.MetodoPago.ShouldBe("Transferencia");
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.NumeroDias.ShouldBeNull();
        pago.ValorPorDia.ShouldBeNull();
    }

    [Fact]
    public async Task UnPagoSinLoteSeRechaza()
    {
        // Un ticket que dice "por los cuyes que aportó a cierto lote" no
        // puede existir sin lote, y sin lote no hay novedades que trazar.
        var (productoraId, _) = await EntregaAsync(3);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId = (int?)null,
                montoUsd = 120m,
                metodoPago = "Transferencia",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}

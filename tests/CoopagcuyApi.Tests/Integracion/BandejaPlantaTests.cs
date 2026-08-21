using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Lo que ve el operador de faenamiento: los tickets que le toca pagar, de
/// los TRES centros de acopio, y los cuyes con novedad de cada uno.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BandejaPlantaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Dos cédulas válidas distintas: una productora por centro
    private const string CedulaPat = "0104576277";
    private const string CedulaNie = "0102030405";

    [Fact]
    public async Task LaPlantaVeLosTicketsDeTodosLosCentros()
    {
        // La planta es única y atiende a los tres CAT: acotarla por centro
        // la dejaría sin ver la mitad de lo que tiene que pagar.
        await PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m);
        await PagoSembradoAsync(CentroAcopio.NIE, CedulaNie, 90m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<TicketPorPagarDto>>("/api/pagos/por-pagar");

        respuesta.ShouldNotBeNull();
        respuesta.Count.ShouldBe(2);
        respuesta.Select(t => t.CentroAcopio)
            .ShouldBe(new[] { "PAT", "NIE" }, ignoreOrder: true);
    }

    [Fact]
    public async Task UnTicketYaPagadoDesapareceDeLaBandeja()
    {
        var pagoId = await PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m);

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.Estado = EstadoPago.Pagado;
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<TicketPorPagarDto>>("/api/pagos/por-pagar");

        respuesta!.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaOperadoraDeCatNoEntraALaBandejaDeLaPlanta()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SoloSeListanLosCuyesConNovedadDeEsaProductora()
    {
        // 3 cuyes: uno con signos clínicos, dos sanos. Y una productora
        // distinta en el mismo lote con otro cuy con novedad, que NO debe
        // aparecer: el ticket es de una sola productora.
        var pagoId = await PagoConNovedadAsync();

        var cuyes = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<CuyConNovedadDto>>(
                $"/api/pagos/{pagoId}/cuyes-con-novedad");

        cuyes.ShouldNotBeNull();
        cuyes.Count.ShouldBe(1);
        cuyes[0].TipoNovedad.ShouldBe("SignosClinicos");
        cuyes[0].Descripcion.ShouldContain("lesion-visible");
    }

    /// Entrega + ticket. Devuelve el Id del pago.
    private async Task<int> PagoSembradoAsync(
        CentroAcopio cat, string cedula, decimal monto, string? signos = null)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, cat, comunidadId: cat == CentroAcopio.PAT ? 1 : 2);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = signos },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = cat.ToString(),
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

        var pago = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }

    private Task<int> PagoConNovedadAsync() =>
        PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m, "lesion-visible");
}

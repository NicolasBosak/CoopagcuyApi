using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Una operadora de CAT ve y registra los pagos de SU centro. El filtro ya
/// estaba implementado —IPagoService lo recibe en las cinco operaciones— pero
/// nadie lo había fijado más allá de la descarga del ticket. Estas pruebas son
/// el entregable: no acompañan a un cambio, lo verifican.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AlcancePagosTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaPat = "0104576277";
    private const string CedulaNie = "0102030405";

    private sealed record FilaPago(int Id, int ProductoraId, decimal MontoUsd);

    [Fact]
    public async Task ElListadoNoTraeLosPagosDeOtroCentro()
    {
        var (pagoPat, _) = await Sembrador.PagoConNovedadAsync(api, CedulaPat, 120m);

        // Una productora de NIE con su propio pago, creado por su operadora.
        var productoraNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);
        var pagoNie = await PagoDirectoAsync(productoraNie.Id, CentroAcopio.NIE);

        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaPago>>();
        filas.ShouldNotBeNull();

        filas.Select(f => f.Id).ShouldContain(pagoPat);
        filas.Select(f => f.Id).ShouldNotContain(pagoNie);
    }

    [Fact]
    public async Task NoSePuedeRegistrarUnPagoAUnaProductoraDeOtroCentro()
    {
        var productoraNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        int loteNie;
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = "NIE-20260818-001",
                ProductoraId = productoraNie.Id,
                CentroAcopio = CentroAcopio.NIE,
                CantidadAnimales = 1,
                PesoTotalGramos = 1300,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
            loteNie = lote.Id;
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productoraNie.Id,
                loteId = loteNie,
                montoUsd = 90m,
                responsable = "Operadora de PAT"
            });

        respuesta.StatusCode.ShouldNotBe(HttpStatusCode.Created);
        respuesta.StatusCode.ShouldNotBe(HttpStatusCode.OK);

        await using var db2 = api.NuevoDbContext();
        (await db2.Pagos.AnyAsync(p => p.ProductoraId == productoraNie.Id))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task LosLotesPendientesDeOtroCentroNoSeConsultan()
    {
        var productoraNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        // Sin un lote pendiente real, la respuesta sale vacía por
        // construcción y la prueba pasaría aunque el filtro por centro
        // desapareciera del todo. El código de este lote ("NIE-20260818-003")
        // es lo que la aserción de más abajo comprueba que NUNCA sale por el
        // cable hacia la operadora de PAT.
        const string codigoLoteNie = "NIE-20260818-003";
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = codigoLoteNie,
                ProductoraId = productoraNie.Id,
                CentroAcopio = CentroAcopio.NIE,
                CantidadAnimales = 1,
                PesoTotalGramos = 1300,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{productoraNie.Id}");

        // Da igual si el servidor lo resuelve con 403, con 404 o con una
        // lista sin el lote de NIE: lo que no puede pasar bajo ningún
        // desenlace es que el código de ese lote salga en el cuerpo.
        if (respuesta.StatusCode == HttpStatusCode.OK)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            cuerpo.ShouldNotContain(codigoLoteNie);
        }
        else
        {
            respuesta.StatusCode.ShouldBeOneOf(
                HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        }
    }

    /// Pago de una productora, creado por la operadora de su propio centro.
    private async Task<int> PagoDirectoAsync(int productoraId, CentroAcopio cat)
    {
        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = $"{cat}-20260818-002",
                ProductoraId = productoraId,
                CentroAcopio = cat,
                CantidadAnimales = 1,
                PesoTotalGramos = 1300,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
            loteId = lote.Id;
        }

        var respuesta = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId,
                montoUsd = 90m,
                responsable = $"Operadora de {cat}"
            });
        respuesta.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productoraId)
            .Select(p => p.Id)
            .FirstAsync();
    }
}

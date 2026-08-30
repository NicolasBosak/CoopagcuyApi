using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Arreglo Task 4: al quitar el `enum CentroAcopio` quedaron tres respuestas
/// distintas a la misma pregunta — "¿qué pasa si `?cat=` trae algo que no es
/// un código en mayúsculas?" — en los tres sitios que filtran lecturas por
/// centro de acopio: Reportes (no filtraba), Productoras (cero filas) y
/// Recepción/lotes (cero filas). La decisión fue normalizar el filtro de
/// lectura en los tres, igual que ya se normalizaba la escritura: `?cat=pat`
/// vale lo mismo que `?cat=PAT`. Esta clase fija esa coherencia — no el
/// comportamiento de cada reporte en sí, que ya cubren sus propias clases de
/// prueba.
///
/// Un código de forma válida (tres letras) pero que no es ninguno de los
/// centros reales (`?cat=XYZ`) debe devolver cero filas en las tres: un
/// filtro que no encuentra nada devuelve nada, sin necesidad de conocer la
/// lista de códigos reales (eso sigue siendo la Task 6).
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class FiltroLecturaCatTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaPat = "0104576277";
    private const string CedulaNie = "0102030405";

    private sealed record ProductoraMin(string Cedula, string CatAsignado);
    private sealed record LoteMin(string CodigoLote, string CentroAcopio);
    private sealed record GananciaProductoraMin(int ProductoraId, string CentroAcopio);

    // ── GET /api/productoras?cat= ───────────────────────────────────────

    [Fact]
    public async Task Productoras_CatEnMinusculasFiltraIgualQueEnMayusculas()
    {
        await Sembrador.ProductoraAsync(api, CedulaPat, "PAT", comunidadId: 1);
        await Sembrador.ProductoraAsync(api, CedulaNie, "NIE", comunidadId: 2);

        var enMinusculas = await api.ComoAdmin().GetAsync("/api/productoras?cat=pat");
        var enMayusculas = await api.ComoAdmin().GetAsync("/api/productoras?cat=PAT");

        enMinusculas.StatusCode.ShouldBe(HttpStatusCode.OK);
        enMayusculas.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listaMin = await enMinusculas.Content.ReadFromJsonAsync<List<ProductoraMin>>();
        var listaMay = await enMayusculas.Content.ReadFromJsonAsync<List<ProductoraMin>>();

        listaMin.ShouldNotBeNull();
        listaMay.ShouldNotBeNull();
        listaMin.Select(p => p.Cedula).ShouldBe(listaMay.Select(p => p.Cedula));
        listaMin.ShouldHaveSingleItem();
        listaMin[0].Cedula.ShouldBe(CedulaPat);
    }

    [Fact]
    public async Task Productoras_CatConFormaValidaPeroInexistente_devuelveCeroFilas()
    {
        await Sembrador.ProductoraAsync(api, CedulaPat, "PAT", comunidadId: 1);

        var respuesta = await api.ComoAdmin().GetAsync("/api/productoras?cat=XYZ");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lista = await respuesta.Content.ReadFromJsonAsync<List<ProductoraMin>>();
        lista.ShouldNotBeNull();
        lista.ShouldBeEmpty();
    }

    // ── GET /api/recepcion/lotes?cat= ───────────────────────────────────

    private async Task SembrarLotesAsync()
    {
        var enPat = await Sembrador.ProductoraAsync(api, CedulaPat, "PAT", comunidadId: 1);
        var enNie = await Sembrador.ProductoraAsync(api, CedulaNie, "NIE", comunidadId: 2);

        await using var db = api.NuevoDbContext();
        db.Lotes.AddRange(
            new Lote
            {
                CodigoLote = "PAT-20260830-001",
                ProductoraId = enPat.Id,
                CentroAcopio = "PAT",
                CantidadAnimales = 3,
                PesoTotalGramos = 3 * 1300m,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado,
                Cerrado = true,
                ResponsableRecepcion = "Operadora de prueba"
            },
            new Lote
            {
                CodigoLote = "NIE-20260830-001",
                ProductoraId = enNie.Id,
                CentroAcopio = "NIE",
                CantidadAnimales = 2,
                PesoTotalGramos = 2 * 1300m,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado,
                Cerrado = true,
                ResponsableRecepcion = "Operadora de prueba"
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Lotes_CatEnMinusculasFiltraIgualQueEnMayusculas()
    {
        await SembrarLotesAsync();

        // Como AdminCooperativa: CatDelOperador() devuelve null y no pisa el
        // ?cat= de la query, que es justo lo que esta prueba necesita
        // ejercitar.
        var enMinusculas = await api.ComoAdmin().GetAsync("/api/recepcion/lotes?cat=pat");
        var enMayusculas = await api.ComoAdmin().GetAsync("/api/recepcion/lotes?cat=PAT");

        enMinusculas.StatusCode.ShouldBe(HttpStatusCode.OK);
        enMayusculas.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listaMin = await enMinusculas.Content.ReadFromJsonAsync<List<LoteMin>>();
        var listaMay = await enMayusculas.Content.ReadFromJsonAsync<List<LoteMin>>();

        listaMin.ShouldNotBeNull();
        listaMay.ShouldNotBeNull();
        listaMin.Select(l => l.CodigoLote).ShouldBe(listaMay.Select(l => l.CodigoLote));
        listaMin.ShouldHaveSingleItem();
        listaMin[0].CodigoLote.ShouldBe("PAT-20260830-001");
    }

    [Fact]
    public async Task Lotes_CatConFormaValidaPeroInexistente_devuelveCeroFilas()
    {
        await SembrarLotesAsync();

        var respuesta = await api.ComoAdmin().GetAsync("/api/recepcion/lotes?cat=XYZ");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lista = await respuesta.Content.ReadFromJsonAsync<List<LoteMin>>();
        lista.ShouldNotBeNull();
        lista.ShouldBeEmpty();
    }

    // ── GET /api/reportes/ganancias/productoras?cat= ────────────────────

    private async Task<(Productora Pat, Productora Nie)> SembrarPagosAsync()
    {
        var enPat = await Sembrador.ProductoraAsync(api, CedulaPat, "PAT", comunidadId: 1);
        var enNie = await Sembrador.ProductoraAsync(api, CedulaNie, "NIE", comunidadId: 2);

        await using var db = api.NuevoDbContext();
        db.Pagos.AddRange(
            new Pago
            {
                ProductoraId = enPat.Id,
                MontoUsd = 40m,
                MontoPagadoUsd = 40m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Efectivo",
                Estado = EstadoPago.Recibido,
                EsVentaLocal = true,
                Responsable = "Operadora de prueba"
            },
            new Pago
            {
                ProductoraId = enNie.Id,
                MontoUsd = 25m,
                MontoPagadoUsd = 25m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Efectivo",
                Estado = EstadoPago.Recibido,
                EsVentaLocal = true,
                Responsable = "Operadora de prueba"
            });
        await db.SaveChangesAsync();
        return (enPat, enNie);
    }

    [Fact]
    public async Task GananciasPorProductora_CatEnMinusculasFiltraIgualQueEnMayusculas()
    {
        await SembrarPagosAsync();
        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");

        var enMinusculas = await api.ComoAdmin().GetAsync(
            $"/api/reportes/ganancias/productoras?desde={hoy}&hasta={hoy}&cat=pat");
        var enMayusculas = await api.ComoAdmin().GetAsync(
            $"/api/reportes/ganancias/productoras?desde={hoy}&hasta={hoy}&cat=PAT");

        enMinusculas.StatusCode.ShouldBe(HttpStatusCode.OK);
        enMayusculas.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listaMin = await enMinusculas.Content
            .ReadFromJsonAsync<List<GananciaProductoraMin>>();
        var listaMay = await enMayusculas.Content
            .ReadFromJsonAsync<List<GananciaProductoraMin>>();

        listaMin.ShouldNotBeNull();
        listaMay.ShouldNotBeNull();
        listaMin.Select(g => g.ProductoraId).ShouldBe(listaMay.Select(g => g.ProductoraId));
        listaMin.ShouldHaveSingleItem();
        listaMin[0].CentroAcopio.ShouldBe("PAT");
    }

    [Fact]
    public async Task GananciasPorProductora_CatConFormaValidaPeroInexistente_devuelveCeroFilas()
    {
        await SembrarPagosAsync();
        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin().GetAsync(
            $"/api/reportes/ganancias/productoras?desde={hoy}&hasta={hoy}&cat=XYZ");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lista = await respuesta.Content.ReadFromJsonAsync<List<GananciaProductoraMin>>();
        lista.ShouldNotBeNull();
        lista.ShouldBeEmpty();
    }
}

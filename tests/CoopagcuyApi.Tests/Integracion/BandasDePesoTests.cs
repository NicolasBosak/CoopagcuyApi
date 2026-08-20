using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Rango operativo nuevo: por debajo de 1200 g se rechaza, entre 1200 y 1500
/// pasa limpio, por encima de 1500 se acepta y se anota. La evaluación corre
/// SIEMPRE en el servidor, también al sincronizar entregas capturadas offline
/// con un bundle antiguo: por eso se prueba por HTTP y no por unidad.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BandasDePesoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private async Task<CuyGuardado> RegistrarUnCuyAsync(decimal pesoGramos)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes = new[]
            {
                new
                {
                    pesoGramos,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal"
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var cuy = await db.CuyRegistros.AsNoTracking().SingleAsync();
        var novedades = await db.Novedades.AsNoTracking()
            .Where(n => n.Tipo != TipoNovedad.SinAyuno)
            .Select(n => n.Tipo)
            .ToListAsync();

        return new CuyGuardado(cuy.Estado, novedades);
    }

    [Fact]
    public async Task MilCientoNoventaYNueveGramosSeRechaza()
    {
        var cuy = await RegistrarUnCuyAsync(1199m);

        cuy.Estado.ShouldBe(EstadoLote.Rechazado);
        cuy.Novedades.ShouldContain(TipoNovedad.BajoPeso);
    }

    [Fact]
    public async Task MilDoscientosGramosSeAceptaSinNovedad()
    {
        var cuy = await RegistrarUnCuyAsync(1200m);

        cuy.Estado.ShouldBe(EstadoLote.Aceptado);
        cuy.Novedades.ShouldBeEmpty();
    }

    [Fact]
    public async Task MilQuinientosGramosSeAceptaSinNovedad()
    {
        // El límite superior es inclusivo: 1500 está DENTRO del rango.
        var cuy = await RegistrarUnCuyAsync(1500m);

        cuy.Estado.ShouldBe(EstadoLote.Aceptado);
        cuy.Novedades.ShouldBeEmpty();
    }

    [Fact]
    public async Task MilQuinientosUnGramosSeAceptaConSobrepeso()
    {
        var cuy = await RegistrarUnCuyAsync(1501m);

        // Sobrepeso NO rechaza: el animal está sano, solo fuera del rango
        // comercial. Es la distinción que pidió la cooperativa.
        cuy.Estado.ShouldBe(EstadoLote.ConNovedad);
        cuy.Novedades.ShouldContain(TipoNovedad.SobrePeso);
    }

    [Fact]
    public async Task ElColorNegroYaNoGeneraNovedad()
    {
        // "Negro" salió del catálogo de captura. Si llegara desde una tablet
        // con caché antigua, se guarda tal cual sin marcar el lote.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuerpo = new
        {
            centroAcopio = "PAT",
            productoraId = productora.Id,
            cuyes = new[]
            {
                new
                {
                    pesoGramos = 1300m,
                    colorPelaje = "Negro",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Normal"
                }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        };

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", cuerpo);
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var cuy = await db.CuyRegistros.AsNoTracking().SingleAsync();

        cuy.Estado.ShouldBe(EstadoLote.Aceptado);
        cuy.ColorPelaje.ShouldBe("Negro");

        var hayColorNoConforme = await db.Novedades.AsNoTracking()
            .AnyAsync(n => n.Tipo == TipoNovedad.ColorNoConforme);
        hayColorNoConforme.ShouldBeFalse();
    }

    private record CuyGuardado(EstadoLote Estado, List<TipoNovedad> Novedades);
}

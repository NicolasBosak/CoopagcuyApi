using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El despacho registra a qué precio se vendió, porque sin eso el sistema no
/// puede decir nada del margen: hasta ahora solo sabía lo que pagaba a las
/// productoras, que es la mitad de la resta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class PrecioDeVentaTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaProductora = "0104576277";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnDespachoSinPrecioSeRechaza()
    {
        var (loteFaenadoId, cuyIds) = await Sembrador.DespachableAsync(api, CedulaProductora);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", CuerpoDespacho(
                loteFaenadoId, cuyIds, precio: null));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = api.NuevoDbContext();
        (await db.Despachos.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task UnPrecioNoPositivoSeRechaza()
    {
        var (loteFaenadoId, cuyIds) = await Sembrador.DespachableAsync(api, CedulaProductora);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", CuerpoDespacho(
                loteFaenadoId, cuyIds, precio: 0m));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConPrecioSeGuardaYElTotalSeDeriva()
    {
        var (loteFaenadoId, cuyIds) = await Sembrador.DespachableAsync(api, CedulaProductora);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", CuerpoDespacho(
                loteFaenadoId, cuyIds, precio: 8.50m));

        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var despacho = await db.Despachos.AsNoTracking().FirstAsync();

        despacho.PrecioUnitarioUsd.ShouldBe(8.50m);

        // El total NO se guarda: se deriva. Guardarlo abriría la puerta a que
        // las dos cifras se contradigan, que es el defecto que este sistema
        // ya sufrió con MontoPagadoUsd y sus descuentos.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("17.00");   // 8.50 x 2 animales
    }

    private static object CuerpoDespacho(
        int loteFaenadoId, List<int> cuyIds, decimal? precio) => new
    {
        loteFaenadoId,
        cuyFaenamientoIds = cuyIds,
        clienteDestino = "Mercado Central",
        fechaDespacho = DateTime.UtcNow,
        responsable = "Operador de prueba",
        tipoMercado = "Local",
        precioUnitarioUsd = precio
    };
}

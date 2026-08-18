using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El reporte de Salida dejó de mostrar los despachos nuevos: el último
/// visible era del 04/08/2026 pese a haberse registrado despachos el 18/08.
/// Los despachos SÍ aparecen en la pantalla Despacho, que consulta sin filtro
/// de fecha, así que la fila existe y la diferencia entre ambas vistas es el
/// rango de fechas.
///
/// Estas pruebas separan las dos mitades del problema: si pasan, la consulta
/// del reporte es correcta y el fallo está en el valor de FechaDespacho que
/// se guarda; si fallan, el fallo está en la consulta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ReporteSalidaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Los nombres deben coincidir con ReporteSalidaDto: System.Text.Json empareja
    // por nombre, no por posición, y un nombre que no exista se deserializa como
    // null en silencio — la aserción fallaría con el sistema sano.
    private sealed record FilaSalida(
        string CodigoLoteFaenado, DateTime FechaDespacho, string Cliente,
        string Chofer, string Ruta, string TipoMercado, string Ubicacion,
        int Unidades, string Responsable);

    [Fact]
    public async Task UnDespachoDeHoy_apareceEnElReporteDelMesEnCurso()
    {
        var hoy = DateTime.UtcNow;
        await Sembrador.DespachoAsync(api, hoy, "Cliente de hoy");

        var desde = new DateTime(hoy.Year, hoy.Month, 1).ToString("yyyy-MM-dd");
        var hasta = hoy.ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/salida?desde={desde}&hasta={hasta}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        filas.ShouldContain(f => f.Cliente == "Cliente de hoy");
    }

    [Fact]
    public async Task UnDespachoDeHoy_apareceAunqueSeaElUltimoDiaDelRango()
    {
        // El límite superior del filtro es exclusivo (día siguiente a las
        // 00:00 UTC). Un despacho de esta tarde cae dentro del último día del
        // rango: si RangoUtc cortara a medianoche, esta prueba lo detectaría.
        var hoy = DateTime.UtcNow;
        await Sembrador.DespachoAsync(api, hoy, "Cliente del borde");

        var dia = hoy.ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/salida?desde={dia}&hasta={dia}");

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        filas.ShouldContain(f => f.Cliente == "Cliente del borde");
    }

    [Fact]
    public async Task UnDespachoFueraDelRango_noApareceEnElReporte()
    {
        // Control negativo: si esta prueba también fallara, el filtro no
        // estaría filtrando nada y las dos anteriores no probarían gran cosa.
        await Sembrador.DespachoAsync(
            api, DateTime.UtcNow.AddDays(-90), "Cliente antiguo");

        var hoy = DateTime.UtcNow;
        var desde = new DateTime(hoy.Year, hoy.Month, 1).ToString("yyyy-MM-dd");
        var hasta = hoy.ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/salida?desde={desde}&hasta={hasta}");

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        filas.ShouldNotContain(f => f.Cliente == "Cliente antiguo");
    }
}

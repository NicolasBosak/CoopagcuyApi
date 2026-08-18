using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El recorrido completo del despacho: se registra por el endpoint real y se
/// busca en el reporte de Salida, tal como lo hace la aplicación.
///
/// ReporteSalidaTests ya cubría la consulta del reporte con filas insertadas a
/// mano, y FechaDespachoEntranteTests la deserialización de la fecha. Faltaba
/// unirlo todo: que lo que escribe RegistrarDespachoAsync sea exactamente lo
/// que el reporte encuentra. Es la última pieza de código que quedaba por
/// descartar en el fallo reportado desde el CAT.
///
/// Los casos horarios importan porque el usuario trabaja en UTC-5: un despacho
/// de la tarde cae en el mismo día UTC, pero uno de después de las 19:00
/// locales ya pertenece al día UTC siguiente.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class DespachoExtremoAExtremoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private sealed record FilaSalida(
        string CodigoLoteFaenado, DateTime FechaDespacho, string Cliente,
        string Chofer, string Ruta, string TipoMercado, string Ubicacion,
        int Unidades, string Responsable);

    /// <summary>
    /// Cadena mínima hasta tener animales despachables: jaula → sesión de
    /// faenamiento → lote faenado. Devuelve los Id de los cuyes faenados.
    /// </summary>
    private async Task<(int LoteFaenadoId, List<int> CuyIds)> SembrarDespachableAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT, comunidadId: 1);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-20260818-001",
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 2,
            PesoTotalGramos = 1800,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Nicolas Nieves"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var loteFaenado = new LoteFaenado
        {
            Codigo = "FAE-20260818-001",
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba"
        };
        db.LotesFaenados.Add(loteFaenado);
        await db.SaveChangesAsync();

        var sesion = new RegistroFaenamiento
        {
            LoteId = lote.Id,
            LoteFaenadoId = loteFaenado.Id,
            NumeroSesion = 1,
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba",
            UnidadesFaenadas = 2,
            PesoTotalCanalGramos = 1200,
            EstadoCanal = EstadoCanal.Apto
        };
        db.Faenamientos.Add(sesion);
        await db.SaveChangesAsync();

        var cuyes = new[]
        {
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 1,
                PesoCanalGramos = 600, Estado = EstadoCanal.Apto
            },
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 2,
                PesoCanalGramos = 600, Estado = EstadoCanal.Apto
            }
        };
        db.CuyFaenamientos.AddRange(cuyes);
        await db.SaveChangesAsync();

        return (loteFaenado.Id, cuyes.Select(c => c.Id).ToList());
    }

    private async Task RegistrarAsync(int loteFaenadoId, int cuyId, DateTime fechaUtc)
    {
        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", new
            {
                loteFaenadoId,
                cuyFaenamientoIds = new[] { cuyId },
                clienteDestino = "Mercado de Cuenca",
                // Exactamente el formato que manda el front:
                // new Date(fecha).toISOString()
                fechaDespacho = fechaUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                responsable = "Nicolas Nieves",
                chofer = "Chofer de prueba",
                ruta = "Cuenca",
                tipoMercado = "Local",
                ciudad = "Cuenca",
                pais = "Ecuador"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created,
            await respuesta.Content.ReadAsStringAsync());
    }

    private async Task<List<FilaSalida>> SalidaAsync(string desde, string hasta)
    {
        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync($"/api/reportes/salida?desde={desde}&hasta={hasta}");
        respuesta.EnsureSuccessStatusCode();
        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        return filas;
    }

    [Fact]
    public async Task UnDespachoDeAhora_apareceEnElReporteDelMesEnCurso()
    {
        var (loteFaenadoId, cuyes) = await SembrarDespachableAsync();

        // "Ahora" y no una hora fija: el validador rechaza fechas pasadas
        // (FaenamientoValidators), así que una hora inventada del día haría
        // fallar la prueba por un motivo ajeno a lo que verifica.
        await RegistrarAsync(loteFaenadoId, cuyes[0], DateTime.UtcNow.AddMinutes(1));

        var hoyLocal = DateTime.UtcNow + FechaUtc.DesfasePiloto;
        var filas = await SalidaAsync(
            new DateTime(hoyLocal.Year, hoyLocal.Month, 1).ToString("yyyy-MM-dd"),
            hoyLocal.ToString("yyyy-MM-dd"));

        filas.ShouldContain(f => f.Cliente == "Mercado de Cuenca");
    }

    [Fact]
    public async Task UnDespachoDeLaNocheEnEcuador_apareceEnElRangoDelDiaLocal()
    {
        // El fallo reportado. En Ecuador (UTC-5) las 19:00 locales ya son las
        // 00:00 UTC del día siguiente, así que un despacho de la noche se
        // guarda con la fecha UTC de mañana.
        //
        // El filtro del reporte recibe fechas que el usuario entiende como
        // días LOCALES ("del 1 al 18 de agosto"), pero el servidor las trataba
        // como días UTC. Resultado: las últimas cinco horas de cada día local
        // —de 19:00 a medianoche— quedaban fuera de todo reporte, y un
        // despacho de la noche desaparecía aunque estuviera bien guardado.
        var (loteFaenadoId, cuyes) = await SembrarDespachableAsync();

        var hoyLocal = (DateTime.UtcNow + FechaUtc.DesfasePiloto).Date;
        // 19:30 de hoy en Ecuador = 00:30 UTC de mañana
        var nocheUtc = DateTime.SpecifyKind(
            hoyLocal.AddHours(19).AddMinutes(30) - FechaUtc.DesfasePiloto,
            DateTimeKind.Utc);

        // Si esa hora ya pasó, la prueba no puede registrar el despacho: el
        // validador rechaza fechas pasadas. Se corre entonces con el día
        // siguiente, que ejercita exactamente el mismo borde.
        if (nocheUtc <= DateTime.UtcNow)
        {
            hoyLocal = hoyLocal.AddDays(1);
            nocheUtc = DateTime.SpecifyKind(
                hoyLocal.AddHours(19).AddMinutes(30) - FechaUtc.DesfasePiloto,
                DateTimeKind.Utc);
        }

        await RegistrarAsync(loteFaenadoId, cuyes[0], nocheUtc);

        var filas = await SalidaAsync(
            new DateTime(hoyLocal.Year, hoyLocal.Month, 1).ToString("yyyy-MM-dd"),
            hoyLocal.ToString("yyyy-MM-dd"));

        filas.ShouldContain(f => f.Cliente == "Mercado de Cuenca");
    }
}

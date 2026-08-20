using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La evidencia clínica que capturó el CAT tiene que llegar al operador de
/// faenamiento, que es quien tiene el animal delante al procesarlo.
///
/// Para poder mostrarla junto al cuy correcto, la novedad necesita saber a qué
/// animal pertenece: antes solo lo decía el texto "Cuy #N: …" de su descripción,
/// que es una relación demasiado frágil para adjudicar un defecto.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class EvidenciaEnFaenamientoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    // JPEG mínimo válido: SOI + APP0 + EOI. Basta para el viaje de ida y vuelta.
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    private static object Cuy(decimal peso, string? signos = null, string? foto = null) => new
    {
        pesoGramos = peso,
        colorPelaje = "Blanco",
        estadoOreja = "Blanda",
        tamanoAnimal = "Normal",
        signosClinicos = signos,
        fotoBase64 = foto
    };

    private async Task<int> RegistrarEntregaAsync(
        CentroAcopio cat, object[] cuyes, bool enAyunas = true)
    {
        var productora = await Sembrador.ProductoraAsync(api, CedulaProductora, cat);

        var respuesta = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = cat.ToString(),
                productoraId = productora.Id,
                cuyes,
                enAyunas,
                responsableRecepcion = "Operadora de prueba"
            });

        respuesta.EnsureSuccessStatusCode();
        return productora.Id;
    }

    /// <summary>
    /// Deja los lotes del centro listos para faenar: cerrados y con la llegada
    /// a planta confirmada, que es lo que exige LotesDisponiblesAsync.
    /// </summary>
    private async Task PrepararParaFaenamientoAsync(CentroAcopio cat)
    {
        await using var db = api.NuevoDbContext();

        var lotes = await db.Lotes.Where(l => l.CentroAcopio == cat).ToListAsync();
        foreach (var lote in lotes)
        {
            lote.Cerrado = true;
            lote.FechaCierre = DateTime.UtcNow;
            db.Movilizaciones.Add(new Movilizacion
            {
                LoteId = lote.Id,
                FechaDespacho = DateTime.UtcNow,
                Conductor = "Conductor de prueba",
                CantidadMovilizada = lote.CantidadAnimales,
                ResponsableDespacho = "Responsable de prueba",
                SinAntibioticos7Dias = true,
                FechaRecepcionPlanta = DateTime.UtcNow,
                RecibidoPor = "Planta de prueba"
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LaNovedadClinicaQuedaEnlazadaAlCuyCorrectoAlPartirseLaJaula()
    {
        // 16 cuyes con la jaula en 15: el primero y el último caen en jaulas
        // DISTINTAS, y el último vuelve a ser "Cuy #1" en la suya. Emparejar
        // por el número dentro del lote sería ambiguo justo aquí.
        var cuyes = new object[16];
        cuyes[0] = Cuy(1300m, "lesion-del-primero", Convert.ToBase64String(JpegMinimo));
        for (var i = 1; i < 15; i++) cuyes[i] = Cuy(1300m);
        cuyes[15] = Cuy(1300m, "lesion-del-ultimo", Convert.ToBase64String(JpegMinimo));

        await RegistrarEntregaAsync(CentroAcopio.PAT, cuyes);

        await using var db = api.NuevoDbContext();

        foreach (var signos in new[] { "lesion-del-primero", "lesion-del-ultimo" })
        {
            var novedad = await db.Novedades.AsNoTracking()
                .SingleAsync(n => n.Tipo == TipoNovedad.SignosClinicos
                                  && n.Descripcion.Contains(signos));

            novedad.CuyRegistroId.ShouldNotBeNull(
                $"la novedad de '{signos}' debe saber a qué cuy pertenece");

            var cuy = await db.CuyRegistros.AsNoTracking()
                .SingleAsync(c => c.Id == novedad.CuyRegistroId);

            cuy.SignosClinicos.ShouldBe(signos);
        }
    }

    [Fact]
    public async Task LaNovedadDeLoteNoSeEnlazaANingunCuy()
    {
        // "Sin ayuno" es de la entrega, no de un animal concreto.
        await RegistrarEntregaAsync(
            CentroAcopio.PAT, [Cuy(1300m)], enAyunas: false);

        await using var db = api.NuevoDbContext();
        var novedad = await db.Novedades.AsNoTracking()
            .SingleAsync(n => n.Tipo == TipoNovedad.SinAyuno);

        novedad.CuyRegistroId.ShouldBeNull();
    }

    [Fact]
    public async Task LotesDisponiblesTraeElIdDeLaNovedadSoloEnElCuyConFoto()
    {
        await RegistrarEntregaAsync(CentroAcopio.PAT,
        [
            Cuy(1300m),
            Cuy(1300m, "lesion-visible", Convert.ToBase64String(JpegMinimo)),
            Cuy(1300m, "sin-foto")
        ]);
        await PrepararParaFaenamientoAsync(CentroAcopio.PAT);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/lotes-disponibles");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var lotes = await respuesta.Content.ReadFromJsonAsync<List<LoteParcial>>();
        var cuyes = lotes!.Single().CuyesDisponibles;

        // El cuy con foto trae el id; los otros dos no, ni el sano ni el que
        // tiene novedad clínica pero sin evidencia.
        cuyes.Count(c => c.NovedadFotoId is not null).ShouldBe(1);
        cuyes.Single(c => c.NumeroEnLote == 2).NovedadFotoId.ShouldNotBeNull();
        cuyes.Single(c => c.NumeroEnLote == 1).NovedadFotoId.ShouldBeNull();
        cuyes.Single(c => c.NumeroEnLote == 3).NovedadFotoId.ShouldBeNull();
    }

    [Fact]
    public async Task UnaFotoCaducadaNoApareceEnLotesDisponibles()
    {
        await RegistrarEntregaAsync(CentroAcopio.PAT,
            [Cuy(1300m, "lesion-vieja", Convert.ToBase64String(JpegMinimo))]);
        await PrepararParaFaenamientoAsync(CentroAcopio.PAT);

        await using (var db = api.NuevoDbContext())
        {
            var novedad = await db.Novedades
                .SingleAsync(n => n.Tipo == TipoNovedad.SignosClinicos);
            novedad.FotoExpiraEn = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/lotes-disponibles");
        var lotes = await respuesta.Content.ReadFromJsonAsync<List<LoteParcial>>();

        // Nulo, no un id que al pulsarlo daría 404: el botón no debe ofrecerse.
        lotes!.Single().CuyesDisponibles.Single().NovedadFotoId.ShouldBeNull();
    }

    [Fact]
    public async Task ElOperadorDeFaenamientoDescargaLaFotoDeCualquierCentro()
    {
        // La planta recibe jaulas de los cinco centros: no debe filtrarse por CAT.
        await RegistrarEntregaAsync(CentroAcopio.NIE,
            [Cuy(1300m, "lesion", Convert.ToBase64String(JpegMinimo))]);

        int novedadId;
        await using (var db = api.NuevoDbContext())
            novedadId = (await db.Novedades.AsNoTracking()
                .SingleAsync(n => n.Tipo == TipoNovedad.SignosClinicos)).Id;

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync($"/api/recepcion/novedades/{novedadId}/foto");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");
        (await respuesta.Content.ReadAsByteArrayAsync()).ShouldBe(JpegMinimo);
    }

    [Fact]
    public async Task ElOperadorDeCatSigueSinVerLaFotoDeOtroCentro()
    {
        // Ampliar los roles no puede aflojar el filtro por centro de acopio.
        await RegistrarEntregaAsync(CentroAcopio.PAT,
            [Cuy(1300m, "lesion", Convert.ToBase64String(JpegMinimo))]);

        int novedadId;
        await using (var db = api.NuevoDbContext())
            novedadId = (await db.Novedades.AsNoTracking()
                .SingleAsync(n => n.Tipo == TipoNovedad.SignosClinicos)).Id;

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/recepcion/novedades/{novedadId}/foto");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Proyecciones mínimas: solo lo que estas pruebas afirman.
    private record LoteParcial(string CodigoLote, List<CuyParcial> CuyesDisponibles);
    private record CuyParcial(int NumeroEnLote, int? NovedadFotoId);
}

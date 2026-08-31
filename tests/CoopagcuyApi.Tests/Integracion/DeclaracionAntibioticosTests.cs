using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La pregunta por los días de retiro se sustituyó por una declaración
/// explícita de que los cuyes no recibieron antibióticos en los últimos 7
/// días. Es obligatoria: sin ella la guía de movilización no tendría
/// respaldo sanitario de nadie.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class DeclaracionAntibioticosTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";
    private const string CodigoLote = "PAT-20260819-001";

    private async Task SembrarLoteAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, "PAT");

        await using var db = api.NuevoDbContext();
        db.Lotes.Add(new Lote
        {
            CodigoLote = CodigoLote,
            ProductoraId = productora.Id,
            CentroAcopio = "PAT",
            CantidadAnimales = 5,
            PesoTotalGramos = 5 * 1300m,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            Cerrado = true,
            ResponsableRecepcion = "Operadora de prueba"
        });
        await db.SaveChangesAsync();
    }

    private static object Movilizacion(bool? declaracion) => new
    {
        conductor = "Juan Pérez",
        cantidadMovilizada = 5,
        condicionesTransporte = Array.Empty<string>(),
        tipoForraje = "Concentrado sin proteína animal",
        sinAntibioticos7Dias = declaracion,
        responsableDespacho = "Responsable de prueba"
    };

    [Fact]
    public async Task SinLaDeclaracionSeRechazaCon400()
    {
        await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: null));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        cuerpo!.Mensaje.ShouldContain("antibióticos");
    }

    [Fact]
    public async Task DeclararFalsoTambienSeRechaza()
    {
        await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: false));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConLaDeclaracionSeRegistraYSeGuarda()
    {
        await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: true));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var db = api.NuevoDbContext();
        var movilizacion = await db.Movilizaciones.AsNoTracking().SingleAsync();

        movilizacion.SinAntibioticos7Dias.ShouldBe(true);
        movilizacion.TipoForraje.ShouldBe("Concentrado sin proteína animal");
        movilizacion.DiasRetiroMedicamentos.ShouldBeNull();
    }

    [Fact]
    public async Task LaGuiaSeGeneraParaUnaMovilizacionDeclarada()
    {
        await SembrarLoteAsync();

        await api.ComoOperadorCat("PAT").PostAsJsonAsync(
            $"/api/recepcion/lotes/{CodigoLote}/movilizacion",
            Movilizacion(declaracion: true));

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{CodigoLote}/guia");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");
    }

    private record RespuestaError(string Mensaje);
}

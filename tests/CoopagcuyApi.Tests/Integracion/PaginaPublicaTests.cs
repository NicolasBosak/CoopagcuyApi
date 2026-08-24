using System.Net;
using System.Text.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La página que abre el consumidor al escanear el QR es anónima: cualquiera
/// con el código ve su contenido. El detalle animal por animal y las
/// observaciones del proceso salieron de ahí — no aportan al consumidor y son
/// datos de producción sobre una comunidad identificable.
///
/// Esto sí es comprobable de verdad: es JSON, no un PDF.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class PaginaPublicaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";
    private const string CodigoFaenado = "FAE-20260818-001";

    [Fact]
    public async Task NoExponeElDetalleAnimalPorAnimal()
    {
        await SembrarPaginaAsync();

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{CodigoFaenado}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Aserción sobre el cuerpo y no sobre el tipo de C#: lo que importa es
        // lo que sale por el cable.
        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("detalleCuyes", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("observacionesProceso", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task SigueDiciendoSiElLoteTuvoNovedad()
    {
        // conNovedad se calcula A PARTIR de detalleCuyes. Borrar la variable
        // al quitar el campo del DTO rompería el indicador sin que se note al
        // leer el diff: este lote lleva un animal con novedad a propósito.
        await SembrarPaginaAsync();

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{CodigoFaenado}");

        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("estadoCalidad").GetString()
            .ShouldBe("ConNovedad");
        doc.RootElement.GetProperty("estadoCanal").GetString()
            .ShouldBe("ConNovedad");
    }

    /// Lote faenado con QR activo y dos animales, uno de ellos con novedad.
    private async Task SembrarPaginaAsync()
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
            PesoTotalGramos = 2600,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Operadora de prueba"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var loteFaenado = new LoteFaenado
        {
            Codigo = CodigoFaenado,
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
            EstadoCanal = EstadoCanal.ConNovedad
        };
        db.Faenamientos.Add(sesion);
        await db.SaveChangesAsync();

        db.CuyFaenamientos.AddRange(
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 1,
                PesoCanalGramos = 600, Estado = EstadoCanal.Apto
            },
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 2,
                PesoCanalGramos = 600, Estado = EstadoCanal.ConNovedad,
                Motivo = "hematoma en el costado"
            });

        // Sin un QR activo, ObtenerPaginaPublicaAsync devuelve null y el
        // endpoint responde 404.
        db.CodigosQR.Add(new CodigoQR
        {
            LoteFaenadoId = loteFaenado.Id,
            UrlPublica = $"https://localhost/qr/{CodigoFaenado}",
            Activo = true
        });

        await db.SaveChangesAsync();
    }
}

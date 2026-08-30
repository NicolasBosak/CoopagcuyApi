using System.Net;
using System.Text.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
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

    // "Azuay" estuvo escrita a mano en cuatro sitios de QRService. Con una
    // sola provincia nunca se notó; en cuanto entre otra, el QR le mentiría
    // al consumidor sobre de dónde viene el cuy que tiene en la mano.
    [Fact]
    public async Task LaFichaPublica_diceLaProvinciaDeLaComunidad_noUnaFija()
    {
        var comunidadId = await ComunidadLojanaAsync();

        await SembrarPaginaAsync(comunidadId);

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{CodigoFaenado}");
        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("provincia").GetString().ShouldBe("Loja");

        var parametros = doc.RootElement.GetProperty("parametrosAprobados")
            .EnumerateArray().Select(p => p.GetString()!).ToList();
        parametros.ShouldContain(p => p.Contains("Loja, Ecuador"));
    }

    /// Comunidad de Loja que entrega en un CAT de Azuay. Es el caso que
    /// obliga a derivar la provincia de la COMUNIDAD y no del centro: una
    /// comunidad entrega donde le queda más cerca, aunque sea otra provincia.
    private async Task<int> ComunidadLojanaAsync()
    {
        await using var db = api.NuevoDbContext();

        // Respawn no trunca Comunidades entre pruebas: si esta prueba corre
        // dos veces en la misma base, la segunda choca con el índice único
        // (CantonId, Nombre). Se devuelve la existente en vez de duplicar.
        var existente = await db.Comunidades
            .FirstOrDefaultAsync(c => c.CantonId == 108 && c.Nombre == "Comunidad Lojana");
        if (existente is not null) return existente.Id;

        // Cantón 108 = Loja (Loja), el primero de la provincia 12 en
        // GeografiaEcuador: las once anteriores suman 107 cantones.
        var comunidad = new Comunidad
        {
            Nombre = "Comunidad Lojana",
            CantonId = 108,
            CatReferencia = "PAT",
        };

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();
        return comunidad.Id;
    }

    /// Lote faenado con QR activo y dos animales, uno de ellos con novedad.
    /// La comunidad es parámetro desde 2026-08: la ficha pública dice de qué
    /// provincia viene el cuy, y eso solo se puede verificar con una
    /// comunidad que no sea de Azuay.
    private async Task SembrarPaginaAsync(int comunidadId = 1)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, "PAT", comunidadId: comunidadId);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-20260818-001",
            ProductoraId = productora.Id,
            CentroAcopio = "PAT",
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

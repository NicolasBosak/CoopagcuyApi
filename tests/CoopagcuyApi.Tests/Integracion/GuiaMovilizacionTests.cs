using System.Net;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La guía de movilización imprimía el nombre de la CLASE de la comunidad
/// —"CoopagcuyApi.Features.Catalogos.Models.Comunidad"— porque interpolaba el
/// objeto de navegación en vez de su propiedad Nombre.
///
/// El PDF es binario y el proyecto no tiene extractor de texto, así que estas
/// pruebas cubren dos cosas distintas: que el documento se genera de punta a
/// punta, y que la consulta carga la comunidad de cada cuy —que es la
/// condición sin la cual la celda no puede componerse bien.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class GuiaMovilizacionTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    /// <summary>
    /// Jaula mínima con un animal: lo justo para que la guía tenga que
    /// componer la fila de "DETALLE POR ANIMAL", que es donde estaba el fallo.
    /// </summary>
    private async Task<string> SembrarLoteAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT, comunidadId: 1);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-20260818-001",
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 1,
            PesoTotalGramos = 900,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Nicolas Nieves"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        db.CuyRegistros.Add(new CuyRegistro
        {
            LoteId = lote.Id,
            ProductoraId = productora.Id,
            NumeroEnLote = 1,
            PesoGramos = 900,
            ColorPelaje = "Bayo",
            EstadoOreja = "Semiblanda",
            TamanoAnimal = "Grande",
            Estado = EstadoLote.Aceptado
        });
        await db.SaveChangesAsync();

        return lote.CodigoLote;
    }

    [Fact]
    public async Task LaGuiaSeGeneraParaUnLoteConDetallePorAnimal()
    {
        var codigo = await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigo}/guia");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public void ElLogoEstaEmbebidoEnElEnsamblado()
    {
        // Si el recurso no se declara en el .csproj, esto falla aquí y no
        // dentro de la generación de un PDF en producción.
        var logo = CoopagcuyApi.Common.Branding.BrandingAssets.Logo;

        logo.ShouldNotBeNull();
        logo.Length.ShouldBeGreaterThan(0);
        // Firma PNG: descarta que se haya embebido cualquier otro archivo.
        logo[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    [Fact]
    public async Task LaGuiaConLogoPesaMasQueElUmbralMinimo()
    {
        // Comprobación indirecta pero real: sin extractor de PDF no se puede
        // afirmar "hay una imagen", pero un PDF con el logo incrustado pesa
        // claramente más que uno de solo texto.
        var codigo = await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigo}/guia");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public async Task LaConsultaDeLaGuiaCargaLaComunidadDeCadaCuy()
    {
        // Es la condición previa a que la celda se componga bien: si la
        // comunidad llegara nula, .Nombre reventaría al generar el PDF.
        var codigo = await SembrarLoteAsync();

        await using var db = api.NuevoDbContext();
        var lote = await db.Lotes
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
                .ThenInclude(p => p!.Comunidad)
            .AsNoTracking()
            .FirstAsync(l => l.CodigoLote == codigo);

        var cuy = lote.Cuyes.Single();
        cuy.Productora.ShouldNotBeNull();
        cuy.Productora.Comunidad.ShouldNotBeNull();
        cuy.Productora.Comunidad.Nombre.ShouldBe("Patococha");
    }
}

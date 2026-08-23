using System.Net;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Recepcion.Services;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

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
public class GuiaMovilizacionTests(ApiFactory api, ITestOutputHelper output) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    /// <summary>
    /// Jaula mínima con un animal: lo justo para que la guía tenga que
    /// componer la fila de "DETALLE POR ANIMAL", que es donde estaba el fallo.
    ///
    /// Acepta un código y una cédula opcionales para poder sembrar lotes
    /// "gemelos" —mismo contenido, distinta productora y código— cuando una
    /// prueba necesita comparar dos guías entre sí: dos lotes con la misma
    /// cédula chocarían contra el índice único de Productoras.
    /// </summary>
    private async Task<string> SembrarLoteAsync(
        string codigo = "PAT-20260818-001", string cedula = CedulaProductora)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT, comunidadId: 1);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = codigo,
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

    /// <summary>
    /// Movilización insertada directamente en la base, sin pasar por el
    /// endpoint: lo único que estas pruebas necesitan es un registro con
    /// CondicionesClaves fijado a lo que se quiere comprobar en la guía.
    /// </summary>
    private async Task MovilizarAsync(string codigoLote, string? condicionesClaves)
    {
        await using var db = api.NuevoDbContext();
        var lote = await db.Lotes.FirstAsync(l => l.CodigoLote == codigoLote);

        db.Movilizaciones.Add(new Movilizacion
        {
            LoteId = lote.Id,
            FechaDespacho = DateTime.UtcNow,
            Conductor = "Conductor de prueba",
            CantidadMovilizada = 1,
            CondicionesTransporte = condicionesClaves is null
                ? null
                : CondicionTransporte.Describir(TextosGuia.ClavesDe(condicionesClaves)),
            CondicionesClaves = condicionesClaves,
            SinAntibioticos7Dias = true,
            ResponsableDespacho = "Responsable de prueba"
        });
        await db.SaveChangesAsync();
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

    [Fact]
    public async Task LaGuiaCreceCuandoFaltanCondicionesPorVerificar()
    {
        // Un lote con una sola condición marcada debe imprimir el bloque de
        // "No se verificó" con las otras seis, así que su guía tiene que
        // pesar sensiblemente más que la de un lote con las siete marcadas
        // (que no imprime nada nuevo).
        var codigoCompleto = await SembrarLoteAsync("PAT-20260818-001", "0104576277");
        await MovilizarAsync(codigoCompleto,
            string.Join(CondicionTransporte.Separador, CondicionTransporte.Catalogo.Keys));

        var codigoIncompleto = await SembrarLoteAsync("PAT-20260818-002", "0102030405");
        await MovilizarAsync(codigoIncompleto, "JaulasLimpias");

        var respuestaCompleto = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigoCompleto}/guia");
        var respuestaIncompleto = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigoIncompleto}/guia");

        var bytesCompleto = await respuestaCompleto.Content.ReadAsByteArrayAsync();
        var bytesIncompleto = await respuestaIncompleto.Content.ReadAsByteArrayAsync();

        output.WriteLine($"Completo: {bytesCompleto.Length} bytes");
        output.WriteLine($"Incompleto: {bytesIncompleto.Length} bytes");
        output.WriteLine($"Diferencia: {bytesIncompleto.Length - bytesCompleto.Length} bytes");

        // Umbral medido: con 6 de 7 condiciones sin marcar, el bloque nuevo
        // agrega entre 100 y 103 bytes al PDF (tres corridas). El ruido por
        // subconjunto de fuente entre dos lotes gemelos —ver la prueba
        // siguiente— midió entre 53 y 59 bytes. 70 queda cómodo por encima
        // del ruido y por debajo del crecimiento real.
        bytesIncompleto.Length.ShouldBeGreaterThan(bytesCompleto.Length + 70);
    }

    [Fact]
    public async Task LaGuiaDeUnLoteConElChecklistCompletoNoCrece()
    {
        // Con las siete condiciones marcadas no hay nada que imprimir de
        // más, así que la guía tiene que salir igual que antes de esta
        // feature. Como eso no se puede comparar contra el pasado, la forma
        // honesta es sembrar dos lotes gemelos —mismo contenido, distinto
        // código— y comprobar que sus PDF miden prácticamente lo mismo.
        var codigoA = await SembrarLoteAsync("PAT-20260818-003", "0104576277");
        await MovilizarAsync(codigoA,
            string.Join(CondicionTransporte.Separador, CondicionTransporte.Catalogo.Keys));

        var codigoB = await SembrarLoteAsync("PAT-20260818-004", "0102030405");
        await MovilizarAsync(codigoB,
            string.Join(CondicionTransporte.Separador, CondicionTransporte.Catalogo.Keys));

        var respuestaA = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigoA}/guia");
        var respuestaB = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigoB}/guia");

        var bytesA = await respuestaA.Content.ReadAsByteArrayAsync();
        var bytesB = await respuestaB.Content.ReadAsByteArrayAsync();

        output.WriteLine($"Gemelo A: {bytesA.Length} bytes");
        output.WriteLine($"Gemelo B: {bytesB.Length} bytes");
        output.WriteLine($"Diferencia: {Math.Abs(bytesA.Length - bytesB.Length)} bytes");

        // Umbral medido: con el mismo largo de código y cédula entre los dos
        // gemelos, el único diferencial es el subconjunto de fuente que
        // QuestPDF arma para los dígitos —cédula y código de lote—, que
        // midió entre 53 y 59 bytes en tres corridas. 80 deja margen sin
        // acercarse a los ~100 bytes que sí representan una línea nueva
        // (ver la prueba de crecimiento).
        Math.Abs(bytesA.Length - bytesB.Length).ShouldBeLessThan(80);
    }
}

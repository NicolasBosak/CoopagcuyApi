using System.Net;
using System.Net.Http.Json;
using System.Text;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Recepcion.Services;
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
///
/// LÍMITE MEDIDO — por qué esta clase no compara PDF por longitud en bytes:
/// el contenido no se puede afirmar desde código porque QuestPDF comprime
/// los flujos de texto del documento, así que no hay forma razonable de leer
/// "qué dice" la guía desde una prueba. Se intentó rodear ese límite
/// comparando el TAMAÑO de dos PDF en vez de su contenido —un lote con el
/// checklist completo contra uno incompleto, o dos "gemelos" entre sí— pero
/// esa técnica tampoco sirve a esta escala: el subconjunto de fuentes que
/// QuestPDF/SkiaSharp embebe en cada documento introduce hasta ~238 bytes de
/// variación entre dos PDF equivalentes generados por invocaciones de
/// proceso separadas (medido en este mismo entorno Docker, con tres de
/// cuatro corridas aisladas cayendo por ese ruido). El bloque de "No se
/// verificó" que esta feature agrega pesa ~100 bytes — la señal queda por
/// debajo del ruido, así que ninguna prueba de longitud puede distinguir un
/// cambio real de una variación del subconjunto de fuentes. (En proyectos
/// anteriores de este repo la comparación por bytes sí funcionó: el bloque
/// medido entonces pesaba ~2600 bytes y aplastaba un ruido del mismo orden.
/// Aquí la señal es demasiado chica para el mismo método.)
///
/// Por eso el contenido impreso de la guía —incluido el bloque de "No se
/// verificó"— se verifica a mano, y las pruebas de esta clase se limitan a
/// confirmar que el documento se genera correctamente (200, tipo
/// application/pdf, cabecera %PDF, tamaño no trivial) sin afirmar nada sobre
/// su contenido. La garantía de no regresión del checklist completo la
/// sostienen, por construcción, las pruebas unitarias de
/// TextosGuia.LineaNoVerificadas (CondicionesNoVerificadasTests): son
/// deterministas porque no pasan por QuestPDF.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class GuiaMovilizacionTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    /// <summary>
    /// Jaula con <paramref name="cantidadAnimales"/> animales (uno por
    /// defecto), de una productora nueva: lo justo para que la guía tenga que
    /// componer la fila de "DETALLE POR ANIMAL", que es donde estaba el
    /// fallo.
    /// </summary>
    private async Task<string> SembrarLoteAsync(
        int cantidadAnimales = 1, string sufijo = "001")
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT, comunidadId: 1);

        return await SembrarLoteParaProductoraAsync(
            productora.Id, cantidadAnimales, sufijo);
    }

    /// <summary>
    /// Igual que <see cref="SembrarLoteAsync"/> pero para una productora ya
    /// existente: permite sembrar más de un lote en la misma prueba —cada uno
    /// con su propio <paramref name="sufijo"/> de código— sin chocar contra
    /// la cédula única de la productora.
    /// </summary>
    private async Task<string> SembrarLoteParaProductoraAsync(
        int productoraId, int cantidadAnimales, string sufijo)
    {
        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = $"PAT-20260818-{sufijo}",
            ProductoraId = productoraId,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = cantidadAnimales,
            PesoTotalGramos = 900 * cantidadAnimales,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Nicolas Nieves"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        for (var numero = 1; numero <= cantidadAnimales; numero++)
        {
            db.CuyRegistros.Add(new CuyRegistro
            {
                LoteId = lote.Id,
                ProductoraId = productoraId,
                NumeroEnLote = numero,
                PesoGramos = 900,
                ColorPelaje = "Bayo",
                EstadoOreja = "Semiblanda",
                TamanoAnimal = "Grande",
                Estado = EstadoLote.Aceptado
            });
        }
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
    public async Task LaGuiaListaLosAnimalesVendidosEnLaComunidad()
    {
        // Misma técnica que LaGuiaConLogoPesaMasQueElUmbralMinimo: del PDF no
        // se puede afirmar nada sobre su contenido, pero de que el bloque de
        // "VENDIDOS EN LA COMUNIDAD" llegó (encabezado + un renglón por
        // animal) sí se puede afirmar que el documento pesa más.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT, comunidadId: 1);

        var codigoSinVentas = await SembrarLoteParaProductoraAsync(
            productora.Id, cantidadAnimales: 3, sufijo: "001");
        var codigoConVentas = await SembrarLoteParaProductoraAsync(
            productora.Id, cantidadAnimales: 3, sufijo: "002");

        int loteId;
        int[] cuyIds;
        await using (var db = api.NuevoDbContext())
        {
            var lote = await db.Lotes.SingleAsync(l => l.CodigoLote == codigoConVentas);
            loteId = lote.Id;
            cuyIds = await db.CuyRegistros
                .Where(c => c.LoteId == loteId)
                .OrderBy(c => c.NumeroEnLote)
                .Select(c => c.Id)
                .ToArrayAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = cuyIds.Take(2).ToArray(),
                montoUsd = 20m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        var respuestaSinVentas = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigoSinVentas}/guia");
        var respuestaConVentas = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigoConVentas}/guia");

        var bytesSinVentas = await respuestaSinVentas.Content.ReadAsByteArrayAsync();
        var bytesConVentas = await respuestaConVentas.Content.ReadAsByteArrayAsync();

        // Medido empíricamente: con 2 de 3 animales vendidos el PDF creció
        // 474 bytes frente al equivalente sin ventas (encabezado "VENDIDOS EN
        // LA COMUNIDAD" + resumen + dos renglones). Umbral holgadamente por
        // debajo de ese valor medido.
        (bytesConVentas.Length - bytesSinVentas.Length).ShouldBeGreaterThan(200);
    }

    [Fact]
    public async Task LaGuiaDeUnLoteConChecklistIncompletoSeGeneraSinReventar()
    {
        // No se puede afirmar el CONTENIDO del bloque de "No se verificó"
        // desde aquí (ver el límite documentado en la clase), pero sí que el
        // camino de renderizado que lo pinta no revienta: un Include mal
        // puesto, un valor nulo sin manejar en TextosGuia, o un contenedor
        // QuestPDF equivocado alrededor del bloque nuevo sí lo harían, y esta
        // prueba los atraparía con un 500 o un tipo de contenido incorrecto.
        var codigo = await SembrarLoteAsync();
        await MovilizarAsync(codigo, "JaulasLimpias");

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigo}/guia");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("%PDF");
    }
}

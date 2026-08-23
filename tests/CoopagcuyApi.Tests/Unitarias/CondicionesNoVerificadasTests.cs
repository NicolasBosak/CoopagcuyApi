using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Recepcion.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El operador del CAT marca un checklist antes de enviar la jaula, y hasta
/// ahora lo que dejaba sin marcar no se reflejaba en ningún sitio: la guía
/// imprimía lo verificado y lo que faltó desaparecía.
///
/// Funciones puras porque del PDF no se puede afirmar nada: QuestPDF comprime
/// los flujos de texto del documento.
/// </summary>
public class CondicionesNoVerificadasTests
{
    [Fact]
    public void NoVerificadas_devuelveLasQueFaltan_enOrdenDelCatalogo()
    {
        var marcadas = new[] { "JaulasLimpias", "Ventilacion" };

        var faltan = CondicionTransporte.NoVerificadas(marcadas);

        faltan.Count.ShouldBe(CondicionTransporte.Catalogo.Count - 2);
        faltan.ShouldContain("Vehículo limpio");
        faltan.ShouldNotContain("Ventilación adecuada");
    }

    [Fact]
    public void NoVerificadas_conTodasMarcadas_devuelveVacio()
    {
        var todas = CondicionTransporte.Catalogo.Keys.ToArray();

        CondicionTransporte.NoVerificadas(todas).ShouldBeEmpty();
    }

    [Fact]
    public void NoVerificadas_ignoraUnaClaveDesconocida()
    {
        // Una movilización guardada con una clave que después se retiró del
        // catálogo no puede hacer aparecer una condición inventada.
        var faltan = CondicionTransporte.NoVerificadas(new[] { "ClaveQueYaNoExiste" });

        faltan.Count.ShouldBe(CondicionTransporte.Catalogo.Count);
    }

    [Fact]
    public void ClavesDe_partePorElSeparador()
    {
        TextosGuia.ClavesDe("JaulasLimpias;Ventilacion")
            .ShouldBe(new[] { "JaulasLimpias", "Ventilacion" });
    }

    [Fact]
    public void ClavesDe_conNuloDevuelveVacio()
    {
        TextosGuia.ClavesDe(null).ShouldBeEmpty();
    }

    [Fact]
    public void LineaNoVerificadas_nombraLasQueFaltan()
    {
        var linea = TextosGuia.LineaNoVerificadas("JaulasLimpias;Ventilacion");

        linea.ShouldNotBeNull();
        linea.ShouldContain("Vehículo limpio");
    }

    [Fact]
    public void LineaNoVerificadas_conTodasMarcadas_devuelveNulo()
    {
        // Nada que imprimir: la guía de un lote con el checklist completo
        // debe salir idéntica a las de antes de esta feature.
        var todas = string.Join(CondicionTransporte.Separador,
            CondicionTransporte.Catalogo.Keys);

        TextosGuia.LineaNoVerificadas(todas).ShouldBeNull();
    }

    [Fact]
    public void LineaNoVerificadas_sinRegistro_noAfirmaQueNoSeVerificoNada()
    {
        // Una movilización anterior a esta feature no tiene claves guardadas.
        // "No se registró" NO es lo mismo que "no se verificó ninguna", y la
        // guía no puede decir lo segundo cuando lo cierto es lo primero.
        TextosGuia.LineaNoVerificadas(null).ShouldBeNull();
    }
}

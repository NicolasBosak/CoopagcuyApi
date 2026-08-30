using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Recepcion.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Los dos defectos reportados sobre la guía de movilización, fijados donde sí
/// se pueden comprobar: el PDF comprime su texto y no hay forma razonable de
/// afirmar nada sobre el binario.
/// </summary>
public class TextosGuiaTests
{
    private static CuyRegistro Cuy(
        Productora? productora = null,
        string pelaje = "Blanco",
        string oreja = "Blanda",
        string tamano = "Normal") => new()
        {
            NumeroEnLote = 1,
            PesoGramos = 900,
            ColorPelaje = pelaje,
            EstadoOreja = oreja,
            TamanoAnimal = tamano,
            Estado = EstadoLote.Aceptado,
            Productora = productora,
            ProductoraId = productora?.Id
        };

    private static Productora ProductoraEn(string comunidad) => new()
    {
        Id = 7,
        NombreCompleto = "Nicolas Productor",
        Cedula = "0104576277",
        ComunidadId = 1,
        CatAsignado = CentroAcopio.PAT,
        Comunidad = new Comunidad
        {
            Id = 1,
            Nombre = comunidad,
            CatReferencia = CentroAcopio.PAT
        }
    };

    [Fact]
    public void Productora_poneElNombreDeLaComunidad_noElDeSuClase()
    {
        // El defecto exacto que se reportó: el paréntesis mostraba
        // "CoopagcuyApi.Features.Catalogos.Models.Comunidad".
        var texto = TextosGuia.Productora(Cuy(ProductoraEn("Patococha")));

        texto.ShouldBe("Nicolas Productor (Patococha)");
        texto.ShouldNotContain("CoopagcuyApi");
        texto.ShouldNotContain("Models.Comunidad");
    }

    [Fact]
    public void Productora_sinProductoraAsociada_devuelveUnGuion()
    {
        TextosGuia.Productora(Cuy()).ShouldBe("—");
    }

    [Fact]
    public void Productora_sinComunidadCargada_noImprimeUnParentesisVacio()
    {
        // Si algún día se olvida el Include, mejor el nombre a secas que
        // "Nicolas Productor ()" o una excepción en mitad del PDF.
        var productora = ProductoraEn("Patococha");
        productora.Comunidad = null!;

        TextosGuia.Productora(Cuy(productora)).ShouldBe("Nicolas Productor");
    }

    [Fact]
    public void Caracteristicas_rotulaCadaDato()
    {
        // Sin rótulo, "Blanco · Blanda · Normal" se leía como el catálogo de
        // opciones y no como los datos de este animal.
        var texto = TextosGuia.Caracteristicas(
            Cuy(pelaje: "Bayo", oreja: "Semiblanda", tamano: "Grande"));

        texto.ShouldBe("Pelaje: Bayo · Oreja: Semiblanda · Tamaño: Grande");
    }

    [Fact]
    public void Caracteristicas_omiteLosCamposVacios()
    {
        var texto = TextosGuia.Caracteristicas(
            Cuy(pelaje: "Bayo", oreja: "", tamano: "Grande"));

        texto.ShouldBe("Pelaje: Bayo · Tamaño: Grande");
    }

    [Fact]
    public void LaGuiaDeclaraLaAusenciaDeAntibioticosConElResponsable()
    {
        var movilizacion = new Movilizacion
        {
            ResponsableDespacho = "Nicolas Nieves",
            SinAntibioticos7Dias = true
        };

        var texto = GuiaMovilizacionService.TextoDeclaracionSanitaria(movilizacion);

        texto.ShouldContain("Sin antibióticos últimos 7 días");
        texto.ShouldContain("Nicolas Nieves");
    }

    [Fact]
    public void UnaMovilizacionHeredadaConservaLaLineaDeDiasDeRetiro()
    {
        // Reimprimir una guía anterior al cambio no puede perder el dato que
        // sí se capturó entonces.
        var movilizacion = new Movilizacion
        {
            ResponsableDespacho = "Nicolas Nieves",
            SinAntibioticos7Dias = null,
            DiasRetiroMedicamentos = 12
        };

        var texto = GuiaMovilizacionService.TextoDeclaracionSanitaria(movilizacion);

        texto.ShouldContain("12 días");
    }

    [Fact]
    public void UnaMovilizacionHeredadaSinDatoDiceSinDeclaracion()
    {
        var movilizacion = new Movilizacion
        {
            ResponsableDespacho = "Nicolas Nieves",
            SinAntibioticos7Dias = null,
            DiasRetiroMedicamentos = null
        };

        var texto = GuiaMovilizacionService.TextoDeclaracionSanitaria(movilizacion);

        texto.ShouldContain("sin declaración");
    }

    [Fact]
    public void LineaVentaLocal_nombraAlAnimalYaSuProductora()
    {
        var cuy = new CuyRegistro
        {
            NumeroEnLote = 3,
            Productora = new Productora
            {
                NombreCompleto = "María Quizhpi",
                Comunidad = new Comunidad { Nombre = "Patococha" }
            }
        };
        var fecha = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);

        // La fecha va en hora local del piloto, como el resto del documento.
        TextosGuia.LineaVentaLocal(cuy, fecha)
            .ShouldBe("#3 · María Quizhpi (Patococha) · 21/08/2026");
    }

    [Fact]
    public void LineaVentaLocal_sinProductoraNoRevienta()
    {
        var cuy = new CuyRegistro { NumeroEnLote = 7, Productora = null };
        var fecha = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        TextosGuia.LineaVentaLocal(cuy, fecha).ShouldBe("#7 · — · 21/08/2026");
    }
}

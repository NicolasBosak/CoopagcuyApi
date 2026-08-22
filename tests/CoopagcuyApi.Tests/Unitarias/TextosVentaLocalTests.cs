using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Pagos.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El ticket de una venta local tiene que decir que lo es. La productora se
/// lleva ese papel y es el único canal por el que sabe bajo qué condiciones se
/// le pagó — sobre todo si fue a cuotas, donde el dinero todavía no llegó.
///
/// Funciones puras porque del PDF no se puede afirmar nada: QuestPDF comprime
/// los flujos de texto del documento.
/// </summary>
public class TextosVentaLocalTests
{
    private static Pago Local(string metodo, int? dias = null, decimal? valor = null) =>
        new()
        {
            EsVentaLocal = true,
            Estado = EstadoPago.Recibido,
            MontoUsd = 75m,
            MontoPagadoUsd = 75m,
            MetodoPago = metodo,
            NumeroDias = dias,
            ValorPorDia = valor
        };

    [Fact]
    public void ElEncabezadoDistingueLaVentaLocal()
    {
        TextosVentaLocal.Encabezado(Local("Efectivo")).ShouldBe("VENTA LOCAL");
    }

    [Fact]
    public void UnPagoDeLaPlantaConservaSuEncabezado()
    {
        // Garantía de no regresión: el ticket del ciclo con la planta no
        // cambia ni una letra.
        var dePlanta = new Pago { EsVentaLocal = false, MontoUsd = 120m };

        TextosVentaLocal.Encabezado(dePlanta).ShouldBe("COMPROBANTE DE PAGO");
    }

    [Fact]
    public void UnaVentaEnEfectivoDiceQueYaSeCobro()
    {
        TextosVentaLocal.TextoEstado(Local("Efectivo"))
            .ShouldBe("VENDIDO EN LA COMUNIDAD — COBRADO");
    }

    [Fact]
    public void UnaVentaACuotasNoDiceQueYaSeCobro()
    {
        // El dinero no ha llegado: el papel no puede afirmar lo contrario,
        // aunque el estado interno del pago sea Recibido.
        var texto = TextosVentaLocal.TextoEstado(Local("Cuotas", 30, 2.5m));

        texto.ShouldBe("VENDIDO EN LA COMUNIDAD — A CUOTAS");
        texto.ShouldNotContain("COBRADO");
    }

    [Fact]
    public void LaLineaDeMetodoLlevaElAcuerdoDeCuotas()
    {
        TextosVentaLocal.LineaMetodo(Local("Cuotas", 30, 2.5m))
            .ShouldBe("A cuotas: 30 días × USD 2,50");
    }

    [Fact]
    public void LaLineaDeMetodoSinCuotasEsSoloElMetodo()
    {
        TextosVentaLocal.LineaMetodo(Local("Efectivo")).ShouldBe("Efectivo");
        TextosVentaLocal.LineaMetodo(Local("Transferencia")).ShouldBe("Transferencia");
    }

    [Fact]
    public void ElAcuerdoNoDependeDeLaCulturaDeLaMaquina()
    {
        // Mismo motivo que las fechas: el separador decimal cambia con la
        // cultura activa del contenedor.
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            TextosVentaLocal.LineaMetodo(Local("Cuotas", 30, 2.5m))
                .ShouldBe("A cuotas: 30 días × USD 2,50");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = anterior;
        }
    }
}

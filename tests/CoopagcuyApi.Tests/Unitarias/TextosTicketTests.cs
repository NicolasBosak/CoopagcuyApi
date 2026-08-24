using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Pagos.Services;
using CoopagcuyApi.Features.Recepcion.Models;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El ticket imprimía siempre el monto original y no mencionaba los
/// descuentos: una productora con un ticket reimpreso leía "USD 120,00" y
/// "PAGADO" cuando lo que le llegaron fueron 103. No tiene cuenta en el
/// sistema — el papel es el único canal por el que puede enterarse.
/// </summary>
public class TextosTicketTests
{
    private static DescuentoPago Descuento(
        TipoNovedad tipo, int? numeroEnLote, decimal monto = 8m) =>
        new()
        {
            MontoUsd = monto,
            Descripcion = "motivo de prueba",
            NovedadCat = new Novedad
            {
                Tipo = tipo,
                CuyRegistro = numeroEnLote is int n
                    ? new CuyRegistro { NumeroEnLote = n }
                    : null
            }
        };

    [Fact]
    public void LineaNovedad_nombraElAnimalYElTipo()
    {
        TextosTicket.LineaNovedad(Descuento(TipoNovedad.OrejaDura, 3))
            .ShouldBe("Cuy #3 · Oreja dura");
    }

    [Fact]
    public void LineaNovedad_sinAnimalNoInventaUnNumero()
    {
        // PagoService rechaza con 409 los descuentos cuya novedad no cuelga de
        // un animal, así que por la vía de escritura actual esto no llega
        // aquí. Se contempla igual: el modelo lo admite, y una función que
        // revienta con un nulo legal convierte el ticket en un error 500.
        TextosTicket.LineaNovedad(Descuento(TipoNovedad.SinAyuno, null))
            .ShouldBe("Sin ayuno");
    }

    [Fact]
    public void EtiquetaTipo_cubreTodoElEnum()
    {
        // Sin esto, añadir un TipoNovedad nuevo dejaría el rótulo por defecto
        // impreso en un ticket sin que nada avisara.
        foreach (var tipo in Enum.GetValues<TipoNovedad>())
            TextosTicket.EtiquetaTipo(tipo).ShouldNotBe("Novedad");
    }

    [Fact]
    public void EtiquetaTipo_noImprimeElNombreDelEnum()
    {
        // "BajoPeso" no es algo que se le entregue impreso a una productora.
        TextosTicket.EtiquetaTipo(TipoNovedad.BajoPeso).ShouldBe("Bajo peso");
        TextosTicket.EtiquetaTipo(TipoNovedad.SignosClinicos)
            .ShouldBe("Signos clínicos");
    }

    [Fact]
    public void MontoDestacado_pendiente_esElDelTicket()
    {
        var pago = new Pago { MontoUsd = 120m, MontoPagadoUsd = null };

        TextosTicket.MontoDestacado(pago).ShouldBe(120m);
    }

    [Fact]
    public void MontoDestacado_pagado_esLoQueLaProductoraCobro()
    {
        // El fallo entero de esta feature en una línea: imprimir 120 en un
        // ticket donde llegaron 103.
        var pago = new Pago { MontoUsd = 120m, MontoPagadoUsd = 103m };

        TextosTicket.MontoDestacado(pago).ShouldBe(103m);
    }

    [Fact]
    public void HayDesglose_sinDescuentosEsFalso()
    {
        // Un ticket pendiente debe salir EXACTAMENTE igual que antes de esta
        // feature.
        TextosTicket.HayDesglose(new Pago()).ShouldBeFalse();
    }

    [Fact]
    public void HayDesglose_conDescuentosEsVerdadero()
    {
        var pago = new Pago();
        pago.Descuentos.Add(Descuento(TipoNovedad.BajoPeso, 1));

        TextosTicket.HayDesglose(pago).ShouldBeTrue();
    }
}

using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;

namespace CoopagcuyApi.Features.Pagos.Services;

/// <summary>
/// Composición de las líneas del ticket cuyo contenido depende de una regla.
///
/// Vive fuera del armado del PDF por el mismo motivo que TextosGuia: QuestPDF
/// comprime los flujos de texto del documento, así que del binario no hay
/// forma razonable de afirmar nada. Como funciones puras sí se comprueban.
/// </summary>
public static class TextosTicket
{
    /// <summary>
    /// Etiqueta legible del tipo de novedad.
    ///
    /// El API no tenía ninguna: el único mapa que existía vivía en el front
    /// (AnilloNovedades.tsx). El nombre del enum tal cual —"BajoPeso"— no es
    /// algo que se le entregue impreso a una productora.
    /// </summary>
    public static string EtiquetaTipo(TipoNovedad tipo) => tipo switch
    {
        TipoNovedad.BajoPeso => "Bajo peso",
        TipoNovedad.OrejaDura => "Oreja dura",
        TipoNovedad.ColorNoConforme => "Color no conforme",
        TipoNovedad.SinAyuno => "Sin ayuno",
        TipoNovedad.SobrePeso => "Sobre peso",
        TipoNovedad.SignosClinicos => "Signos clínicos",
        TipoNovedad.Otro => "Otro",
        _ => "Novedad"
    };

    /// <summary>
    /// "Cuy #3 · Oreja dura", o solo el tipo si la novedad no cuelga de un
    /// animal.
    ///
    /// PagoService rechaza con 409 los descuentos cuya novedad no tiene cuy
    /// asociado, así que por la vía de escritura actual el nulo no llega
    /// aquí. Se contempla igual: el modelo lo admite, y reventar con un nulo
    /// legal convertiría el ticket en un 500 al pulsar "Imprimir".
    /// </summary>
    public static string LineaNovedad(DescuentoPago descuento)
    {
        var tipo = EtiquetaTipo(descuento.NovedadCat.Tipo);
        var numero = descuento.NovedadCat.CuyRegistro?.NumeroEnLote;
        return numero is int n ? $"Cuy #{n} · {tipo}" : tipo;
    }

    /// <summary>
    /// La cifra que va en grande: lo que la productora cobra de verdad.
    ///
    /// Mientras el ticket está pendiente no hay monto pagado y se imprime el
    /// del ticket, igual que siempre. Una vez pagado con descuentos, imprimir
    /// MontoUsd sería darle una cifra que nadie le entregó.
    /// </summary>
    public static decimal MontoDestacado(Pago pago) =>
        pago.MontoPagadoUsd ?? pago.MontoUsd;

    /// <summary>
    /// Si hay que imprimir el bloque de descuentos. Sin descuentos, el ticket
    /// sale exactamente igual que antes de esta feature.
    /// </summary>
    public static bool HayDesglose(Pago pago) => pago.Descuentos.Count > 0;
}

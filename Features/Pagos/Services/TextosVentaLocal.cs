using System.Globalization;
using CoopagcuyApi.Features.Pagos.Models;

namespace CoopagcuyApi.Features.Pagos.Services;

/// <summary>
/// Las líneas del ticket que cambian cuando el pago es una venta local.
///
/// Fuera del armado del PDF por el mismo motivo que TextosGuia y
/// TextosTicket: QuestPDF comprime los flujos de texto del documento, así que
/// del binario no se puede afirmar nada. Como funciones puras sí se comprueban.
/// </summary>
public static class TextosVentaLocal
{
    /// <summary>
    /// "VENTA LOCAL" para la venta en la comunidad, o el literal histórico
    /// "Comprobante de pago" para el ciclo con la planta.
    ///
    /// Ese segundo literal NO SE TOCA: es el texto que ya salió impreso en
    /// tickets que circulan hoy. Devolver "COMPROBANTE DE PAGO" en
    /// mayúsculas parece un detalle cosmético pero es exactamente la
    /// regresión que esta tarea prometía no cometer — cada letra salvo la
    /// primera habría cambiado de caja frente al ticket que la planta ya
    /// conoce.
    /// </summary>
    public static string Encabezado(Pago pago) =>
        pago.EsVentaLocal ? "VENTA LOCAL" : "Comprobante de pago";

    /// <summary>
    /// Rótulo de estado.
    ///
    /// Quien recibe el dinero del comprador en una venta local es la CAT, no
    /// la productora. El pago "nace cobrado" para el sistema —Estado Recibido,
    /// nada que nadie tenga que hacer— pero la plata todavía no llegó a manos
    /// de quien se lleva este papel, y el pie del ticket ya lo dice: acredita
    /// "un pago pendiente de la cooperativa". El rótulo no puede afirmar lo
    /// contrario del pie del mismo documento.
    ///
    /// Por eso ninguna de las dos ramas dice que se cobró. La de cuotas sigue
    /// existiendo porque además informa el mecanismo, que la línea de método
    /// detalla justo debajo.
    /// </summary>
    public static string TextoEstado(Pago pago)
    {
        if (!pago.EsVentaLocal) return TicketPagoService.TextoEstado(pago.Estado);

        return pago.EsCuotas()
            ? "VENDIDO EN LA COMUNIDAD — A CUOTAS"
            : "VENDIDO EN LA COMUNIDAD — POR COBRAR";
    }

    /// <summary>
    /// "Efectivo", o "A cuotas: 30 días × USD 2,50".
    ///
    /// InvariantCulture por el mismo motivo que las fechas: el separador
    /// decimal cambia con la cultura activa del contenedor, y la cifra del
    /// acuerdo es de las que la productora mira primero.
    /// </summary>
    public static string LineaMetodo(Pago pago)
    {
        if (!pago.EsCuotas()) return pago.MetodoPago;

        var valor = (pago.ValorPorDia ?? 0)
            .ToString("N2", CultureInfo.InvariantCulture)
            .Replace('.', ',');

        return $"A cuotas: {pago.NumeroDias} días × USD {valor}";
    }
}

namespace CoopagcuyApi.Features.Reportes.Services;

/// Un animal que salió en un despacho del período.
public record AnimalDespachado(int LoteId, int NumeroEnLote, int? ProductoraId);

/// <summary>
/// Lo que una productora cobró por un lote, y entre cuántos animales se
/// reparte.
///
/// `AnimalesCubiertos` NO incluye los vendidos en la comunidad: esos nunca
/// llegaron a la planta y su pago fue otro. Es además el mismo conteo que la
/// operadora vio al crear el pago.
/// </summary>
public record PagoDeLote(
    int LoteId, int ProductoraId, decimal MontoPagado, int AnimalesCubiertos);

/// El costo atribuido, y cuántos animales quedaron sin poder atribuirse.
public record CostoAtribuido(decimal Total, int AnimalesSinCosto);

/// <summary>
/// Reparte el costo de los animales despachados a partir de los pagos de sus
/// productoras.
///
/// Es un prorrateo: un pago es una cifra global por los animales de una
/// productora en una jaula, y repartirla a partes iguales asume que todos
/// valían lo mismo. Es la única atribución posible con los datos que hay, y
/// por eso el reporte no da margen por despacho individual — a esa escala el
/// redondeo pesa más que la señal.
/// </summary>
public static class CostoDeLoDespachado
{
    public static CostoAtribuido Calcular(
        IReadOnlyList<AnimalDespachado> animales,
        IReadOnlyList<PagoDeLote> pagos)
    {
        // Se asume que Tarea 5 no produce dos pagos con la misma clave
        // (LoteId, ProductoraId): si eso pasara, ToDictionary lanza en vez
        // de silenciosamente sumar o quedarse con el último.
        var porClave = pagos.ToDictionary(p => (p.LoteId, p.ProductoraId));

        decimal total = 0m;
        var sinCosto = 0;

        foreach (var animal in animales)
        {
            // Sin productora no hay a quién atribuirlo: jaula antigua sin
            // detalle por animal.
            if (animal.ProductoraId is not int productoraId)
            {
                sinCosto++;
                continue;
            }

            // Su productora todavía no ha cobrado este lote. Vale
            // DESCONOCIDO, no cero.
            if (!porClave.TryGetValue((animal.LoteId, productoraId), out var pago)
                || pago.AnimalesCubiertos <= 0)
            {
                sinCosto++;
                continue;
            }

            total += pago.MontoPagado / pago.AnimalesCubiertos;
        }

        return new CostoAtribuido(Math.Round(total, 2), sinCosto);
    }
}

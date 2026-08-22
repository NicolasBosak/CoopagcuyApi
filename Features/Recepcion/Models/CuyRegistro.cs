using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Registro individual de cada cuy dentro de un lote de recepción.
/// Permite rastrear qué animal específico presentó observaciones y
/// vincularlo con su productora de origen.
/// </summary>
public class CuyRegistro
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    // Productora que entregó este animal específico: el lote (jaula)
    // puede reunir cuyes de varias productoras
    public int? ProductoraId { get; set; }
    public Productora? Productora { get; set; }

    // Posición del animal dentro del lote (1..20)
    public int NumeroEnLote { get; set; }

    public decimal PesoGramos { get; set; }
    public string ColorPelaje { get; set; } = string.Empty;
    public string EstadoOreja { get; set; } = string.Empty;
    public string TamanoAnimal { get; set; } = string.Empty;
    public string? SignosClinicos { get; set; }

    // Estado individual del animal según los parámetros de aceptación
    public EstadoLote Estado { get; set; }
    public string? MotivoNovedad { get; set; }

    // Venta local: la CAT vendió este animal en la comunidad en vez de
    // enviarlo a la planta, y queda atado al pago de esa venta. Nulo = sigue
    // disponible para movilizar.
    //
    // Vive aquí y no en una tabla intermedia porque la relación no tiene
    // ningún dato propio: el Pago ya lleva monto, fecha, responsable y
    // método, y lo único que faltaba era QUÉ animales.
    public int? VentaLocalPagoId { get; set; }
    public Pago? VentaLocalPago { get; set; }
}

using CoopagcuyApi.Common;
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
}

using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Retorno de un cuy específico a su productora de origen, cuando el
/// animal se marca como no apto durante el faenamiento. Visible en la
/// pantalla de productoras para la gestión de descuentos y seguimiento.
/// </summary>
public class RetornoProductora
{
    public int Id { get; set; }

    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    // Productora dueña del cuy retornado (con jaulas multi-productora
    // se toma del registro individual del animal)
    public int ProductoraId { get; set; }
    public Productora Productora { get; set; } = null!;

    // Número del cuy dentro del lote que se retorna
    public int NumeroEnLote { get; set; }

    public string Motivo { get; set; } = string.Empty;
    public DateTime FechaRetorno { get; set; } = DateTime.UtcNow;
    public string Responsable { get; set; } = string.Empty;
}

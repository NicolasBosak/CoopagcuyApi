namespace CoopagcuyApi.Features.Productoras.Models;

/// <summary>
/// Historial de cambios sobre el perfil de una productora — RF-105.
/// Cada fila registra la modificación de un campo individual.
/// </summary>
public class ProductoraCambio
{
    public int Id { get; set; }
    public int ProductoraId { get; set; }
    public Productora Productora { get; set; } = null!;

    public string CampoModificado { get; set; } = string.Empty;
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string ModificadoPor { get; set; } = string.Empty;
    public DateTime FechaCambio { get; set; } = DateTime.UtcNow;
}

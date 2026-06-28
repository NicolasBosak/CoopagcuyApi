using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Productoras.Models;

public class Productora
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;     // identificador único
    public string Comunidad { get; set; } = string.Empty;
    public string Canton { get; set; } = string.Empty;
    public CentroAcopio CatAsignado { get; set; }
    public string? Telefono { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Navegación
    public ICollection<Lote> Lotes { get; set; } = [];
}

using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Catalogos.Models;

namespace CoopagcuyApi.Features.Productoras.Models;

public class Productora
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;     // identificador único

    // Origen de la productora. El catálogo es la única fuente de nombres y
    // cantones válidos: como texto libre, "Patacocha" y "Patococha" eran dos
    // comunidades distintas en la ficha pública del QR. El cantón ya no se
    // guarda aquí — se lee de Comunidad.Canton.
    public int ComunidadId { get; set; }
    public Comunidad Comunidad { get; set; } = null!;

    public CentroAcopio CatAsignado { get; set; }
    public string? Telefono { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Navegación
    public ICollection<Lote> Lotes { get; set; } = [];
}

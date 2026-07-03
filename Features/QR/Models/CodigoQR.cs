using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.QR.Models;

public class CodigoQR
{
    public int Id { get; set; }

    // El QR pertenece a un lote faenado (producto terminado, puede reunir
    // varias jaulas) o a una jaula de recepción (registros históricos)
    public int? LoteId { get; set; }
    public Lote? Lote { get; set; }

    public int? LoteFaenadoId { get; set; }
    public LoteFaenado? LoteFaenado { get; set; }

    public string UrlPublica { get; set; } = string.Empty;   // URL de la página estática
    public string? BlobPath { get; set; }                    // ruta en Azure Blob Storage
    public DateTime FechaGeneracion { get; set; } = DateTime.UtcNow;
    public bool Activo { get; set; } = true;
}

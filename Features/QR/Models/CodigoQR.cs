using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.QR.Models;

public class CodigoQR
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public string UrlPublica { get; set; } = string.Empty;   // URL de la página estática
    public string? BlobPath { get; set; }                    // ruta en Azure Blob Storage
    public DateTime FechaGeneracion { get; set; } = DateTime.UtcNow;
    public bool Activo { get; set; } = true;
}

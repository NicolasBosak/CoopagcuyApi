using CoopagcuyApi.Common;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Faenamiento.Models;

namespace CoopagcuyApi.Features.Productoras.Models;

public class Lote
{
    public int Id { get; set; }

    // Código único: PAT-20260615-001
    public string CodigoLote { get; set; } = string.Empty;

    public int ProductoraId { get; set; }
    public Productora Productora { get; set; } = null!;

    public CentroAcopio CentroAcopio { get; set; }
    public DateTime FechaRecepcion { get; set; }
    public int CantidadAnimales { get; set; }          // máx 20 por SRS RF-104
    public decimal PesoTotalGramos { get; set; }
    public EstadoLote Estado { get; set; }

    public string? ResponsableRecepcion { get; set; }
    public string? Observaciones { get; set; }
    public bool SincronizadoOffline { get; set; } = false;
    public DateTime? FechaSincronizacion { get; set; }

    // Navegación
    public ICollection<Novedad> Novedades { get; set; } = [];
    public RegistroFaenamiento? Faenamiento { get; set; }
    public CodigoQR? CodigoQR { get; set; }
}
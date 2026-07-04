using CoopagcuyApi.Common;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Features.Faenamiento.Models;

namespace CoopagcuyApi.Features.Productoras.Models;

/// <summary>
/// El lote representa una jaula de transporte de hasta 20 cuyes.
/// Puede contener animales de varias productoras: cada CuyRegistro
/// lleva su propia productora de origen. Mientras está abierto acepta
/// entregas; al completar 20 (o cerrarse manualmente) queda listo
/// para movilización a la planta.
/// </summary>
public class Lote
{
    public int Id { get; set; }

    // Código único: PAT-20260615-001
    public string CodigoLote { get; set; } = string.Empty;

    // Productora "principal" (histórico). En lotes multi-productora es la
    // primera que entregó; el origen real de cada animal está en CuyRegistro.
    public int? ProductoraId { get; set; }
    public Productora? Productora { get; set; }

    public CentroAcopio CentroAcopio { get; set; }
    public DateTime FechaRecepcion { get; set; }
    public int CantidadAnimales { get; set; }          // máx 20 por SRS RF-104
    public decimal PesoTotalGramos { get; set; }
    public EstadoLote Estado { get; set; }

    // Ciclo de vida de la jaula
    public bool Cerrado { get; set; } = true;          // históricos: cerrados
    public DateTime? FechaCierre { get; set; }

    public string? ResponsableRecepcion { get; set; }
    public string? Observaciones { get; set; }

    // Condición sanitaria visual: null/vacío = sin signos clínicos visibles
    public string? SignosClinicos { get; set; }

    public bool SincronizadoOffline { get; set; } = false;
    public DateTime? FechaSincronizacion { get; set; }

    // Navegación
    public ICollection<Novedad> Novedades { get; set; } = [];
    public ICollection<CuyRegistro> Cuyes { get; set; } = [];
    public ICollection<RegistroFaenamiento> Faenamientos { get; set; } = [];
    public Movilizacion? Movilizacion { get; set; }
    public CodigoQR? CodigoQR { get; set; }
}

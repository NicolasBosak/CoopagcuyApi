using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

public class RegistroFaenamiento
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    // Distintivo de la sesión dentro del lote (F1, F2, …): un lote puede
    // faenarse en varias sesiones parciales y cada una debe identificarse
    public int NumeroSesion { get; set; } = 1;

    // Lote de producto terminado al que pertenece esta sesión: una sesión
    // de planta que toma animales de varias jaulas genera UN lote faenado
    public int? LoteFaenadoId { get; set; }
    public LoteFaenado? LoteFaenado { get; set; }

    public DateTime FechaFaenamiento { get; set; }
    public string OperarioResponsable { get; set; } = string.Empty;
    public int UnidadesFaenadas { get; set; }
    public decimal PesoTotalCanalGramos { get; set; }
    public decimal? TemperaturaAlmacenamiento { get; set; }
    public EstadoCanal EstadoCanal { get; set; }
    public string? Observaciones { get; set; }

    // Decomisos: animales descartados durante el proceso (punto de control)
    public int UnidadesDecomisadas { get; set; } = 0;
    public string? MotivoDecomiso { get; set; }

    // Puntos de control intermedios: lavado, empaque y cadena de frío
    public int? TiempoLavadoMinutos { get; set; }
    public string? PresentacionEmpaque { get; set; }
    public DateTime? FechaIngresoFrio { get; set; }
    public DateTime? FechaSalidaFrio { get; set; }

    // Navegación: estado individual por animal
    public ICollection<CuyFaenamiento> Cuyes { get; set; } = [];
}
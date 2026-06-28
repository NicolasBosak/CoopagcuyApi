using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

public class Faenamiento
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public DateTime FechaFaenamiento { get; set; }
    public string OperarioResponsable { get; set; } = string.Empty;
    public int UnidadesFaenadas { get; set; }
    public decimal PesoTotalCanalGramos { get; set; }  // referencia: 907g/canal
    public decimal? TemperaturaAlmacenamiento { get; set; }
    public EstadoCanal EstadoCanal { get; set; }
    public string? Observaciones { get; set; }
}
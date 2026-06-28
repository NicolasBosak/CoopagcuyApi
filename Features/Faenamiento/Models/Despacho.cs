using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

public class Despacho
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public string ClienteDestino { get; set; } = string.Empty;
    public DateTime FechaDespacho { get; set; }
    public int CantidadUnidades { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string? Transporte { get; set; }
    public string? Observaciones { get; set; }
}
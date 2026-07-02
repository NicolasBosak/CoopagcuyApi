using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Devolución de producto por parte de un cliente (restaurante) — RF-307.
/// Queda vinculada al lote de origen y, a través de él, a la productora,
/// para la gestión de pérdidas y descuentos.
/// </summary>
public class Devolucion
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public string ClienteDevuelve { get; set; } = string.Empty;
    public DateTime FechaDevolucion { get; set; }
    public int CantidadUnidades { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}

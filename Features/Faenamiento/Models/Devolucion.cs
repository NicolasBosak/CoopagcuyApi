using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Devolución de producto por parte de un cliente (restaurante) — RF-307.
/// Nace de un despacho concreto: el cliente y el lote faenado se derivan
/// de él, y las unidades devueltas nunca pueden superar las enviadas.
/// </summary>
public class Devolucion
{
    public int Id { get; set; }

    // Despacho del que proviene el producto devuelto
    public int? DespachoId { get; set; }
    public Despacho? Despacho { get; set; }

    // Referencias legadas: las devoluciones anteriores al vínculo con el
    // despacho apuntaban a la jaula y a la sesión de faenamiento
    public int? LoteId { get; set; }
    public Lote? Lote { get; set; }
    public int? RegistroFaenamientoId { get; set; }
    public RegistroFaenamiento? RegistroFaenamiento { get; set; }

    // Copiado del despacho al registrar: la historia queda inmutable
    public string ClienteDevuelve { get; set; } = string.Empty;

    public DateTime FechaDevolucion { get; set; }
    public int CantidadUnidades { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}

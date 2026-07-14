using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Despacho comercial del producto terminado. Pertenece a un lote faenado
/// (FAE-…) y detalla los animales específicos enviados, de modo que el
/// saldo despachable por lote se calcula por unidad y cada canal queda
/// trazada hasta su cliente.
/// </summary>
public class Despacho
{
    public int Id { get; set; }

    // Lote de producto terminado despachado
    public int? LoteFaenadoId { get; set; }
    public LoteFaenado? LoteFaenado { get; set; }

    // Referencia legada: los despachos anteriores al detalle por animal
    // apuntaban a la jaula de recepción
    public int? LoteId { get; set; }
    public Lote? Lote { get; set; }

    public string ClienteDestino { get; set; } = string.Empty;
    public DateTime FechaDespacho { get; set; }
    public int CantidadUnidades { get; set; }
    public string Responsable { get; set; } = string.Empty;
    // Datos del transporte de salida (para el reporte de Salida)
    public string? Chofer { get; set; }
    public string? Ruta { get; set; }
    // Mercado de destino — RF trazabilidad hacia adelante: clasifica la
    // entrega en los mercados de COOPAGCUY (local / nacional / internacional)
    public string TipoMercado { get; set; } = "Local"; // Local|Nacional|Internacional
    public string? Ciudad { get; set; }
    public string? Pais { get; set; }
    // Campo genérico legado; reemplazado por Chofer/Ruta
    public string? Transporte { get; set; }
    public string? Observaciones { get; set; }

    // Animales específicos incluidos en este despacho
    public ICollection<DespachoCuy> Cuyes { get; set; } = [];
}

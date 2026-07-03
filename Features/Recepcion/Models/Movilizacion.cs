using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Registro de movilización del lote desde el CAT hasta la planta de
/// faenamiento — eslabón 2 del modelo de trazabilidad (transporte).
/// Cierra el quiebre documental identificado en el diagnóstico: sin este
/// registro la trazabilidad se pierde durante el traslado.
/// </summary>
public class Movilizacion
{
    public int Id { get; set; }

    // Un lote se moviliza una sola vez hacia la planta (1:1)
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public DateTime FechaDespacho { get; set; }
    public string Conductor { get; set; } = string.Empty;
    public int CantidadMovilizada { get; set; }
    public string? CondicionesTransporte { get; set; }

    // Declaración de tratamientos básicos (guía de movilización)
    public string? TipoForraje { get; set; }
    public int? DiasRetiroMedicamentos { get; set; }

    public string ResponsableDespacho { get; set; } = string.Empty;
    public string? Observaciones { get; set; }

    // Confirmación de llegada a la planta de Sulupali Chico
    public DateTime? FechaRecepcionPlanta { get; set; }
    public string? RecibidoPor { get; set; }
    public string? CondicionLlegada { get; set; }

    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}

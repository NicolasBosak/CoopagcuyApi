using CoopagcuyApi.Features.Recepcion.Models;

namespace CoopagcuyApi.Features.Pagos.Models;

/// <summary>
/// Rebaja sobre el monto del ticket, justificada por un defecto que el centro
/// de acopio documentó.
///
/// `NovedadCatId` es obligatorio y no anulable: ahí vive toda la trazabilidad
/// de la feature. Una fila de descuento sin novedad de origen sería
/// exactamente el caso que este diseño existe para impedir — la planta pagando
/// de menos por un problema que nadie vio.
/// </summary>
public class DescuentoPago
{
    public int Id { get; set; }

    public int PagoId { get; set; }
    public Pago Pago { get; set; } = null!;

    public int NovedadCatId { get; set; }
    public Novedad NovedadCat { get; set; } = null!;

    // Lo que observó la planta, con sus palabras. La novedad del CAT dice lo
    // que se vio al recibir; esto dice lo que se vio al faenar.
    public string Descripcion { get; set; } = string.Empty;

    public decimal MontoUsd { get; set; }
    public string RegistradoPor { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}

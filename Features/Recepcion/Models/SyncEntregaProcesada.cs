namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Marca de idempotencia del sync offline — RF-211. Cada entrega capturada
/// sin conexión viaja con un Id generado en el dispositivo; al procesarla,
/// esta marca se guarda EN LA MISMA transacción que los datos. Si el
/// dispositivo reintenta (timeout, corte de red tras guardar), la marca
/// permite reconocer el duplicado y no registrar los animales dos veces.
/// </summary>
public class SyncEntregaProcesada
{
    public int Id { get; set; }
    public string DispositivoId { get; set; } = string.Empty;
    public string IdCliente { get; set; } = string.Empty;
    public DateTime FechaProcesado { get; set; } = DateTime.UtcNow;
}

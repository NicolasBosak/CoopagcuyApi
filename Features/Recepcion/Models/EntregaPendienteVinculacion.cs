namespace CoopagcuyApi.Features.Recepcion.Models;

public enum EstadoVinculacion
{
    Pendiente,   // esperando que un admin la asigne a una productora
    Vinculada,   // ya se resolvió y registró como entrega
    Descartada   // el admin la desechó (cédula errada sin productora real)
}

/// <summary>
/// Entrega capturada sin conexión cuya cédula ES una cédula ecuatoriana
/// válida pero NO corresponde a ninguna productora registrada. En lugar de
/// rechazarla o inventar una productora, queda en cuarentena en esta bandeja;
/// un administrador la vincula a la productora correcta (o la descarta) y
/// solo entonces los animales entran a una jaula. Así ningún cuy queda sin
/// productora real y no se ensucia el catálogo con altas automáticas.
///
/// El detalle de cuyes se conserva como JSON: la vinculación reconstruye la
/// entrega original tal como se capturó en el CAT.
/// </summary>
public class EntregaPendienteVinculacion
{
    public int Id { get; set; }

    public string Cedula { get; set; } = string.Empty;
    public string CentroAcopio { get; set; } = string.Empty;

    public bool EnAyunas { get; set; } = true;
    public string ResponsableRecepcion { get; set; } = string.Empty;
    public string? Observaciones { get; set; }

    // Momento real de la captura en la tablet (no el de la sincronización)
    public DateTime FechaCaptura { get; set; }

    // Idempotencia del sync: identifica la entrega en el dispositivo de origen
    public string DispositivoId { get; set; } = string.Empty;
    public string IdCliente { get; set; } = string.Empty;

    // Cuyes serializados (List<CuyRegistroDto>) tal como llegaron del cliente
    public string CuyesJson { get; set; } = string.Empty;

    public EstadoVinculacion Estado { get; set; } = EstadoVinculacion.Pendiente;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaResolucion { get; set; }
    public string? ResueltaPor { get; set; }
    public int? ProductoraVinculadaId { get; set; }
}

namespace CoopagcuyApi.Common.Auth.Recuperacion;

public enum EstadoSolicitudPassword
{
    Pendiente,   // esperando que un administrador la atienda
    Resuelta,    // se asignó una contraseña temporal
    Descartada   // el administrador decidió no atenderla
}

/// <summary>
/// Petición de un usuario que olvidó su contraseña. Los operadores entran
/// solo con cédula y el correo es opcional, así que no hay dónde enviar un
/// enlace de un solo uso: la solicitud queda aquí, un administrador la ve en
/// su bandeja y entrega una contraseña temporal por teléfono.
///
/// Es la misma forma que <c>EntregaPendienteVinculacion</c>: una cola de
/// trabajo persistente revisada por un humano. Persistente y no en memoria a
/// propósito — el Container App escala a cero y una cola en memoria se
/// perdería con la última réplica.
/// </summary>
public class SolicitudRestablecerPassword
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    // Se copia al crear: la auditoría sobrevive aunque el usuario cambie
    public string CedulaSolicitada { get; set; } = string.Empty;

    public EstadoSolicitudPassword Estado { get; set; }
        = EstadoSolicitudPassword.Pendiente;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaResolucion { get; set; }

    // Cédula del administrador que resolvió o descartó
    public string? ResueltaPor { get; set; }

    // IP real del solicitante (ya reescrita por UseForwardedHeaders)
    public string? IpSolicitud { get; set; }
}

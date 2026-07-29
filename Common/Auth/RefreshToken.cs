namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Sesión persistente de un dispositivo. El secreto (refresh token) nunca
/// se almacena en claro: solo su hash SHA-256, de modo que una fuga de la
/// base de datos no permite reconstruir tokens vigentes.
///
/// Ciclo de vida:
///   · Se emite al iniciar sesión y viaja al cliente en una cookie httpOnly.
///   · Cada uso lo ROTA: se marca Revocado y se enlaza al que lo reemplaza
///     (ReemplazadoPorHash). Presentar un token ya rotado es señal de robo
///     y revoca toda la familia (reuse detection).
///   · Expira a los 7 días de forma absoluta (no se extiende con el uso).
///   · Un administrador puede revocarlo desde la pantalla de sesiones.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    // Hash SHA-256 (hex) del token; el valor en claro solo lo tiene el cliente
    public string TokenHash { get; set; } = string.Empty;

    // Identifica la tablet/navegador para la pantalla de sesiones activas
    public string? DispositivoId { get; set; }

    // Rastro para auditoría y para la pantalla de administración
    public string? UserAgent { get; set; }
    public string? IpCreacion { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaExpiracion { get; set; }
    public DateTime FechaUltimoUso { get; set; } = DateTime.UtcNow;

    public bool Revocado { get; set; }
    public DateTime? FechaRevocacion { get; set; }
    // Hash del token que sustituyó a este al rotar (cadena de rotación)
    public string? ReemplazadoPorHash { get; set; }

    // Vigente = ni revocado ni expirado
    public bool EstaActivo => !Revocado && DateTime.UtcNow < FechaExpiracion;
}

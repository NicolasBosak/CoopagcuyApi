namespace CoopagcuyApi.Common.Auth;

public record LoginRequestDto(
    string Cedula,
    string Password,
    // Identificador estable de la tablet/navegador; permite listar y revocar
    // sesiones por dispositivo. Opcional: el servidor genera uno si falta.
    string? DispositivoId
);

public record LoginResponseDto(
    string Token,
    string NombreCompleto,
    string Cedula,
    string Rol,
    string? CatAsignado,
    // Expiración del access token (corto). El front renueva al vencer.
    DateTime Expira,
    // Fin de la sesión de 7 días (expiración del refresh token). El front la
    // usa para saber hasta cuándo permite "entrar directo" sin conexión.
    DateTime SesionExpira,
    // Se activó tras un restablecimiento: el front lleva al usuario a la
    // pantalla de cambio y no le deja navegar a otra hasta que la cambie.
    bool DebeCambiarPassword
);

// Resultado interno del login/refresh: la respuesta que va al cuerpo más el
// refresh token en claro, que el controlador coloca en una cookie httpOnly
// y nunca se serializa en el JSON de respuesta.
public record AuthTokensResultado(
    LoginResponseDto Respuesta,
    string RefreshTokenPlano,
    DateTime RefreshExpira
);

// Fila de la pantalla de administración de sesiones activas. Nunca incluye
// el token ni su hash: solo metadatos para identificar y revocar.
public record SesionActivaDto(
    int Id,
    int UsuarioId,
    string NombreUsuario,
    string Cedula,
    string Rol,
    string? CatAsignado,
    string? DispositivoId,
    string? UserAgent,
    string? IpCreacion,
    DateTime FechaCreacion,
    DateTime FechaUltimoUso,
    DateTime FechaExpiracion,
    // La sesión de quien está viendo la pantalla, para no auto-desconectarse
    bool EsSesionActual
);

namespace CoopagcuyApi.Common.Auth.Recuperacion;

public record SolicitarRecuperacionDto(string Cedula);

// Fila de la bandeja del administrador. Nunca lleva hash ni contraseña.
public record SolicitudPasswordDto(
    int Id,
    int UsuarioId,
    string NombreCompleto,
    string Cedula,
    string Rol,
    string? CatAsignado,
    // El usuario pudo desactivarse tras solicitar: la bandeja lo muestra
    // para que el administrador no intente restablecer en vano
    bool UsuarioActivo,
    string Estado,
    DateTime FechaCreacion,
    DateTime? FechaResolucion,
    string? ResueltaPor
);

// La contraseña temporal viaja UNA sola vez, en la respuesta de resolver.
// No se guarda en claro ni se puede volver a consultar.
public record PasswordTemporalDto(
    string PasswordTemporal,
    string NombreCompleto,
    string Cedula
);

public record CambiarPasswordDto(string PasswordActual, string PasswordNueva);

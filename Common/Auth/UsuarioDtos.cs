using CoopagcuyApi.Common;

namespace CoopagcuyApi.Common.Auth;

public class CrearUsuarioDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    // Número de cédula: identificador único de inicio de sesión
    public string Cedula { get; set; } = string.Empty;
    // Correo de contacto opcional; no sirve para iniciar sesión
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    // Obligatorio para OperadorCAT: centro donde puede registrar
    public CentroAcopio? CatAsignado { get; set; }
}

public class ActualizarUsuarioDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    // Correo de contacto opcional (vacío = quitarlo)
    public string? Email { get; set; }
    public RolUsuario Rol { get; set; }
    public CentroAcopio? CatAsignado { get; set; }
    // Opcional: si viene, se restablece la contraseña
    public string? NuevaPassword { get; set; }
}

public record UsuarioResponseDto(
    int Id,
    string NombreCompleto,
    string Cedula,
    string? Email,
    string Rol,
    string? CatAsignado,
    bool Activo,
    DateTime FechaCreacion
);

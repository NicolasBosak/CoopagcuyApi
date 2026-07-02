using CoopagcuyApi.Common;

namespace CoopagcuyApi.Common.Auth;

public class CrearUsuarioDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
}

public class ActualizarUsuarioDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    // Opcional: si viene, se restablece la contraseña
    public string? NuevaPassword { get; set; }
}

public record UsuarioResponseDto(
    int Id,
    string NombreCompleto,
    string Email,
    string Rol,
    bool Activo,
    DateTime FechaCreacion
);

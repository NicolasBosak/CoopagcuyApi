using CoopagcuyApi.Common;

namespace CoopagcuyApi.Common.Auth;

public class Usuario
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;

    // Identificador único de acceso: número de cédula ecuatoriana.
    // Es la única credencial de inicio de sesión junto con la contraseña.
    public string Cedula { get; set; } = string.Empty;

    // Dato de contacto opcional; no sirve para iniciar sesión
    public string? Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }

    // Centro de acopio asignado: un Operador de CAT solo puede registrar
    // entregas en su propio centro (comunidad)
    public CentroAcopio? CatAsignado { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
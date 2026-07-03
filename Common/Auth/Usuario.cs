using CoopagcuyApi.Common;

namespace CoopagcuyApi.Common.Auth;

public class Usuario
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }

    // Centro de acopio asignado: un Operador de CAT solo puede registrar
    // entregas en su propio centro (comunidad)
    public CentroAcopio? CatAsignado { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Alta de usuarios para las pruebas. Devuelve la entidad ya guardada porque
/// Respawn trunca SIN RESTART IDENTITY: ninguna prueba puede asumir que el
/// primer usuario sembrado tenga Id 1.
/// </summary>
public static class Sembrador
{
    public const string PasswordPorDefecto = "clave1234";

    public static async Task<Usuario> UsuarioAsync(
        ApiFactory api,
        string cedula,
        RolUsuario rol = RolUsuario.OperadorCAT,
        CentroAcopio? cat = CentroAcopio.PAT,
        bool activo = true,
        string password = PasswordPorDefecto)
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = $"Usuario {cedula}",
            Cedula = cedula,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = rol,
            CatAsignado = rol == RolUsuario.OperadorCAT ? cat : null,
            Activo = activo
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }
}

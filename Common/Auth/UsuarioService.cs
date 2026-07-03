using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Common.Auth;

public interface IUsuarioService
{
    Task<IEnumerable<UsuarioResponseDto>> ListarAsync(bool incluirInactivos);
    Task<UsuarioResponseDto?> ObtenerPorIdAsync(int id);
    Task<UsuarioResponseDto> CrearAsync(CrearUsuarioDto dto);
    Task<bool> ActualizarAsync(int id, ActualizarUsuarioDto dto);
    Task<bool> CambiarEstadoAsync(int id, bool activo, int usuarioActualId);
}

public class UsuarioService(AppDbContext db) : IUsuarioService
{
    public async Task<IEnumerable<UsuarioResponseDto>> ListarAsync(bool incluirInactivos)
    {
        var query = db.Usuarios.AsQueryable();
        if (!incluirInactivos)
            query = query.Where(u => u.Activo);

        return await query
            .OrderBy(u => u.NombreCompleto)
            .Select(u => new UsuarioResponseDto(
                u.Id, u.NombreCompleto, u.Email,
                u.Rol.ToString(),
                u.CatAsignado != null ? u.CatAsignado.ToString() : null,
                u.Activo, u.FechaCreacion))
            .ToListAsync();
    }

    public async Task<UsuarioResponseDto?> ObtenerPorIdAsync(int id)
    {
        var u = await db.Usuarios.FindAsync(id);
        return u is null ? null : MapToDto(u);
    }

    public async Task<UsuarioResponseDto> CrearAsync(CrearUsuarioDto dto)
    {
        ValidarPassword(dto.Password);
        ValidarCatOperador(dto.Rol, dto.CatAsignado);

        var email = dto.Email.Trim().ToLowerInvariant();
        var existe = await db.Usuarios.AnyAsync(u => u.Email == email);
        if (existe)
            throw new InvalidOperationException(
                "Ya existe un usuario registrado con ese correo.");

        var usuario = new Usuario
        {
            NombreCompleto = dto.NombreCompleto.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Rol = dto.Rol,
            CatAsignado = dto.Rol == RolUsuario.OperadorCAT
                ? dto.CatAsignado : null
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return MapToDto(usuario);
    }

    public async Task<bool> ActualizarAsync(int id, ActualizarUsuarioDto dto)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return false;

        ValidarCatOperador(dto.Rol, dto.CatAsignado);

        usuario.NombreCompleto = dto.NombreCompleto.Trim();
        usuario.Rol = dto.Rol;
        usuario.CatAsignado = dto.Rol == RolUsuario.OperadorCAT
            ? dto.CatAsignado : null;

        if (!string.IsNullOrEmpty(dto.NuevaPassword))
        {
            ValidarPassword(dto.NuevaPassword);
            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NuevaPassword);
        }

        await db.SaveChangesAsync();
        return true;
    }

    // Un Operador de CAT debe tener centro asignado: es lo que limita
    // dónde puede registrar entregas
    private static void ValidarCatOperador(RolUsuario rol, CentroAcopio? cat)
    {
        if (rol == RolUsuario.OperadorCAT && cat is null)
            throw new InvalidOperationException(
                "Un Operador de CAT debe tener un centro de acopio asignado.");
    }

    public async Task<bool> CambiarEstadoAsync(int id, bool activo, int usuarioActualId)
    {
        if (id == usuarioActualId && !activo)
            throw new InvalidOperationException(
                "No puedes desactivar tu propia cuenta.");

        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return false;

        // Evita dejar el sistema sin ningún administrador activo
        if (!activo && usuario.Rol is RolUsuario.AdminCooperativa or RolUsuario.AdminTecnico)
        {
            var otrosAdminsActivos = await db.Usuarios.CountAsync(u =>
                u.Id != id && u.Activo &&
                (u.Rol == RolUsuario.AdminCooperativa || u.Rol == RolUsuario.AdminTecnico));

            if (otrosAdminsActivos == 0)
                throw new InvalidOperationException(
                    "No se puede desactivar al único administrador activo del sistema.");
        }

        usuario.Activo = activo;
        await db.SaveChangesAsync();
        return true;
    }

    // Política mínima de contraseñas: 8+ caracteres, al menos una letra y un número
    private static void ValidarPassword(string password)
    {
        if (password.Length < 8 ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit))
        {
            throw new InvalidOperationException(
                "La contraseña debe tener al menos 8 caracteres, " +
                "incluyendo una letra y un número.");
        }
    }

    private static UsuarioResponseDto MapToDto(Usuario u) => new(
        u.Id, u.NombreCompleto, u.Email,
        u.Rol.ToString(), u.CatAsignado?.ToString(),
        u.Activo, u.FechaCreacion);
}

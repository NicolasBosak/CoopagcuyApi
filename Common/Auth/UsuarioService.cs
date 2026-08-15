using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Common.Auth;

public interface IUsuarioService
{
    Task<IEnumerable<UsuarioResponseDto>> ListarAsync(bool incluirInactivos);
    Task<UsuarioResponseDto?> ObtenerPorIdAsync(int id);
    Task<UsuarioCreadoDto> CrearAsync(CrearUsuarioDto dto);
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
                u.Id, u.NombreCompleto, u.Cedula, u.Email,
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

    public async Task<UsuarioCreadoDto> CrearAsync(CrearUsuarioDto dto)
    {
        ValidarCedula(dto.Cedula);
        ValidarCatOperador(dto.Rol, dto.CatAsignado);

        var cedula = dto.Cedula.Trim();
        var existe = await db.Usuarios.AnyAsync(u => u.Cedula == cedula);
        if (existe)
            throw new InvalidOperationException(
                "Ya existe un usuario registrado con esa cédula.");

        var usuario = new Usuario
        {
            NombreCompleto = dto.NombreCompleto.Trim(),
            Cedula = cedula,
            Email = NormalizarEmail(dto.Email),
            Rol = dto.Rol,
            CatAsignado = dto.Rol == RolUsuario.OperadorCAT
                ? dto.CatAsignado : null
        };

        // La contraseña la genera el sistema: el administrador da de alta la
        // cuenta pero nunca elige —ni llega a conocer— la contraseña con la
        // que esa persona va a operar. La dicta una vez y el usuario la cambia.
        var temporal = CredencialTemporal.Asignar(usuario);

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return new UsuarioCreadoDto(MapToDto(usuario), temporal);
    }

    public async Task<bool> ActualizarAsync(int id, ActualizarUsuarioDto dto)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return false;

        ValidarCatOperador(dto.Rol, dto.CatAsignado);

        usuario.NombreCompleto = dto.NombreCompleto.Trim();
        // La cédula es inmutable; el correo de contacto sí puede cambiar
        usuario.Email = NormalizarEmail(dto.Email);
        usuario.Rol = dto.Rol;
        usuario.CatAsignado = dto.Rol == RolUsuario.OperadorCAT
            ? dto.CatAsignado : null;

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

    // La cédula es el identificador de acceso: debe ser una cédula
    // ecuatoriana válida (provincia y dígito verificador)
    private static void ValidarCedula(string cedula)
    {
        if (!ValidadorCedula.EsValida(cedula))
            throw new InvalidOperationException(
                "El número de cédula ingresado no es válido.");
    }

    private static string? NormalizarEmail(string? email) =>
        string.IsNullOrWhiteSpace(email)
            ? null : email.Trim().ToLowerInvariant();

    private static UsuarioResponseDto MapToDto(Usuario u) => new(
        u.Id, u.NombreCompleto, u.Cedula, u.Email,
        u.Rol.ToString(), u.CatAsignado?.ToString(),
        u.Activo, u.FechaCreacion);
}

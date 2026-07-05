using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Common.Auth;

public interface IAuthService
{
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto dto);
    Task<bool> ExistenUsuariosAsync();
    Task<Usuario> CrearUsuarioInicialAsync(
        string nombre, string cedula, string? email, string password, RolUsuario rol);
}

public class AuthService(
    AppDbContext db,
    IJwtTokenService tokenService) : IAuthService
{
    // Hash señuelo generado al arrancar: la verificación se ejecuta aunque
    // la cédula no exista, para no revelar qué cédulas están registradas
    // mediante diferencias de tiempo de respuesta (timing attack)
    private static readonly string HashSenuelo =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto dto)
    {
        // No se valida el formato aquí: el identificador es lo que esté
        // almacenado, para nunca dejar fuera a un usuario existente
        var cedula = dto.Cedula.Trim();
        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Cedula == cedula && u.Activo);

        var hashComparacion = usuario?.PasswordHash ?? HashSenuelo;

        var passwordValida = BCrypt.Net.BCrypt.Verify(dto.Password, hashComparacion);

        if (usuario is null || !passwordValida)
            return null;

        var token = tokenService.GenerarToken(usuario);

        return new LoginResponseDto(
            Token: token,
            NombreCompleto: usuario.NombreCompleto,
            Cedula: usuario.Cedula,
            Rol: usuario.Rol.ToString(),
            CatAsignado: usuario.CatAsignado?.ToString(),
            Expira: DateTime.UtcNow.AddHours(10)
        );
    }

    public Task<bool> ExistenUsuariosAsync() => db.Usuarios.AnyAsync();

    public async Task<Usuario> CrearUsuarioInicialAsync(
        string nombre, string cedula, string? email, string password, RolUsuario rol)
    {
        if (!ValidadorCedula.EsValida(cedula))
        {
            throw new InvalidOperationException(
                "El número de cédula ingresado no es válido.");
        }

        if (password.Length < 8 ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit))
        {
            throw new InvalidOperationException(
                "La contraseña debe tener al menos 8 caracteres, " +
                "incluyendo una letra y un número.");
        }

        var usuario = new Usuario
        {
            NombreCompleto = nombre.Trim(),
            Cedula = cedula.Trim(),
            Email = string.IsNullOrWhiteSpace(email)
                ? null : email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = rol
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }
}

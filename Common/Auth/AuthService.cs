using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Common.Auth;

public interface IAuthService
{
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto dto);
    Task<Usuario> CrearUsuarioInicialAsync(
        string nombre, string email, string password, RolUsuario rol);
}

public class AuthService(
    AppDbContext db,
    IJwtTokenService tokenService) : IAuthService
{
    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto dto)
    {
        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Email == dto.Email && u.Activo);

        if (usuario is null)
            return null;

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, usuario.PasswordHash))
            return null;

        var token = tokenService.GenerarToken(usuario);

        return new LoginResponseDto(
            Token: token,
            NombreCompleto: usuario.NombreCompleto,
            Email: usuario.Email,
            Rol: usuario.Rol.ToString(),
            Expira: DateTime.UtcNow.AddHours(10)
        );
    }

    public async Task<Usuario> CrearUsuarioInicialAsync(
        string nombre, string email, string password, RolUsuario rol)
    {
        var usuario = new Usuario
        {
            NombreCompleto = nombre,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = rol
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Common.Auth;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>
    /// Inicia sesión y retorna un token JWT.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        var resultado = await authService.LoginAsync(dto);

        if (resultado is null)
            return Unauthorized(new { mensaje = "Correo o contraseña incorrectos." });

        return Ok(resultado);
    }

    /// <summary>
    /// Crea el usuario administrador inicial del sistema.
    /// Solo ejecutar una vez en el despliegue inicial.
    /// Proteger o eliminar este endpoint en producción.
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup([FromBody] SetupRequestDto dto)
    {
        if (dto.ClaveSetup != "COOPAGCUY-SETUP-2026")
            return Forbid();

        var usuario = await authService.CrearUsuarioInicialAsync(
            dto.Nombre,
            dto.Email,
            dto.Password,
            RolUsuario.AdminTecnico);

        return Ok(new
        {
            mensaje = "Usuario administrador creado correctamente.",
            id = usuario.Id,
            email = usuario.Email,
            rol = usuario.Rol.ToString()
        });
    }
}

public record SetupRequestDto(
    string ClaveSetup,
    string Nombre,
    string Email,
    string Password
);
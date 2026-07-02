using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoopagcuyApi.Common.Auth;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController(
    IAuthService authService,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
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
        {
            logger.LogWarning(
                "Intento de login fallido para {Email} desde {IP}",
                dto.Email, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { mensaje = "Correo o contraseña incorrectos." });
        }

        return Ok(resultado);
    }

    /// <summary>
    /// Crea el usuario administrador inicial del sistema.
    /// Solo funciona si el sistema no tiene ningún usuario registrado
    /// y requiere la clave configurada en Setup:Key (variable de entorno).
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup([FromBody] SetupRequestDto dto)
    {
        // Solo utilizable en el arranque inicial: si ya existe algún
        // usuario, el endpoint queda deshabilitado permanentemente.
        if (await authService.ExistenUsuariosAsync())
        {
            logger.LogWarning(
                "Intento de uso de /setup con el sistema ya inicializado desde {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return NotFound();
        }

        var claveConfigurada = configuration["Setup:Key"];
        if (string.IsNullOrEmpty(claveConfigurada) || dto.ClaveSetup != claveConfigurada)
            return Unauthorized(new { mensaje = "Clave de instalación incorrecta." });

        try
        {
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

public record SetupRequestDto(
    string ClaveSetup,
    string Nombre,
    string Email,
    string Password
);

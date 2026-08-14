using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoopagcuyApi.Common.Auth;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController(
    IAuthService authService,
    ISesionService sesionService,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    // Nombre y alcance de la cookie del refresh token. El Path la limita a
    // los endpoints de auth: no viaja en cada llamada al API, reduciendo su
    // exposición. httpOnly la hace invisible a JavaScript (a prueba de XSS).
    private const string CookieRefresh = "refresh_token";
    private const string CookiePath = "/api/auth";

    /// <summary>
    /// Inicia sesión. Devuelve el access token (JWT) en el cuerpo y coloca
    /// el refresh token en una cookie httpOnly válida por 7 días.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        var resultado = await authService.LoginAsync(
            dto, Request.Headers.UserAgent.ToString(), IpCliente());

        if (resultado is null)
        {
            logger.LogWarning(
                "Intento de login fallido para la cédula {Cedula} desde {IP}",
                dto.Cedula, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { mensaje = "Cédula o contraseña incorrectas." });
        }

        EscribirCookieRefresh(resultado.RefreshTokenPlano, resultado.RefreshExpira);
        return Ok(resultado.Respuesta);
    }

    /// <summary>
    /// Renueva el access token usando el refresh token de la cookie httpOnly.
    /// Rota el refresh token en cada uso.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var refreshPlano = Request.Cookies[CookieRefresh];
        if (string.IsNullOrEmpty(refreshPlano))
            return Unauthorized(new { mensaje = "No hay sesión activa." });

        var resultado = await sesionService.RefrescarAsync(
            refreshPlano, dispositivoId: null,
            Request.Headers.UserAgent.ToString(), IpCliente());

        if (resultado is null)
        {
            BorrarCookieRefresh();
            return Unauthorized(new { mensaje = "La sesión expiró. Inicia sesión de nuevo." });
        }

        EscribirCookieRefresh(resultado.RefreshTokenPlano, resultado.RefreshExpira);
        return Ok(resultado.Respuesta);
    }

    /// <summary>
    /// Cierra la sesión del dispositivo actual: revoca el refresh token y
    /// borra la cookie.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        var refreshPlano = Request.Cookies[CookieRefresh];
        if (!string.IsNullOrEmpty(refreshPlano))
            await sesionService.RevocarPorTokenAsync(refreshPlano);

        BorrarCookieRefresh();
        return NoContent();
    }

    // ── Administración de sesiones activas ────────────────────────────────
    // Solo el administrador TÉCNICO: revocar sesiones es una herramienta de
    // soporte, no de gestión. El administrador de cooperativa conserva la
    // bandeja de contraseñas, que es lo que necesita para desbloquear gente.

    /// <summary>
    /// Lista las sesiones activas de todos los usuarios. Marca cuál es la
    /// sesión del propio administrador para que no se desconecte por error.
    /// </summary>
    [HttpGet("sesiones")]
    [Authorize(Roles = "AdminTecnico")]
    public async Task<IActionResult> ListarSesiones()
    {
        var sesiones = await sesionService.ListarActivasAsync(
            Request.Cookies[CookieRefresh]);
        return Ok(sesiones);
    }

    /// <summary>
    /// Revoca una sesión concreta. La tablet correspondiente perderá acceso
    /// en su siguiente intento de renovar el token.
    /// </summary>
    [HttpDelete("sesiones/{id:int}")]
    [Authorize(Roles = "AdminTecnico")]
    public async Task<IActionResult> RevocarSesion(int id)
    {
        var ok = await sesionService.RevocarSesionAsync(id);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Revoca todas las sesiones activas de un usuario (p. ej. si perdió la
    /// tablet o dejó de trabajar en la cooperativa).
    /// </summary>
    [HttpDelete("sesiones/usuario/{usuarioId:int}")]
    [Authorize(Roles = "AdminTecnico")]
    public async Task<IActionResult> RevocarSesionesUsuario(int usuarioId)
    {
        var revocadas = await sesionService.RevocarUsuarioAsync(usuarioId);
        return Ok(new { revocadas });
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
                dto.Cedula,
                dto.Email,
                dto.Password,
                RolUsuario.AdminTecnico);

            return Ok(new
            {
                mensaje = "Usuario administrador creado correctamente.",
                id = usuario.Id,
                cedula = usuario.Cedula,
                rol = usuario.Rol.ToString()
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    // ── Helpers de cookie ─────────────────────────────────────────────────

    private void EscribirCookieRefresh(string tokenPlano, DateTime expira)
    {
        Response.Cookies.Append(CookieRefresh, tokenPlano, new CookieOptions
        {
            HttpOnly = true,   // invisible a JavaScript → inmune a robo por XSS
            Secure = true,     // solo por HTTPS
            SameSite = SameSiteMode.None, // el front vive en otro dominio (SWA↔ACA)
            Path = CookiePath,
            Expires = expira,
            IsEssential = true
        });
    }

    private void BorrarCookieRefresh()
    {
        Response.Cookies.Append(CookieRefresh, "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = CookiePath,
            Expires = DateTimeOffset.UnixEpoch,
            IsEssential = true
        });
    }

    // IP real del cliente (ya reescrita por UseForwardedHeaders tras el proxy)
    private string? IpCliente() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}

public record SetupRequestDto(
    string ClaveSetup,
    string Nombre,
    string Cedula,
    string? Email,
    string Password
);

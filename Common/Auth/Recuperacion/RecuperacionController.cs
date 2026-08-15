using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace CoopagcuyApi.Common.Auth.Recuperacion;

/// <summary>
/// Recuperación de contraseña asistida por un administrador.
///
/// La ruta se declara explícitamente en vez de usar el "api/[controller]"
/// del resto del proyecto: la convención por nombre daría "api/recuperacion",
/// fuera del prefijo /api/auth al que está limitada la cookie del refresh
/// token, y agrupar ahí los endpoints de autenticación mantiene coherente el
/// modelo mental del módulo.
/// </summary>
[ApiController]
[Route("api/auth/recuperacion")]
public class RecuperacionController(IRecuperacionService servicio) : ControllerBase
{
    private const string MensajeGenerico =
        "Tu solicitud fue enviada al administrador. " +
        "Te contactará para darte una contraseña nueva.";

    // Ambos administradores. Reservarlo al técnico crearía un bloqueo: si él
    // olvidara su contraseña, nadie podría restablecérsela.
    private const string RolesAdmin = "AdminCooperativa,AdminTecnico";

    /// <summary>
    /// Registra la solicitud de un usuario que olvidó su contraseña.
    /// Anónimo y con el mismo rate limiter que el login.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarRecuperacionDto dto)
    {
        if (!ValidadorCedula.EsValida(dto.Cedula))
            return BadRequest(new
            {
                mensaje = "El número de cédula ingresado no es válido."
            });

        await servicio.SolicitarAsync(dto.Cedula, IpCliente());

        // Misma respuesta exista o no el usuario: el endpoint es público y no
        // debe revelar qué cédulas tienen cuenta en el sistema.
        return Ok(new { mensaje = MensajeGenerico });
    }

    /// <summary>
    /// Bandeja de solicitudes. Por defecto solo las pendientes; con
    /// ?incluirResueltas=true devuelve también el historial auditable.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> Listar([FromQuery] bool incluirResueltas = false)
        => Ok(await servicio.ListarAsync(incluirResueltas));

    /// <summary>
    /// Asigna una contraseña temporal, obliga al usuario a cambiarla y revoca
    /// sus sesiones. La temporal viaja en la respuesta y no se puede volver a
    /// consultar: el administrador la dicta y el sistema la olvida.
    /// </summary>
    [HttpPost("{id:int}/resolver")]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> Resolver(int id)
    {
        try
        {
            return Ok(await servicio.ResolverAsync(id, CedulaAdmin()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Cierra una solicitud que el administrador decide no atender, dejando
    /// constancia en lugar de borrar la fila.
    /// </summary>
    [HttpPost("{id:int}/descartar")]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> Descartar(int id)
    {
        try
        {
            return await servicio.DescartarAsync(id, CedulaAdmin())
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Cambia la contraseña del usuario autenticado. Sirve al cambio
    /// obligatorio tras un restablecimiento y al voluntario de cualquier
    /// usuario: es la misma operación.
    ///
    /// La ruta es absoluta porque no cuelga de /api/auth/recuperacion — el
    /// cambio de contraseña no es parte del flujo de solicitud.
    /// </summary>
    [HttpPost("/api/auth/cambiar-password")]
    [Authorize]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordDto dto)
    {
        var cedula = User.FindFirstValue("cedula");
        if (string.IsNullOrEmpty(cedula))
            return Unauthorized(new { mensaje = "Sesión no válida." });

        try
        {
            var ok = await servicio.CambiarPasswordAsync(
                cedula, dto.PasswordActual, dto.PasswordNueva);

            return ok
                ? NoContent()
                : Unauthorized(new { mensaje = "La contraseña actual no es correcta." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized(new { mensaje = "Sesión no válida." });
        }
    }

    /// <summary>
    /// Restablece la contraseña de un usuario por iniciativa del administrador,
    /// sin solicitud previa. Vive aquí y no en UsuariosController aunque el
    /// botón esté en la lista de usuarios: escribe en la tabla de solicitudes y
    /// revoca sesiones, así que pertenece al módulo de contraseñas.
    /// </summary>
    [HttpPost("usuario/{usuarioId:int}")]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> RestablecerPorAdmin(int usuarioId)
    {
        try
        {
            return Ok(await servicio.RestablecerPorAdminAsync(usuarioId, CedulaAdmin()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    // Cédula del administrador autenticado, para la auditoría de la solicitud
    private string CedulaAdmin() => User.FindFirstValue("cedula") ?? "desconocido";

    // IP real del cliente (ya reescrita por UseForwardedHeaders tras el proxy)
    private string? IpCliente() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}

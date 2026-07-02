using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Gestión de usuarios del sistema — RF-504.
/// Solo administradores pueden crear, editar y desactivar usuarios.
/// </summary>
[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = "AdminCooperativa,AdminTecnico")]
public class UsuariosController(IUsuarioService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool incluirInactivos = false)
    {
        var result = await service.ListarAsync(incluirInactivos);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var result = await service.ObtenerPorIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearUsuarioDto dto)
    {
        try
        {
            var result = await service.CrearAsync(dto);
            return CreatedAtAction(nameof(ObtenerPorId), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarUsuarioDto dto)
    {
        try
        {
            var ok = await service.ActualizarAsync(id, dto);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Activa o desactiva un usuario. No permite auto-desactivarse
    /// ni desactivar al último administrador activo.
    /// </summary>
    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioDto dto)
    {
        var usuarioActualId = int.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        try
        {
            var ok = await service.CambiarEstadoAsync(id, dto.Activo, usuarioActualId);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
}

public record CambiarEstadoUsuarioDto(bool Activo);

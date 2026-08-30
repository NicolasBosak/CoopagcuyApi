using System.Security.Claims;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Features.Productoras.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Productoras.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // todos los endpoints requieren JWT
public class ProductorasController(
    IProductoraService service,
    IValidator<CrearProductoraDto> validator) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> Listar(
        [FromQuery] string? comunidad,
        [FromQuery] string? cat,
        [FromQuery] bool incluirInactivas = false)
    {
        // El operador de CAT solo ve las productoras de su centro —el filtro
        // por catEfectivo lo garantiza—, incluidas las inactivas: desde que
        // puede reactivarlas, ocultárselas le quitaría la mitad del trabajo.
        var esOperador = User.IsInRole("OperadorCAT");
        var catEfectivo = esOperador
            ? User.FindFirst("cat")?.Value ?? cat
            : cat;
        var result = await service.ObtenerTodasAsync(
            comunidad, catEfectivo, incluirInactivas);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var result = await service.ObtenerPorIdAsync(id);
        if (result is null) return NotFound();

        // Un operador no puede leer la ficha de una productora de otro centro
        if (User.FueraDeAlcance(result.CatAsignado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede consultar productoras de su centro."
            });

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> Crear([FromBody] CrearProductoraDto dto)
    {
        // El operador no elige el centro de la productora que registra: se
        // sella con el CAT de su token. Se hace ANTES de validar para que el
        // validador vea el objeto definitivo, y para que un cuerpo con otro
        // centro no sea un error sino sencillamente un dato ignorado.
        if (User.CatRestringido() is string catOperador)
            dto.CatAsignado = catOperador;

        var validacion = await validator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

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
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] CrearProductoraDto dto)
    {
        var actual = await service.ObtenerPorIdAsync(id);
        if (actual is null) return NotFound();

        // Alcance sobre la productora tal como está HOY: si se comprobara
        // contra el DTO, bastaría con mandar el propio CAT en el cuerpo para
        // editar la de cualquier otro centro.
        if (User.FueraDeAlcance(actual.CatAsignado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede editar productoras de su centro."
            });

        if (User.CatRestringido() is string catOperador)
            dto.CatAsignado = catOperador;

        var validacion = await validator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

        // La auditoría identifica al usuario por su cédula (claim del token)
        var modificadoPor = User.FindFirstValue("cedula") ?? "desconocido";
        var ok = await service.ActualizarAsync(id, dto, modificadoPor);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Activa o desactiva una productora (baja lógica). Conserva su
    /// historial de lotes, pagos y trazabilidad.
    /// </summary>
    [HttpPatch("{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> CambiarEstado(
        int id, [FromBody] CambiarEstadoProductoraDto dto)
    {
        var actual = await service.ObtenerPorIdAsync(id);
        if (actual is null) return NotFound();

        // Vale para las dos direcciones: dar de baja y volver a activar.
        if (User.FueraDeAlcance(actual.CatAsignado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede cambiar el estado de las " +
                          "productoras de su centro."
            });

        var ok = await service.CambiarEstadoAsync(id, dto.Activa);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Historial de cambios de la productora — RF-105.
    /// </summary>
    [HttpGet("{id:int}/historial")]
    [Authorize(Roles = "AdminCooperativa")]
    public async Task<IActionResult> Historial(int id)
    {
        var result = await service.ObtenerHistorialAsync(id);
        return Ok(result);
    }
}

public record CambiarEstadoProductoraDto(bool Activa);

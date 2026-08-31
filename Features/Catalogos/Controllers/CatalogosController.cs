using CoopagcuyApi.Features.Catalogos.DTOs;
using CoopagcuyApi.Features.Catalogos.Services;
using CoopagcuyApi.Features.Recepcion.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Catalogos.Controllers;

/// <summary>
/// Gestión de catálogos del sistema — RF-506.
/// Lectura disponible para todos los roles autenticados;
/// escritura restringida a administradores.
/// </summary>
[ApiController]
[Route("api/catalogos")]
[Authorize]
public class CatalogosController(
    ICatalogosService service,
    IGeografiaService geografia) : ControllerBase
{
    // ── Provincias ───────────────────────────────────────────────────

    [HttpGet("provincias")]
    public async Task<IActionResult> ListarProvincias(
        [FromQuery] bool incluirInactivas = false) =>
        Ok(await geografia.ListarProvinciasAsync(incluirInactivas));

    [HttpPost("provincias")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearProvincia([FromBody] GuardarProvinciaDto dto)
    {
        try
        {
            var result = await geografia.CrearProvinciaAsync(dto);
            return CreatedAtAction(nameof(ListarProvincias), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("provincias/{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarProvincia(
        int id, [FromBody] GuardarProvinciaDto dto)
    {
        try
        {
            return await geografia.ActualizarProvinciaAsync(id, dto)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("provincias/{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoProvincia(
        int id, [FromBody] CambiarEstadoProvinciaDto dto)
    {
        try
        {
            return await geografia.CambiarEstadoProvinciaAsync(id, dto.Activa)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    // ── Cantones ─────────────────────────────────────────────────────

    [HttpGet("cantones")]
    public async Task<IActionResult> ListarCantones(
        [FromQuery] int? provinciaId = null,
        [FromQuery] bool incluirInactivos = false) =>
        Ok(await geografia.ListarCantonesAsync(provinciaId, incluirInactivos));

    [HttpPost("cantones")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearCanton([FromBody] GuardarCantonDto dto)
    {
        try
        {
            var result = await geografia.CrearCantonAsync(dto);
            return CreatedAtAction(nameof(ListarCantones), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("cantones/{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarCanton(
        int id, [FromBody] GuardarCantonDto dto)
    {
        try
        {
            return await geografia.ActualizarCantonAsync(id, dto)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("cantones/{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoCanton(
        int id, [FromBody] CambiarEstadoCantonDto dto)
    {
        try
        {
            return await geografia.CambiarEstadoCantonAsync(id, dto.Activo)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpGet("comunidades")]
    public async Task<IActionResult> ListarComunidades(
        [FromQuery] bool incluirInactivas = false)
    {
        var result = await service.ListarComunidadesAsync(incluirInactivas);
        return Ok(result);
    }

    [HttpPost("comunidades")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearComunidad([FromBody] GuardarComunidadDto dto)
    {
        try
        {
            var result = await service.CrearComunidadAsync(dto);
            return CreatedAtAction(nameof(ListarComunidades), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("comunidades/{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarComunidad(
        int id, [FromBody] GuardarComunidadDto dto)
    {
        try
        {
            var ok = await service.ActualizarComunidadAsync(id, dto);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("comunidades/{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoComunidad(
        int id, [FromBody] CambiarEstadoComunidadDto dto)
    {
        var ok = await service.CambiarEstadoComunidadAsync(id, dto.Activa);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Catálogo de centros de acopio. Dejó de derivarse de un enum: ahora se
    /// da de alta desde aquí, porque la organización puede sumar provincias.
    /// </summary>
    [HttpGet("centros-acopio")]
    public async Task<IActionResult> ListarCentrosAcopio(
        [FromQuery] bool incluirInactivos = false) =>
        Ok(await service.ListarCentrosAcopioAsync(incluirInactivos));

    [HttpPost("centros-acopio")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CrearCentroAcopio(
        [FromBody] CrearCentroAcopioDto dto)
    {
        try
        {
            var result = await service.CrearCentroAcopioAsync(dto);
            return CreatedAtAction(nameof(ListarCentrosAcopio), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("centros-acopio/{codigo}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ActualizarCentroAcopio(
        string codigo, [FromBody] ActualizarCentroAcopioDto dto)
    {
        try
        {
            return await service.ActualizarCentroAcopioAsync(codigo, dto)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("centros-acopio/{codigo}/estado")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> CambiarEstadoCentroAcopio(
        string codigo, [FromBody] CambiarEstadoCentroAcopioDto dto)
    {
        try
        {
            return await service.CambiarEstadoCentroAcopioAsync(codigo, dto.Activo)
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Condiciones verificables antes de enviar una jaula a planta. El front
    /// las pinta como checklist; se sirven desde aquí para que las etiquetas
    /// no queden duplicadas (y desincronizadas) entre API y front.
    /// </summary>
    [HttpGet("condiciones-transporte")]
    public IActionResult ListarCondicionesTransporte() =>
        Ok(CondicionTransporte.Catalogo
            .Select(kv => new CondicionTransporteDto(kv.Key, kv.Value)));
}

public record CambiarEstadoComunidadDto(bool Activa);
public record CambiarEstadoProvinciaDto(bool Activa);
public record CambiarEstadoCantonDto(bool Activo);
public record CambiarEstadoCentroAcopioDto(bool Activo);

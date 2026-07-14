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
public class CatalogosController(ICatalogosService service) : ControllerBase
{
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
        var ok = await service.ActualizarComunidadAsync(id, dto);
        return ok ? NoContent() : NotFound();
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
    /// Catálogo de centros de acopio. Fijo en el piloto: el código del CAT
    /// forma parte del identificador de lote (CAT-AAAAMMDD-SEC).
    /// </summary>
    [HttpGet("centros-acopio")]
    public IActionResult ListarCentrosAcopio() =>
        Ok(service.ListarCentrosAcopio());

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

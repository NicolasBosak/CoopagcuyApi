using CoopagcuyApi.Features.Faenamiento.DTOs;
using CoopagcuyApi.Features.Faenamiento.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Faenamiento.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FaenamientoController(
    IFaenamientoService service,
    IValidator<RegistrarFaenamientoBatchDto> batchValidator,
    IValidator<RegistrarDespachoDto> despachoValidator) : ControllerBase
{
    /// <summary>
    /// Lotes cerrados con animales pendientes de faenar. Los lotes con
    /// saldo cero desaparecen de esta vista automáticamente.
    /// </summary>
    [HttpGet("lotes-disponibles")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> LotesDisponibles()
    {
        var resultado = await service.LotesDisponiblesAsync();
        return Ok(resultado);
    }

    /// <summary>
    /// Registra una sesión de faenamiento que puede tomar animales de
    /// varios lotes (cuota). Devuelve alertas cuando una novedad marcada
    /// en planta ya venía registrada desde la recepción en el CAT.
    /// </summary>
    [HttpPost("batch")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> RegistrarBatch(
        [FromBody] RegistrarFaenamientoBatchDto dto)
    {
        var validacion = await batchValidator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

        try
        {
            var resultado = await service.RegistrarBatchAsync(dto);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Obtiene el faenamiento de un lote por su Id de lote.
    /// </summary>
    [HttpGet("lote/{loteId:int}")]
    public async Task<IActionResult> ObtenerPorLoteId(int loteId)
    {
        var resultado = await service.ObtenerPorLoteIdAsync(loteId);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>
    /// Obtiene el faenamiento por código de lote (ej: PAT-20260628-001).
    /// </summary>
    [HttpGet("lote/codigo/{codigoLote}")]
    public async Task<IActionResult> ObtenerPorCodigoLote(string codigoLote)
    {
        var resultado = await service.ObtenerPorCodigoLoteAsync(codigoLote);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>
    /// Lista faenamientos con filtro opcional por rango de fechas.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var resultado = await service.ListarAsync(desde, hasta);
        return Ok(resultado);
    }

    /// <summary>
    /// Retorna los datos formateados para el codificador Ink Jet — RF-305.
    /// Incluye código de lote, fecha de faenamiento, vencimiento y datos de origen.
    /// </summary>
    [HttpGet("inkjet/{codigoLote}")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ObtenerDatosInkJet(string codigoLote)
    {
        var resultado = await service.ObtenerDatosInkJetAsync(codigoLote);
        return resultado is null
            ? NotFound(new { mensaje = $"No se encontró faenamiento para el lote {codigoLote}." })
            : Ok(resultado);
    }

    /// <summary>
    /// Lotes faenados con saldo despachable y sus animales disponibles.
    /// Un lote totalmente despachado deja de aparecer aquí.
    /// </summary>
    [HttpGet("despachos/disponibles")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ListarDespachables()
    {
        var resultado = await service.ListarDespachablesAsync();
        return Ok(resultado);
    }

    /// <summary>
    /// Registra el despacho de animales específicos de un lote faenado
    /// a un cliente. Cada animal solo puede despacharse una vez.
    /// </summary>
    [HttpPost("despachos")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> RegistrarDespacho([FromBody] RegistrarDespachoDto dto)
    {
        var validacion = await despachoValidator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

        try
        {
            var resultado = await service.RegistrarDespachoAsync(dto);
            return CreatedAtAction(
                nameof(ListarDespachos), null, resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Registra la devolución de producto por parte de un cliente — RF-307.
    /// Queda vinculada al lote de origen y a su productora.
    /// </summary>
    [HttpPost("devoluciones")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> RegistrarDevolucion([FromBody] RegistrarDevolucionDto dto)
    {
        try
        {
            var resultado = await service.RegistrarDevolucionAsync(dto);
            return CreatedAtAction(
                nameof(ListarDevoluciones),
                new { productoraId = (int?)null },
                resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Lista devoluciones con filtros por fecha y productora — RF-307.
    /// </summary>
    [HttpGet("devoluciones")]
    public async Task<IActionResult> ListarDevoluciones(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int? productoraId)
    {
        var resultado = await service.ListarDevolucionesAsync(desde, hasta, productoraId);
        return Ok(resultado);
    }

    /// <summary>
    /// Lista los retornos de cuyes a sus productoras de origen
    /// (animales marcados como no aptos durante el faenamiento).
    /// </summary>
    [HttpGet("retornos")]
    public async Task<IActionResult> ListarRetornos(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int? productoraId)
    {
        var resultado = await service.ListarRetornosAsync(desde, hasta, productoraId);
        return Ok(resultado);
    }

    /// <summary>
    /// Historial completo de despachos con filtro opcional por rango de fechas.
    /// </summary>
    [HttpGet("despachos")]
    public async Task<IActionResult> ListarDespachos(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var resultado = await service.ListarDespachosAsync(desde, hasta);
        return Ok(resultado);
    }
}
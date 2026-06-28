using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Recepcion.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecepcionController(IRecepcionService service) : ControllerBase
{
    /// <summary>
    /// Registra un nuevo lote de cuyes en un CAT.
    /// Aplica automáticamente las reglas de peso, color, edad y ayuno.
    /// </summary>
    [HttpPost("lotes")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> RegistrarLote([FromBody] RegistrarLoteDto dto)
    {
        try
        {
            var resultado = await service.RegistrarLoteAsync(dto);
            return CreatedAtAction(
                nameof(ObtenerLotePorId),
                new { id = resultado.Id },
                resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Obtiene un lote por su Id interno.
    /// </summary>
    [HttpGet("lotes/{id:int}")]
    public async Task<IActionResult> ObtenerLotePorId(int id)
    {
        var resultado = await service.ObtenerLotePorIdAsync(id);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>
    /// Obtiene un lote por su código único (ej: PAT-20260615-001).
    /// Usado por el módulo QR y por el faenamiento.
    /// </summary>
    [HttpGet("lotes/codigo/{codigo}")]
    public async Task<IActionResult> ObtenerLotePorCodigo(string codigo)
    {
        var resultado = await service.ObtenerLotePorCodigoAsync(codigo);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>
    /// Lista lotes con filtros opcionales por CAT, estado y rango de fechas.
    /// </summary>
    [HttpGet("lotes")]
    public async Task<IActionResult> ListarLotes(
        [FromQuery] CentroAcopio? cat,
        [FromQuery] EstadoLote? estado,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var resultado = await service.ListarLotesAsync(cat, estado, desde, hasta);
        return Ok(resultado);
    }

    /// <summary>
    /// Endpoint de sincronización offline — RF-211.
    /// Recibe un batch de lotes capturados sin internet en el CAT
    /// y los registra en la base de datos central.
    /// </summary>
    [HttpPost("sync")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> SincronizarOffline([FromBody] SyncLotesDto dto)
    {
        var resultado = await service.SincronizarOfflineAsync(dto);
        return Ok(resultado);
    }
}
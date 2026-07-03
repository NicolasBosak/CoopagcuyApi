using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Recepcion.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecepcionController(
    IRecepcionService service,
    IGuiaMovilizacionService guiaService,
    IMovilizacionService movilizacionService) : ControllerBase
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
    /// Genera la guía de movilización del lote en PDF — RF-210.
    /// Documento imprimible que acompaña el transporte del lote
    /// desde el CAT hasta la planta de faenamiento.
    /// </summary>
    [HttpGet("lotes/{codigoLote}/guia")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> GuiaMovilizacion(string codigoLote)
    {
        try
        {
            var bytes = await guiaService.GenerarGuiaPdfAsync(codigoLote);
            return File(bytes, "application/pdf", $"Guia-{codigoLote}.pdf");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
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

    // ── Movilización CAT → planta (eslabón transporte) ────────────────

    /// <summary>
    /// Registra la movilización del lote hacia la planta de faenamiento:
    /// conductor, condiciones de transporte y declaración de tratamientos.
    /// </summary>
    [HttpPost("lotes/{codigoLote}/movilizacion")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> RegistrarMovilizacion(
        string codigoLote, [FromBody] RegistrarMovilizacionDto dto)
    {
        try
        {
            var resultado = await movilizacionService.RegistrarAsync(codigoLote, dto);
            return CreatedAtAction(
                nameof(ObtenerMovilizacionPorLote),
                new { codigoLote },
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
    /// Confirma la llegada del lote a la planta de faenamiento.
    /// Cierra el eslabón documental CAT → transportista → planta.
    /// </summary>
    [HttpPatch("movilizaciones/{id:int}/recepcion")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ConfirmarRecepcionPlanta(
        int id, [FromBody] ConfirmarRecepcionPlantaDto dto)
    {
        try
        {
            var resultado = await movilizacionService.ConfirmarRecepcionAsync(id, dto);
            return resultado is null ? NotFound() : Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Lista movilizaciones; con pendientes=true solo las que
    /// aún no confirman recepción en planta.
    /// </summary>
    [HttpGet("movilizaciones")]
    public async Task<IActionResult> ListarMovilizaciones(
        [FromQuery] bool? pendientes)
    {
        var resultado = await movilizacionService.ListarAsync(pendientes);
        return Ok(resultado);
    }

    /// <summary>
    /// Obtiene la movilización de un lote por su código.
    /// </summary>
    [HttpGet("lotes/{codigoLote}/movilizacion")]
    public async Task<IActionResult> ObtenerMovilizacionPorLote(string codigoLote)
    {
        var resultado = await movilizacionService.ObtenerPorCodigoLoteAsync(codigoLote);
        return resultado is null ? NotFound() : Ok(resultado);
    }
}
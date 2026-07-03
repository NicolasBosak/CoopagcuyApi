using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Features.Productoras.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Productoras.Controllers;

/// <summary>
/// Registro digital de pagos a productoras en el CAT.
/// Reemplaza el cuaderno manual identificado en el diagnóstico.
/// </summary>
[ApiController]
[Route("api/pagos")]
[Authorize(Roles = "OperadorCAT,AdminCooperativa,AdminTecnico")]
public class PagosController(IPagoService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] RegistrarPagoDto dto)
    {
        try
        {
            var resultado = await service.RegistrarAsync(dto);
            return CreatedAtAction(nameof(Listar), null, resultado);
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

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? productoraId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var resultado = await service.ListarAsync(productoraId, desde, hasta);
        return Ok(resultado);
    }
}

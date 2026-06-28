using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Features.Productoras.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Productoras.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // todos los endpoints requieren JWT
public class ProductorasController(IProductoraService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorCAT")]
    public async Task<IActionResult> Listar(
        [FromQuery] string? comunidad,
        [FromQuery] string? cat)
    {
        var result = await service.ObtenerTodasAsync(comunidad, cat);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var result = await service.ObtenerPorIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> Crear([FromBody] CrearProductoraDto dto)
    {
        var result = await service.CrearAsync(dto);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] CrearProductoraDto dto)
    {
        var ok = await service.ActualizarAsync(id, dto);
        return ok ? NoContent() : NotFound();
    }
}

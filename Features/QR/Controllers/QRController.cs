using CoopagcuyApi.Features.QR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoopagcuyApi.Features.QR.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QRController(IQRService service) : ControllerBase
{
    /// <summary>
    /// Genera el código QR de un lote — RF-401.
    /// Requiere que el lote tenga faenamiento registrado.
    /// </summary>
    [HttpPost("{codigoLote}")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> Generar(string codigoLote)
    {
        try
        {
            var resultado = await service.GenerarQRAsync(codigoLote);
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
    /// Consulta el QR registrado de un lote.
    /// </summary>
    [HttpGet("{codigoLote}")]
    [Authorize]
    public async Task<IActionResult> Obtener(string codigoLote)
    {
        var resultado = await service.ObtenerPorCodigoLoteAsync(codigoLote);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>
    /// Descarga el PNG del QR para impresión en etiqueta — RF-405.
    /// </summary>
    [HttpGet("{codigoLote}/png")]
    [Authorize]
    public async Task<IActionResult> DescargarPng(string codigoLote)
    {
        var bytes = await service.DescargarQRPngAsync(codigoLote);
        if (bytes is null) return NotFound();

        return File(bytes, "image/png", $"QR-{codigoLote}.png");
    }

    /// <summary>
    /// Página pública del producto — RF-402.
    /// Sin autenticación: accesible desde cualquier smartphone con cámara.
    /// Este endpoint es el destino del QR que escanea el consumidor final.
    /// </summary>
    [HttpGet("publico/{codigoLote}")]
    [AllowAnonymous]
    // Anónimo y con códigos enumerables: el rate limit por IP frena el
    // scraping masivo sin afectar a un consumidor que escanea su compra
    [EnableRateLimiting("publico")]
    public async Task<IActionResult> PaginaPublica(string codigoLote)
    {
        var datos = await service.ObtenerPaginaPublicaAsync(codigoLote);
        if (datos is null)
            return NotFound(new { mensaje = "Lote no encontrado o QR no activo." });

        // Devolver JSON — el frontend React renderiza la página visual
        // En producción esta URL vive en Azure Static Web Apps
        return Ok(datos);
    }
}
using CoopagcuyApi.Features.Reportes.DTOs;
using CoopagcuyApi.Features.Reportes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Reportes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "AdminCooperativa,AdminTecnico")]
public class ReportesController(IReportesService service) : ControllerBase
{
    /// <summary>
    /// Panel de control con indicadores clave — RF-508.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var resultado = await service.ObtenerDashboardAsync(desde, hasta);
        return Ok(resultado);
    }

    /// <summary>
    /// Reporte de producción por productora — RF-501.
    /// </summary>
    [HttpGet("productoras")]
    public async Task<IActionResult> PorProductora(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.ReportePorProductoraAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Reporte de volumen por CAT — RF-502.
    /// </summary>
    [HttpGet("cat")]
    public async Task<IActionResult> PorCAT(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.ReportePorCATAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Reporte de novedades registradas — RF-503.
    /// </summary>
    [HttpGet("novedades")]
    public async Task<IActionResult> Novedades(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.ReporteNovedadesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Exporta reporte de productoras a Excel — RF-505.
    /// </summary>
    [HttpGet("exportar/excel/productoras")]
    public async Task<IActionResult> ExcelProductoras(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelProductorasAsync(
            new FiltroPeriodoDto(desde, hasta, cat));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Productoras-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta reporte de novedades a Excel — RF-505.
    /// </summary>
    [HttpGet("exportar/excel/novedades")]
    public async Task<IActionResult> ExcelNovedades(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelNovedadesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Novedades-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta la ficha de trazabilidad de un lote en PDF — RF-505.
    /// </summary>
    [HttpGet("exportar/pdf/lote/{codigoLote}")]
    public async Task<IActionResult> PDFLote(string codigoLote)
    {
        try
        {
            var bytes = await service.ExportarPDFLoteAsync(codigoLote);
            return File(bytes, "application/pdf",
                $"Ficha-{codigoLote}.pdf");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
    }
}
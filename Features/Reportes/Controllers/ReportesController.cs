using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Reportes.DTOs;
using CoopagcuyApi.Features.Reportes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Reportes.Controllers;

// El dashboard es la pantalla de aterrizaje de TODOS los roles; los
// reportes detallados y exportaciones siguen siendo de administradores
// (restricción por acción, ya que los atributos de rol no se relajan
// a nivel de clase)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportesController(IReportesService service) : ControllerBase
{
    /// <summary>
    /// Panel de control con indicadores clave — RF-508.
    /// Accesible para todos los roles: un Operador de CAT recibe los
    /// indicadores de recepción de su propio centro.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        CentroAcopio? catOperador = null;
        if (User.IsInRole("OperadorCAT") &&
            Enum.TryParse<CentroAcopio>(
                User.FindFirst("cat")?.Value, out var cat))
            catOperador = cat;

        var resultado = await service.ObtenerDashboardAsync(
            desde, hasta, catOperador);
        return Ok(resultado);
    }

    /// <summary>
    /// Reporte de producción por productora — RF-501.
    /// </summary>
    [HttpGet("productoras")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
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
    /// Reporte individual por cuy: estado de cada animal registrado.
    /// </summary>
    [HttpGet("cuyes")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> PorCuy(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.ReporteCuyesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Reporte de devoluciones de clientes y retornos a productoras.
    /// </summary>
    [HttpGet("devoluciones")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> Devoluciones(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.ReporteDevolucionesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Exporta el detalle individual por cuy a Excel.
    /// </summary>
    [HttpGet("exportar/excel/cuyes")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ExcelCuyes(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelCuyesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Detalle-Cuyes-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta reporte de productoras a Excel — RF-505.
    /// </summary>
    [HttpGet("exportar/excel/productoras")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
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
    /// Exporta el reporte por centro de acopio a Excel — RF-505.
    /// </summary>
    [HttpGet("exportar/excel/cat")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ExcelCAT(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelCATAsync(
            new FiltroPeriodoDto(desde, hasta, cat));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-CAT-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta devoluciones de clientes y retornos a productoras a
    /// Excel (dos hojas) — RF-505.
    /// </summary>
    [HttpGet("exportar/excel/devoluciones")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ExcelDevoluciones(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelDevolucionesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Devoluciones-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta la ficha de trazabilidad de un lote en PDF — RF-505.
    /// </summary>
    [HttpGet("exportar/pdf/lote/{codigoLote}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
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
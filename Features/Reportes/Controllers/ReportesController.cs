using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Reportes.DTOs;
using CoopagcuyApi.Features.Reportes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Reportes.Controllers;

// Cada acción declara sus roles: los atributos de rol no se relajan a nivel de
// clase, así que un endpoint sin lista propia queda abierto a cualquier usuario
// autenticado. Dos ejes de restricción conviven aquí:
//   · El dashboard y los reportes del flujo físico (entrada/tránsito/salida)
//     son operación: no los ve el admin técnico.
//   · Los reportes de gestión y calidad sí los ve, porque atiende consultas
//     sobre ellos.
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
    // Lista explícita y no el [Authorize] de clase: el panel es la portada de
    // la operación y el admin técnico ya no opera. Sin esta línea seguiría
    // alcanzándolo con su token aunque el menú no se lo enseñe.
    [Authorize(Roles = "AdminCooperativa,OperadorCAT,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    /// Ganancias de productoras — lo que cobraron, por productora.
    /// Cobrado en venta local, pactado a cuotas y pagado por la planta van
    /// en columnas separadas porque no son dinero disponible del mismo modo.
    /// </summary>
    [HttpGet("ganancias/productoras")]
    // Ampliado a propósito respecto de la petición original, que solo
    // nombraba a los dos administradores: el operador de faenamiento
    // atiende consultas sobre este reporte igual que sobre los demás
    // reportes de gestión. El OperadorCAT no entra.
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> GananciasPorProductora(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.GananciasPorProductoraAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Ganancias de productoras — lo que cobraron, agrupado por CAT.
    /// </summary>
    [HttpGet("ganancias/cat")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> GananciasPorCat(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.GananciasPorCatAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Ganancias de productoras — lo que cobraron, agrupado por mes local
    /// del piloto.
    /// </summary>
    [HttpGet("ganancias/mes")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> GananciasPorMes(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var resultado = await service.GananciasPorMesAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return Ok(resultado);
    }

    /// <summary>
    /// Margen de la reventa por mes local del piloto. NUNCA se suma con las
    /// ganancias de productoras: un pago a una productora es ingreso para
    /// ella y costo para la cooperativa, la misma fila leída desde dos
    /// lados.
    ///
    /// Sin filtro por CAT — a propósito, no un olvido: un despacho reúne
    /// animales de varias jaulas y por tanto de varios CAT (ver el
    /// comentario en <c>ReportesService.DatosDeMargenAsync</c>).
    /// </summary>
    [HttpGet("margen/mes")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> MargenPorMes(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta)
    {
        var resultado = await service.MargenPorMesAsync(
            new FiltroPeriodoDto(desde, hasta));
        return Ok(resultado);
    }

    /// <summary>
    /// Margen de la reventa por cliente de destino, normalizado.
    ///
    /// Sin filtro por CAT, mismo motivo que <see cref="MargenPorMes"/>.
    /// </summary>
    [HttpGet("margen/cliente")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> MargenPorCliente(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta)
    {
        var resultado = await service.MargenPorClienteAsync(
            new FiltroPeriodoDto(desde, hasta));
        return Ok(resultado);
    }

    /// <summary>
    /// Reporte individual por cuy: estado de cada animal registrado.
    /// </summary>
    [HttpGet("cuyes")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    /// Entrada: cuyes que llegaron a planta, vivos, aún sin faenar.
    /// </summary>
    [HttpGet("entrada")]
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> Entrada(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
        => Ok(await service.ReporteEntradaAsync(
            new FiltroPeriodoDto(desde, hasta, cat)));

    /// <summary>
    /// Tránsito: lotes faenados completos en el período.
    /// </summary>
    [HttpGet("transito")]
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> Transito(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
        => Ok(await service.ReporteTransitoAsync(
            new FiltroPeriodoDto(desde, hasta, cat)));

    /// <summary>
    /// Salida: despachos comerciales con datos de transporte.
    /// </summary>
    [HttpGet("salida")]
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> Salida(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
        => Ok(await service.ReporteSalidaAsync(
            new FiltroPeriodoDto(desde, hasta, cat)));

    [HttpGet("exportar/excel/entrada")]
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ExcelEntrada(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelEntradaAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Entrada-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Todos los dashboards del período en un solo libro, una hoja por cada
    /// uno. Complementa —no reemplaza— las descargas individuales.
    /// </summary>
    [HttpGet("exportar/excel/general")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> ExcelGeneral(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        // La decisión de permisos se toma aquí, no en el servicio: el servicio
        // arma libros, no sabe de roles.
        var incluirFlujoOperativo = !User.IsInRole("AdminTecnico");

        var bytes = await service.ExportarExcelGeneralAsync(
            new FiltroPeriodoDto(desde, hasta, cat), incluirFlujoOperativo);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-General-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    [HttpGet("exportar/excel/transito")]
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ExcelTransito(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelTransitoAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Transito-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    [HttpGet("exportar/excel/salida")]
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ExcelSalida(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelSalidaAsync(
            new FiltroPeriodoDto(desde, hasta, cat));
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Salida-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta el reporte por centro de acopio a Excel — RF-505.
    /// </summary>
    [HttpGet("exportar/excel/cat")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
    /// Exporta el reporte de ganancias a Excel — RF-505. Cinco hojas: las
    /// tres primeras (por CAT, por productora, por mes) sí respetan
    /// ?cat=; las dos de margen (por mes, por cliente) no, porque un
    /// despacho reúne animales de varias CAT (ver el comentario en
    /// <c>ReportesService.ExportarExcelGananciasAsync</c>).
    /// </summary>
    [HttpGet("exportar/excel/ganancias")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> ExcelGanancias(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        var bytes = await service.ExportarExcelGananciasAsync(
            new FiltroPeriodoDto(desde, hasta, cat));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-Ganancias-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Exporta la ficha de trazabilidad de un lote en PDF — RF-505.
    /// </summary>
    [HttpGet("exportar/pdf/lote/{codigoLote}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
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
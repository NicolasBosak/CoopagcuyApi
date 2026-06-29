using ClosedXML.Excel;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Reportes.DTOs;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CoopagcuyApi.Features.Reportes.Services;

public interface IReportesService
{
    Task<DashboardDto> ObtenerDashboardAsync(DateTime? desde, DateTime? hasta);
    Task<IEnumerable<ReporteProductoraDto>> ReportePorProductoraAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteCATDto>> ReportePorCATAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteNovedadDto>> ReporteNovedadesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelProductorasAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelNovedadesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarPDFLoteAsync(string codigoLote);
}

public class ReportesService(AppDbContext db) : IReportesService
{
    // ── Dashboard — RF-508 ────────────────────────────────────────────

    public async Task<DashboardDto> ObtenerDashboardAsync(
        DateTime? desde, DateTime? hasta)
    {
        var desdeUtc = desde.HasValue
            ? DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc)
            : DateTime.UtcNow.AddDays(-30);

        var hastaUtc = hasta.HasValue
            ? DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        var lotes = await db.Lotes
            .Where(l => l.FechaRecepcion >= desdeUtc &&
                        l.FechaRecepcion <= hastaUtc)
            .ToListAsync();

        var total = lotes.Count;

        return new DashboardDto(
            LotesActivos: total,
            AnimalesRecibidosPeriodo: lotes.Sum(l => l.CantidadAnimales),
            TasaAceptacion: total == 0 ? 0 : Math.Round(
                (decimal)lotes.Count(l => l.Estado == EstadoLote.Aceptado)
                / total * 100, 1),
            TasaConNovedad: total == 0 ? 0 : Math.Round(
                (decimal)lotes.Count(l => l.Estado == EstadoLote.ConNovedad)
                / total * 100, 1),
            TasaRechazado: total == 0 ? 0 : Math.Round(
                (decimal)lotes.Count(l => l.Estado == EstadoLote.Rechazado)
                / total * 100, 1),
            LotesConQR: await db.CodigosQR.CountAsync(q => q.Activo),
            TotalProductoras: await db.Productoras.CountAsync(p => p.Activa),
            TotalFaenamientos: await db.Faenamientos.CountAsync(),
            FechaCorte: hastaUtc
        );
    }

    // ── Reporte por productora — RF-501 ───────────────────────────────

    public async Task<IEnumerable<ReporteProductoraDto>> ReportePorProductoraAsync(
        FiltroPeriodoDto filtro)
    {
        var desdeUtc = DateTime.SpecifyKind(filtro.Desde, DateTimeKind.Utc);
        var hastaUtc = DateTime.SpecifyKind(filtro.Hasta, DateTimeKind.Utc);

        var query = db.Lotes
            .Include(l => l.Productora)
            .Where(l => l.FechaRecepcion >= desdeUtc &&
                        l.FechaRecepcion <= hastaUtc);

        if (!string.IsNullOrEmpty(filtro.CentroAcopio) &&
            Enum.TryParse<CentroAcopio>(filtro.CentroAcopio, out var cat))
            query = query.Where(l => l.CentroAcopio == cat);

        var lotes = await query.ToListAsync();

        return lotes
            .GroupBy(l => l.Productora)
            .Select(g => new ReporteProductoraDto(
                ProductoraId: g.Key.Id,
                NombreProductora: g.Key.NombreCompleto,
                Comunidad: g.Key.Comunidad,
                CentroAcopio: g.Key.CatAsignado.ToString(),
                TotalLotes: g.Count(),
                TotalAnimales: g.Sum(l => l.CantidadAnimales),
                LotesAceptados: g.Count(l => l.Estado == EstadoLote.Aceptado),
                LotesConNovedad: g.Count(l => l.Estado == EstadoLote.ConNovedad),
                LotesRechazados: g.Count(l => l.Estado == EstadoLote.Rechazado),
                PesoTotalGramos: g.Sum(l => l.PesoTotalGramos),
                PesoPromedioGramos: g.Any()
                    ? Math.Round(g.Average(l => l.PesoTotalGramos /
                        (l.CantidadAnimales == 0 ? 1 : l.CantidadAnimales)), 0)
                    : 0,
                UltimaEntrega: g.Max(l => (DateTime?)l.FechaRecepcion)
            ))
            .OrderByDescending(r => r.TotalLotes);
    }

    // ── Reporte por CAT — RF-502 ──────────────────────────────────────

    public async Task<IEnumerable<ReporteCATDto>> ReportePorCATAsync(
        FiltroPeriodoDto filtro)
    {
        var desdeUtc = DateTime.SpecifyKind(filtro.Desde, DateTimeKind.Utc);
        var hastaUtc = DateTime.SpecifyKind(filtro.Hasta, DateTimeKind.Utc);

        var lotes = await db.Lotes
            .Where(l => l.FechaRecepcion >= desdeUtc &&
                        l.FechaRecepcion <= hastaUtc)
            .ToListAsync();

        return lotes
            .GroupBy(l => l.CentroAcopio)
            .Select(g =>
            {
                var total = g.Count();
                var aceptados = g.Count(l => l.Estado == EstadoLote.Aceptado);
                return new ReporteCATDto(
                    CentroAcopio: g.Key.ToString(),
                    TotalLotes: total,
                    TotalAnimales: g.Sum(l => l.CantidadAnimales),
                    LotesAceptados: aceptados,
                    LotesConNovedad: g.Count(l => l.Estado == EstadoLote.ConNovedad),
                    LotesRechazados: g.Count(l => l.Estado == EstadoLote.Rechazado),
                    TasaAceptacion: total == 0 ? 0 :
                        Math.Round((decimal)aceptados / total * 100, 1),
                    PesoTotalGramos: g.Sum(l => l.PesoTotalGramos)
                );
            })
            .OrderBy(r => r.CentroAcopio);
    }

    // ── Reporte de novedades — RF-503 ─────────────────────────────────

    public async Task<IEnumerable<ReporteNovedadDto>> ReporteNovedadesAsync(
        FiltroPeriodoDto filtro)
    {
        var desdeUtc = DateTime.SpecifyKind(filtro.Desde, DateTimeKind.Utc);
        var hastaUtc = DateTime.SpecifyKind(filtro.Hasta, DateTimeKind.Utc);

        var query = db.Novedades
            .Include(n => n.Lote).ThenInclude(l => l.Productora)
            .Where(n => n.FechaRegistro >= desdeUtc &&
                        n.FechaRegistro <= hastaUtc);

        if (!string.IsNullOrEmpty(filtro.CentroAcopio) &&
            Enum.TryParse<CentroAcopio>(filtro.CentroAcopio, out var cat))
            query = query.Where(n => n.Lote.CentroAcopio == cat);

        return await query
            .OrderByDescending(n => n.FechaRegistro)
            .Select(n => new ReporteNovedadDto(
                n.Id,
                n.Lote.CodigoLote,
                n.Lote.Productora.NombreCompleto,
                n.Lote.Productora.Comunidad,
                n.Lote.CentroAcopio.ToString(),
                n.Tipo.ToString(),
                n.Descripcion,
                n.PesoRegistradoGramos,
                n.FechaRegistro,
                n.RegistradoPor
            ))
            .ToListAsync();
    }

    // ── Exportar Excel productoras — RF-505 ───────────────────────────

    public async Task<byte[]> ExportarExcelProductorasAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReportePorProductoraAsync(filtro);

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Productoras");

        // Encabezado
        var encabezados = new[]
        {
            "Productora", "Comunidad", "CAT", "Total Lotes",
            "Total Animales", "Aceptados", "Con Novedad",
            "Rechazados", "Peso Total (kg)", "Peso Promedio (g)",
            "Última Entrega"
        };

        for (int i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
            celda.Style.Font.FontColor = XLColor.White;
        }

        // Datos
        int fila = 2;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.NombreProductora;
            hoja.Cell(fila, 2).Value = r.Comunidad;
            hoja.Cell(fila, 3).Value = r.CentroAcopio;
            hoja.Cell(fila, 4).Value = r.TotalLotes;
            hoja.Cell(fila, 5).Value = r.TotalAnimales;
            hoja.Cell(fila, 6).Value = r.LotesAceptados;
            hoja.Cell(fila, 7).Value = r.LotesConNovedad;
            hoja.Cell(fila, 8).Value = r.LotesRechazados;
            hoja.Cell(fila, 9).Value = Math.Round(r.PesoTotalGramos / 1000, 2);
            hoja.Cell(fila, 10).Value = r.PesoPromedioGramos;
            hoja.Cell(fila, 11).Value = r.UltimaEntrega?.ToString("dd/MM/yyyy") ?? "-";
            fila++;
        }

        hoja.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Exportar Excel novedades — RF-505 ─────────────────────────────

    public async Task<byte[]> ExportarExcelNovedadesAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReporteNovedadesAsync(filtro);

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Novedades");

        var encabezados = new[]
        {
            "Código Lote", "Productora", "Comunidad", "CAT",
            "Tipo Novedad", "Descripción", "Peso Registrado (g)",
            "Fecha Registro", "Registrado Por"
        };

        for (int i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#B71C1C");
            celda.Style.Font.FontColor = XLColor.White;
        }

        int fila = 2;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.CodigoLote;
            hoja.Cell(fila, 2).Value = r.NombreProductora;
            hoja.Cell(fila, 3).Value = r.Comunidad;
            hoja.Cell(fila, 4).Value = r.CentroAcopio;
            hoja.Cell(fila, 5).Value = r.TipoNovedad;
            hoja.Cell(fila, 6).Value = r.Descripcion;
            hoja.Cell(fila, 7).Value = r.PesoRegistradoGramos?.ToString() ?? "-";
            hoja.Cell(fila, 8).Value = r.FechaRegistro.ToString("dd/MM/yyyy HH:mm");
            hoja.Cell(fila, 9).Value = r.RegistradoPor;
            fila++;
        }

        hoja.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Exportar PDF de ficha de lote — RF-505 ────────────────────────

    public async Task<byte[]> ExportarPDFLoteAsync(string codigoLote)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Faenamiento)
            .Include(l => l.CodigoQR)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

        var promedio = lote.Faenamiento?.UnidadesFaenadas > 0
            ? lote.Faenamiento!.PesoTotalCanalGramos / lote.Faenamiento.UnidadesFaenadas
            : 0;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("COOPAGCUY — Cuy Azuayito")
                                .FontSize(16).Bold().FontColor("#2E7D32");
                            c.Item().Text("Ficha de Trazabilidad de Lote")
                                .FontSize(11).FontColor("#555555");
                        });
                        row.ConstantItem(120).AlignRight().Column(c =>
                        {
                            c.Item().Text(codigoLote)
                                .FontSize(13).Bold().FontColor("#B71C1C");
                            c.Item().Text(DateTime.Now.ToString("dd/MM/yyyy"))
                                .FontSize(9).FontColor("#777777");
                        });
                    });
                    col.Item().PaddingTop(4)
                        .BorderBottom(1).BorderColor("#2E7D32");
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    // Sección: Datos del productor
                    col.Item().Background("#F1F8E9").Padding(8).Column(c =>
                    {
                        c.Item().Text("DATOS DE ORIGEN")
                            .FontSize(9).Bold().FontColor("#2E7D32");
                        c.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Productora: {lote.Productora.NombreCompleto}");
                            r.RelativeItem().Text(
                                $"Comunidad: {lote.Productora.Comunidad}");
                        });
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Cantón: {lote.Productora.Canton}");
                            r.RelativeItem().Text(
                                $"CAT: {lote.CentroAcopio}");
                        });
                    });

                    col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Column(c =>
                    {
                        c.Item().Text("RECEPCIÓN EN CAT")
                            .FontSize(9).Bold().FontColor("#1565C0");
                        c.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Fecha: {lote.FechaRecepcion:dd/MM/yyyy}");
                            r.RelativeItem().Text(
                                $"Animales: {lote.CantidadAnimales}");
                        });
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Peso total: {lote.PesoTotalGramos:N0}g");
                            r.RelativeItem().Text(
                                $"Estado: {lote.Estado}");
                        });
                        c.Item().Text(
                            $"Responsable: {lote.ResponsableRecepcion ?? "-"}");
                    });

                    // Novedades
                    if (lote.Novedades.Count > 0)
                    {
                        col.Item().PaddingTop(8).Background("#FFF8E1").Padding(8).Column(c =>
                        {
                            c.Item().Text("NOVEDADES REGISTRADAS")
                                .FontSize(9).Bold().FontColor("#E65100");
                            foreach (var n in lote.Novedades)
                            {
                                c.Item().PaddingTop(2).Text(
                                    $"• [{n.Tipo}] {n.Descripcion}");
                            }
                        });
                    }

                    // Faenamiento
                    if (lote.Faenamiento is not null)
                    {
                        col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Column(c =>
                        {
                            c.Item().Text("FAENAMIENTO — Sulupali Chico, Santa Isabel")
                                .FontSize(9).Bold().FontColor("#1565C0");
                            c.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Fecha: {lote.Faenamiento.FechaFaenamiento:dd/MM/yyyy}");
                                r.RelativeItem().Text(
                                    $"Unidades: {lote.Faenamiento.UnidadesFaenadas}");
                            });
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Peso canal total: {lote.Faenamiento.PesoTotalCanalGramos:N0}g");
                                r.RelativeItem().Text(
                                    $"Peso promedio: {promedio:N0}g");
                            });
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Estado canal: {lote.Faenamiento.EstadoCanal}");
                                r.RelativeItem().Text(
                                    $"Temperatura: {lote.Faenamiento.TemperaturaAlmacenamiento}°C");
                            });
                            c.Item().Text(
                                $"Operario: {lote.Faenamiento.OperarioResponsable}");
                        });
                    }

                    // Footer de trazabilidad
                    col.Item().PaddingTop(16).BorderTop(1).BorderColor("#CCCCCC")
                        .PaddingTop(8).Column(c =>
                        {
                            c.Item().Text(
                                "Este documento certifica la trazabilidad del lote indicado " +
                                "conforme al Sistema Cuy Azuayito — COOPAGCUY.")
                                .FontSize(8).FontColor("#777777").Italic();
                            c.Item().Text(
                                "Proyecto Familias Campesinas Liderando — " +
                                "Financiado por la Comisión Europea · Ayuda en Acción")
                                .FontSize(7).FontColor("#AAAAAA");
                        });
                });
            });
        }).GeneratePdf();
    }
}
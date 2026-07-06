using ClosedXML.Excel;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Reportes.DTOs;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CoopagcuyApi.Features.Reportes.Services;

public interface IReportesService
{
    // Con cat: indicadores del centro de acopio del operador; sin cat:
    // vista global de la cadena (administradores y planta)
    Task<DashboardDto> ObtenerDashboardAsync(
        DateTime? desde, DateTime? hasta, CentroAcopio? cat = null);
    Task<IEnumerable<ReporteProductoraDto>> ReportePorProductoraAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteCATDto>> ReportePorCATAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteNovedadDto>> ReporteNovedadesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelProductorasAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelNovedadesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarPDFLoteAsync(string codigoLote);
    Task<IEnumerable<ReporteCuyDto>> ReporteCuyesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelCuyesAsync(FiltroPeriodoDto filtro);
    Task<ReporteDevolucionesDto> ReporteDevolucionesAsync(FiltroPeriodoDto filtro);
}

public class ReportesService(AppDbContext db) : IReportesService
{
    // El filtro llega como fecha sin hora ("2026-07-03"): el límite
    // superior debe cubrir el día completo, no cortar a medianoche.
    // Devuelve (desde inclusivo, hasta exclusivo = día siguiente 00:00).
    private static (DateTime desdeUtc, DateTime hastaExclusivoUtc) RangoUtc(
        FiltroPeriodoDto filtro)
    {
        var desde = DateTime.SpecifyKind(filtro.Desde.Date, DateTimeKind.Utc);
        var hasta = DateTime.SpecifyKind(
            filtro.Hasta.Date.AddDays(1), DateTimeKind.Utc);
        return (desde, hasta);
    }

    // ── Dashboard — RF-508 ────────────────────────────────────────────

    public async Task<DashboardDto> ObtenerDashboardAsync(
        DateTime? desde, DateTime? hasta, CentroAcopio? cat = null)
    {
        var desdeUtc = desde.HasValue
            ? DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc)
            : DateTime.UtcNow.AddDays(-30);

        // Límite superior exclusivo: si llega una fecha sin hora debe
        // cubrir el día completo
        var hastaUtc = hasta.HasValue
            ? DateTime.SpecifyKind(hasta.Value.Date.AddDays(1), DateTimeKind.Utc)
            : DateTime.UtcNow.AddDays(1);

        var query = db.Lotes
            .Where(l => l.FechaRecepcion >= desdeUtc &&
                        l.FechaRecepcion < hastaUtc);

        // Un Operador de CAT ve la recepción de su propio centro; los
        // indicadores de cadena (faenamientos, QR) se mantienen globales
        if (cat.HasValue)
            query = query.Where(l => l.CentroAcopio == cat.Value);

        var lotes = await query
            .AsNoTracking()
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
            TotalProductoras: await db.Productoras.CountAsync(p =>
                p.Activa && (cat == null || p.CatAsignado == cat.Value)),
            TotalFaenamientos: await db.Faenamientos.CountAsync(),
            FechaCorte: hastaUtc
        );
    }

    // ── Reporte por productora — RF-501 ───────────────────────────────
    // Con jaulas compartidas la producción se atribuye por animal: cada
    // cuy cuenta para la productora que lo entregó, no para el lote.

    public async Task<IEnumerable<ReporteProductoraDto>> ReportePorProductoraAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var query = db.CuyRegistros
            .Include(c => c.Productora)
            .Include(c => c.Lote)
            .Where(c => c.Lote.FechaRecepcion >= desdeUtc &&
                        c.Lote.FechaRecepcion < hastaUtc &&
                        c.ProductoraId != null);

        if (!string.IsNullOrEmpty(filtro.CentroAcopio) &&
            Enum.TryParse<CentroAcopio>(filtro.CentroAcopio, out var cat))
            query = query.Where(c => c.Lote.CentroAcopio == cat);

        var cuyes = await query.AsNoTracking().ToListAsync();

        // Agrupar por Id, no por instancia (ver nota en RecepcionService:
        // sin tracking cada fila trae su propio objeto Productora)
        return cuyes
            .GroupBy(c => c.ProductoraId)
            .Select(g =>
            {
                var p = g.First().Productora!;
                return new ReporteProductoraDto(
                    ProductoraId: p.Id,
                    NombreProductora: p.NombreCompleto,
                    Comunidad: p.Comunidad,
                    CentroAcopio: p.CatAsignado.ToString(),
                    TotalLotes: g.Select(c => c.LoteId).Distinct().Count(),
                    TotalAnimales: g.Count(),
                    LotesAceptados: g.Count(c => c.Estado == EstadoLote.Aceptado),
                    LotesConNovedad: g.Count(c => c.Estado == EstadoLote.ConNovedad),
                    LotesRechazados: g.Count(c => c.Estado == EstadoLote.Rechazado),
                    PesoTotalGramos: g.Sum(c => c.PesoGramos),
                    PesoPromedioGramos: Math.Round(g.Average(c => c.PesoGramos), 0),
                    UltimaEntrega: g.Max(c => (DateTime?)c.Lote.FechaRecepcion)
                );
            })
            .OrderByDescending(r => r.TotalAnimales);
    }

    // ── Reporte por CAT — RF-502 ──────────────────────────────────────

    public async Task<IEnumerable<ReporteCATDto>> ReportePorCATAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var lotes = await db.Lotes
            .Where(l => l.FechaRecepcion >= desdeUtc &&
                        l.FechaRecepcion < hastaUtc)
            .AsNoTracking()
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
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var query = db.Novedades
            .Include(n => n.Lote).ThenInclude(l => l.Productora)
            .Where(n => n.FechaRegistro >= desdeUtc &&
                        n.FechaRegistro < hastaUtc);

        if (!string.IsNullOrEmpty(filtro.CentroAcopio) &&
            Enum.TryParse<CentroAcopio>(filtro.CentroAcopio, out var cat))
            query = query.Where(n => n.Lote.CentroAcopio == cat);

        return await query
            .OrderByDescending(n => n.FechaRegistro)
            .Select(n => new ReporteNovedadDto(
                n.Id,
                n.Lote.CodigoLote,
                n.Lote.Productora != null
                    ? n.Lote.Productora.NombreCompleto : "Varias productoras",
                n.Lote.Productora != null
                    ? n.Lote.Productora.Comunidad : "-",
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

    // ── Reporte individual por cuy ────────────────────────────────────

    public async Task<IEnumerable<ReporteCuyDto>> ReporteCuyesAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var query = db.CuyRegistros
            .Include(c => c.Lote).ThenInclude(l => l.Productora)
            .Where(c => c.Lote.FechaRecepcion >= desdeUtc &&
                        c.Lote.FechaRecepcion < hastaUtc);

        if (!string.IsNullOrEmpty(filtro.CentroAcopio) &&
            Enum.TryParse<CentroAcopio>(filtro.CentroAcopio, out var cat))
            query = query.Where(c => c.Lote.CentroAcopio == cat);

        return await query
            .OrderByDescending(c => c.Lote.FechaRecepcion)
            .ThenBy(c => c.Lote.CodigoLote)
            .ThenBy(c => c.NumeroEnLote)
            .Select(c => new ReporteCuyDto(
                c.Lote.CodigoLote,
                // La productora del animal específico; los registros antiguos
                // caen a la productora principal del lote
                c.Productora != null
                    ? c.Productora.NombreCompleto
                    : c.Lote.Productora != null
                        ? c.Lote.Productora.NombreCompleto : string.Empty,
                c.Productora != null
                    ? c.Productora.Comunidad
                    : c.Lote.Productora != null
                        ? c.Lote.Productora.Comunidad : string.Empty,
                c.Lote.CentroAcopio.ToString(),
                c.NumeroEnLote,
                c.PesoGramos,
                c.ColorPelaje,
                c.EstadoOreja,
                c.TamanoAnimal,
                c.Estado.ToString(),
                c.MotivoNovedad,
                c.Lote.FechaRecepcion))
            .ToListAsync();
    }

    public async Task<byte[]> ExportarExcelCuyesAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReporteCuyesAsync(filtro);

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Detalle por cuy");

        var encabezados = new[]
        {
            "Código Lote", "Productora", "Comunidad", "CAT", "Cuy N°",
            "Peso (g)", "Color", "Oreja", "Tamaño", "Estado",
            "Motivo de novedad", "Fecha recepción"
        };

        for (int i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
            celda.Style.Font.FontColor = XLColor.White;
        }

        int fila = 2;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.CodigoLote;
            hoja.Cell(fila, 2).Value = r.NombreProductora;
            hoja.Cell(fila, 3).Value = r.Comunidad;
            hoja.Cell(fila, 4).Value = r.CentroAcopio;
            hoja.Cell(fila, 5).Value = r.NumeroEnLote;
            hoja.Cell(fila, 6).Value = r.PesoGramos;
            hoja.Cell(fila, 7).Value = r.ColorPelaje;
            hoja.Cell(fila, 8).Value = r.EstadoOreja;
            hoja.Cell(fila, 9).Value = r.TamanoAnimal;
            hoja.Cell(fila, 10).Value = r.Estado;
            hoja.Cell(fila, 11).Value = r.MotivoNovedad ?? "-";
            hoja.Cell(fila, 12).Value = r.FechaRecepcion.ToString("dd/MM/yyyy");

            if (r.Estado == "Rechazado")
                hoja.Cell(fila, 10).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");
            else if (r.Estado == "ConNovedad")
                hoja.Cell(fila, 10).Style.Font.FontColor = XLColor.FromHtml("#E65100");

            fila++;
        }

        hoja.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Reporte de devoluciones y retornos a productora ───────────────

    public async Task<ReporteDevolucionesDto> ReporteDevolucionesAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var devQuery = db.Devoluciones
            .Include(d => d.Lote).ThenInclude(l => l.Productora)
            .Where(d => d.FechaDevolucion >= desdeUtc &&
                        d.FechaDevolucion < hastaUtc);

        var retQuery = db.RetornosProductora
            .Include(r => r.Lote).ThenInclude(l => l.Productora)
            .Where(r => r.FechaRetorno >= desdeUtc &&
                        r.FechaRetorno < hastaUtc);

        if (!string.IsNullOrEmpty(filtro.CentroAcopio) &&
            Enum.TryParse<CentroAcopio>(filtro.CentroAcopio, out var cat))
        {
            devQuery = devQuery.Where(d => d.Lote.CentroAcopio == cat);
            retQuery = retQuery.Where(r => r.Lote.CentroAcopio == cat);
        }

        var devoluciones = await devQuery
            .OrderByDescending(d => d.FechaDevolucion)
            .Select(d => new DevolucionItemDto(
                d.Id, d.Lote.CodigoLote,
                d.RegistroFaenamiento != null
                    ? d.RegistroFaenamiento.NumeroSesion : null,
                d.Lote.Productora != null
                    ? d.Lote.Productora.NombreCompleto : "Varias productoras",
                d.Lote.Productora != null
                    ? d.Lote.Productora.Comunidad : "-",
                d.ClienteDevuelve, d.FechaDevolucion,
                d.CantidadUnidades, d.Motivo))
            .ToListAsync();

        var retornos = await retQuery
            .OrderByDescending(r => r.FechaRetorno)
            .Select(r => new RetornoItemDto(
                r.Id, r.Lote.CodigoLote,
                r.Productora.NombreCompleto, r.Productora.Comunidad,
                r.NumeroEnLote, r.Motivo, r.FechaRetorno, r.Responsable))
            .ToListAsync();

        return new ReporteDevolucionesDto(
            TotalDevolucionesClientes: devoluciones.Count,
            TotalUnidadesDevueltas: devoluciones.Sum(d => d.CantidadUnidades),
            TotalRetornosProductora: retornos.Count,
            DevolucionesClientes: devoluciones,
            RetornosProductora: retornos
        );
    }

    // Ficha del lote de producto terminado: agrupa toda la sesión de
    // planta bajo el código FAE con el detalle por comunidad y por animal
    private async Task<byte[]> ExportarPDFLoteFaenadoAsync(string codigo)
    {
        var loteFaenado = await db.LotesFaenados
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Cuyes)
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                .ThenInclude(l => l.Productora)
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                .ThenInclude(l => l.Cuyes).ThenInclude(c => c.Productora)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(lf => lf.Codigo == codigo)
            ?? throw new KeyNotFoundException(
                $"Lote faenado {codigo} no encontrado.");

        // Animales procesados con su origen individual
        var animales = loteFaenado.Sesiones
            .SelectMany(f => f.Cuyes.Select(cf => (
                Faenado: cf,
                Jaula: f.Lote,
                Comunidad: f.Lote.Cuyes
                    .FirstOrDefault(c => c.NumeroEnLote == cf.NumeroEnLote)
                    ?.Productora?.Comunidad
                    ?? f.Lote.Productora?.Comunidad ?? "—")))
            .OrderBy(a => a.Jaula.CodigoLote)
            .ThenBy(a => a.Faenado.NumeroEnLote)
            .ToList();

        var aportes = animales
            .Where(a => a.Faenado.Estado != EstadoCanal.Rechazado)
            .GroupBy(a => a.Comunidad)
            .Select(g => (Comunidad: g.Key, Cantidad: g.Count()))
            .OrderByDescending(x => x.Cantidad)
            .ToList();

        var unidades = loteFaenado.Sesiones.Sum(f => f.UnidadesFaenadas);
        var pesoTotal = loteFaenado.Sesiones.Sum(f => f.PesoTotalCanalGramos);
        var promedio = unidades > 0 ? pesoTotal / unidades : 0;

        byte[]? qrPng = null;
        var qr = await db.CodigosQR.FirstOrDefaultAsync(
            q => q.LoteFaenadoId == loteFaenado.Id && q.Activo);
        if (qr is not null)
        {
            using var generador = new QRCodeGenerator();
            var datos = generador.CreateQrCode(
                qr.UrlPublica, QRCodeGenerator.ECCLevel.Q);
            qrPng = new PngByteQRCode(datos).GetGraphic(10);
        }

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
                            c.Item().Text("Ficha de Lote Faenado")
                                .FontSize(11).FontColor("#555555");
                        });
                        row.ConstantItem(140).AlignRight().Column(c =>
                        {
                            c.Item().Text(codigo)
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
                    // Comunidades que aportaron al lote faenado
                    col.Item().Background("#F1F8E9").Padding(8).Column(c =>
                    {
                        c.Item().Text("COMUNIDADES QUE APORTARON AL LOTE")
                            .FontSize(9).Bold().FontColor("#2E7D32");
                        foreach (var (comunidad, cantidad) in aportes)
                        {
                            c.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem(3).Text($"• {comunidad}");
                                r.RelativeItem(1).AlignRight().Text(
                                    $"{cantidad} {(cantidad == 1 ? "cuy" : "cuyes")}")
                                    .Bold();
                            });
                        }
                        c.Item().PaddingTop(3).Text(
                            "Jaulas de origen: " + string.Join(", ",
                                loteFaenado.Sesiones
                                    .Select(s => s.Lote.CodigoLote).Distinct()))
                            .FontSize(8).FontColor("#555555");
                    });

                    // Datos del proceso
                    col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Column(c =>
                    {
                        c.Item().Text("FAENAMIENTO — Sulupali Chico, Santa Isabel")
                            .FontSize(9).Bold().FontColor("#1565C0");
                        c.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Fecha: {loteFaenado.FechaFaenamiento:dd/MM/yyyy HH:mm}");
                            r.RelativeItem().Text(
                                $"Operario: {loteFaenado.OperarioResponsable}");
                        });
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Unidades faenadas: {unidades}");
                            r.RelativeItem().Text(
                                $"Peso canal total: {pesoTotal:N0}g " +
                                $"(promedio {promedio:N0}g)");
                        });
                        if (loteFaenado.TemperaturaAlmacenamiento is decimal temp)
                            c.Item().Text($"Temperatura: {temp}°C");
                    });

                    // Detalle individual: cómo llegó cada cuy de su comunidad
                    col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Column(c =>
                    {
                        c.Item().Text("DETALLE POR ANIMAL")
                            .FontSize(9).Bold().FontColor("#2E7D32");

                        c.Item().PaddingTop(4).Table(tabla =>
                        {
                            tabla.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);    // Jaula origen
                                cols.ConstantColumn(30);   // N°
                                cols.RelativeColumn(2);    // Comunidad
                                cols.ConstantColumn(65);   // Peso canal
                                cols.ConstantColumn(70);   // Estado
                                cols.RelativeColumn(2);    // Observación
                            });

                            tabla.Header(h =>
                            {
                                foreach (var titulo in new[]
                                    { "Jaula origen", "N°", "Comunidad",
                                      "Peso canal", "Estado", "Observación" })
                                {
                                    h.Cell().BorderBottom(1).BorderColor("#CCCCCC")
                                        .PaddingBottom(2)
                                        .Text(titulo).FontSize(7).Bold();
                                }
                            });

                            foreach (var animal in animales)
                            {
                                var colorEstado = animal.Faenado.Estado switch
                                {
                                    EstadoCanal.Rechazado => "#B71C1C",
                                    EstadoCanal.ConNovedad => "#E65100",
                                    _ => "#2E7D32"
                                };

                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Jaula.CodigoLote).FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text($"{animal.Faenado.NumeroEnLote}").FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Comunidad).FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Faenado.PesoCanalGramos is decimal p
                                        ? $"{p:F0}g" : "—").FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Faenado.Estado.ToString()).FontSize(7)
                                    .FontColor(colorEstado);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Faenado.Motivo ?? "—").FontSize(7);
                            }
                        });
                    });

                    // Código QR del producto
                    if (qrPng is not null)
                    {
                        col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Row(r =>
                        {
                            r.ConstantItem(90).Image(qrPng);
                            r.RelativeItem().PaddingLeft(8).Column(c =>
                            {
                                c.Item().Text("CÓDIGO QR DEL PRODUCTO")
                                    .FontSize(9).Bold().FontColor("#2E7D32");
                                c.Item().PaddingTop(4).Text(
                                    "Escanea el código para ver la trazabilidad " +
                                    "pública de este lote faenado.")
                                    .FontSize(8).FontColor("#555555");
                            });
                        });
                    }

                    col.Item().PaddingTop(16).BorderTop(1).BorderColor("#CCCCCC")
                        .PaddingTop(8).Column(c =>
                        {
                            c.Item().Text(
                                "Este documento certifica la trazabilidad del lote " +
                                "faenado indicado conforme al Sistema Cuy Azuayito — COOPAGCUY.")
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

    // Nombre de origen para la ficha: una productora o el conteo de varias
    private static string NombreOrigenLote(
        Features.Productoras.Models.Lote lote)
    {
        var distintas = lote.Cuyes
            .Where(c => c.Productora is not null)
            .Select(c => c.Productora!.Id)
            .Distinct()
            .Count();

        return distintas > 1
            ? $"Varias productoras ({distintas})"
            : lote.Productora?.NombreCompleto ?? "-";
    }

    // ── Exportar PDF de ficha de lote — RF-505 ────────────────────────

    public async Task<byte[]> ExportarPDFLoteAsync(string codigoLote)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Código de producto terminado (FAE-…): la ficha es del lote
        // faenado completo, con las comunidades que aportaron
        if (codigoLote.StartsWith("FAE-", StringComparison.OrdinalIgnoreCase))
            return await ExportarPDFLoteFaenadoAsync(codigoLote);

        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .Include(l => l.CodigoQR)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

        // Solo los animales procesados en planta: la ficha refleja el
        // producto faenado, no la recepción completa de la jaula
        var animalesFaenados = lote.Faenamientos
            .OrderBy(f => f.NumeroSesion)
            .SelectMany(f => f.Cuyes.Select(cf => new
            {
                Sesion = f.NumeroSesion,
                Faenado = cf,
                Recepcion = lote.Cuyes
                    .FirstOrDefault(c => c.NumeroEnLote == cf.NumeroEnLote)
            }))
            .OrderBy(x => x.Sesion).ThenBy(x => x.Faenado.NumeroEnLote)
            .ToList();

        // Agregados sobre las sesiones parciales de faenamiento del lote
        var unidadesFaenadas = lote.Faenamientos.Sum(f => f.UnidadesFaenadas);
        var pesoCanalTotal = lote.Faenamientos.Sum(f => f.PesoTotalCanalGramos);
        var ultimaSesion = lote.Faenamientos
            .OrderByDescending(f => f.FechaFaenamiento)
            .FirstOrDefault();
        var promedio = unidadesFaenadas > 0
            ? pesoCanalTotal / unidadesFaenadas
            : 0;

        // Imagen del código QR del lote, si ya fue generado
        byte[]? qrPng = null;
        if (lote.CodigoQR is not null && lote.CodigoQR.Activo)
        {
            using var generador = new QRCodeGenerator();
            var datos = generador.CreateQrCode(
                lote.CodigoQR.UrlPublica, QRCodeGenerator.ECCLevel.Q);
            qrPng = new PngByteQRCode(datos).GetGraphic(10);
        }

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
                                $"Productora: {NombreOrigenLote(lote)}");
                            r.RelativeItem().Text(
                                $"Comunidad: {lote.Productora?.Comunidad ?? "-"}");
                        });
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Cantón: {lote.Productora?.Canton ?? "-"}");
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

                    // Detalle de los animales faenados: la jaula puede reunir
                    // cuyes de varias comunidades y cada uno lleva su origen
                    if (animalesFaenados.Count > 0)
                    {
                        col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Column(c =>
                        {
                            c.Item().Text("DETALLE DE ANIMALES FAENADOS")
                                .FontSize(9).Bold().FontColor("#2E7D32");

                            c.Item().PaddingTop(4).Table(tabla =>
                            {
                                tabla.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(30);   // N°
                                    cols.ConstantColumn(35);   // Sesión
                                    cols.RelativeColumn(2);    // Comunidad de origen
                                    cols.ConstantColumn(65);   // Peso canal
                                    cols.ConstantColumn(70);   // Estado
                                    cols.RelativeColumn(2);    // Observación
                                });

                                tabla.Header(h =>
                                {
                                    foreach (var titulo in new[]
                                        { "N°", "Sesión", "Comunidad de origen",
                                          "Peso canal", "Estado", "Observación" })
                                    {
                                        h.Cell().BorderBottom(1).BorderColor("#CCCCCC")
                                            .PaddingBottom(2)
                                            .Text(titulo).FontSize(7).Bold();
                                    }
                                });

                                foreach (var animal in animalesFaenados)
                                {
                                    var colorEstado = animal.Faenado.Estado switch
                                    {
                                        EstadoCanal.Rechazado => "#B71C1C",
                                        EstadoCanal.ConNovedad => "#E65100",
                                        _ => "#2E7D32"
                                    };

                                    var comunidad = animal.Recepcion?.Productora?.Comunidad
                                        ?? lote.Productora?.Comunidad ?? "—";

                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"{animal.Faenado.NumeroEnLote}").FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"F{animal.Sesion}").FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(comunidad).FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(animal.Faenado.PesoCanalGramos is decimal p
                                            ? $"{p:F0}g" : "—").FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(animal.Faenado.Estado.ToString()).FontSize(7)
                                        .FontColor(colorEstado);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(animal.Faenado.Motivo ?? "—").FontSize(7);
                                }
                            });
                        });
                    }

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

                    // Faenamiento: una entrada por cada sesión parcial
                    if (lote.Faenamientos.Count > 0)
                    {
                        col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Column(c =>
                        {
                            c.Item().Text("FAENAMIENTO — Sulupali Chico, Santa Isabel")
                                .FontSize(9).Bold().FontColor("#1565C0");
                            c.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Total faenado: {unidadesFaenadas} unidades");
                                r.RelativeItem().Text(
                                    $"Peso promedio: {promedio:N0}g");
                            });

                            foreach (var sesion in lote.Faenamientos
                                .OrderBy(f => f.FechaFaenamiento))
                            {
                                c.Item().PaddingTop(4).Text(
                                    $"• {sesion.FechaFaenamiento:dd/MM/yyyy}: " +
                                    $"{sesion.UnidadesFaenadas} faenados" +
                                    (sesion.UnidadesDecomisadas > 0
                                        ? $", {sesion.UnidadesDecomisadas} decomisados" : "") +
                                    $" · {sesion.PesoTotalCanalGramos:N0}g" +
                                    $" · Estado: {sesion.EstadoCanal}" +
                                    $" · Operario: {sesion.OperarioResponsable}")
                                    .FontSize(8);
                            }
                        });
                    }

                    // Código QR de trazabilidad pública
                    if (qrPng is not null)
                    {
                        col.Item().PaddingTop(8).Background("#FAFAFA").Padding(8).Row(r =>
                        {
                            r.ConstantItem(90).Image(qrPng);
                            r.RelativeItem().PaddingLeft(8).Column(c =>
                            {
                                c.Item().Text("CÓDIGO QR DEL PRODUCTO")
                                    .FontSize(9).Bold().FontColor("#2E7D32");
                                c.Item().PaddingTop(4).Text(
                                    "Escanea el código para ver la trazabilidad " +
                                    "pública de este lote.")
                                    .FontSize(8).FontColor("#555555");
                                c.Item().PaddingTop(2)
                                    .Text(lote.CodigoQR!.UrlPublica)
                                    .FontSize(7).FontColor("#999999");
                            });
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
using ClosedXML.Excel;
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Branding;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
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
        DateTime? desde, DateTime? hasta, string? cat = null);
    Task<IEnumerable<ReporteProductoraDto>> ReportePorProductoraAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteCATDto>> ReportePorCATAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteNovedadDto>> ReporteNovedadesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelProductorasAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelNovedadesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelCATAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelDevolucionesAsync(FiltroPeriodoDto filtro);
    // Flujo de trazabilidad: entrada (en espera), tránsito (faenado), salida
    Task<IEnumerable<ReporteEntradaDto>> ReporteEntradaAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteTransitoDto>> ReporteTransitoAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<ReporteSalidaDto>> ReporteSalidaAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelEntradaAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelTransitoAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelSalidaAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelGeneralAsync(
        FiltroPeriodoDto filtro, bool incluirFlujoOperativo = true);
    Task<byte[]> ExportarPDFLoteAsync(string codigoLote);
    Task<IEnumerable<ReporteCuyDto>> ReporteCuyesAsync(FiltroPeriodoDto filtro);
    Task<byte[]> ExportarExcelCuyesAsync(FiltroPeriodoDto filtro);
    Task<ReporteDevolucionesDto> ReporteDevolucionesAsync(FiltroPeriodoDto filtro);
    // Lo que ganaron las productoras: NUNCA se suma con el margen de la
    // reventa (tareas aparte). Tres vistas de la misma consulta base.
    Task<IEnumerable<GananciaProductoraDto>> GananciasPorProductoraAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<GananciaCatDto>> GananciasPorCatAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<GananciaMesDto>> GananciasPorMesAsync(FiltroPeriodoDto filtro);
    // El margen de la reventa: la otra mitad del reporte, y la que NUNCA se
    // suma con las ganancias de productoras de arriba.
    Task<IEnumerable<MargenDto>> MargenPorMesAsync(FiltroPeriodoDto filtro);
    Task<IEnumerable<MargenDto>> MargenPorClienteAsync(FiltroPeriodoDto filtro);
    // Unidades de cuyes vendidas por las dos vías, por mes local del piloto:
    // ver el comentario de UnidadesMesDto para por qué aquí sumar SÍ es
    // válido, a diferencia del resto de este reporte.
    Task<IEnumerable<UnidadesMesDto>> UnidadesPorMesAsync(FiltroPeriodoDto filtro);
    // Las cinco vistas de arriba, en un solo libro, cada una en su propia
    // hoja: nunca en la misma celda, porque las dos mitades del reporte
    // (ganancias de productoras y margen de la reventa) no se suman.
    Task<byte[]> ExportarExcelGananciasAsync(FiltroPeriodoDto filtro);
}

public class ReportesService(AppDbContext db) : IReportesService
{
    // El filtro llega como fecha sin hora ("2026-07-03"): el límite
    // superior debe cubrir el día completo, no cortar a medianoche.
    // Devuelve (desde inclusivo, hasta exclusivo = día siguiente 00:00).
    //
    // Los días son LOCALES del piloto (Ecuador, UTC-5), no UTC. Antes se
    // tomaban como UTC directamente, y eso recortaba de todos los reportes
    // las últimas cinco horas de cada día local: un despacho de las 20:00 en
    // el CAT ya pertenecía al día UTC siguiente y no salía en el reporte "de
    // hoy" pese a estar bien guardado. Ese fue el fallo que se reportó como
    // "los despachos nuevos no aparecen en Salida".
    private static (DateTime desdeUtc, DateTime hastaExclusivoUtc) RangoUtc(
        FiltroPeriodoDto filtro)
    {
        var desde = FechaUtc.InicioDelDiaLocal(filtro.Desde);
        var hasta = FechaUtc.InicioDelDiaLocal(filtro.Hasta.Date.AddDays(1));
        return (desde, hasta);
    }

    // ── Dashboard — RF-508 ────────────────────────────────────────────

    public async Task<DashboardDto> ObtenerDashboardAsync(
        DateTime? desde, DateTime? hasta, string? cat = null)
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
        if (!string.IsNullOrEmpty(cat))
            query = query.Where(l => l.CentroAcopio == cat);

        var lotes = await query
            .Include(l => l.Cuyes)
            .AsNoTracking()
            .ToListAsync();

        // Las tasas se cuentan por animal, no por jaula. Lote.Estado marca la
        // jaula entera con novedad si UN solo cuy la tiene, así que por jaula
        // la aceptación daba 0% con 19 de 20 animales perfectos: un número que
        // no describía nada de lo que pasaba en el CAT.
        var cuyes = lotes.SelectMany(l => l.Cuyes).ToList();
        var totalCuyes = cuyes.Count;
        var aceptados = cuyes.Count(c => c.Estado == EstadoLote.Aceptado);
        var conNovedad = cuyes.Count(c => c.Estado == EstadoLote.ConNovedad);
        var rechazados = cuyes.Count(c => c.Estado == EstadoLote.Rechazado);

        decimal Tasa(int parte) => totalCuyes == 0
            ? 0
            : Math.Round((decimal)parte / totalCuyes * 100, 1);

        // Etapas posteriores a la recepción, contadas aparte: aquí el animal
        // ya fue aceptado y entró a la cadena
        var retornos = await db.RetornosProductora.CountAsync(r =>
            r.FechaRetorno >= desdeUtc && r.FechaRetorno < hastaUtc);
        var devoluciones = await db.Devoluciones
            .Where(d => d.FechaDevolucion >= desdeUtc && d.FechaDevolucion < hastaUtc)
            .ToListAsync();

        return new DashboardDto(
            LotesActivos: lotes.Count,
            // Cuenta los animales realmente registrados; CantidadAnimales es
            // el contador de la jaula y puede ir por delante del detalle
            AnimalesRecibidosPeriodo: totalCuyes,
            TasaAceptacion: Tasa(aceptados),
            TasaConNovedad: Tasa(conNovedad),
            TasaRechazado: Tasa(rechazados),
            AnimalesAceptados: aceptados,
            AnimalesConNovedad: conNovedad,
            AnimalesRechazados: rechazados,
            LotesConQR: await db.CodigosQR.CountAsync(q => q.Activo),
            // Mismo criterio de "sin filtro" que usa la consulta de Lotes de
            // arriba (IsNullOrEmpty, no == null): un claim "cat" vacío debe
            // significar "sin acotar" en las dos consultas del mismo
            // dashboard, no en una sí y en la otra no. Con `== null`, un
            // claim vacío dejaba los lotes sin filtrar pero ponía el conteo
            // de productoras en cero — el peor de los tres resultados
            // posibles (alcance mixto dentro del mismo panel).
            TotalProductoras: await db.Productoras.CountAsync(p =>
                p.Activa && (string.IsNullOrEmpty(cat) || p.CatAsignado == cat)),
            TotalFaenamientos: await db.Faenamientos.CountAsync(),
            FechaCorte: hastaUtc,
            RetornosDesdePlanta: retornos,
            DevolucionesClientes: devoluciones.Count,
            UnidadesDevueltas: devoluciones.Sum(d => d.CantidadUnidades)
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

        if (filtro.CentroAcopio is not null)
            query = query.Where(c => c.Lote.CentroAcopio == filtro.CentroAcopio);

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
                    Comunidad: p.Comunidad.Nombre,
                    CentroAcopio: p.CatAsignado,
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
                    CentroAcopio: g.Key,
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

        if (filtro.CentroAcopio is not null)
            query = query.Where(n => n.Lote.CentroAcopio == filtro.CentroAcopio);

        return await query
            .OrderByDescending(n => n.FechaRegistro)
            .Select(n => new ReporteNovedadDto(
                n.Id,
                n.Lote.CodigoLote,
                n.Lote.Productora != null
                    ? n.Lote.Productora.NombreCompleto : "Varias productoras",
                n.Lote.Productora != null
                    ? n.Lote.Productora.Comunidad.Nombre : "-",
                n.Lote.CentroAcopio,
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
            hoja.Cell(fila, 11).Value = FechaUtc.FechaLocal(r.UltimaEntrega);
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
            hoja.Cell(fila, 8).Value = FechaUtc.FechaHoraLocal(r.FechaRegistro);
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

        if (filtro.CentroAcopio is not null)
            query = query.Where(c => c.Lote.CentroAcopio == filtro.CentroAcopio);

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
                    ? c.Productora.Comunidad.Nombre
                    : c.Lote.Productora != null
                        ? c.Lote.Productora.Comunidad.Nombre : string.Empty,
                c.Lote.CentroAcopio,
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
            hoja.Cell(fila, 12).Value = FechaUtc.FechaLocal(r.FechaRecepcion);

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

    // ── Exportar Excel por CAT — RF-505 ───────────────────────────────

    public async Task<byte[]> ExportarExcelCATAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReportePorCATAsync(filtro);

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Por CAT");

        var encabezados = new[]
        {
            "Centro de Acopio", "Lotes", "Animales", "Aceptados",
            "Con novedad", "Rechazados", "Tasa aceptación (%)",
            "Peso total (g)"
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
            hoja.Cell(fila, 1).Value = r.CentroAcopio;
            hoja.Cell(fila, 2).Value = r.TotalLotes;
            hoja.Cell(fila, 3).Value = r.TotalAnimales;
            hoja.Cell(fila, 4).Value = r.LotesAceptados;
            hoja.Cell(fila, 5).Value = r.LotesConNovedad;
            hoja.Cell(fila, 6).Value = r.LotesRechazados;
            hoja.Cell(fila, 7).Value = r.TasaAceptacion;
            hoja.Cell(fila, 8).Value = r.PesoTotalGramos;
            fila++;
        }

        hoja.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Exportar Excel devoluciones y retornos — RF-505 ───────────────
    // Dos hojas: devoluciones de clientes (post-despacho) y cuyes
    // devueltos vivos a su productora (pre-faenamiento)

    public async Task<byte[]> ExportarExcelDevolucionesAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReporteDevolucionesAsync(filtro);

        using var libro = new XLWorkbook();

        var hojaDev = libro.Worksheets.Add("Devoluciones clientes");
        var encDev = new[]
        {
            "Lote", "Sesión", "Productora", "Comunidad", "Cliente",
            "Unidades", "Motivo", "Fecha"
        };
        for (int i = 0; i < encDev.Length; i++)
        {
            var celda = hojaDev.Cell(1, i + 1);
            celda.Value = encDev[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
            celda.Style.Font.FontColor = XLColor.White;
        }

        int fila = 2;
        foreach (var d in datos.DevolucionesClientes)
        {
            hojaDev.Cell(fila, 1).Value = d.CodigoLote;
            hojaDev.Cell(fila, 2).Value = d.NumeroSesion is int s ? $"F{s}" : "—";
            hojaDev.Cell(fila, 3).Value = d.NombreProductora;
            hojaDev.Cell(fila, 4).Value = d.Comunidad;
            hojaDev.Cell(fila, 5).Value = d.ClienteDevuelve;
            hojaDev.Cell(fila, 6).Value = d.CantidadUnidades;
            hojaDev.Cell(fila, 7).Value = d.Motivo;
            hojaDev.Cell(fila, 8).Value = FechaUtc.FechaLocal(d.FechaDevolucion);
            fila++;
        }
        hojaDev.Columns().AdjustToContents();

        var hojaRet = libro.Worksheets.Add("Retornos a productora");
        var encRet = new[]
        {
            "Lote", "Cuy N°", "Productora", "Comunidad", "Motivo",
            "Fecha", "Responsable"
        };
        for (int i = 0; i < encRet.Length; i++)
        {
            var celda = hojaRet.Cell(1, i + 1);
            celda.Value = encRet[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
            celda.Style.Font.FontColor = XLColor.White;
        }

        fila = 2;
        foreach (var r in datos.RetornosProductora)
        {
            hojaRet.Cell(fila, 1).Value = r.CodigoLote;
            hojaRet.Cell(fila, 2).Value = r.NumeroEnLote;
            hojaRet.Cell(fila, 3).Value = r.NombreProductora;
            hojaRet.Cell(fila, 4).Value = r.Comunidad;
            hojaRet.Cell(fila, 5).Value = r.Motivo;
            hojaRet.Cell(fila, 6).Value = FechaUtc.FechaLocal(r.FechaRetorno);
            hojaRet.Cell(fila, 7).Value = r.Responsable;
            fila++;
        }
        hojaRet.Columns().AdjustToContents();

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
            .Include(d => d.Lote).ThenInclude(l => l!.Productora)
            .Where(d => d.FechaDevolucion >= desdeUtc &&
                        d.FechaDevolucion < hastaUtc);

        var retQuery = db.RetornosProductora
            .Include(r => r.Lote).ThenInclude(l => l.Productora)
            .Where(r => r.FechaRetorno >= desdeUtc &&
                        r.FechaRetorno < hastaUtc);

        if (filtro.CentroAcopio is not null)
        {
            // Las devoluciones por despacho abarcan un lote faenado que
            // puede cruzar varios CAT: solo las legadas (por jaula)
            // admiten este filtro
            devQuery = devQuery.Where(d =>
                d.Lote != null && d.Lote.CentroAcopio == filtro.CentroAcopio);
            retQuery = retQuery.Where(r => r.Lote.CentroAcopio == filtro.CentroAcopio);
        }

        var devoluciones = await devQuery
            .OrderByDescending(d => d.FechaDevolucion)
            .Select(d => new DevolucionItemDto(
                d.Id,
                d.Despacho != null && d.Despacho.LoteFaenado != null
                    ? d.Despacho.LoteFaenado.Codigo
                    : d.Lote != null ? d.Lote.CodigoLote : "—",
                d.RegistroFaenamiento != null
                    ? d.RegistroFaenamiento.NumeroSesion : null,
                d.Lote != null && d.Lote.Productora != null
                    ? d.Lote.Productora.NombreCompleto : "Varias productoras",
                d.Lote != null && d.Lote.Productora != null
                    ? d.Lote.Productora.Comunidad.Nombre : "-",
                d.ClienteDevuelve, d.FechaDevolucion,
                d.CantidadUnidades, d.Motivo))
            .ToListAsync();

        var retornos = await retQuery
            .OrderByDescending(r => r.FechaRetorno)
            .Select(r => new RetornoItemDto(
                r.Id, r.Lote.CodigoLote,
                r.Productora.NombreCompleto, r.Productora.Comunidad.Nombre,
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

    // ── Ganancias de productoras ───────────────────────────────────────
    //
    // El reporte publica dos cifras que NUNCA se suman: lo que ganaron las
    // productoras (esta sección) y el margen de la reventa. Un pago a una
    // productora es ingreso para ella y costo para la cooperativa — la
    // misma fila leída desde dos lados.

    // Pagos que cuentan como dinero movido: los pendientes son tickets que la
    // planta todavía no ha transferido, y no son dinero movido.
    //
    // Se suma MontoPagadoUsd y no MontoUsd: la diferencia son los descuentos
    // por novedades, y contarlos como pagados inflaría la cifra justo donde
    // el sistema ya sabe que no lo fueron. En las ventas locales los dos
    // valores coinciden —el servicio los iguala al registrar— así que la
    // regla es uniforme.
    private IQueryable<Pago> PagosDelPeriodo(FiltroPeriodoDto filtro)
    {
        var (desde, hasta) = RangoUtc(filtro);
        return db.Pagos
            .Where(p => p.FechaPago >= desde && p.FechaPago < hasta
                && p.Estado != EstadoPago.Pendiente);
    }

    // Clasifica los pagos de un grupo (una productora, un CAT o un mes) en
    // las tres columnas que no se suman entre sí. Único lugar que sabe cómo
    // distinguir un pago cobrado, pactado o de planta: si mañana ese
    // criterio cambia (como acaba de pasar con "Cuotas") y una de las tres
    // vistas quedara con su propia copia, podrían desincronizarse en
    // silencio entre sí sobre los mismos datos.
    private static (decimal Cobrado, decimal Pactado, decimal Planta) SumarPorCanal(
        IEnumerable<Pago> pagos)
    {
        decimal cobrado = 0, pactado = 0, planta = 0;
        foreach (var p in pagos)
        {
            var monto = p.MontoPagadoUsd ?? 0;
            if (!p.EsVentaLocal) planta += monto;
            else if (p.EsCuotas()) pactado += monto;
            else cobrado += monto;
        }
        return (cobrado, pactado, planta);
    }

    public async Task<IEnumerable<GananciaProductoraDto>> GananciasPorProductoraAsync(
        FiltroPeriodoDto filtro)
    {
        IQueryable<Pago> query = PagosDelPeriodo(filtro)
            .Include(p => p.Productora).ThenInclude(pr => pr.Comunidad);

        if (filtro.CentroAcopio is not null)
            query = query.Where(p => p.Productora!.CatAsignado == filtro.CentroAcopio);

        var pagos = await query.AsNoTracking().ToListAsync();

        return pagos
            .GroupBy(p => p.ProductoraId)
            .Select(g =>
            {
                var p = g.First().Productora!;
                var (cobrado, pactado, planta) = SumarPorCanal(g);
                return new GananciaProductoraDto(
                    ProductoraId: p.Id,
                    NombreProductora: p.NombreCompleto,
                    Comunidad: p.Comunidad.Nombre,
                    CentroAcopio: p.CatAsignado,
                    CobradoLocal: cobrado,
                    PactadoCuotas: pactado,
                    PagadoPlanta: planta,
                    TotalPagos: g.Count()
                );
            })
            // N1: orden lexicográfico, no la suma de las tres columnas — esa
            // suma no existe en ningún DTO ni celda de este reporte (las
            // tres NUNCA se suman entre sí). Cobrado local pesa más porque
            // es dinero que la CAT ya tiene en la mano; pagado
            // planta va después porque también es dinero movido; pactado a
            // cuotas al final porque todavía no ha llegado.
            .OrderByDescending(r => r.CobradoLocal)
            .ThenByDescending(r => r.PagadoPlanta)
            .ThenByDescending(r => r.PactadoCuotas)
            .ToList();
    }

    public async Task<IEnumerable<GananciaCatDto>> GananciasPorCatAsync(
        FiltroPeriodoDto filtro)
    {
        // A diferencia de ReportePorCATAsync (que sí deja el parámetro sin
        // efecto porque agrupa por el mismo campo), aquí SÍ se filtra: las
        // otras dos vistas de ganancias (productoras y mes) honran ?cat=, y
        // un consumidor que pase el mismo parámetro a las tres esperaría el
        // mismo comportamiento. Sin este filtro, ?cat=PAT devolvía TODAS las
        // filas (una por CAT) en vez de acotar a una — mismo parámetro, tres
        // endpoints, forma de respuesta distinta sin ninguna señal.
        IQueryable<Pago> query = PagosDelPeriodo(filtro)
            .Include(p => p.Productora);

        if (filtro.CentroAcopio is not null)
            query = query.Where(p => p.Productora!.CatAsignado == filtro.CentroAcopio);

        var pagos = await query.AsNoTracking().ToListAsync();

        return pagos
            .GroupBy(p => p.Productora!.CatAsignado)
            .Select(g =>
            {
                var (cobrado, pactado, planta) = SumarPorCanal(g);
                return new GananciaCatDto(
                    CentroAcopio: g.Key,
                    CobradoLocal: cobrado,
                    PactadoCuotas: pactado,
                    PagadoPlanta: planta,
                    TotalPagos: g.Count()
                );
            })
            .OrderBy(r => r.CentroAcopio)
            .ToList();
    }

    public async Task<IEnumerable<GananciaMesDto>> GananciasPorMesAsync(
        FiltroPeriodoDto filtro)
    {
        var query = PagosDelPeriodo(filtro);

        if (filtro.CentroAcopio is not null)
            query = query.Where(p => p.Productora!.CatAsignado == filtro.CentroAcopio);

        var pagos = await query.AsNoTracking().ToListAsync();

        // El mes se agrupa por el día LOCAL del piloto, no por el UTC: un
        // pago de las 20:00 del 31 de agosto pertenece a agosto, y agrupar
        // por UTC lo mandaría a septiembre.
        //
        // FechaUtc.ALocal no se traduce a SQL, así que esta vista materializa
        // (arriba, con ToListAsync) antes de agrupar. El volumen del piloto
        // lo permite de sobra: no cambiar esto por un GroupBy en base de
        // datos, que rompería la frontera del mes en silencio.
        return pagos
            .Select(p => (Pago: p, Local: FechaUtc.ALocal(p.FechaPago)))
            .GroupBy(x => new { x.Local.Year, x.Local.Month })
            .Select(g =>
            {
                var (cobrado, pactado, planta) = SumarPorCanal(g.Select(x => x.Pago));
                return new GananciaMesDto(
                    Anio: g.Key.Year,
                    Mes: g.Key.Month,
                    CobradoLocal: cobrado,
                    PactadoCuotas: pactado,
                    PagadoPlanta: planta,
                    TotalPagos: g.Count()
                );
            })
            .OrderBy(r => r.Anio).ThenBy(r => r.Mes)
            .ToList();
    }

    // ── Margen de la reventa ────────────────────────────────────────────
    //
    // La otra mitad del reporte, y la que NUNCA se suma con las ganancias de
    // productoras de arriba: un pago a una productora es ingreso para ella y
    // costo para la cooperativa, la misma fila leída desde dos lados.

    /// <summary>
    /// Trae los despachos del período con la cadena completa hasta la
    /// productora de origen de cada animal, y los pagos de planta que
    /// atribuyen su costo. Alimenta la función pura
    /// <see cref="CostoDeLoDespachado.Calcular"/>, que no se reescribe aquí.
    ///
    /// Deliberadamente NO filtra por CAT (ni aquí ni en el <c>cat</c> de
    /// <see cref="FiltroPeriodoDto"/>, que este método ignora): un despacho
    /// reúne animales de varias jaulas y por tanto de varios CAT. Filtrar
    /// por CAT obligaría a elegir entre sumar el ingreso completo de un
    /// despacho mixto bajo un único CAT (contando de más si se repitiera el
    /// filtro para cada CAT) o excluir del pool de costo a un animal cuya
    /// productora sí cobró —solo porque esa productora pertenece a otro
    /// CAT—, etiquetando un costo conocido como desconocido. El margen es
    /// de la cooperativa, no de un CAT: mismo criterio que
    /// <see cref="ReporteSalidaAsync"/>, el otro reporte que también gira
    /// sobre <c>Despacho</c> y tampoco filtra por CAT.
    /// </summary>
    private async Task<(
        List<Despacho> Despachos,
        Dictionary<int, List<AnimalDespachado>> AnimalesPorDespacho,
        List<PagoDeLote> Pagos,
        Dictionary<int, int> UnidadesDevueltasPorDespacho)> DatosDeMargenAsync(
            FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        // Se materializa y se resuelve en memoria a propósito. La cadena
        // DespachoCuy -> CuyFaenamiento -> RegistroFaenamiento -> Lote, más el
        // salto de NumeroEnLote a CuyRegistro, produce en SQL una consulta que
        // nadie va a poder leer dentro de seis meses. Con el volumen del piloto
        // —cientos de animales por período— traerlo y resolverlo aquí es más
        // barato en mantenimiento que en milisegundos, y la parte con reglas
        // vive en una función pura que sí se puede fijar.
        // Should-fix 4: orden explícito, no el que el proveedor entregue. Sin
        // esto, MargenPorClienteAsync toma la etiqueta visible de
        // g.First().ClienteDestino (ver el comentario ahí) y "el primer
        // despacho del grupo" quedaba a merced del orden físico de la
        // consulta —que con AsSplitQuery y cuatro niveles de Include no está
        // garantizado— así que la forma mostrada de un mismo cliente podía
        // cambiar entre una corrida y la siguiente del mismo reporte. Con
        // este orden, gana el despacho más antiguo del grupo (FechaDespacho
        // ascendente) y, en empate, el de menor Id.
        var despachos = await db.Despachos
            .Include(d => d.Cuyes)
                .ThenInclude(dc => dc.CuyFaenamiento)
                    .ThenInclude(cf => cf.Registro)
                        .ThenInclude(r => r.Lote)
                            .ThenInclude(l => l.Cuyes)
            .Where(d => d.FechaDespacho >= desdeUtc && d.FechaDespacho < hastaUtc)
            .OrderBy(d => d.FechaDespacho).ThenBy(d => d.Id)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync();

        // Animales de cada despacho, resueltos a su lote y su productora de
        // origen: el mismo salto de NumeroEnLote que usa el resto del reporte
        // para encontrar quién entregó cada animal. Un despacho legado (sin
        // filas DespachoCuy, apuntando directo a Lote) no tiene ese detalle:
        // sus CantidadUnidades animales entran igual al pool de costo, pero
        // sin productora — CostoDeLoDespachado.Calcular los declara
        // AnimalesSinCosto en vez de tratarlos como si costaran cero.
        var animalesPorDespacho = despachos.ToDictionary(
            d => d.Id,
            d => d.Cuyes.Count > 0
                ? d.Cuyes.Select(dc =>
                {
                    var registro = dc.CuyFaenamiento.Registro;
                    var origen = OrigenDelAnimal(
                        registro.Lote, dc.CuyFaenamiento.NumeroEnLote);
                    return new AnimalDespachado(
                        registro.LoteId, dc.CuyFaenamiento.NumeroEnLote,
                        origen?.ProductoraId);
                }).ToList()
                : Enumerable.Repeat(
                    new AnimalDespachado(d.LoteId ?? 0, 0, null),
                    d.CantidadUnidades).ToList());

        var loteIds = despachos
            .SelectMany(d => d.Cuyes.Select(dc => dc.CuyFaenamiento.Registro.LoteId))
            .Distinct()
            .ToList();

        // Solo cuentan los pagos de planta: una venta local no es dinero que
        // la cooperativa haya puesto para comprar el animal, lo puso la CAT,
        // y ya se contó en las ganancias de productoras.
        var pagosCrudos = await db.Pagos
            .Where(p => p.LoteId != null && loteIds.Contains(p.LoteId.Value)
                && !p.EsVentaLocal && p.Estado != EstadoPago.Pendiente)
            .AsNoTracking()
            .ToListAsync();

        var cuyes = despachos
            .SelectMany(d => d.Cuyes)
            .Select(dc => dc.CuyFaenamiento.Registro.Lote)
            .DistinctBy(l => l.Id)
            .SelectMany(l => l.Cuyes)
            .ToList();

        // Se agrupa por (LoteId, ProductoraId) antes de construir cada
        // PagoDeLote: Calcular() asume una sola fila por esa clave y lanza
        // si le llegan dos, así que aquí es donde se garantiza.
        var pagos = pagosCrudos
            .Where(p => p.LoteId.HasValue)
            .GroupBy(p => (LoteId: p.LoteId!.Value, p.ProductoraId))
            .Select(g => new PagoDeLote(
                g.Key.LoteId, g.Key.ProductoraId,
                MontoPagado: g.Sum(p => p.MontoPagadoUsd ?? 0m),
                // Los animales que ese pago cubrió: los de esa productora en
                // ese lote que NO se vendieron en la comunidad. Esos nunca
                // llegaron a la planta y su pago fue otro; además es el
                // mismo conteo que la operadora vio al crear el pago.
                AnimalesCubiertos: cuyes.Count(c => c.LoteId == g.Key.LoteId
                    && c.ProductoraId == g.Key.ProductoraId
                    && c.VentaLocalPagoId == null)))
            .ToList();

        // Unidades devueltas por despacho, con la misma agrupación que usa
        // FaenamientoService.ListarDespachosAsync (Devolucion.UnidadesPorDespachoAsync):
        // el ingreso del margen se cuenta neto de lo devuelto (decisión del
        // producto owner, S1). El costo NO se ajusta por esto — ver el
        // comentario en ConstruirMargen. La consulta de abajo acota solo por
        // DespachoId, no por FechaDevolucion — a propósito: es lo que hace
        // que una devolución de marzo baje el ingreso de enero al reejecutar
        // ese reporte, en vez de acotar por fecha y estrandar esa devolución
        // en un mes cuyo margen no le pertenece.
        var despachoIds = despachos.Select(d => d.Id).ToList();
        var unidadesDevueltasPorDespacho = await Devolucion.UnidadesPorDespachoAsync(
            db.Devoluciones.Where(v => v.DespachoId != null
                && despachoIds.Contains(v.DespachoId.Value)));

        return (despachos, animalesPorDespacho, pagos, unidadesDevueltasPorDespacho);
    }

    // El salto de NumeroEnLote a CuyRegistro para encontrar quién entregó un
    // animal: un único punto para no dejar que dos copias de este lambda se
    // aparten con el tiempo.
    private static CuyRegistro? OrigenDelAnimal(Lote lote, int numeroEnLote) =>
        lote.Cuyes.FirstOrDefault(c => c.NumeroEnLote == numeroEnLote);

    // El ingreso solo suma los despachos CON precio; los que no lo tienen se
    // cuentan en DespachosSinPrecio y no como cero — un despacho sin precio
    // no se vendió gratis.
    //
    // S1 — el ingreso es NETO de devoluciones: decisión del product owner.
    // Un cliente que se lleva 10 y devuelve 6 no "dejó" el ingreso de los
    // 10; contarlo así habría invertido el ranking por cliente que esta
    // vista existe para dar ("para saber cuál deja más" — spec, Parte 3).
    //
    // El COSTO NO se ajusta por la devolución, a propósito:
    //   1. La cooperativa ya le pagó (o le debe) a la productora por ese
    //      animal específico. Ese pago no se reversa porque un cliente haya
    //      devuelto el producto después — es un problema de la venta, no de
    //      la compra.
    //   2. Devolucion no identifica QUÉ animal puntual volvió (solo cuenta
    //      unidades por despacho), así que no hay forma de sacar del pool
    //      de costo a un animal concreto sin inventar cuál fue — la misma
    //      honestidad que ya rige el resto de este reporte: lo que no se
    //      sabe no se fuerza a cero, y aquí tampoco se fuerza un supuesto
    //      que el dato no sostiene.
    // El resultado: una devolución baja el margen (el ingreso cae, el costo
    // no), que es la lectura correcta — la cooperativa sigue habiendo
    // pagado por un animal que no generó ingreso.
    private static MargenDto ConstruirMargen(
        string agrupacion,
        IEnumerable<Despacho> despachosDelGrupo,
        Dictionary<int, List<AnimalDespachado>> animalesPorDespacho,
        List<PagoDeLote> pagos,
        Dictionary<int, int> unidadesDevueltasPorDespacho)
    {
        decimal ingreso = 0m;
        var sinPrecio = 0;
        var unidadesDevueltas = 0;
        var animales = new List<AnimalDespachado>();

        foreach (var d in despachosDelGrupo)
        {
            var devueltas = unidadesDevueltasPorDespacho.GetValueOrDefault(d.Id);
            unidadesDevueltas += devueltas;
            // RegistrarDevolucionAsync no deja devolver más de lo enviado,
            // pero el Max(0, ...) es la misma defensa en profundidad que el
            // resto del reporte aplica a datos que otro servicio garantiza.
            var unidadesNetas = Math.Max(0, d.CantidadUnidades - devueltas);

            if (d.PrecioUnitarioUsd is decimal precio)
                ingreso += precio * unidadesNetas;
            else
                sinPrecio++;

            animales.AddRange(animalesPorDespacho[d.Id]);
        }

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        return new MargenDto(
            Agrupacion: agrupacion,
            Ingreso: ingreso,
            CostoAtribuido: costo.Total,
            Margen: ingreso - costo.Total,
            DespachosSinPrecio: sinPrecio,
            AnimalesSinCosto: costo.AnimalesSinCosto,
            UnidadesDevueltas: unidadesDevueltas);
    }

    public async Task<IEnumerable<MargenDto>> MargenPorMesAsync(FiltroPeriodoDto filtro)
    {
        var (despachos, animalesPorDespacho, pagos, unidadesDevueltas) =
            await DatosDeMargenAsync(filtro);

        // El mes se agrupa por el día LOCAL del piloto, misma técnica que
        // GananciasPorMesAsync: FechaUtc.ALocal no se traduce a SQL, así que
        // esta vista materializa (arriba, en DatosDeMargenAsync) antes de
        // agrupar. El volumen del piloto lo permite de sobra: no cambiar
        // esto por un GroupBy en base de datos, que rompería la frontera del
        // mes en silencio.
        return despachos
            .Select(d => (Despacho: d, Local: FechaUtc.ALocal(d.FechaDespacho)))
            .GroupBy(x => new { x.Local.Year, x.Local.Month })
            .Select(g => ConstruirMargen(
                $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                g.Select(x => x.Despacho), animalesPorDespacho, pagos, unidadesDevueltas))
            .OrderBy(m => m.Agrupacion)
            .ToList();
    }

    public async Task<IEnumerable<MargenDto>> MargenPorClienteAsync(FiltroPeriodoDto filtro)
    {
        var (despachos, animalesPorDespacho, pagos, unidadesDevueltas) =
            await DatosDeMargenAsync(filtro);

        return despachos
            // ClienteDestino es texto libre, así que "Mercado Central" y
            // "mercado central" serían dos filas. La CLAVE de agrupación se
            // normaliza (recorte + mayúsculas) para que no se separen. Un
            // catálogo de clientes lo resolvería de raíz, pero es otro
            // proyecto.
            .GroupBy(d => (d.ClienteDestino ?? string.Empty).Trim().ToUpperInvariant())
            .Select(g =>
            {
                // N3: la ETIQUETA visible NO es la clave normalizada —
                // conserva las mayúsculas originales del primer despacho del
                // grupo. Normalizar para agrupar no significa que la
                // pantalla y el Excel deban mostrar el nombre del cliente
                // GRITADO en mayúsculas.
                //
                // Should-fix 4: "el primero" es determinista porque
                // DatosDeMargenAsync ordena los despachos por FechaDespacho
                // ascendente (empate: Id ascendente) antes de llegar aquí —
                // gana la forma en que se escribió el despacho MÁS ANTIGUO
                // del grupo, no la que el proveedor de datos devuelva.
                var etiqueta = (g.First().ClienteDestino ?? string.Empty).Trim();
                return ConstruirMargen(
                    etiqueta, g, animalesPorDespacho, pagos, unidadesDevueltas);
            })
            .OrderByDescending(m => m.Ingreso)
            .ToList();
    }

    // ── Unidades vendidas ────────────────────────────────────────────────
    //
    // Es la única excepción del reporte donde sumar SÍ es válido: ver el
    // comentario de UnidadesMesDto.

    public async Task<IEnumerable<UnidadesMesDto>> UnidadesPorMesAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        // ── Vendidas en la comunidad ──────────────────────────────────
        // Se fechan por el PAGO de la venta local, no por la entrega del
        // animal: la venta ocurre cuando se cobra. Es el mismo criterio que
        // usan las tres vistas de ganancias, que también van por FechaPago.
        //
        // SÍ filtra por CAT: el animal tiene productora y la productora su
        // centro asignado.
        IQueryable<CuyRegistro> comunidad = db.CuyRegistros
            .Where(c => c.VentaLocalPagoId != null
                && c.VentaLocalPago!.FechaPago >= desdeUtc
                && c.VentaLocalPago.FechaPago < hastaUtc
                && c.VentaLocalPago.Estado != EstadoPago.Pendiente);

        if (filtro.CentroAcopio is not null)
            comunidad = comunidad.Where(
                c => c.Productora!.CatAsignado == filtro.CentroAcopio);

        // Solo la fecha: es lo único que hace falta para agrupar, y una fila
        // por animal vendido es exactamente el conteo que se busca.
        var fechasComunidad = await comunidad
            .Select(c => c.VentaLocalPago!.FechaPago)
            .ToListAsync();

        // ── Despachadas a clientes ────────────────────────────────────
        // NO filtra por CAT, y es deliberado: un despacho mezcla animales de
        // varias jaulas y por tanto de varios CAT, así que filtrarlo o
        // duplicaría las unidades de un despacho mixto o las atribuiría a un
        // centro que solo puso una parte. Misma decisión, y mismo motivo, que
        // dejó las dos vistas de margen sin filtro de CAT.
        var despachos = await db.Despachos
            .Where(d => d.FechaDespacho >= desdeUtc && d.FechaDespacho < hastaUtc)
            .Select(d => new { d.Id, d.FechaDespacho, d.CantidadUnidades })
            .ToListAsync();

        // Mismo helper que el margen y que ListarDespachosAsync: el criterio
        // de qué cuenta como devuelto vive en un solo sitio y no puede
        // desincronizarse. Acota solo por DespachoId, no por FechaDevolucion
        // —a propósito, igual que en DatosDeMargenAsync—: una devolución de
        // marzo baja las unidades de enero al reejecutar ese reporte, en vez
        // de quedar varada en un mes al que no pertenece.
        var despachoIds = despachos.Select(d => d.Id).ToList();
        var devueltas = await Devolucion.UnidadesPorDespachoAsync(
            db.Devoluciones.Where(v => v.DespachoId != null
                && despachoIds.Contains(v.DespachoId.Value)));

        // ── Agrupación por el mes LOCAL ───────────────────────────────
        // FechaUtc.ALocal no se traduce a SQL, así que las dos consultas de
        // arriba materializan antes de agrupar. El volumen del piloto lo
        // permite de sobra: no cambiar esto por un GroupBy en base de datos,
        // que rompería la frontera del mes en silencio —un despacho de las
        // 20:00 del 31 de agosto pertenece a agosto, no a septiembre.
        static string Mes(DateTime utc)
        {
            var local = FechaUtc.ALocal(utc);
            return $"{local.Year:D4}-{local.Month:D2}";
        }

        var porMes = new SortedDictionary<string, (int Comunidad, int Despacho)>(
            StringComparer.Ordinal);

        foreach (var fecha in fechasComunidad)
        {
            var mes = Mes(fecha);
            var acumulado = porMes.GetValueOrDefault(mes);
            porMes[mes] = (acumulado.Comunidad + 1, acumulado.Despacho);
        }

        foreach (var d in despachos)
        {
            var mes = Mes(d.FechaDespacho);
            var acumulado = porMes.GetValueOrDefault(mes);
            // Math.Max por si una devolución corrupta superara lo despachado:
            // se muestra 0, no un negativo. Misma guarda que ConstruirMargen.
            var netas = Math.Max(
                0, d.CantidadUnidades - devueltas.GetValueOrDefault(d.Id));
            porMes[mes] = (acumulado.Comunidad, acumulado.Despacho + netas);
        }

        return porMes
            .Select(kv => new UnidadesMesDto(
                Agrupacion: kv.Key,
                VendidasComunidad: kv.Value.Comunidad,
                DespachadasClientes: kv.Value.Despacho,
                // Sumar aquí SÍ es válido: un cuy vendido en la comunidad no
                // puede acabar despachado, así que no hay doble conteo.
                Total: kv.Value.Comunidad + kv.Value.Despacho))
            .ToList();
    }

    // ── Exportar Excel del reporte de ganancias — RF-505 ───────────────
    //
    // Cinco hojas, sin ninguna celda que sume las dos cifras que NUNCA se
    // suman: lo que ganaron las productoras (las tres primeras hojas) y el
    // margen de la reventa (las dos últimas). Un pago a una productora es
    // ingreso para ella y costo para la cooperativa — la misma fila leída
    // desde dos lados, y cada lado se queda en su propia hoja.
    //
    // Las dos hojas de margen ignoran filtro.CentroAcopio a propósito, con
    // el mismo motivo que MargenPorMesAsync y MargenPorClienteAsync (ver el
    // comentario en DatosDeMargenAsync): un despacho reúne animales de
    // varias CAT. Las tres primeras hojas sí lo respetan. Quien abra este
    // libro con ?cat= puesto puede extrañarse de que las dos últimas hojas
    // no se hayan acotado igual — por eso cada una de las cinco hojas
    // declara su propio alcance de CAT en una línea debajo de la tabla
    // (EscribirAlcanceCat para las tres primeras, la advertencia fija en
    // AgregarHojaMargen para las dos últimas), y el nombre del archivo
    // incluye la CAT cuando el pedido vino filtrado (ReportesController).
    public async Task<byte[]> ExportarExcelGananciasAsync(FiltroPeriodoDto filtro)
    {
        var porCat = await GananciasPorCatAsync(filtro);
        var porProductora = await GananciasPorProductoraAsync(filtro);
        var porMes = await GananciasPorMesAsync(filtro);
        var margenPorMes = await MargenPorMesAsync(filtro);
        var margenPorCliente = await MargenPorClienteAsync(filtro);
        var unidades = await UnidadesPorMesAsync(filtro);

        using var libro = new XLWorkbook();

        AgregarHojaGananciaCat(libro, porCat, filtro.CentroAcopio);
        AgregarHojaGananciaProductora(libro, porProductora, filtro.CentroAcopio);
        AgregarHojaGananciaMes(libro, porMes, filtro.CentroAcopio);
        AgregarHojaMargen(libro, "Margen por mes", margenPorMes);
        AgregarHojaMargen(libro, "Margen por cliente", margenPorCliente);
        AgregarHojaUnidades(libro, unidades, filtro.CentroAcopio);

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    private static void EscribirEncabezadosGanancias(
        IXLWorksheet hoja, string[] encabezados, int fila = 1)
    {
        for (int i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(fila, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
            celda.Style.Font.FontColor = XLColor.White;
        }
    }

    // El texto que describe el alcance de CAT de una hoja: las tres hojas
    // de ganancias sí respetan ?cat= (a diferencia de las dos de margen, ver
    // AgregarHojaMargen). Sin esto en el libro, alguien que lleve el Excel a
    // una reunión filtrado por PAT puede leer la hoja 2 ("PAT cobró $X")
    // junto a la hoja 4 ("margen $Y", que es de TODA la cooperativa) como si
    // ambas hablaran del mismo universo.
    //
    // Arreglo Task 4: el ?cat= llega aquí ya normalizado a mayúsculas por
    // FiltroPeriodoDto (el borde único de todo el feature), así que un
    // simple null-check basta y es lo mismo que "no filtró arriba". El
    // criterio viejo (EsCatValido, forma de tres letras A-Z) se retiró:
    // con la normalización puesta, su única función residual era responder
    // distinto que Productoras y Recepción ante una forma rara (?cat=p,
    // ?cat=PATO) — las otras dos simplemente no encuentran esa fila y
    // devuelven cero; Reportes hace ahora lo mismo, sin volver a fijar en
    // el binario la lista de los cinco códigos ni comparar contra ella
    // (eso es la Task 6).
    private static string DescripcionAlcanceCat(string? cat) =>
        cat ?? "Todos los centros de acopio";

    // Una sola función que escribe la celda; las dos de abajo solo deciden
    // en qué fila.
    private static void EscribirLineaAlcanceCat(IXLWorksheet hoja, int fila, string? cat)
    {
        var celda = hoja.Cell(fila, 1);
        celda.Value = $"Centro de acopio: {DescripcionAlcanceCat(cat)}";
        celda.Style.Font.Bold = true;
    }

    // Should-fix 3: fila 1, ANTES del encabezado. Con muchas filas de datos
    // (p. ej. "Ganancias por productora" con cincuenta productoras) una nota
    // solo al final del todo obliga a desplazarse por toda la tabla para
    // enterarse de que la hoja está filtrada — justo lo que esta etiqueta
    // existe para evitar. Se repite también al final (EscribirAlcanceCatAlFinal)
    // para quien entra navegando desde abajo.
    private static void EscribirAlcanceCatAlInicio(IXLWorksheet hoja, string? cat) =>
        EscribirLineaAlcanceCat(hoja, 1, cat);

    // Debajo de la tabla, con una fila de por medio: mismo lugar y mismo
    // estilo que las advertencias de AgregarHojaMargen.
    private static void EscribirAlcanceCatAlFinal(
        IXLWorksheet hoja, int filaSiguienteVacia, string? cat) =>
        EscribirLineaAlcanceCat(hoja, filaSiguienteVacia + 1, cat);

    private static void AgregarHojaGananciaCat(
        XLWorkbook libro, IEnumerable<GananciaCatDto> datos, string? cat)
    {
        var hoja = libro.Worksheets.Add("Ganancias por CAT");
        EscribirAlcanceCatAlInicio(hoja, cat);
        EscribirEncabezadosGanancias(hoja, new[]
        {
            "Centro de Acopio", "Cobrado local", "Pactado a cuotas",
            "Pagado planta", "N.º de pagos"
        }, fila: 2);

        int fila = 3;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.CentroAcopio;
            hoja.Cell(fila, 2).Value = r.CobradoLocal;
            hoja.Cell(fila, 3).Value = r.PactadoCuotas;
            hoja.Cell(fila, 4).Value = r.PagadoPlanta;
            hoja.Cell(fila, 5).Value = r.TotalPagos;
            fila++;
        }
        EscribirAlcanceCatAlFinal(hoja, fila, cat);
        // Should-fix 3 (companion): AdjustToContents al final, después de
        // escribir las dos líneas de alcance — antes corría antes de
        // escribirlas, así que la columna nunca se dimensionaba para ese
        // texto (el más largo de la hoja).
        hoja.Columns().AdjustToContents();
    }

    private static void AgregarHojaGananciaProductora(
        XLWorkbook libro, IEnumerable<GananciaProductoraDto> datos, string? cat)
    {
        var hoja = libro.Worksheets.Add("Ganancias por productora");
        EscribirAlcanceCatAlInicio(hoja, cat);
        EscribirEncabezadosGanancias(hoja, new[]
        {
            "Productora", "Comunidad", "Centro de Acopio", "Cobrado local",
            "Pactado a cuotas", "Pagado planta", "N.º de pagos"
        }, fila: 2);

        int fila = 3;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.NombreProductora;
            hoja.Cell(fila, 2).Value = r.Comunidad;
            hoja.Cell(fila, 3).Value = r.CentroAcopio;
            hoja.Cell(fila, 4).Value = r.CobradoLocal;
            hoja.Cell(fila, 5).Value = r.PactadoCuotas;
            hoja.Cell(fila, 6).Value = r.PagadoPlanta;
            hoja.Cell(fila, 7).Value = r.TotalPagos;
            fila++;
        }
        EscribirAlcanceCatAlFinal(hoja, fila, cat);
        hoja.Columns().AdjustToContents();
    }

    private static void AgregarHojaGananciaMes(
        XLWorkbook libro, IEnumerable<GananciaMesDto> datos, string? cat)
    {
        var hoja = libro.Worksheets.Add("Ganancias por mes");
        EscribirAlcanceCatAlInicio(hoja, cat);
        EscribirEncabezadosGanancias(hoja, new[]
        {
            "Año", "Mes", "Cobrado local", "Pactado a cuotas",
            "Pagado planta", "N.º de pagos"
        }, fila: 2);

        int fila = 3;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.Anio;
            hoja.Cell(fila, 2).Value = r.Mes;
            hoja.Cell(fila, 3).Value = r.CobradoLocal;
            hoja.Cell(fila, 4).Value = r.PactadoCuotas;
            hoja.Cell(fila, 5).Value = r.PagadoPlanta;
            hoja.Cell(fila, 6).Value = r.TotalPagos;
            fila++;
        }
        EscribirAlcanceCatAlFinal(hoja, fila, cat);
        hoja.Columns().AdjustToContents();
    }

    // Las dos advertencias van DEBAJO de la tabla, en texto: un libro que
    // alguien lleva a una reunión no puede dejarlas solo en la pantalla de
    // origen. Un despacho sin precio no se vendió gratis; un animal cuya
    // productora no ha cobrado no costó cero — un margen que las omitiera
    // sería optimista justo cuando más falta pagar.
    private static void AgregarHojaMargen(
        XLWorkbook libro, string nombreHoja, IEnumerable<MargenDto> datos)
    {
        var hoja = libro.Worksheets.Add(nombreHoja);
        EscribirEncabezadosGanancias(hoja, new[]
        {
            "Agrupación", "Ingreso (neto de devoluciones)", "Costo atribuido", "Margen",
            "Despachos sin precio", "Animales sin costo", "Unidades devueltas"
        });

        int fila = 2;
        var totalDespachosSinPrecio = 0;
        var totalAnimalesSinCosto = 0;
        var totalUnidadesDevueltas = 0;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.Agrupacion;
            hoja.Cell(fila, 2).Value = r.Ingreso;
            hoja.Cell(fila, 3).Value = r.CostoAtribuido;
            hoja.Cell(fila, 4).Value = r.Margen;
            hoja.Cell(fila, 5).Value = r.DespachosSinPrecio;
            hoja.Cell(fila, 6).Value = r.AnimalesSinCosto;
            hoja.Cell(fila, 7).Value = r.UnidadesDevueltas;
            totalDespachosSinPrecio += r.DespachosSinPrecio;
            totalAnimalesSinCosto += r.AnimalesSinCosto;
            totalUnidadesDevueltas += r.UnidadesDevueltas;
            fila++;
        }

        // Debajo de la tabla, no en una columna más: son advertencias sobre
        // el libro entero, no un dato por fila.
        var filaAdvertencia = fila + 1;
        hoja.Cell(filaAdvertencia, 1).Value =
            $"Despachos sin precio (no se vendieron gratis): {totalDespachosSinPrecio}";
        hoja.Cell(filaAdvertencia, 1).Style.Font.Bold = true;
        hoja.Cell(filaAdvertencia, 1).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");

        hoja.Cell(filaAdvertencia + 1, 1).Value =
            "Animales sin costo (su productora no ha cobrado, no costaron " +
            $"cero): {totalAnimalesSinCosto}";
        hoja.Cell(filaAdvertencia + 1, 1).Style.Font.Bold = true;
        hoja.Cell(filaAdvertencia + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");

        // Tercer contador, mismo estilo que los dos de arriba: el Ingreso ya
        // es neto de devoluciones (S1), así que un despacho enteramente
        // devuelto aporta $0 sin dejar rastro en ninguna otra celda del
        // libro si esta línea no existiera. El rótulo NO dice "ya restadas
        // del ingreso" sin matiz: ConstruirMargen suma esta cifra por cada
        // despacho del grupo, con precio o sin él, así que un despacho
        // legado sin precio (el que cuenta la línea de arriba) que reciba
        // una devolución también suma aquí sin que haya nada de qué
        // restarlo — afirmarlo sin condición sería una relación que ese
        // caso no sostiene.
        hoja.Cell(filaAdvertencia + 2, 1).Value =
            "Unidades devueltas (restan del ingreso solo en despachos con " +
            $"precio): {totalUnidadesDevueltas}";
        hoja.Cell(filaAdvertencia + 2, 1).Style.Font.Bold = true;
        hoja.Cell(filaAdvertencia + 2, 1).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");

        // A diferencia de las tres hojas de ganancias, esta hoja (y su
        // hermana "Margen por cliente") IGNORA ?cat= a propósito: un
        // despacho reúne animales de varias jaulas y por tanto de varias
        // CAT (ver el comentario en DatosDeMargenAsync). Sin esta línea en
        // el libro, quien lo abra filtrado por una CAT puede leer esta hoja
        // como si también estuviera acotada.
        hoja.Cell(filaAdvertencia + 3, 1).Value =
            "Toda la cooperativa — este reporte no se filtra por centro de acopio";
        hoja.Cell(filaAdvertencia + 3, 1).Style.Font.Bold = true;
        hoja.Cell(filaAdvertencia + 3, 1).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");

        // El spec lo exige como requisito, no como sugerencia (ver su
        // sección "Fuera de alcance"): el margen es sobre el costo de los
        // animales, no un resultado contable de la cooperativa.
        hoja.Cell(filaAdvertencia + 4, 1).Value =
            "El margen es sobre el costo de los animales: no incluye " +
            "transporte, faenamiento ni empaque, así que no es un resultado " +
            "contable de la cooperativa.";
        hoja.Cell(filaAdvertencia + 4, 1).Style.Font.Bold = true;
        hoja.Cell(filaAdvertencia + 4, 1).Style.Font.FontColor = XLColor.FromHtml("#B71C1C");

        // Should-fix 3 (companion): AdjustToContents al final, después de
        // escribir las advertencias — antes corría antes de escribirlas, así
        // que la columna nunca se dimensionaba para ese texto (el más largo
        // de la hoja). Mismo orden que ya llevan las tres hojas de
        // ganancias arriba.
        hoja.Columns().AdjustToContents();
    }

    // Sexta hoja. Lleva su línea de alcance como las tres de ganancias
    // porque la asimetría del filtro también la afecta, y de una forma que
    // no se ve en la propia hoja: la columna de comunidad SÍ está filtrada
    // por CAT y la de despacho NO. Quien abra el libro con ?cat= puesto
    // tiene que poder saberlo sin salir de esta pestaña.
    private static void AgregarHojaUnidades(
        XLWorkbook libro, IEnumerable<UnidadesMesDto> datos, string? cat)
    {
        var hoja = libro.Worksheets.Add("Unidades vendidas");
        EscribirAlcanceCatAlInicio(hoja, cat);
        EscribirEncabezadosGanancias(hoja, new[]
        {
            "Mes", "Vendidas en la comunidad", "Despachadas a clientes",
            "Total de animales"
        }, fila: 2);

        int fila = 3;
        var totalComunidad = 0;
        var totalDespacho = 0;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.Agrupacion;
            hoja.Cell(fila, 2).Value = r.VendidasComunidad;
            hoja.Cell(fila, 3).Value = r.DespachadasClientes;
            hoja.Cell(fila, 4).Value = r.Total;
            totalComunidad += r.VendidasComunidad;
            totalDespacho += r.DespachadasClientes;
            fila++;
        }

        // El rótulo dice ANIMALES a propósito: en el resto del libro nada se
        // suma, y sin esa palabra esta línea podría leerse como permiso para
        // sumar también las cifras de dinero de las otras hojas.
        var filaTotal = fila + 1;
        hoja.Cell(filaTotal, 1).Value =
            $"Total de animales vendidos en el período: {totalComunidad + totalDespacho} " +
            $"({totalComunidad} en la comunidad + {totalDespacho} despachados)";
        hoja.Cell(filaTotal, 1).Style.Font.Bold = true;

        hoja.Cell(filaTotal + 1, 1).Value =
            "La columna de comunidad respeta el filtro por centro de acopio; " +
            "la de despacho no, porque un despacho mezcla animales de varias " +
            "jaulas y por tanto de varios centros.";

        EscribirAlcanceCatAlFinal(hoja, filaTotal + 1, cat);

        // Al final, cuando ya está todo escrito: si se ajusta antes, las
        // líneas de abajo no se tienen en cuenta para el ancho.
        hoja.Columns().AdjustToContents();
    }

    // ── Flujo de trazabilidad: Entrada / Tránsito / Salida ────────────

    // Entrada: lotes con llegada a planta confirmada y animales vivos que
    // aún no pasan al faenamiento (en espera).
    public async Task<IEnumerable<ReporteEntradaDto>> ReporteEntradaAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var lotes = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .Include(l => l.Movilizacion)
            .Where(l => l.Movilizacion != null
                     && l.Movilizacion.FechaRecepcionPlanta != null
                     && l.Movilizacion.FechaRecepcionPlanta >= desdeUtc
                     && l.Movilizacion.FechaRecepcionPlanta < hastaUtc)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync();

        if (filtro.CentroAcopio is not null)
            lotes = lotes.Where(l => l.CentroAcopio == filtro.CentroAcopio).ToList();

        return lotes
            .Select(l =>
            {
                var usados = l.Faenamientos.Sum(f =>
                    f.Cuyes.Count > 0 ? f.Cuyes.Count
                        : f.UnidadesFaenadas + f.UnidadesDecomisadas);
                var enEspera = Math.Max(0, l.CantidadAnimales - usados);
                var (prod, com) = ResumenProductoras(l);
                return new ReporteEntradaDto(
                    l.CodigoLote, l.CentroAcopio, prod, com,
                    enEspera, l.Movilizacion!.FechaRecepcionPlanta!.Value);
            })
            .Where(r => r.CantidadEnEspera > 0)
            .OrderBy(r => r.FechaLlegada)
            .ToList();
    }

    // Tránsito: lotes faenados completos en el período.
    public async Task<IEnumerable<ReporteTransitoDto>> ReporteTransitoAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var faes = await db.LotesFaenados
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Cuyes)
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                .ThenInclude(l => l.Productora)
            .Include(lf => lf.Sesiones).ThenInclude(f => f.Lote)
                .ThenInclude(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Where(lf => lf.FechaFaenamiento >= desdeUtc
                      && lf.FechaFaenamiento < hastaUtc)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync();

        if (filtro.CentroAcopio is not null)
            faes = faes.Where(lf =>
                lf.Sesiones.Any(s => s.Lote.CentroAcopio == filtro.CentroAcopio)).ToList();

        return faes
            .Select(lf =>
            {
                var jaulas = string.Join(", ", lf.Sesiones
                    .Select(s => s.Lote.CodigoLote).Distinct());
                var comunidades = string.Join(" y ", lf.Sesiones
                    .SelectMany(s => s.Cuyes
                        .Where(c => c.Estado != EstadoCanal.Rechazado)
                        .Select(c => s.Lote.Cuyes
                            .FirstOrDefault(x => x.NumeroEnLote == c.NumeroEnLote)
                            ?.Productora?.Comunidad.Nombre
                            ?? s.Lote.Productora?.Comunidad.Nombre ?? "—"))
                    .Distinct());
                var unidades = lf.Sesiones.Sum(s => s.UnidadesFaenadas);
                var pesoTotal = lf.Sesiones.Sum(s => s.PesoTotalCanalGramos);
                var promedio = unidades > 0
                    ? Math.Round(pesoTotal / unidades, 0) : 0;

                var cuyes = lf.Sesiones.SelectMany(s => s.Cuyes).ToList();
                var estado = cuyes.Count == 0 ? "—"
                    : cuyes.All(c => c.Estado == EstadoCanal.Rechazado) ? "Rechazado"
                    : cuyes.Any(c => c.Estado == EstadoCanal.ConNovedad) ? "Con novedad"
                    : "Apto";

                return new ReporteTransitoDto(
                    lf.Codigo, lf.FechaFaenamiento, lf.OperarioResponsable,
                    jaulas, comunidades, unidades, pesoTotal, promedio, estado);
            })
            .OrderByDescending(r => r.FechaFaenamiento)
            .ToList();
    }

    // Salida: despachos comerciales con datos de transporte.
    public async Task<IEnumerable<ReporteSalidaDto>> ReporteSalidaAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        var despachos = await db.Despachos
            .Include(d => d.LoteFaenado)
            .Include(d => d.Lote)
            .Where(d => d.FechaDespacho >= desdeUtc && d.FechaDespacho < hastaUtc)
            .OrderByDescending(d => d.FechaDespacho)
            .AsNoTracking()
            .ToListAsync();

        return despachos.Select(d => new ReporteSalidaDto(
            d.LoteFaenado != null ? d.LoteFaenado.Codigo
                : d.Lote != null ? d.Lote.CodigoLote : "—",
            d.FechaDespacho, d.ClienteDestino,
            string.IsNullOrWhiteSpace(d.Chofer) ? "—" : d.Chofer,
            string.IsNullOrWhiteSpace(d.Ruta) ? "—" : d.Ruta,
            string.IsNullOrWhiteSpace(d.TipoMercado) ? "Local" : d.TipoMercado,
            string.Join(", ", new[] { d.Ciudad, d.Pais }
                .Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } u
                ? u : "—",
            d.CantidadUnidades, d.Responsable)).ToList();
    }

    // Resumen de productoras de una jaula, agrupando por Id (nunca por
    // instancia: con AsNoTracking cada fila trae su propio objeto)
    private static (string Productora, string Comunidad) ResumenProductoras(Lote lote)
    {
        var grupos = lote.Cuyes
            .Where(c => c.Productora != null)
            .GroupBy(c => c.ProductoraId)
            .Select(g => g.First().Productora!)
            .ToList();

        if (grupos.Count == 0 && lote.Productora != null)
            grupos.Add(lote.Productora);

        if (grupos.Count == 0) return ("—", "—");
        if (grupos.Count == 1)
            return (grupos[0].NombreCompleto, grupos[0].Comunidad.Nombre);

        var comunidades = string.Join(" y ",
            grupos.Select(p => p.Comunidad.Nombre).Distinct());
        return ($"Varias productoras ({grupos.Count})", comunidades);
    }

    // ── Exportaciones Excel del flujo ─────────────────────────────────

    private static void EncabezadoExcel(IXLWorksheet hoja, string[] titulos)
    {
        for (int i = 0; i < titulos.Length; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = titulos[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
            celda.Style.Font.FontColor = XLColor.White;
        }
    }

    public async Task<byte[]> ExportarExcelEntradaAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReporteEntradaAsync(filtro);
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Entrada");
        EncabezadoExcel(hoja, ["Código lote", "CAT", "Productora",
            "Comunidad", "En espera", "Fecha de llegada"]);
        int fila = 2;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.CodigoLote;
            hoja.Cell(fila, 2).Value = r.CentroAcopio;
            hoja.Cell(fila, 3).Value = r.Productora;
            hoja.Cell(fila, 4).Value = r.Comunidad;
            hoja.Cell(fila, 5).Value = r.CantidadEnEspera;
            hoja.Cell(fila, 6).Value = FechaUtc.FechaLocal(r.FechaLlegada);
            fila++;
        }
        hoja.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportarExcelTransitoAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReporteTransitoAsync(filtro);
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Tránsito");
        EncabezadoExcel(hoja, ["Lote faenado", "Fecha", "Operario",
            "Jaulas de origen", "Comunidades", "Unidades",
            "Peso total (g)", "Peso prom. (g)", "Estado"]);
        int fila = 2;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.CodigoLoteFaenado;
            hoja.Cell(fila, 2).Value = FechaUtc.FechaLocal(r.FechaFaenamiento);
            hoja.Cell(fila, 3).Value = r.Operario;
            hoja.Cell(fila, 4).Value = r.JaulasOrigen;
            hoja.Cell(fila, 5).Value = r.Comunidades;
            hoja.Cell(fila, 6).Value = r.Unidades;
            hoja.Cell(fila, 7).Value = r.PesoTotalGramos;
            hoja.Cell(fila, 8).Value = r.PesoPromedioGramos;
            hoja.Cell(fila, 9).Value = r.Estado;
            fila++;
        }
        hoja.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportarExcelSalidaAsync(FiltroPeriodoDto filtro)
    {
        var datos = await ReporteSalidaAsync(filtro);
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Salida");
        EncabezadoExcel(hoja, ["Lote faenado", "Fecha", "Cliente",
            "Chofer", "Ruta", "Mercado", "Ubicación", "Unidades", "Responsable"]);
        int fila = 2;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.CodigoLoteFaenado;
            hoja.Cell(fila, 2).Value = FechaUtc.FechaLocal(r.FechaDespacho);
            hoja.Cell(fila, 3).Value = r.Cliente;
            hoja.Cell(fila, 4).Value = r.Chofer;
            hoja.Cell(fila, 5).Value = r.Ruta;
            hoja.Cell(fila, 6).Value = r.TipoMercado;
            hoja.Cell(fila, 7).Value = r.Ubicacion;
            hoja.Cell(fila, 8).Value = r.Unidades;
            hoja.Cell(fila, 9).Value = r.Responsable;
            fila++;
        }
        hoja.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Exportar Excel general: todos los dashboards en un libro ───────

    /// <summary>
    /// Un solo archivo con una hoja por dashboard, para no ir descargando
    /// siete por separado.
    ///
    /// Compone el libro copiando las hojas que ya generan los exportadores
    /// individuales, en vez de volver a maquetarlas aquí: así cada hoja tiene
    /// una única fuente de verdad y no se desincroniza de su versión suelta.
    /// El precio es serializar y releer cada libro, irrelevante con el volumen
    /// del piloto y a cambio de no duplicar siete maquetaciones.
    /// </summary>
    public async Task<byte[]> ExportarExcelGeneralAsync(
        FiltroPeriodoDto filtro, bool incluirFlujoOperativo = true)
    {
        // El orden sigue el flujo de la cadena: primero los tres eslabones
        // de trazabilidad, después los desgloses y las devoluciones.
        //
        // Los tres primeros se omiten para quien no puede consultarlos por
        // separado (el admin técnico): si no, la restricción de rol se
        // escaparía por esta descarga, que es una sola llamada a un endpoint
        // que sí conserva.
        var partes = new List<byte[]>();

        if (incluirFlujoOperativo)
        {
            partes.Add(await ExportarExcelEntradaAsync(filtro));
            partes.Add(await ExportarExcelTransitoAsync(filtro));
            partes.Add(await ExportarExcelSalidaAsync(filtro));
        }

        partes.Add(await ExportarExcelProductorasAsync(filtro));
        partes.Add(await ExportarExcelCATAsync(filtro));
        partes.Add(await ExportarExcelNovedadesAsync(filtro));
        partes.Add(await ExportarExcelCuyesAsync(filtro));
        // Aporta dos hojas: devoluciones de clientes y retornos
        partes.Add(await ExportarExcelDevolucionesAsync(filtro));

        using var libro = new XLWorkbook();
        foreach (var bytes in partes)
        {
            using var origen = new MemoryStream(bytes);
            using var libroOrigen = new XLWorkbook(origen);
            foreach (var hoja in libroOrigen.Worksheets)
                hoja.CopyTo(libro, hoja.Name);
        }

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
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

        // Animales procesados con su origen individual: la productora se
        // muestra en la ficha para poder devolverle en mano los animales
        // retornados vivos
        var animales = loteFaenado.Sesiones
            .SelectMany(f => f.Cuyes.Select(cf =>
            {
                var origen = f.Lote.Cuyes
                    .FirstOrDefault(c => c.NumeroEnLote == cf.NumeroEnLote)
                    ?.Productora;
                return (
                    Faenado: cf,
                    Jaula: f.Lote,
                    Comunidad: origen?.Comunidad.Nombre
                        ?? f.Lote.Productora?.Comunidad.Nombre ?? "—",
                    Productora: origen?.NombreCompleto
                        ?? f.Lote.Productora?.NombreCompleto ?? "—");
            }))
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
                page.DefaultTextStyle(t => t
                    .FontSize(10)
                    .FontFamily(BrandingAssets.FamiliaTipografica));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // 38 y no 30 como en la guía: estos documentos son A4.
                        row.ConstantItem(38).PaddingRight(10).AlignMiddle()
                            .Image(BrandingAssets.Logo).FitWidth();

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
                            c.Item().Text(FechaUtc.FechaLocal(DateTime.UtcNow))
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
                                $"Fecha: {FechaUtc.FechaHoraLocal(loteFaenado.FechaFaenamiento)}");
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
                                cols.ConstantColumn(25);   // N°
                                cols.RelativeColumn(2);    // Productora
                                cols.RelativeColumn(2);    // Comunidad
                                cols.ConstantColumn(55);   // Peso canal
                                cols.ConstantColumn(70);   // Estado
                                cols.RelativeColumn(2);    // Observación
                            });

                            tabla.Header(h =>
                            {
                                foreach (var titulo in new[]
                                    { "Jaula origen", "N°", "Productora",
                                      "Comunidad", "Peso canal", "Estado",
                                      "Observación" })
                                {
                                    h.Cell().BorderBottom(1).BorderColor("#CCCCCC")
                                        .PaddingBottom(2)
                                        .Text(titulo).FontSize(7).Bold();
                                }
                            });

                            foreach (var animal in animales)
                            {
                                // Un animal retornado vivo se distingue del
                                // decomiso: hay que devolverlo en mano a su
                                // productora
                                var estadoTexto = animal.Faenado.RetornadoAProductora
                                    ? "Devuelto vivo"
                                    : animal.Faenado.Estado.ToString();

                                var colorEstado = animal.Faenado.RetornadoAProductora
                                    ? "#E65100"
                                    : animal.Faenado.Estado switch
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
                                    .Text(animal.Productora).FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Comunidad).FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(animal.Faenado.PesoCanalGramos is decimal p
                                        ? $"{p:F0}g" : "—").FontSize(7);
                                tabla.Cell().PaddingVertical(1)
                                    .Text(estadoTexto).FontSize(7)
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
            // AsNoTracking sin resolución de identidad: a diferencia de una
            // consulta con tracking, este Include no comparte instancia con
            // el de l.Cuyes.ThenInclude(c => c.Productora), así que el
            // cantón hay que pedirlo aquí también o llega null.
            .Include(l => l.Productora).ThenInclude(p => p!.Comunidad)
                .ThenInclude(c => c.Canton)
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
                page.DefaultTextStyle(t => t
                    .FontSize(10)
                    .FontFamily(BrandingAssets.FamiliaTipografica));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // 38 y no 30 como en la guía: estos documentos son A4.
                        row.ConstantItem(38).PaddingRight(10).AlignMiddle()
                            .Image(BrandingAssets.Logo).FitWidth();

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
                            c.Item().Text(FechaUtc.FechaLocal(DateTime.UtcNow))
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
                                $"Comunidad: {lote.Productora?.Comunidad.Nombre ?? "-"}");
                        });
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Cantón: {lote.Productora?.Comunidad.Canton.Nombre ?? "-"}");
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
                                $"Fecha: {FechaUtc.FechaLocal(lote.FechaRecepcion)}");
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

                                    var comunidad = animal.Recepcion?.Productora?.Comunidad.Nombre
                                        ?? lote.Productora?.Comunidad.Nombre ?? "—";

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
                                    $"• {FechaUtc.FechaLocal(sesion.FechaFaenamiento)}: " +
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
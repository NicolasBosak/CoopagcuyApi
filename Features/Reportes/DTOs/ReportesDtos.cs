namespace CoopagcuyApi.Features.Reportes.DTOs;

// ── Dashboard — RF-508 ────────────────────────────────────────────────

public record DashboardDto(
    int LotesActivos,
    int AnimalesRecibidosPeriodo,
    decimal TasaAceptacion,
    decimal TasaConNovedad,
    decimal TasaRechazado,
    int LotesConQR,
    int TotalProductoras,
    int TotalFaenamientos,
    DateTime FechaCorte
);

// ── Reporte por productora — RF-501 ──────────────────────────────────

public record ReporteProductoraDto(
    int ProductoraId,
    string NombreProductora,
    string Comunidad,
    string CentroAcopio,
    int TotalLotes,
    int TotalAnimales,
    int LotesAceptados,
    int LotesConNovedad,
    int LotesRechazados,
    decimal PesoTotalGramos,
    decimal PesoPromedioGramos,
    DateTime? UltimaEntrega
);

// ── Reporte por CAT — RF-502 ──────────────────────────────────────────

public record ReporteCATDto(
    string CentroAcopio,
    int TotalLotes,
    int TotalAnimales,
    int LotesAceptados,
    int LotesConNovedad,
    int LotesRechazados,
    decimal TasaAceptacion,
    decimal PesoTotalGramos
);

// ── Reporte de novedades — RF-503 ─────────────────────────────────────

public record ReporteNovedadDto(
    int NovedadId,
    string CodigoLote,
    string NombreProductora,
    string Comunidad,
    string CentroAcopio,
    string TipoNovedad,
    string Descripcion,
    decimal? PesoRegistradoGramos,
    DateTime FechaRegistro,
    string RegistradoPor
);

// ── Filtros compartidos ───────────────────────────────────────────────

public record FiltroPeriodoDto(
    DateTime Desde,
    DateTime Hasta,
    string? CentroAcopio = null
);
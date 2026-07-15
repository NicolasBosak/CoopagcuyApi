namespace CoopagcuyApi.Features.Reportes.DTOs;

// ── Dashboard — RF-508 ────────────────────────────────────────────────

public record DashboardDto(
    int LotesActivos,
    int AnimalesRecibidosPeriodo,
    // Tasas sobre ANIMALES, no sobre jaulas: una jaula se marca con novedad
    // en cuanto un solo cuy la tiene, así que por jaula la aceptación caía a
    // 0% aunque 19 de 20 animales estuvieran perfectos.
    decimal TasaAceptacion,
    decimal TasaConNovedad,
    decimal TasaRechazado,
    // Desglose en números absolutos: el porcentaje solo no dice sobre cuántos
    int AnimalesAceptados,
    int AnimalesConNovedad,
    int AnimalesRechazados,
    int LotesConQR,
    int TotalProductoras,
    int TotalFaenamientos,
    DateTime FechaCorte,
    // Lo que ocurre DESPUÉS de la recepción. Son etapas distintas y no deben
    // mezclarse con el rechazo del CAT: aquí el animal ya entró a la cadena.
    int RetornosDesdePlanta,
    int DevolucionesClientes,
    int UnidadesDevueltas
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

// ── Reporte individual por cuy ────────────────────────────────────────

public record ReporteCuyDto(
    string CodigoLote,
    string NombreProductora,
    string Comunidad,
    string CentroAcopio,
    int NumeroEnLote,
    decimal PesoGramos,
    string ColorPelaje,
    string EstadoOreja,
    string TamanoAnimal,
    string Estado,
    string? MotivoNovedad,
    DateTime FechaRecepcion
);

// ── Reporte de devoluciones y retornos ────────────────────────────────

public record ReporteDevolucionesDto(
    int TotalDevolucionesClientes,
    int TotalUnidadesDevueltas,
    int TotalRetornosProductora,
    List<DevolucionItemDto> DevolucionesClientes,
    List<RetornoItemDto> RetornosProductora
);

public record DevolucionItemDto(
    int Id,
    string CodigoLote,
    int? NumeroSesion,
    string NombreProductora,
    string Comunidad,
    string ClienteDevuelve,
    DateTime FechaDevolucion,
    int CantidadUnidades,
    string Motivo
);

public record RetornoItemDto(
    int Id,
    string CodigoLote,
    string NombreProductora,
    string Comunidad,
    int NumeroEnLote,
    string Motivo,
    DateTime FechaRetorno,
    string Responsable
);

// ── Flujo de trazabilidad: Entrada / Tránsito / Salida ────────────────

// Entrada: cuyes que llegaron a planta, vivos, aún sin faenar
public record ReporteEntradaDto(
    string CodigoLote,
    string CentroAcopio,
    string Productora,
    string Comunidad,
    int CantidadEnEspera,
    DateTime FechaLlegada
);

// Tránsito: lote faenado completo con sus datos consolidados
public record ReporteTransitoDto(
    string CodigoLoteFaenado,
    DateTime FechaFaenamiento,
    string Operario,
    string JaulasOrigen,
    string Comunidades,
    int Unidades,
    decimal PesoTotalGramos,
    decimal PesoPromedioGramos,
    string Estado
);

// Salida: despacho comercial con datos de transporte y mercado
public record ReporteSalidaDto(
    string CodigoLoteFaenado,
    DateTime FechaDespacho,
    string Cliente,
    string Chofer,
    string Ruta,
    // Mercado de destino (Local/Nacional/Internacional) y su ubicación
    string TipoMercado,
    string Ubicacion,
    int Unidades,
    string Responsable
);

// ── Filtros compartidos ───────────────────────────────────────────────

public record FiltroPeriodoDto(
    DateTime Desde,
    DateTime Hasta,
    string? CentroAcopio = null
);
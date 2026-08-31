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

// ── Reporte de ganancias de productoras ───────────────────────────────
//
// El reporte entero publica dos cifras que NUNCA se suman: lo que ganaron
// las productoras (estos tres DTOs) y el margen de la reventa. Un pago a
// una productora es ingreso para ella y costo para la cooperativa — la
// misma fila leída desde dos lados.
//
// Dentro de esta mitad hay una segunda separación que tampoco se suma:
// cobrado es dinero que la CAT ya tiene en la mano, pactado es un
// compromiso a cuotas que todavía no ha llegado, y lo pagado por la planta
// es la otra vía de cobro. Sumarlas mostraría ganancias que la productora
// no tiene en caja.

public record GananciaProductoraDto(
    int ProductoraId,
    string NombreProductora,
    string Comunidad,
    string CentroAcopio,
    decimal CobradoLocal,
    decimal PactadoCuotas,
    decimal PagadoPlanta,
    int TotalPagos
);

public record GananciaCatDto(
    string CentroAcopio,
    decimal CobradoLocal,
    decimal PactadoCuotas,
    decimal PagadoPlanta,
    int TotalPagos
);

public record GananciaMesDto(
    int Anio,
    int Mes,
    decimal CobradoLocal,
    decimal PactadoCuotas,
    decimal PagadoPlanta,
    int TotalPagos
);

// ── Margen de la reventa ───────────────────────────────────────────────
//
// La otra mitad del reporte, y la que NUNCA se suma con las ganancias de
// productoras de arriba: un pago a una productora es ingreso para ella y
// costo para la cooperativa, la misma fila leída desde dos lados.
//
// DespachosSinPrecio y AnimalesSinCosto se muestran junto a la cifra en vez
// de contarse como cero: un despacho sin precio no se vendió gratis, y un
// animal cuya productora no ha cobrado no costó cero. Un margen que los
// ignorase sería optimista justo cuando más falta pagar.
//
// UnidadesDevueltas también se muestra junto a la cifra, por el mismo
// motivo: Ingreso ya es neto de devoluciones (S1), así que un despacho
// enteramente devuelto aporta $0 de ingreso sin dejar ningún rastro si esta
// columna no existiera.

public record MargenDto(
    string Agrupacion,
    decimal Ingreso,
    decimal CostoAtribuido,
    decimal Margen,
    int DespachosSinPrecio,
    int AnimalesSinCosto,
    int UnidadesDevueltas
);

// ── Unidades vendidas ─────────────────────────────────────────────────
//
// Las dos vías por las que se vende un cuy, separadas. Un cuy va por UNA
// de las dos y nunca por las dos: el sistema impide que un animal vendido
// en la comunidad acabe despachado —lo comprueban la movilización, el
// selector de lotes pendientes de pago, el botón "A planta" y el
// faenamiento—, así que aquí NO hay doble conteo y Total es un número
// real.
//
// Es la única excepción de este reporte: las cifras de dinero nunca se
// suman entre sí, porque un pago a una productora es ingreso para ella y
// costo para la cooperativa. Estas son animales, y sí se suman.
//
// DespachadasClientes va NETA de devoluciones, igual que MargenDto.Ingreso:
// si fuera bruta, las dos cifras se contradirían sobre el mismo despacho.
public record UnidadesMesDto(
    string Agrupacion,
    int VendidasComunidad,
    int DespachadasClientes,
    int Total
);

// ── Filtros compartidos ───────────────────────────────────────────────

// El ?cat= se normaliza en este único borde por donde entran los ~20
// filtros de lectura de Reportes (todos construyen este DTO desde el
// controlador): Postgres compara CentroAcopio distinguiendo mayúsculas, y
// un ?cat=pat sin normalizar dejaría a quien lo pidió viendo "Todos los
// centros" o una hoja vacía sin ningún error que lo explique. Una cadena
// vacía o solo espacios se guarda como null: eso sigue significando "sin
// filtro" en todo el feature.
public record FiltroPeriodoDto
{
    public DateTime Desde { get; }
    public DateTime Hasta { get; }
    public string? CentroAcopio { get; }

    public FiltroPeriodoDto(DateTime desde, DateTime hasta, string? centroAcopio = null)
    {
        Desde = desde;
        Hasta = hasta;
        CentroAcopio = string.IsNullOrWhiteSpace(centroAcopio)
            ? null
            : centroAcopio.Trim().ToUpperInvariant();
    }
}
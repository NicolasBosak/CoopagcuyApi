namespace CoopagcuyApi.Features.QR.DTOs;

public record GenerarQRRequestDto(
    string CodigoLote
);

public record QRResponseDto(
    int Id,
    string CodigoLote,
    string UrlPublica,
    string UrlQRImagen,
    bool Activo,
    DateTime FechaGeneracion
);

// Datos que ve el consumidor final al escanear el QR — RF-402
public record PaginaPublicaDto(
    string CodigoLote,
    string ComunidadOrigen,
    string Canton,
    string NombreProductora,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CantidadAnimales,
    string EstadoCalidad,
    List<string> ParametrosAprobados,
    DateTime FechaFaenamiento,
    decimal PesoPromedioCanalGramos,
    string EstadoCanal,
    string Marca,
    // Transporte CAT → Centro de Faenamiento (eslabón visible al consumidor)
    DateTime? FechaSalidaCat,
    DateTime? FechaLlegadaPlanta,
    // Trazabilidad hacia adelante: comercialización (último despacho)
    DateTime? FechaComercializacion,
    string? DestinoComercial,
    // Mercado de destino: Local | Nacional | Internacional, y su ubicación
    string? TipoMercado,
    string? UbicacionMercado,
    // Novedades registradas en planta sobre los animales faenados
    List<string> ObservacionesProceso,
    // Comunidades que aportaron animales, con su cantidad
    List<ComunidadAporteDto> ComunidadesAporte,
    // Estado individual de cada animal faenado
    List<CuyPublicoDto> DetalleCuyes
);

public record ComunidadAporteDto(string Comunidad, int Cantidad);

public record CuyPublicoDto(
    string Comunidad,
    string CodigoJaula,
    int NumeroEnLote,
    decimal? PesoCanalGramos,
    string Estado
);
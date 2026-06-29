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
    string Marca
);
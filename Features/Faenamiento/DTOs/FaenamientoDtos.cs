using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Faenamiento.DTOs;

public class RegistrarFaenamientoDto
{
    public int LoteId { get; set; }
    public DateTime FechaFaenamiento { get; set; }
    public string OperarioResponsable { get; set; } = string.Empty;
    public int UnidadesFaenadas { get; set; }
    public decimal PesoTotalCanalGramos { get; set; }   // referencia: 907g/canal
    public decimal? TemperaturaAlmacenamiento { get; set; }
    public EstadoCanal EstadoCanal { get; set; }
    public string? Observaciones { get; set; }

    // Decomisos y puntos de control intermedios del proceso
    public int UnidadesDecomisadas { get; set; } = 0;
    public string? MotivoDecomiso { get; set; }
    public int? TiempoLavadoMinutos { get; set; }
    public string? PresentacionEmpaque { get; set; }
    public DateTime? FechaIngresoFrio { get; set; }
    public DateTime? FechaSalidaFrio { get; set; }
}

public class RegistrarDespachoDto
{
    public int LoteId { get; set; }
    public string ClienteDestino { get; set; } = string.Empty;
    public DateTime FechaDespacho { get; set; }
    public int CantidadUnidades { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string? Transporte { get; set; }
    public string? Observaciones { get; set; }
}

public record FaenamientoResponseDto(
    int Id,
    int LoteId,
    string CodigoLote,
    string NombreProductora,
    string ComunidadOrigen,
    DateTime FechaFaenamiento,
    string OperarioResponsable,
    int UnidadesFaenadas,
    decimal PesoTotalCanalGramos,
    decimal PesoPromedioCanalGramos,
    decimal? TemperaturaAlmacenamiento,
    string EstadoCanal,
    string? Observaciones,
    int UnidadesDecomisadas,
    string? MotivoDecomiso,
    int? TiempoLavadoMinutos,
    string? PresentacionEmpaque,
    DateTime? FechaIngresoFrio,
    DateTime? FechaSalidaFrio
);

public record DespachoResponseDto(
    int Id,
    int LoteId,
    string CodigoLote,
    string ClienteDestino,
    DateTime FechaDespacho,
    int CantidadUnidades,
    string Responsable,
    string? Transporte,
    string? Observaciones
);

public class RegistrarDevolucionDto
{
    public int LoteId { get; set; }
    public string ClienteDevuelve { get; set; } = string.Empty;
    public DateTime FechaDevolucion { get; set; }
    public int CantidadUnidades { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

public record DevolucionResponseDto(
    int Id,
    int LoteId,
    string CodigoLote,
    string NombreProductora,
    string Comunidad,
    string ClienteDevuelve,
    DateTime FechaDevolucion,
    int CantidadUnidades,
    string Motivo,
    string Responsable,
    string? Observaciones
);

// Datos que se envían al codificador Ink Jet — RF-305
public record InkJetCodigoDto(
    string CodigoLote,
    string FechaFaenamiento,
    string FechaVencimiento,    // faenamiento + 5 días en frío
    string ComunidadOrigen,
    string NombreProductora,
    int UnidadesFaenadas,
    decimal PesoPromedioCanalGramos
);
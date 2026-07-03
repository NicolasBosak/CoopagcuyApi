using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Faenamiento.DTOs;

public class CuyFaenamientoDto
{
    public int NumeroEnLote { get; set; }
    public decimal? PesoCanalGramos { get; set; }
    public EstadoCanal Estado { get; set; }
    public string? Motivo { get; set; }
    // Si el animal no es apto y se devuelve a su productora de origen
    public bool RetornarAProductora { get; set; } = false;
}

public record CuyFaenamientoResponseDto(
    int Id,
    int NumeroEnLote,
    decimal? PesoCanalGramos,
    string Estado,
    string? Motivo,
    bool RetornadoAProductora
);

// ── Faenamiento por cuota: puede tomar animales de varios lotes ──────

public class RegistrarFaenamientoBatchDto
{
    public DateTime FechaFaenamiento { get; set; }
    public string OperarioResponsable { get; set; } = string.Empty;
    public decimal? TemperaturaAlmacenamiento { get; set; }
    public string? Observaciones { get; set; }
    public List<FaenamientoLoteDto> Lotes { get; set; } = [];
}

public class FaenamientoLoteDto
{
    public int LoteId { get; set; }
    public List<CuyFaenamientoDto> Cuyes { get; set; } = [];
}

public record FaenamientoBatchResultadoDto(
    List<FaenamientoResponseDto> Registros,
    // Novedades marcadas en planta que YA venían registradas desde el CAT,
    // con la productora que envió ese cuy específico
    List<AlertaNovedadPreviaDto> AlertasNovedadPrevia
);

public record AlertaNovedadPreviaDto(
    string CodigoLote,
    int NumeroEnLote,
    string NovedadRecepcion,
    string? NombreProductora,
    string? Comunidad
);

// Lotes con saldo pendiente de faenar y sus animales disponibles
public record LoteDisponibleDto(
    int LoteId,
    string CodigoLote,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CantidadAnimales,
    int Disponibles,
    List<CuyDisponibleDto> CuyesDisponibles
);

public record CuyDisponibleDto(
    int NumeroEnLote,
    decimal PesoGramos,
    string EstadoRecepcion,
    string? MotivoNovedad,
    string? NombreProductora,
    string? Comunidad
);

public record RetornoProductoraResponseDto(
    int Id,
    int LoteId,
    string CodigoLote,
    int ProductoraId,
    string NombreProductora,
    string Comunidad,
    int NumeroEnLote,
    string Motivo,
    DateTime FechaRetorno,
    string Responsable
);

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
    int NumeroSesion,
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
    DateTime? FechaSalidaFrio,
    List<CuyFaenamientoResponseDto> Cuyes
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
    // Sesión de faenamiento de la que proviene el producto devuelto
    public int? RegistroFaenamientoId { get; set; }
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
    int? NumeroSesion,
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
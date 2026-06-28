using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Recepcion.DTOs;

public class RegistrarLoteDto
{
    public int ProductoraId { get; set; }
    public CentroAcopio CentroAcopio { get; set; }
    public DateTime FechaRecepcion { get; set; }
    public int CantidadAnimales { get; set; }
    public decimal PesoTotalGramos { get; set; }
    public string ColorPelaje { get; set; } = string.Empty;
    public string EstadoOreja { get; set; } = string.Empty;
    public string TamanoAnimal { get; set; } = string.Empty;
    public bool EnAyunas { get; set; }
    public string ResponsableRecepcion { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public bool SincronizadoOffline { get; set; } = false;
    public string? DispositivoId { get; set; }
}

public class AgregarNovedadDto
{
    public int LoteId { get; set; }
    public TipoNovedad Tipo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal? PesoRegistradoGramos { get; set; }
}

public class SyncLotesDto
{
    public string DispositivoId { get; set; } = string.Empty;
    public List<RegistrarLoteDto> Lotes { get; set; } = [];
}

// Los Response DTOs se quedan como records (solo salida)
public record LoteResponseDto(
    int Id,
    string CodigoLote,
    int ProductoraId,
    string NombreProductora,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CantidadAnimales,
    decimal PesoTotalGramos,
    string Estado,
    string? ResponsableRecepcion,
    string? Observaciones,
    bool SincronizadoOffline,
    List<NovedadResponseDto> Novedades
);

public record NovedadResponseDto(
    int Id,
    string Tipo,
    string Descripcion,
    decimal? PesoRegistradoGramos,
    DateTime FechaRegistro,
    string RegistradoPor
);

public record SyncResultadoDto(
    int TotalRecibidos,
    int TotalGuardados,
    int TotalConError,
    List<SyncErrorDto> Errores
);

public record SyncErrorDto(
    string DispositivoId,
    string CodigoLoteTemp,
    string Motivo
);
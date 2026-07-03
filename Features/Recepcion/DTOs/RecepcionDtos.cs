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

    // Condición sanitaria visual: null/vacío = sin signos clínicos
    public string? SignosClinicos { get; set; }

    // Registro individual por animal. Si viene con datos, la evaluación
    // se hace cuy por cuy y los totales del lote se derivan de aquí.
    // Si viene vacío se usa el flujo agregado (registros offline antiguos).
    public List<CuyRegistroDto> Cuyes { get; set; } = [];

    public bool SincronizadoOffline { get; set; } = false;
    public string? DispositivoId { get; set; }
}

public class CuyRegistroDto
{
    public decimal PesoGramos { get; set; }
    public string ColorPelaje { get; set; } = string.Empty;
    public string EstadoOreja { get; set; } = string.Empty;
    public string TamanoAnimal { get; set; } = string.Empty;
    public string? SignosClinicos { get; set; }
}

public record CuyRegistroResponseDto(
    int Id,
    int NumeroEnLote,
    decimal PesoGramos,
    string ColorPelaje,
    string EstadoOreja,
    string TamanoAnimal,
    string? SignosClinicos,
    string Estado,
    string? MotivoNovedad
);

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
    List<NovedadResponseDto> Novedades,
    List<CuyRegistroResponseDto> Cuyes
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

// ── Movilización CAT → planta (eslabón transporte) ────────────────────

public class RegistrarMovilizacionDto
{
    public DateTime FechaDespacho { get; set; }
    public string Conductor { get; set; } = string.Empty;
    public int CantidadMovilizada { get; set; }
    public string? CondicionesTransporte { get; set; }
    public string? TipoForraje { get; set; }
    public int? DiasRetiroMedicamentos { get; set; }
    public string ResponsableDespacho { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

public class ConfirmarRecepcionPlantaDto
{
    public DateTime FechaRecepcionPlanta { get; set; }
    public string RecibidoPor { get; set; } = string.Empty;
    public string? CondicionLlegada { get; set; }
}

public record MovilizacionResponseDto(
    int Id,
    int LoteId,
    string CodigoLote,
    string CentroAcopio,
    string NombreProductora,
    DateTime FechaDespacho,
    string Conductor,
    int CantidadMovilizada,
    string? CondicionesTransporte,
    string? TipoForraje,
    int? DiasRetiroMedicamentos,
    string ResponsableDespacho,
    string? Observaciones,
    DateTime? FechaRecepcionPlanta,
    string? RecibidoPor,
    string? CondicionLlegada
);
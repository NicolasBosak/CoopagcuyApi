using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Recepcion.DTOs;

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
    string? MotivoNovedad,
    string? NombreProductora
);

// ── Entregas por productora: la jaula (lote) se arma acumulando ───────

public class RegistrarEntregaDto
{
    public CentroAcopio CentroAcopio { get; set; }
    public int ProductoraId { get; set; }
    public DateTime FechaEntrega { get; set; }
    public List<CuyRegistroDto> Cuyes { get; set; } = [];
    public bool EnAyunas { get; set; } = true;
    public string ResponsableRecepcion { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public bool SincronizadoOffline { get; set; } = false;
    public string? DispositivoId { get; set; }
}

public record EntregaResultadoDto(
    int CuyesRegistrados,
    // Lotes que recibieron cuyes de esta entrega (puede dividirse en dos
    // si la jaula actual se completó a mitad de la entrega)
    List<LoteResponseDto> LotesAfectados,
    bool SeCompletoJaula
);

public record ProductoraEnLoteDto(
    int ProductoraId,
    string Nombre,
    string Comunidad,
    int Cantidad
);

public class SyncEntregasDto
{
    public string DispositivoId { get; set; } = string.Empty;
    public List<RegistrarEntregaDto> Entregas { get; set; } = [];
}

// Los Response DTOs se quedan como records (solo salida)
public record LoteResponseDto(
    int Id,
    string CodigoLote,
    int? ProductoraId,
    string NombreProductora,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CantidadAnimales,
    decimal PesoTotalGramos,
    string Estado,
    string? ResponsableRecepcion,
    string? Observaciones,
    bool SincronizadoOffline,
    bool Cerrado,
    int Disponibles,
    List<ProductoraEnLoteDto> Productoras,
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
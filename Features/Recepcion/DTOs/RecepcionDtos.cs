namespace CoopagcuyApi.Features.Recepcion.DTOs;

public class CuyRegistroDto
{
    public decimal PesoGramos { get; set; }
    public string ColorPelaje { get; set; } = string.Empty;
    public string EstadoOreja { get; set; } = string.Empty;
    public string TamanoAnimal { get; set; } = string.Empty;
    public string? SignosClinicos { get; set; }

    // Evidencia del defecto, en base64 y sin prefijo data:. Viaja dentro del
    // cuy y no por multipart aparte: así el sync offline no cambia de forma.
    //
    // OJO: la idempotencia por IdCliente NO cubre la subida de la foto. La
    // subida (SubirEvidenciasAsync) corre ANTES de la estrategia de
    // ejecución, y la comprobación de SyncEntregasProcesadas vive DENTRO de
    // la transacción; un reintento tras una respuesta perdida vuelve a subir
    // todas las fotos como blobs nuevos antes de descubrir que la entrega ya
    // estaba procesada. Lo único idempotente es la fila que queda en la base.
    public string? FotoBase64 { get; set; }
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
    public string CentroAcopio { get; set; } = string.Empty;
    public int ProductoraId { get; set; }
    // Captura offline sin catálogo: cuando el dispositivo no tiene el Id de
    // la productora, envía su cédula y el servidor la resuelve al sincronizar.
    // Si la cédula es válida pero no coincide con ninguna productora del
    // centro, la entrega va a la bandeja de vinculación.
    public string? CedulaProductora { get; set; }
    // Momento de captura sellado por la tablet, no editable por la operadora.
    // Solo se usa en el sync offline —donde es obligatorio— porque la entrega
    // pudo capturarse días antes de recuperar señal. En línea lo sella el
    // servidor y este campo se ignora.
    public DateTime? FechaCapturaOffline { get; set; }
    public List<CuyRegistroDto> Cuyes { get; set; } = [];
    public bool EnAyunas { get; set; } = true;
    public string ResponsableRecepcion { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public bool SincronizadoOffline { get; set; } = false;
    public string? DispositivoId { get; set; }
    // Id generado por el dispositivo (UUID de IndexedDB): clave de
    // idempotencia del sync offline. Nulo en registros en línea.
    public string? IdCliente { get; set; }
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
    // Ya tiene registro de movilización hacia la planta
    bool TieneMovilizacion,
    // Animales del lote ya vendidos en la comunidad (venta local pagada)
    int CuyesVendidosLocal,
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
    string RegistradoPor,
    // El front solo necesita saber si hay algo que pedir: el binario se
    // descarga aparte y solo si la ficha se abre.
    bool TieneFoto
);

// Resultado del sync: un ítem POR CADA entrega recibida, identificado por
// el IdCliente del dispositivo. El cliente empareja por ese Id (nunca por
// posición) para marcar cada entrega local como sincronizada o con error.
public record SyncResultadoDto(
    int TotalRecibidos,
    int TotalGuardados,
    int TotalDuplicados,
    int TotalConError,
    // Entregas con cédula válida sin productora: encoladas en la bandeja
    int TotalPendientesVinculacion,
    List<SyncItemResultadoDto> Resultados
);

public record SyncItemResultadoDto(
    string? IdCliente,
    bool Exito,
    // La entrega ya se había sincronizado en un intento anterior:
    // se ignora sin duplicar animales y el cliente la marca como lista
    bool Duplicada,
    // Cédula válida sin productora en el centro: la entrega quedó en la
    // bandeja de vinculación. El cliente la marca "en revisión" y no la
    // reenvía; un administrador la asigna a la productora correcta.
    bool PendienteVinculacion,
    string? Motivo
);

// ── Bandeja de vinculación (admin) ────────────────────────────────────

public record VinculacionPendienteDto(
    int Id,
    string Cedula,
    string CentroAcopio,
    DateTime FechaCaptura,
    bool EnAyunas,
    string ResponsableRecepcion,
    string? Observaciones,
    int CantidadCuyes,
    decimal PesoTotalGramos,
    string DispositivoId,
    DateTime FechaCreacion
);

public record ResolverVinculacionDto(int ProductoraId);

// ── Movilización CAT → planta (eslabón transporte) ────────────────────

// La fecha de salida y la de llegada a planta las sella el servidor: son el
// momento en que ocurre el registro, no un dato que el operador elija.
public class RegistrarMovilizacionDto
{
    public string Conductor { get; set; } = string.Empty;
    public int CantidadMovilizada { get; set; }
    // Claves del catálogo CondicionTransporte que el operador verificó.
    // Ya no es texto libre: el servidor arma la descripción canónica.
    public List<string> CondicionesTransporte { get; set; } = [];
    public string? TipoForraje { get; set; }

    // Obligatoria y true: la exige RegistrarMovilizacionValidator. Es bool?
    // y no bool para poder distinguir "no vino en el cuerpo" de "vino false"
    // y dar un mensaje de error distinto en cada caso.
    public bool? SinAntibioticos7Dias { get; set; }
    public string ResponsableDespacho { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

public class ConfirmarRecepcionPlantaDto
{
    public string RecibidoPor { get; set; } = string.Empty;

    // Observación libre de siempre. El catálogo dice QUÉ pasó; esto dice qué
    // vio el operador con sus palabras.
    public string? CondicionLlegada { get; set; }

    // Obligatoria cuando el checklist de transporte salió incompleto.
    public bool? LlegaronEnBuenEstado { get; set; }

    // Solo se leen cuando LlegaronEnBuenEstado es false.
    public List<string> CondicionesLlegada { get; set; } = [];
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
    string? CondicionLlegada,
    bool? SinAntibioticos7Dias,
    string? CondicionesClaves,
    bool? LlegaronEnBuenEstado,
    string? CondicionesLlegadaClaves
);
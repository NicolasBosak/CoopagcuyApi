namespace CoopagcuyApi.Features.Pagos.DTOs;

/// <summary>
/// Alta de un ticket por la operadora del CAT. El método de pago no viaja:
/// desde el paso a transferencia única lo fija el servidor.
/// </summary>
public class RegistrarPagoDto
{
    public int ProductoraId { get; set; }
    // Obligatorio pese a ser anulable: se valida en el servicio para poder
    // responder 409 con un mensaje legible en vez de un 400 de modelo.
    public int? LoteId { get; set; }
    public decimal MontoUsd { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

// Lote por el que aún se le debe pagar a una productora. Cantidad y peso son
// el aporte de ESA productora a la jaula, no el total de la jaula.
public record LotePendientePagoDto(
    int LoteId,
    string CodigoLote,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CuyesEntregados,
    decimal PesoEntregadoGramos
);

public record PagoResponseDto(
    int Id,
    int ProductoraId,
    string NombreProductora,
    int? LoteId,
    string? CodigoLote,
    decimal MontoUsd,
    DateTime FechaPago,
    string MetodoPago,
    string Estado,
    decimal? MontoPagadoUsd,
    DateTime? FechaPagoEfectivo,
    string? PagadoPor,
    // No se expone la URL del blob: el comprobante se sirve por su propio
    // endpoint autenticado. Un booleano basta para decidir si pintar el visor.
    bool TieneComprobante,
    DateTime? FechaVerificacion,
    string? VerificadoPor,
    string Responsable,
    string? Observaciones,
    bool EsVentaLocal
);

/// <summary>
/// Venta de parte de una jaula en la comunidad. Los animales viajan en la
/// petición uno a uno: la operadora elige cuáles, no cuántos, porque después
/// hay que decir en la guía exactamente qué se fue.
/// </summary>
public class RegistrarVentaLocalDto
{
    public int ProductoraId { get; set; }
    public int LoteId { get; set; }
    public List<int> CuyRegistroIds { get; set; } = [];
    public decimal MontoUsd { get; set; }

    // Efectivo | Transferencia | Cuotas
    public string MetodoPago { get; set; } = string.Empty;

    // Solo para "Cuotas": el acuerdo, sin seguimiento de qué cuota se pagó.
    public int? NumeroDias { get; set; }
    public decimal? ValorPorDia { get; set; }

    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

/// <summary>
/// Cuy de esa productora en ese lote que todavía puede venderse. Alimenta las
/// casillas del formulario de venta local.
/// </summary>
public record CuyDisponibleDto(
    int CuyRegistroId,
    int NumeroEnLote,
    decimal PesoGramos,
    string Estado,
    string? MotivoNovedad
);

/// <summary>
/// Ticket que la planta tiene pendiente de pagar. Sin filtro por centro: la
/// planta es única y atiende a los tres CAT.
/// </summary>
public record TicketPorPagarDto(
    int PagoId,
    int ProductoraId,
    string NombreProductora,
    string Cedula,
    int LoteId,
    string CodigoLote,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CuyesEntregados,
    decimal MontoUsd,
    DateTime FechaEmision
);

/// <summary>
/// Cuy de esa productora en ese lote que llegó con novedad del CAT. Es la
/// única lista sobre la que la planta puede descontar: `NovedadId` es lo que
/// después va a `DescuentoPago.NovedadCatId`.
/// </summary>
public record CuyConNovedadDto(
    int CuyRegistroId,
    int NumeroEnLote,
    decimal PesoGramos,
    int NovedadId,
    string TipoNovedad,
    string Descripcion,
    // Para decidir si pintar el visor de la foto sin pedirla de más
    bool TieneFoto
);

/// <summary>
/// Rebaja que aplica la planta. `NovedadCatId` cita la novedad del CAT que la
/// justifica; sin ella el descuento no se acepta.
/// </summary>
public record DescuentoDto(int NovedadCatId, string Descripcion, decimal MontoUsd);

/// <summary>
/// Pago efectivo por la planta. Descuentos, captura y cambio de estado viajan
/// juntos a propósito: un ticket marcado como pagado sin su comprobante
/// dejaría a la CAT sin nada que verificar.
/// </summary>
public class RegistrarPagoEfectivoDto
{
    public List<DescuentoDto> Descuentos { get; set; } = [];
    public string ComprobanteBase64 { get; set; } = string.Empty;
    public string PagadoPor { get; set; } = string.Empty;
}

/// <summary>
/// Confirmación de la CAT de que el dinero llegó. Solo el nombre de quien
/// confirma: la fecha la sella el servidor, como todas las del sistema.
/// </summary>
public class VerificarPagoDto
{
    public string VerificadoPor { get; set; } = string.Empty;
}

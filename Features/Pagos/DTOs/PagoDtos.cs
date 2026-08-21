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
    string? Observaciones
);

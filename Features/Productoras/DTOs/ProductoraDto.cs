using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Productoras.DTOs;

public class CrearProductoraDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    // Comunidad del catálogo. El cantón ya no se envía: se deriva de ella.
    public int ComunidadId { get; set; }
    public CentroAcopio CatAsignado { get; set; }
    public string? Telefono { get; set; }
}

public record ProductoraResponseDto(
    int Id,
    string NombreCompleto,
    string Cedula,
    int ComunidadId,
    string Comunidad,
    string Canton,
    string CatAsignado,
    string? Telefono,
    bool Activa,
    DateTime FechaRegistro,
    // Cuyes retornados desde la planta por no aptos (seguimiento)
    int TotalRetornos = 0
);

// Historial de cambios de una productora — RF-105
public record ProductoraCambioDto(
    int Id,
    string CampoModificado,
    string? ValorAnterior,
    string? ValorNuevo,
    string ModificadoPor,
    DateTime FechaCambio
);

// ── Pagos a productoras (registro digital, antes cuaderno manual) ─────

public class RegistrarPagoDto
{
    public int ProductoraId { get; set; }
    public int? LoteId { get; set; }
    public decimal MontoUsd { get; set; }
    // La fecha del pago la sella el servidor al registrarlo
    public string MetodoPago { get; set; } = string.Empty; // Contado | Credito
    // Solo para crédito: en cuántos días se difiere. El valor por día lo
    // calcula el servidor (monto ÷ número de días).
    public int? NumeroDias { get; set; }
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
    int? NumeroDias,
    decimal? ValorPorDia,
    string Responsable,
    string? Observaciones
);
using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Productoras.DTOs;

public class CrearProductoraDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string Comunidad { get; set; } = string.Empty;
    public string Canton { get; set; } = string.Empty;
    public CentroAcopio CatAsignado { get; set; }
    public string? Telefono { get; set; }
}

public record ProductoraResponseDto(
    int Id,
    string NombreCompleto,
    string Cedula,
    string Comunidad,
    string Canton,
    string CatAsignado,
    string? Telefono,
    bool Activa,
    DateTime FechaRegistro
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
    public DateTime FechaPago { get; set; }
    public string MetodoPago { get; set; } = string.Empty; // Efectivo | Transferencia
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

public record PagoResponseDto(
    int Id,
    int ProductoraId,
    string NombreProductora,
    int? LoteId,
    string? CodigoLote,
    decimal MontoUsd,
    DateTime FechaPago,
    string MetodoPago,
    string Responsable,
    string? Observaciones
);
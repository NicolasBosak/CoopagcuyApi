namespace CoopagcuyApi.Features.Productoras.DTOs;

public class CrearProductoraDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    // Comunidad del catálogo. El cantón ya no se envía: se deriva de ella.
    public int ComunidadId { get; set; }
    public string CatAsignado { get; set; } = string.Empty;
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


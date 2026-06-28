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
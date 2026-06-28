using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Productoras.DTOs;

public record CrearProductoraDto(
    string NombreCompleto,
    string Cedula,
    string Comunidad,
    string Canton,
    CentroAcopio CatAsignado,
    string? Telefono
);

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
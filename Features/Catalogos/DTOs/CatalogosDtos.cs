using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Catalogos.DTOs;

public class GuardarComunidadDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Canton { get; set; } = string.Empty;
    public CentroAcopio CatReferencia { get; set; }
}

public record ComunidadResponseDto(
    int Id,
    string Nombre,
    string Canton,
    string CatReferencia,
    bool Activa
);

// Catálogo de centros de acopio (fijo, derivado del enum)
public record CentroAcopioDto(string Codigo, string Nombre);

// Condición verificable del checklist de transporte CAT → planta
public record CondicionTransporteDto(string Clave, string Etiqueta);

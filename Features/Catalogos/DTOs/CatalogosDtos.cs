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

// ── Geografía ────────────────────────────────────────────────────────

public class GuardarProvinciaDto
{
    public string Nombre { get; set; } = string.Empty;
}

// TotalCantones acompaña a la provincia para que Administración pueda
// explicar por qué una baja fue rechazada sin pedir otra consulta.
public record ProvinciaDto(int Id, string Nombre, bool Activa, int TotalCantones);

public class GuardarCantonDto
{
    public string Nombre { get; set; } = string.Empty;
    public int ProvinciaId { get; set; }
}

public record CantonDto(
    int Id, string Nombre, int ProvinciaId, string Provincia,
    bool Activo, int TotalComunidades);

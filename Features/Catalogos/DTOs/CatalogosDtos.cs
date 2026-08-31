namespace CoopagcuyApi.Features.Catalogos.DTOs;

public class GuardarComunidadDto
{
    public string Nombre { get; set; } = string.Empty;
    public int CantonId { get; set; }
    public string CatReferencia { get; set; } = string.Empty;
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public int? AltitudMinM { get; set; }
    public int? AltitudMaxM { get; set; }
}

public record ComunidadResponseDto(
    int Id,
    string Nombre,
    int CantonId,
    string Canton,
    string Provincia,
    string CatReferencia,
    bool Activa,
    decimal? Latitud,
    decimal? Longitud,
    int? AltitudMinM,
    int? AltitudMaxM
);

// Centro de acopio del catálogo. Antes era `(Codigo, Nombre)` derivado del
// enum; los campos nuevos son aditivos, así que un cliente viejo sigue
// leyendo codigo y nombre sin enterarse.
public record CentroAcopioDto(
    string Codigo, string Nombre, int CantonId, string Canton,
    string Provincia, bool Activo);

public class CrearCentroAcopioDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int CantonId { get; set; }
}

// Sin Codigo a propósito: es inmutable. No ofrecerlo en el contrato dice más
// que aceptarlo para después rechazarlo.
public class ActualizarCentroAcopioDto
{
    public string Nombre { get; set; } = string.Empty;
    public int CantonId { get; set; }
}

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

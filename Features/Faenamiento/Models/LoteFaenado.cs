namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Lote de producto faenado: agrupa una sesión completa de planta bajo un
/// código propio (FAE-AAAAMMDD-SEC). Puede tomar animales de varias jaulas
/// de recepción —y por lo tanto de varias comunidades— pero genera un solo
/// código QR y una sola ficha, evitando informes duplicados.
/// </summary>
public class LoteFaenado
{
    public int Id { get; set; }

    // Código único del producto terminado: FAE-20260703-001
    public string Codigo { get; set; } = string.Empty;

    public DateTime FechaFaenamiento { get; set; }
    public string OperarioResponsable { get; set; } = string.Empty;
    public decimal? TemperaturaAlmacenamiento { get; set; }
    public string? Observaciones { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Sesiones por jaula de origen que componen este lote faenado
    public ICollection<RegistroFaenamiento> Sesiones { get; set; } = [];
}

using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Faenamiento.Models;

/// <summary>
/// Estado individual de cada cuy durante el faenamiento.
/// Permite registrar por animal el peso de canal y las novedades,
/// y marcar el retorno a la productora cuando no es apto.
/// </summary>
public class CuyFaenamiento
{
    public int Id { get; set; }
    public int RegistroFaenamientoId { get; set; }
    public RegistroFaenamiento Registro { get; set; } = null!;

    // Posición del animal dentro del lote (correlativo con la recepción)
    public int NumeroEnLote { get; set; }

    public decimal? PesoCanalGramos { get; set; }
    public EstadoCanal Estado { get; set; }
    public string? Motivo { get; set; }

    // Si el animal no es apto y se devuelve a su productora de origen
    public bool RetornadoAProductora { get; set; } = false;
}

using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Recepcion.Models;

public class Novedad
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public TipoNovedad Tipo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    public string RegistradoPor { get; set; } = string.Empty;

    // Para novedades de peso: guardar el peso registrado
    public decimal? PesoRegistradoGramos { get; set; }
}
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

    // Animal al que pertenece la novedad. Antes solo lo decía el texto
    // "Cuy #N: …" de la descripción, que sirve para leerlo pero no para
    // relacionarlo: el faenamiento necesita mostrar la evidencia JUNTO al
    // cuy correcto, y adjudicar un defecto al animal equivocado es peor
    // que no mostrar nada.
    //
    // Anulable por dos motivos distintos: las novedades de la entrega
    // (SinAyuno) no pertenecen a ningún animal, y las filas anteriores a
    // este cambio se quedan sin enlace. Lo segundo no cuesta nada: ninguna
    // novedad histórica tiene foto, así que no hay nada que rellenar.
    public int? CuyRegistroId { get; set; }
    public CuyRegistro? CuyRegistro { get; set; }

    // Evidencia fotográfica para reclamar al proveedor. Solo la llevan las
    // novedades de tipo SignosClinicos. El binario vive en Blob, no en la
    // base: aquí queda la referencia y la fecha en que deja de ser válida.
    public string? FotoUrl { get; set; }

    // El blob lo borra una política de ciclo de vida de Azure a los 90 días.
    // Esta fecha permite que el API deje de servir la foto en el momento
    // exacto, sin depender de cuándo pase el barrido de Azure.
    public DateTime? FotoExpiraEn { get; set; }
}
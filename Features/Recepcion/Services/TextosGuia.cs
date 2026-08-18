using CoopagcuyApi.Features.Recepcion.Models;

namespace CoopagcuyApi.Features.Recepcion.Services;

/// <summary>
/// Composición de las celdas del detalle por animal de la guía de
/// movilización.
///
/// Vive fuera del armado del PDF a propósito. Estas dos celdas acumularon dos
/// defectos que llegaron a producción —el objeto Comunidad interpolado en vez
/// de su nombre, y tres valores sin rótulo que se leían como una lista de
/// opciones— y ninguno era detectable desde una prueba: el PDF es binario y
/// comprime sus flujos de texto, así que no hay forma razonable de afirmar
/// nada sobre su contenido. Como funciones puras sí se comprueban.
/// </summary>
public static class TextosGuia
{
    /// <summary>
    /// "Nombre de la productora (Comunidad)", o "—" si el animal no tiene
    /// productora asociada (jaula antigua sin detalle por animal).
    /// </summary>
    public static string Productora(CuyRegistro cuy)
    {
        if (cuy.Productora is null) return "—";

        // .Comunidad.Nombre, no .Comunidad: lo segundo interpola la ENTIDAD y
        // su ToString() imprime el nombre de la clase dentro del paréntesis.
        var comunidad = cuy.Productora.Comunidad?.Nombre;

        return string.IsNullOrWhiteSpace(comunidad)
            ? cuy.Productora.NombreCompleto
            : $"{cuy.Productora.NombreCompleto} ({comunidad})";
    }

    /// <summary>
    /// "Pelaje: Blanco · Oreja: Blanda · Tamaño: Normal".
    ///
    /// Con rótulo: sin él, "Blanco · Blanda · Normal" se lee como una lista de
    /// opciones disponibles y no como los datos de ESTE animal, que es lo que
    /// son. Fue justo la confusión que se reportó desde el CAT.
    /// </summary>
    public static string Caracteristicas(CuyRegistro cuy) =>
        string.Join(" · ", new[]
        {
            Rotulado("Pelaje", cuy.ColorPelaje),
            Rotulado("Oreja", cuy.EstadoOreja),
            Rotulado("Tamaño", cuy.TamanoAnimal),
        }.Where(t => t is not null));

    private static string? Rotulado(string rotulo, string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : $"{rotulo}: {valor}";
}

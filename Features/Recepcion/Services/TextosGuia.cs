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

    /// <summary>
    /// Claves guardadas en una columna, partidas por su separador. Nulo o
    /// vacío devuelve una lista vacía, no una lista con una cadena vacía
    /// dentro — eso último haría que NoVerificadas creyera que hay una clave
    /// marcada que no existe.
    /// </summary>
    public static IReadOnlyList<string> ClavesDe(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(CondicionTransporte.Separador,
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                 .ToList();

    /// <summary>
    /// "No se verificó: Ventilación adecuada, Vehículo limpio", o NULO cuando
    /// no hay nada que decir.
    ///
    /// Devuelve nulo en DOS casos distintos que la guía trata igual —no
    /// imprimir nada— pero que no son lo mismo: con todas las condiciones
    /// marcadas no falta ninguna, y en una movilización anterior a esta
    /// feature no se registró cuáles se marcaron. Afirmar en ese segundo caso
    /// que "no se verificó ninguna" sería inventar un dato que nadie guardó.
    /// </summary>
    public static string? LineaNoVerificadas(string? clavesCsv)
    {
        if (string.IsNullOrWhiteSpace(clavesCsv)) return null;

        var faltan = CondicionTransporte.NoVerificadas(ClavesDe(clavesCsv));
        return faltan.Count == 0
            ? null
            : $"No se verificó: {string.Join(", ", faltan)}";
    }
}

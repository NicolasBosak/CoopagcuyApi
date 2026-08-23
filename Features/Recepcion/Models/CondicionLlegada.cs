namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Catálogo cerrado de lo que un operador de planta puede constatar al abrir
/// la jaula cuando los animales NO llegaron en buen estado.
///
/// Cerrado por el mismo motivo que CondicionTransporte: un campo abierto hace
/// que cada planta escriba lo suyo y que después no se pueda contar ni
/// comparar nada. El servidor solo acepta estas claves; el texto que se
/// guarda y se imprime lo pone él, no el operador.
/// </summary>
public static class CondicionLlegada
{
    public static readonly IReadOnlyDictionary<string, string> Catalogo =
        new Dictionary<string, string>
        {
            ["AnimalesGolpeados"] = "Animales con golpes o heridas",
            ["AnimalesDeshidratados"] = "Animales deshidratados o decaídos",
            ["AnimalesMuertos"] = "Animales muertos",
            ["JaulasSucias"] = "Jaulas sucias o con excretas",
            ["Hacinamiento"] = "Hacinamiento en la jaula",
            ["JaulasDanadas"] = "Jaulas rotas o mal aseguradas",
            ["Otro"] = "Otra condición (ver observación)",
        };

    public static bool EsValida(string clave) => Catalogo.ContainsKey(clave);

    /// <summary>
    /// Texto canónico que se guarda y se imprime, en el orden del catálogo
    /// para que dos recepciones sean comparables.
    /// </summary>
    public static string Describir(IEnumerable<string> claves)
    {
        var marcadas = claves.ToHashSet();
        return string.Join(", ", Catalogo
            .Where(kv => marcadas.Contains(kv.Key))
            .Select(kv => kv.Value));
    }
}

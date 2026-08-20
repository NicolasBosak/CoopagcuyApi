using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Catálogo cerrado de condiciones verificables antes de enviar una jaula a
/// planta. Reemplaza el texto libre: el diagnóstico PRODUCTO1 señala la
/// "identificación física informal" y los "registros no estandarizados" como
/// brechas, y un campo abierto hacía que cada CAT escribiera lo suyo.
///
/// El servidor solo acepta estas claves; el texto que se guarda e imprime en
/// la guía lo pone él, no el operador.
/// </summary>
public static class CondicionTransporte
{
    public static readonly IReadOnlyDictionary<string, string> Catalogo =
        new Dictionary<string, string>
        {
            ["JaulasLimpias"] = "Jaulas limpias y desinfectadas",
            // La CLAVE se queda como "Maximo20" aunque el tope sea 15: las
            // tablets con el bundle antiguo en caché la siguen enviando, y
            // MovilizacionService rechaza con 400 cualquier clave que no
            // reconozca. Lo que se ve y se imprime es la etiqueta.
            ["Maximo20"] = $"Máximo {ReglasRecepcion.CapacidadJaula} animales por jaula",
            ["Ventilacion"] = "Ventilación adecuada",
            ["JaulasAseguradas"] = "Jaulas aseguradas, sin apilar",
            ["ProteccionClima"] = "Protección contra sol y lluvia",
            ["EnAyuno"] = "Animales en ayuno",
            ["VehiculoLimpio"] = "Vehículo limpio",
        };

    public static bool EsValida(string clave) => Catalogo.ContainsKey(clave);

    /// <summary>
    /// Texto canónico que se guarda y se imprime en la guía de movilización.
    /// Respeta el orden del catálogo para que dos guías sean comparables.
    /// </summary>
    public static string Describir(IEnumerable<string> claves)
    {
        var marcadas = claves.ToHashSet();
        var etiquetas = Catalogo
            .Where(kv => marcadas.Contains(kv.Key))
            .Select(kv => kv.Value);
        return string.Join(", ", etiquetas);
    }
}

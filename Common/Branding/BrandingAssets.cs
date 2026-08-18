using System.Reflection;

namespace CoopagcuyApi.Common.Branding;

/// <summary>
/// Imágenes de marca embebidas en el ensamblado, disponibles para los
/// documentos que genera el sistema.
///
/// Se leen una sola vez y se guardan en memoria: los PDF se generan por
/// petición y volver a leer el flujo del ensamblado en cada uno sería trabajo
/// repetido para unos pocos kilobytes que no cambian nunca.
///
/// Si el recurso no está declarado en el .csproj, esto lanza al primer acceso
/// con un mensaje que dice exactamente qué falta — mejor que un PDF sin logo
/// que nadie nota hasta que el documento ya está impreso.
/// </summary>
public static class BrandingAssets
{
    /// <summary>
    /// Familia tipográfica de los documentos.
    ///
    /// Se nombra explícitamente porque la fuente por defecto de QuestPDF (Lato)
    /// no está en las imágenes Linux. Al no encontrarla, QuestPDF no avisa:
    /// resuelve por fontconfig y acaba eligiendo Liberation MONO, así que los
    /// PDF del servidor salían maquetados con letra de máquina de escribir
    /// mientras en un equipo Windows —donde sí hay tipografías— se veían bien.
    ///
    /// Liberation Sans la instalan tanto el Dockerfile de producción como el
    /// del ejecutor de pruebas. Donde no exista, QuestPDF vuelve a su
    /// comportamiento anterior: cambia el aspecto, no rompe nada.
    /// </summary>
    public const string FamiliaTipografica = "Liberation Sans";

    private const string RecursoLogo = "CoopagcuyApi.Common.Branding.coopagcuy-logo.png";

    private static readonly Lazy<byte[]> _logo = new(() => Leer(RecursoLogo));

    /// <summary>
    /// Isotipo de COOPAGCUY en PNG (la insignia verde con el cuy), para la
    /// cabecera de los documentos.
    ///
    /// Es el isotipo y no el logotipo completo a propósito: el lockup lleva
    /// debajo "Cuy Azuayito" en gris casi blanco, invisible sobre papel, y la
    /// cabecera de cada documento ya escribe ese nombre como texto.
    /// </summary>
    public static byte[] Logo => _logo.Value;

    private static byte[] Leer(string nombre)
    {
        var ensamblado = Assembly.GetExecutingAssembly();

        using var flujo = ensamblado.GetManifestResourceStream(nombre)
            ?? throw new InvalidOperationException(
                $"El recurso embebido '{nombre}' no existe. Comprueba que " +
                $"CoopagcuyApi.csproj lo declara como <EmbeddedResource>. " +
                $"Recursos disponibles: " +
                $"{string.Join(", ", ensamblado.GetManifestResourceNames())}");

        using var memoria = new MemoryStream();
        flujo.CopyTo(memoria);
        return memoria.ToArray();
    }
}

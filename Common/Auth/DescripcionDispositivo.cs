namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Traduce un User-Agent al nombre corto que se muestra en la pantalla de
/// sesiones activas ("Chrome · Android"). Función pura, sin dependencias.
///
/// Deliberadamente simple: son cinco coincidencias de texto para las tablets
/// del piloto, no un analizador completo de User-Agent. Lo que no reconozca
/// devuelve un texto neutro; nunca lanza, porque un cliente puede no mandar
/// User-Agent y la pantalla no puede caerse por eso.
///
/// El orden de las comprobaciones importa: Edge se anuncia como Chrome y
/// Chrome como Safari, así que lo más específico va primero.
/// </summary>
public static class DescripcionDispositivo
{
    public const string Desconocido = "Dispositivo desconocido";

    public static string Describir(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return Desconocido;

        var navegador = Navegador(userAgent);
        var sistema = Sistema(userAgent);

        if (navegador is null && sistema is null) return Desconocido;
        if (navegador is null) return sistema!;
        if (sistema is null) return navegador;
        return $"{navegador} · {sistema}";
    }

    private static string? Navegador(string ua) =>
        // Edge antes que Chrome: su User-Agent contiene "Chrome" además de "Edg"
        ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
        : ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
        : ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
        : ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
        // Safari va al final: Chrome y Edge también dicen "Safari"
        : ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
        : null;

    private static string? Sistema(string ua) =>
        // iPad antes que iPhone y que Mac: su cadena menciona "Mac OS X"
        ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
        : ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
        // Android antes que Linux: todo Android es también Linux
        : ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
        : ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
        : ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ? "macOS"
        : ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
        : null;
}

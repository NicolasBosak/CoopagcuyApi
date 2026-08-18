using CoopagcuyApi.Common.Auth;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// La pantalla de sesiones muestra un User-Agent traducido a algo que un
/// administrador pueda leer de un vistazo. No se usa una librería: son cinco
/// coincidencias de texto para las tablets del piloto, y una dependencia más
/// es una dependencia más que auditar.
/// </summary>
public class DescripcionDispositivoTests
{
    [Theory]
    // Tablet Android con Chrome — el caso mayoritario del piloto
    [InlineData(
        "Mozilla/5.0 (Linux; Android 13; SM-X200) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome · Android")]
    // iPad
    [InlineData(
        "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.0 Safari/605.1.15",
        "Safari · iPad")]
    // Escritorio Windows con Edge
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
        "Edge · Windows")]
    // Firefox en Windows
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) " +
        "Gecko/20100101 Firefox/121.0",
        "Firefox · Windows")]
    public void Describir_traduceLosUserAgentDelPiloto(string ua, string esperado)
    {
        DescripcionDispositivo.Describir(ua).ShouldBe(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("algo-que-no-es-un-user-agent")]
    public void Describir_noRevientaConEntradaInutil(string? ua)
    {
        // Un cliente puede no mandar User-Agent, o mandar cualquier cosa. La
        // pantalla de sesiones no puede caerse por eso.
        DescripcionDispositivo.Describir(ua).ShouldBe("Dispositivo desconocido");
    }
}

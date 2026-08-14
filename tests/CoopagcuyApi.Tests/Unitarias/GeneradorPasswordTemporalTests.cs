using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// La contraseña temporal se dicta por teléfono a operadores en campo. Tiene
/// que cumplir la política del sistema Y ser pronunciable; si falla lo
/// primero, el usuario no puede entrar, y si falla lo segundo, no llega a
/// escribirla bien nunca.
/// </summary>
public class GeneradorPasswordTemporalTests
{
    [Fact]
    public void Generar_siempreCumpleLaPoliticaDeContrasenas()
    {
        for (var i = 0; i < 500; i++)
            PoliticaPassword.EsValida(GeneradorPasswordTemporal.Generar())
                .ShouldBeTrue();
    }

    [Fact]
    public void Generar_produceValoresDistintos()
    {
        var generadas = Enumerable.Range(0, 200)
            .Select(_ => GeneradorPasswordTemporal.Generar())
            .ToHashSet();

        // Con 14 palabras y 90 000 números, 200 tiradas repetidas serían un
        // generador roto, no mala suerte
        generadas.Count.ShouldBeGreaterThan(190);
    }

    [Fact]
    public void Generar_usaSoloLetrasMinusculasDigitosYUnGuion()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = GeneradorPasswordTemporal.Generar();

            // Mayúsculas y símbolos no sobreviven a un dictado por teléfono
            password.ShouldAllBe(c => char.IsAsciiLetterLower(c)
                                   || char.IsAsciiDigit(c)
                                   || c == '-');
        }
    }
}

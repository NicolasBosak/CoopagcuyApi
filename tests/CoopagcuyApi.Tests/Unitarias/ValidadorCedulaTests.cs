using CoopagcuyApi.Common.Auth;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

// Primera prueba del repositorio: su valor real es confirmar que la
// tubería (compilación, runner, Docker, CI) funciona de extremo a extremo.
// La batería completa de cédulas llega en la Fase 2.
public class ValidadorCedulaTests
{
    [Theory]
    [InlineData("0102030400")]   // dígito verificador correcto (módulo 10)
    public void Cedula_conDigitoVerificadorValido_esAceptada(string cedula)
    {
        ValidadorCedula.EsValida(cedula).ShouldBeTrue();
    }

    [Theory]
    [InlineData("0102030401")]   // último dígito alterado
    [InlineData("010203040")]    // nueve dígitos
    [InlineData("3002030405")]   // código de provincia 30, inexistente
    [InlineData("")]
    public void Cedula_invalida_esRechazada(string cedula)
    {
        ValidadorCedula.EsValida(cedula).ShouldBeFalse();
    }
}

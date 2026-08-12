using System.Net;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

// No toca la base de datos a propósito: aísla "¿arranca la aplicación dentro
// del harness?" de "¿está bien la base de datos?". Si esta pasa y las demás
// fallan, el problema es de datos, no de configuración.
[Collection(ColeccionApi.Nombre)]
public class SaludTests(ApiFactory api)
{
    [Fact]
    public async Task Health_respondeOk_sinAutenticacion()
    {
        var respuesta = await api.CreateClient().GetAsync("/health");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

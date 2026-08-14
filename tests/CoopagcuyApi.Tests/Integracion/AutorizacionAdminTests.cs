using System.Net;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Los dos roles de administración dejan de ser intercambiables: el técnico
/// conserva todo el sistema, el de cooperativa pierde las sesiones activas
/// pero gana la bandeja de contraseñas. Se comprueba en el API y no solo en
/// las rutas del front: una ruta protegida sin su [Authorize] correspondiente
/// es una falsa sensación de seguridad — con el token se llama igual.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AutorizacionAdminTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdminCooperativa_noPuedeVerLasSesionesActivas()
    {
        var respuesta = await api.ComoAdmin().GetAsync("/api/auth/sesiones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminCooperativa_noPuedeRevocarSesiones()
    {
        var porId = await api.ComoAdmin().DeleteAsync("/api/auth/sesiones/1");
        var porUsuario = await api.ComoAdmin()
            .DeleteAsync("/api/auth/sesiones/usuario/1");

        porId.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        porUsuario.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminTecnico_siPuedeVerLasSesionesActivas()
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync("/api/auth/sesiones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LosDosAdministradores_venLaBandejaDeContrasenas()
    {
        var cooperativa = await api.ComoAdmin().GetAsync("/api/auth/recuperacion");
        var tecnico = await api.ComoAdminTecnico().GetAsync("/api/auth/recuperacion");

        cooperativa.StatusCode.ShouldBe(HttpStatusCode.OK);
        tecnico.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnOperador_noVeLaBandejaDeContrasenas()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/auth/recuperacion");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

using System.Net;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

// Verifica el andamiaje, no reglas de negocio: que las migraciones se apliquen,
// que Respawn limpie entre pruebas y que los tokens de prueba sean aceptados.
// Si esta clase está en verde, la Fase 3 puede escribirse sin sorpresas.
[Collection(ColeccionApi.Nombre)]
public class ArranqueBaseDatosTests(ApiFactory api) : IAsyncLifetime
{
    [Fact]
    public async Task LasMigraciones_seAplicaron_completas()
    {
        await using var db = api.NuevoDbContext();

        var pendientes = await db.Database.GetPendingMigrationsAsync();

        pendientes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Respawn_dejaLaBaseVacia_antesDeCadaPrueba()
    {
        await using var db = api.NuevoDbContext();

        // InitializeAsync ya corrió la limpieza
        (await db.Productoras.CountAsync()).ShouldBe(0);
        (await db.Lotes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task EndpointProtegido_sinToken_responde401()
    {
        var respuesta = await api.ComoAnonimo().GetAsync("/api/productoras");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EndpointProtegido_conTokenDeAdmin_responde200()
    {
        var respuesta = await api.ComoAdmin().GetAsync("/api/productoras");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}

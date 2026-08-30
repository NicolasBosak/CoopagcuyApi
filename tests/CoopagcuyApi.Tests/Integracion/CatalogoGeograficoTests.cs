using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El catálogo geográfico llega sembrado por migración, no se da de alta a
/// mano. Estas pruebas verifican la semilla, no reglas de negocio: si se
/// caen, la migración no dejó la base como el resto del sistema espera.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CatalogoGeograficoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LaSemilla_traeLasVeinticuatroProvincias()
    {
        await using var db = api.NuevoDbContext();

        (await db.Provincias.CountAsync()).ShouldBe(24);
    }

    [Fact]
    public async Task LaSemilla_traeLosDoscientosVeintiunCantones()
    {
        await using var db = api.NuevoDbContext();

        (await db.Cantones.CountAsync()).ShouldBe(221);
    }

    [Fact]
    public async Task Azuay_traeLosCantonesDelPiloto()
    {
        await using var db = api.NuevoDbContext();

        var cantones = await db.Cantones
            .Where(c => c.Provincia.Nombre == "Azuay")
            .Select(c => c.Nombre)
            .ToListAsync();

        cantones.ShouldContain("Nabón");
        cantones.ShouldContain("Pucará");
        cantones.ShouldContain("Santa Isabel");
    }

    // Respawn trunca todo lo que no esté en TablesToIgnore. Si esta prueba
    // se cae, el catálogo se está vaciando entre pruebas y media batería va
    // a fallar por claves foráneas, no por lo que cada prueba verifica.
    [Fact]
    public async Task ElCatalogo_sobreviveALaLimpiezaEntrePruebas()
    {
        await api.LimpiarAsync();

        await using var db = api.NuevoDbContext();

        (await db.Provincias.AnyAsync()).ShouldBeTrue();
        (await db.Cantones.AnyAsync()).ShouldBeTrue();
    }
}

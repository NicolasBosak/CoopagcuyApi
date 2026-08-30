using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Catalogos.Models;
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

    [Fact]
    public async Task LasComunidadesSembradas_apuntanASuCanton()
    {
        await using var db = api.NuevoDbContext();

        var comunidades = await db.Comunidades
            .Include(c => c.Canton)
            .ThenInclude(c => c.Provincia)
            .OrderBy(c => c.Id)
            .ToListAsync();

        comunidades.Select(c => c.Canton.Nombre).ShouldBe(
        [
            "Pucará",        // 1 Patococha
            "Pucará",        // 2 Las Nieves — NO Nabón, ver nota de la semilla
            "Santa Isabel",  // 3 Huertas
            "Nabón",         // 4 Nabón / El Progreso
            "Pucará",        // 5 Pelincay
        ]);

        comunidades.ShouldAllBe(c => c.Canton.Provincia.Nombre == "Azuay");
    }

    // El cruce del backfill ignora tildes y mayúsculas. No es un detalle: en
    // la base real hay una comunidad cuyo cantón se escribió "Nabon" desde
    // Administración, y con comparación cruda se habría quedado sin cantón.
    //
    // La columna "Canton" ya no existe después de migrar, así que la prueba
    // ejecuta el MISMO SQL del backfill sobre un valor de entrada suelto. Es
    // la única forma de ejercitar esa lógica una vez aplicada la migración;
    // si el SQL de aquí y el de la migración divergen, esta prueba deja de
    // proteger nada — mantenerlos idénticos.
    [Theory]
    [InlineData("Nabon", "Nabón")]     // el caso real de la base
    [InlineData("NABÓN", "Nabón")]     // mayúsculas
    [InlineData("  Pucara  ", "Pucará")] // espacios y tilde
    public async Task ElCruceDeCantones_ignoraTildesYMayusculas(
        string escritoAMano, string esperado)
    {
        await using var db = api.NuevoDbContext();

        var id = await db.Database
            .SqlQuery<int>($"""
                SELECT ct."Id" AS "Value"
                FROM "Cantones" ct
                WHERE translate(lower(trim({escritoAMano})),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                    = translate(lower(trim(ct."Nombre")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                  AND ct."ProvinciaId" = 1
                """)
            .SingleAsync();

        var canton = await db.Cantones.FindAsync(id);

        canton!.Nombre.ShouldBe(esperado);
    }

    // Dos provincias distintas pueden tener una comunidad con el mismo
    // nombre. Antes el índice único era global y eso habría bloqueado el alta.
    [Fact]
    public async Task DosComunidadesHomonimas_puedenCoexistirEnCantonesDistintos()
    {
        await using var db = api.NuevoDbContext();

        db.Comunidades.Add(new Comunidad
        {
            Nombre = "San José", CantonId = 1, CatReferencia = CentroAcopio.PAT,
        });
        db.Comunidades.Add(new Comunidad
        {
            Nombre = "San José", CantonId = 2, CatReferencia = CentroAcopio.PAT,
        });

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }
}

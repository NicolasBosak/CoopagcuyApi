using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El centro de acopio dejó de ser un enum compilado y es una tabla. Su clave
/// es el código de tres letras, no un Id: ese código prefija el identificador
/// de cada jaula (PAT-20260615-001) y ya estaba guardado como texto en las
/// cinco columnas que lo referencian.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CatalogoCatTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LaSemilla_traeLosCincoCentrosDelPiloto()
    {
        await using var db = api.NuevoDbContext();

        var codigos = await db.CentrosAcopio
            .OrderBy(c => c.Codigo).Select(c => c.Codigo).ToListAsync();

        codigos.ShouldBe(["HUE", "NAB", "NIE", "PAT", "PEL"]);
    }

    [Fact]
    public async Task CadaCentro_conoceSuCantonYSuProvincia()
    {
        await using var db = api.NuevoDbContext();

        var pat = await db.CentrosAcopio
            .Include(c => c.Canton).ThenInclude(c => c.Provincia)
            .SingleAsync(c => c.Codigo == "PAT");

        pat.Nombre.ShouldBe("Patococha");
        pat.Canton.Nombre.ShouldBe("Pucará");
        pat.Canton.Provincia.Nombre.ShouldBe("Azuay");
    }

    // La clave foránea es lo que impide que una jaula nazca apuntando a un
    // centro que no existe. Antes lo garantizaba el enum; ahora, la base.
    [Fact]
    public async Task UnaJaula_conCentroInexistente_esRechazadaPorLaBase()
    {
        await using var db = api.NuevoDbContext();

        db.Lotes.Add(new CoopagcuyApi.Features.Productoras.Models.Lote
        {
            CodigoLote = "ZZZ-20260101-001",
            CentroAcopio = "ZZZ",
            FechaRecepcion = new DateTime(2026, 1, 1),
            CantidadAnimales = 0,
            PesoTotalGramos = 0,
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ElCatalogoDeCat_sobreviveALaLimpiezaEntrePruebas()
    {
        await api.LimpiarAsync();

        await using var db = api.NuevoDbContext();

        (await db.CentrosAcopio.CountAsync()).ShouldBe(5);
    }
}

using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Alta y baja de provincias y cantones. La regla que gobierna todo: nada se
/// borra, se desactiva — y no se desactiva lo que todavía sostiene a otros.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ApiGeografiaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record ProvinciaLeida(int Id, string Nombre, bool Activa, int TotalCantones);
    private sealed record CantonLeido(
        int Id, string Nombre, int ProvinciaId, string Provincia,
        bool Activo, int TotalComunidades);

    [Fact]
    public async Task CualquierAutenticado_listaLasProvincias()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetFromJsonAsync<List<ProvinciaLeida>>("/api/catalogos/provincias");

        respuesta!.ShouldContain(p => p.Nombre == "Azuay");
    }

    // "Provincias" está en TablesToIgnore de Respawn (es catálogo sembrado
    // por migración, no se trunca entre pruebas, ver BaseDatosFixture), así
    // que la fila creada aquí sobreviviría a la prueba y rompería
    // LaSemilla_traeLasVeinticuatroProvincias (en CatalogoGeograficoTests,
    // que espera 24) en la siguiente corrida de la batería. El nombre de
    // prueba y el borrado en el finally evitan dejar rastro.
    [Fact]
    public async Task Admin_creaUnaProvincia()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/provincias", new { nombre = "Provincia De Prueba" });

        try
        {
            respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            await using var db = api.NuevoDbContext();
            var creada = await db.Provincias
                .SingleOrDefaultAsync(p => p.Nombre == "Provincia De Prueba");
            if (creada is not null)
            {
                db.Provincias.Remove(creada);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task OperadorCat_noPuedeCrearProvincias()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/catalogos/provincias", new { nombre = "Prohibida" });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Provincia_conNombreRepetido_esRechazada()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/provincias", new { nombre = "Azuay" });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Provincia_conCantonesActivos_noSeDesactiva()
    {
        // Azuay es la provincia 1 y trae 15 cantones sembrados. El servicio
        // lanza antes de tocar "Activa", así que esta prueba no muta nada
        // que limpiar.
        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/provincias/1/estado", new { activa = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Canton_conComunidadesActivas_noSeDesactiva()
    {
        // Cantón 6 = Pucará, sostiene a Patococha y Pelincay. El servicio
        // lanza antes de tocar "Activo", así que esta prueba no muta nada
        // que limpiar.
        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/cantones/6/estado", new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // "Cantones" tampoco se trunca entre pruebas (mismo TablesToIgnore).
    // Dejar Cuenca desactivada rompería LosCantones_seFiltranPorProvincia
    // (espera 15 cantones activos en Azuay) en la siguiente prueba o en la
    // siguiente corrida de la batería. El finally la reactiva.
    [Fact]
    public async Task Canton_sinComunidades_seDesactiva()
    {
        // Cantón 1 = Cuenca, sembrado pero sin comunidades del piloto
        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/cantones/1/estado", new { activo = false });

        try
        {
            respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            await using var db = api.NuevoDbContext();
            var cuenca = await db.Cantones.SingleAsync(c => c.Id == 1);
            cuenca.Activo = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task LosCantones_seFiltranPorProvincia()
    {
        var cantones = await api.ComoAdmin()
            .GetFromJsonAsync<List<CantonLeido>>("/api/catalogos/cantones?provinciaId=1");

        cantones!.Count.ShouldBe(15);
        cantones.ShouldAllBe(c => c.Provincia == "Azuay");
    }

    [Fact]
    public async Task DosCantonesHomonimos_enProvinciasDistintas_seAceptan()
    {
        // "Bolívar" ya existe en Carchi (4) y en Manabí (14) desde la semilla
        var cantones = await api.ComoAdmin()
            .GetFromJsonAsync<List<CantonLeido>>("/api/catalogos/cantones");

        cantones!.Count(c => c.Nombre == "Bolívar").ShouldBe(2);
    }

    [Fact]
    public async Task Canton_repetidoDentroDeSuProvincia_esRechazado()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/cantones",
                new { nombre = "Nabón", provinciaId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}

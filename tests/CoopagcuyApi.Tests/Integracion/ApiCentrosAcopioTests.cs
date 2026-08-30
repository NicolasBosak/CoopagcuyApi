using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Alta y baja de centros de acopio. El código de tres letras es la clave del
/// sistema —prefija cada jaula— así que se valida al entrar y no se toca nunca
/// más.
///
/// CentrosAcopio y Comunidades son catálogo sembrado por migración y NO se
/// truncan entre pruebas (ver TablesToIgnore en BaseDatosFixture): cada
/// prueba que crea o modifica una fila ahí la deshace en un `finally`, para
/// que sobreviva a una aserción fallida y no deje rastro para la prueba
/// siguiente ni para las clases que cuentan filas de catálogo
/// (CatalogoCatTests, CatalogoGeograficoTests).
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ApiCentrosAcopioTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record CatLeido(
        string Codigo, string Nombre, int CantonId, string Canton,
        string Provincia, bool Activo);

    // Borra el centro y, antes, cualquier Lote que haya quedado apuntándole
    // dentro de LA MISMA prueba (Lotes se trunca recién en el InitializeAsync
    // de la prueba SIGUIENTE, así que al llegar aquí la FK todavía existe).
    private static async Task LimpiarCentroAsync(ApiFactory api, params string[] codigos)
    {
        await using var db = api.NuevoDbContext();
        db.ChangeTracker.Clear();
        foreach (var codigo in codigos)
        {
            var lotes = await db.Lotes.Where(l => l.CentroAcopio == codigo).ToListAsync();
            db.Lotes.RemoveRange(lotes);
            var centro = await db.CentrosAcopio.FindAsync(codigo);
            if (centro is not null) db.CentrosAcopio.Remove(centro);
        }
        await db.SaveChangesAsync();
    }

    private static async Task LimpiarComunidadAsync(ApiFactory api, string nombre)
    {
        await using var db = api.NuevoDbContext();
        db.ChangeTracker.Clear();
        var comunidad = await db.Comunidades.FirstOrDefaultAsync(c => c.Nombre == nombre);
        if (comunidad is not null) db.Comunidades.Remove(comunidad);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Admin_creaUnCentroDeAcopio()
    {
        try
        {
            var respuesta = await api.ComoAdmin()
                .PostAsJsonAsync("/api/catalogos/centros-acopio",
                    new { codigo = "CUE", nombre = "Cuenca Centro", cantonId = 1 });

            respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            await LimpiarCentroAsync(api, "CUE");
        }
    }

    [Fact]
    public async Task ElCodigo_seNormalizaAMayusculas()
    {
        try
        {
            await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "gir", nombre = "Girón", cantonId = 2 });

            var lista = await api.ComoAdmin()
                .GetFromJsonAsync<List<CatLeido>>("/api/catalogos/centros-acopio");

            lista!.ShouldContain(c => c.Codigo == "GIR");
        }
        finally
        {
            await LimpiarCentroAsync(api, "GIR");
        }
    }

    [Theory]
    [InlineData("PA")]      // dos letras
    [InlineData("PATO")]    // cuatro
    [InlineData("P4T")]     // un dígito
    [InlineData("")]        // vacío
    public async Task UnCodigoQueNoEsTresLetras_esRechazado(string codigo)
    {
        // Ninguna de estas formas llega a persistir fila alguna (se rechazan
        // en el servicio antes del SaveChanges): no hace falta limpiar nada.
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo, nombre = "Da igual", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnCodigoRepetido_esRechazado()
    {
        // PAT ya existe desde la semilla: el intento se rechaza sin crear ni
        // modificar ninguna fila, así que tampoco hay nada que limpiar.
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "PAT", nombre = "Otro Patococha", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // El contrato de actualización NO tiene campo Codigo. Mandarlo no hace
    // nada: el centro sigue llamándose igual que siempre.
    //
    // Este PUT sí llega a persistir (Nombre/CantonId de PAT, la fila
    // sembrada): se captura el valor original antes y se restaura en el
    // finally, porque CentrosAcopio no se trunca entre pruebas y otras
    // clases (CatalogoCatTests) verifican el nombre original de PAT.
    [Fact]
    public async Task ElCodigo_noSePuedeCambiar()
    {
        string nombreOriginal;
        int cantonIdOriginal;
        await using (var lectura = api.NuevoDbContext())
        {
            var pat = await lectura.CentrosAcopio.SingleAsync(c => c.Codigo == "PAT");
            nombreOriginal = pat.Nombre;
            cantonIdOriginal = pat.CantonId;
        }

        try
        {
            await api.ComoAdmin().PutAsJsonAsync("/api/catalogos/centros-acopio/PAT",
                new { codigo = "XXX", nombre = "Patococha renombrada", cantonId = 6 });

            var lista = await api.ComoAdmin()
                .GetFromJsonAsync<List<CatLeido>>("/api/catalogos/centros-acopio");

            lista!.ShouldContain(c => c.Codigo == "PAT");
            lista!.ShouldNotContain(c => c.Codigo == "XXX");
        }
        finally
        {
            await using var db = api.NuevoDbContext();
            db.ChangeTracker.Clear();
            var pat = await db.CentrosAcopio.SingleAsync(c => c.Codigo == "PAT");
            pat.Nombre = nombreOriginal;
            pat.CantonId = cantonIdOriginal;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task UnCentro_conJaulaAbierta_noSeDesactiva()
    {
        // Lotes SÍ se trunca entre pruebas (no está en TablesToIgnore): la
        // jaula sembrada aquí no deja rastro por sí sola. La desactivación
        // se rechaza (409), así que PAT.Activo tampoco cambia.
        await Sembrador.LoteAbiertoAsync(api, "PAT");

        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/centros-acopio/PAT/estado",
                new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnCentro_conProductorasActivas_noSeDesactiva()
    {
        // Productoras tampoco se trunca... salvo que SÍ se trunca (no está en
        // TablesToIgnore): la productora sembrada aquí desaparece sola en la
        // siguiente limpieza. La desactivación se rechaza (409), así que
        // NIE.Activo tampoco cambia.
        await Sembrador.ProductoraAsync(api, "0104576277", cat: "NIE", comunidadId: 2);

        var respuesta = await api.ComoAdmin()
            .PatchAsJsonAsync("/api/catalogos/centros-acopio/NIE/estado",
                new { activo = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnCentroReciénCreado_seDesactivaSinProblema()
    {
        try
        {
            await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "SIG", nombre = "Sígsig", cantonId = 9 });

            var respuesta = await api.ComoAdmin()
                .PatchAsJsonAsync("/api/catalogos/centros-acopio/SIG/estado",
                    new { activo = false });

            respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            await LimpiarCentroAsync(api, "SIG");
        }
    }

    [Fact]
    public async Task OperadorCat_noPuedeCrearCentros()
    {
        // 403 antes de llegar al servicio: no hay fila que limpiar.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "ABC", nombre = "Prohibido", cantonId = 1 });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Una comunidad puede entregar en el CAT que le quede más cerca, aunque
    // esté en otra provincia. No hay validación geográfica y no debe haberla.
    [Fact]
    public async Task UnaComunidad_puedeReferenciarUnCatDeOtraProvincia()
    {
        try
        {
            // Cantón 108 = Loja (Loja). Es el primero de la provincia 12: las
            // once anteriores suman 107 cantones en GeografiaEcuador.
            await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "LOJ", nombre = "Loja Centro", cantonId = 108 });

            var respuesta = await api.ComoAdmin()
                .PostAsJsonAsync("/api/catalogos/comunidades",
                    new { nombre = "Comunidad Fronteriza", cantonId = 4, catReferencia = "LOJ" });

            respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            // La comunidad primero: tiene FK a CentrosAcopio.Codigo y
            // borrar LOJ mientras algo la referencia rompería por clave
            // foránea.
            await LimpiarComunidadAsync(api, "Comunidad Fronteriza");
            await LimpiarCentroAsync(api, "LOJ");
        }
    }

    // Un centro creado en caliente acota igual que los cinco del piloto: el
    // alcance sale del claim "cat" del token, y ese claim ya era un string
    // antes de que el enum desapareciera. Si esto se cayera, un operador de
    // un centro nuevo vería las jaulas de todos los demás.
    [Fact]
    public async Task UnOperador_deUnCentroNuevo_soloVeLoSuyo()
    {
        try
        {
            await api.ComoAdmin().PostAsJsonAsync("/api/catalogos/centros-acopio",
                new { codigo = "GUA", nombre = "Gualaceo", cantonId = 3 });

            await Sembrador.LoteAbiertoAsync(api, "PAT");
            await Sembrador.LoteAbiertoAsync(api, "GUA");

            var jaulas = await api.ComoOperadorCat("GUA")
                .GetFromJsonAsync<List<JaulaLeida>>("/api/recepcion/lotes");

            jaulas!.ShouldAllBe(j => j.CentroAcopio == "GUA");
            jaulas!.Count.ShouldBe(1);
        }
        finally
        {
            await LimpiarCentroAsync(api, "GUA");
        }
    }

    private sealed record JaulaLeida(int Id, string CodigoLote, string CentroAcopio);
}

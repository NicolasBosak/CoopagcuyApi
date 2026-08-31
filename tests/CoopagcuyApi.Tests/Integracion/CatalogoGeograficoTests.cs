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

        // Acotado a las 5 sembradas por HasData: "Comunidades" está en
        // TablesToIgnore de Respawn (no se trunca entre pruebas, ver
        // BaseDatosFixture) y otra prueba de esta misma clase inserta filas
        // adicionales. Sin este filtro, esta aserción quedaría dependiente
        // del orden de ejecución.
        var comunidades = await db.Comunidades
            .Include(c => c.Canton)
            .ThenInclude(c => c.Provincia)
            .Where(c => c.Id <= 5)
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

    // La decisión de datos más deliberada de la tarea, y la más fácil de
    // "arreglar" por error en el futuro: Pelincay se sembró sin coordenadas
    // (los 4 campos en null) porque no se tenían, no por olvido. Patococha
    // sí las tiene y son las del front.
    [Fact]
    public async Task LasComunidadesSembradas_traenLasCoordenadasCorrectas()
    {
        await using var db = api.NuevoDbContext();

        var patococha = await db.Comunidades.SingleAsync(c => c.Id == 1);
        patococha.Nombre.ShouldBe("Patococha");
        patococha.Latitud.ShouldBe(-3.225944m);
        patococha.Longitud.ShouldBe(-79.504472m);

        var pelincay = await db.Comunidades.SingleAsync(c => c.Id == 5);
        pelincay.Nombre.ShouldBe("Pelincay");
        pelincay.Latitud.ShouldBeNull();
        pelincay.Longitud.ShouldBeNull();
        pelincay.AltitudMinM.ShouldBeNull();
        pelincay.AltitudMaxM.ShouldBeNull();
    }

    // La columna "Canton" ya no existe después de migrar, así que esta
    // prueba NO puede correr el backfill contra "Comunidades" — lo monta
    // sobre una tabla temporal con la misma forma que tenía esa tabla antes
    // de migrar ("Canton" texto + "CantonId") y ejecuta LITERALMENTE el
    // UPDATE ... FROM del backfill de la migración
    // (20260830020720_ComunidadCuelgaDeCanton.cs: mismo translate(), mismo
    // WHERE "CantonId" = 0, SIN el predicado de provincia que tenía la
    // versión anterior de esta prueba — ese predicado ocultaba el problema
    // de nombres de cantón repetidos entre provincias). Si el SQL de aquí y
    // el de la migración divergen, esta prueba deja de proteger nada:
    // mantenerlos idénticos.
    //
    // La conexión se abre explícitamente porque una tabla TEMP solo vive
    // mientras la conexión de sesión sigue abierta; si EF la cerrara entre
    // sentencias (su comportamiento por defecto), la tabla desaparecería
    // antes del UPDATE.
    [Fact]
    public async Task ElBackfill_cruzaIgnorandoTildesYMayusculasYRespetaLoYaAsignado()
    {
        await using var db = api.NuevoDbContext();
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TEMP TABLE "ComunidadesBackfillTmp" (
                    "Id" integer PRIMARY KEY,
                    "Canton" varchar(100) NOT NULL,
                    "CantonId" integer NOT NULL
                );
                """);

            // Fila 4 simula una comunidad cuyo CantonId ya fue asignado
            // (999, valor centinela sin cantón real): la guarda
            // "CantonId = 0" del backfill no debe tocarla. El texto tiene
            // que cruzar contra el catálogo (aquí "Nabón", que sí existe)
            // para que la prueba muerda de verdad: si el texto no cruzara
            // (como pasaba antes con "Las Nieves", que no es ningún cantón
            // de Bolívar — "Las Naves" sí lo es, pero es otro nombre), la
            // fila quedaría intacta por el JOIN y no por el WHERE, y quitar
            // el "CantonId = 0" del UPDATE no haría fallar nada aquí.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "ComunidadesBackfillTmp" ("Id", "Canton", "CantonId") VALUES
                    (1, 'Nabon', 0),
                    (2, 'NABÓN', 0),
                    (3, '  Pucara  ', 0),
                    (4, 'Nabón', 999);
                """);

            // Copia literal del UPDATE ... FROM del backfill, aplicado sobre
            // la tabla temporal en vez de "Comunidades".
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "ComunidadesBackfillTmp" c
                SET "CantonId" = ct."Id"
                FROM "Cantones" ct
                WHERE c."CantonId" = 0
                  AND translate(lower(trim(c."Canton")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                    = translate(lower(trim(ct."Nombre")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN');
                """);

            async Task<int> CantonIdDe(int id) =>
                await db.Database
                    .SqlQuery<int>($"""
                        SELECT "CantonId" AS "Value"
                        FROM "ComunidadesBackfillTmp" WHERE "Id" = {id}
                        """)
                    .SingleAsync();

            (await CantonIdDe(1)).ShouldBe(4); // Nabón — "Nabon" sin tilde, el caso real de la base
            (await CantonIdDe(2)).ShouldBe(4); // Nabón — mayúsculas
            (await CantonIdDe(3)).ShouldBe(6); // Pucará — espacios y sin tilde
            (await CantonIdDe(4)).ShouldBe(999); // ya asignado: el backfill no la toca aunque "Nabón" sí cruce
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // Un nombre de cantón puede existir en más de una provincia: "Bolívar"
    // en Carchi (id 31) y en Manabí (id 138). Es la razón de ser de la
    // guarda "DO $$ ... HAVING count(*) > 1" que la migración corre antes
    // del backfill (20260830020720_ComunidadCuelgaDeCanton.cs): con texto
    // libre no hay forma de saber cuál de los dos era, y esta prueba
    // demuestra que el cruce por nombre normalizado sí encuentra ambos.
    [Fact]
    public async Task ElCruceDeCantones_esAmbiguoParaBolivarEntreProvincias()
    {
        await using var db = api.NuevoDbContext();

        var candidatos = await db.Database
            .SqlQuery<int>($"""
                SELECT count(*) AS "Value"
                FROM "Cantones" ct
                WHERE translate(lower(trim(ct."Nombre")),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                    = translate(lower(trim('Bolívar')),
                                'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                """)
            .SingleAsync();

        candidatos.ShouldBeGreaterThan(1);
    }

    // La prueba anterior solo demuestra la premisa (el catálogo tiene dos
    // "Bolívar"): no dispara el bloque "DO $$ ... HAVING count(*) > 1" que
    // la migración corre antes del backfill para frenarse ante esa
    // ambigüedad. Una guarda que nunca se ejercita en su propio camino de
    // disparo es peor que ninguna, porque parece protección: una edición
    // futura podría dejarla muda sin que nada lo note. Esta prueba sí la
    // ejecuta.
    //
    // Copia literal del primer DO $$ de la migración
    // (20260830020720_ComunidadCuelgaDeCanton.cs), adaptado solo en el
    // nombre de la tabla ("Comunidades" -> la tabla temporal de abajo, que
    // por eso necesita también la columna "Nombre" que esa migración usa
    // para el mensaje). Si el SQL de aquí y el de la migración divergen,
    // esta prueba deja de proteger nada: mantenerlos idénticos.
    //
    // Misma razón que la prueba de arriba para abrir la conexión a mano y
    // cerrarla en el finally: una tabla TEMP solo vive mientras la conexión
    // de sesión siga abierta.
    [Fact]
    public async Task ElBackfill_paraSiElCantonEsAmbiguoEntreProvincias()
    {
        await using var db = api.NuevoDbContext();
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TEMP TABLE "ComunidadesAmbiguedadTmp" (
                    "Id" integer PRIMARY KEY,
                    "Nombre" varchar(100) NOT NULL,
                    "Canton" varchar(100) NOT NULL,
                    "CantonId" integer NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "ComunidadesAmbiguedadTmp" ("Id", "Nombre", "Canton", "CantonId") VALUES
                    (1, 'Comunidad Ambigua', 'Bolívar', 0);
                """);

            var excepcion = await Should.ThrowAsync<Npgsql.PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("""
                    DO $$
                    DECLARE ambiguas text;
                    BEGIN
                        SELECT string_agg(x.detalle, ', ')
                        INTO ambiguas
                        FROM (
                            SELECT format('%s (cantón "%s", en %s provincias)',
                                          c."Nombre", c."Canton", count(*)) AS detalle
                            FROM "ComunidadesAmbiguedadTmp" c
                            JOIN "Cantones" ct
                              ON translate(lower(trim(c."Canton")),
                                           'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                               = translate(lower(trim(ct."Nombre")),
                                           'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')
                            WHERE c."CantonId" = 0
                            GROUP BY c."Id", c."Nombre", c."Canton"
                            HAVING count(*) > 1
                        ) x;

                        IF ambiguas IS NOT NULL THEN
                            RAISE EXCEPTION
                                'Hay comunidades cuyo cantón existe en más de una provincia: %. '
                                'Asígnalas a mano antes de migrar (ver scripts/verificar-cantones.sql).',
                                ambiguas;
                        END IF;
                    END $$;
                    """));

            // La guarda existe para ser accionable, no solo para abortar: el
            // mensaje tiene que nombrar la comunidad problemática.
            excepcion.Message.ShouldContain("Comunidad Ambigua");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // Dos provincias distintas pueden tener una comunidad con el mismo
    // nombre. Antes el índice único era global y eso habría bloqueado el alta.
    [Fact]
    public async Task DosComunidadesHomonimas_puedenCoexistirEnCantonesDistintos()
    {
        await using var db = api.NuevoDbContext();

        db.Comunidades.Add(new Comunidad
        {
            Nombre = "San José", CantonId = 1, CatReferencia = "PAT",
        });
        db.Comunidades.Add(new Comunidad
        {
            Nombre = "San José", CantonId = 2, CatReferencia = "PAT",
        });

        try
        {
            await Should.NotThrowAsync(() => db.SaveChangesAsync());
        }
        finally
        {
            // Si el SaveChangesAsync del try falló, las dos entidades siguen
            // en el rastreador en estado Added: sin este Clear(), el
            // SaveChangesAsync de abajo reintentaría el mismo insert que ya
            // falló, y su excepción sustituiría a la del try, dejando un
            // mensaje que despista sobre la causa real del fallo.
            db.ChangeTracker.Clear();

            // "Comunidades" está en TablesToIgnore de Respawn (es catálogo
            // sembrado, no dato de prueba) y por eso NO se trunca entre
            // pruebas. Sin esta limpieza, estas dos filas sobrevivirían a
            // esta prueba y romperían la siguiente corrida de la batería
            // contra la misma base (por ejemplo, cualquier aserción que
            // cuente filas de "Comunidades").
            db.Comunidades.RemoveRange(
                db.Comunidades.Where(c => c.Nombre == "San José"));
            await db.SaveChangesAsync();
        }
    }
}

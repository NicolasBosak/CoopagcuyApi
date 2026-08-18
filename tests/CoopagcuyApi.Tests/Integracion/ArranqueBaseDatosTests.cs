using System.Net;
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Catalogos.Models;
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
        // Usuario no tiene dependencias hacia otras tablas (ver
        // Common/Auth/Usuario.cs: sin claves foráneas), así que se puede
        // insertar directamente sin sembrar el resto del grafo.
        //
        // Antes esta prueba usaba Comunidad por la misma razón, pero esa
        // tabla dejó de servir: ahora se preserva a propósito (ver la prueba
        // de abajo), así que ya no demuestra que Respawn trunque nada.
        await using (var db = api.NuevoDbContext())
        {
            db.Usuarios.Add(new Usuario
            {
                NombreCompleto = "Usuario de prueba",
                Cedula = "0101112225",
                PasswordHash = "no-importa",
                Rol = RolUsuario.AdminCooperativa
            });
            await db.SaveChangesAsync();
        }

        await using (var dbConFila = api.NuevoDbContext())
            (await dbConFila.Usuarios.CountAsync()).ShouldBe(1);

        await api.LimpiarAsync();

        await using var dbLimpia = api.NuevoDbContext();
        (await dbLimpia.Usuarios.CountAsync()).ShouldBe(0);

        // El historial de migraciones debe sobrevivir al truncado de
        // Respawn (TablesToIgnore en BaseDatosFixture); si Respawn lo
        // arrasara, GetAppliedMigrationsAsync volvería vacío y la próxima
        // limpieza fallaría en silencio, no con un error claro.
        (await dbLimpia.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ElCatalogoDeComunidades_sobreviveALaLimpieza()
    {
        // Comunidades lo siembra la migración con HasData: es catálogo, no
        // datos de prueba. Cuando Respawn lo truncaba, la tabla quedaba vacía
        // desde la primera limpieza y cualquier alta de productora fallaba
        // con 400 ("la comunidad no existe o está inactiva") por un motivo
        // que no tenía nada que ver con lo que la prueba verificaba.
        await api.LimpiarAsync();

        await using var db = api.NuevoDbContext();
        var comunidades = await db.Comunidades.AsNoTracking().ToListAsync();

        // Se comprueba que las cinco sembradas SIGAN ahí, no que sean las
        // únicas: el catálogo es editable desde la administración, así que una
        // prueba que dé de alta una comunidad no debe hacer fallar a esta.
        // El contrapunto es que una comunidad creada en una prueba sobrevive
        // al resto de la corrida — quien la cree, que la borre.
        foreach (var (nombre, cat) in new[]
        {
            ("Patococha", CentroAcopio.PAT),
            ("Las Nieves", CentroAcopio.NIE),
            ("Huertas", CentroAcopio.HUE),
            ("Nabón / El Progreso", CentroAcopio.NAB),
            ("Pelincay", CentroAcopio.PEL),
        })
        {
            comunidades.ShouldContain(c =>
                c.Nombre == nombre && c.CatReferencia == cat && c.Activa);
        }
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

    [Fact]
    public async Task ComoOperadorCat_conCatEnMayusculas_esAceptadoPorElEndpointDeSuCat()
    {
        // Contrato de ComoOperadorCat: el argumento es el nombre del enum
        // CentroAcopio en MAYÚSCULAS ("PAT", "NIE", "HUE", "NAB", "PEL" —
        // Common/Enums.cs). El formato importa porque los dos controladores
        // que leen el claim "cat" reaccionan distinto a un valor mal
        // formado (p. ej. en minúsculas):
        //   - RecepcionController.CatNoAutorizado compara texto exacto
        //     (catOperador != cat.ToString()), así que un CAT en minúsculas
        //     SÍ falla, pero con 403 y el mensaje "Tu usuario solo puede
        //     registrar en el centro pat" en vez de un 401/400 que delate
        //     que el token está mal formado.
        //   - ReportesController.Dashboard usa Enum.TryParse<CentroAcopio>,
        //     que también es sensible a mayúsculas: con un CAT en
        //     minúsculas el parseo falla, catOperador queda en null y el
        //     operador ve el dashboard de TODA la cooperativa en vez del de
        //     su propio centro — una degradación silenciosa, sin error
        //     alguno. Aquí se fija el formato correcto para no ejercitar
        //     ninguna de las dos ramas.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsync("/api/recepcion/lotes/PAT-99999999-999/cerrar", null);

        // La base está vacía: el lote no existe. Un 404 (en vez de 401/403)
        // demuestra que el rol y el CAT del token pasaron la autorización.
        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ComoOperadorFaenamiento_esAceptadoPorElEndpointDeFaenamiento()
    {
        // FaenamientoController restringe la clase entera a
        // OperadorFaenamiento/AdminCooperativa; un OperadorCAT no debe poder
        // leer aquí ni siquiera un listado vacío.
        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/lotes-disponibles");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}

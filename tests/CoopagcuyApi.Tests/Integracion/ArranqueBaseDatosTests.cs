using System.Net;
using CoopagcuyApi.Common;
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
        // Comunidad no tiene dependencias hacia otras tablas (ver
        // Features/Catalogos/Models/Comunidad.cs: Id, Nombre, Canton,
        // CatReferencia y Activa, sin claves foráneas), así que se puede
        // insertar directamente sin sembrar el resto del grafo.
        await using (var db = api.NuevoDbContext())
        {
            db.Comunidades.Add(new Comunidad
            {
                Nombre = "Comunidad de prueba",
                Canton = "Santa Isabel",
                CatReferencia = CentroAcopio.PAT
            });
            await db.SaveChangesAsync();
        }

        await using (var dbConFila = api.NuevoDbContext())
            (await dbConFila.Comunidades.CountAsync()).ShouldBe(1);

        await api.LimpiarAsync();

        await using var dbLimpia = api.NuevoDbContext();
        (await dbLimpia.Comunidades.CountAsync()).ShouldBe(0);

        // El historial de migraciones debe sobrevivir al truncado de
        // Respawn (TablesToIgnore en BaseDatosFixture); si Respawn lo
        // arrasara, GetAppliedMigrationsAsync volvería vacío y la próxima
        // limpieza fallaría en silencio, no con un error claro.
        (await dbLimpia.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
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
        // OperadorFaenamiento/AdminCooperativa/AdminTecnico; un OperadorCAT
        // no debe poder leer aquí ni siquiera un listado vacío.
        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/lotes-disponibles");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}

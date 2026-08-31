using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El operador de CAT gestiona las productoras de su propio centro. El alcance
/// se comprueba contra el claim "cat" del token y nunca contra lo que mande el
/// cuerpo de la petición: si el cliente pudiera elegir su propio alcance, no
/// habría alcance.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AlcanceProductorasTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Cédulas ecuatorianas válidas: provincia entre 01 y 24, tercer dígito
    // menor que 6 y dígito verificador correcto. ProductoraService las
    // revalida al crear, así que un número inventado haría fallar la prueba
    // por un motivo que no tiene nada que ver con lo que verifica.
    private const string CedulaUno = "0104576277";
    private const string CedulaDos = "0111223343";

    private sealed record RespuestaProductora(
        int Id, string NombreCompleto, string Cedula, int ComunidadId,
        string Comunidad, string Canton, string CatAsignado, string? Telefono,
        bool Activa, DateTime FechaRegistro, int TotalRetornos);

    // ── Alta ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OperadorCat_creaProductoraEnSuCentro()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 1,          // Patococha, CatReferencia = PAT
                catAsignado = "PAT",
                telefono = "0999999999"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OperadorCat_creaProductoraDeCualquierComunidad()
    {
        // Cambio de criterio de 2026-08: la comunidad es donde vive la
        // productora y el CAT es donde entrega. Antes esto respondía 403 para
        // no "ensuciar el catálogo de otro centro"; resultó que la realidad
        // del piloto es justo esa — hay productoras que viven en una comunidad
        // y entregan en el CAT de al lado.
        //
        // Lo que NO cambia: el centro lo sigue sellando el token.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, CatReferencia = NIE
                catAsignado = "PAT",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var creada = await respuesta.Content
            .ReadFromJsonAsync<RespuestaProductora>();
        creada.ShouldNotBeNull();
        creada.Comunidad.ShouldBe("Las Nieves");
        creada.CatAsignado.ShouldBe("PAT");
    }

    [Fact]
    public async Task UnaComunidadInexistenteSeSigueRechazando()
    {
        // La guarda retirada también cubría este caso de rebote: devolvía 403
        // cuando la comunidad no existía. Al quitarla hay que asegurarse de
        // que sigue habiendo un rechazo limpio y no un 500 de la clave
        // foránea.
        //
        // Quien lo rechaza ahora es CrearProductoraValidator, con una regla
        // MustAsync que comprueba que la comunidad exista y esté activa. Por
        // eso es 400 y no 404: es un error del cuerpo de la petición, que es
        // justo el criterio 400/409 que sigue este proyecto.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaDos,
                comunidadId = 99999,
                catAsignado = "PAT",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // El código por sí solo no distingue esta guardia de cualquier otro
        // fallo de validación del cuerpo (nombre vacío, cédula inválida…),
        // que también responden 400. El mensaje exacto —copiado literal de
        // CrearProductoraValidator— es lo que fija que el rechazo vino de la
        // regla MustAsync de la comunidad.
        var cuerpo = await respuesta.Content
            .ReadFromJsonAsync<Dictionary<string, string>>();
        cuerpo.ShouldNotBeNull();
        cuerpo["mensaje"].ShouldBe(
            "La comunidad seleccionada no existe o está inactiva.");
    }

    [Fact]
    public async Task OperadorCat_noEligeElCentroDeLaProductoraQueCrea()
    {
        // Manda "NIE" en el cuerpo teniendo "PAT" en el token: el servidor
        // debe ignorar el cuerpo y sellar la productora con el CAT del token.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 1,
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var creada = await respuesta.Content
            .ReadFromJsonAsync<RespuestaProductora>();
        creada.ShouldNotBeNull();
        creada.CatAsignado.ShouldBe("PAT");
    }

    [Fact]
    public async Task AdminCooperativa_sigueCreandoEnCualquierCentro()
    {
        // Control: el admin no queda atrapado por la regla del operador.
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaDos,
                comunidadId = 2,
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ── Edición, baja y alta ──────────────────────────────────────────

    [Fact]
    public async Task OperadorCat_editaProductoraDeSuCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, "PAT", comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Nombre corregido",
                cedula = CedulaUno,
                comunidadId = 1,
                catAsignado = "PAT",
                telefono = "0988888888"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OperadorCat_noEditaProductoraDeOtroCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, "NIE", comunidadId: 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Intento de edición",
                cedula = CedulaUno,
                comunidadId = 2,
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_noMueveUnaProductoraAOtroCentro()
    {
        // La propiedad sigue siendo verdad; lo que caducó es cómo se
        // comprobaba. Antes esta prueba esperaba un 403, pero ese 403 lo daba
        // la guarda de comunidad, no el alcance por centro: era incidental.
        //
        // Con el criterio de 2026-08 la edición se acepta —una productora de
        // PAT puede vivir en una comunidad de Las Nieves— y lo que impide el
        // traslado es el sellado del CAT con el token. Así que se afirma el
        // resultado, no el código de estado: entra siendo de PAT y sigue
        // siendo de PAT, por mucho que el cuerpo pida "NIE".
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, "PAT", comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Intento de traslado",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, de otro cantón
                catAsignado = "NIE",      // …y el cuerpo pide otro centro
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db = api.NuevoDbContext();
        var actualizada = await db.Productoras.AsNoTracking()
            .FirstAsync(p => p.Id == productora.Id);

        actualizada.CatAsignado.ShouldBe("PAT");   // no se movió
        actualizada.ComunidadId.ShouldBe(2);                  // sí cambió de comunidad
    }

    [Fact]
    public async Task OperadorCat_desactivaYReactivaProductoraDeSuCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, "PAT", comunidadId: 1);
        var cliente = api.ComoOperadorCat("PAT");

        var baja = await cliente.PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = false });
        var alta = await cliente.PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = true });

        baja.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        alta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OperadorCat_noCambiaElEstadoDeOtroCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, "NIE", comunidadId: 2);

        var respuesta = await api.ComoOperadorCat("PAT").PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_veLasInactivasDeSuCentroYNingunaDeOtro()
    {
        // Sin las inactivas a la vista no hay forma de reactivar ninguna.
        await Sembrador.ProductoraAsync(
            api, CedulaUno, "PAT", comunidadId: 1, activa: false);
        await Sembrador.ProductoraAsync(
            api, CedulaDos, "NIE", comunidadId: 2, activa: false);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/productoras?incluirInactivas=true");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var lista = await respuesta.Content
            .ReadFromJsonAsync<List<RespuestaProductora>>();
        lista.ShouldNotBeNull();
        lista.ShouldContain(p => p.Cedula == CedulaUno);
        lista.ShouldNotContain(p => p.Cedula == CedulaDos);
    }

    [Fact]
    public async Task OperadorCat_noVeElHistorialDeCambios()
    {
        // Es información de auditoría: sigue siendo de administradores.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, "PAT", comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/productoras/{productora.Id}/historial");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

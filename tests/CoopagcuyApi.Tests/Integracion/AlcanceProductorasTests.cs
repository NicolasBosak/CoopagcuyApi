using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
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
    public async Task OperadorCat_noCreaProductoraEnComunidadDeOtroCentro()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, CatReferencia = NIE
                catAsignado = "PAT",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);

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
            api, CedulaUno, CentroAcopio.NIE, comunidadId: 2);

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
        // Sin esta comprobación, una edición sacaría a la productora de su
        // alcance de un solo golpe: entra siendo de PAT y sale siendo de NIE,
        // fuera de la vista de quien la movió y dentro de la de otro centro.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Intento de traslado",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, de otro centro
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_desactivaYReactivaProductoraDeSuCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);
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
            api, CedulaUno, CentroAcopio.NIE, comunidadId: 2);

        var respuesta = await api.ComoOperadorCat("PAT").PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_veLasInactivasDeSuCentroYNingunaDeOtro()
    {
        // Sin las inactivas a la vista no hay forma de reactivar ninguna.
        await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1, activa: false);
        await Sembrador.ProductoraAsync(
            api, CedulaDos, CentroAcopio.NIE, comunidadId: 2, activa: false);

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
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/productoras/{productora.Id}/historial");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

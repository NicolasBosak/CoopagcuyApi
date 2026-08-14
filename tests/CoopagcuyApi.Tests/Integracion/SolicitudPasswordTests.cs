using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El endpoint de solicitud es anónimo y público. Su invariante principal no
/// es funcional sino de privacidad: desde fuera debe ser imposible distinguir
/// una cédula con cuenta de una sin ella.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class SolicitudPasswordTests(ApiFactory api) : IAsyncLifetime
{
    // Cédulas ecuatorianas VÁLIDAS: provincia 01, tercer dígito < 6 y dígito
    // verificador correcto por módulo 10. No sirve inventarlas — ValidadorCedula
    // rechaza cualquier cosa que no cuadre y el endpoint devolvería 400.
    private const string CedulaConCuenta = "0104576277";
    private const string CedulaSinCuenta = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> Solicitar(HttpClient cliente, string cedula) =>
        cliente.PostAsJsonAsync("/api/auth/recuperacion", new { cedula });

    [Fact]
    public async Task CedulaConCuenta_yCedulaSin_devuelvenLaMismaRespuesta()
    {
        await Sembrador.UsuarioAsync(api, CedulaConCuenta);
        var cliente = api.ComoAnonimo();

        var conCuenta = await Solicitar(cliente, CedulaConCuenta);
        var sinCuenta = await Solicitar(cliente, CedulaSinCuenta);

        conCuenta.StatusCode.ShouldBe(HttpStatusCode.OK);
        sinCuenta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpoConCuenta = await conCuenta.Content.ReadAsStringAsync();
        var cuerpoSinCuenta = await sinCuenta.Content.ReadAsStringAsync();
        cuerpoConCuenta.ShouldBe(cuerpoSinCuenta);
    }

    [Fact]
    public async Task CedulaSinCuenta_noDejaRastroEnLaTabla()
    {
        await Solicitar(api.ComoAnonimo(), CedulaSinCuenta);

        await using var db = api.NuevoDbContext();
        (await db.SolicitudesRestablecerPassword.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CedulaConCuenta_creaUnaSolicitudPendiente()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaConCuenta);

        await Solicitar(api.ComoAnonimo(), CedulaConCuenta);

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.SingleAsync();
        solicitud.UsuarioId.ShouldBe(usuario.Id);
        solicitud.CedulaSolicitada.ShouldBe(CedulaConCuenta);
        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Pendiente);
        solicitud.FechaResolucion.ShouldBeNull();
    }

    [Fact]
    public async Task TresSolicitudesSeguidas_dejanUnaSolaFila()
    {
        await Sembrador.UsuarioAsync(api, CedulaConCuenta);
        var cliente = api.ComoAnonimo();

        // El operador nervioso que pulsa el botón varias veces no debe
        // multiplicar el trabajo del administrador
        for (var i = 0; i < 3; i++)
        {
            var respuesta = await Solicitar(cliente, CedulaConCuenta);
            respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using var db = api.NuevoDbContext();
        (await db.SolicitudesRestablecerPassword.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CedulaConDigitoVerificadorMalo_devuelve400()
    {
        // Mismo número que CedulaConCuenta con el último dígito cambiado: es
        // el error de tipeo típico que el dígito verificador existe para atrapar
        var respuesta = await Solicitar(api.ComoAnonimo(), "0104576270");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UsuarioDesactivado_noGeneraSolicitud()
    {
        await Sembrador.UsuarioAsync(api, CedulaConCuenta, activo: false);

        var respuesta = await Solicitar(api.ComoAnonimo(), CedulaConCuenta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = api.NuevoDbContext();
        (await db.SolicitudesRestablecerPassword.CountAsync()).ShouldBe(0);
    }
}

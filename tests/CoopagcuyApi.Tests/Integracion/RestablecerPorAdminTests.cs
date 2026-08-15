using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El restablecimiento por iniciativa del administrador es la única vía por la
/// que alguien toca la cuenta de otro sin que medie una solicitud. Por eso
/// tiene que dejar rastro, y por eso no puede aplicarse a uno mismo.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class RestablecerPorAdminTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaOperadora = "0104576277";
    private const string CedulaAdmin = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient ComoAdminConCedula() =>
        api.ComoUsuario("AdminCooperativa", CedulaAdmin);

    private static Task<HttpResponseMessage> Restablecer(HttpClient cliente, int usuarioId) =>
        cliente.PostAsync($"/api/auth/recuperacion/usuario/{usuarioId}", null);

    [Fact]
    public async Task Restablecer_generaTemporal_revocaSesiones_yDejaFilaDeAdministrador()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora);

        await using (var db = api.NuevoDbContext())
        {
            var ahora = DateTime.UtcNow;
            db.RefreshTokens.Add(new RefreshToken
            {
                UsuarioId = usuario.Id,
                TokenHash = "hash-de-una-sesion-abierta",
                FechaCreacion = ahora,
                FechaUltimoUso = ahora,
                FechaExpiracion = ahora.AddDays(7)
            });
            await db.SaveChangesAsync();
        }

        var respuesta = await Restablecer(ComoAdminConCedula(), usuario.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var temporal = await respuesta.Content.ReadFromJsonAsync<PasswordTemporalDto>();
        PoliticaPassword.EsValida(temporal!.PasswordTemporal).ShouldBeTrue();

        await using var verificacion = api.NuevoDbContext();

        var actualizado = await verificacion.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        actualizado.DebeCambiarPassword.ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(temporal.PasswordTemporal, actualizado.PasswordHash)
            .ShouldBeTrue();

        var sesionesVivas = await verificacion.RefreshTokens
            .CountAsync(t => t.UsuarioId == usuario.Id && !t.Revocado);
        sesionesVivas.ShouldBe(0);

        var solicitud = await verificacion.SolicitudesRestablecerPassword
            .AsNoTracking().SingleAsync();
        solicitud.Origen.ShouldBe(OrigenSolicitudPassword.Administrador);
        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Resuelta);
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);
    }

    [Fact]
    public async Task Restablecer_aQuienYaTeniaPendiente_resuelveEsaFilaSinCrearOtra()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora);

        await using (var db = api.NuevoDbContext())
        {
            db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
            {
                UsuarioId = usuario.Id,
                CedulaSolicitada = CedulaOperadora
            });
            await db.SaveChangesAsync();
        }

        await Restablecer(ComoAdminConCedula(), usuario.Id);

        await using var verificacion = api.NuevoDbContext();
        var solicitud = await verificacion.SolicitudesRestablecerPassword
            .AsNoTracking().SingleAsync();

        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Resuelta);
        // Esa persona SÍ pidió el cambio: el origen no se reescribe
        solicitud.Origen.ShouldBe(OrigenSolicitudPassword.Usuario);
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);
    }

    [Fact]
    public async Task Restablecer_aUnUsuarioDesactivado_devuelve409()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora, activo: false);

        var respuesta = await Restablecer(ComoAdminConCedula(), usuario.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        actualizado.DebeCambiarPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task UnAdministrador_noPuedeRestablecerseASiMismo()
    {
        var admin = await Sembrador.UsuarioAsync(
            api, CedulaAdmin, rol: RolUsuario.AdminCooperativa, cat: null);

        var respuesta = await Restablecer(ComoAdminConCedula(), admin.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == admin.Id);
        // Su contraseña sigue intacta: no quedó a medias
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, actualizado.PasswordHash)
            .ShouldBeTrue();
        actualizado.DebeCambiarPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Restablecer_aUnUsuarioInexistente_devuelve404()
    {
        var respuesta = await Restablecer(ComoAdminConCedula(), 999999);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnOperador_noPuedeRestablecerContrasenas()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora);

        var respuesta = await Restablecer(api.ComoOperadorCat("PAT"), usuario.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

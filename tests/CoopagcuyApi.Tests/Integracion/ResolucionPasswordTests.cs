using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Resolver una solicitud es la operación con más efectos a la vez: cambia la
/// contraseña, marca la obligación de cambiarla y revoca las sesiones. La
/// revocación es la que se olvida al implementar y la que más importa: si la
/// solicitud vino porque alguien tomó la tablet, restablecer sin revocar deja
/// al intruso dentro con su sesión de 7 días intacta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ResolucionPasswordTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaOperadora = "0104576277";
    private const string CedulaAdmin = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// Siembra un usuario, le crea una solicitud pendiente y devuelve ambos ids
    private async Task<(int UsuarioId, int SolicitudId)> ConSolicitudPendienteAsync(
        bool activo = true)
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora, activo: activo);

        await using var db = api.NuevoDbContext();
        var solicitud = new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = CedulaOperadora
        };
        db.SolicitudesRestablecerPassword.Add(solicitud);
        await db.SaveChangesAsync();

        return (usuario.Id, solicitud.Id);
    }

    [Fact]
    public async Task Listar_devuelveSoloLasPendientes()
    {
        var (usuarioId, _) = await ConSolicitudPendienteAsync();

        await using (var db = api.NuevoDbContext())
        {
            db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
            {
                UsuarioId = usuarioId,
                CedulaSolicitada = CedulaOperadora,
                Estado = EstadoSolicitudPassword.Descartada,
                FechaResolucion = DateTime.UtcNow,
                ResueltaPor = CedulaAdmin
            });
            await db.SaveChangesAsync();
        }

        var pendientes = await api.ComoAdmin()
            .GetFromJsonAsync<List<SolicitudPasswordDto>>("/api/auth/recuperacion");
        var todas = await api.ComoAdmin()
            .GetFromJsonAsync<List<SolicitudPasswordDto>>(
                "/api/auth/recuperacion?incluirResueltas=true");

        pendientes!.Count.ShouldBe(1);
        pendientes[0].Estado.ShouldBe("Pendiente");
        todas!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Resolver_devuelveTemporalValida_yMarcaLaObligacionDeCambiarla()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync();

        var respuesta = await api.ComoAdmin()
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var temporal = await respuesta.Content.ReadFromJsonAsync<PasswordTemporalDto>();
        PoliticaPassword.EsValida(temporal!.PasswordTemporal).ShouldBeTrue();

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId);
        usuario.DebeCambiarPassword.ShouldBeTrue();
        // La temporal devuelta es la que quedó guardada, hasheada
        BCrypt.Net.BCrypt.Verify(temporal.PasswordTemporal, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Resolver_cierraLaSolicitud_conFechaYAutor()
    {
        var (_, solicitudId) = await ConSolicitudPendienteAsync();

        await api.ComoUsuario("AdminCooperativa", CedulaAdmin)
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.AsNoTracking()
            .FirstAsync(s => s.Id == solicitudId);

        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Resuelta);
        solicitud.FechaResolucion.ShouldNotBeNull();
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);
    }

    [Fact]
    public async Task Resolver_revocaLasSesionesActivasDelUsuario()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync();

        await using (var db = api.NuevoDbContext())
        {
            var ahora = DateTime.UtcNow;
            db.RefreshTokens.Add(new RefreshToken
            {
                UsuarioId = usuarioId,
                TokenHash = "hash-de-una-sesion-abierta",
                FechaCreacion = ahora,
                FechaUltimoUso = ahora,
                FechaExpiracion = ahora.AddDays(7)
            });
            await db.SaveChangesAsync();
        }

        await api.ComoAdmin()
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        await using var verificacion = api.NuevoDbContext();
        var vivas = await verificacion.RefreshTokens
            .CountAsync(t => t.UsuarioId == usuarioId && !t.Revocado);
        vivas.ShouldBe(0);
    }

    [Fact]
    public async Task ResolverDosVeces_laSegundaDevuelve409()
    {
        var (_, solicitudId) = await ConSolicitudPendienteAsync();
        var cliente = api.ComoAdmin();

        var primera = await cliente.PostAsync(
            $"/api/auth/recuperacion/{solicitudId}/resolver", null);
        var segunda = await cliente.PostAsync(
            $"/api/auth/recuperacion/{solicitudId}/resolver", null);

        primera.StatusCode.ShouldBe(HttpStatusCode.OK);
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Resolver_deUsuarioDesactivado_devuelve409_ySinCambiarSuPassword()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync(activo: false);

        var respuesta = await api.ComoAdmin()
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId);
        usuario.DebeCambiarPassword.ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Resolver_solicitudInexistente_devuelve404()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsync("/api/auth/recuperacion/999999/resolver", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Descartar_dejaConstancia_sinTocarLaPassword()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync();

        var respuesta = await api.ComoUsuario("AdminTecnico", CedulaAdmin)
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/descartar", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.AsNoTracking()
            .FirstAsync(s => s.Id == solicitudId);
        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Descartada);
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);

        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId);
        usuario.DebeCambiarPassword.ShouldBeFalse();
    }
}

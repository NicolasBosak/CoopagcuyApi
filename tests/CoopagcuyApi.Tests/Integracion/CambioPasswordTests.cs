using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El circuito se cierra aquí: la bandera que pone el restablecimiento tiene
/// que llegar al front en la respuesta del login, y tiene que bajarse al
/// cambiar la contraseña. Si se queda activa, el operador entra en un bucle
/// del que no puede salir.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CambioPasswordTests(ApiFactory api) : IAsyncLifetime
{
    private const string Cedula = "0104576277";
    private const string PasswordNueva = "montania2026";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> Cambiar(
        HttpClient cliente, string actual, string nueva) =>
        cliente.PostAsJsonAsync("/api/auth/cambiar-password",
            new { passwordActual = actual, passwordNueva = nueva });

    [Fact]
    public async Task Login_deUsuarioConObligacionPendiente_traeLaBandera()
    {
        var usuario = await Sembrador.UsuarioAsync(api, Cedula);
        await using (var db = api.NuevoDbContext())
        {
            var guardado = await db.Usuarios.FirstAsync(u => u.Id == usuario.Id);
            guardado.DebeCambiarPassword = true;
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login",
            new { cedula = Cedula, password = Sembrador.PasswordPorDefecto });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await respuesta.Content.ReadFromJsonAsync<LoginResponseDto>();
        login!.DebeCambiarPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_deUsuarioNormal_traeLaBanderaApagada()
    {
        await Sembrador.UsuarioAsync(api, Cedula);

        var respuesta = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login",
            new { cedula = Cedula, password = Sembrador.PasswordPorDefecto });

        var login = await respuesta.Content.ReadFromJsonAsync<LoginResponseDto>();
        login!.DebeCambiarPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Cambiar_conLaPasswordActualCorrecta_bajaLaBandera()
    {
        var usuario = await Sembrador.UsuarioAsync(api, Cedula);
        await using (var db = api.NuevoDbContext())
        {
            var guardado = await db.Usuarios.FirstAsync(u => u.Id == usuario.Id);
            guardado.DebeCambiarPassword = true;
            await db.SaveChangesAsync();
        }

        var respuesta = await Cambiar(
            api.ComoUsuario("OperadorCAT", Cedula),
            Sembrador.PasswordPorDefecto, PasswordNueva);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var verificacion = api.NuevoDbContext();
        var actualizado = await verificacion.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        actualizado.DebeCambiarPassword.ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(PasswordNueva, actualizado.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Cambiar_conLaPasswordActualIncorrecta_devuelve401_ySinCambiarNada()
    {
        var usuario = await Sembrador.UsuarioAsync(api, Cedula);

        var respuesta = await Cambiar(
            api.ComoUsuario("OperadorCAT", Cedula), "otra-clave-9999", PasswordNueva);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, actualizado.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Cambiar_aUnaPasswordQueIncumpleLaPolitica_devuelve400()
    {
        await Sembrador.UsuarioAsync(api, Cedula);

        // Sin dígitos y demasiado corta
        var respuesta = await Cambiar(
            api.ComoUsuario("OperadorCAT", Cedula),
            Sembrador.PasswordPorDefecto, "corta");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cambiar_sinAutenticar_devuelve401()
    {
        var respuesta = await Cambiar(
            api.ComoAnonimo(), Sembrador.PasswordPorDefecto, PasswordNueva);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

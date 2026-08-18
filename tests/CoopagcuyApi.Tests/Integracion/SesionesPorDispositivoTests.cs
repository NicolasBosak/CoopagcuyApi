using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La pantalla de sesiones mostraba cinco filas del mismo usuario: cada inicio
/// de sesión insertaba una fila nueva sin mirar si esa misma tablet ya tenía
/// sesión abierta. Una tablet, una sesión.
///
/// Las filas no se borran, se revocan: el rastro de auditoría se conserva.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class SesionesPorDispositivoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0101112225";

    private sealed record Sesion(
        int Id, int UsuarioId, string NombreUsuario, string Cedula, string Rol,
        string? CatAsignado, string? DispositivoId, string? UserAgent,
        string? IpCreacion, DateTime FechaCreacion, DateTime FechaUltimoUso,
        DateTime FechaExpiracion, bool EsSesionActual, string Dispositivo);

    private async Task EntrarAsync(string? dispositivoId, string? userAgent = null)
    {
        var cliente = api.ComoAnonimo();
        if (userAgent is not null)
            cliente.DefaultRequestHeaders.Add("User-Agent", userAgent);

        var respuesta = await cliente.PostAsJsonAsync("/api/auth/login", new
        {
            cedula = Cedula,
            password = Sembrador.PasswordPorDefecto,
            dispositivoId
        });
        respuesta.EnsureSuccessStatusCode();
    }

    private async Task<List<Sesion>> SesionesAsync()
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync("/api/auth/sesiones");
        respuesta.EnsureSuccessStatusCode();
        var sesiones = await respuesta.Content.ReadFromJsonAsync<List<Sesion>>();
        sesiones.ShouldNotBeNull();
        return sesiones;
    }

    [Fact]
    public async Task DosIniciosDeSesionDelMismoDispositivo_dejanUnaSolaSesion()
    {
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01");
        await EntrarAsync("tablet-pat-01");

        var sesiones = await SesionesAsync();

        sesiones.Count(s => s.Cedula == Cedula).ShouldBe(1);
    }

    [Fact]
    public async Task DosDispositivosDistintos_mantienenSusDosSesiones()
    {
        // La regla no puede cerrar tablets legítimas de otros compañeros.
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01");
        await EntrarAsync("tablet-pat-02");

        var sesiones = await SesionesAsync();

        sesiones.Count(s => s.Cedula == Cedula).ShouldBe(2);
    }

    [Fact]
    public async Task UnInicioSinDispositivo_noCierraLasSesionesExistentes()
    {
        // Sin identificador no hay forma de saber de qué tablet se trata:
        // revocar por usuario cerraría sesiones de otras tablets legítimas.
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01");
        await EntrarAsync(null);

        var sesiones = await SesionesAsync();

        sesiones.Count(s => s.Cedula == Cedula).ShouldBe(2);
    }

    [Fact]
    public async Task LaSesionDescribeElDispositivo()
    {
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01",
            "Mozilla/5.0 (Linux; Android 13; SM-X200) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var sesiones = await SesionesAsync();
        var mia = sesiones.Single(s => s.Cedula == Cedula);

        mia.Dispositivo.ShouldBe("Chrome · Android");
        mia.DispositivoId.ShouldBe("tablet-pat-01");
    }
}

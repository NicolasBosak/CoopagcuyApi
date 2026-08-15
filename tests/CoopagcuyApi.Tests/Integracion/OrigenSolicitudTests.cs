using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El origen distingue "esta persona pidió el cambio" de "un administrador
/// tocó su cuenta sin que nadie se lo pidiera". Lo segundo es lo único que
/// conviene poder auditar después, así que tiene que quedar registrado.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class OrigenSolicitudTests(ApiFactory api) : IAsyncLifetime
{
    private const string Cedula = "0104576277";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnaSolicitudNueva_naceConOrigenUsuario()
    {
        await Sembrador.UsuarioAsync(api, Cedula);

        await api.ComoAnonimo().PostAsJsonAsync(
            "/api/auth/recuperacion", new { cedula = Cedula });

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.SingleAsync();
        solicitud.Origen.ShouldBe(OrigenSolicitudPassword.Usuario);
    }

    [Fact]
    public async Task LaBandeja_exponeElOrigenDeCadaSolicitud()
    {
        await Sembrador.UsuarioAsync(api, Cedula);
        await api.ComoAnonimo().PostAsJsonAsync(
            "/api/auth/recuperacion", new { cedula = Cedula });

        var solicitudes = await api.ComoAdmin()
            .GetFromJsonAsync<List<SolicitudPasswordDto>>("/api/auth/recuperacion");

        solicitudes!.Single().Origen.ShouldBe("Usuario");
    }
}

using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Las dos pruebas que dan valor a este diseño comprueban una AUSENCIA: que
/// enviar una contraseña al crear o al actualizar no tenga efecto. Un
/// formulario sin campo no demuestra eso — solo esconde la puerta; el endpoint
/// sigue ahí y se llama igual con curl.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AltaUsuarioPasswordTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaNueva = "0104576277";
    private const string CedulaExistente = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> Crear(HttpClient cliente, object cuerpo) =>
        cliente.PostAsJsonAsync("/api/usuarios", cuerpo);

    [Fact]
    public async Task Crear_devuelveUnaTemporalYObligaACambiarla()
    {
        var respuesta = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Rosa Lliguicota",
            cedula = CedulaNueva,
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var creado = await respuesta.Content.ReadFromJsonAsync<UsuarioCreadoDto>();
        PoliticaPassword.EsValida(creado!.PasswordTemporal).ShouldBeTrue();

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Cedula == CedulaNueva);

        usuario.DebeCambiarPassword.ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(creado.PasswordTemporal, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Crear_ignoraCualquierPasswordQueLlegueEnElCuerpo()
    {
        // El campo ya no existe en el DTO, pero el endpoint sigue siendo
        // llamable con curl: esto comprueba que la puerta está cerrada de
        // verdad y no solo oculta en el formulario del front
        var respuesta = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Rosa Lliguicota",
            cedula = CedulaNueva,
            password = "elegida-por-el-admin-1234",
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });

        var creado = await respuesta.Content.ReadFromJsonAsync<UsuarioCreadoDto>();

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Cedula == CedulaNueva);

        BCrypt.Net.BCrypt.Verify("elegida-por-el-admin-1234", usuario.PasswordHash)
            .ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(creado!.PasswordTemporal, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Actualizar_ignoraCualquierNuevaPasswordQueLlegue()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaExistente);

        var respuesta = await api.ComoAdmin().PutAsJsonAsync(
            $"/api/usuarios/{usuario.Id}", new
            {
                nombreCompleto = "Ana Quizhpe",
                rol = "OperadorCAT",
                catAsignado = "PAT",
                nuevaPassword = "elegida-por-el-admin-1234"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);

        // El nombre sí cambió: la edición sigue funcionando
        actualizado.NombreCompleto.ShouldBe("Ana Quizhpe");
        // La contraseña no
        BCrypt.Net.BCrypt.Verify("elegida-por-el-admin-1234", actualizado.PasswordHash)
            .ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, actualizado.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ElUsuarioRecienCreado_entraYElSistemaLePideCambiarla()
    {
        var creacion = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Rosa Lliguicota",
            cedula = CedulaNueva,
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });
        var creado = await creacion.Content.ReadFromJsonAsync<UsuarioCreadoDto>();

        var login = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login",
            new { cedula = CedulaNueva, password = creado!.PasswordTemporal });

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sesion = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        sesion!.DebeCambiarPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Crear_conCedulaRepetida_sigueDevolviendo409()
    {
        await Sembrador.UsuarioAsync(api, CedulaExistente);

        var respuesta = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Otra persona",
            cedula = CedulaExistente,
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}

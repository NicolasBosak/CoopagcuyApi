using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El índice único parcial es lo que impide que un operador nervioso llene
/// la bandeja del administrador pulsando el botón cinco veces. Se verifica
/// contra Postgres real porque un índice filtrado no existe en memoria: es
/// justo el tipo de garantía que un doble de prueba no reproduce.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class EsquemaRecuperacionTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DosSolicitudesPendientes_delMismoUsuario_chocanConElIndice()
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = "Operadora de prueba",
            Cedula = "0102030405",
            PasswordHash = "hash-irrelevante",
            Rol = RolUsuario.OperadorCAT,
            CatAsignado = "PAT"
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula
        });
        await db.SaveChangesAsync();

        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UnaPendienteYUnaResuelta_delMismoUsuario_conviven()
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = "Operadora de prueba",
            Cedula = "0102030405",
            PasswordHash = "hash-irrelevante",
            Rol = RolUsuario.OperadorCAT,
            CatAsignado = "PAT"
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        // El historial no estorba: el filtro del índice solo mira las pendientes
        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula,
            Estado = EstadoSolicitudPassword.Resuelta,
            FechaResolucion = DateTime.UtcNow,
            ResueltaPor = "0102030499"
        });
        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula
        });

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UsuarioNuevo_naceSinObligacionDeCambiarPassword()
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = "Operadora de prueba",
            Cedula = "0102030405",
            PasswordHash = "hash-irrelevante",
            Rol = RolUsuario.OperadorCAT,
            CatAsignado = "PAT"
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        var guardado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);

        guardado.DebeCambiarPassword.ShouldBeFalse();
    }
}

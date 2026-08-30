using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Asignar una contraseña temporal es una regla de seguridad que aplican tres
/// rutas distintas (alta de usuario, resolución de solicitud y restablecimiento
/// por el administrador). Se comprueba aquí, sin base de datos, para que las
/// tres hereden la misma garantía sin repetir la verificación.
/// </summary>
public class CredencialTemporalTests
{
    private static Usuario UsuarioDePrueba() => new()
    {
        NombreCompleto = "Operadora de prueba",
        Cedula = "0104576277",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("la-anterior-1234"),
        Rol = RolUsuario.OperadorCAT,
        CatAsignado = "PAT"
    };

    [Fact]
    public void Asignar_dejaAlUsuarioObligadoACambiarla()
    {
        var usuario = UsuarioDePrueba();

        CredencialTemporal.Asignar(usuario);

        usuario.DebeCambiarPassword.ShouldBeTrue();
    }

    [Fact]
    public void Asignar_devuelveExactamenteLaContrasenaQueQuedaGuardada()
    {
        var usuario = UsuarioDePrueba();

        var temporal = CredencialTemporal.Asignar(usuario);

        // Si esto falla, el administrador dicta una contraseña con la que el
        // usuario no puede entrar: el peor fallo posible de esta función
        BCrypt.Net.BCrypt.Verify(temporal, usuario.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public void Asignar_invalidaLaContrasenaAnterior()
    {
        var usuario = UsuarioDePrueba();

        CredencialTemporal.Asignar(usuario);

        BCrypt.Net.BCrypt.Verify("la-anterior-1234", usuario.PasswordHash)
            .ShouldBeFalse();
    }

    [Fact]
    public void Asignar_devuelveUnaContrasenaQueCumpleLaPolitica()
    {
        for (var i = 0; i < 100; i++)
            PoliticaPassword.EsValida(CredencialTemporal.Asignar(UsuarioDePrueba()))
                .ShouldBeTrue();
    }
}

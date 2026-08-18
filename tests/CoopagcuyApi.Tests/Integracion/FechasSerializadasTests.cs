using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Toda fecha que sale del API debe llevar su zona horaria explícita.
///
/// El fallo que motiva estas pruebas: la pantalla de sesiones mostraba las
/// 9:51 p. m. cuando el reloj del equipo marcaba las 4:56 p. m. —cinco horas
/// de más, justo el desfase de Ecuador (UTC-5)—. No era un problema de la
/// pantalla: era UTC pintado como si fuera hora local.
///
/// La cadena de causas:
///   · Program.cs activa Npgsql.EnableLegacyTimestampBehavior.
///   · Las columnas son "timestamp without time zone", así que al LEER de
///     Postgres los DateTime vuelven con Kind=Unspecified.
///   · System.Text.Json serializa un Unspecified SIN la "Z" final.
///   · new Date("2026-08-18T21:51:00") en el navegador interpreta una fecha
///     sin zona como hora LOCAL, y muestra 21:51 tal cual.
///
/// Afecta a todas las fechas del sistema, no solo a las sesiones: es solo que
/// en una lista de sesiones la hora exacta se mira con lupa.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class FechasSerializadasTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0101112225";

    [Fact]
    public async Task LasFechasDeLasSesiones_llevanLaZonaHorariaExplicita()
    {
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        var login = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login", new
        {
            cedula = Cedula,
            password = Sembrador.PasswordPorDefecto,
            dispositivoId = "tablet-pat-01"
        });
        login.EnsureSuccessStatusCode();

        var respuesta = await api.ComoAdminTecnico().GetAsync("/api/auth/sesiones");
        var json = await respuesta.Content.ReadAsStringAsync();

        // Se comprueba sobre el JSON CRUDO a propósito. Deserializar a DateTime
        // ocultaría el fallo: .NET rellena la zona que falta, y la prueba
        // pasaría mientras el navegador sigue leyendo mal la hora.
        json.ShouldContain("\"fechaUltimoUso\"");
        ExtraerFechas(json, "fechaUltimoUso").ShouldAllBe(f => f.EndsWith("Z"));
        ExtraerFechas(json, "fechaCreacion").ShouldAllBe(f => f.EndsWith("Z"));
        ExtraerFechas(json, "fechaExpiracion").ShouldAllBe(f => f.EndsWith("Z"));
    }

    [Fact]
    public async Task LaFechaDeUnDespacho_llevaLaZonaHorariaExplicita()
    {
        // Misma raíz, otra pantalla: el historial de despachos pinta la fecha
        // con toLocaleString y la desplazaría igual.
        await Sembrador.DespachoAsync(api, DateTime.UtcNow, "Cliente de hoy");

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/faenamiento/despachos");
        var json = await respuesta.Content.ReadAsStringAsync();

        ExtraerFechas(json, "fechaDespacho").ShouldAllBe(f => f.EndsWith("Z"));
    }

    /// Valores de todas las apariciones de "propiedad":"...".
    private static List<string> ExtraerFechas(string json, string propiedad)
    {
        var valores = new List<string>();
        var marca = $"\"{propiedad}\":\"";
        var i = json.IndexOf(marca, StringComparison.Ordinal);
        while (i >= 0)
        {
            var inicio = i + marca.Length;
            var fin = json.IndexOf('"', inicio);
            valores.Add(json[inicio..fin]);
            i = json.IndexOf(marca, fin, StringComparison.Ordinal);
        }

        valores.ShouldNotBeEmpty(
            $"El JSON no traía ninguna propiedad '{propiedad}': la prueba no " +
            "estaría comprobando nada.");
        return valores;
    }
}

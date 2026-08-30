using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Aplica las migraciones una sola vez y limpia las tablas entre pruebas.
///
/// No se usa una transacción con rollback por prueba: el código de producción
/// abre sus propias transacciones dentro de CreateExecutionStrategy, y anidar
/// no funciona. Por eso Respawn, que trunca.
/// </summary>
public class BaseDatosFixture
{
    private NpgsqlConnection? _conexion;
    private Respawner? _respawner;

    public async Task InicializarAsync()
    {
        // Program.cs fija este switch al arrancar, pero el fixture crea su
        // propio DbContext antes de eso: sin esto, las migraciones fallan con
        // "Cannot write DateTime with Kind=Unspecified".
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        await EsperarPostgresAsync();

        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ApiFactory.Cadena)
            .Options;

        await using (var db = new AppDbContext(opciones))
            await db.Database.MigrateAsync();

        _conexion = new NpgsqlConnection(ApiFactory.Cadena);
        await _conexion.OpenAsync();

        _respawner = await Respawner.CreateAsync(_conexion, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore =
            [
                // El historial de migraciones debe sobrevivir a la limpieza
                new Table("public", "__EFMigrationsHistory"),
                // Comunidades es un CATÁLOGO sembrado por la migración con
                // HasData, no datos de prueba. Truncarlo dejaba la tabla vacía
                // desde la primera limpieza, y cualquier alta de productora
                // fallaba con 400 ("la comunidad no existe o está inactiva")
                // por un motivo que no tenía nada que ver con la prueba.
                new Table("public", "Comunidades"),
                // Misma razón que Comunidades: Provincias y Cantones son
                // catálogo sembrado por migración. Truncarlos dejaría a
                // Comunidad sin el cantón al que apunta y toda la batería
                // caería por clave foránea, no por lo que cada prueba mira.
                new Table("public", "Provincias"),
                new Table("public", "Cantones"),
                // El CAT también es catálogo sembrado por migración: truncarlo
                // rompería en cascada cualquier Lote, Productora, Usuario o
                // EntregaPendienteVinculacion que apunte a él por clave foránea.
                new Table("public", "CentrosAcopio")
            ]
        });
    }

    public Task LimpiarAsync() => _respawner!.ResetAsync(_conexion!);

    public async ValueTask LiberarAsync()
    {
        if (_conexion is not null) await _conexion.DisposeAsync();
    }

    /// Postgres tarda en aceptar conexiones aunque el contenedor ya exista.
    /// Se reintenta 30 segundos antes de rendirse con un mensaje accionable.
    private static async Task EsperarPostgresAsync()
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var prueba = new NpgsqlConnection(ApiFactory.Cadena);
                await prueba.OpenAsync();
                return;
            }
            // 28P01 (contraseña/usuario inválidos) y 3D000 (la base no
            // existe) no se van a arreglar reintentando: son un error de
            // configuración, no un Postgres que todavía está arrancando.
            // Fallar rápido aquí evita quemar los 30 s completos y, sobre
            // todo, evita el mensaje engañoso de "¿levantaste el compose?"
            // cuando el contenedor sí está arriba pero mal configurado.
            catch (PostgresException ex) when (ex.SqlState is "28P01" or "3D000")
            {
                var (host, puerto, baseDatos) = DatosDeConexionSinContrasena();
                throw new InvalidOperationException(
                    "El Postgres de pruebas respondió pero rechazó la conexión " +
                    $"(SqlState={ex.SqlState}: {ex.MessageText}). Servidor: " +
                    $"{host}:{puerto}, base '{baseDatos}'. Revisa usuario, " +
                    "contraseña y nombre de base en TEST_DB_CONNECTION.", ex);
            }
            catch (NpgsqlException) when (DateTime.UtcNow < limite)
            {
                await Task.Delay(500);
            }
            catch (NpgsqlException ex)
            {
                var (host, puerto, baseDatos) = DatosDeConexionSinContrasena();
                throw new InvalidOperationException(
                    "No se pudo conectar al Postgres de pruebas en 30 s. Servidor: " +
                    $"{host}:{puerto}, base '{baseDatos}' (sin la contraseña: " +
                    "no va a los logs de un repo público). " +
                    "¿Levantaste docker-compose.tests.yml?", ex);
            }
        }
    }

    /// Host, puerto y base son lo que hace falta para diagnosticar; la
    /// contraseña no, y estos mensajes terminan en los logs de un repo
    /// público vía TEST_DB_CONNECTION.
    private static (string Host, int Puerto, string BaseDatos) DatosDeConexionSinContrasena()
    {
        var builder = new NpgsqlConnectionStringBuilder(ApiFactory.Cadena);
        return (builder.Host ?? "?", builder.Port, builder.Database ?? "?");
    }
}

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
            // El historial de migraciones debe sobrevivir a la limpieza
            TablesToIgnore = [new Table("public", "__EFMigrationsHistory")]
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
            catch (NpgsqlException) when (DateTime.UtcNow < limite)
            {
                await Task.Delay(500);
            }
            catch (NpgsqlException ex)
            {
                throw new InvalidOperationException(
                    "No se pudo conectar al Postgres de pruebas en 30 s. " +
                    $"TEST_DB_CONNECTION = '{ApiFactory.Cadena}'. " +
                    "¿Levantaste docker-compose.tests.yml?", ex);
            }
        }
    }
}

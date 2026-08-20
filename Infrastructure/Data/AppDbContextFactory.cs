using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CoopagcuyApi.Infrastructure.Data;

/// <summary>
/// Fábrica en tiempo de diseño para EF Core: permite ejecutar migraciones
/// (dotnet ef …) sin arrancar toda la aplicación. Sin esto, el generador
/// de migraciones ejecutaría Program.cs, que exige Jwt:Key y demás
/// secretos que no existen en el pipeline de CI.
///
/// La cadena de conexión se toma de:
///   · variable de entorno ConnectionStrings__NeonDb (en CI), o
///   · user-secrets del proyecto (en la máquina del desarrollador).
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Debe coincidir con Program.cs: sin esto, Npgsql mapea DateTime a
        // "timestamp with time zone" (default moderno) en vez de "without",
        // y las migraciones generadas querrían alterar TODA columna de fecha
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<AppDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var conn = config.GetConnectionString("NeonDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NeonDb no configurado. Defínelo en " +
                "user-secrets (local) o como variable de entorno " +
                "ConnectionStrings__NeonDb (CI) para ejecutar migraciones.");

        // Mismos reintentos que Program.cs, y por el mismo motivo: Neon es
        // serverless y suspende el cómputo por inactividad, así que la primera
        // conexión tras un rato paga un arranque en frío. Sin esto, las
        // migraciones del pipeline fallaban con "Timeout during reading
        // attempt" dentro de AuthenticateSASL mientras la aplicación, que sí
        // reintentaba, funcionaba con esa misma cadena de conexión.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options;

        return new AppDbContext(options);
    }
}

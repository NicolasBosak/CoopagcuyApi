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

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}

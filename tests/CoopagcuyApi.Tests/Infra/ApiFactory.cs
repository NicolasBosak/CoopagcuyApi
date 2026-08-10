using Microsoft.AspNetCore.Mvc.Testing;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Arranca la API real con configuración de pruebas. No sustituye servicios
/// por dobles: el objetivo es ejercitar el pipeline completo (autenticación,
/// rate limiter, exception handler) contra un Postgres de verdad.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// Clave de firma solo para pruebas. HMAC-SHA256 exige 256 bits,
    /// es decir 32 caracteres o más.
    public const string ClaveJwt = "clave-de-pruebas-coopagcuy-32-chars!!";

    private const string CadenaPorDefecto =
        "Host=localhost;Port=5433;Database=coopagcuy_test;" +
        "Username=postgres;Password=postgres";

    /// Dentro del compose llega por variable de entorno; fuera, apunta al
    /// Postgres publicado en 5433. Nunca a Neon.
    public static string Cadena =>
        Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
        ?? CadenaPorDefecto;

    // `Program.cs` lee varias claves de configuración de forma ANSIOSA, antes
    // de llamar a `builder.Build()`: `Jwt:Key` (línea ~45, con `?? throw` si
    // falta), la cadena `ConnectionStrings:NeonDb` al registrar el DbContext,
    // y `builder.Environment.IsDevelopment()` en la política de CORS. El
    // mecanismo habitual de `WebApplicationFactory` para inyectar
    // configuración de prueba —sobreescribir vía `ConfigureWebHost` →
    // `ConfigureAppConfiguration`— solo toma efecto DENTRO de
    // `builder.Build()`, así que para esas lecturas llega demasiado tarde: la
    // app ya lanzó su excepción de arranque antes de que el override exista.
    //
    // La única fuente que `WebApplication.CreateBuilder(args)` carga ANTES de
    // esas lecturas ansiosas son las variables de entorno reales del proceso
    // (`.AddEnvironmentVariables()`, sin filtro de prefijo). Por eso se fijan
    // aquí, en el constructor estático, para que ya estén puestas cuando
    // `Program.Main` arranque. Es deliberado no usar también
    // `ConfigureAppConfiguration` para esto mismo: dos mecanismos para la
    // misma configuración son dos fuentes de verdad, y quien lea esto en seis
    // meses "simplificaría" de vuelta a `AddInMemoryCollection` sin saber que
    // eso es justo lo que rompe el arranque contra Postgres de prueba.
    //
    // `ASPNETCORE_ENVIRONMENT=Testing` va por la misma razón: así el CORS usa
    // la lista explícita de orígenes (no el modo laxo de Development) y
    // Swagger queda apagado, como en producción — y queda fijado antes de que
    // `builder.Environment.IsDevelopment()` se evalúe.
    static ApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__NeonDb", Cadena);
        Environment.SetEnvironmentVariable("Jwt__Key", ClaveJwt);
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins", "https://localhost:5173");
        Environment.SetEnvironmentVariable("AzureBlob__ConnectionString", "");
        Environment.SetEnvironmentVariable("QR__BaseUrl", "https://localhost/qr");
    }
}

using System.Net.Http.Headers;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Arranca la API real con configuración de pruebas. No sustituye servicios
/// por dobles: el objetivo es ejercitar el pipeline completo (autenticación,
/// rate limiter, exception handler) contra un Postgres de verdad.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// Clave de firma solo para pruebas. HMAC-SHA256 exige 256 bits,
    /// es decir 32 caracteres o más.
    public const string ClaveJwt = "clave-de-pruebas-coopagcuy-32-chars!!";

    /// Mismo valor que `Jwt:Issuer` en `appsettings.json`: los tokens que
    /// firme la Tarea 5 deben validar contra el emisor real, no uno
    /// inventado, para ejercitar la validación de emisor tal cual ocurre en
    /// producción.
    public const string EmisorJwt = "CoopagcuyApi";

    /// Mismo valor que `Jwt:Audience` en `appsettings.json`, por la misma
    /// razón que `EmisorJwt`.
    public const string AudienciaJwt = "CoopagcuyFrontend";

    private const string CadenaPorDefecto =
        "Host=localhost;Port=5433;Database=coopagcuy_test;" +
        "Username=postgres;Password=postgres";

    /// Dentro del compose llega por variable de entorno; fuera, apunta al
    /// Postgres publicado en 5433. Nunca a Neon.
    public static string Cadena =>
        Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
        ?? CadenaPorDefecto;

    private const string CadenaBlobPorDefecto =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        "BlobEndpoint=http://localhost:10000/devstoreaccount1;";

    /// Dentro del compose llega por variable de entorno; fuera, apunta al
    /// Azurite publicado en 10000. Nunca a una cuenta real de Azure.
    public static string CadenaBlob =>
        Environment.GetEnvironmentVariable("TEST_BLOB_CONNECTION")
        ?? CadenaBlobPorDefecto;

    // `Program.cs` lee varias claves de configuración ANTES de llamar a
    // `builder.Build()` (líneas 29, 41, 45, 57, 58, 170 y 189): `Jwt:Key`
    // (con `?? throw` si falta), `ConnectionStrings:NeonDb` al registrar el
    // DbContext, `Jwt:Issuer`/`Jwt:Audience` al configurar el JWT bearer, y
    // `Cors:AllowedOrigins`/`builder.Environment.IsDevelopment()` en la
    // política de CORS. El mecanismo habitual de `WebApplicationFactory`
    // para inyectar configuración de prueba —sobreescribir vía
    // `ConfigureWebHost` → `ConfigureAppConfiguration`— solo toma efecto
    // DENTRO de `builder.Build()`, así que para esas lecturas llega
    // demasiado tarde: la app ya leyó (o ya lanzó su excepción de arranque)
    // antes de que el override exista.
    //
    // La única fuente que `WebApplication.CreateBuilder(args)` carga ANTES
    // de esas lecturas son las variables de entorno reales del proceso
    // (`.AddEnvironmentVariables()`, sin filtro de prefijo). Por eso se
    // fijan aquí, en el constructor estático, para que ya estén puestas
    // cuando `Program.Main` arranque. Es deliberado no usar también
    // `ConfigureAppConfiguration` para esto mismo: dos mecanismos para la
    // misma configuración son dos fuentes de verdad, y quien lea esto en
    // seis meses "simplificaría" de vuelta a `AddInMemoryCollection` sin
    // saber que eso es justo lo que rompe el arranque contra Postgres de
    // prueba (y, para `Jwt:Issuer`/`Jwt:Audience`, dejaría esas dos claves
    // gobernadas por `appsettings.json` de producción en vez de por
    // `ApiFactory`, que es la trampa que esto evita para la Tarea 5).
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
        Environment.SetEnvironmentVariable("Jwt__Issuer", EmisorJwt);
        Environment.SetEnvironmentVariable("Jwt__Audience", AudienciaJwt);
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins", "https://localhost:5173");
        Environment.SetEnvironmentVariable("AzureBlob__ConnectionString", CadenaBlob);
        Environment.SetEnvironmentVariable("AzureBlob__ContainerName", "qr-test");
        Environment.SetEnvironmentVariable("AzureBlob__ContainerEvidencias", "evidencias-test");
        Environment.SetEnvironmentVariable("QR__BaseUrl", "https://localhost/qr");
    }

    private readonly BaseDatosFixture _baseDatos = new();

    // xUnit invoca esto una vez por colección, antes de la primera prueba
    public async Task InitializeAsync() => await _baseDatos.InicializarAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _baseDatos.LiberarAsync();

    /// Deja la base vacía. Se llama desde InitializeAsync de cada clase de
    /// prueba, no desde DisposeAsync: así una prueba que revienta a mitad no
    /// contamina a la siguiente.
    public Task LimpiarAsync() => _baseDatos.LimpiarAsync();

    /// Contexto independiente para hacer aserciones directas contra la base.
    /// El llamador lo libera.
    public AppDbContext NuevoDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Cadena)
            .Options);

    // Contador para repartir una IP distinta a cada cliente de prueba.
    private int _clientesCreados;

    public HttpClient ComoAnonimo() => ClienteCon(token: null);

    public HttpClient ComoAdmin() => ClienteCon(Jwt.Emitir("AdminCooperativa"));

    public HttpClient ComoOperadorCat(string cat) =>
        ClienteCon(Jwt.Emitir("OperadorCAT", cat));

    public HttpClient ComoOperadorFaenamiento() =>
        ClienteCon(Jwt.Emitir("OperadorFaenamiento"));

    public HttpClient ComoAdminTecnico() => ClienteCon(Jwt.Emitir("AdminTecnico"));

    /// Cliente con la cédula que se le indique: los endpoints que actúan
    /// sobre "el usuario del token" la leen del claim "cedula".
    public HttpClient ComoUsuario(string rol, string cedula) =>
        ClienteCon(Jwt.Emitir(rol, cat: null, cedula: cedula));

    /// <summary>
    /// Cliente con una IP propia y, si se le pasa, un token.
    ///
    /// La IP importa: la política "auth" del rate limiter permite 10
    /// peticiones por minuto y POR IP, y la comparten /api/auth/login y
    /// /api/auth/recuperacion. Sin esto todas las pruebas saldrían de la
    /// misma IP —la batería entera corre en unos segundos, dentro de la
    /// misma ventana— y alguna recibiría 429 en vez de su código esperado,
    /// fallando por un motivo que no tiene que ver con lo que verifica.
    ///
    /// Se hace con X-Forwarded-For y no con un doble del limitador porque
    /// UseForwardedHeaders va primero en el pipeline y reescribe
    /// RemoteIpAddress: así se ejercita el camino real de producción, que es
    /// justamente el que hay que mantener funcionando tras el proxy de Azure.
    ///
    /// El rango 203.0.113.0/24 es TEST-NET-3 (RFC 5737), reservado para
    /// documentación y pruebas. Asume menos de 254 clientes por corrida.
    /// </summary>
    private HttpClient ClienteCon(string? token)
    {
        var cliente = CreateClient();

        var n = Interlocked.Increment(ref _clientesCreados);
        cliente.DefaultRequestHeaders.Add(
            "X-Forwarded-For", $"203.0.113.{(n % 254) + 1}");

        if (token is not null)
            cliente.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

        return cliente;
    }
}

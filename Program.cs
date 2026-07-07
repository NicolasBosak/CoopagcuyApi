using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Exceptions;
using CoopagcuyApi.Features.Catalogos.Services;
using CoopagcuyApi.Features.Faenamiento.Services;
using CoopagcuyApi.Features.Productoras.Services;
using CoopagcuyApi.Features.Productoras.Validators;
using CoopagcuyApi.Features.QR.Services;
using CoopagcuyApi.Features.Recepcion.Services;
using CoopagcuyApi.Features.Reportes.Services;
using CoopagcuyApi.Infrastructure.Data;
using CoopagcuyApi.Infrastructure.Storage;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Base de datos: Neon PostgreSQL ────────────────────────────────────
// Neon (serverless) suspende y corta conexiones inactivas: los errores
// transitorios de conexión se reintentan en lugar de responder 500
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("NeonDb"),
        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

// ── Autenticación JWT ────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key no configurado.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// ── FluentValidation ─────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<CrearProductoraValidator>();

// ── Servicios de módulos ─────────────────────────────────────────────
builder.Services.AddScoped<IProductoraService, ProductoraService>();
builder.Services.AddScoped<IRecepcionService, RecepcionService>();
builder.Services.AddScoped<IGuiaMovilizacionService, GuiaMovilizacionService>();
builder.Services.AddScoped<IMovilizacionService, MovilizacionService>();
builder.Services.AddScoped<IPagoService, PagoService>();
builder.Services.AddScoped<IFaenamientoService, FaenamientoService>();
builder.Services.AddScoped<IQRService, QRService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IReportesService, ReportesService>();
builder.Services.AddScoped<ICatalogosService, CatalogosService>();

// ── Servicios de autenticación ───────────────────────────────────────
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();

// ── Rate limiting ─────────────────────────────────────────────────────
// Las ventanas se particionan POR IP: un cliente abusivo no consume el
// cupo de los demás operadores. (Nota: al desplegar detrás de un proxy
// —Azure Container Apps— habrá que reenviar X-Forwarded-For con
// UseForwardedHeaders para que la IP real llegue hasta aquí.)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login y setup: protección contra fuerza bruta
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Página pública del QR (anónima, códigos enumerables): frena el
    // scraping masivo sin afectar a un consumidor real que escanea
    options.AddPolicy("publico", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        return new ValueTask(context.HttpContext.Response.WriteAsync(
            """{"mensaje":"Demasiados intentos. Espera un minuto e intenta de nuevo."}"""));
    };
});

// ── Controllers + Swagger ────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Permite enviar enums como strings en el JSON ("PAT" en lugar de 0)
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cuy Azuayito API",
        Version = "v1",
        Description = "Sistema de Trazabilidad COOPAGCUY · Proyecto Familias Campesinas Liderando"
    });

    // Soporte para JWT en Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingresa: Bearer {token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference
                { Type = ReferenceType.SecurityScheme, Id = "Bearer" }},
            Array.Empty<string>()
        }
    });
});

// ── CORS (para el frontend React en Static Web Apps) ─────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // En desarrollo Vite salta de puerto (5173 → 5174…) cuando el
            // habitual está ocupado: se acepta cualquier origen de loopback
            // para que el login no falle por CORS tras un cambio de puerto
            policy.SetIsOriginAllowed(origen =>
                      Uri.TryCreate(origen, UriKind.Absolute, out var uri)
                      && uri.IsLoopback)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(
                      builder.Configuration["Cors:AllowedOrigins"]?.Split(',')
                      ?? ["http://localhost:5173"])
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// ── Cabeceras reenviadas por el proxy (Azure Container Apps) ─────────
// El ingress termina el TLS y reenvía al contenedor por HTTP: sin esto,
// el rate limiter por-IP vería la IP del proxy (una sola partición) y
// UseHttpsRedirection creería que la petición llegó sin cifrar. El
// ingress tiene IP dinámica, por eso se confía en sus cabeceras.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Sonda de salud para las probes de Container Apps y el smoke test del CI
builder.Services.AddHealthChecks();

// Evita el error "Cannot write DateTime with Kind=Unspecified"
// Npgsql tratará todos los DateTime sin zona horaria como UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────

// Debe ir PRIMERO: reescribe esquema e IP del cliente a partir de las
// cabeceras del proxy, antes de que cualquier middleware las lea
app.UseForwardedHeaders();

// Red de seguridad para lo no previsto: cualquier excepción que los
// controllers no manejen sale como JSON { mensaje } en lugar de un 500
// vacío. Los choques de unicidad (dos registros simultáneos) se traducen
// a 409 con un mensaje accionable.
app.UseExceptionHandler(manejador => manejador.Run(async context =>
{
    var excepcion = context.Features
        .Get<IExceptionHandlerFeature>()?.Error;

    context.Response.ContentType = "application/json";

    switch (excepcion)
    {
        case EntregaDuplicadaException:
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                mensaje = "Esta entrega ya fue sincronizada anteriormente."
            });
            break;

        case DbUpdateException { InnerException: PostgresException { SqlState: "23505" } }:
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                mensaje = "El registro choca con otro guardado al mismo tiempo. " +
                          "Actualiza la pantalla e intenta de nuevo."
            });
            break;

        default:
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                mensaje = "Ocurrió un error inesperado en el servidor. " +
                          "Intenta de nuevo; si persiste, avisa al administrador."
            });
            break;
    }
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

// Cabeceras de seguridad en toda respuesta del API
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";

    // Swagger UI necesita cargar sus propios scripts y estilos
    if (!context.Request.Path.StartsWithSegments("/swagger"))
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

    await next();
});

app.UseCors("FrontendPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Endpoint anónimo y liviano: no toca la base para no despertar a Neon
// en cada probe. Confirma solo que el proceso responde.
app.MapHealthChecks("/health");

app.Run();

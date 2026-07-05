using CoopagcuyApi.Common.Auth;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NeonDb")));

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
// Protege login y setup contra fuerza bruta: 10 intentos/minuto por IP
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

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

// Evita el error "Cannot write DateTime with Kind=Unspecified"
// Npgsql tratará todos los DateTime sin zona horaria como UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────
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

app.Run();

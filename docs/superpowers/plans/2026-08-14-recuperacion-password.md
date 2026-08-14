# Recuperación de contraseña asistida por administrador — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que un operador que olvidó su contraseña la recupere pidiéndola con su cédula, un administrador se la restablezca desde una bandeja interna con una contraseña temporal dictable por teléfono, y el sistema le obligue a cambiarla al entrar.

**Architecture:** Módulo aislado `Common/Auth/Recuperacion/` en el API (modelo, servicio, controlador, generador) más una tabla nueva con índice único parcial que garantiza una sola solicitud pendiente por usuario. En el front, una pantalla pública de solicitud, una de cambio obligatorio, y una tercera pestaña dentro de `Administración`. Sin colas, sin correo, sin servicios externos.

**Tech Stack:** ASP.NET Core 8, EF Core + Npgsql (PostgreSQL), BCrypt.Net, xUnit + Shouldly + Respawn; React 19 + TypeScript + Vite + TanStack Query + Tailwind.

**Especificación de referencia:** `docs/superpowers/specs/2026-08-14-recuperacion-password-design.md`

## Global Constraints

- **Idioma:** todo comentario, nombre de prueba, mensaje de commit y texto de interfaz va en español, siguiendo el estilo del repositorio.
- **Nunca ejecutar `dotnet test` ni `dotnet ef` directamente en Windows.** Smart App Control bloquea la carga del DLL recién compilado desde OneDrive (error `0x800711C7`). Todo pasa por Docker.
- **Comando único de pruebas del API:** `docker compose -f docker-compose.tests.yml run --rm tests`. No hay forma de correr una sola prueba desde Windows; se ejecuta la batería completa.
- **El SDK 8 no entiende `.slnx`** (`MSB4068`). Todo comando `dotnet` apunta a `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`, que arrastra al API por referencia de proyecto.
- **Prohibido FluentAssertions** (licencia comercial desde la v7). Se usa **Shouldly**.
- **Respawn trunca sin `RESTART IDENTITY`:** ninguna prueba puede asumir `Id == 1`. Siempre capturar el Id devuelto al sembrar.
- **Las cédulas de prueba no se pueden inventar.** `ValidadorCedula` exige provincia 01–24, tercer dígito < 6 y dígito verificador correcto por módulo 10; una cédula inventada hace que el endpoint devuelva 400 y la prueba falle por el motivo equivocado. Cédulas válidas verificadas para este plan: **`0104576277`**, **`0111223343`** y **`0102030400`** (esta última es la que ya usa `Jwt.Emitir` por defecto). Inválida por dígito verificador, para el caso de error: **`0104576270`**.
- **`Npgsql.EnableLegacyTimestampBehavior`** debe seguir activo en `AppDbContextFactory`, o la migración generará un `AlterColumn` masivo de todas las columnas de fecha.
- **Dos repositorios:** las tareas 1–7 son de `C:\Users\nicol\OneDrive\Documents\CoopagcuyApi`; las tareas 8–12 son de `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`. Las rutas de cada tarea son relativas a su repositorio.
- **Commits:** uno por tarea, con el prefijo indicado. **No hacer `push` sin pedírselo al usuario.**
- **El front no tiene runner de pruebas todavía** (Vitest es fase pendiente del plan de testing). Sus tareas se verifican con `pnpm build` —que es `tsc -b && vite build`— y con verificación en navegador. Esto es una limitación real del repositorio, no un descuido del plan.

---

## Estructura de archivos

### API — `CoopagcuyApi`

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `Common/Auth/PoliticaPassword.cs` | **Crear.** Política mínima de contraseñas, hoy duplicada dentro de `UsuarioService` | 1 |
| `Common/Auth/Recuperacion/SolicitudRestablecerPassword.cs` | **Crear.** Modelo y enum de estado | 2 |
| `Infrastructure/Data/AppDbContext.cs` | **Modificar.** `DbSet` y configuración con el índice único parcial | 2 |
| `Common/Auth/Usuario.cs` | **Modificar.** Columna `DebeCambiarPassword` | 2 |
| `Infrastructure/Data/Migrations/*_RecuperacionPassword.cs` | **Generar.** Migración aditiva | 2 |
| `Common/Auth/Recuperacion/GeneradorPasswordTemporal.cs` | **Crear.** Contraseña temporal dictable | 3 |
| `Common/Auth/Recuperacion/RecuperacionDtos.cs` | **Crear.** DTOs del módulo | 4 |
| `Common/Auth/Recuperacion/RecuperacionService.cs` | **Crear.** Toda la lógica del módulo | 4, 5, 6 |
| `Common/Auth/Recuperacion/RecuperacionController.cs` | **Crear.** Endpoints y autorización | 4, 5, 6 |
| `Program.cs` | **Modificar.** Registro del servicio | 4 |
| `Common/Auth/AuthDtos.cs` | **Modificar.** `DebeCambiarPassword` en `LoginResponseDto` | 6 |
| `Common/Auth/SesionService.cs` | **Modificar.** Propagar la bandera al construir la respuesta | 6 |
| `Common/Auth/AuthController.cs` | **Modificar.** Sesiones pasan a `AdminTecnico` | 7 |
| `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs` | **Modificar.** Clientes `ComoAdminTecnico` y `ComoUsuario`, más una IP propia por cliente (ver corrección 2) | 4 |
| `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs` | **Crear.** Alta de usuarios de prueba | 4 |
| `tests/CoopagcuyApi.Tests/Unitarias/GeneradorPasswordTemporalTests.cs` | **Crear.** | 3 |
| `tests/CoopagcuyApi.Tests/Integracion/SolicitudPasswordTests.cs` | **Crear.** Endpoint público | 4 |
| `tests/CoopagcuyApi.Tests/Integracion/ResolucionPasswordTests.cs` | **Crear.** Bandeja del admin | 5 |
| `tests/CoopagcuyApi.Tests/Integracion/CambioPasswordTests.cs` | **Crear.** Cambio obligatorio | 6 |
| `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs` | **Crear.** Matriz de roles | 7 |

### Front — `coopagcuy-frontend`

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `src/types/auth.ts` | **Modificar.** `debeCambiarPassword` en `LoginResponse` | 8 |
| `src/types/recuperacion.ts` | **Crear.** Tipos del módulo | 8 |
| `src/api/recuperacion.ts` | **Crear.** Cliente HTTP | 8 |
| `src/pages/RecuperarPassword.tsx` | **Crear.** Pantalla pública | 9 |
| `src/pages/Login.tsx` | **Modificar.** Enlace "¿Olvidaste tu contraseña?" | 9 |
| `src/App.tsx` | **Modificar.** Rutas nuevas y restricción de `/sesiones` | 9, 10, 12 |
| `src/context/AuthContext.tsx` | **Modificar.** Bandera en el estado de sesión | 10 |
| `src/components/PrivateRoute.tsx` | **Modificar.** Redirección al cambio obligatorio | 10 |
| `src/pages/CambiarPassword.tsx` | **Crear.** Cambio de contraseña | 10 |
| `src/components/admin/TablaUsuarios.tsx` | **Crear.** Extraída de `Administracion.tsx` | 11 |
| `src/components/admin/TablaComunidades.tsx` | **Crear.** Extraída de `Administracion.tsx` | 11 |
| `src/components/admin/SolicitudesPassword.tsx` | **Crear.** Bandeja del administrador | 11 |
| `src/pages/Administracion.tsx` | **Modificar.** Tres pestañas, delegando en los componentes | 11 |
| `src/components/layout/MainLayout.tsx` | **Modificar.** "Sesiones" solo para `AdminTecnico` | 12 |

---

## Tarea 1: Extraer la política de contraseñas

`UsuarioService` guarda la política mínima en un método privado. El módulo de recuperación necesita exactamente la misma regla y el mismo texto de error. Duplicarla garantizaría que dentro de seis meses una de las dos copias se desvíe.

**Files:**
- Create: `Common/Auth/PoliticaPassword.cs`
- Modify: `Common/Auth/UsuarioService.cs:127-137`

**Interfaces:**
- Consumes: nada
- Produces: `PoliticaPassword.EsValida(string?) -> bool`, `PoliticaPassword.Validar(string) -> void` (lanza `InvalidOperationException`), `PoliticaPassword.Requisitos -> const string`

- [ ] **Step 1: Crear la clase de política**

Crear `Common/Auth/PoliticaPassword.cs`:

```csharp
namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Política mínima de contraseñas del sistema: 8 caracteres o más, con al
/// menos una letra y un dígito.
///
/// Vive aparte porque la aplican tres sitios distintos —alta de usuario,
/// edición de usuario y cambio de contraseña tras un restablecimiento— y
/// el texto del error es el mismo en los tres. Con copias separadas, subir
/// el mínimo a 10 caracteres exigiría acordarse de los tres.
/// </summary>
public static class PoliticaPassword
{
    public const string Requisitos =
        "La contraseña debe tener al menos 8 caracteres, " +
        "incluyendo una letra y un número.";

    public static bool EsValida(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= 8
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    public static void Validar(string password)
    {
        if (!EsValida(password))
            throw new InvalidOperationException(Requisitos);
    }
}
```

- [ ] **Step 2: Hacer que `UsuarioService` delegue**

En `Common/Auth/UsuarioService.cs`, sustituir el método privado `ValidarPassword` (líneas 126-137, incluido su comentario) por:

```csharp
    // Política mínima de contraseñas: 8+ caracteres, al menos una letra y un
    // número. La regla vive en PoliticaPassword porque la comparte el módulo
    // de recuperación de contraseña.
    private static void ValidarPassword(string password) =>
        PoliticaPassword.Validar(password);
```

No hay que tocar las dos llamadas existentes (`CrearAsync` y `ActualizarAsync`): conservan la misma firma y el mismo comportamiento.

- [ ] **Step 3: Verificar que compila y que nada se rompió**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: compila sin errores y las 12 pruebas existentes siguen en verde (`Passed!  - Failed: 0`).

- [ ] **Step 4: Commit**

```bash
git add Common/Auth/PoliticaPassword.cs Common/Auth/UsuarioService.cs
git commit -m "refactor: extraer la política de contraseñas a una clase compartida"
```

---

## Tarea 2: Modelo, esquema y migración

**Files:**
- Create: `Common/Auth/Recuperacion/SolicitudRestablecerPassword.cs`
- Modify: `Common/Auth/Usuario.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Create: `Infrastructure/Data/Migrations/*_RecuperacionPassword.cs` (generada)
- Test: `tests/CoopagcuyApi.Tests/Integracion/EsquemaRecuperacionTests.cs`

**Interfaces:**
- Consumes: nada
- Produces: `SolicitudRestablecerPassword` (propiedades `Id`, `UsuarioId`, `Usuario`, `CedulaSolicitada`, `Estado`, `FechaCreacion`, `FechaResolucion`, `ResueltaPor`, `IpSolicitud`); `EstadoSolicitudPassword` (`Pendiente`, `Resuelta`, `Descartada`); `Usuario.DebeCambiarPassword -> bool`; `AppDbContext.SolicitudesRestablecerPassword -> DbSet<SolicitudRestablecerPassword>`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/EsquemaRecuperacionTests.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El índice único parcial es lo que impide que un operador nervioso llene
/// la bandeja del administrador pulsando el botón cinco veces. Se verifica
/// contra Postgres real porque un índice filtrado no existe en memoria: es
/// justo el tipo de garantía que un doble de prueba no reproduce.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class EsquemaRecuperacionTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DosSolicitudesPendientes_delMismoUsuario_chocanConElIndice()
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = "Operadora de prueba",
            Cedula = "0102030405",
            PasswordHash = "hash-irrelevante",
            Rol = RolUsuario.OperadorCAT,
            CatAsignado = CentroAcopio.PAT
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula
        });
        await db.SaveChangesAsync();

        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UnaPendienteYUnaResuelta_delMismoUsuario_conviven()
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = "Operadora de prueba",
            Cedula = "0102030405",
            PasswordHash = "hash-irrelevante",
            Rol = RolUsuario.OperadorCAT,
            CatAsignado = CentroAcopio.PAT
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        // El historial no estorba: el filtro del índice solo mira las pendientes
        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula,
            Estado = EstadoSolicitudPassword.Resuelta,
            FechaResolucion = DateTime.UtcNow,
            ResueltaPor = "0102030499"
        });
        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = usuario.Cedula
        });

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UsuarioNuevo_naceSinObligacionDeCambiarPassword()
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = "Operadora de prueba",
            Cedula = "0102030405",
            PasswordHash = "hash-irrelevante",
            Rol = RolUsuario.OperadorCAT,
            CatAsignado = CentroAcopio.PAT
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        var guardado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);

        guardado.DebeCambiarPassword.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA en compilación con `CS0246` — no existen `SolicitudRestablecerPassword`, `EstadoSolicitudPassword` ni la propiedad `DebeCambiarPassword`.

- [ ] **Step 3: Crear el modelo**

Crear `Common/Auth/Recuperacion/SolicitudRestablecerPassword.cs`:

```csharp
namespace CoopagcuyApi.Common.Auth.Recuperacion;

public enum EstadoSolicitudPassword
{
    Pendiente,   // esperando que un administrador la atienda
    Resuelta,    // se asignó una contraseña temporal
    Descartada   // el administrador decidió no atenderla
}

/// <summary>
/// Petición de un usuario que olvidó su contraseña. Los operadores entran
/// solo con cédula y el correo es opcional, así que no hay dónde enviar un
/// enlace de un solo uso: la solicitud queda aquí, un administrador la ve en
/// su bandeja y entrega una contraseña temporal por teléfono.
///
/// Es la misma forma que <c>EntregaPendienteVinculacion</c>: una cola de
/// trabajo persistente revisada por un humano. Persistente y no en memoria a
/// propósito — el Container App escala a cero y una cola en memoria se
/// perdería con la última réplica.
/// </summary>
public class SolicitudRestablecerPassword
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    // Se copia al crear: la auditoría sobrevive aunque el usuario cambie
    public string CedulaSolicitada { get; set; } = string.Empty;

    public EstadoSolicitudPassword Estado { get; set; }
        = EstadoSolicitudPassword.Pendiente;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaResolucion { get; set; }

    // Cédula del administrador que resolvió o descartó
    public string? ResueltaPor { get; set; }

    // IP real del solicitante (ya reescrita por UseForwardedHeaders)
    public string? IpSolicitud { get; set; }
}
```

- [ ] **Step 4: Añadir la columna al usuario**

En `Common/Auth/Usuario.cs`, añadir antes de `public bool Activo`:

```csharp
    // Se activa al restablecerle la contraseña: el front lo obliga a poner
    // una propia antes de dejarle usar el resto de la aplicación, para que
    // el administrador no quede conociendo su contraseña definitiva.
    public bool DebeCambiarPassword { get; set; }
```

- [ ] **Step 5: Registrar el DbSet y su configuración**

En `Infrastructure/Data/AppDbContext.cs`, añadir el `using` junto a los demás:

```csharp
using CoopagcuyApi.Common.Auth.Recuperacion;
```

Añadir el `DbSet` después de `EntregasPendientesVinculacion`:

```csharp
    public DbSet<SolicitudRestablecerPassword> SolicitudesRestablecerPassword =>
        Set<SolicitudRestablecerPassword>();
```

Añadir la configuración dentro de `OnModelCreating`, justo después del bloque `modelBuilder.Entity<EntregaPendienteVinculacion>(…)`:

```csharp
        // Solicitud de restablecimiento de contraseña: bandeja que atiende un
        // administrador. El índice único PARCIAL es la pieza importante —
        // garantiza en la base, no en código, que un usuario no acumule
        // solicitudes pendientes. El historial (Resuelta/Descartada) queda
        // fuera del filtro, así que el mismo usuario puede pedirlo otra vez
        // dentro de un mes sin chocar con su propia solicitud vieja.
        modelBuilder.Entity<SolicitudRestablecerPassword>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.CedulaSolicitada).HasMaxLength(10).IsRequired();
            e.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.ResueltaPor).HasMaxLength(10);
            e.Property(s => s.IpSolicitud).HasMaxLength(60);

            e.HasOne(s => s.Usuario)
                .WithMany()
                .HasForeignKey(s => s.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            // El Estado se persiste como texto (HasConversion<string>), por eso
            // el filtro compara contra 'Pendiente' y no contra un entero.
            e.HasIndex(s => s.UsuarioId)
                .IsUnique()
                .HasFilter("\"Estado\" = 'Pendiente'")
                .HasDatabaseName("IX_SolicitudesRestablecerPassword_Pendiente");
        });
```

- [ ] **Step 6: Generar la migración dentro de Docker**

Desde Git Bash, en la raíz del repositorio del API:

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=placeholder;Username=postgres;Password=postgres" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet tool install --global dotnet-ef --version 8.* && export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add RecuperacionPassword --project CoopagcuyApi.csproj"
```

La cadena de conexión es un marcador: `migrations add` no se conecta a la base, pero `AppDbContextFactory` lanza una excepción si la variable no existe.

Expected: `Done. To undo this action, use 'ef migrations remove'` y dos archivos nuevos en `Infrastructure/Data/Migrations/`.

- [ ] **Step 7: Revisar que la migración sea aditiva**

Abrir el archivo `Infrastructure/Data/Migrations/*_RecuperacionPassword.cs` y confirmar que `Up()` contiene **solo**:
- `CreateTable("SolicitudesRestablecerPassword", …)`
- `AddColumn<bool>("DebeCambiarPassword", "Usuarios", …)`
- `CreateIndex(… filter: "\"Estado\" = 'Pendiente'" …)`

**Si aparece cualquier `AlterColumn` sobre columnas de fecha**, la migración se generó sin `Npgsql.EnableLegacyTimestampBehavior`: borrar los dos archivos generados, verificar `AppDbContextFactory.cs` y repetir el paso 6.

- [ ] **Step 8: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 3 pruebas nuevas de `EsquemaRecuperacionTests` pasan y las 12 anteriores siguen en verde.

- [ ] **Step 9: Commit**

```bash
git add Common/Auth/Recuperacion/ Common/Auth/Usuario.cs Infrastructure/Data/ tests/CoopagcuyApi.Tests/Integracion/EsquemaRecuperacionTests.cs
git commit -m "feat: modelar la solicitud de restablecimiento de contraseña"
```

---

## Tarea 3: Generador de contraseñas temporales

**Files:**
- Create: `Common/Auth/Recuperacion/GeneradorPasswordTemporal.cs`
- Test: `tests/CoopagcuyApi.Tests/Unitarias/GeneradorPasswordTemporalTests.cs`

**Interfaces:**
- Consumes: `PoliticaPassword.EsValida(string?) -> bool` (Tarea 1)
- Produces: `GeneradorPasswordTemporal.Generar() -> string`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Unitarias/GeneradorPasswordTemporalTests.cs`:

```csharp
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// La contraseña temporal se dicta por teléfono a operadores en campo. Tiene
/// que cumplir la política del sistema Y ser pronunciable; si falla lo
/// primero, el usuario no puede entrar, y si falla lo segundo, no llega a
/// escribirla bien nunca.
/// </summary>
public class GeneradorPasswordTemporalTests
{
    [Fact]
    public void Generar_siempreCumpleLaPoliticaDeContrasenas()
    {
        for (var i = 0; i < 500; i++)
            PoliticaPassword.EsValida(GeneradorPasswordTemporal.Generar())
                .ShouldBeTrue();
    }

    [Fact]
    public void Generar_produceValoresDistintos()
    {
        var generadas = Enumerable.Range(0, 200)
            .Select(_ => GeneradorPasswordTemporal.Generar())
            .ToHashSet();

        // Con 14 palabras y 90 000 números, 200 tiradas repetidas serían un
        // generador roto, no mala suerte
        generadas.Count.ShouldBeGreaterThan(190);
    }

    [Fact]
    public void Generar_usaSoloLetrasMinusculasDigitosYUnGuion()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = GeneradorPasswordTemporal.Generar();

            // Mayúsculas y símbolos no sobreviven a un dictado por teléfono
            password.ShouldAllBe(c => char.IsAsciiLetterLower(c)
                                   || char.IsAsciiDigit(c)
                                   || c == '-');
        }
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA en compilación con `CS0103`/`CS0246` — `GeneradorPasswordTemporal` no existe.

- [ ] **Step 3: Escribir el generador**

Crear `Common/Auth/Recuperacion/GeneradorPasswordTemporal.cs`:

```csharp
using System.Security.Cryptography;

namespace CoopagcuyApi.Common.Auth.Recuperacion;

/// <summary>
/// Genera la contraseña temporal que el administrador dicta al operador por
/// teléfono. El formato —palabra corta + guion + cinco dígitos— está elegido
/// para DICTARSE, que es el requisito real de este sistema: una cadena como
/// "xK7mQ2vP" es más fuerte sobre el papel e inservible cuando hay que
/// deletreársela a alguien en el campo con mala cobertura.
///
/// La entropía es baja a propósito y se compensa por tres vías: la temporal
/// vive minutos, /api/auth/login limita a 10 intentos por minuto y por IP, y
/// queda inutilizada en cuanto el operador la cambia (DebeCambiarPassword).
/// </summary>
public static class GeneradorPasswordTemporal
{
    // Palabras del entorno de trabajo: fáciles de decir y de recordar el
    // tiempo que tarda el operador en teclearlas. Sin tildes ni "ñ": el
    // teclado de la tablet las esconde detrás de una pulsación larga.
    private static readonly string[] Palabras =
    [
        "cuy", "andes", "sierra", "campo", "valle", "monte", "rio",
        "sol", "trigo", "maiz", "cedro", "pino", "nube", "paramo"
    ];

    public static string Generar()
    {
        var palabra = Palabras[RandomNumberGenerator.GetInt32(Palabras.Length)];

        // El rango arranca en 10 000 para que siempre salgan cinco dígitos:
        // un cero a la izquierda se pierde al dictarlo
        var numero = RandomNumberGenerator.GetInt32(10_000, 100_000);

        return $"{palabra}-{numero}";
    }
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 3 pruebas de `GeneradorPasswordTemporalTests` pasan.

- [ ] **Step 5: Commit**

```bash
git add Common/Auth/Recuperacion/GeneradorPasswordTemporal.cs tests/CoopagcuyApi.Tests/Unitarias/GeneradorPasswordTemporalTests.cs
git commit -m "feat: generar contraseñas temporales dictables por teléfono"
```

---

## Tarea 4: Endpoint público de solicitud

**Files:**
- Create: `Common/Auth/Recuperacion/RecuperacionDtos.cs`
- Create: `Common/Auth/Recuperacion/RecuperacionService.cs`
- Create: `Common/Auth/Recuperacion/RecuperacionController.cs`
- Modify: `Program.cs`
- Create: `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`
- Modify: `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/SolicitudPasswordTests.cs`

**Interfaces:**
- Consumes: `SolicitudRestablecerPassword`, `EstadoSolicitudPassword` (Tarea 2)
- Produces: `IRecuperacionService.SolicitarAsync(string cedula, string? ip) -> Task`; `SolicitarRecuperacionDto(string Cedula)`; `Sembrador.UsuarioAsync(...) -> Task<Usuario>`; `ApiFactory.ComoAdminTecnico() -> HttpClient`; `ApiFactory.ComoUsuario(string rol, string cedula) -> HttpClient`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Alta de usuarios para las pruebas. Devuelve la entidad ya guardada porque
/// Respawn trunca SIN RESTART IDENTITY: ninguna prueba puede asumir que el
/// primer usuario sembrado tenga Id 1.
/// </summary>
public static class Sembrador
{
    public const string PasswordPorDefecto = "clave1234";

    public static async Task<Usuario> UsuarioAsync(
        ApiFactory api,
        string cedula,
        RolUsuario rol = RolUsuario.OperadorCAT,
        CentroAcopio? cat = CentroAcopio.PAT,
        bool activo = true,
        string password = PasswordPorDefecto)
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = $"Usuario {cedula}",
            Cedula = cedula,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = rol,
            CatAsignado = rol == RolUsuario.OperadorCAT ? cat : null,
            Activo = activo
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }
}
```

Añadir a `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`, junto a los demás métodos de cliente:

```csharp
    public HttpClient ComoAdminTecnico() => ClienteCon(Jwt.Emitir("AdminTecnico"));

    /// Cliente con la cédula que se le indique: los endpoints que actúan
    /// sobre "el usuario del token" la leen del claim "cedula".
    public HttpClient ComoUsuario(string rol, string cedula) =>
        ClienteCon(Jwt.Emitir(rol, cat: null, cedula: cedula));
```

Crear `tests/CoopagcuyApi.Tests/Integracion/SolicitudPasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El endpoint de solicitud es anónimo y público. Su invariante principal no
/// es funcional sino de privacidad: desde fuera debe ser imposible distinguir
/// una cédula con cuenta de una sin ella.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class SolicitudPasswordTests(ApiFactory api) : IAsyncLifetime
{
    // Cédulas ecuatorianas VÁLIDAS: provincia 01, tercer dígito < 6 y dígito
    // verificador correcto por módulo 10. No sirve inventarlas — ValidadorCedula
    // rechaza cualquier cosa que no cuadre y el endpoint devolvería 400.
    private const string CedulaConCuenta = "0104576277";
    private const string CedulaSinCuenta = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> Solicitar(HttpClient cliente, string cedula) =>
        cliente.PostAsJsonAsync("/api/auth/recuperacion", new { cedula });

    [Fact]
    public async Task CedulaConCuenta_yCedulaSin_devuelvenLaMismaRespuesta()
    {
        await Sembrador.UsuarioAsync(api, CedulaConCuenta);
        var cliente = api.ComoAnonimo();

        var conCuenta = await Solicitar(cliente, CedulaConCuenta);
        var sinCuenta = await Solicitar(cliente, CedulaSinCuenta);

        conCuenta.StatusCode.ShouldBe(HttpStatusCode.OK);
        sinCuenta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpoConCuenta = await conCuenta.Content.ReadAsStringAsync();
        var cuerpoSinCuenta = await sinCuenta.Content.ReadAsStringAsync();
        cuerpoConCuenta.ShouldBe(cuerpoSinCuenta);
    }

    [Fact]
    public async Task CedulaSinCuenta_noDejaRastroEnLaTabla()
    {
        await Solicitar(api.ComoAnonimo(), CedulaSinCuenta);

        await using var db = api.NuevoDbContext();
        (await db.SolicitudesRestablecerPassword.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CedulaConCuenta_creaUnaSolicitudPendiente()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaConCuenta);

        await Solicitar(api.ComoAnonimo(), CedulaConCuenta);

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.SingleAsync();
        solicitud.UsuarioId.ShouldBe(usuario.Id);
        solicitud.CedulaSolicitada.ShouldBe(CedulaConCuenta);
        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Pendiente);
        solicitud.FechaResolucion.ShouldBeNull();
    }

    [Fact]
    public async Task TresSolicitudesSeguidas_dejanUnaSolaFila()
    {
        await Sembrador.UsuarioAsync(api, CedulaConCuenta);
        var cliente = api.ComoAnonimo();

        // El operador nervioso que pulsa el botón varias veces no debe
        // multiplicar el trabajo del administrador
        for (var i = 0; i < 3; i++)
        {
            var respuesta = await Solicitar(cliente, CedulaConCuenta);
            respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using var db = api.NuevoDbContext();
        (await db.SolicitudesRestablecerPassword.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CedulaConDigitoVerificadorMalo_devuelve400()
    {
        // Mismo número que CedulaConCuenta con el último dígito cambiado: es
        // el error de tipeo típico que el dígito verificador existe para atrapar
        var respuesta = await Solicitar(api.ComoAnonimo(), "0104576270");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UsuarioDesactivado_noGeneraSolicitud()
    {
        await Sembrador.UsuarioAsync(api, CedulaConCuenta, activo: false);

        var respuesta = await Solicitar(api.ComoAnonimo(), CedulaConCuenta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = api.NuevoDbContext();
        (await db.SolicitudesRestablecerPassword.CountAsync()).ShouldBe(0);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA — los endpoints no existen todavía (404 en lugar de 200/400).

- [ ] **Step 3: Crear los DTOs**

Crear `Common/Auth/Recuperacion/RecuperacionDtos.cs`:

```csharp
namespace CoopagcuyApi.Common.Auth.Recuperacion;

public record SolicitarRecuperacionDto(string Cedula);

// Fila de la bandeja del administrador. Nunca lleva hash ni contraseña.
public record SolicitudPasswordDto(
    int Id,
    int UsuarioId,
    string NombreCompleto,
    string Cedula,
    string Rol,
    string? CatAsignado,
    // El usuario pudo desactivarse tras solicitar: la bandeja lo muestra
    // para que el administrador no intente restablecer en vano
    bool UsuarioActivo,
    string Estado,
    DateTime FechaCreacion,
    DateTime? FechaResolucion,
    string? ResueltaPor
);

// La contraseña temporal viaja UNA sola vez, en la respuesta de resolver.
// No se guarda en claro ni se puede volver a consultar.
public record PasswordTemporalDto(
    string PasswordTemporal,
    string NombreCompleto,
    string Cedula
);

public record CambiarPasswordDto(string PasswordActual, string PasswordNueva);
```

- [ ] **Step 4: Crear el servicio con la operación de solicitud**

Crear `Common/Auth/Recuperacion/RecuperacionService.cs`:

```csharp
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoopagcuyApi.Common.Auth.Recuperacion;

public interface IRecuperacionService
{
    Task SolicitarAsync(string cedula, string? ip);
}

public class RecuperacionService(AppDbContext db) : IRecuperacionService
{
    public async Task SolicitarAsync(string cedula, string? ip)
    {
        var cedulaNormalizada = cedula.Trim();

        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Cedula == cedulaNormalizada && u.Activo);

        // Cédula que no corresponde a ningún usuario activo: no se registra
        // nada. El endpoint es público, y persistir aquí permitiría inflar la
        // tabla desde internet. El controlador responde igual en ambos casos.
        if (usuario is null) return;

        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = cedulaNormalizada,
            IpSolicitud = Recortar(ip, 60)
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Ya tenía una pendiente (índice único parcial). Se descarta el
            // cambio y se responde igual: desde fuera, "ya tenías una
            // solicitud" y "acabo de crearla" deben ser indistinguibles.
            db.ChangeTracker.Clear();
        }
    }

    private static string? Recortar(string? valor, int max) =>
        string.IsNullOrWhiteSpace(valor) ? null
            : valor.Length <= max ? valor : valor[..max];
}
```

- [ ] **Step 5: Crear el controlador**

Crear `Common/Auth/Recuperacion/RecuperacionController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoopagcuyApi.Common.Auth.Recuperacion;

/// <summary>
/// Recuperación de contraseña asistida por un administrador.
///
/// La ruta se declara explícitamente en vez de usar el "api/[controller]"
/// del resto del proyecto: la convención por nombre daría "api/recuperacion",
/// fuera del prefijo /api/auth al que está limitada la cookie del refresh
/// token, y agrupar ahí los endpoints de autenticación mantiene coherente el
/// modelo mental del módulo.
/// </summary>
[ApiController]
[Route("api/auth/recuperacion")]
public class RecuperacionController(IRecuperacionService servicio) : ControllerBase
{
    private const string MensajeGenerico =
        "Tu solicitud fue enviada al administrador. " +
        "Te contactará para darte una contraseña nueva.";

    /// <summary>
    /// Registra la solicitud de un usuario que olvidó su contraseña.
    /// Anónimo y con el mismo rate limiter que el login.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarRecuperacionDto dto)
    {
        if (!ValidadorCedula.EsValida(dto.Cedula))
            return BadRequest(new
            {
                mensaje = "El número de cédula ingresado no es válido."
            });

        await servicio.SolicitarAsync(dto.Cedula, IpCliente());

        // Misma respuesta exista o no el usuario: el endpoint es público y no
        // debe revelar qué cédulas tienen cuenta en el sistema.
        return Ok(new { mensaje = MensajeGenerico });
    }

    // IP real del cliente (ya reescrita por UseForwardedHeaders tras el proxy)
    private string? IpCliente() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
```

- [ ] **Step 6: Registrar el servicio**

En `Program.cs`, en el bloque "Servicios de autenticación" (después de `AddScoped<IUsuarioService, UsuarioService>()`), añadir:

```csharp
builder.Services.AddScoped<IRecuperacionService, RecuperacionService>();
```

Y el `using` junto a los demás del encabezado:

```csharp
using CoopagcuyApi.Common.Auth.Recuperacion;
```

- [ ] **Step 7: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 6 pruebas de `SolicitudPasswordTests` pasan y todo lo anterior sigue en verde.

- [ ] **Step 8: Commit**

```bash
git add Common/Auth/Recuperacion/ Program.cs tests/CoopagcuyApi.Tests/
git commit -m "feat: registrar solicitudes de recuperación sin revelar qué cédulas existen"
```

---

## Tarea 5: Bandeja del administrador — listar, resolver y descartar

**Files:**
- Modify: `Common/Auth/Recuperacion/RecuperacionService.cs`
- Modify: `Common/Auth/Recuperacion/RecuperacionController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/ResolucionPasswordTests.cs`

**Interfaces:**
- Consumes: `IRecuperacionService.SolicitarAsync` (Tarea 4), `GeneradorPasswordTemporal.Generar()` (Tarea 3), `ISesionService.RevocarUsuarioAsync(int) -> Task<int>` (ya existe)
- Produces: `IRecuperacionService.ListarAsync(bool incluirResueltas) -> Task<List<SolicitudPasswordDto>>`; `IRecuperacionService.ResolverAsync(int id, string cedulaAdmin) -> Task<PasswordTemporalDto>`; `IRecuperacionService.DescartarAsync(int id, string cedulaAdmin) -> Task<bool>`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/ResolucionPasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Resolver una solicitud es la operación con más efectos a la vez: cambia la
/// contraseña, marca la obligación de cambiarla y revoca las sesiones. La
/// revocación es la que se olvida al implementar y la que más importa: si la
/// solicitud vino porque alguien tomó la tablet, restablecer sin revocar deja
/// al intruso dentro con su sesión de 7 días intacta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ResolucionPasswordTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaOperadora = "0104576277";
    private const string CedulaAdmin = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// Siembra un usuario, le crea una solicitud pendiente y devuelve ambos ids
    private async Task<(int UsuarioId, int SolicitudId)> ConSolicitudPendienteAsync(
        bool activo = true)
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora, activo: activo);

        await using var db = api.NuevoDbContext();
        var solicitud = new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = CedulaOperadora
        };
        db.SolicitudesRestablecerPassword.Add(solicitud);
        await db.SaveChangesAsync();

        return (usuario.Id, solicitud.Id);
    }

    [Fact]
    public async Task Listar_devuelveSoloLasPendientes()
    {
        var (usuarioId, _) = await ConSolicitudPendienteAsync();

        await using (var db = api.NuevoDbContext())
        {
            db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
            {
                UsuarioId = usuarioId,
                CedulaSolicitada = CedulaOperadora,
                Estado = EstadoSolicitudPassword.Descartada,
                FechaResolucion = DateTime.UtcNow,
                ResueltaPor = CedulaAdmin
            });
            await db.SaveChangesAsync();
        }

        var pendientes = await api.ComoAdmin()
            .GetFromJsonAsync<List<SolicitudPasswordDto>>("/api/auth/recuperacion");
        var todas = await api.ComoAdmin()
            .GetFromJsonAsync<List<SolicitudPasswordDto>>(
                "/api/auth/recuperacion?incluirResueltas=true");

        pendientes!.Count.ShouldBe(1);
        pendientes[0].Estado.ShouldBe("Pendiente");
        todas!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Resolver_devuelveTemporalValida_yMarcaLaObligacionDeCambiarla()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync();

        var respuesta = await api.ComoAdmin()
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var temporal = await respuesta.Content.ReadFromJsonAsync<PasswordTemporalDto>();
        PoliticaPassword.EsValida(temporal!.PasswordTemporal).ShouldBeTrue();

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId);
        usuario.DebeCambiarPassword.ShouldBeTrue();
        // La temporal devuelta es la que quedó guardada, hasheada
        BCrypt.Net.BCrypt.Verify(temporal.PasswordTemporal, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Resolver_cierraLaSolicitud_conFechaYAutor()
    {
        var (_, solicitudId) = await ConSolicitudPendienteAsync();

        await api.ComoUsuario("AdminCooperativa", CedulaAdmin)
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.AsNoTracking()
            .FirstAsync(s => s.Id == solicitudId);

        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Resuelta);
        solicitud.FechaResolucion.ShouldNotBeNull();
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);
    }

    [Fact]
    public async Task Resolver_revocaLasSesionesActivasDelUsuario()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync();

        await using (var db = api.NuevoDbContext())
        {
            var ahora = DateTime.UtcNow;
            db.RefreshTokens.Add(new RefreshToken
            {
                UsuarioId = usuarioId,
                TokenHash = "hash-de-una-sesion-abierta",
                FechaCreacion = ahora,
                FechaUltimoUso = ahora,
                FechaExpiracion = ahora.AddDays(7)
            });
            await db.SaveChangesAsync();
        }

        await api.ComoAdmin()
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        await using var verificacion = api.NuevoDbContext();
        var vivas = await verificacion.RefreshTokens
            .CountAsync(t => t.UsuarioId == usuarioId && !t.Revocado);
        vivas.ShouldBe(0);
    }

    [Fact]
    public async Task ResolverDosVeces_laSegundaDevuelve409()
    {
        var (_, solicitudId) = await ConSolicitudPendienteAsync();
        var cliente = api.ComoAdmin();

        var primera = await cliente.PostAsync(
            $"/api/auth/recuperacion/{solicitudId}/resolver", null);
        var segunda = await cliente.PostAsync(
            $"/api/auth/recuperacion/{solicitudId}/resolver", null);

        primera.StatusCode.ShouldBe(HttpStatusCode.OK);
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Resolver_deUsuarioDesactivado_devuelve409_ySinCambiarSuPassword()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync(activo: false);

        var respuesta = await api.ComoAdmin()
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/resolver", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId);
        usuario.DebeCambiarPassword.ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Resolver_solicitudInexistente_devuelve404()
    {
        var respuesta = await api.ComoAdmin()
            .PostAsync("/api/auth/recuperacion/999999/resolver", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Descartar_dejaConstancia_sinTocarLaPassword()
    {
        var (usuarioId, solicitudId) = await ConSolicitudPendienteAsync();

        var respuesta = await api.ComoUsuario("AdminTecnico", CedulaAdmin)
            .PostAsync($"/api/auth/recuperacion/{solicitudId}/descartar", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.AsNoTracking()
            .FirstAsync(s => s.Id == solicitudId);
        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Descartada);
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);

        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId);
        usuario.DebeCambiarPassword.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA — los endpoints de bandeja no existen (404 donde se esperan 200/409/204).

- [ ] **Step 3: Ampliar el servicio**

En `Common/Auth/Recuperacion/RecuperacionService.cs`, añadir los tres métodos a la interfaz:

```csharp
public interface IRecuperacionService
{
    Task SolicitarAsync(string cedula, string? ip);
    Task<List<SolicitudPasswordDto>> ListarAsync(bool incluirResueltas);
    Task<PasswordTemporalDto> ResolverAsync(int id, string cedulaAdmin);
    Task<bool> DescartarAsync(int id, string cedulaAdmin);
}
```

Cambiar la declaración de la clase para recibir el servicio de sesiones:

```csharp
public class RecuperacionService(
    AppDbContext db,
    ISesionService sesionService) : IRecuperacionService
```

Y añadir los tres métodos dentro de la clase, antes del helper `Recortar`:

```csharp
    public async Task<List<SolicitudPasswordDto>> ListarAsync(bool incluirResueltas)
    {
        var query = db.SolicitudesRestablecerPassword.AsNoTracking();

        if (!incluirResueltas)
            query = query.Where(s => s.Estado == EstadoSolicitudPassword.Pendiente);

        return await query
            .OrderByDescending(s => s.FechaCreacion)
            // Mismo tope que el resto de listados del sistema: el historial
            // completo no cabe en una pantalla ni hace falta en la bandeja
            .Take(300)
            .Select(s => new SolicitudPasswordDto(
                s.Id,
                s.UsuarioId,
                s.Usuario.NombreCompleto,
                s.Usuario.Cedula,
                s.Usuario.Rol.ToString(),
                s.Usuario.CatAsignado == null ? null : s.Usuario.CatAsignado.ToString(),
                s.Usuario.Activo,
                s.Estado.ToString(),
                s.FechaCreacion,
                s.FechaResolucion,
                s.ResueltaPor))
            .ToListAsync();
    }

    public async Task<PasswordTemporalDto> ResolverAsync(int id, string cedulaAdmin)
    {
        // La conexión a Neon reintenta (EnableRetryOnFailure), y una
        // transacción explícita solo es compatible con eso dentro de una
        // execution strategy. El ChangeTracker se limpia al entrar porque en
        // un reintento las entidades cargadas antes quedarían obsoletas.
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync();

            var solicitud = await db.SolicitudesRestablecerPassword
                .Include(s => s.Usuario)
                .FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException("La solicitud no existe.");

            if (solicitud.Estado != EstadoSolicitudPassword.Pendiente)
                throw new InvalidOperationException(
                    "Otro administrador ya atendió esta solicitud.");

            // El usuario pudo desactivarse entre la solicitud y su resolución:
            // restablecerle la contraseña sería devolverle el acceso a alguien
            // que la cooperativa acaba de apartar.
            if (!solicitud.Usuario.Activo)
                throw new InvalidOperationException(
                    "El usuario está desactivado. " +
                    "Reactívalo antes de restablecer su contraseña.");

            var temporal = GeneradorPasswordTemporal.Generar();

            solicitud.Usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporal);
            solicitud.Usuario.DebeCambiarPassword = true;

            solicitud.Estado = EstadoSolicitudPassword.Resuelta;
            solicitud.FechaResolucion = DateTime.UtcNow;
            solicitud.ResueltaPor = cedulaAdmin;

            await db.SaveChangesAsync();

            // Imprescindible: si la solicitud vino porque alguien tomó la
            // tablet del operador, dejarle la sesión de 7 días viva anularía
            // el restablecimiento.
            await sesionService.RevocarUsuarioAsync(solicitud.UsuarioId);

            await transaccion.CommitAsync();

            return new PasswordTemporalDto(
                temporal,
                solicitud.Usuario.NombreCompleto,
                solicitud.Usuario.Cedula);
        });
    }

    public async Task<bool> DescartarAsync(int id, string cedulaAdmin)
    {
        var solicitud = await db.SolicitudesRestablecerPassword.FindAsync(id);
        if (solicitud is null) return false;

        if (solicitud.Estado != EstadoSolicitudPassword.Pendiente)
            throw new InvalidOperationException(
                "Otro administrador ya atendió esta solicitud.");

        solicitud.Estado = EstadoSolicitudPassword.Descartada;
        solicitud.FechaResolucion = DateTime.UtcNow;
        solicitud.ResueltaPor = cedulaAdmin;
        await db.SaveChangesAsync();
        return true;
    }
```

- [ ] **Step 4: Ampliar el controlador**

En `Common/Auth/Recuperacion/RecuperacionController.cs`, añadir el `using`:

```csharp
using System.Security.Claims;
```

Añadir la constante de roles junto a `MensajeGenerico`:

```csharp
    // Ambos administradores. Reservarlo al técnico crearía un bloqueo: si él
    // olvidara su contraseña, nadie podría restablecérsela.
    private const string RolesAdmin = "AdminCooperativa,AdminTecnico";
```

Y los tres endpoints, antes del helper `IpCliente`:

```csharp
    /// <summary>
    /// Bandeja de solicitudes. Por defecto solo las pendientes; con
    /// ?incluirResueltas=true devuelve también el historial auditable.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> Listar([FromQuery] bool incluirResueltas = false)
        => Ok(await servicio.ListarAsync(incluirResueltas));

    /// <summary>
    /// Asigna una contraseña temporal, obliga al usuario a cambiarla y revoca
    /// sus sesiones. La temporal viaja en la respuesta y no se puede volver a
    /// consultar: el administrador la dicta y el sistema la olvida.
    /// </summary>
    [HttpPost("{id:int}/resolver")]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> Resolver(int id)
    {
        try
        {
            return Ok(await servicio.ResolverAsync(id, CedulaAdmin()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Cierra una solicitud que el administrador decide no atender, dejando
    /// constancia en lugar de borrar la fila.
    /// </summary>
    [HttpPost("{id:int}/descartar")]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> Descartar(int id)
    {
        try
        {
            return await servicio.DescartarAsync(id, CedulaAdmin())
                ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    // Cédula del administrador autenticado, para la auditoría de la solicitud
    private string CedulaAdmin() => User.FindFirstValue("cedula") ?? "desconocido";
```

- [ ] **Step 5: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 8 pruebas de `ResolucionPasswordTests` pasan y todo lo anterior sigue en verde.

- [ ] **Step 6: Commit**

```bash
git add Common/Auth/Recuperacion/ tests/CoopagcuyApi.Tests/Integracion/ResolucionPasswordTests.cs
git commit -m "feat: resolver y descartar solicitudes desde la bandeja del administrador"
```

---

## Tarea 6: Cambio obligatorio de contraseña

**Files:**
- Modify: `Common/Auth/AuthDtos.cs`
- Modify: `Common/Auth/SesionService.cs:181-188`
- Modify: `Common/Auth/Recuperacion/RecuperacionService.cs`
- Modify: `Common/Auth/Recuperacion/RecuperacionController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/CambioPasswordTests.cs`

**Interfaces:**
- Consumes: `PoliticaPassword.Validar(string)` (Tarea 1), `Usuario.DebeCambiarPassword` (Tarea 2)
- Produces: `LoginResponseDto.DebeCambiarPassword -> bool` (octavo parámetro posicional); `IRecuperacionService.CambiarPasswordAsync(string cedula, string passwordActual, string passwordNueva) -> Task<bool>`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/CambioPasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El circuito se cierra aquí: la bandera que pone el restablecimiento tiene
/// que llegar al front en la respuesta del login, y tiene que bajarse al
/// cambiar la contraseña. Si se queda activa, el operador entra en un bucle
/// del que no puede salir.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CambioPasswordTests(ApiFactory api) : IAsyncLifetime
{
    private const string Cedula = "0104576277";
    private const string PasswordNueva = "montania2026";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> Cambiar(
        HttpClient cliente, string actual, string nueva) =>
        cliente.PostAsJsonAsync("/api/auth/cambiar-password",
            new { passwordActual = actual, passwordNueva = nueva });

    [Fact]
    public async Task Login_deUsuarioConObligacionPendiente_traeLaBandera()
    {
        var usuario = await Sembrador.UsuarioAsync(api, Cedula);
        await using (var db = api.NuevoDbContext())
        {
            var guardado = await db.Usuarios.FirstAsync(u => u.Id == usuario.Id);
            guardado.DebeCambiarPassword = true;
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login",
            new { cedula = Cedula, password = Sembrador.PasswordPorDefecto });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await respuesta.Content.ReadFromJsonAsync<LoginResponseDto>();
        login!.DebeCambiarPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_deUsuarioNormal_traeLaBanderaApagada()
    {
        await Sembrador.UsuarioAsync(api, Cedula);

        var respuesta = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login",
            new { cedula = Cedula, password = Sembrador.PasswordPorDefecto });

        var login = await respuesta.Content.ReadFromJsonAsync<LoginResponseDto>();
        login!.DebeCambiarPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Cambiar_conLaPasswordActualCorrecta_bajaLaBandera()
    {
        var usuario = await Sembrador.UsuarioAsync(api, Cedula);
        await using (var db = api.NuevoDbContext())
        {
            var guardado = await db.Usuarios.FirstAsync(u => u.Id == usuario.Id);
            guardado.DebeCambiarPassword = true;
            await db.SaveChangesAsync();
        }

        var respuesta = await Cambiar(
            api.ComoUsuario("OperadorCAT", Cedula),
            Sembrador.PasswordPorDefecto, PasswordNueva);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var verificacion = api.NuevoDbContext();
        var actualizado = await verificacion.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        actualizado.DebeCambiarPassword.ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(PasswordNueva, actualizado.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Cambiar_conLaPasswordActualIncorrecta_devuelve401_ySinCambiarNada()
    {
        var usuario = await Sembrador.UsuarioAsync(api, Cedula);

        var respuesta = await Cambiar(
            api.ComoUsuario("OperadorCAT", Cedula), "otra-clave-9999", PasswordNueva);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, actualizado.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Cambiar_aUnaPasswordQueIncumpleLaPolitica_devuelve400()
    {
        await Sembrador.UsuarioAsync(api, Cedula);

        // Sin dígitos y demasiado corta
        var respuesta = await Cambiar(
            api.ComoUsuario("OperadorCAT", Cedula),
            Sembrador.PasswordPorDefecto, "corta");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cambiar_sinAutenticar_devuelve401()
    {
        var respuesta = await Cambiar(
            api.ComoAnonimo(), Sembrador.PasswordPorDefecto, PasswordNueva);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA en compilación — `LoginResponseDto` no tiene `DebeCambiarPassword`.

- [ ] **Step 3: Añadir la bandera a la respuesta de login**

En `Common/Auth/AuthDtos.cs`, añadir el parámetro **al final** de `LoginResponseDto` (añadirlo en medio rompería a cualquier llamador posicional):

```csharp
public record LoginResponseDto(
    string Token,
    string NombreCompleto,
    string Cedula,
    string Rol,
    string? CatAsignado,
    // Expiración del access token (corto). El front renueva al vencer.
    DateTime Expira,
    // Fin de la sesión de 7 días (expiración del refresh token). El front la
    // usa para saber hasta cuándo permite "entrar directo" sin conexión.
    DateTime SesionExpira,
    // Se activó tras un restablecimiento: el front lleva al usuario a la
    // pantalla de cambio y no le deja navegar a otra hasta que la cambie.
    bool DebeCambiarPassword
);
```

- [ ] **Step 4: Propagar la bandera desde el servicio de sesiones**

En `Common/Auth/SesionService.cs`, dentro de `ConstruirResultado`, añadir el argumento a la construcción del DTO (queda como último parámetro nombrado):

```csharp
        var respuesta = new LoginResponseDto(
            Token: accessToken,
            NombreCompleto: usuario.NombreCompleto,
            Cedula: usuario.Cedula,
            Rol: usuario.Rol.ToString(),
            CatAsignado: usuario.CatAsignado?.ToString(),
            Expira: DateTime.UtcNow.Add(DuracionAccessToken),
            SesionExpira: refreshExpira,
            DebeCambiarPassword: usuario.DebeCambiarPassword);
```

Esto cubre login **y** refresh: ambos pasan por `ConstruirResultado`, así que la bandera sobrevive a una recarga de la página.

- [ ] **Step 5: Añadir el cambio de contraseña al servicio**

En `Common/Auth/Recuperacion/RecuperacionService.cs`, añadir a la interfaz:

```csharp
    Task<bool> CambiarPasswordAsync(string cedula, string passwordActual, string passwordNueva);
```

Y el método a la clase:

```csharp
    /// <summary>
    /// Cambia la contraseña del usuario autenticado. Devuelve false si la
    /// contraseña actual no coincide; lanza InvalidOperationException si la
    /// nueva incumple la política.
    /// </summary>
    public async Task<bool> CambiarPasswordAsync(
        string cedula, string passwordActual, string passwordNueva)
    {
        // Se valida ANTES de comprobar la actual: no tiene sentido pedirle al
        // usuario que reintente la actual si la nueva no iba a servir igual
        PoliticaPassword.Validar(passwordNueva);

        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Cedula == cedula && u.Activo)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(passwordActual, usuario.PasswordHash))
            return false;

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva);
        usuario.DebeCambiarPassword = false;
        await db.SaveChangesAsync();
        return true;
    }
```

- [ ] **Step 6: Añadir el endpoint**

En `Common/Auth/Recuperacion/RecuperacionController.cs`, antes del helper `IpCliente`:

```csharp
    /// <summary>
    /// Cambia la contraseña del usuario autenticado. Sirve al cambio
    /// obligatorio tras un restablecimiento y al voluntario de cualquier
    /// usuario: es la misma operación.
    ///
    /// La ruta es absoluta porque no cuelga de /api/auth/recuperacion — el
    /// cambio de contraseña no es parte del flujo de solicitud.
    /// </summary>
    [HttpPost("/api/auth/cambiar-password")]
    [Authorize]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordDto dto)
    {
        var cedula = User.FindFirstValue("cedula");
        if (string.IsNullOrEmpty(cedula))
            return Unauthorized(new { mensaje = "Sesión no válida." });

        try
        {
            var ok = await servicio.CambiarPasswordAsync(
                cedula, dto.PasswordActual, dto.PasswordNueva);

            return ok
                ? NoContent()
                : Unauthorized(new { mensaje = "La contraseña actual no es correcta." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized(new { mensaje = "Sesión no válida." });
        }
    }
```

- [ ] **Step 7: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 6 pruebas de `CambioPasswordTests` pasan y todo lo anterior sigue en verde.

- [ ] **Step 8: Commit**

```bash
git add Common/Auth/ tests/CoopagcuyApi.Tests/Integracion/CambioPasswordTests.cs
git commit -m "feat: obligar a cambiar la contraseña temporal al entrar"
```

---

## Tarea 7: Restringir las sesiones activas al administrador técnico

**Files:**
- Modify: `Common/Auth/AuthController.cs:94`, `:107`, `:119`
- Test: `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`

**Interfaces:**
- Consumes: `ApiFactory.ComoAdmin()` (= AdminCooperativa), `ApiFactory.ComoAdminTecnico()` (Tarea 4)
- Produces: nada

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`:

```csharp
using System.Net;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Los dos roles de administración dejan de ser intercambiables: el técnico
/// conserva todo el sistema, el de cooperativa pierde las sesiones activas
/// pero gana la bandeja de contraseñas. Se comprueba en el API y no solo en
/// las rutas del front: una ruta protegida sin su [Authorize] correspondiente
/// es una falsa sensación de seguridad — con el token se llama igual.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AutorizacionAdminTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdminCooperativa_noPuedeVerLasSesionesActivas()
    {
        var respuesta = await api.ComoAdmin().GetAsync("/api/auth/sesiones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminCooperativa_noPuedeRevocarSesiones()
    {
        var porId = await api.ComoAdmin().DeleteAsync("/api/auth/sesiones/1");
        var porUsuario = await api.ComoAdmin()
            .DeleteAsync("/api/auth/sesiones/usuario/1");

        porId.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        porUsuario.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminTecnico_siPuedeVerLasSesionesActivas()
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync("/api/auth/sesiones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LosDosAdministradores_venLaBandejaDeContrasenas()
    {
        var cooperativa = await api.ComoAdmin().GetAsync("/api/auth/recuperacion");
        var tecnico = await api.ComoAdminTecnico().GetAsync("/api/auth/recuperacion");

        cooperativa.StatusCode.ShouldBe(HttpStatusCode.OK);
        tecnico.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnOperador_noVeLaBandejaDeContrasenas()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/auth/recuperacion");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLAN las dos primeras — `AdminCooperativa` todavía recibe 200/404 en sesiones en lugar de 403.

- [ ] **Step 3: Restringir los tres endpoints**

En `Common/Auth/AuthController.cs`, cambiar el atributo de los tres endpoints de sesiones (`ListarSesiones` línea 94, `RevocarSesion` línea 107, `RevocarSesionesUsuario` línea 119):

```csharp
    [Authorize(Roles = "AdminTecnico")]
```

Y actualizar el comentario de sección justo encima de `ListarSesiones` para que diga por qué:

```csharp
    // ── Administración de sesiones activas ────────────────────────────────
    // Solo el administrador TÉCNICO: revocar sesiones es una herramienta de
    // soporte, no de gestión. El administrador de cooperativa conserva la
    // bandeja de contraseñas, que es lo que necesita para desbloquear gente.
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 5 pruebas de `AutorizacionAdminTests` pasan. **Toda la batería del API en verde** — 12 originales + 3 + 3 + 6 + 8 + 6 + 5 = 43 pruebas.

- [ ] **Step 5: Commit**

```bash
git add Common/Auth/AuthController.cs tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs
git commit -m "feat: reservar las sesiones activas al administrador técnico"
```

---

## Tarea 8: Cliente HTTP y tipos del front

A partir de aquí se trabaja en `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`.

**Files:**
- Modify: `src/types/auth.ts`
- Create: `src/types/recuperacion.ts`
- Create: `src/api/recuperacion.ts`

**Interfaces:**
- Consumes: los endpoints de las tareas 4, 5 y 6
- Produces: `LoginResponse.debeCambiarPassword: boolean`; `SolicitudPassword`, `PasswordTemporal` (tipos); `recuperacionApi.solicitar/listar/resolver/descartar/cambiarPassword`

- [ ] **Step 1: Añadir la bandera al tipo de login**

En `src/types/auth.ts`, dentro de `LoginResponse`, después de `sesionExpira`:

```typescript
    // Se activó tras un restablecimiento: hay que cambiar la contraseña antes
    // de poder usar el resto de la aplicación
    debeCambiarPassword: boolean;
```

- [ ] **Step 2: Crear los tipos del módulo**

Crear `src/types/recuperacion.ts`:

```typescript
export type EstadoSolicitud = "Pendiente" | "Resuelta" | "Descartada";

export interface SolicitudPassword {
    id: number;
    usuarioId: number;
    nombreCompleto: string;
    cedula: string;
    rol: string;
    catAsignado: string | null;
    // El usuario pudo desactivarse tras solicitar: restablecerle la
    // contraseña devolvería el acceso a alguien ya apartado
    usuarioActivo: boolean;
    estado: EstadoSolicitud;
    fechaCreacion: string;
    fechaResolucion: string | null;
    resueltaPor: string | null;
}

// Llega UNA sola vez, al resolver. No se puede volver a consultar.
export interface PasswordTemporal {
    passwordTemporal: string;
    nombreCompleto: string;
    cedula: string;
}
```

- [ ] **Step 3: Crear el cliente HTTP**

Crear `src/api/recuperacion.ts`:

```typescript
import client from "./client";
import type { PasswordTemporal, SolicitudPassword } from "../types/recuperacion";

export const recuperacionApi = {
    // Anónimo. Responde siempre lo mismo exista o no el usuario: es el
    // servidor quien decide, aquí no hay nada que interpretar.
    solicitar: async (cedula: string) => {
        const { data } = await client.post<{ mensaje: string }>(
            "/api/auth/recuperacion", { cedula });
        return data.mensaje;
    },

    listar: async (incluirResueltas = false) => {
        const { data } = await client.get<SolicitudPassword[]>(
            "/api/auth/recuperacion",
            { params: incluirResueltas ? { incluirResueltas: true } : undefined });
        return data;
    },

    resolver: async (id: number) => {
        const { data } = await client.post<PasswordTemporal>(
            `/api/auth/recuperacion/${id}/resolver`);
        return data;
    },

    descartar: async (id: number) => {
        await client.post(`/api/auth/recuperacion/${id}/descartar`);
    },

    cambiarPassword: async (passwordActual: string, passwordNueva: string) => {
        await client.post("/api/auth/cambiar-password",
            { passwordActual, passwordNueva });
    },
};
```

- [ ] **Step 4: Verificar que compila**

Run:
```bash
pnpm build
```
Expected: `tsc -b` sin errores y `vite build` completa (`✓ built in …`).

- [ ] **Step 5: Commit**

```bash
git add src/types/auth.ts src/types/recuperacion.ts src/api/recuperacion.ts
git commit -m "feat: cliente HTTP de recuperación de contraseña"
```

---

## Tarea 9: Pantalla pública de solicitud

**Files:**
- Create: `src/pages/RecuperarPassword.tsx`
- Modify: `src/pages/Login.tsx`
- Modify: `src/App.tsx`

**Interfaces:**
- Consumes: `recuperacionApi.solicitar(cedula) -> Promise<string>` (Tarea 8), `esCedulaValida(cedula) -> boolean` (ya existe en `src/utils/validarCedula.ts`)
- Produces: ruta pública `/recuperar-password`

- [ ] **Step 1: Crear la pantalla**

Crear `src/pages/RecuperarPassword.tsx`:

```tsx
import { useState } from "react";
import { Link } from "react-router-dom";
import { recuperacionApi } from "../api/recuperacion";
import { esCedulaValida } from "../utils/validarCedula";

export default function RecuperarPassword() {
    const [cedula, setCedula] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [enviado, setEnviado] = useState(false);
    const [cargando, setCargando] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);

        // Se valida ANTES de llamar al servidor: el dígito verificador atrapa
        // casi todo error de tipeo sin tocar la red, y en una tablet con mala
        // señal eso es la diferencia entre respuesta inmediata y quince
        // segundos de espera. El servidor lo revalida igual.
        if (!esCedulaValida(cedula)) {
            setError("Ese número de cédula no es válido. Revisa los diez dígitos.");
            return;
        }

        setCargando(true);
        try {
            await recuperacionApi.solicitar(cedula);
            setEnviado(true);
        } catch (e: unknown) {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje
                ?? "No se pudo enviar la solicitud. Verifica tu conexión "
                + "e intenta de nuevo.");
        } finally {
            setCargando(false);
        }
    };

    const campo = "w-full px-3.5 py-3 bg-blanco border border-gray-300 rounded-xl "
        + "text-sm text-gray-900 placeholder:text-gray-400 "
        + "focus:border-primary-600 focus:outline-none "
        + "transition-colors duration-150";

    return (
        <div className="min-h-screen bg-superficie flex items-center justify-center
                    px-6 py-10">
            <div className="w-full max-w-sm bg-blanco rounded-3xl border
                      border-gray-200 px-6 sm:px-8 py-8 animate-fade-in-up">

                <img
                    src="/brand/aliados/cuy-azuayito.png"
                    alt="Cuy Azuayito — COOPAGCUY"
                    className="h-20 w-auto mx-auto mb-8"
                />

                {enviado ? (
                    /* Mensaje deliberadamente idéntico exista o no la cuenta:
                       decir "esa cédula no está registrada" permitiría a
                       cualquiera averiguar quién tiene acceso al sistema. */
                    <div className="text-center">
                        <div className="w-12 h-12 rounded-full bg-primary-50 mx-auto
                            mb-4 flex items-center justify-center text-2xl">
                            ✓
                        </div>
                        <h1 className="text-xl font-extrabold tracking-tight
                           text-gray-900 mb-2">
                            Solicitud enviada
                        </h1>
                        <p className="text-sm text-gray-600 leading-relaxed">
                            El administrador recibió tu solicitud. Se pondrá en
                            contacto contigo para darte una contraseña nueva.
                        </p>
                        <Link to="/login"
                            className="inline-block mt-8 text-sm font-semibold
                         text-primary-600 hover:text-primary-800">
                            Volver al inicio de sesión
                        </Link>
                    </div>
                ) : (
                    <>
                        <h1 className="text-2xl font-extrabold tracking-tight
                           text-gray-900 mb-2">
                            Recuperar contraseña
                        </h1>
                        <p className="text-sm text-gray-500 mb-7 leading-relaxed">
                            Escribe tu número de cédula. El administrador
                            recibirá tu solicitud y te contactará para darte
                            una contraseña nueva.
                        </p>

                        <form onSubmit={handleSubmit} className="space-y-5">
                            <div>
                                <label htmlFor="cedula"
                                    className="block text-sm font-medium text-gray-700 mb-1.5">
                                    Número de cédula
                                </label>
                                <input
                                    id="cedula"
                                    type="text"
                                    required
                                    autoFocus
                                    inputMode="numeric"
                                    autoComplete="username"
                                    maxLength={10}
                                    value={cedula}
                                    onChange={(e) => {
                                        setCedula(e.target.value.replace(/\D/g, ""));
                                        setError(null);
                                    }}
                                    placeholder="0102030405"
                                    className={campo}
                                />
                            </div>

                            {error && (
                                <div role="alert"
                                    className="bg-teja-50 border border-teja-200 rounded-xl
                                px-3.5 py-3 text-sm text-teja-700 animate-fade-in">
                                    {error}
                                </div>
                            )}

                            <button
                                type="submit"
                                disabled={cargando}
                                className="w-full min-h-[52px] px-4 bg-primary-600
                           hover:bg-primary-700 disabled:bg-primary-300
                           disabled:cursor-not-allowed text-blanco
                           font-display text-base rounded-xl
                           shadow-sm shadow-primary-900/20
                           transition-colors duration-150"
                            >
                                {cargando ? "Enviando…" : "Enviar solicitud"}
                            </button>
                        </form>

                        <Link to="/login"
                            className="block text-center mt-6 text-sm font-semibold
                         text-gray-500 hover:text-gray-800">
                            Volver al inicio de sesión
                        </Link>
                    </>
                )}
            </div>
        </div>
    );
}
```

- [ ] **Step 2: Añadir el enlace en el login**

En `src/pages/Login.tsx`, añadir el import de `Link` (la línea 2 pasa a importar ambos):

```tsx
import { useNavigate, Link } from "react-router-dom";
```

E insertar, inmediatamente **después** del `</form>` de cierre y antes del párrafo `<p className="text-xs text-gray-500 mt-10 …">`:

```tsx
                    <Link
                        to="/recuperar-password"
                        className="block text-center mt-5 text-sm font-semibold
                       text-primary-600 hover:text-primary-800
                       animate-fade-in-up"
                        style={{ animationDelay: "280ms" }}
                    >
                        ¿Olvidaste tu contraseña?
                    </Link>
```

- [ ] **Step 3: Registrar la ruta pública**

En `src/App.tsx`, añadir el import junto a los demás:

```tsx
import RecuperarPassword from "./pages/RecuperarPassword";
```

Y la ruta dentro del bloque `{/* Rutas públicas */}`, después de la de `/qr/:codigoLote`:

```tsx
            <Route path="/recuperar-password" element={<RecuperarPassword />} />
```

- [ ] **Step 4: Verificar que compila**

Run:
```bash
pnpm build
```
Expected: sin errores de TypeScript y build completa.

- [ ] **Step 5: Verificar en el navegador**

Levantar el front con la herramienta de preview y comprobar tres cosas en `/recuperar-password`:
1. Una cédula con el último dígito cambiado (`0102030406`) muestra el error **sin** que salga ninguna petición en el panel de red.
2. Una cédula válida dispara `POST /api/auth/recuperacion`.
3. Tras responder, la pantalla muestra "Solicitud enviada".

Tomar una captura de la pantalla de confirmación para el usuario.

- [ ] **Step 6: Commit**

```bash
git add src/pages/RecuperarPassword.tsx src/pages/Login.tsx src/App.tsx
git commit -m "feat: pantalla pública para solicitar una contraseña nueva"
```

---

## Tarea 10: Cambio obligatorio de contraseña en el front

**Files:**
- Create: `src/pages/CambiarPassword.tsx`
- Modify: `src/context/AuthContext.tsx`
- Modify: `src/components/PrivateRoute.tsx`
- Modify: `src/App.tsx`

**Interfaces:**
- Consumes: `recuperacionApi.cambiarPassword(actual, nueva)` (Tarea 8), `LoginResponse.debeCambiarPassword` (Tarea 8)
- Produces: `AuthContextType.debeCambiarPassword: boolean`; `AuthContextType.marcarPasswordCambiada: () => void`; ruta privada `/cambiar-password`

- [ ] **Step 1: Exponer la bandera en el contexto de sesión**

En `src/context/AuthContext.tsx`:

Añadir a `AuthContextType`, después de `modoOffline`:

```typescript
    // El usuario entró con una contraseña temporal: hasta que ponga una
    // propia, PrivateRoute lo mantiene en /cambiar-password
    debeCambiarPassword: boolean;
    marcarPasswordCambiada: () => void;
```

Añadir el estado junto a los demás `useState`:

```typescript
    const [debeCambiarPassword, setDebeCambiarPassword] = useState(false);
```

En `finalizar`, aceptar y fijar la bandera. La firma pasa a:

```typescript
    function finalizar(
        estado: AuthState, autenticado: boolean, offline: boolean,
        cambioPendiente = false,
    ) {
        setAuth(estado);
        setIsAuthenticated(autenticado);
        setModoOffline(offline);
        setDebeCambiarPassword(cambioPendiente);
        setBootstrapping(false);
    }
```

En `aplicarLogin`, pasar la bandera que llega del servidor (vale tanto para login como para refresh: ambos devuelven `LoginResponseDto`):

```typescript
        if (limpiarCache) queryClient.clear();
        finalizar(aEstado(identidad), true, false, data.debeCambiarPassword);
```

En `logout`, apagarla junto al resto del estado:

```typescript
        setModoOffline(false);
        setDebeCambiarPassword(false);
```

Añadir la función que la baja tras un cambio exitoso, justo antes del `return`:

```typescript
    // La llama la pantalla de cambio: evita tener que recargar la sesión
    // entera solo para enterarse de que la obligación ya se cumplió
    const marcarPasswordCambiada = () => setDebeCambiarPassword(false);
```

Y añadirla al valor del proveedor:

```tsx
        <AuthContext.Provider value={{
            auth, bootstrapping, isAuthenticated, modoOffline,
            debeCambiarPassword, marcarPasswordCambiada, login, logout,
        }}>
```

Nota: la restauración **offline** (`finalizar(aEstado(identidad), true, true)`) deja la bandera en `false` por omisión. Es correcto: sin conexión no se puede cambiar la contraseña de todos modos, y la única pantalla disponible offline es la de recepción. Al recuperar la señal, el `refresh` traerá la bandera real.

- [ ] **Step 2: Crear la pantalla de cambio**

Crear `src/pages/CambiarPassword.tsx`:

```tsx
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { recuperacionApi } from "../api/recuperacion";
import { useAuth } from "../context/useAuth";
import { MainLayout } from "../components/layout/MainLayout";

export default function CambiarPassword() {
    const { debeCambiarPassword, marcarPasswordCambiada } = useAuth();
    const navigate = useNavigate();

    const [actual, setActual] = useState("");
    const [nueva, setNueva] = useState("");
    const [repetida, setRepetida] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [cargando, setCargando] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);

        if (nueva !== repetida) {
            setError("Las dos contraseñas nuevas no coinciden.");
            return;
        }

        setCargando(true);
        try {
            await recuperacionApi.cambiarPassword(actual, nueva);
            marcarPasswordCambiada();
            navigate("/dashboard");
        } catch (e: unknown) {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje
                ?? "No se pudo cambiar la contraseña. Verifica tu conexión.");
        } finally {
            setCargando(false);
        }
    };

    const campo = "w-full px-3.5 py-3 bg-blanco border border-gray-300 rounded-xl "
        + "text-sm text-gray-900 placeholder:text-gray-400 "
        + "focus:border-primary-600 focus:outline-none "
        + "transition-colors duration-150";

    const formulario = (
        <div className="w-full max-w-sm mx-auto">
            <h1 className="text-2xl font-extrabold tracking-tight text-gray-900 mb-2">
                {debeCambiarPassword ? "Crea tu contraseña" : "Cambiar contraseña"}
            </h1>
            <p className="text-sm text-gray-500 mb-7 leading-relaxed">
                {debeCambiarPassword
                    ? "Entraste con una contraseña temporal. Elige una propia "
                    + "para seguir: nadie más debe conocerla."
                    : "Debe tener al menos 8 caracteres, con una letra y un número."}
            </p>

            <form onSubmit={handleSubmit} className="space-y-5">
                <div>
                    <label htmlFor="actual"
                        className="block text-sm font-medium text-gray-700 mb-1.5">
                        {debeCambiarPassword
                            ? "Contraseña temporal" : "Contraseña actual"}
                    </label>
                    <input id="actual" type="password" required
                        autoComplete="current-password"
                        value={actual}
                        onChange={(e) => setActual(e.target.value)}
                        className={campo} />
                </div>

                <div>
                    <label htmlFor="nueva"
                        className="block text-sm font-medium text-gray-700 mb-1.5">
                        Contraseña nueva
                    </label>
                    <input id="nueva" type="password" required
                        autoComplete="new-password"
                        value={nueva}
                        onChange={(e) => setNueva(e.target.value)}
                        className={campo} />
                    <p className="text-xs text-gray-400 mt-1.5">
                        Mínimo 8 caracteres, con al menos una letra y un número.
                    </p>
                </div>

                <div>
                    <label htmlFor="repetida"
                        className="block text-sm font-medium text-gray-700 mb-1.5">
                        Repite la contraseña nueva
                    </label>
                    <input id="repetida" type="password" required
                        autoComplete="new-password"
                        value={repetida}
                        onChange={(e) => setRepetida(e.target.value)}
                        className={campo} />
                </div>

                {error && (
                    <div role="alert"
                        className="bg-teja-50 border border-teja-200 rounded-xl
                        px-3.5 py-3 text-sm text-teja-700 animate-fade-in">
                        {error}
                    </div>
                )}

                <button type="submit" disabled={cargando}
                    className="w-full min-h-[52px] px-4 bg-primary-600
                     hover:bg-primary-700 disabled:bg-primary-300
                     disabled:cursor-not-allowed text-blanco
                     font-display text-base rounded-xl
                     shadow-sm shadow-primary-900/20
                     transition-colors duration-150">
                    {cargando ? "Guardando…" : "Guardar contraseña"}
                </button>
            </form>
        </div>
    );

    // Con la obligación pendiente se muestra SIN el armazón de navegación: el
    // menú invitaría a irse a otra pantalla, que es justo lo que se impide.
    if (debeCambiarPassword) {
        return (
            <div className="min-h-screen bg-superficie flex items-center
                      justify-center px-6 py-10">
                <div className="w-full max-w-sm bg-blanco rounded-3xl
                        border border-gray-200 px-6 sm:px-8 py-8
                        animate-fade-in-up">
                    {formulario}
                </div>
            </div>
        );
    }

    return <MainLayout>{formulario}</MainLayout>;
}
```

- [ ] **Step 3: Bloquear la navegación mientras la obligación siga viva**

En `src/components/PrivateRoute.tsx`, ampliar el import de react-router-dom:

```typescript
import { Navigate, useLocation } from "react-router-dom";
```

Cambiar la desestructuración del hook y añadir la ubicación actual:

```typescript
    const { isAuthenticated, bootstrapping, modoOffline, auth,
        debeCambiarPassword } = useAuth();
    const location = useLocation();
```

E insertar la guarda inmediatamente **después** del bloque `if (!isAuthenticated)` y **antes** de la comprobación de roles:

```typescript
    // Entró con una contraseña temporal: no se le deja ir a ninguna otra
    // pantalla hasta que ponga una propia. La comprobación va antes que la
    // de roles porque aplica a todos por igual.
    //
    // Se lee la ruta con useLocation y no con window.location.pathname: el
    // hook re-renderiza al navegar, la propiedad del navegador no, y con ella
    // la guarda se quedaría evaluando la ruta anterior.
    if (debeCambiarPassword && location.pathname !== "/cambiar-password") {
        return <Navigate to="/cambiar-password" replace />;
    }
```

- [ ] **Step 4: Registrar la ruta**

En `src/App.tsx`, añadir el import:

```tsx
import CambiarPassword from "./pages/CambiarPassword";
```

Y la ruta dentro del bloque de rutas privadas, justo después de la de `/dashboard`:

```tsx
            <Route path="/cambiar-password" element={
              <PrivateRoute><CambiarPassword /></PrivateRoute>
            } />
```

- [ ] **Step 5: Verificar que compila**

Run:
```bash
pnpm build
```
Expected: sin errores de TypeScript y build completa.

- [ ] **Step 6: Commit**

```bash
git add src/pages/CambiarPassword.tsx src/context/AuthContext.tsx src/components/PrivateRoute.tsx src/App.tsx
git commit -m "feat: obligar a crear una contraseña propia tras el restablecimiento"
```

---

## Tarea 11: Bandeja del administrador y partición de Administración

**Files:**
- Create: `src/components/admin/TablaUsuarios.tsx`
- Create: `src/components/admin/TablaComunidades.tsx`
- Create: `src/components/admin/SolicitudesPassword.tsx`
- Modify: `src/pages/Administracion.tsx`

**Interfaces:**
- Consumes: `recuperacionApi.listar/resolver/descartar` (Tarea 8), `SolicitudPassword`, `PasswordTemporal` (Tarea 8), `ModalShell`, `Badge`, `Segmentado` (ya existen)
- Produces: `<TablaUsuarios />`, `<TablaComunidades />`, `<SolicitudesPassword />` — los tres sin props: cada uno gestiona sus propias consultas y su propio formulario

- [ ] **Step 1: Extraer la tabla de usuarios**

Crear `src/components/admin/TablaUsuarios.tsx` moviendo el bloque `{tab === "usuarios" && (…)}` de `Administracion.tsx` junto con su `useQuery`, su mutación `toggleUsuario`, su estado de formulario y los helpers `nombreRol`/`nombreCat`:

```tsx
import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { usuariosApi } from "../../api/admin";
import { Badge } from "../ui/Badge";
import { FormUsuario } from "./FormUsuario";
import type { Usuario } from "../../types/admin";
import { ROLES } from "../../types/admin";
import { CENTROS_ACOPIO } from "../../types/productora";

export function TablaUsuarios() {
    const qc = useQueryClient();
    const [usuarioEditar, setUsuarioEditar] = useState<Usuario | null>(null);
    const [showForm, setShowForm] = useState(false);
    const [aviso, setAviso] = useState<string | null>(null);

    const { data: usuarios = [], isLoading } = useQuery({
        queryKey: ["usuarios"],
        queryFn: () => usuariosApi.listar(true),
    });

    const toggle = useMutation({
        mutationFn: ({ id, activo }: { id: number; activo: boolean }) =>
            usuariosApi.cambiarEstado(id, activo),
        onSuccess: () => qc.invalidateQueries({ queryKey: ["usuarios"] }),
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setAviso(err.response?.data?.mensaje
                ?? "No se pudo cambiar el estado del usuario.");
        },
    });

    const nombreRol = (rol: string) =>
        ROLES.find((r) => r.value === rol)?.label ?? rol;

    const nombreCat = (cat: string) =>
        CENTROS_ACOPIO.find((c) => c.value === cat)?.label ?? cat;

    return (
        <>
            <div className="flex justify-end mb-4">
                <button
                    onClick={() => { setUsuarioEditar(null); setShowForm(true); }}
                    className="h-11 px-5 bg-primary-600 hover:bg-primary-700
                     text-white text-sm font-semibold rounded-xl transition
                     active:scale-[0.98]"
                >
                    + Nuevo usuario
                </button>
            </div>

            {aviso && (
                <div className="bg-teja-50 border border-teja-100 rounded-xl px-4 py-3
                        text-sm text-teja-700 mb-4 flex items-center justify-between">
                    {aviso}
                    <button onClick={() => setAviso(null)}
                        className="text-teja-500 font-bold ml-4">✕</button>
                </div>
            )}

            <div className="bg-white rounded-2xl border border-gray-200 overflow-x-auto
                      animate-fade-in-up">
                {isLoading ? (
                    <div className="p-8 text-center text-sm text-gray-400">
                        Cargando usuarios…
                    </div>
                ) : (
                    <table className="w-full text-sm">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                {["Nombre", "Cédula", "Rol", "CAT", "Estado", ""].map((h) => (
                                    <th key={h}
                                        className="px-4 py-3 text-left text-xs font-bold
                                 text-gray-500 uppercase tracking-wide">
                                        {h}
                                    </th>
                                ))}
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {usuarios.map((u) => (
                                <tr key={u.id} className="hover:bg-gray-50 transition">
                                    <td className="px-4 py-3 font-medium text-gray-800">
                                        {u.nombreCompleto}
                                        {u.email && (
                                            <span className="block text-xs font-normal
                                                   text-gray-400">
                                                {u.email}
                                            </span>
                                        )}
                                    </td>
                                    <td className="px-4 py-3 font-mono text-xs text-gray-600">
                                        {u.cedula}
                                    </td>
                                    <td className="px-4 py-3 text-gray-600">
                                        {nombreRol(u.rol)}
                                    </td>
                                    <td className="px-4 py-3 text-gray-600">
                                        {u.catAsignado ? nombreCat(u.catAsignado) : "—"}
                                    </td>
                                    <td className="px-4 py-3">
                                        <Badge
                                            label={u.activo ? "Activo" : "Inactivo"}
                                            variant={u.activo ? "success" : "danger"}
                                        />
                                    </td>
                                    <td className="px-4 py-3 text-right space-x-3 whitespace-nowrap">
                                        <button
                                            onClick={() => {
                                                setUsuarioEditar(u);
                                                setShowForm(true);
                                            }}
                                            className="text-xs font-semibold text-primary-600
                                   hover:text-primary-800"
                                        >
                                            Editar
                                        </button>
                                        <button
                                            onClick={() => toggle.mutate({
                                                id: u.id, activo: !u.activo
                                            })}
                                            className={`text-xs font-semibold
                                    ${u.activo
                                                    ? "text-teja-500 hover:text-teja-700"
                                                    : "text-primary-600 hover:text-primary-800"}`}
                                        >
                                            {u.activo ? "Desactivar" : "Activar"}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {showForm && (
                <FormUsuario
                    usuario={usuarioEditar}
                    onClose={() => setShowForm(false)}
                />
            )}
        </>
    );
}
```

- [ ] **Step 2: Extraer la tabla de comunidades**

Crear `src/components/admin/TablaComunidades.tsx` con la misma operación sobre el bloque `{tab === "comunidades" && (…)}`:

```tsx
import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { catalogosApi } from "../../api/admin";
import { Badge } from "../ui/Badge";
import { FormComunidad } from "./FormComunidad";
import type { Comunidad } from "../../types/admin";
import { CENTROS_ACOPIO } from "../../types/productora";

export function TablaComunidades() {
    const qc = useQueryClient();
    const [comunidadEditar, setComunidadEditar] = useState<Comunidad | null>(null);
    const [showForm, setShowForm] = useState(false);

    const { data: comunidades = [], isLoading } = useQuery({
        queryKey: ["comunidades", "admin"],
        queryFn: () => catalogosApi.listarComunidades(true),
    });

    const toggle = useMutation({
        mutationFn: ({ id, activa }: { id: number; activa: boolean }) =>
            catalogosApi.cambiarEstadoComunidad(id, activa),
        onSuccess: () => qc.invalidateQueries({ queryKey: ["comunidades"] }),
    });

    const nombreCat = (cat: string) =>
        CENTROS_ACOPIO.find((c) => c.value === cat)?.label ?? cat;

    return (
        <>
            <div className="flex justify-end mb-4">
                <button
                    onClick={() => { setComunidadEditar(null); setShowForm(true); }}
                    className="h-11 px-5 bg-primary-600 hover:bg-primary-700
                     text-white text-sm font-semibold rounded-xl transition
                     active:scale-[0.98]"
                >
                    + Nueva comunidad
                </button>
            </div>

            <div className="bg-white rounded-2xl border border-gray-200 overflow-x-auto
                      animate-fade-in-up">
                {isLoading ? (
                    <div className="p-8 text-center text-sm text-gray-400">
                        Cargando comunidades…
                    </div>
                ) : (
                    <table className="w-full text-sm">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                {["Comunidad", "Cantón", "CAT de referencia", "Estado", ""]
                                    .map((h) => (
                                        <th key={h}
                                            className="px-4 py-3 text-left text-xs font-bold
                                     text-gray-500 uppercase tracking-wide">
                                            {h}
                                        </th>
                                    ))}
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {comunidades.map((c) => (
                                <tr key={c.id} className="hover:bg-gray-50 transition">
                                    <td className="px-4 py-3 font-medium text-gray-800">
                                        {c.nombre}
                                    </td>
                                    <td className="px-4 py-3 text-gray-600">{c.canton}</td>
                                    <td className="px-4 py-3 text-gray-600">
                                        {nombreCat(c.catReferencia)}
                                    </td>
                                    <td className="px-4 py-3">
                                        <Badge
                                            label={c.activa ? "Activa" : "Inactiva"}
                                            variant={c.activa ? "success" : "danger"}
                                        />
                                    </td>
                                    <td className="px-4 py-3 text-right space-x-3 whitespace-nowrap">
                                        <button
                                            onClick={() => {
                                                setComunidadEditar(c);
                                                setShowForm(true);
                                            }}
                                            className="text-xs font-semibold text-primary-600
                                   hover:text-primary-800"
                                        >
                                            Editar
                                        </button>
                                        <button
                                            onClick={() => toggle.mutate({
                                                id: c.id, activa: !c.activa
                                            })}
                                            className={`text-xs font-semibold
                                    ${c.activa
                                                    ? "text-teja-500 hover:text-teja-700"
                                                    : "text-primary-600 hover:text-primary-800"}`}
                                        >
                                            {c.activa ? "Desactivar" : "Activar"}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {showForm && (
                <FormComunidad
                    comunidad={comunidadEditar}
                    onClose={() => setShowForm(false)}
                />
            )}
        </>
    );
}
```

- [ ] **Step 3: Crear la bandeja de contraseñas**

Crear `src/components/admin/SolicitudesPassword.tsx`:

```tsx
import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { recuperacionApi } from "../../api/recuperacion";
import { ModalShell } from "../ui/ModalShell";
import { Badge } from "../ui/Badge";
import type { PasswordTemporal } from "../../types/recuperacion";
import { ROLES } from "../../types/admin";

// "hace 3 horas" pesa más que una fecha absoluta para decidir a quién llamar
// primero: lo que importa es cuánto lleva esperando ese operador.
function antiguedad(iso: string): string {
    const minutos = Math.max(0,
        Math.floor((Date.now() - Date.parse(iso)) / 60_000));
    if (minutos < 60) return `hace ${minutos} min`;
    const horas = Math.floor(minutos / 60);
    if (horas < 24) return `hace ${horas} h`;
    const dias = Math.floor(horas / 24);
    return dias === 1 ? "hace 1 día" : `hace ${dias} días`;
}

export function SolicitudesPassword() {
    const qc = useQueryClient();
    const [verHistorial, setVerHistorial] = useState(false);
    const [temporal, setTemporal] = useState<PasswordTemporal | null>(null);
    const [copiada, setCopiada] = useState(false);
    const [aviso, setAviso] = useState<string | null>(null);

    const { data: solicitudes = [], isLoading } = useQuery({
        queryKey: ["solicitudes-password", verHistorial],
        queryFn: () => recuperacionApi.listar(verHistorial),
    });

    const invalidar = () =>
        qc.invalidateQueries({ queryKey: ["solicitudes-password"] });

    const mensajeError = (e: unknown, porDefecto: string) => {
        const err = e as { response?: { data?: { mensaje?: string } } };
        setAviso(err.response?.data?.mensaje ?? porDefecto);
    };

    const resolver = useMutation({
        mutationFn: (id: number) => recuperacionApi.resolver(id),
        onSuccess: (datos) => {
            setTemporal(datos);
            setCopiada(false);
            invalidar();
        },
        onError: (e) => mensajeError(e,
            "No se pudo restablecer la contraseña. Actualiza la pantalla."),
    });

    const descartar = useMutation({
        mutationFn: (id: number) => recuperacionApi.descartar(id),
        onSuccess: invalidar,
        onError: (e) => mensajeError(e, "No se pudo descartar la solicitud."),
    });

    const nombreRol = (rol: string) =>
        ROLES.find((r) => r.value === rol)?.label ?? rol;

    return (
        <>
            <div className="flex items-center justify-between mb-4">
                <label className="flex items-center gap-2 text-sm text-gray-600">
                    <input type="checkbox" checked={verHistorial}
                        onChange={(e) => setVerHistorial(e.target.checked)}
                        className="w-4 h-4 rounded border-gray-300
                       text-primary-600 focus:ring-primary-500" />
                    Ver también las ya atendidas
                </label>
            </div>

            {aviso && (
                <div className="bg-teja-50 border border-teja-100 rounded-xl px-4 py-3
                        text-sm text-teja-700 mb-4 flex items-center justify-between">
                    {aviso}
                    <button onClick={() => setAviso(null)}
                        className="text-teja-500 font-bold ml-4">✕</button>
                </div>
            )}

            <div className="bg-white rounded-2xl border border-gray-200 overflow-x-auto
                      animate-fade-in-up">
                {isLoading ? (
                    <div className="p-8 text-center text-sm text-gray-400">
                        Cargando solicitudes…
                    </div>
                ) : solicitudes.length === 0 ? (
                    <div className="p-10 text-center">
                        <p className="text-sm font-medium text-gray-600">
                            No hay solicitudes pendientes
                        </p>
                        <p className="text-xs text-gray-400 mt-1.5">
                            Aquí aparecerán los usuarios que pidan una
                            contraseña nueva desde la pantalla de ingreso.
                        </p>
                    </div>
                ) : (
                    <table className="w-full text-sm">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                {["Usuario", "Cédula", "Rol", "Solicitada", "Estado", ""]
                                    .map((h) => (
                                        <th key={h}
                                            className="px-4 py-3 text-left text-xs font-bold
                                     text-gray-500 uppercase tracking-wide">
                                            {h}
                                        </th>
                                    ))}
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {solicitudes.map((s) => (
                                <tr key={s.id} className="hover:bg-gray-50 transition">
                                    <td className="px-4 py-3 font-medium text-gray-800">
                                        {s.nombreCompleto}
                                        {!s.usuarioActivo && (
                                            <span className="block text-xs font-normal
                                                   text-teja-600">
                                                Usuario desactivado
                                            </span>
                                        )}
                                    </td>
                                    <td className="px-4 py-3 font-mono text-xs text-gray-600">
                                        {s.cedula}
                                    </td>
                                    <td className="px-4 py-3 text-gray-600">
                                        {nombreRol(s.rol)}
                                    </td>
                                    <td className="px-4 py-3 text-gray-600">
                                        {antiguedad(s.fechaCreacion)}
                                    </td>
                                    <td className="px-4 py-3">
                                        <Badge
                                            label={s.estado}
                                            variant={s.estado === "Pendiente" ? "warning"
                                                : s.estado === "Resuelta" ? "success"
                                                    : "neutral"}
                                        />
                                    </td>
                                    <td className="px-4 py-3 text-right space-x-3 whitespace-nowrap">
                                        {s.estado === "Pendiente" ? (
                                            <>
                                                <button
                                                    disabled={resolver.isPending}
                                                    onClick={() => resolver.mutate(s.id)}
                                                    className="text-xs font-semibold
                                       text-primary-600 hover:text-primary-800
                                       disabled:text-gray-300"
                                                >
                                                    Restablecer
                                                </button>
                                                <button
                                                    disabled={descartar.isPending}
                                                    onClick={() => descartar.mutate(s.id)}
                                                    className="text-xs font-semibold
                                       text-teja-500 hover:text-teja-700
                                       disabled:text-gray-300"
                                                >
                                                    Descartar
                                                </button>
                                            </>
                                        ) : (
                                            <span className="text-xs text-gray-400">
                                                {s.resueltaPor
                                                    ? `por ${s.resueltaPor}` : "—"}
                                            </span>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {/* La contraseña temporal existe fuera del hash una sola vez, aquí.
                Si el administrador cierra sin anotarla, no hay forma de
                recuperarla: hay que restablecer otra vez. */}
            {temporal && (
                <ModalShell
                    onClose={() => setTemporal(null)}
                    title="Contraseña temporal"
                    subtitle={`Para ${temporal.nombreCompleto} · ${temporal.cedula}`}
                    footer={
                        <button
                            onClick={() => setTemporal(null)}
                            className="w-full min-h-[48px] bg-primary-600
                         hover:bg-primary-700 text-white text-sm
                         font-semibold rounded-xl transition"
                        >
                            Ya la anoté, cerrar
                        </button>
                    }
                >
                    <p className="text-sm text-gray-600 leading-relaxed mb-5">
                        Díctasela por teléfono o entrégasela en persona.
                        Al entrar, el sistema le pedirá crear una propia.
                    </p>

                    <div className="bg-superficie border border-gray-200 rounded-2xl
                          px-5 py-6 text-center">
                        <p className="font-mono text-2xl font-bold tracking-wide
                          text-gray-900 select-all">
                            {temporal.passwordTemporal}
                        </p>
                    </div>

                    <button
                        onClick={() => {
                            navigator.clipboard
                                ?.writeText(temporal.passwordTemporal)
                                .then(() => setCopiada(true))
                                .catch(() => setCopiada(false));
                        }}
                        className="w-full mt-3 min-h-[44px] text-sm font-semibold
                       text-primary-600 hover:text-primary-800"
                    >
                        {copiada ? "✓ Copiada" : "Copiar"}
                    </button>

                    <div className="mt-5 bg-teja-50 border border-teja-100 rounded-xl
                          px-4 py-3">
                        <p className="text-sm text-teja-700 leading-relaxed">
                            <strong>No se volverá a mostrar.</strong> Anótala
                            antes de cerrar esta ventana. Las sesiones abiertas
                            de este usuario ya fueron cerradas.
                        </p>
                    </div>
                </ModalShell>
            )}
        </>
    );
}
```

- [ ] **Step 4: Reescribir `Administracion.tsx` como contenedor de pestañas**

Sustituir el contenido completo de `src/pages/Administracion.tsx` por:

```tsx
import { useState } from "react";
import { MainLayout } from "../components/layout/MainLayout";
import { Segmentado } from "../components/ui/Segmentado";
import { TablaUsuarios } from "../components/admin/TablaUsuarios";
import { TablaComunidades } from "../components/admin/TablaComunidades";
import { SolicitudesPassword } from "../components/admin/SolicitudesPassword";

type Tab = "usuarios" | "comunidades" | "contrasenas";

// Esta pantalla solo elige pestaña. Cada una gestiona sus propios datos,
// su formulario y sus errores: antes vivían las tres cosas aquí y el
// archivo mezclaba responsabilidades que no se tocan entre sí.
export default function Administracion() {
    const [tab, setTab] = useState<Tab>("usuarios");

    return (
        <MainLayout>
            <div className="mb-6">
                <h1 className="text-2xl font-extrabold tracking-tight text-gray-900">
                    Administración
                </h1>
                <p className="text-sm text-gray-500 mt-1">
                    Usuarios, catálogo de comunidades y solicitudes de contraseña
                </p>
            </div>

            <Segmentado
                activo={tab}
                onCambio={setTab}
                opciones={[
                    { id: "usuarios", label: "Usuarios" },
                    { id: "comunidades", label: "Comunidades" },
                    { id: "contrasenas", label: "Contraseñas" },
                ]}
            />

            {tab === "usuarios" && <TablaUsuarios />}
            {tab === "comunidades" && <TablaComunidades />}
            {tab === "contrasenas" && <SolicitudesPassword />}
        </MainLayout>
    );
}
```

- [ ] **Step 5: Verificar que compila**

Run:
```bash
pnpm build
```
Expected: sin errores de TypeScript y build completa.

- [ ] **Step 7: Verificar en el navegador**

Con el API corriendo y una solicitud pendiente sembrada, entrar como administrador a `/administracion` y comprobar:
1. Las tres pestañas cargan (usuarios y comunidades siguen funcionando igual que antes de la extracción).
2. "Restablecer" abre el modal con la contraseña temporal.
3. Tras cerrarlo, la solicitud desaparece de la lista de pendientes.

Tomar una captura del modal con la contraseña temporal.

- [ ] **Step 8: Commit**

```bash
git add src/components/admin/ src/pages/Administracion.tsx
git commit -m "feat: bandeja de solicitudes de contraseña en Administración"
```

---

## Tarea 12: Restringir la pantalla de sesiones en el front

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/components/layout/MainLayout.tsx:15`

**Interfaces:**
- Consumes: nada
- Produces: nada

- [ ] **Step 1: Restringir la ruta**

En `src/App.tsx`, cambiar la ruta de `/sesiones`:

```tsx
            {/* Sesiones activas: solo el administrador técnico. Revocar
                sesiones es soporte, no gestión. */}
            <Route path="/sesiones" element={
              <PrivateRoute rolesPermitidos={["AdminTecnico"]}>
                <Sesiones />
              </PrivateRoute>
            } />
```

- [ ] **Step 2: Restringir la entrada de menú**

En `src/components/layout/MainLayout.tsx`, línea 15, cambiar los roles del elemento "Sesiones":

```tsx
    { to: "/sesiones", label: "Sesiones", roles: ["AdminTecnico"] },
```

- [ ] **Step 3: Verificar que compila**

Run:
```bash
pnpm build
```
Expected: sin errores de TypeScript y build completa.

- [ ] **Step 4: Verificar en el navegador**

Entrar como `AdminCooperativa` y comprobar que "Sesiones" **no** aparece en el menú, y que `/administracion` sigue mostrando las tres pestañas. Entrar como `AdminTecnico` y comprobar que "Sesiones" sí aparece y la pantalla carga.

- [ ] **Step 5: Commit**

```bash
git add src/App.tsx src/components/layout/MainLayout.tsx
git commit -m "feat: reservar la pantalla de sesiones al administrador técnico"
```

---

## Correcciones descubiertas al ejecutar el plan

Tres puntos donde el plan original estaba equivocado. Se corrigieron en el
texto de arriba; se dejan anotados aquí porque son trampas que volverían a
morder a quien retome el proyecto.

**1 · Las cédulas de prueba eran inválidas.** El plan usaba `0102030405`,
`0104576270` y `0102030413` como "cédulas válidas". Ninguna lo es: el dígito
verificador de `010203040` es **0**, no 5. El resultado fue que *todas* las
peticiones respondían 400 y —lo peor— la prueba que esperaba un 400 pasaba por
el motivo equivocado, dando una falsa sensación de cobertura. Cédulas válidas
verificadas: `0104576277`, `0111223343`, `0102030400`. Al elegir una cédula de
prueba hay que calcular el verificador, no inventarlo. Cuidado además con el
tercer dígito: si es 6 o mayor es un RUC, y el validador lo rechaza aunque el
verificador cuadre (`0176543213` y `0198765430` caen ahí).

**2 · El endpoint nuevo agotaba el rate limiter compartido.** `/api/auth/
recuperacion` entró en la política `auth`, que permite 10 peticiones por minuto
**y por IP** y ya la usaba `/api/auth/login`. Como toda la batería corre en
segundos desde la misma IP, al añadir las pruebas de la Tarea 6 el cupo se
agotó y `CedulaConDigitoVerificadorMalo_devuelve400` empezó a recibir 429 —una
prueba de la Tarea 4 rota por una tarea posterior, sin que nada en su código
cambiara. `Jwt.cs` ya advertía de este riesgo y el plan no lo tuvo en cuenta.
Solución: `ApiFactory.ClienteCon` da a cada cliente de prueba una IP propia del
rango TEST-NET-3 mediante `X-Forwarded-For`, que es lo que `UseForwardedHeaders`
reescribe en producción. Se ejercita el camino real en vez de doblar el
limitador. **Cualquier endpoint que se añada a la política `auth` en el futuro
hereda este problema.**

**3 · La guarda de `PrivateRoute` leía la ruta del navegador.** El borrador
usaba `window.location.pathname`, que no provoca re-render al navegar con React
Router: la guarda habría evaluado la ruta anterior. Se cambió a `useLocation()`.

---

## Cierre

Al terminar las 12 tareas:

1. **Batería completa del API en verde:**
   ```bash
   docker compose -f docker-compose.tests.yml run --rm tests
   ```
   43 pruebas, 0 fallos.

2. **Front compilando:**
   ```bash
   pnpm build
   ```

3. **La migración `RecuperacionPassword` está generada pero NO aplicada a Neon.** Se aplica sola en el despliegue (`main` con aprobación manual), o a mano con el mismo patrón en Docker cambiando `migrations add` por `database update` y usando la cadena real de Neon en `ConnectionStrings__NeonDb`.

4. **No hacer `push` sin pedírselo al usuario.** Son dos repositorios: el del API y el del front.

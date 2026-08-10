# Fases 0 y 1 — Tubería de pruebas y control de seguridad

> **Para agentes:** SUB-SKILL REQUERIDA: usa `superpowers:subagent-driven-development`
> (recomendado) o `superpowers:executing-plans` para ejecutar este plan tarea por
> tarea. Los pasos usan checkbox (`- [ ]`) para seguimiento.

**Objetivo:** dejar funcionando la tubería que ejecuta pruebas del API dentro de
Docker, y activar CodeQL, Trivy y Dependabot en ambos repos — sin escribir
todavía la batería de tests de negocio.

**Arquitectura:** un proyecto xUnit nuevo (`tests/CoopagcuyApi.Tests`) que
arranca la API real con `WebApplicationFactory` contra un PostgreSQL levantado
por `docker compose` en local y por *service container* en CI. Cinco workflows
nuevos de GitHub Actions cubren análisis estático, escaneo de imagen y
dependencias. Se implementa en el orden de las tareas: cada una deja el
repositorio en verde.

**Stack:** .NET 8 · xUnit 2.9 · Shouldly · Respawn · Testcontainers **no** ·
PostgreSQL 16 · GitHub Actions · CodeQL · Trivy · Dependabot

**Especificación de referencia:**
`docs/superpowers/specs/2026-08-10-testing-y-seguridad-design.md`

## Restricciones globales

- **Idioma:** todo comentario, nombre de test, mensaje de commit y texto de
  workflow va en español, siguiendo el estilo del repositorio.
- **TargetFramework:** `net8.0` en el proyecto de tests. El SDK instalado en la
  máquina es 10.0.302, que compila `net8.0` sin problema; el CI y los
  contenedores usan `8.0.x` / `mcr.microsoft.com/dotnet/sdk:8.0`.
- **Nunca ejecutar `dotnet test` ni `dotnet ef` directamente en Windows.** Smart
  App Control bloquea la carga del DLL recién compilado desde OneDrive (error
  `0x800711C7`). Todo pasa por Docker.
- **Nunca apuntar los tests a Neon.** La cadena de conexión de test sale de
  `TEST_DB_CONNECTION`; si la variable no existe se usa un Postgres local en el
  puerto **5433** (no 5432, para no chocar con una instalación existente).
- **Prohibido FluentAssertions.** Desde la versión 7 pasó a licencia comercial
  (Xceed). Como la Tarea 15 activa Dependabot, un bump automático a 8.x
  introduciría una obligación de licencia en silencio. Se usa **Shouldly**
  (BSD-3), que no tiene ese problema.
- **`Jwt:Key` de prueba:** debe tener 32 caracteres o más; HMAC-SHA256 exige
  256 bits. Valor fijo definido en la Tarea 4.
- **No tocar `appsettings.json`.** La configuración de test se inyecta en
  memoria desde `ApiFactory`.
- **Commits:** uno por tarea, con el prefijo indicado en cada una. No hacer
  `push` sin pedirlo al usuario.

---

## Hallazgo previo que condiciona la Tarea 1

Durante el análisis, el repositorio del API cambió de rama (`git checkout
develop`). Eso destapó un problema real:

| Repo | `develop` (staging) | `main` (producción) |
|---|---|---|
| **API** | `e8aad52` — **4 commits detrás** | `d88b7bd` — tiene sesiones, `AlcanceUsuario`, vinculación |
| **Front** | `7266588` — tiene la pantalla de sesiones | `34cefb1` — 6 commits detrás |

Están invertidos **entre sí**: el front de staging tiene la pantalla de sesiones
activas, pero el API de staging **no tiene esos endpoints**. Es exactamente el
fallo del 2026-07-08 con `api/reportes.ts`, repetido.

Consecuencia directa para este plan: en `develop` del API no existen
`AlcanceUsuario.cs`, `SesionService.cs`, `RefreshToken.cs` ni
`EntregaPendienteVinculacion.cs`, así que los tests de las fases 2 y 3 **no
compilarían ahí**. La Tarea 1 lo resuelve antes de tocar nada más.

---

## Estructura de archivos

### Repo `CoopagcuyApi`

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `Program.cs` (modificar, final) | Exponer `Program` como tipo público anclable | 2 |
| `CoopagcuyApi.csproj` (modificar) | Activar `packages.lock.json` | 8 |
| `CoopagcuyApi.slnx` (modificar) | Registrar el proyecto de tests | 2 |
| `.dockerignore` (modificar) | Excluir `tests/` del contexto de imagen | 9 |
| `Dockerfile` (modificar) | Usuario sin privilegios | 9 |
| `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj` | Proyecto de pruebas | 2 |
| `tests/CoopagcuyApi.Tests/Unitarias/ValidadorCedulaTests.cs` | Primera prueba, sin infraestructura | 2 |
| `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs` | Arranca la API con configuración de test; punto de entrada único de los tests | 4, 5 |
| `tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs` | Espera Postgres, migra una vez, limpia con Respawn | 5 |
| `tests/CoopagcuyApi.Tests/Infra/ColeccionApi.cs` | `[CollectionDefinition]` que comparte `ApiFactory` | 4 |
| `tests/CoopagcuyApi.Tests/Infra/Jwt.cs` | Emite tokens de prueba por rol | 5 |
| `tests/CoopagcuyApi.Tests/Integracion/SaludTests.cs` | Verifica que la API arranca en el harness | 4 |
| `tests/CoopagcuyApi.Tests/Integracion/ArranqueBaseDatosTests.cs` | Verifica migración, limpieza y autenticación | 5 |
| `docker-compose.tests.yml` | Postgres + runner de `dotnet test` | 3 |
| `.github/workflows/codeql.yml` | Análisis estático C# | 10 |
| `.github/workflows/seguridad.yml` | Trivy fs/secret/config + auditoría NuGet | 12 |
| `.github/workflows/deploy.yml` (modificar) | Pruebas en el job `build-test` (T7) y escaneo de imagen antes del push (T13) | 7, 13 |
| `.github/dependabot.yml` | nuget · github-actions · docker | 15 |

### Repo `coopagcuy-frontend`

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `.github/workflows/codeql.yml` | Análisis estático TS | 11 |
| `.github/workflows/deploy.yml` (modificar) | `pnpm lint` y `pnpm audit` como gate | 14 |
| `.github/dependabot.yml` | npm · github-actions | 15 |

---

## Tarea 1: Reconciliar `develop` con `main` en el API

Sin esto, las fases 2 y 3 no compilan. Es la única tarea que toca ramas
compartidas.

**Archivos:** ninguno. Solo operaciones de git.

**Interfaces:**
- Produce: rama `develop` del API con el mismo árbol que `main`, base sobre la
  que trabajan todas las tareas siguientes.

- [ ] **Paso 1: Confirmar que `develop` no tiene commits propios**

```bash
git -C CoopagcuyApi log --oneline main..develop
```

Salida esperada: **vacía**. Si imprime commits, DETENER y avisar al usuario: la
fusión ya no es un fast-forward y hay que decidir cómo integrar.

- [ ] **Paso 2: Confirmar qué traerá el fast-forward**

```bash
git -C CoopagcuyApi log --oneline develop..main
```

Salida esperada: exactamente 4 commits, terminando en `d88b7bd feat: Sesiones
activas y sistema de catalagos mejorado.`

- [ ] **Paso 3: Adelantar `develop` a `main`**

```bash
git -C CoopagcuyApi checkout develop && git -C CoopagcuyApi merge --ff-only main
```

Salida esperada: `Fast-forward` y `develop` en `d88b7bd`.

- [ ] **Paso 4: Verificar que los archivos que faltaban ya están**

```bash
ls CoopagcuyApi/Common/Auth/AlcanceUsuario.cs CoopagcuyApi/Common/Auth/SesionService.cs
```

Esperado: ambos existen.

- [ ] **Paso 5: Compilar para confirmar que el árbol es coherente**

```bash
dotnet build CoopagcuyApi/CoopagcuyApi.csproj -c Release
```

Esperado: `Build succeeded. 0 Error(s)`. (`dotnet build` sí funciona en Windows;
lo que falla es cargar el DLL, cosa que `build` no hace.)

- [ ] **Paso 6: PARAR y pedir confirmación al usuario antes de publicar**

Empujar `develop` dispara un despliegue a staging **y aplica la migración
`SesionesYVinculacionOffline` a la rama Neon de staging**. No ejecutar sin un sí
explícito:

```bash
git -C CoopagcuyApi push origin develop
```

El resto del plan funciona con `develop` solo en local. Si el usuario prefiere
no publicar todavía, continuar con la Tarea 2.

---

## Tarea 2: Proyecto de pruebas con la primera unitaria en verde

**Archivos:**
- Modificar: `Program.cs` (final del archivo)
- Modificar: `CoopagcuyApi.slnx`
- Crear: `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`
- Crear: `tests/CoopagcuyApi.Tests/Unitarias/ValidadorCedulaTests.cs`

**Interfaces:**
- Consume: rama `develop` de la Tarea 1.
- Produce: el ensamblado de pruebas `CoopagcuyApi.Tests`, referenciado desde
  `CoopagcuyApi.slnx`, sobre el que construyen todas las tareas siguientes.

- [ ] **Paso 1: Leer el validador que se va a probar**

```bash
cat CoopagcuyApi/Common/Auth/ValidadorCedula.cs
```

Anotar el nombre exacto del tipo, del método público y su firma. El código del
Paso 3 asume `ValidadorCedula.EsValida(string)` devolviendo `bool`; **si la
firma real difiere, ajustar el test a la real, no al revés.**

- [ ] **Paso 2: Crear el proyecto de pruebas**

Crear `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <!-- Shouldly y no FluentAssertions: esta última pasó a licencia
         comercial en la v7 y Dependabot la subiría sin avisar -->
    <PackageReference Include="Shouldly" Version="4.2.1" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.4" />
    <PackageReference Include="Respawn" Version="6.2.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\CoopagcuyApi.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Paso 3: Escribir el test que debe fallar**

Crear `tests/CoopagcuyApi.Tests/Unitarias/ValidadorCedulaTests.cs`:

```csharp
using CoopagcuyApi.Common.Auth;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

// Primera prueba del repositorio: su valor real es confirmar que la
// tubería (compilación, runner, Docker, CI) funciona de extremo a extremo.
// La batería completa de cédulas llega en la Fase 2.
public class ValidadorCedulaTests
{
    [Theory]
    [InlineData("0102030405")]   // dígito verificador correcto
    public void Cedula_conDigitoVerificadorValido_esAceptada(string cedula)
    {
        ValidadorCedula.EsValida(cedula).ShouldBeTrue();
    }

    [Theory]
    [InlineData("0102030406")]   // último dígito alterado
    [InlineData("010203040")]    // nueve dígitos
    [InlineData("3002030405")]   // código de provincia 30, inexistente
    [InlineData("")]
    public void Cedula_invalida_esRechazada(string cedula)
    {
        ValidadorCedula.EsValida(cedula).ShouldBeFalse();
    }
}
```

**Antes de continuar, verificar que `0102030405` es realmente válida** según el
algoritmo del Paso 1. Si no lo es, calcular una que sí lo sea con el módulo 10
ecuatoriano y sustituirla; un test de referencia con datos falsos es peor que
no tenerlo.

- [ ] **Paso 4: Registrar el proyecto en la solución**

Reemplazar `CoopagcuyApi.slnx` por:

```xml
<Solution>
  <Project Path="CoopagcuyApi.csproj" />
  <Project Path="tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj" />
</Solution>
```

- [ ] **Paso 5: Verificar que compila**

```bash
dotnet build CoopagcuyApi.slnx -c Release
```

Esperado: `Build succeeded. 0 Error(s)`, con dos proyectos compilados.

No ejecutar `dotnet test` todavía: en Windows fallaría con `0x800711C7`. Eso lo
resuelve la Tarea 3.

- [ ] **Paso 6: Hacer `Program` anclable para `WebApplicationFactory`**

Añadir al **final** de `Program.cs`, después de `app.Run();`:

```csharp

// `WebApplicationFactory<T>` necesita un tipo público del ensamblado de la
// aplicación al que anclarse. Con top-level statements la clase generada es
// interna, así que se declara explícitamente aquí.
public partial class Program;
```

- [ ] **Paso 7: Verificar que sigue compilando**

```bash
dotnet build CoopagcuyApi.slnx -c Release
```

Esperado: `Build succeeded. 0 Error(s)`.

- [ ] **Paso 8: Commit**

```bash
git add CoopagcuyApi.slnx Program.cs tests/
git commit -m "test: proyecto de pruebas xUnit y primera unitaria de cedula"
```

---

## Tarea 3: Ejecutar las pruebas dentro de Docker

Esta tarea existe para demostrar que el rodeo a Smart App Control funciona. Si
falla, todo el plan se detiene aquí.

**Archivos:**
- Crear: `docker-compose.tests.yml`

**Interfaces:**
- Consume: `tests/CoopagcuyApi.Tests` de la Tarea 2.
- Produce: el servicio `tests` y el servicio `postgres` (host `postgres`, puerto
  interno `5432`, expuesto en el host como **5433**, base `coopagcuy_test`,
  usuario/clave `postgres`/`postgres`). Las Tareas 5 y 7 dependen de esos
  valores exactos.

- [ ] **Paso 1: Crear el compose**

Crear `docker-compose.tests.yml`:

```yaml
# Ejecuta la batería de pruebas dentro de Linux. Necesario porque Smart App
# Control bloquea la carga del DLL recién compilado desde OneDrive (0x800711C7),
# así que `dotnet test` no corre en el Windows del equipo.
#
#   docker compose -f docker-compose.tests.yml run --rm tests
#
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: coopagcuy_test
    ports:
      - "5433:5432"        # 5433 en el host: no choca con un Postgres local
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d coopagcuy_test"]
      interval: 3s
      timeout: 3s
      retries: 20
    tmpfs:
      - /var/lib/postgresql/data   # efímero: la BD de test no debe sobrevivir

  tests:
    image: mcr.microsoft.com/dotnet/sdk:8.0
    working_dir: /src
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      # Nombre de servicio, no localhost: se resuelve en la red del compose
      TEST_DB_CONNECTION: "Host=postgres;Port=5432;Database=coopagcuy_test;Username=postgres;Password=postgres"
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      # Los paquetes NuGet se cachean en un volumen para no re-descargar
      NUGET_PACKAGES: /nuget
    volumes:
      - .:/src
      - nuget-cache:/nuget
    command: dotnet test CoopagcuyApi.slnx --logger "console;verbosity=normal"

volumes:
  nuget-cache:
```

- [ ] **Paso 2: Ejecutar**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed!  - Failed: 0, Passed: 5` (una del `[Theory]` válido, cuatro
del inválido). La primera corrida tarda varios minutos por la descarga de
paquetes; las siguientes reutilizan el volumen `nuget-cache`.

- [ ] **Paso 3: Comprobar que un fallo se detecta**

Cambiar temporalmente en `ValidadorCedulaTests.cs` el `ShouldBeTrue()` de la
primera prueba por `ShouldBeFalse()` y volver a ejecutar el comando del Paso 2.

Esperado: `Failed!  - Failed: 1`. Un runner que nunca falla no está probando
nada. **Revertir el cambio** y confirmar que vuelve a `Failed: 0`.

- [ ] **Paso 4: Bajar el Postgres que quedó levantado**

```bash
docker compose -f docker-compose.tests.yml down -v
```

- [ ] **Paso 5: Commit**

```bash
git add docker-compose.tests.yml
git commit -m "test: ejecucion de pruebas en Docker con Postgres efimero"
```

---

## Tarea 4: `ApiFactory` y prueba de arranque sin base de datos

**Archivos:**
- Crear: `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`
- Crear: `tests/CoopagcuyApi.Tests/Infra/ColeccionApi.cs`
- Crear: `tests/CoopagcuyApi.Tests/Integracion/SaludTests.cs`

**Interfaces:**
- Consume: `Program` público (Tarea 2), `TEST_DB_CONNECTION` (Tarea 3).
- Produce:
  - `ApiFactory : WebApplicationFactory<Program>`
  - `ApiFactory.Cadena` → `string`, cadena de conexión de test
  - `ApiFactory.ClaveJwt` → `const string`, clave de firma de test
  - `ColeccionApi.Nombre` → `const string` `"api"`, para `[Collection(...)]`

- [ ] **Paso 1: Escribir el test que debe fallar**

Crear `tests/CoopagcuyApi.Tests/Integracion/SaludTests.cs`:

```csharp
using System.Net;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

// No toca la base de datos a propósito: aísla "¿arranca la aplicación dentro
// del harness?" de "¿está bien la base de datos?". Si esta pasa y las demás
// fallan, el problema es de datos, no de configuración.
[Collection(ColeccionApi.Nombre)]
public class SaludTests(ApiFactory api)
{
    [Fact]
    public async Task Health_respondeOk_sinAutenticacion()
    {
        var respuesta = await api.CreateClient().GetAsync("/health");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

- [ ] **Paso 2: Verificar que falla por no compilar**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: error de compilación `CS0246: The type or namespace name 'ApiFactory'
could not be found`.

- [ ] **Paso 3: Crear la fábrica**

Crear `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" y no "Development": así el CORS usa la lista explícita de
        // orígenes y Swagger queda apagado, como en producción.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Se añade al final para que gane sobre appsettings.json
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NeonDb"] = Cadena,
                ["Jwt:Key"] = ClaveJwt,
                ["Cors:AllowedOrigins"] = "https://localhost:5173",
                ["AzureBlob:ConnectionString"] = "",
                ["QR:BaseUrl"] = "https://localhost/qr"
            });
        });
    }
}
```

- [ ] **Paso 4: Crear la colección compartida**

Crear `tests/CoopagcuyApi.Tests/Infra/ColeccionApi.cs`:

```csharp
using Xunit;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Todas las clases de prueba comparten una sola <see cref="ApiFactory"/>.
/// Esto además las serializa, que es obligatorio: comparten una única base de
/// datos y correrlas en paralelo haría que la limpieza de una borrase los
/// datos de otra.
/// </summary>
[CollectionDefinition(Nombre)]
public class ColeccionApi : ICollectionFixture<ApiFactory>
{
    public const string Nombre = "api";
}
```

- [ ] **Paso 5: Verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed!  - Failed: 0, Passed: 6`.

Si falla con `Jwt:Key no configurado`, la configuración en memoria no está
ganando: revisar que `AddInMemoryCollection` sea la última fuente registrada.

- [ ] **Paso 6: Commit**

```bash
git add tests/
git commit -m "test: ApiFactory y prueba de arranque contra /health"
```

---

## Tarea 5: Base de datos real, limpieza entre pruebas y tokens

**Archivos:**
- Crear: `tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs`
- Crear: `tests/CoopagcuyApi.Tests/Infra/Jwt.cs`
- Modificar: `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`
- Crear: `tests/CoopagcuyApi.Tests/Integracion/ArranqueBaseDatosTests.cs`

**Interfaces:**
- Consume: `ApiFactory`, `ColeccionApi.Nombre` (Tarea 4).
- Produce, y de esto dependen todas las pruebas de las Fases 2 y 3:
  - `ApiFactory.LimpiarAsync()` → `Task`
  - `ApiFactory.NuevoDbContext()` → `AppDbContext` (el llamador lo libera)
  - `ApiFactory.ComoAnonimo()` → `HttpClient`
  - `ApiFactory.ComoAdmin()` → `HttpClient`
  - `ApiFactory.ComoOperadorCat(string cat)` → `HttpClient`
  - `ApiFactory.ComoOperadorFaenamiento()` → `HttpClient`

- [ ] **Paso 1: Confirmar los nombres reales de los claims y roles**

```bash
cat CoopagcuyApi/Common/Auth/JwtTokenService.cs
grep -rn "IsInRole\|Roles = " CoopagcuyApi/Common/Auth/AlcanceUsuario.cs
```

Anotar el nombre exacto del claim de rol, del claim `cat` y del claim `cedula`.
El Paso 3 asume `ClaimTypes.Role`, `"cat"` y `"cedula"`; **si difieren, usar los
reales.** Un token con un claim mal nombrado produce 403 en todas las pruebas
posteriores y cuesta horas de diagnosticar.

- [ ] **Paso 2: Escribir el test que debe fallar**

Crear `tests/CoopagcuyApi.Tests/Integracion/ArranqueBaseDatosTests.cs`:

```csharp
using System.Net;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

// Verifica el andamiaje, no reglas de negocio: que las migraciones se apliquen,
// que Respawn limpie entre pruebas y que los tokens de prueba sean aceptados.
// Si esta clase está en verde, la Fase 3 puede escribirse sin sorpresas.
[Collection(ColeccionApi.Nombre)]
public class ArranqueBaseDatosTests(ApiFactory api) : IAsyncLifetime
{
    [Fact]
    public async Task LasMigraciones_seAplicaron_completas()
    {
        await using var db = api.NuevoDbContext();

        var pendientes = await db.Database.GetPendingMigrationsAsync();

        pendientes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Respawn_dejaLaBaseVacia_antesDeCadaPrueba()
    {
        await using var db = api.NuevoDbContext();

        // InitializeAsync ya corrió la limpieza
        (await db.Productoras.CountAsync()).ShouldBe(0);
        (await db.Lotes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task EndpointProtegido_sinToken_responde401()
    {
        var respuesta = await api.ComoAnonimo().GetAsync("/api/productoras");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EndpointProtegido_conTokenDeAdmin_responde200()
    {
        var respuesta = await api.ComoAdmin().GetAsync("/api/productoras");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}
```

- [ ] **Paso 3: Crear el emisor de tokens**

Crear `tests/CoopagcuyApi.Tests/Infra/Jwt.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Emite tokens firmados con la misma clave, emisor y audiencia que configura
/// <see cref="ApiFactory"/>. Se firman aquí en vez de llamar a /api/auth/login
/// para no depender de usuarios sembrados y para no gastar el cupo del rate
/// limiter de "auth" (10 peticiones por minuto y por IP: todas las pruebas
/// salen de la misma IP y lo agotarían).
/// </summary>
public static class Jwt
{
    // Deben coincidir con appsettings.json
    private const string Emisor = "CoopagcuyApi";
    private const string Audiencia = "CoopagcuyFrontend";

    public static string Emitir(string rol, string? cat = null,
        string cedula = "0102030405")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, rol),
            new("cedula", cedula),
            new(JwtRegisteredClaimNames.Sub, "1")
        };

        if (cat is not null)
            claims.Add(new Claim("cat", cat));

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.ClaveJwt)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Emisor,
            audience: Audiencia,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Paso 4: Crear el fixture de base de datos**

Crear `tests/CoopagcuyApi.Tests/Infra/BaseDatosFixture.cs`:

```csharp
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
```

- [ ] **Paso 5: Conectar el fixture y los clientes a `ApiFactory`**

En `tests/CoopagcuyApi.Tests/Infra/ApiFactory.cs`, cambiar la declaración de la
clase para que implemente `IAsyncLifetime`:

```csharp
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
```

Añadir estos `using` al principio del archivo:

```csharp
using System.Net.Http.Headers;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
```

Y añadir estos miembros dentro de la clase, después de `ConfigureWebHost`:

```csharp
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

    public HttpClient ComoAnonimo() => CreateClient();

    public HttpClient ComoAdmin() => ClienteCon(Jwt.Emitir("AdminCooperativa"));

    public HttpClient ComoOperadorCat(string cat) =>
        ClienteCon(Jwt.Emitir("OperadorCAT", cat));

    public HttpClient ComoOperadorFaenamiento() =>
        ClienteCon(Jwt.Emitir("OperadorFaenamiento"));

    private HttpClient ClienteCon(string token)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }
```

- [ ] **Paso 6: Verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed!  - Failed: 0, Passed: 10`.

Diagnóstico si falla:
- `relation "..." does not exist` → las migraciones no corrieron; revisar que la
  Tarea 1 se completó y que `develop` incluye `SesionesYVinculacionOffline`.
- `401` donde se esperaba `200` → los claims del Paso 1 no coinciden con los
  reales; corregir `Jwt.cs`.
- `Cannot write DateTime with Kind=Unspecified` → falta el `AppContext.SetSwitch`
  en `BaseDatosFixture.InicializarAsync`.

- [ ] **Paso 7: Commit**

```bash
git add tests/
git commit -m "test: fixture de Postgres con Respawn y clientes autenticados"
```

---

## Tarea 6: Documentar cómo se corren las pruebas

**Archivos:**
- Crear: `docs/PRUEBAS.md`

**Interfaces:**
- Consume: los comandos de las Tareas 3 y 5.
- Produce: nada de código.

- [ ] **Paso 1: Escribir el documento**

Crear `docs/PRUEBAS.md`:

```markdown
# Cómo correr las pruebas

## Requisito

Docker Desktop en marcha. **No** se ejecuta `dotnet test` directamente en
Windows: Smart App Control bloquea la carga del DLL recién compilado desde
OneDrive (error `0x800711C7`).

## Batería completa

    docker compose -f docker-compose.tests.yml run --rm tests

La primera corrida tarda varios minutos descargando paquetes NuGet. Las
siguientes reutilizan el volumen `nuget-cache`.

## Una sola clase

    docker compose -f docker-compose.tests.yml run --rm tests \
      dotnet test CoopagcuyApi.slnx --filter "FullyQualifiedName~DespachoTests"

## Bajar el Postgres al terminar

    docker compose -f docker-compose.tests.yml down -v

## Cómo está montado

- `postgres:16-alpine` efímero (`tmpfs`), publicado en el puerto **5433** del
  host para no chocar con un Postgres instalado.
- Las migraciones se aplican una vez por corrida; entre pruebas se truncan las
  tablas con Respawn, excepto `__EFMigrationsHistory`.
- Las clases de prueba comparten una sola `ApiFactory` y por tanto **no corren
  en paralelo**: comparten base de datos.
- Los tokens se firman en el propio test (`Infra/Jwt.cs`) en vez de llamar a
  `/api/auth/login`, para no agotar el rate limiter de `auth` (10/min por IP).
- Las pruebas **nunca** tocan Neon. La cadena sale de `TEST_DB_CONNECTION`.
```

- [ ] **Paso 2: Commit**

```bash
git add docs/PRUEBAS.md
git commit -m "docs: guia de ejecucion de pruebas"
```

---

## Tarea 7: Ejecutar las pruebas en CI

**Archivos:**
- Modificar: `.github/workflows/deploy.yml` (job `build-test`)

**Interfaces:**
- Consume: `CoopagcuyApi.slnx` con el proyecto de tests (Tarea 2).
- Produce: el check `Desplegar API / build-test`, del que ya depende el job
  `deploy` y que la Fase 5 marcará como obligatorio.

**Por qué no un `ci.yml` aparte.** Sería más ordenado conceptualmente, pero
GitHub Actions no encadena workflows de forma nativa: `deploy` perdería su
`needs: build-test` y habría que orquestarlo con una acción de terceros que
sondea el estado del check. Se amplía el job que ya existe y la dependencia
sigue funcionando sola.

- [ ] **Paso 1: Añadir el Postgres de servicio al job `build-test`**

En `.github/workflows/deploy.yml`, sustituir el job `build-test` completo
(desde `  build-test:` hasta `      - run: dotnet build -c Release --no-restore`)
por:

```yaml
  build-test:
    runs-on: ubuntu-latest

    # Mismo Postgres que docker-compose.tests.yml, publicado en 5433 para que
    # la cadena por defecto del código de pruebas también sirva aquí.
    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_USER: postgres
          POSTGRES_PASSWORD: postgres
          POSTGRES_DB: coopagcuy_test
        ports:
          - 5433:5432
        options: >-
          --health-cmd "pg_isready -U postgres -d coopagcuy_test"
          --health-interval 3s
          --health-timeout 3s
          --health-retries 20

    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      # La solución, no solo el csproj: así se compila también el proyecto
      # de pruebas
      - run: dotnet restore CoopagcuyApi.slnx
      - run: dotnet build CoopagcuyApi.slnx -c Release --no-restore

      - name: Pruebas
        env:
          TEST_DB_CONNECTION: "Host=localhost;Port=5433;Database=coopagcuy_test;Username=postgres;Password=postgres"
        run: >-
          dotnet test CoopagcuyApi.slnx -c Release --no-build
          --logger "trx;LogFileName=resultados.trx"
          --results-directory artefactos

      # Se suben también cuando fallan: es justo cuando hacen falta
      - name: Publicar resultados
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: resultados-pruebas
          path: artefactos/
```

No tocar el job `deploy`: su `needs: build-test` ya hace que un test en rojo
detenga el despliegue.

- [ ] **Paso 2: Comprobar que el job quedó bien formado**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs | keys' .github/workflows/deploy.yml
```

Esperado:
```
- build-test
- deploy
```

- [ ] **Paso 3: Comprobar que el despliegue sigue dependiendo de las pruebas**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs.deploy.needs' .github/workflows/deploy.yml
```

Esperado: `build-test`.

- [ ] **Paso 4: Comprobar que el paso de pruebas existe**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs["build-test"].steps | map(.name) | .[]' .github/workflows/deploy.yml
```

Esperado: la lista incluye `Pruebas` y `Publicar resultados`.

- [ ] **Paso 5: Commit**

```bash
git add .github/workflows/deploy.yml
git commit -m "ci: ejecutar las pruebas con Postgres de servicio en el pipeline"
```

---

## Tarea 8: Bloqueo de dependencias NuGet

Sin `packages.lock.json`, `trivy fs` no ve el árbol transitivo de NuGet y la
Tarea 12 no auditaría nada del backend.

**Archivos:**
- Modificar: `CoopagcuyApi.csproj`
- Crear: `packages.lock.json` (generado)
- Crear: `tests/CoopagcuyApi.Tests/packages.lock.json` (generado)

**Interfaces:**
- Produce: `packages.lock.json` en la raíz, que la Tarea 12 escanea.

- [ ] **Paso 1: Activar el bloqueo**

En `CoopagcuyApi.csproj`, dentro del primer `<PropertyGroup>`, añadir después de
`<UserSecretsId>`:

```xml
    <!-- Genera packages.lock.json: hace reproducibles los restore y permite
         que Trivy audite las dependencias transitivas de NuGet -->
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
```

Añadir la misma línea en el `<PropertyGroup>` de
`tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`.

- [ ] **Paso 2: Generar los archivos de bloqueo**

```bash
docker compose -f docker-compose.tests.yml run --rm --no-deps tests dotnet restore CoopagcuyApi.slnx
```

Esperado: aparecen `packages.lock.json` y
`tests/CoopagcuyApi.Tests/packages.lock.json`.

- [ ] **Paso 3: Confirmar que no están ignorados por git**

```bash
git check-ignore -v packages.lock.json; echo "codigo de salida: $?"
```

Esperado: `codigo de salida: 1` (no ignorado). Si sale `0`, el `.gitignore`
tiene una regla que lo excluye: añadir `!packages.lock.json` al final del
`.gitignore`.

- [ ] **Paso 4: Verificar que los tests siguen pasando**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed!  - Failed: 0, Passed: 10`.

Si falla con `NU1004` (el lock está desactualizado), regenerarlo con
`dotnet restore --force-evaluate`.

- [ ] **Paso 5: Commit**

```bash
git add CoopagcuyApi.csproj tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj packages.lock.json tests/CoopagcuyApi.Tests/packages.lock.json
git commit -m "build: bloqueo de dependencias NuGet para auditoria reproducible"
```

---

## Tarea 9: Endurecer la imagen Docker

**Archivos:**
- Modificar: `Dockerfile`
- Modificar: `.dockerignore`

**Interfaces:**
- Produce: la imagen que escanea la Tarea 13.

- [ ] **Paso 1: Excluir los tests del contexto de imagen**

En `.dockerignore`, añadir bajo el bloque
`# Infraestructura y documentación: no son parte del runtime`:

```
tests/
docker-compose.tests.yml
```

- [ ] **Paso 2: Ejecutar la aplicación sin privilegios**

En `Dockerfile`, sustituir el bloque desde `ENV ASPNETCORE_ENVIRONMENT=Production`
hasta el final por:

```dockerfile
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Correr como root dentro del contenedor no aporta nada aquí y amplía el daño
# de cualquier ejecución remota de código. La imagen aspnet:8.0 ya trae el
# usuario "app" (UID 1654) creado por Microsoft.
USER app

ENTRYPOINT ["dotnet", "CoopagcuyApi.dll"]
```

- [ ] **Paso 3: Construir la imagen**

```bash
docker build -t coopagcuy-api:local .
```

Esperado: `naming to docker.io/library/coopagcuy-api:local` sin errores.

Si falla con `unable to find user app`, la imagen base no trae ese usuario:
sustituir `USER app` por, justo antes de él,
`RUN useradd --uid 1654 --create-home app` y mantener `USER app`.

- [ ] **Paso 4: Verificar que arranca y responde**

```bash
docker run --rm -d -p 8081:8080 -e Jwt__Key=clave-de-pruebas-coopagcuy-32-chars!! -e ConnectionStrings__NeonDb="Host=nohay;Database=x;Username=x;Password=x" --name coopagcuy-humo coopagcuy-api:local
```

Esperar 5 segundos y comprobar:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8081/health
```

Esperado: `200`. `/health` no toca la base, así que la cadena falsa no importa.

- [ ] **Paso 5: Confirmar que no corre como root**

```bash
docker exec coopagcuy-humo id -u
```

Esperado: `1654` (no `0`).

- [ ] **Paso 6: Limpiar**

```bash
docker rm -f coopagcuy-humo
```

- [ ] **Paso 7: Commit**

```bash
git add Dockerfile .dockerignore
git commit -m "build: la imagen corre sin privilegios y excluye los tests"
```

---

## Tarea 10: CodeQL en el API

**Archivos:**
- Crear: `.github/workflows/codeql.yml`

**Interfaces:**
- Produce: el check `CodeQL / analizar (csharp)`.

- [ ] **Paso 1: Crear el workflow**

Crear `.github/workflows/codeql.yml`:

```yaml
name: CodeQL

# En PR bloquea; en push a main solo publica. El cron existe porque las reglas
# de CodeQL mejoran con el tiempo: código que hoy pasa puede no pasar mañana.
on:
  push:
    branches: [main]
  pull_request:
    branches: [develop, main]
  schedule:
    - cron: "0 6 * * 1"   # lunes 06:00 UTC
  workflow_dispatch:

jobs:
  analizar:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read
      actions: read

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      - name: Inicializar CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: csharp
          # Manual y no autobuild: la solución es un .slnx, formato nuevo que
          # el autobuild puede no resolver
          build-mode: manual
          queries: security-extended

      - name: Compilar
        run: |
          dotnet restore CoopagcuyApi.csproj
          dotnet build CoopagcuyApi.csproj -c Release --no-restore

      - name: Analizar
        uses: github/codeql-action/analyze@v3
        with:
          category: "/language:csharp"
```

Nota: se compila solo `CoopagcuyApi.csproj`, no la solución. El código de
pruebas no se despliega y sus hallazgos serían ruido.

- [ ] **Paso 2: Validar la sintaxis**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs.analizar.steps | length' .github/workflows/codeql.yml
```

Esperado: `5`.

- [ ] **Paso 3: Commit**

```bash
git add .github/workflows/codeql.yml
git commit -m "ci: analisis estatico con CodeQL sobre el API"
```

---

## Tarea 11: CodeQL en el front

**Archivos:**
- Crear: `coopagcuy-frontend/.github/workflows/codeql.yml`

**Interfaces:**
- Produce: el check `CodeQL / analizar (javascript-typescript)` en el repo del
  front.

- [ ] **Paso 1: Crear el workflow**

Crear `.github/workflows/codeql.yml` en el repo `coopagcuy-frontend`:

```yaml
name: CodeQL

on:
  push:
    branches: [main]
  pull_request:
    branches: [develop, main]
  schedule:
    - cron: "30 6 * * 1"   # lunes 06:30 UTC, escalonado respecto al API
  workflow_dispatch:

jobs:
  analizar:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read
      actions: read

    steps:
      - uses: actions/checkout@v4

      - name: Inicializar CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: javascript-typescript
          # TypeScript no necesita compilarse para el análisis
          build-mode: none
          queries: security-extended

      - name: Analizar
        uses: github/codeql-action/analyze@v3
        with:
          category: "/language:javascript-typescript"
```

- [ ] **Paso 2: Validar la sintaxis**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs.analizar.steps | length' .github/workflows/codeql.yml
```

Esperado: `3`.

- [ ] **Paso 3: Commit (en el repo del front)**

```bash
git add .github/workflows/codeql.yml
git commit -m "ci: analisis estatico con CodeQL sobre el front"
```

---

## Tarea 12: Trivy y auditoría de dependencias en el API

**Archivos:**
- Crear: `.github/workflows/seguridad.yml`

**Interfaces:**
- Consume: `packages.lock.json` (Tarea 8).
- Produce: el check `Seguridad / escaneo`.

- [ ] **Paso 1: Crear el workflow**

Crear `.github/workflows/seguridad.yml`:

```yaml
name: Seguridad

on:
  push:
    branches: [develop, main]
  pull_request:
    branches: [develop, main]
  schedule:
    - cron: "0 7 * * 1"
  workflow_dispatch:

jobs:
  escaneo:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read

    steps:
      - uses: actions/checkout@v4

      # ── Dependencias vulnerables (NuGet) ──────────────────────────────
      # Falla solo en PR: en push a main queremos enterarnos, no quedarnos
      # sin poder desplegar un arreglo urgente.
      - name: Trivy sobre el sistema de archivos
        uses: aquasecurity/trivy-action@0.28.0
        with:
          scan-type: fs
          scan-ref: .
          scanners: vuln
          severity: HIGH,CRITICAL
          ignore-unfixed: true
          format: sarif
          output: trivy-fs.sarif
          exit-code: ${{ github.event_name == 'pull_request' && '1' || '0' }}

      # ── Secretos commiteados ──────────────────────────────────────────
      # Cualquier hallazgo bloquea, incluso en push: una cadena de Neon o una
      # Jwt:Key en el historial es una emergencia, no un aviso.
      - name: Trivy sobre secretos
        uses: aquasecurity/trivy-action@0.28.0
        with:
          scan-type: fs
          scan-ref: .
          scanners: secret
          format: table
          exit-code: "1"

      # ── Malas prácticas del Dockerfile ────────────────────────────────
      - name: Trivy sobre configuración
        uses: aquasecurity/trivy-action@0.28.0
        with:
          scan-type: config
          scan-ref: .
          format: table
          exit-code: "0"     # informativo

      # Se sube siempre, también si el paso de fs falló: el reporte es
      # justamente lo que hace falta para diagnosticar.
      - name: Publicar hallazgos
        if: always()
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: trivy-fs.sarif
          category: trivy-fs

      # ── Segunda opinión con la herramienta nativa de .NET ─────────────
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"
      - name: Auditoría de paquetes NuGet
        # `if` explícito y no `[ ... ] && exit 1`: el shell de Actions corre
        # con `bash -e` y una comparación falsa en una lista `&&` es una
        # fuente clásica de gates que fallan (o no) por accidente.
        run: |
          dotnet restore CoopagcuyApi.slnx
          salida=$(dotnet list CoopagcuyApi.slnx package --vulnerable --include-transitive)
          echo "$salida"
          {
            echo '### Paquetes NuGet vulnerables'
            echo '```'
            echo "$salida"
            echo '```'
          } >> "$GITHUB_STEP_SUMMARY"

          if echo "$salida" | grep -q "has the following vulnerable packages"; then
            echo "Se encontraron paquetes vulnerables."
            if [ "${{ github.event_name }}" = "pull_request" ]; then
              exit 1
            fi
          fi
```

- [ ] **Paso 2: Probar el escaneo de secretos en local antes de subirlo**

```bash
docker run --rm -v "$PWD:/w" -w /w aquasec/trivy:latest fs --scanners secret --exit-code 0 .
```

Esperado: `Total: 0`. **Si aparece algún secreto, DETENER el plan y avisar al
usuario**: hay una credencial en el árbol de trabajo o en el historial y eso se
atiende antes que nada (rotar la credencial primero, limpiar después).

- [ ] **Paso 3: Probar el escaneo de configuración**

```bash
docker run --rm -v "$PWD:/w" -w /w aquasec/trivy:latest config --exit-code 0 .
```

Esperado: el hallazgo de usuario root **ya no aparece** (lo arregló la Tarea 9).
Anotar los hallazgos que queden; son informativos.

- [ ] **Paso 4: Validar la sintaxis**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs.escaneo.steps | length' .github/workflows/seguridad.yml
```

Esperado: `7`.

- [ ] **Paso 5: Commit**

```bash
git add .github/workflows/seguridad.yml
git commit -m "ci: escaneo Trivy de dependencias, secretos y configuracion"
```

---

## Tarea 13: Escanear la imagen antes de publicarla

Hoy `docker/build-push-action` construye y publica en un solo paso, así que no
hay dónde insertar el escaneo. Se parte en construir → escanear → publicar.

**Archivos:**
- Modificar: `.github/workflows/deploy.yml`

**Interfaces:**
- Consume: el `Dockerfile` endurecido (Tarea 9).
- Produce: el gate que impide publicar una imagen con CVEs parcheables.

- [ ] **Paso 1: Sustituir el paso de construcción y publicación**

En `.github/workflows/deploy.yml`, reemplazar el paso
`- name: Construir y publicar imagen` completo por estos tres:

```yaml
      # Se construye SIN publicar para poder escanear antes de exponerla
      - name: Construir imagen
        uses: docker/build-push-action@v6
        with:
          context: .
          push: false
          load: true
          tags: ${{ env.IMAGE }}:${{ github.sha }}

      - name: Trivy sobre la imagen
        uses: aquasecurity/trivy-action@0.28.0
        with:
          image-ref: ${{ env.IMAGE }}:${{ github.sha }}
          severity: HIGH,CRITICAL
          # Sin esto, una CVE de Debian sin parche disponible dejaría al
          # proyecto sin poder desplegar ni siquiera un hotfix
          ignore-unfixed: true
          format: table
          exit-code: ${{ github.event_name == 'pull_request' && '1' || '0' }}

      - name: Publicar imagen
        run: |
          docker tag ${{ env.IMAGE }}:${{ github.sha }} ${{ env.IMAGE }}:${{ github.ref_name }}
          docker push ${{ env.IMAGE }}:${{ github.sha }}
          docker push ${{ env.IMAGE }}:${{ github.ref_name }}
```

- [ ] **Paso 2: Reproducir el escaneo en local**

```bash
docker build -t coopagcuy-api:local . && docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:latest image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 0 coopagcuy-api:local
```

Anotar cuántos HIGH/CRITICAL parcheables aparecen. Si hay alguno, el gate
bloquearía los PR: resolverlo actualizando la imagen base o el paquete señalado
**antes** de continuar, o el equipo aprenderá a ignorar el check.

- [ ] **Paso 3: Validar la sintaxis**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs.deploy.steps | map(.name) | .[]' .github/workflows/deploy.yml
```

Esperado: la lista incluye `Construir imagen`, `Trivy sobre la imagen` y
`Publicar imagen`, en ese orden.

- [ ] **Paso 4: Commit**

```bash
git add .github/workflows/deploy.yml
git commit -m "ci: escaneo Trivy de la imagen antes de publicarla en ghcr"
```

---

## Tarea 14: Lint y auditoría en el CI del front

Hoy `pnpm lint` existe como script pero el pipeline solo ejecuta `pnpm build`.

**Archivos:**
- Modificar: `coopagcuy-frontend/.github/workflows/deploy.yml`

**Interfaces:**
- Produce: los pasos `Lint` y `Auditoría de dependencias` en el job existente.

- [ ] **Paso 1: Insertar los pasos**

En `.github/workflows/deploy.yml` del front, entre
`      - run: pnpm install --frozen-lockfile` y `      - name: Compilar`,
añadir:

```yaml
      - name: Lint
        run: pnpm lint

      # --prod: solo lo que se sirve al navegador. Las devDependencies
      # vulnerables no llegan al usuario y bloquearlas sería ruido.
      - name: Auditoría de dependencias
        run: pnpm audit --prod --audit-level high
```

- [ ] **Paso 2: Comprobar que el lint pasa hoy**

```bash
cd coopagcuy-frontend && pnpm lint
```

Esperado: sin errores. **Si falla, arreglar los hallazgos en este mismo commit**;
añadir un gate que ya está en rojo bloquea todos los PR desde el primer día.

- [ ] **Paso 3: Comprobar la auditoría**

```bash
cd coopagcuy-frontend && pnpm audit --prod --audit-level high
```

Esperado: `No known vulnerabilities found`. Si aparece alguna, actualizar el
paquete señalado y volver a ejecutar.

- [ ] **Paso 4: Validar la sintaxis**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.jobs["build-deploy"].steps | map(.name) | .[]' .github/workflows/deploy.yml
```

Esperado: la lista incluye `Lint` y `Auditoría de dependencias` antes de
`Compilar`.

- [ ] **Paso 5: Commit (en el repo del front)**

```bash
git add .github/workflows/deploy.yml
git commit -m "ci: lint y auditoria de dependencias como gate del front"
```

---

## Tarea 15: Dependabot en ambos repos

**Archivos:**
- Crear: `.github/dependabot.yml` (API)
- Crear: `coopagcuy-frontend/.github/dependabot.yml`

**Interfaces:**
- Consume: `packages.lock.json` (Tarea 8) para que el ecosistema `nuget`
  resuelva el árbol completo.

- [ ] **Paso 1: Crear el de la API**

Crear `.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
      day: monday
    open-pull-requests-limit: 5
    target-branch: develop
    commit-message:
      prefix: "deps"
    groups:
      # Un solo PR para todo el stack de Microsoft: subirlos por separado
      # genera conflictos de versión entre paquetes que van acoplados
      microsoft:
        patterns:
          - "Microsoft.*"
          - "System.*"
    ignore:
      # El proyecto está fijado a net8.0; los majors de EF Core exigen
      # migrar el TargetFramework y eso es una decisión, no un bump
      - dependency-name: "Microsoft.EntityFrameworkCore*"
        update-types: ["version-update:semver-major"]

  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: weekly
      day: monday
    target-branch: develop
    commit-message:
      prefix: "ci"

  - package-ecosystem: docker
    directory: "/"
    schedule:
      interval: weekly
      day: monday
    target-branch: develop
    commit-message:
      prefix: "build"
```

- [ ] **Paso 2: Crear el del front**

Crear `.github/dependabot.yml` en `coopagcuy-frontend`:

```yaml
version: 2
updates:
  - package-ecosystem: npm
    directory: "/"
    schedule:
      interval: weekly
      day: monday
    open-pull-requests-limit: 5
    target-branch: develop
    commit-message:
      prefix: "deps"
    groups:
      react:
        patterns:
          - "react"
          - "react-dom"
          - "@types/react"
          - "@types/react-dom"
      eslint:
        patterns:
          - "eslint*"
          - "@eslint/*"
          - "typescript-eslint"

  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: weekly
      day: monday
    target-branch: develop
    commit-message:
      prefix: "ci"
```

- [ ] **Paso 3: Validar la sintaxis de ambos**

```bash
docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e '.updates | map(.package-ecosystem) | .[]' .github/dependabot.yml
```

Esperado en el API: `nuget`, `github-actions`, `docker`. En el front: `npm`,
`github-actions`.

- [ ] **Paso 4: Commit en cada repo**

```bash
git add .github/dependabot.yml
git commit -m "ci: actualizaciones automaticas de dependencias con Dependabot"
```

---

## Verificación final de las fases 0 y 1

- [ ] **Paso 1: Batería completa en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed!  - Failed: 0, Passed: 10`.

- [ ] **Paso 2: La imagen construye, arranca y no corre como root**

```bash
docker build -t coopagcuy-api:local . && docker run --rm --entrypoint id coopagcuy-api:local -u
```

Esperado: `1654`.

- [ ] **Paso 3: Todos los workflows son YAML válido**

```bash
for f in .github/workflows/*.yml .github/dependabot.yml; do docker run --rm -v "$PWD:/w" -w /w mikefarah/yq:4 e 'true' "$f" > /dev/null && echo "OK  $f" || echo "MAL $f"; done
```

Esperado: `OK` en `codeql.yml`, `seguridad.yml`, `deploy.yml` y
`dependabot.yml`.

- [ ] **Paso 4: Sin secretos en el árbol**

```bash
docker run --rm -v "$PWD:/w" -w /w aquasec/trivy:latest fs --scanners secret --exit-code 1 .
```

Esperado: código de salida `0` y `Total: 0`.

- [ ] **Paso 5: Bajar todo**

```bash
docker compose -f docker-compose.tests.yml down -v
```

- [ ] **Paso 6: Preguntar al usuario antes de publicar**

Nada se ha empujado todavía. Resumirle qué commits hay en cada repo y esperar
su decisión: el primer push a `develop` dispara despliegue a staging, aplica la
migración `SesionesYVinculacionOffline` a la rama Neon de staging, y ejecuta por
primera vez CodeQL y Trivy (que pueden tardar y arrojar hallazgos iniciales).

---

## Qué queda fuera de estas dos fases

Cada una tendrá su propio plan, escrito cuando esta esté en verde:

- **Fase 2** — Unitarias del API (`ValidadorCedula` completo, `AlcanceUsuario`,
  `FechaUtc`), `cedulas.json` compartido con verificación cruzada, y Vitest en
  el front.
- **Fase 3** — Las diez clases de integración: locks, idempotencia, stock, tope
  de devolución, sesiones, segmentación por CAT y reportes. Requiere
  `Semillas.cs`, que no se construye aquí porque su forma la dictan los tests
  que la usan.
- **Fase 4** — Playwright: cinco flujos y el workflow reutilizable
  (`workflow_call`) invocado desde ambos repos. Requiere HTTPS con `mkcert`.
- **Fase 5** — Branch protection y activación de los gates como checks
  obligatorios.

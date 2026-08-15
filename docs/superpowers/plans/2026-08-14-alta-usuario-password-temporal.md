# Contraseña temporal al crear la cuenta — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que ningún administrador llegue a conocer la contraseña con la que un usuario opera el sistema — ni al crear su cuenta, ni editándola después.

**Architecture:** Se cierran las dos puertas por las que hoy un administrador elige una contraseña (`CrearUsuarioDto.Password` y `ActualizarUsuarioDto.NuevaPassword`) y se sustituyen por una contraseña temporal que genera el sistema. Las tres rutas que asignan una temporal —alta, resolución de solicitud y restablecimiento por iniciativa del administrador— comparten `CredencialTemporal.Asignar`, un ayudante puro que no toca la base de datos. El restablecimiento proactivo queda auditado en la tabla que ya existe, con una columna `Origen` nueva.

**Tech Stack:** ASP.NET Core 8, EF Core + Npgsql (PostgreSQL), BCrypt.Net, xUnit + Shouldly + Respawn; React 19 + TypeScript + Vite + TanStack Query + Tailwind.

**Especificación de referencia:** `docs/superpowers/specs/2026-08-14-alta-usuario-password-temporal-design.md`

**Depende de:** el trabajo de `2026-08-14-recuperacion-password.md`, que debe estar aplicado en el árbol de trabajo. Este plan reutiliza `GeneradorPasswordTemporal`, `PoliticaPassword`, `Usuario.DebeCambiarPassword`, `SolicitudesRestablecerPassword`, `PasswordTemporalDto` y la pantalla `/cambiar-password`.

## Global Constraints

- **Idioma:** todo comentario, nombre de prueba, mensaje de commit y texto de interfaz va en español, siguiendo el estilo del repositorio.
- **Nunca ejecutar `dotnet test` ni `dotnet ef` directamente en Windows.** Smart App Control bloquea la carga del DLL recién compilado desde OneDrive (error `0x800711C7`). Todo pasa por Docker.
- **Comando único de pruebas del API:** `docker compose -f docker-compose.tests.yml run --rm tests`. Ejecuta la batería completa; no hay forma de correr una sola prueba desde Windows.
- **El SDK 8 no entiende `.slnx`** (`MSB4068`). Todo comando `dotnet` apunta a `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`.
- **Prohibido FluentAssertions** (licencia comercial desde la v7). Se usa **Shouldly**.
- **Las cédulas de prueba no se pueden inventar.** `ValidadorCedula` exige provincia 01–24, tercer dígito < 6 y dígito verificador correcto. Válidas verificadas: **`0104576277`**, **`0111223343`**, **`0102030400`**. Inválida por verificador: **`0104576270`**.
- **Respawn trunca sin `RESTART IDENTITY`:** ninguna prueba puede asumir `Id == 1`. Capturar siempre el Id devuelto al sembrar.
- **Cada cliente de prueba lleva su propia IP** (`ApiFactory.ClienteCon` con `X-Forwarded-For`). No revertir: sin ello la batería agota el rate limiter compartido de la política `auth`.
- **`Npgsql.EnableLegacyTimestampBehavior`** debe seguir activo en `AppDbContextFactory`, o la migración generará un `AlterColumn` masivo de todas las columnas de fecha.
- **Dos repositorios:** las tareas 1–4 son de `C:\Users\nicol\OneDrive\Documents\CoopagcuyApi`; las 5–8 de `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`. Las rutas de cada tarea son relativas a su repositorio.
- **El front no tiene runner de pruebas.** Sus tareas se verifican con `pnpm build` (`tsc -b && vite build`), `pnpm lint` y verificación en navegador.
- **Commits:** el usuario pidió expresamente **no hacer commits**. Cada tarea termina en verificación, no en `git commit`. No hacer `push` en ningún caso.
- **No tocar `/api/auth/setup` ni `AuthService.CrearUsuarioInicialAsync`.** Ese endpoint crea el administrador inicial con `Setup:Key` y conserva su parámetro de contraseña a propósito: quien ejecuta la instalación **es** el usuario que se está creando, así que no hay nadie a quien dictarle una temporal. Está fuera de alcance por decisión del diseño (§6 del spec), no por olvido.

---

## Estructura de archivos

### API — `CoopagcuyApi`

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `Common/Auth/Recuperacion/CredencialTemporal.cs` | **Crear.** Ayudante puro que asigna una temporal a un usuario | 1 |
| `Common/Auth/Recuperacion/RecuperacionService.cs` | **Modificar.** Usa el ayudante; método nuevo de restablecimiento proactivo | 1, 4 |
| `Common/Auth/Recuperacion/SolicitudRestablecerPassword.cs` | **Modificar.** Enum y propiedad `Origen` | 2 |
| `Common/Auth/Recuperacion/RecuperacionDtos.cs` | **Modificar.** `Origen` en `SolicitudPasswordDto` | 2 |
| `Infrastructure/Data/AppDbContext.cs` | **Modificar.** Mapeo de `Origen` | 2 |
| `Infrastructure/Data/Migrations/*_OrigenSolicitudPassword.cs` | **Generar.** Migración aditiva de una columna | 2 |
| `Common/Auth/UsuarioDtos.cs` | **Modificar.** Sin `Password` ni `NuevaPassword`; `UsuarioCreadoDto` nuevo | 3 |
| `Common/Auth/UsuarioService.cs` | **Modificar.** El alta genera la temporal; la edición ya no toca la contraseña | 3 |
| `Common/Auth/UsuariosController.cs` | **Modificar.** Devuelve `UsuarioCreadoDto` | 3 |
| `Common/Auth/Recuperacion/RecuperacionController.cs` | **Modificar.** Endpoint del restablecimiento proactivo | 4 |
| `tests/CoopagcuyApi.Tests/Unitarias/CredencialTemporalTests.cs` | **Crear.** | 1 |
| `tests/CoopagcuyApi.Tests/Integracion/OrigenSolicitudTests.cs` | **Crear.** | 2 |
| `tests/CoopagcuyApi.Tests/Integracion/AltaUsuarioPasswordTests.cs` | **Crear.** Las dos puertas cerradas | 3 |
| `tests/CoopagcuyApi.Tests/Integracion/RestablecerPorAdminTests.cs` | **Crear.** | 4 |

### Front — `coopagcuy-frontend`

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `src/types/recuperacion.ts` | **Modificar.** `origen` en `SolicitudPassword` | 5 |
| `src/types/admin.ts` | **Modificar.** Sin `password` ni `nuevaPassword`; `UsuarioCreado` nuevo | 5 |
| `src/api/recuperacion.ts` | **Modificar.** `restablecerPorUsuario` | 5 |
| `src/api/admin.ts` | **Modificar.** `crear` devuelve `UsuarioCreado` | 5 |
| `src/components/admin/ModalPasswordTemporal.tsx` | **Crear.** El modal, extraído para sus tres consumidores | 5 |
| `src/components/admin/SolicitudesPassword.tsx` | **Modificar.** Usa el modal extraído; columna Origen | 5, 8 |
| `src/components/admin/FormUsuario.tsx` | **Modificar.** Sin campo de contraseña; muestra la temporal al crear | 6 |
| `src/components/admin/TablaUsuarios.tsx` | **Modificar.** Botón "Restablecer" por fila | 7 |

---

## Tarea 1: El ayudante compartido `CredencialTemporal`

Hoy `ResolverAsync` genera la temporal, la hashea y activa la bandera con líneas propias. Van a hacer falta en dos sitios más, así que la regla se nombra y se muda a un solo lugar antes de reutilizarla.

**Files:**
- Create: `Common/Auth/Recuperacion/CredencialTemporal.cs`
- Modify: `Common/Auth/Recuperacion/RecuperacionService.cs`
- Test: `tests/CoopagcuyApi.Tests/Unitarias/CredencialTemporalTests.cs`

**Interfaces:**
- Consumes: `GeneradorPasswordTemporal.Generar() -> string`, `PoliticaPassword.EsValida(string?) -> bool`, `Usuario` (propiedades `PasswordHash`, `DebeCambiarPassword`)
- Produces: `CredencialTemporal.Asignar(Usuario usuario) -> string` (devuelve la contraseña en claro)

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Unitarias/CredencialTemporalTests.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Asignar una contraseña temporal es una regla de seguridad que aplican tres
/// rutas distintas (alta de usuario, resolución de solicitud y restablecimiento
/// por el administrador). Se comprueba aquí, sin base de datos, para que las
/// tres hereden la misma garantía sin repetir la verificación.
/// </summary>
public class CredencialTemporalTests
{
    private static Usuario UsuarioDePrueba() => new()
    {
        NombreCompleto = "Operadora de prueba",
        Cedula = "0104576277",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("la-anterior-1234"),
        Rol = RolUsuario.OperadorCAT,
        CatAsignado = CentroAcopio.PAT
    };

    [Fact]
    public void Asignar_dejaAlUsuarioObligadoACambiarla()
    {
        var usuario = UsuarioDePrueba();

        CredencialTemporal.Asignar(usuario);

        usuario.DebeCambiarPassword.ShouldBeTrue();
    }

    [Fact]
    public void Asignar_devuelveExactamenteLaContrasenaQueQuedaGuardada()
    {
        var usuario = UsuarioDePrueba();

        var temporal = CredencialTemporal.Asignar(usuario);

        // Si esto falla, el administrador dicta una contraseña con la que el
        // usuario no puede entrar: el peor fallo posible de esta función
        BCrypt.Net.BCrypt.Verify(temporal, usuario.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public void Asignar_invalidaLaContrasenaAnterior()
    {
        var usuario = UsuarioDePrueba();

        CredencialTemporal.Asignar(usuario);

        BCrypt.Net.BCrypt.Verify("la-anterior-1234", usuario.PasswordHash)
            .ShouldBeFalse();
    }

    [Fact]
    public void Asignar_devuelveUnaContrasenaQueCumpleLaPolitica()
    {
        for (var i = 0; i < 100; i++)
            PoliticaPassword.EsValida(CredencialTemporal.Asignar(UsuarioDePrueba()))
                .ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA en compilación con `CS0103` — `CredencialTemporal` no existe.

- [ ] **Step 3: Crear el ayudante**

Crear `Common/Auth/Recuperacion/CredencialTemporal.cs`:

```csharp
namespace CoopagcuyApi.Common.Auth.Recuperacion;

/// <summary>
/// Asigna a un usuario una contraseña temporal de un solo uso.
///
/// Es una función pura sobre la entidad: NO toca la base de datos y NO guarda
/// nada. Cada llamador decide cuándo persistir, si revoca sesiones y si deja
/// rastro de auditoría — que es justo lo que difiere entre las tres rutas que
/// la usan (alta de usuario, resolución de una solicitud y restablecimiento por
/// iniciativa del administrador). Meter esas diferencias aquí convertiría esto
/// en un método con tres banderas booleanas.
///
/// Vive aparte porque es una regla de seguridad: el día que la temporal deba
/// caducar o cambiar de formato, hay un solo sitio que tocar.
/// </summary>
public static class CredencialTemporal
{
    /// <summary>
    /// Devuelve la contraseña EN CLARO. Es la única vez que existe fuera del
    /// hash: el llamador se la entrega al administrador para que la dicte, y
    /// el sistema la olvida.
    /// </summary>
    public static string Asignar(Usuario usuario)
    {
        var temporal = GeneradorPasswordTemporal.Generar();

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporal);
        usuario.DebeCambiarPassword = true;

        return temporal;
    }
}
```

- [ ] **Step 4: Hacer que `ResolverAsync` use el ayudante**

En `Common/Auth/Recuperacion/RecuperacionService.cs`, dentro de `ResolverAsync`, sustituir estas tres líneas:

```csharp
            var temporal = GeneradorPasswordTemporal.Generar();

            solicitud.Usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporal);
            solicitud.Usuario.DebeCambiarPassword = true;
```

por esta:

```csharp
            var temporal = CredencialTemporal.Asignar(solicitud.Usuario);
```

- [ ] **Step 5: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 4 pruebas de `CredencialTemporalTests` pasan y **las 43 anteriores siguen en verde** — en particular `ResolucionPasswordTests`, que demuestra que el refactor no cambió la conducta.

---

## Tarea 2: Columna `Origen` y su migración

**Files:**
- Modify: `Common/Auth/Recuperacion/SolicitudRestablecerPassword.cs`
- Modify: `Common/Auth/Recuperacion/RecuperacionDtos.cs`
- Modify: `Common/Auth/Recuperacion/RecuperacionService.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Create: `Infrastructure/Data/Migrations/*_OrigenSolicitudPassword.cs` (generada)
- Test: `tests/CoopagcuyApi.Tests/Integracion/OrigenSolicitudTests.cs`

**Interfaces:**
- Consumes: `SolicitudRestablecerPassword`, `EstadoSolicitudPassword`
- Produces: `OrigenSolicitudPassword` (`Usuario`, `Administrador`); `SolicitudRestablecerPassword.Origen -> OrigenSolicitudPassword`; `SolicitudPasswordDto` con un parámetro posicional `string Origen` **entre `Estado` y `FechaCreacion`**

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/OrigenSolicitudTests.cs`:

```csharp
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El origen distingue "esta persona pidió el cambio" de "un administrador
/// tocó su cuenta sin que nadie se lo pidiera". Lo segundo es lo único que
/// conviene poder auditar después, así que tiene que quedar registrado.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class OrigenSolicitudTests(ApiFactory api) : IAsyncLifetime
{
    private const string Cedula = "0104576277";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnaSolicitudNueva_naceConOrigenUsuario()
    {
        await Sembrador.UsuarioAsync(api, Cedula);

        await api.ComoAnonimo().PostAsJsonAsync(
            "/api/auth/recuperacion", new { cedula = Cedula });

        await using var db = api.NuevoDbContext();
        var solicitud = await db.SolicitudesRestablecerPassword.SingleAsync();
        solicitud.Origen.ShouldBe(OrigenSolicitudPassword.Usuario);
    }

    [Fact]
    public async Task LaBandeja_exponeElOrigenDeCadaSolicitud()
    {
        await Sembrador.UsuarioAsync(api, Cedula);
        await api.ComoAnonimo().PostAsJsonAsync(
            "/api/auth/recuperacion", new { cedula = Cedula });

        var solicitudes = await api.ComoAdmin()
            .GetFromJsonAsync<List<SolicitudPasswordDto>>("/api/auth/recuperacion");

        solicitudes!.Single().Origen.ShouldBe("Usuario");
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA en compilación — no existen `OrigenSolicitudPassword` ni `SolicitudPasswordDto.Origen`.

- [ ] **Step 3: Añadir el enum y la propiedad**

En `Common/Auth/Recuperacion/SolicitudRestablecerPassword.cs`, añadir el enum después de `EstadoSolicitudPassword`:

```csharp
public enum OrigenSolicitudPassword
{
    Usuario,        // la pidió la propia persona desde la pantalla de ingreso
    Administrador   // la inició un administrador, sin que mediara solicitud
}
```

Y la propiedad dentro de la clase, después de `Estado`:

```csharp
    // Quién puso en marcha el restablecimiento. Importa para auditar: el de
    // origen Administrador es el único caso en que alguien toca la cuenta de
    // otra persona sin que esa persona lo haya pedido.
    public OrigenSolicitudPassword Origen { get; set; }
        = OrigenSolicitudPassword.Usuario;
```

- [ ] **Step 4: Mapear la columna**

En `Infrastructure/Data/AppDbContext.cs`, dentro del bloque `modelBuilder.Entity<SolicitudRestablecerPassword>(…)`, junto a la línea de `Estado`:

```csharp
            // El valor por defecto se declara aquí y no solo en el
            // inicializador de la propiedad: EF no lo lee del C#, y sin esto
            // la migración añade la columna con cadena vacía — un valor que no
            // corresponde a ningún miembro del enum y que reventaría al leer
            // las solicitudes que ya existen.
            e.Property(s => s.Origen)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(OrigenSolicitudPassword.Usuario);
```

**`HasDefaultValue` no es opcional.** Sin esa línea la migración se genera con
`defaultValue: ""`, porque EF ignora el `= OrigenSolicitudPassword.Usuario` de
la propiedad — ese inicializador solo actúa sobre objetos nuevos en memoria, no
sobre el esquema.

- [ ] **Step 5: Exponer el origen en el DTO**

En `Common/Auth/Recuperacion/RecuperacionDtos.cs`, añadir el parámetro a `SolicitudPasswordDto` **inmediatamente después de `Estado`**:

```csharp
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
    // "Usuario" o "Administrador": quién puso en marcha el restablecimiento
    string Origen,
    DateTime FechaCreacion,
    DateTime? FechaResolucion,
    string? ResueltaPor
);
```

En `Common/Auth/Recuperacion/RecuperacionService.cs`, dentro de `ListarAsync`, añadir el valor en la misma posición de la proyección — entre `s.Estado.ToString()` y `s.FechaCreacion`:

```csharp
                s.Estado.ToString(),
                s.Origen.ToString(),
                s.FechaCreacion,
```

- [ ] **Step 6: Generar la migración dentro de Docker**

Desde Git Bash, en la raíz del repositorio del API:

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=placeholder;Username=postgres;Password=postgres" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet tool install --global dotnet-ef --version 8.* >/dev/null 2>&1; export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add OrigenSolicitudPassword --project CoopagcuyApi.csproj"
```

Expected: `Done. To undo this action, use 'ef migrations remove'`.

- [ ] **Step 7: Revisar que la migración sea aditiva**

Abrir `Infrastructure/Data/Migrations/*_OrigenSolicitudPassword.cs` y confirmar que `Up()` contiene **solo** un `AddColumn<string>` sobre `SolicitudesRestablecerPassword` con `defaultValue: "Usuario"`.

**Si aparece `defaultValue: ""`**, falta el `HasDefaultValue` del paso 4. Corregirlo y rehacer la migración: borrar los dos archivos generados, restaurar el snapshot con `git checkout -- Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs` y repetir el paso 6. (`dotnet ef migrations remove` no sirve aquí: intenta conectarse a la base y falla con la cadena de marcador.)

Las filas existentes nacieron todas de una solicitud del usuario, así que el valor por defecto ya es correcto y no hace falta arreglo de datos.

**Si aparece cualquier `AlterColumn` sobre columnas de fecha**, la migración se generó sin `Npgsql.EnableLegacyTimestampBehavior`: borrar los dos archivos generados, verificar `AppDbContextFactory.cs` y repetir el paso 6.

- [ ] **Step 8: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 2 pruebas de `OrigenSolicitudTests` pasan y las anteriores siguen en verde.

---

## Tarea 3: Cerrar las dos puertas del formulario de usuario

Es la tarea que cumple el objetivo del diseño. Al terminarla, ningún administrador puede elegir la contraseña de nadie: ni al crear la cuenta ni editándola.

**Files:**
- Modify: `Common/Auth/UsuarioDtos.cs`
- Modify: `Common/Auth/UsuarioService.cs`
- Modify: `Common/Auth/UsuariosController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/AltaUsuarioPasswordTests.cs`

**Interfaces:**
- Consumes: `CredencialTemporal.Asignar(Usuario) -> string` (Tarea 1)
- Produces: `UsuarioCreadoDto(UsuarioResponseDto Usuario, string PasswordTemporal)`; `IUsuarioService.CrearAsync(CrearUsuarioDto) -> Task<UsuarioCreadoDto>`; `CrearUsuarioDto` **sin** `Password`; `ActualizarUsuarioDto` **sin** `NuevaPassword`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/AltaUsuarioPasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Las dos pruebas que dan valor a este diseño comprueban una AUSENCIA: que
/// enviar una contraseña al crear o al actualizar no tenga efecto. Un
/// formulario sin campo no demuestra eso — solo esconde la puerta; el endpoint
/// sigue ahí y se llama igual con curl.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AltaUsuarioPasswordTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaNueva = "0104576277";
    private const string CedulaExistente = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> Crear(HttpClient cliente, object cuerpo) =>
        cliente.PostAsJsonAsync("/api/usuarios", cuerpo);

    [Fact]
    public async Task Crear_devuelveUnaTemporalYObligaACambiarla()
    {
        var respuesta = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Rosa Lliguicota",
            cedula = CedulaNueva,
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var creado = await respuesta.Content.ReadFromJsonAsync<UsuarioCreadoDto>();
        PoliticaPassword.EsValida(creado!.PasswordTemporal).ShouldBeTrue();

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Cedula == CedulaNueva);

        usuario.DebeCambiarPassword.ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(creado.PasswordTemporal, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Crear_ignoraCualquierPasswordQueLleguenEnElCuerpo()
    {
        // El campo ya no existe en el DTO, pero el endpoint sigue siendo
        // llamable con curl: esto comprueba que la puerta está cerrada de
        // verdad y no solo oculta en el formulario del front
        var respuesta = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Rosa Lliguicota",
            cedula = CedulaNueva,
            password = "elegida-por-el-admin-1234",
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });

        var creado = await respuesta.Content.ReadFromJsonAsync<UsuarioCreadoDto>();

        await using var db = api.NuevoDbContext();
        var usuario = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Cedula == CedulaNueva);

        BCrypt.Net.BCrypt.Verify("elegida-por-el-admin-1234", usuario.PasswordHash)
            .ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(creado!.PasswordTemporal, usuario.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Actualizar_ignoraCualquierNuevaPasswordQueLlegue()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaExistente);

        var respuesta = await api.ComoAdmin().PutAsJsonAsync(
            $"/api/usuarios/{usuario.Id}", new
            {
                nombreCompleto = "Ana Quizhpe",
                rol = "OperadorCAT",
                catAsignado = "PAT",
                nuevaPassword = "elegida-por-el-admin-1234"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);

        // El nombre sí cambió: la edición sigue funcionando
        actualizado.NombreCompleto.ShouldBe("Ana Quizhpe");
        // La contraseña no
        BCrypt.Net.BCrypt.Verify("elegida-por-el-admin-1234", actualizado.PasswordHash)
            .ShouldBeFalse();
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, actualizado.PasswordHash)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ElUsuarioRecienCreado_entraYElSistemaLePideCambiarla()
    {
        var creacion = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Rosa Lliguicota",
            cedula = CedulaNueva,
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });
        var creado = await creacion.Content.ReadFromJsonAsync<UsuarioCreadoDto>();

        var login = await api.ComoAnonimo().PostAsJsonAsync("/api/auth/login",
            new { cedula = CedulaNueva, password = creado!.PasswordTemporal });

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sesion = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        sesion!.DebeCambiarPassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Crear_conCedulaRepetida_sigueDevolviendo409()
    {
        await Sembrador.UsuarioAsync(api, CedulaExistente);

        var respuesta = await Crear(api.ComoAdmin(), new
        {
            nombreCompleto = "Otra persona",
            cedula = CedulaExistente,
            rol = "OperadorCAT",
            catAsignado = "PAT"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA en compilación — `UsuarioCreadoDto` no existe.

- [ ] **Step 3: Ajustar los DTOs**

En `Common/Auth/UsuarioDtos.cs`, quitar la línea `public string Password { get; set; } = string.Empty;` de `CrearUsuarioDto` y la línea `public string? NuevaPassword { get; set; }` de `ActualizarUsuarioDto` (junto con su comentario `// Opcional: si viene, se restablece la contraseña`). Ambas clases quedan así:

```csharp
public class CrearUsuarioDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    // Número de cédula: identificador único de inicio de sesión
    public string Cedula { get; set; } = string.Empty;
    // Correo de contacto opcional; no sirve para iniciar sesión
    public string? Email { get; set; }
    public RolUsuario Rol { get; set; }
    // Obligatorio para OperadorCAT: centro donde puede registrar
    public CentroAcopio? CatAsignado { get; set; }
}

public class ActualizarUsuarioDto
{
    public string NombreCompleto { get; set; } = string.Empty;
    // Correo de contacto opcional (vacío = quitarlo)
    public string? Email { get; set; }
    public RolUsuario Rol { get; set; }
    public CentroAcopio? CatAsignado { get; set; }
}
```

Y añadir al final del archivo, después de `UsuarioResponseDto`:

```csharp
// Respuesta del alta. La contraseña temporal viaja UNA sola vez y no se puede
// volver a consultar: si el administrador no la anota, restablece otra desde
// la lista de usuarios.
public record UsuarioCreadoDto(
    UsuarioResponseDto Usuario,
    string PasswordTemporal
);
```

- [ ] **Step 4: Ajustar el servicio**

En `Common/Auth/UsuarioService.cs`, añadir el `using` del módulo de recuperación junto a los existentes:

```csharp
using CoopagcuyApi.Common.Auth.Recuperacion;
```

Cambiar la firma en la interfaz:

```csharp
    Task<UsuarioCreadoDto> CrearAsync(CrearUsuarioDto dto);
```

Sustituir el método `CrearAsync` completo por:

```csharp
    public async Task<UsuarioCreadoDto> CrearAsync(CrearUsuarioDto dto)
    {
        ValidarCedula(dto.Cedula);
        ValidarCatOperador(dto.Rol, dto.CatAsignado);

        var cedula = dto.Cedula.Trim();
        var existe = await db.Usuarios.AnyAsync(u => u.Cedula == cedula);
        if (existe)
            throw new InvalidOperationException(
                "Ya existe un usuario registrado con esa cédula.");

        var usuario = new Usuario
        {
            NombreCompleto = dto.NombreCompleto.Trim(),
            Cedula = cedula,
            Email = NormalizarEmail(dto.Email),
            Rol = dto.Rol,
            CatAsignado = dto.Rol == RolUsuario.OperadorCAT
                ? dto.CatAsignado : null
        };

        // La contraseña la genera el sistema: el administrador da de alta la
        // cuenta pero nunca elige —ni llega a conocer— la contraseña con la
        // que esa persona va a operar. La dicta una vez y el usuario la cambia.
        var temporal = CredencialTemporal.Asignar(usuario);

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return new UsuarioCreadoDto(MapToDto(usuario), temporal);
    }
```

En `ActualizarAsync`, eliminar el bloque que aplicaba la contraseña:

```csharp
        if (!string.IsNullOrEmpty(dto.NuevaPassword))
        {
            ValidarPassword(dto.NuevaPassword);
            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NuevaPassword);
        }
```

Y eliminar el método privado `ValidarPassword`, que se queda sin llamadores:

```csharp
    // Política mínima de contraseñas: 8+ caracteres, al menos una letra y un
    // número. La regla vive en PoliticaPassword porque la comparte el módulo
    // de recuperación de contraseña.
    private static void ValidarPassword(string password) =>
        PoliticaPassword.Validar(password);
```

`PoliticaPassword` sigue en uso desde `RecuperacionService.CambiarPasswordAsync`, que es donde el usuario sí elige su contraseña.

- [ ] **Step 5: Ajustar el controlador**

En `Common/Auth/UsuariosController.cs`, el método `Crear` pasa a leer el Id desde el DTO anidado:

```csharp
    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearUsuarioDto dto)
    {
        try
        {
            var result = await service.CrearAsync(dto);
            return CreatedAtAction(nameof(ObtenerPorId),
                new { id = result.Usuario.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

- [ ] **Step 6: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 5 pruebas de `AltaUsuarioPasswordTests` pasan y todo lo anterior sigue en verde.

---

## Tarea 4: Restablecimiento por iniciativa del administrador

**Files:**
- Modify: `Common/Auth/Recuperacion/RecuperacionService.cs`
- Modify: `Common/Auth/Recuperacion/RecuperacionController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/RestablecerPorAdminTests.cs`

**Interfaces:**
- Consumes: `CredencialTemporal.Asignar` (Tarea 1), `OrigenSolicitudPassword` (Tarea 2), `ISesionService.RevocarUsuarioAsync(int) -> Task<int>`
- Produces: `IRecuperacionService.RestablecerPorAdminAsync(int usuarioId, string cedulaAdmin) -> Task<PasswordTemporalDto>`; endpoint `POST /api/auth/recuperacion/usuario/{usuarioId}`

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/RestablecerPorAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El restablecimiento por iniciativa del administrador es la única vía por la
/// que alguien toca la cuenta de otro sin que medie una solicitud. Por eso
/// tiene que dejar rastro, y por eso no puede aplicarse a uno mismo.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class RestablecerPorAdminTests(ApiFactory api) : IAsyncLifetime
{
    private const string CedulaOperadora = "0104576277";
    private const string CedulaAdmin = "0111223343";

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient ComoAdminConCedula() =>
        api.ComoUsuario("AdminCooperativa", CedulaAdmin);

    private static Task<HttpResponseMessage> Restablecer(HttpClient cliente, int usuarioId) =>
        cliente.PostAsync($"/api/auth/recuperacion/usuario/{usuarioId}", null);

    [Fact]
    public async Task Restablecer_generaTemporal_revocaSesiones_yDejaFilaDeAdministrador()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora);

        await using (var db = api.NuevoDbContext())
        {
            var ahora = DateTime.UtcNow;
            db.RefreshTokens.Add(new RefreshToken
            {
                UsuarioId = usuario.Id,
                TokenHash = "hash-de-una-sesion-abierta",
                FechaCreacion = ahora,
                FechaUltimoUso = ahora,
                FechaExpiracion = ahora.AddDays(7)
            });
            await db.SaveChangesAsync();
        }

        var respuesta = await Restablecer(ComoAdminConCedula(), usuario.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var temporal = await respuesta.Content.ReadFromJsonAsync<PasswordTemporalDto>();
        PoliticaPassword.EsValida(temporal!.PasswordTemporal).ShouldBeTrue();

        await using var verificacion = api.NuevoDbContext();

        var actualizado = await verificacion.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        actualizado.DebeCambiarPassword.ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(temporal.PasswordTemporal, actualizado.PasswordHash)
            .ShouldBeTrue();

        var sesionesVivas = await verificacion.RefreshTokens
            .CountAsync(t => t.UsuarioId == usuario.Id && !t.Revocado);
        sesionesVivas.ShouldBe(0);

        var solicitud = await verificacion.SolicitudesRestablecerPassword
            .AsNoTracking().SingleAsync();
        solicitud.Origen.ShouldBe(OrigenSolicitudPassword.Administrador);
        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Resuelta);
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);
    }

    [Fact]
    public async Task Restablecer_aQuienYaTeniaPendiente_resuelveEsaFilaSinCrearOtra()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora);

        await using (var db = api.NuevoDbContext())
        {
            db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
            {
                UsuarioId = usuario.Id,
                CedulaSolicitada = CedulaOperadora
            });
            await db.SaveChangesAsync();
        }

        await Restablecer(ComoAdminConCedula(), usuario.Id);

        await using var verificacion = api.NuevoDbContext();
        var solicitud = await verificacion.SolicitudesRestablecerPassword
            .AsNoTracking().SingleAsync();

        solicitud.Estado.ShouldBe(EstadoSolicitudPassword.Resuelta);
        // Esa persona SÍ pidió el cambio: el origen no se reescribe
        solicitud.Origen.ShouldBe(OrigenSolicitudPassword.Usuario);
        solicitud.ResueltaPor.ShouldBe(CedulaAdmin);
    }

    [Fact]
    public async Task Restablecer_aUnUsuarioDesactivado_devuelve409()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora, activo: false);

        var respuesta = await Restablecer(ComoAdminConCedula(), usuario.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == usuario.Id);
        actualizado.DebeCambiarPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task UnAdministrador_noPuedeRestablecerseASiMismo()
    {
        var admin = await Sembrador.UsuarioAsync(
            api, CedulaAdmin, rol: RolUsuario.AdminCooperativa, cat: null);

        var respuesta = await Restablecer(ComoAdminConCedula(), admin.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var actualizado = await db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == admin.Id);
        // Su contraseña sigue intacta: no quedó a medias
        BCrypt.Net.BCrypt.Verify(Sembrador.PasswordPorDefecto, actualizado.PasswordHash)
            .ShouldBeTrue();
        actualizado.DebeCambiarPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Restablecer_aUnUsuarioInexistente_devuelve404()
    {
        var respuesta = await Restablecer(ComoAdminConCedula(), 999999);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnOperador_noPuedeRestablecerContrasenas()
    {
        var usuario = await Sembrador.UsuarioAsync(api, CedulaOperadora);

        var respuesta = await Restablecer(api.ComoOperadorCat("PAT"), usuario.Id);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: FALLA — el endpoint no existe (404 donde se esperan 200/409/403).

- [ ] **Step 3: Añadir el método al servicio**

En `Common/Auth/Recuperacion/RecuperacionService.cs`, añadir a la interfaz:

```csharp
    Task<PasswordTemporalDto> RestablecerPorAdminAsync(int usuarioId, string cedulaAdmin);
```

Y el método a la clase, después de `ResolverAsync`:

```csharp
    /// <summary>
    /// Restablece la contraseña de un usuario por iniciativa del administrador,
    /// sin que medie una solicitud. Es la vía para desbloquear a quien llama por
    /// teléfono o no logra usar la pantalla de recuperación por su cuenta.
    /// </summary>
    public async Task<PasswordTemporalDto> RestablecerPorAdminAsync(
        int usuarioId, string cedulaAdmin)
    {
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync();

            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId)
                ?? throw new KeyNotFoundException("El usuario no existe.");

            // Restablecerse a uno mismo revocaría la propia sesión en mitad de
            // la operación: el administrador quedaría desconectado con la
            // temporal a medio leer en un modal que su sesión acaba de
            // invalidar. Para cambiar la propia está /cambiar-password.
            if (usuario.Cedula == cedulaAdmin)
                throw new InvalidOperationException(
                    "No puedes restablecer tu propia contraseña desde aquí. " +
                    "Usa la pantalla de cambiar contraseña.");

            if (!usuario.Activo)
                throw new InvalidOperationException(
                    "El usuario está desactivado. " +
                    "Reactívalo antes de restablecer su contraseña.");

            var temporal = CredencialTemporal.Asignar(usuario);
            var ahora = DateTime.UtcNow;

            // Si ya tenía una solicitud pendiente se resuelve ESA, conservando
            // su origen: esa persona sí pidió el cambio, y el administrador la
            // atendió sin pasar por el botón de la bandeja. Crear una segunda
            // fila dejaría la pendiente colgada para siempre y el administrador
            // vería una solicitud fantasma de alguien a quien ya atendió.
            var pendiente = await db.SolicitudesRestablecerPassword
                .FirstOrDefaultAsync(s => s.UsuarioId == usuarioId
                    && s.Estado == EstadoSolicitudPassword.Pendiente);

            if (pendiente is not null)
            {
                pendiente.Estado = EstadoSolicitudPassword.Resuelta;
                pendiente.FechaResolucion = ahora;
                pendiente.ResueltaPor = cedulaAdmin;
            }
            else
            {
                db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
                {
                    UsuarioId = usuario.Id,
                    CedulaSolicitada = usuario.Cedula,
                    Origen = OrigenSolicitudPassword.Administrador,
                    Estado = EstadoSolicitudPassword.Resuelta,
                    FechaCreacion = ahora,
                    FechaResolucion = ahora,
                    ResueltaPor = cedulaAdmin
                });
            }

            await db.SaveChangesAsync();

            // Igual que al resolver una solicitud: si la cuenta estaba
            // comprometida, dejarle la sesión de 7 días viva anularía el
            // restablecimiento.
            await sesionService.RevocarUsuarioAsync(usuario.Id);

            await transaccion.CommitAsync();

            return new PasswordTemporalDto(
                temporal, usuario.NombreCompleto, usuario.Cedula);
        });
    }
```

- [ ] **Step 4: Añadir el endpoint**

En `Common/Auth/Recuperacion/RecuperacionController.cs`, después del endpoint `Descartar` y antes del helper `CedulaAdmin`:

```csharp
    /// <summary>
    /// Restablece la contraseña de un usuario por iniciativa del administrador,
    /// sin solicitud previa. Vive aquí y no en UsuariosController aunque el
    /// botón esté en la lista de usuarios: escribe en la tabla de solicitudes y
    /// revoca sesiones, así que pertenece al módulo de contraseñas.
    /// </summary>
    [HttpPost("usuario/{usuarioId:int}")]
    [Authorize(Roles = RolesAdmin)]
    public async Task<IActionResult> RestablecerPorAdmin(int usuarioId)
    {
        try
        {
            return Ok(await servicio.RestablecerPorAdminAsync(usuarioId, CedulaAdmin()));
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
```

- [ ] **Step 5: Ejecutar y verificar que pasa**

Run:
```bash
docker compose -f docker-compose.tests.yml run --rm tests
```
Expected: las 6 pruebas de `RestablecerPorAdminTests` pasan. **Toda la batería del API en verde** — 43 anteriores + 4 + 2 + 5 + 6 = 60 pruebas.

---

## Tarea 5: Front — extraer el modal, tipos y cliente

A partir de aquí se trabaja en `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`.

**Files:**
- Create: `src/components/admin/ModalPasswordTemporal.tsx`
- Modify: `src/components/admin/SolicitudesPassword.tsx`
- Modify: `src/types/recuperacion.ts`
- Modify: `src/types/admin.ts`
- Modify: `src/api/recuperacion.ts`
- Modify: `src/api/admin.ts`

**Interfaces:**
- Consumes: `PasswordTemporal` (ya existe), `ModalShell` (ya existe)
- Produces: `<ModalPasswordTemporal datos={PasswordTemporal} onClose={() => void} sesionesRevocadas?={boolean} />`; `recuperacionApi.restablecerPorUsuario(usuarioId: number) -> Promise<PasswordTemporal>`; `UsuarioCreado { usuario: Usuario; passwordTemporal: string }`; `usuariosApi.crear(body) -> Promise<UsuarioCreado>`

- [ ] **Step 1: Extraer el modal a su propio archivo**

Crear `src/components/admin/ModalPasswordTemporal.tsx`:

```tsx
import { useState } from "react";
import { ModalShell } from "../ui/ModalShell";
import type { PasswordTemporal } from "../../types/recuperacion";

interface Props {
    datos: PasswordTemporal;
    onClose: () => void;
    /**
     * Un restablecimiento cierra las sesiones abiertas del usuario; un alta no
     * tiene ninguna que cerrar. Sin esta distinción el modal le diría al
     * administrador que acaba de cerrar sesiones de una cuenta recién creada,
     * que es sencillamente falso.
     */
    sesionesRevocadas?: boolean;
}

/**
 * La contraseña temporal existe fuera del hash una sola vez, aquí. Si el
 * administrador cierra sin anotarla, no hay forma de recuperarla: hay que
 * generar otra con el botón "Restablecer" de la lista de usuarios.
 *
 * Vive en su propio archivo porque lo abren tres pantallas —alta de usuario,
 * restablecimiento desde la lista y resolución de una solicitud— y la tercera
 * copia es la que convierte un descuido en una divergencia.
 */
export function ModalPasswordTemporal({
    datos, onClose, sesionesRevocadas = false,
}: Props) {
    const [copiada, setCopiada] = useState(false);

    return (
        <ModalShell
            onClose={onClose}
            title="Contraseña temporal"
            subtitle={`Para ${datos.nombreCompleto} · ${datos.cedula}`}
            footer={
                <button
                    onClick={onClose}
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
                    {datos.passwordTemporal}
                </p>
            </div>

            <button
                onClick={() => {
                    navigator.clipboard
                        ?.writeText(datos.passwordTemporal)
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
                    <strong>No se volverá a mostrar.</strong> Anótala antes de
                    cerrar esta ventana.
                    {sesionesRevocadas
                        && " Las sesiones abiertas de este usuario ya fueron cerradas."}
                </p>
            </div>
        </ModalShell>
    );
}
```

- [ ] **Step 2: Hacer que la bandeja use el modal extraído**

En `src/components/admin/SolicitudesPassword.tsx`:

Quitar el import de `ModalShell` y añadir el del modal nuevo:

```tsx
import { ModalPasswordTemporal } from "./ModalPasswordTemporal";
```

Quitar el estado `copiada`, que ahora vive dentro del modal:

```tsx
    const [copiada, setCopiada] = useState(false);
```

En la mutación `resolver`, quitar la línea `setCopiada(false);` de su `onSuccess`, que queda:

```tsx
    const resolver = useMutation({
        mutationFn: (id: number) => recuperacionApi.resolver(id),
        onSuccess: (datos) => {
            setTemporal(datos);
            invalidar();
        },
        onError: (e) => mensajeError(e,
            "No se pudo restablecer la contraseña. Actualiza la pantalla."),
    });
```

Y sustituir todo el bloque `{temporal && (<ModalShell …>…</ModalShell>)}` —desde el comentario `{/* La contraseña temporal existe fuera del hash…` hasta su cierre— por:

```tsx
            {temporal && (
                <ModalPasswordTemporal
                    datos={temporal}
                    onClose={() => setTemporal(null)}
                    sesionesRevocadas
                />
            )}
```

- [ ] **Step 3: Añadir el origen al tipo de solicitud**

En `src/types/recuperacion.ts`, añadir el campo dentro de `SolicitudPassword`, después de `estado`:

```typescript
    // Quién puso en marcha el restablecimiento
    origen: "Usuario" | "Administrador";
```

- [ ] **Step 4: Ajustar los tipos de administración**

En `src/types/admin.ts`, quitar `password: string;` de `CrearUsuarioRequest` y `nuevaPassword?: string;` de `ActualizarUsuarioRequest`. Ambas quedan así:

```typescript
export interface CrearUsuarioRequest {
    nombreCompleto: string;
    cedula: string;
    email?: string;
    rol: string;
    catAsignado?: string;
}

export interface ActualizarUsuarioRequest {
    nombreCompleto: string;
    email?: string;
    rol: string;
    catAsignado?: string;
}
```

Y añadir después de `ActualizarUsuarioRequest`:

```typescript
// Respuesta del alta: la contraseña temporal viaja UNA sola vez y no se puede
// volver a consultar.
export interface UsuarioCreado {
    usuario: Usuario;
    passwordTemporal: string;
}
```

- [ ] **Step 5: Ajustar los clientes HTTP**

En `src/api/admin.ts`, añadir `UsuarioCreado` al import de tipos y cambiar `crear`:

```typescript
import type {
    Usuario, UsuarioCreado, CrearUsuarioRequest, ActualizarUsuarioRequest,
    Comunidad, GuardarComunidadRequest, CondicionTransporte,
} from "../types/admin";
```

```typescript
    crear: async (body: CrearUsuarioRequest) => {
        const { data } = await client.post<UsuarioCreado>("/api/usuarios", body);
        return data;
    },
```

En `src/api/recuperacion.ts`, añadir el método después de `descartar`:

```typescript
    // Restablecimiento por iniciativa del administrador, sin solicitud previa
    restablecerPorUsuario: async (usuarioId: number) => {
        const { data } = await client.post<PasswordTemporal>(
            `/api/auth/recuperacion/usuario/${usuarioId}`);
        return data;
    },
```

- [ ] **Step 6: Verificar que compila**

Run:
```bash
pnpm build
```
Expected: `tsc -b` sin errores y `vite build` completa. **`FormUsuario.tsx` fallará** porque todavía envía `password` y `nuevaPassword`; eso lo arregla la Tarea 6. Si el build falla **solo** en `src/components/admin/FormUsuario.tsx`, continuar; cualquier otro error hay que corregirlo aquí.

---

## Tarea 6: Front — el formulario de usuario pierde la contraseña

**Files:**
- Modify: `src/components/admin/FormUsuario.tsx`

**Interfaces:**
- Consumes: `<ModalPasswordTemporal>` (Tarea 5), `usuariosApi.crear -> Promise<UsuarioCreado>` (Tarea 5)
- Produces: nada

- [ ] **Step 1: Quitar el estado y la validación de la contraseña**

En `src/components/admin/FormUsuario.tsx`, añadir los imports:

```tsx
import { ModalPasswordTemporal } from "./ModalPasswordTemporal";
import type { PasswordTemporal } from "../../types/recuperacion";
```

Sustituir la línea del estado `password`:

```tsx
    const [password, setPassword] = useState("");
```

por el estado de la temporal:

```tsx
    // Al crear, la contraseña la genera el servidor y llega en la respuesta:
    // el formulario da paso al modal que la muestra una única vez
    const [temporal, setTemporal] = useState<PasswordTemporal | null>(null);
```

En `handleSubmit`, eliminar el bloque de validación que ya no aplica:

```tsx
        if (!editando && password.length < 8) {
            setError("La contraseña debe tener al menos 8 caracteres, con una letra y un número.");
            return;
        }
```

- [ ] **Step 2: Ajustar la mutación**

Sustituir la mutación completa por:

```tsx
    const mutation = useMutation({
        mutationFn: async () => {
            const cat = esOperadorCat ? catAsignado : undefined;
            if (editando) {
                await usuariosApi.actualizar(usuario.id, {
                    nombreCompleto: nombre,
                    email: email || undefined,
                    rol,
                    catAsignado: cat,
                });
                return null;
            }
            return await usuariosApi.crear({
                nombreCompleto: nombre, cedula,
                email: email || undefined, rol,
                catAsignado: cat,
            });
        },
        onSuccess: (creado) => {
            qc.invalidateQueries({ queryKey: ["usuarios"] });
            // Al editar se cierra sin más; al crear hay que entregar la
            // temporal antes de cerrar, o el usuario nuevo no puede entrar
            if (creado === null) {
                onClose();
                return;
            }
            setTemporal({
                passwordTemporal: creado.passwordTemporal,
                nombreCompleto: creado.usuario.nombreCompleto,
                cedula: creado.usuario.cedula,
            });
        },
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje
                ?? "No se pudo guardar. Verifica los datos e intenta nuevamente.");
        },
    });
```

- [ ] **Step 3: Mostrar el modal en lugar del formulario tras crear**

Inmediatamente antes del `return (` que renderiza el `ModalShell`, añadir:

```tsx
    // El usuario ya está creado: el formulario cede el sitio a la temporal.
    // Cerrar este modal cierra también el formulario — no hay nada más que
    // hacer con él.
    if (temporal) {
        return <ModalPasswordTemporal datos={temporal} onClose={onClose} />;
    }
```

No se pasa `sesionesRevocadas`: una cuenta recién creada no tiene sesiones abiertas que cerrar.

- [ ] **Step 4: Eliminar el campo del formulario**

Borrar el bloque completo del campo de contraseña, desde `<div>` hasta su `</div>`:

```tsx
                <div>
                    <label className="block text-xs font-bold uppercase tracking-wide
                        text-gray-500 mb-1">
                        {editando ? "Nueva contraseña (dejar vacío para no cambiar)" : "Contraseña"}
                    </label>
                    <input
                        type="password"
                        required={!editando}
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        placeholder="Mínimo 8 caracteres, con letra y número"
                        autoComplete="new-password"
                        className="w-full h-12 px-3 rounded-xl border-2 border-gray-200
                       text-base focus:border-primary-500 focus:outline-none"
                    />
                </div>
```

- [ ] **Step 5: Verificar que compila y pasa el lint**

Run:
```bash
pnpm build
```
Expected: sin errores. El aviso de `password` sin usar debe haber desaparecido.

Run:
```bash
pnpm lint
```
Expected: sin salida.

---

## Tarea 7: Front — botón "Restablecer" en la lista de usuarios

**Files:**
- Modify: `src/components/admin/TablaUsuarios.tsx`

**Interfaces:**
- Consumes: `recuperacionApi.restablecerPorUsuario` y `<ModalPasswordTemporal>` (Tarea 5), `useAuth()` (ya existe, expone `auth.cedula`)
- Produces: nada

- [ ] **Step 1: Añadir imports, estado y mutación**

En `src/components/admin/TablaUsuarios.tsx`, añadir los imports:

```tsx
import { recuperacionApi } from "../../api/recuperacion";
import { ModalPasswordTemporal } from "./ModalPasswordTemporal";
import { useAuth } from "../../context/useAuth";
import type { PasswordTemporal } from "../../types/recuperacion";
```

Dentro del componente, después de `const [aviso, setAviso] = useState<string | null>(null);`:

```tsx
    const { auth } = useAuth();
    const [temporal, setTemporal] = useState<PasswordTemporal | null>(null);
```

Y después de la mutación `toggle`:

```tsx
    const restablecer = useMutation({
        mutationFn: (id: number) => recuperacionApi.restablecerPorUsuario(id),
        onSuccess: (datos) => setTemporal(datos),
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setAviso(err.response?.data?.mensaje
                ?? "No se pudo restablecer la contraseña.");
        },
    });
```

- [ ] **Step 2: Añadir el botón a la fila**

En la celda de acciones, después del botón "Editar" y antes del de activar/desactivar:

```tsx
                                        {u.activo && u.cedula !== auth.cedula && (
                                            <button
                                                disabled={restablecer.isPending}
                                                onClick={() => restablecer.mutate(u.id)}
                                                className="text-xs font-semibold
                                   text-primary-600 hover:text-primary-800
                                   disabled:text-gray-300"
                                            >
                                                Restablecer
                                            </button>
                                        )}
```

El botón no aparece en usuarios inactivos ni en la fila del propio administrador porque el servidor rechaza ambos casos con 409: un botón que siempre falla es peor que un botón ausente.

- [ ] **Step 3: Renderizar el modal**

Junto al `{showForm && (<FormUsuario … />)}` del final del componente, añadir:

```tsx
            {temporal && (
                <ModalPasswordTemporal
                    datos={temporal}
                    onClose={() => setTemporal(null)}
                    sesionesRevocadas
                />
            )}
```

- [ ] **Step 4: Verificar que compila y pasa el lint**

Run:
```bash
pnpm build
```
Expected: sin errores.

Run:
```bash
pnpm lint
```
Expected: sin salida.

---

## Tarea 8: Front — mostrar el origen en la bandeja

**Files:**
- Modify: `src/components/admin/SolicitudesPassword.tsx`

**Interfaces:**
- Consumes: `SolicitudPassword.origen` (Tarea 5)
- Produces: nada

- [ ] **Step 1: Añadir la columna a la cabecera**

En `src/components/admin/SolicitudesPassword.tsx`, cambiar el arreglo de cabeceras para insertar "Origen" entre "Rol" y "Solicitada":

```tsx
                                {["Usuario", "Cédula", "Rol", "Origen", "Solicitada", "Estado", ""]
```

- [ ] **Step 2: Añadir la celda a cada fila**

Insertar la celda entre la del rol y la de la antigüedad:

```tsx
                                    <td className="px-4 py-3 text-gray-600">
                                        {s.origen === "Administrador"
                                            ? "Iniciado por el admin."
                                            : "Lo pidió el usuario"}
                                    </td>
```

Se escribe en palabras y no con el valor crudo del enum porque la distinción que importa al leer la bandeja es *quién dio el primer paso*, no cómo se llama el campo en la base.

- [ ] **Step 3: Verificar que compila y pasa el lint**

Run:
```bash
pnpm build
```
Expected: sin errores.

Run:
```bash
pnpm lint
```
Expected: sin salida.

- [ ] **Step 4: Levantar API y front para la verificación manual**

Postgres de pruebas y API en Docker, desde la raíz del repositorio del API:

```bash
docker compose -f docker-compose.tests.yml up -d postgres
```

```bash
MSYS_NO_PATHCONV=1 docker run -d --name coopagcuy-api-verif --network coopagcuyapi_default -p 7275:8080 -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ASPNETCORE_ENVIRONMENT=Development -e ASPNETCORE_URLS=http://+:8080 -e ConnectionStrings__NeonDb="Host=postgres;Port=5432;Database=coopagcuy_test;Username=postgres;Password=postgres" -e Jwt__Key="clave-de-pruebas-coopagcuy-32-chars!!" -e Jwt__Issuer=CoopagcuyApi -e Jwt__Audience=CoopagcuyFrontend -e AzureBlob__ConnectionString="" -e QR__BaseUrl="http://localhost/qr" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet run --project CoopagcuyApi.csproj --no-launch-profile"
```

El contenedor tarda un par de minutos en restaurar y compilar. Esperar a que `/health` responda:

```bash
until curl -s -o /dev/null -w '%{http_code}' http://localhost:7275/health | grep -q 200; do sleep 5; done; echo "API lista"
```

La base no tiene usuarios: crear un administrador para poder entrar.

```bash
docker exec -i coopagcuyapi-postgres-1 psql -U postgres -d coopagcuy_test -c "CREATE EXTENSION IF NOT EXISTS pgcrypto; INSERT INTO \"Usuarios\" (\"NombreCompleto\",\"Cedula\",\"Email\",\"PasswordHash\",\"Rol\",\"CatAsignado\",\"Activo\",\"FechaCreacion\",\"DebeCambiarPassword\") VALUES ('Ana Quizhpe','0111223343',NULL,crypt('admin1234', gen_salt('bf',11)),'AdminCooperativa',NULL,true,now() AT TIME ZONE 'utc',false);"
```

El front apunta por defecto a `https://localhost:7275`, y el API de verificación sirve por HTTP. Crear un override temporal en `coopagcuy-frontend/.env.development.local`:

```
VITE_API_URL=http://localhost:7275
```

Arrancar el front con `pnpm dev` (o la herramienta de preview del entorno) y entrar con `0111223343` / `admin1234`.

- [ ] **Step 5: Comprobar el circuito en el navegador**

Cuatro cosas:

1. Crear un usuario nuevo desde Administración → Usuarios muestra el modal con la temporal, **sin** la frase sobre sesiones cerradas.
2. Entrar con ese usuario y su temporal lleva directo a "Crea tu contraseña".
3. El botón "Restablecer" de la lista genera otra temporal, **con** la frase sobre sesiones cerradas, y la bandeja muestra la fila como "Iniciado por el admin.".
4. El botón "Restablecer" **no aparece** en la fila del propio administrador ni en usuarios desactivados.

Tomar una captura del modal tras crear un usuario.

- [ ] **Step 6: Retirar el andamiaje de verificación**

```bash
rm -f /c/Users/nicol/OneDrive/Documents/CoopagcuyFront/coopagcuy-frontend/.env.development.local
```

```bash
docker rm -f coopagcuy-api-verif
```

```bash
docker compose -f docker-compose.tests.yml down
```

Confirmar con `git status` en el repo del front que no queda `.env.development.local`: **no está en `.gitignore`** (solo lo está `.env`), así que olvidarlo lo dejaría como archivo sin seguimiento apuntando a un API que ya no existe.

---

## Correcciones descubiertas al ejecutar el plan

**1 · La migración nacía con el valor por defecto equivocado.** El plan mapeaba
`Origen` con `HasConversion<string>().HasMaxLength(20)` y daba por hecho que el
inicializador de la propiedad (`= OrigenSolicitudPassword.Usuario`) bastaría.
No basta: ese inicializador solo actúa sobre objetos nuevos en memoria, EF no
lo lee, y la migración salió con **`defaultValue: ""`**. Las filas que ya
existían habrían quedado con un `Origen` vacío —un valor que no corresponde a
ningún miembro del enum— y leerlas habría reventado al convertir.

Corregido añadiendo `.HasDefaultValue(OrigenSolicitudPassword.Usuario)` al
mapeo. **Regla para este repositorio:** toda columna nueva no anulable sobre una
tabla con datos necesita su valor por defecto declarado en el `modelBuilder`,
no solo en la propiedad de C#.

Rehacer una migración mal generada tiene su propia trampa: `dotnet ef migrations
remove` **intenta conectarse a la base** y falla con la cadena de marcador que
usa `migrations add`. Lo que funciona es borrar los dos archivos generados y
restaurar el snapshot con `git checkout -- Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`.

---

## Cierre

Al terminar las 8 tareas:

1. **Batería completa del API en verde:**
   ```bash
   docker compose -f docker-compose.tests.yml run --rm tests
   ```
   60 pruebas, 0 fallos.

2. **Front compilando y sin avisos de lint:**
   ```bash
   pnpm build
   ```

3. **Dos migraciones sin aplicar a Neon** — `RecuperacionPassword` (del plan anterior) y `OrigenSolicitudPassword`. Ambas aditivas. Se aplican solas al desplegar `main`, o a mano con el patrón de Docker cambiando `migrations add` por `database update` y la cadena real de Neon.

4. **Sin commits ni `push`**, por indicación expresa del usuario. Todo queda en el árbol de trabajo de los dos repositorios.

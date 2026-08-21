# Ciclo de pago por ticket — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convertir el pago a productoras de un apunte plano en un ciclo de tres estados —la CAT emite un ticket imprimible, la planta transfiere y sube la captura, la CAT verifica— con descuentos que solo pueden apoyarse en una novedad que el centro de acopio ya registró.

**Architecture:** Una sola entidad `Pago` con máquina de estados (`Pendiente → Pagado → Recibido`), una tabla `DescuentoPago` cuyo `NovedadCatId` obligatorio es lo que sostiene la trazabilidad, un PDF de 80 mm generado con QuestPDF, y un contenedor de blobs propio con doble mecanismo de borrado (política de Azure a 30 días + barrido oportunista a 5 días tras la verificación).

**Tech Stack:** ASP.NET Core 8, EF Core + PostgreSQL (Neon), FluentValidation, QuestPDF 2024.3.1, Azure Blob Storage; React 19 + TypeScript + Vite + Tailwind, React Query 5.

**Spec:** `docs/superpowers/specs/2026-08-21-ciclo-de-pago-por-ticket-design.md`

## Global Constraints

- **Las pruebas del API corren SOLO dentro de Docker.** Smart App Control bloquea la carga del DLL desde OneDrive (0x800711C7), así que `dotnet test` no funciona en este Windows. Comando: `docker compose -f docker-compose.tests.yml run --rm tests`.
- **`dotnet ef` tampoco corre en local.** Las migraciones se generan dentro de un contenedor Linux con `MSYS_NO_PATHCONV=1` (el comando exacto está en la Task 2).
- **Nunca apuntar a `CoopagcuyApi.slnx`.** El SDK 8 no entiende `.slnx` (MSB4068). Todos los comandos `dotnet` van contra `tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj`.
- **Ninguna prueba puede asumir `Id == 1`.** Respawn trunca sin `RESTART IDENTITY`. Usar siempre el Id que devuelve `Sembrador`.
- **Nunca agrupar ni comparar por instancia de entidad.** Siempre por `Id` — hubo una regresión por esto en este repo.
- **Nombres de contenedor con `IsNullOrWhiteSpace`, jamás con `??`.** `appsettings.json` declara las claves con cadena VACÍA como superficie de documentación, y `??` solo cubre null. Esto causó un 500 en producción el 2026-08-20.
- **Validar todo ANTES de subir cualquier blob.** Si la validación se intercala con la subida, un fallo a mitad deja evidencias huérfanas que nadie va a limpiar.
- **Las pruebas de blobs se cuentan por diferencia de blobs, no por filas.** Una prueba que solo mira filas pasa con el fallo presente; ya ocurrió.
- **En el front no hay Vitest ni Playwright.** La verificación es `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los tres en verde.
- **Cédulas de prueba válidas según el algoritmo ecuatoriano.** `ProductoraService` las revalida; un número inventado rompe la prueba por un motivo ajeno a lo que verifica. Usar `0104576277`.
- **Denegar por centro ajeno responde 404, no 403.** Confirmar que el recurso existe ya filtraría información de otro CAT.

## Estructura de archivos

**API — se crea:**

| Archivo | Responsabilidad |
|---|---|
| `Features/Pagos/Models/Pago.cs` | Entidad con su ciclo de vida (movida desde `Productoras`) |
| `Features/Pagos/Models/DescuentoPago.cs` | Descuento atado a una novedad del CAT |
| `Features/Pagos/DTOs/PagoDtos.cs` | Todos los DTO de pago (extraídos de `ProductoraDto.cs`) |
| `Features/Pagos/Services/PagoService.cs` | Emisión, pago, verificación, descuentos, barrido |
| `Features/Pagos/Services/TicketPagoService.cs` | Solo el PDF de 80 mm |
| `Features/Pagos/Controllers/PagosController.cs` | Rutas y roles (movido) |
| `Common/Exceptions/TransicionInvalidaException.cs` | Señala un cambio de estado fuera de orden → 409 |

**API — se modifica:** `Common/Enums.cs`, `Infrastructure/Data/AppDbContext.cs`, `Infrastructure/Storage/BlobStorageService.cs`, `Program.cs`, `Features/Productoras/DTOs/ProductoraDto.cs`, `infra/politica-evidencias.json`.

**Front — se crea:** `src/components/ui/ImagenProtegida.tsx`, `src/components/faenamiento/FormPagoProductora.tsx`, `src/components/recepcion/VerificarPago.tsx`, `src/api/pagos.ts`.

**Front — se modifica:** `src/components/ui/EvidenciaNovedad.tsx`, `src/components/recepcion/FormPago.tsx`, `src/pages/Faenamiento.tsx`, `src/pages/Recepcion.tsx`, `src/types/productora.ts`.

**Una desviación respecto al spec:** el diseño listaba un `Features/Pagos/Validators/PagoValidators.cs`. Este plan no lo crea. Toda la validación de esta feature es de dominio —¿esta novedad pertenece a un cuy de esta productora en este lote?, ¿el ticket está en el estado que admite esta transición?— y necesita consultar la base. FluentValidation valida la forma del cuerpo, no relaciones entre entidades: meterla ahí obligaría a repetir las mismas consultas en dos capas y a mantenerlas sincronizadas. Las reglas viven en `PagoService`, que es donde está la transacción que las hace ciertas.

---

# FASE A — Ticket y estados

Al terminar esta fase la productora ya se lleva su papel impreso.

---

### Task 1: Estado, columnas y tabla de descuentos

**Files:**
- Modify: `Common/Enums.cs`
- Create: `Features/Pagos/Models/Pago.cs` (movido desde `Features/Productoras/Models/Pago.cs`)
- Create: `Features/Pagos/Models/DescuentoPago.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/CicloPagoTests.cs`

**Interfaces:**
- Consumes: nada (primera tarea).
- Produces: `EstadoPago` (`Pendiente`/`Pagado`/`Recibido`); `Pago` con `Estado`, `MontoPagadoUsd`, `FechaPagoEfectivo`, `PagadoPor`, `ComprobanteUrl`, `ComprobanteExpiraEn`, `FechaVerificacion`, `VerificadoPor`, `Descuentos`; `DescuentoPago` con `PagoId`, `NovedadCatId`, `Descripcion`, `MontoUsd`, `RegistradoPor`, `FechaRegistro`; `db.Descuentos`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/CicloPagoTests.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Ciclo de vida del pago: emisión por la CAT, pago por la planta y
/// verificación por la CAT. Las transiciones son de un solo sentido.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CicloPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    [Fact]
    public async Task UnPagoNuevoNaceEnEstadoPendiente()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        var pago = new Pago
        {
            ProductoraId = productora.Id,
            MontoUsd = 120m,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        var guardado = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == pago.Id);

        guardado.Estado.ShouldBe(EstadoPago.Pendiente);
        guardado.MontoPagadoUsd.ShouldBeNull();
        guardado.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnDescuentoNoPuedeRepetirLaMismaNovedadEnElMismoPago()
    {
        // Índice único, no solo validación de servicio: dos peticiones
        // simultáneas pasarían las dos por la validación y descontarían
        // el mismo defecto dos veces.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();

        var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
        {
            CodigoLote = $"PAT-{Guid.NewGuid():N}"[..12],
            CentroAcopio = CentroAcopio.PAT,
            ProductoraId = productora.Id,
            FechaRecepcion = DateTime.UtcNow,
            CantidadAnimales = 1
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var novedad = new CoopagcuyApi.Features.Recepcion.Models.Novedad
        {
            LoteId = lote.Id,
            Tipo = TipoNovedad.SignosClinicos,
            Descripcion = "lesión visible",
            RegistradoPor = "Operadora de prueba"
        };
        db.Novedades.Add(novedad);

        var pago = new Pago
        {
            ProductoraId = productora.Id,
            LoteId = lote.Id,
            MontoUsd = 120m,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        db.Descuentos.Add(new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = novedad.Id,
            Descripcion = "llegó muerto",
            MontoUsd = 8m,
            RegistradoPor = "Planta de prueba"
        });
        await db.SaveChangesAsync();

        db.Descuentos.Add(new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = novedad.Id,
            Descripcion = "segundo intento sobre el mismo defecto",
            MontoUsd = 5m,
            RegistradoPor = "Planta de prueba"
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CicloPagoTests" --logger "console;verbosity=normal"
```

Expected: FALLO de compilación — `The type or namespace name 'Pagos' does not exist`, `'EstadoPago' could not be found`, `'AppDbContext' does not contain a definition for 'Descuentos'`.

- [ ] **Step 3: Añadir el enum**

En `Common/Enums.cs`, al final del archivo:

```csharp
/// <summary>
/// Ciclo de vida de un pago. Las transiciones son de un solo sentido: no se
/// anula un pago, se corrige con otro.
/// </summary>
public enum EstadoPago
{
    Pendiente,  // la CAT lo emitió, la planta aún no transfiere
    Pagado,     // la planta transfirió y subió la captura
    Recibido    // la CAT confirmó que el dinero llegó
}
```

- [ ] **Step 4: Mover y ampliar `Pago`**

```bash
mkdir -p Features/Pagos/Models Features/Pagos/DTOs Features/Pagos/Services Features/Pagos/Controllers
git mv Features/Productoras/Models/Pago.cs Features/Pagos/Models/Pago.cs
```

Reescribir `Features/Pagos/Models/Pago.cs` completo:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Pagos.Models;

/// <summary>
/// Pago a una productora por los cuyes que aportó a un lote.
///
/// No es un apunte, es un ciclo: la CAT reconoce lo que se debe y entrega un
/// ticket impreso, la planta transfiere y sube la captura, la CAT confirma que
/// el dinero llegó. Cada actor escribe su propio bloque de campos y ninguno
/// reescribe el del otro.
/// </summary>
public class Pago
{
    public int Id { get; set; }

    public int ProductoraId { get; set; }
    public Productora Productora { get; set; } = null!;

    // Anulable en el esquema por las filas anteriores a este ciclo, pero
    // OBLIGATORIO en el servicio para los pagos nuevos: un ticket que dice
    // "por los cuyes que aportó a cierto lote" no puede existir sin lote, y
    // sin lote tampoco hay novedades que trazar.
    public int? LoteId { get; set; }
    public Lote? Lote { get; set; }

    // ── Lo que emite la CAT ──────────────────────────────────────────
    public decimal MontoUsd { get; set; }
    public DateTime FechaPago { get; set; }
    // Desde 2026-08 siempre "Transferencia". Los valores "Contado",
    // "Credito", "Efectivo" son legados de filas anteriores.
    public string MetodoPago { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Ya no se escriben. Las columnas permanecen por las filas del pago a
    // crédito, retirado con el paso a transferencia única — igual que se
    // hizo con el color Negro en TipoNovedad.
    public int? NumeroDias { get; set; }
    public decimal? ValorPorDia { get; set; }

    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;

    // ── Lo que escribe la planta al transferir ───────────────────────

    // MontoUsd menos la suma de descuentos. Lo calcula SIEMPRE el servidor:
    // es la cifra que la productora cobra, y no puede depender de lo que
    // mande el cliente. Nulo mientras el pago siga pendiente.
    public decimal? MontoPagadoUsd { get; set; }
    public DateTime? FechaPagoEfectivo { get; set; }
    public string? PagadoPor { get; set; }
    public string? ComprobanteUrl { get; set; }

    // ── Lo que escribe la CAT al verificar ───────────────────────────
    public DateTime? FechaVerificacion { get; set; }
    public string? VerificadoPor { get; set; }

    // Verificación + 5 días. Permite que el API deje de servir la captura en
    // el momento exacto, sin depender de cuándo pase el barrido.
    public DateTime? ComprobanteExpiraEn { get; set; }

    public ICollection<DescuentoPago> Descuentos { get; set; } = [];
}
```

- [ ] **Step 5: Crear `DescuentoPago`**

Crear `Features/Pagos/Models/DescuentoPago.cs`:

```csharp
using CoopagcuyApi.Features.Recepcion.Models;

namespace CoopagcuyApi.Features.Pagos.Models;

/// <summary>
/// Rebaja sobre el monto del ticket, justificada por un defecto que el centro
/// de acopio documentó.
///
/// `NovedadCatId` es obligatorio y no anulable: ahí vive toda la trazabilidad
/// de la feature. Una fila de descuento sin novedad de origen sería
/// exactamente el caso que este diseño existe para impedir — la planta pagando
/// de menos por un problema que nadie vio.
/// </summary>
public class DescuentoPago
{
    public int Id { get; set; }

    public int PagoId { get; set; }
    public Pago Pago { get; set; } = null!;

    public int NovedadCatId { get; set; }
    public Novedad NovedadCat { get; set; } = null!;

    // Lo que observó la planta, con sus palabras. La novedad del CAT dice lo
    // que se vio al recibir; esto dice lo que se vio al faenar.
    public string Descripcion { get; set; } = string.Empty;

    public decimal MontoUsd { get; set; }
    public string RegistradoPor { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 6: Registrar en el `DbContext`**

En `Infrastructure/Data/AppDbContext.cs`, cambiar el `using` de `Pago` a `using CoopagcuyApi.Features.Pagos.Models;` y añadir junto a `DbSet<Pago> Pagos`:

```csharp
public DbSet<DescuentoPago> Descuentos => Set<DescuentoPago>();
```

En `OnModelCreating`, añadir:

```csharp
// Un mismo defecto no se descuenta dos veces sobre el mismo ticket. Va en el
// índice y no solo en el servicio: dos peticiones simultáneas pasarían las
// dos por la validación y grabarían el descuento por duplicado.
modelBuilder.Entity<DescuentoPago>()
    .HasIndex(d => new { d.PagoId, d.NovedadCatId })
    .IsUnique();

// Restrict y no Cascade: borrar una novedad no puede llevarse por delante la
// justificación de un pago ya cobrado.
modelBuilder.Entity<DescuentoPago>()
    .HasOne(d => d.NovedadCat)
    .WithMany()
    .HasForeignKey(d => d.NovedadCatId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<DescuentoPago>()
    .HasOne(d => d.Pago)
    .WithMany(p => p.Descuentos)
    .HasForeignKey(d => d.PagoId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 7: Arreglar los `using` rotos por el movimiento**

```bash
grep -rln "Features.Productoras.Models" --include=*.cs . | xargs grep -ln "Pago" 
```

En cada archivo que salga —como mínimo `Features/Productoras/Services/PagoService.cs`, `Features/Productoras/Controllers/PagosController.cs` y `Features/Productoras/Models/Productora.cs`— añadir `using CoopagcuyApi.Features.Pagos.Models;`.

- [ ] **Step 8: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~CicloPagoTests" --logger "console;verbosity=normal"
```

Expected: PASS, 2 pruebas. Si `UnDescuentoNoPuedeRepetir…` falla porque no lanza, el índice único del paso 6 no se aplicó — la migración de la Task 2 aún no existe, así que **esta prueba solo pasará tras la Task 2**. Dejarla en rojo y continuar; se verifica al final de la Task 2.

- [ ] **Step 9: Commit**

```bash
git add Common/Enums.cs Features/Pagos Features/Productoras Infrastructure/Data/AppDbContext.cs tests/CoopagcuyApi.Tests/Integracion/CicloPagoTests.cs
git commit -m "feat: el pago gana ciclo de vida y descuentos trazables"
```

---

### Task 2: Migración aditiva

**Files:**
- Create: `Infrastructure/Data/Migrations/*_CicloPagoPorTicket.cs`
- Modify: `Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs` (lo genera EF)

**Interfaces:**
- Consumes: `EstadoPago`, `Pago`, `DescuentoPago` de la Task 1.
- Produces: el esquema en base; a partir de aquí las consultas sobre `Estado` y `Descuentos` funcionan.

- [ ] **Step 1: Generar la migración**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=placeholder;Username=postgres;Password=postgres" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet tool install --global dotnet-ef --version 8.* >/dev/null 2>&1; export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add CicloPagoPorTicket --project CoopagcuyApi.csproj"
```

Expected: `Done. To undo this action, use 'ef migrations remove'`.

- [ ] **Step 2: Revisar que sea aditiva**

Abrir `Infrastructure/Data/Migrations/*_CicloPagoPorTicket.cs` y confirmar que `Up()` contiene **solo**:

- `AddColumn` sobre `Pagos`: `Estado` (int, defaultValue 0), `MontoPagadoUsd`, `FechaPagoEfectivo`, `PagadoPor`, `ComprobanteUrl`, `ComprobanteExpiraEn`, `FechaVerificacion`, `VerificadoPor` — todas anulables salvo `Estado`.
- `CreateTable` de `Descuentos` con sus dos claves foráneas.
- `CreateIndex` único sobre `("PagoId", "NovedadCatId")`.

**Si aparece cualquier `AlterColumn` sobre columnas de fecha**, la migración se generó sin `Npgsql.EnableLegacyTimestampBehavior`: borrar los dos archivos generados, restaurar el snapshot con `git checkout -- Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`, verificar `AppDbContextFactory.cs` y repetir el paso 1.

**Si aparece cualquier `DropColumn`**, algo se perdió al mover el modelo. Mismo procedimiento de rehacer. (`dotnet ef migrations remove` no sirve: intenta conectarse a la base y falla con la cadena de marcador.)

- [ ] **Step 3: Añadir el arreglo de datos de las filas viejas**

Al final de `Up()`, antes del cierre, añadir:

```csharp
// Los pagos anteriores a este ciclo son transacciones cerradas del flujo en
// efectivo: se pagó lo que se reconoció y nadie tiene que verificar nada.
// Dejarlos en Pendiente los haría aparecer en la bandeja de la planta como
// deuda viva.
migrationBuilder.Sql(@"
    UPDATE ""Pagos""
    SET ""Estado"" = 2, ""MontoPagadoUsd"" = ""MontoUsd""
    WHERE ""FechaRegistro"" < NOW();");
```

`2` es `EstadoPago.Recibido`. `MetodoPago` no se reescribe: conserva su valor histórico.

- [ ] **Step 4: Ejecutar la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Expected: PASS en todas, incluidas las 2 de `CicloPagoTests` que quedaron rojas en la Task 1.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/Data/Migrations
git commit -m "feat: migración del ciclo de pago, aditiva y con las filas viejas en Recibido"
```

---

### Task 3: Emisión — transferencia única y lote obligatorio

**Files:**
- Create: `Features/Pagos/DTOs/PagoDtos.cs`
- Modify: `Features/Productoras/DTOs/ProductoraDto.cs` (quitar los tres DTO de pago)
- Create: `Features/Pagos/Services/PagoService.cs` (movido desde `Features/Productoras/Services/`)
- Create: `Features/Pagos/Controllers/PagosController.cs` (movido)
- Modify: `Program.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/EmisionTicketTests.cs`

**Interfaces:**
- Consumes: `Pago`, `EstadoPago` de la Task 1.
- Produces: `RegistrarPagoDto` sin `NumeroDias`; `PagoResponseDto` con `Estado`, `MontoPagadoUsd`, `TieneComprobante`; `IPagoService.RegistrarAsync(RegistrarPagoDto, CentroAcopio?)` sin cambios de firma.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/EmisionTicketTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Emisión del ticket por la CAT: siempre transferencia, siempre con lote.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class EmisionTicketTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private static object Cuy(decimal peso) => new
    {
        pesoGramos = peso,
        colorPelaje = "Blanco",
        estadoOreja = "Blanda",
        tamanoAnimal = "Normal"
    };

    /// Registra una entrega real y devuelve (productoraId, loteId).
    private async Task<(int ProductoraId, int LoteId)> EntregaAsync(int cuantosCuyes)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, cuantosCuyes)
            .Select(_ => Cuy(1300m)).ToArray();

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var loteId = await db.CuyRegistros
            .Where(c => c.ProductoraId == productora.Id)
            .Select(c => c.LoteId)
            .FirstAsync();

        return (productora.Id, loteId);
    }

    [Fact]
    public async Task ElPagoSeGuardaSiempreComoTransferenciaYEnPendiente()
    {
        var (productoraId, loteId) = await EntregaAsync(3);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId,
                montoUsd = 120m,
                // El cliente manda basura a propósito: el servidor la ignora
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.ProductoraId == productoraId);

        pago.MetodoPago.ShouldBe("Transferencia");
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.NumeroDias.ShouldBeNull();
        pago.ValorPorDia.ShouldBeNull();
    }

    [Fact]
    public async Task UnPagoSinLoteSeRechaza()
    {
        // Un ticket que dice "por los cuyes que aportó a cierto lote" no
        // puede existir sin lote, y sin lote no hay novedades que trazar.
        var (productoraId, _) = await EntregaAsync(3);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId = (int?)null,
                montoUsd = 120m,
                metodoPago = "Transferencia",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~EmisionTicketTests" --logger "console;verbosity=normal"
```

Expected: FAIL. La primera con `MetodoPago should be "Transferencia" but was "Efectivo"`; la segunda con `should be Conflict but was Created`.

- [ ] **Step 3: Extraer los DTO a su propio archivo**

Crear `Features/Pagos/DTOs/PagoDtos.cs`:

```csharp
namespace CoopagcuyApi.Features.Pagos.DTOs;

/// <summary>
/// Alta de un ticket por la operadora del CAT. El método de pago no viaja:
/// desde el paso a transferencia única lo fija el servidor.
/// </summary>
public class RegistrarPagoDto
{
    public int ProductoraId { get; set; }
    // Obligatorio pese a ser anulable: se valida en el servicio para poder
    // responder 409 con un mensaje legible en vez de un 400 de modelo.
    public int? LoteId { get; set; }
    public decimal MontoUsd { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

// Lote por el que aún se le debe pagar a una productora. Cantidad y peso son
// el aporte de ESA productora a la jaula, no el total de la jaula.
public record LotePendientePagoDto(
    int LoteId,
    string CodigoLote,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CuyesEntregados,
    decimal PesoEntregadoGramos
);

public record PagoResponseDto(
    int Id,
    int ProductoraId,
    string NombreProductora,
    int? LoteId,
    string? CodigoLote,
    decimal MontoUsd,
    DateTime FechaPago,
    string MetodoPago,
    string Estado,
    decimal? MontoPagadoUsd,
    DateTime? FechaPagoEfectivo,
    string? PagadoPor,
    // No se expone la URL del blob: el comprobante se sirve por su propio
    // endpoint autenticado. Un booleano basta para decidir si pintar el visor.
    bool TieneComprobante,
    DateTime? FechaVerificacion,
    string? VerificadoPor,
    string Responsable,
    string? Observaciones
);
```

Borrar `RegistrarPagoDto`, `LotePendientePagoDto` y `PagoResponseDto` de `Features/Productoras/DTOs/ProductoraDto.cs`.

- [ ] **Step 4: Mover el servicio y el controlador**

```bash
git mv Features/Productoras/Services/PagoService.cs Features/Pagos/Services/PagoService.cs
git mv Features/Productoras/Controllers/PagosController.cs Features/Pagos/Controllers/PagosController.cs
```

En ambos, cambiar el `namespace` a `CoopagcuyApi.Features.Pagos.Services` / `.Controllers`, y los `using` a:

```csharp
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
```

- [ ] **Step 5: Reescribir `RegistrarAsync`**

En `Features/Pagos/Services/PagoService.cs`, sustituir el cuerpo de `RegistrarAsync` desde la línea del `Lote? lote = null;` hasta el `return`:

```csharp
        // El ticket es por los cuyes de un lote concreto. Sin lote no hay nada
        // que imprimir ni novedades que trazar después.
        if (dto.LoteId is not int loteId)
            throw new InvalidOperationException(
                "El pago debe corresponder a un lote.");

        var lote = await db.Lotes.FindAsync(loteId)
            ?? throw new KeyNotFoundException($"Lote con Id {loteId} no encontrado.");

        // La jaula es multi-productora: el pago es válido si la productora
        // entregó cuyes en ese lote (Lote.ProductoraId es solo la referencia
        // histórica de quien abrió la jaula)
        var participo = lote.ProductoraId == dto.ProductoraId
            || await db.CuyRegistros.AnyAsync(c =>
                c.LoteId == loteId && c.ProductoraId == dto.ProductoraId);

        if (!participo)
            throw new InvalidOperationException(
                "La productora no registra entregas en ese lote.");

        if (dto.MontoUsd <= 0)
            throw new InvalidOperationException(
                "El monto del pago debe ser mayor a cero.");

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = loteId,
            MontoUsd = dto.MontoUsd,
            FechaPago = DateTime.UtcNow,
            // Fijado por el servidor, no por el cliente: desde el paso a
            // transferencia única no hay nada que elegir.
            MetodoPago = "Transferencia",
            Estado = EstadoPago.Pendiente,
            Responsable = dto.Responsable.Trim(),
            Observaciones = dto.Observaciones
        };

        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        return Mapear(pago, productora.NombreCompleto, lote.CodigoLote);
```

Borrar el bloque `esCredito` / `numeroDias` / `valorPorDia` entero.

- [ ] **Step 6: Actualizar `Mapear`**

```csharp
    private static PagoResponseDto Mapear(
        Pago p, string nombreProductora, string? codigoLote) => new(
        Id: p.Id,
        ProductoraId: p.ProductoraId,
        NombreProductora: nombreProductora,
        LoteId: p.LoteId,
        CodigoLote: codigoLote,
        MontoUsd: p.MontoUsd,
        FechaPago: p.FechaPago,
        MetodoPago: p.MetodoPago,
        Estado: p.Estado.ToString(),
        MontoPagadoUsd: p.MontoPagadoUsd,
        FechaPagoEfectivo: p.FechaPagoEfectivo,
        PagadoPor: p.PagadoPor,
        TieneComprobante: p.ComprobanteUrl != null,
        FechaVerificacion: p.FechaVerificacion,
        VerificadoPor: p.VerificadoPor,
        Responsable: p.Responsable,
        Observaciones: p.Observaciones
    );
```

- [ ] **Step 7: Actualizar el `using` de `Program.cs`**

Cambiar el `using` del servicio de pagos a `using CoopagcuyApi.Features.Pagos.Services;`. La línea `builder.Services.AddScoped<IPagoService, PagoService>();` no cambia.

- [ ] **Step 8: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~EmisionTicketTests" --logger "console;verbosity=normal"
```

Expected: PASS, 2 pruebas.

- [ ] **Step 9: Batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Expected: PASS en todas. Si alguna prueba antigua de pagos falla por `numeroDias`, actualizarla: el crédito ya no existe.

- [ ] **Step 10: Commit**

```bash
git add Features Program.cs tests
git commit -m "feat: el pago se emite siempre como transferencia y exige lote"
```

---

### Task 4: El ticket en PDF de 80 mm

**Files:**
- Create: `Features/Pagos/Services/TicketPagoService.cs`
- Modify: `Features/Pagos/Controllers/PagosController.cs`
- Modify: `Program.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs`

**Interfaces:**
- Consumes: `Pago`, `EstadoPago` (Task 1); `PagoService` (Task 3).
- Produces: `ITicketPagoService.GenerarAsync(int pagoId) → Task<byte[]>`; los estáticos `TicketPagoService.TextoEstado(EstadoPago) → string` y `TicketPagoService.LeyendaLegal() → string`; endpoint `GET /api/pagos/{id}/ticket`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Services;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Ticket imprimible del pago pendiente.
///
/// Del binario del PDF no se puede afirmar casi nada: QuestPDF comprime su
/// texto. Por eso las líneas cuyo contenido depende de una regla se componen
/// en métodos estáticos y se fijan por unidad, igual que hace la guía de
/// movilización con TextoDeclaracionSanitaria.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class TicketPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    [Theory]
    [InlineData(EstadoPago.Pendiente, "PENDIENTE DE PAGO")]
    [InlineData(EstadoPago.Pagado, "PAGADO — POR VERIFICAR")]
    [InlineData(EstadoPago.Recibido, "PAGO VERIFICADO")]
    public void ElEstadoSeImprimeEnCastellanoYEnMayusculas(
        EstadoPago estado, string esperado)
    {
        TicketPagoService.TextoEstado(estado).ShouldBe(esperado);
    }

    [Fact]
    public void LaLeyendaAclaraQueNoEsFactura()
    {
        // La productora se lleva este papel. Si parece una factura, lo será
        // para ella — y no lo es.
        TicketPagoService.LeyendaLegal()
            .ShouldContain("no es una factura", Case.Insensitive);
    }

    [Fact]
    public async Task ElTicketSeDescargaComoPdfNoVacio()
    {
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        // Cabecera de PDF: %PDF
        bytes[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task ElOperadorDeFaenamientoTambienPuedeDescargarlo()
    {
        // Es quien va a pagar: necesita ver el ticket que tiene delante la
        // productora.
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnOperadorDeOtroCentroRecibe404()
    {
        // 404 y no 403: confirmar que el pago existe ya filtraría información
        // de otro CAT.
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// Entrega real de 3 cuyes en PAT + su ticket de $120. Devuelve el Id.
    private async Task<int> PagoSembradoAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 3).Select(_ => new
        {
            pesoGramos = 1300m,
            colorPelaje = "Blanco",
            estadoOreja = "Blanda",
            tamanoAnimal = "Normal"
        }).ToArray();

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TicketPagoTests" --logger "console;verbosity=normal"
```

Expected: FALLO de compilación — `'TicketPagoService' could not be found`.

- [ ] **Step 3: Escribir el generador**

Crear `Features/Pagos/Services/TicketPagoService.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CoopagcuyApi.Features.Pagos.Services;

public interface ITicketPagoService
{
    Task<byte[]> GenerarAsync(int pagoId);
}

/// <summary>
/// Comprobante impreso que la productora se lleva del centro de acopio.
///
/// Se genera con ancho continuo de 80 mm y alto variable porque el papel
/// térmico no tiene páginas: fijar un alto dejaría avances en blanco al final
/// de cada ticket, o cortaría el pie.
/// </summary>
public class TicketPagoService(AppDbContext db) : ITicketPagoService
{
    // Ancho del rollo. 80 mm es el estándar de las impresoras de recibos de
    // punto de venta; el contenido se compone para ~42 caracteres por línea.
    private const float AnchoMm = 80f;

    // Márgenes estrechos: el cabezal térmico no imprime en los bordes, pero
    // más de 3 mm desperdicia un ancho que ya es escaso.
    private const float MargenMm = 3f;

    /// <summary>
    /// Estado del pago tal y como se imprime. Público y estático para poder
    /// fijarlo por unidad: del binario del PDF no se puede afirmar nada.
    /// </summary>
    public static string TextoEstado(EstadoPago estado) => estado switch
    {
        EstadoPago.Pendiente => "PENDIENTE DE PAGO",
        EstadoPago.Pagado => "PAGADO — POR VERIFICAR",
        EstadoPago.Recibido => "PAGO VERIFICADO",
        _ => "ESTADO DESCONOCIDO"
    };

    /// <summary>
    /// Aclaración al pie. La productora se lleva este papel: si parece una
    /// factura, para ella lo será.
    /// </summary>
    public static string LeyendaLegal() =>
        "Este documento acredita un pago pendiente de la cooperativa. " +
        "No es una factura ni un comprobante tributario.";

    public async Task<byte[]> GenerarAsync(int pagoId)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pago = await db.Pagos
            .Include(p => p.Productora).ThenInclude(pr => pr.Comunidad)
            .Include(p => p.Lote)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        // Aporte de ESTA productora a la jaula, no el total: la jaula es
        // multi-productora y el ticket es de una sola.
        var cuyes = pago.LoteId is int loteId
            ? await db.CuyRegistros
                .Where(c => c.LoteId == loteId && c.ProductoraId == pago.ProductoraId)
                .Select(c => c.PesoGramos)
                .ToListAsync()
            : [];

        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(page =>
            {
                page.ContinuousSize(AnchoMm, Unit.Millimetre);
                page.Margin(MargenMm, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontSize(8).FontFamily(Fonts.Calibri));

                page.Content().Column(col =>
                {
                    col.Spacing(3);

                    col.Item().AlignCenter().Text("COOPAGCUY")
                        .FontSize(13).Bold();
                    col.Item().AlignCenter().Text("Comprobante de pago")
                        .FontSize(8);
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text($"Ticket N.º {pago.Id:D6}").Bold();
                    col.Item().Text(
                        $"Emitido: {pago.FechaPago:dd/MM/yyyy HH:mm}");
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text("PRODUCTORA").Bold();
                    col.Item().Text(pago.Productora.NombreCompleto);
                    col.Item().Text($"C.I. {pago.Productora.Cedula}");
                    col.Item().Text(pago.Productora.Comunidad.Nombre);
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text("LOTE").Bold();
                    col.Item().Text(pago.Lote?.CodigoLote ?? "—");
                    col.Item().Text(
                        $"Centro: {pago.Lote?.CentroAcopio.ToString() ?? "—"}");
                    col.Item().Text(
                        $"Recibido: {pago.Lote?.FechaRecepcion:dd/MM/yyyy}");
                    col.Item().Text($"Cuyes aportados: {cuyes.Count}");
                    col.Item().Text($"Peso total: {cuyes.Sum():N0} g");
                    col.Item().LineHorizontal(0.5f);

                    col.Item().AlignCenter().Text($"USD {pago.MontoUsd:N2}")
                        .FontSize(18).Bold();
                    col.Item().AlignCenter().Text(TextoEstado(pago.Estado))
                        .FontSize(9).Bold();

                    col.Item().LineHorizontal(0.5f);
                    col.Item().Text($"Responsable: {pago.Responsable}").FontSize(7);
                    col.Item().Text(LeyendaLegal()).FontSize(6);
                });
            });
        });

        return documento.GeneratePdf();
    }
}
```

- [ ] **Step 4: Añadir el endpoint**

En `Features/Pagos/Controllers/PagosController.cs`, cambiar la firma de la clase a
`public class PagosController(IPagoService service, ITicketPagoService tickets) : ControllerBase`
y añadir:

```csharp
    /// <summary>
    /// Ticket imprimible. Lo descarga tanto el CAT que lo emite como la planta
    /// que va a pagarlo, así que el rol de faenamiento entra aquí — pero sin
    /// acotarse por centro: la planta es única y atiende a los tres CAT.
    /// </summary>
    [HttpGet("{id:int}/ticket")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> Ticket(int id)
    {
        var filtro = FiltroCat();
        if (filtro is CentroAcopio cat)
        {
            var suyo = await service.EsDeCentroAsync(id, cat);
            // 404 y no 403: confirmar que existe ya sería filtrar el dato
            if (!suyo) return NotFound();
        }

        try
        {
            var pdf = await tickets.GenerarAsync(id);
            return File(pdf, "application/pdf", $"ticket-{id:D6}.pdf");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
```

- [ ] **Step 5: Añadir `EsDeCentroAsync` al servicio**

En `IPagoService`:

```csharp
    /// Si el pago pertenece a una productora de ese centro. Sirve para
    /// responder 404 sin revelar la existencia del recurso.
    Task<bool> EsDeCentroAsync(int pagoId, CentroAcopio cat);
```

En `PagoService`:

```csharp
    public Task<bool> EsDeCentroAsync(int pagoId, CentroAcopio cat) =>
        db.Pagos.AnyAsync(p => p.Id == pagoId && p.Productora.CatAsignado == cat);
```

- [ ] **Step 6: Registrar en DI**

En `Program.cs`, junto a `AddScoped<IPagoService, PagoService>()`:

```csharp
builder.Services.AddScoped<ITicketPagoService, TicketPagoService>();
```

- [ ] **Step 7: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TicketPagoTests" --logger "console;verbosity=normal"
```

Expected: PASS, 7 pruebas (3 del `Theory` + 4 `Fact`).

Si `ElTicketSeDescargaComoPdfNoVacio` falla con un PDF de menos de 1000 bytes, faltan las fuentes: `Dockerfile.tests` ya las instala, así que el problema es que la prueba corrió fuera del compose.

- [ ] **Step 8: Commit**

```bash
git add Features/Pagos Program.cs tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs
git commit -m "feat: ticket de pago imprimible en 80 mm"
```

---

### Task 5: Front — botón único de transferencia

**Files:**
- Modify: `src/components/recepcion/FormPago.tsx`
- Modify: `src/types/productora.ts`
- Create: `src/api/pagos.ts`
- Modify: `src/api/productoras.ts` (retirar `pagosApi`)

**Interfaces:**
- Consumes: `RegistrarPagoDto` y `PagoResponseDto` de la Task 3.
- Produces: `pagosApi` en su propio módulo con `registrar`, `listar`, `lotesPendientes`, `descargarTicket`; tipo `Pago` con `estado`, `montoPagadoUsd`, `tieneComprobante`.

- [ ] **Step 1: Actualizar los tipos**

En `src/types/productora.ts`, sustituir `RegistrarPagoRequest` y `Pago`:

```typescript
export interface RegistrarPagoRequest {
    productoraId: number;
    // Obligatorio: el ticket es por los cuyes de un lote concreto
    loteId: number;
    montoUsd: number;
    responsable: string;
    observaciones?: string;
}

export type EstadoPago = "Pendiente" | "Pagado" | "Recibido";

export interface Pago {
    id: number;
    productoraId: number;
    nombreProductora: string;
    loteId: number | null;
    codigoLote: string | null;
    montoUsd: number;
    fechaPago: string;
    metodoPago: string;
    estado: EstadoPago;
    montoPagadoUsd: number | null;
    fechaPagoEfectivo: string | null;
    pagadoPor: string | null;
    tieneComprobante: boolean;
    fechaVerificacion: string | null;
    verificadoPor: string | null;
    responsable: string;
    observaciones: string | null;
}
```

- [ ] **Step 2: Extraer el cliente de pagos**

Crear `src/api/pagos.ts`:

```typescript
import { client } from "./client";
import type {
    Pago, RegistrarPagoRequest, LotePendientePago,
} from "../types/productora";

export const pagosApi = {
    registrar: async (body: RegistrarPagoRequest) => {
        const { data } = await client.post<Pago>("/api/pagos", body);
        return data;
    },

    listar: async (params?: {
        productoraId?: number; desde?: string; hasta?: string;
    }) => {
        const { data } = await client.get<Pago[]>("/api/pagos", { params });
        return data;
    },

    // Lotes por los que aún se le debe a la productora: el servidor ya excluye
    // los que ella tiene pagados, así que un lote pagado no vuelve a ofrecerse
    lotesPendientes: async (productoraId: number) => {
        const { data } = await client.get<LotePendientePago[]>(
            `/api/pagos/lotes-pendientes/${productoraId}`
        );
        return data;
    },

    // Pasa por `client` y no por una URL directa para que el interceptor
    // adjunte el Bearer: el token vive en memoria, no en una cookie.
    descargarTicket: async (pagoId: number): Promise<Blob> => {
        const { data } = await client.get<Blob>(
            `/api/pagos/${pagoId}/ticket`, { responseType: "blob" });
        return data;
    },
};
```

Retirar el bloque `pagosApi` de `src/api/productoras.ts` y sus imports ya sin uso.

- [ ] **Step 3: Reescribir el selector de método en `FormPago.tsx`**

Sustituir la constante `METODOS` (líneas 13-16) por nada, y el bloque `<div>` del "¿Cómo se pagó?" (líneas 186-207) por:

```tsx
                    <div>
                        <p className="text-xs font-bold uppercase tracking-wide
                          text-gray-500 mb-2">
                            Forma de pago
                        </p>
                        {/* Un solo botón, siempre activo: desde el paso a
                            transferencia única no hay nada que elegir. Se
                            mantiene visible —y no como texto suelto— para que
                            la operadora vea con qué se va a registrar. */}
                        <div className="h-12 rounded-xl border-2 border-primary-600
                            bg-primary-50 text-primary-800 text-sm font-semibold
                            flex items-center justify-center gap-2">
                            <span aria-hidden="true">🏦</span>
                            Transferencia bancaria
                        </div>
                    </div>
```

Borrar el bloque entero de `{esCredito && (…)}` (líneas 209-244), la constante `esCredito` y la constante `valorPorDia`.

- [ ] **Step 4: Ajustar el estado del formulario**

```tsx
    const [form, setForm] = useState<RegistrarPagoRequest>({
        productoraId: 0,
        loteId: 0,
        montoUsd: 0,
        responsable: auth.nombreCompleto ?? "",
        observaciones: "",
    });
```

En la mutación, quitar el `numeroDias`:

```tsx
    const mutation = useMutation({
        mutationFn: () => pagosApi.registrar(form),
```

En `handleSubmit`, añadir la validación del lote, que ahora es obligatorio:

```tsx
        if (form.loteId === 0) {
            setError("Selecciona el lote por el que se paga.");
            return;
        }
```

En el `<select>` del lote, quitar la opción "Sin lote específico" y el `?? 0`:

```tsx
                        <select
                            required
                            value={form.loteId}
                            onChange={(e) => setForm({
                                ...form, loteId: Number(e.target.value),
                            })}
                            disabled={form.productoraId === 0}
                            className="w-full h-11 px-3 rounded-xl border-2 border-gray-200
                         text-sm focus:border-primary-500 focus:outline-none
                         disabled:bg-gray-50 disabled:text-gray-400"
                        >
                            <option value={0}>Seleccionar lote…</option>
```

Cambiar también la etiqueta: `Lote pendiente de pago (opcional)` → `Lote por el que se paga`.

Actualizar el import: `import { pagosApi } from "../../api/pagos";`.

- [ ] **Step 5: Verificar**

```bash
cd ../CoopagcuyFront/coopagcuy-frontend && pnpm lint && pnpm exec tsc -b && pnpm build
```

Expected: los tres con salida 0.

- [ ] **Step 6: Commit**

```bash
git add src/components/recepcion/FormPago.tsx src/types/productora.ts src/api/pagos.ts src/api/productoras.ts
git commit -m "feat: el pago se registra siempre como transferencia"
```

---

### Task 6: Front — imprimir el ticket

**Files:**
- Modify: `src/pages/Recepcion.tsx`
- Modify: `src/components/recepcion/FormPago.tsx`

**Interfaces:**
- Consumes: `pagosApi.descargarTicket` (Task 5); endpoint del ticket (Task 4).
- Produces: la función `imprimirTicket(pagoId: number): Promise<void>`, reutilizada en la Task 15.

- [ ] **Step 1: Añadir el helper de impresión**

Crear `src/api/imprimirTicket.ts`:

```typescript
import { pagosApi } from "./pagos";

/**
 * Descarga el ticket y lo abre para imprimir.
 *
 * Va por el cliente autenticado y no por un enlace directo: el access token
 * vive en memoria y lo pone un interceptor, así que un `<a href>` al endpoint
 * recibiría 401.
 *
 * El object URL se revoca tras un minuto y no de inmediato: revocarlo al
 * instante cierra la pestaña recién abierta antes de que el navegador termine
 * de renderizar el PDF.
 */
export async function imprimirTicket(pagoId: number): Promise<void> {
    const blob = await pagosApi.descargarTicket(pagoId);
    const url = URL.createObjectURL(blob);
    window.open(url, "_blank", "noopener");
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
```

- [ ] **Step 2: Imprimir al terminar de registrar**

En `FormPago.tsx`, en `onSuccess` de la mutación:

```tsx
        onSuccess: async (pago) => {
            qc.invalidateQueries({ queryKey: ["pagos"] });
            // El lote recién pagado debe desaparecer del selector
            qc.invalidateQueries({ queryKey: ["lotes_pendientes_pago"] });
            // La productora está delante esperando su papel: imprimir aquí
            // ahorra que la operadora tenga que buscar la fila después.
            // Si falla la impresión el pago YA está registrado, así que no
            // se propaga el error: se cierra igual y queda el botón de la
            // lista para reintentar.
            try {
                await imprimirTicket(pago.id);
            } catch {
                // Sin ruido: el ticket se puede reimprimir desde la lista
            }
            onClose();
        },
```

Añadir `import { imprimirTicket } from "../../api/imprimirTicket";`.

- [ ] **Step 3: Botón de reimpresión en la lista**

En `src/pages/Recepcion.tsx`, dentro de la pestaña `pagos`, en cada fila de la tabla añadir una celda:

```tsx
                                        <td className="px-3 py-2 text-right">
                                            <button
                                                type="button"
                                                onClick={() => void imprimirTicket(p.id)}
                                                title="Reimprimir el ticket"
                                                className="min-h-[44px] px-3 rounded-xl
                                                    border-2 border-gray-200 bg-white
                                                    text-xs font-bold text-gray-700
                                                    hover:bg-gray-50 active:scale-95
                                                    transition"
                                            >
                                                🧾 Ticket
                                            </button>
                                        </td>
```

Y su cabecera correspondiente `<th />`. Añadir el import de `imprimirTicket`.

- [ ] **Step 4: Verificar**

```bash
cd ../CoopagcuyFront/coopagcuy-frontend && pnpm lint && pnpm exec tsc -b && pnpm build
```

Expected: los tres con salida 0.

- [ ] **Step 5: Commit**

```bash
git add src/api/imprimirTicket.ts src/components/recepcion/FormPago.tsx src/pages/Recepcion.tsx
git commit -m "feat: el ticket se imprime al registrar el pago y desde la lista"
```

---

# FASE B — Pago desde faenamiento

Al terminar esta fase la planta ya transfiere, descuenta con justificación y sube su captura.

---

### Task 7: Contenedor de comprobantes y política de 30 días

**Files:**
- Modify: `Infrastructure/Storage/BlobStorageService.cs`
- Modify: `infra/politica-evidencias.json`
- Modify: `appsettings.json`
- Test: `tests/CoopagcuyApi.Tests/Integracion/ComprobantePagoTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `IBlobStorageService.SubirComprobanteAsync(string nombre, byte[] imagen) → Task<string>`, `DescargarComprobanteAsync(string nombre) → Task<byte[]?>`, `BorrarComprobanteAsync(string nombre) → Task`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/ComprobantePagoTests.cs`:

```csharp
using System.Text;
using CoopagcuyApi.Infrastructure.Storage;
using CoopagcuyApi.Tests.Infra;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Contenedor de capturas de transferencia. Separado del de evidencias
/// clínicas porque la política de caducidad se aplica POR CONTENEDOR y los
/// plazos son distintos: compartirlo borraría las evidencias a los 30 días.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ComprobantePagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static IBlobStorageService ServicioBlob(string? contenedor = null)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureBlob:ConnectionString"] = ApiFactory.CadenaBlob,
                ["AzureBlob:ContainerComprobantes"] = contenedor
            })
            .Build();

        return new BlobStorageService(configuracion);
    }

    [Fact]
    public async Task ElComprobanteSubeYVuelveIgual()
    {
        var servicio = ServicioBlob("comprobantes-test");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";
        var contenido = Encoding.UTF8.GetBytes("captura-de-transferencia");

        await servicio.SubirComprobanteAsync(nombre, contenido);
        var recuperado = await servicio.DescargarComprobanteAsync(nombre);

        recuperado.ShouldBe(contenido);
    }

    [Fact]
    public async Task ConElNombreDeContenedorVacioSeUsaElPorDefecto()
    {
        // Misma trampa que costó un 500 en producción el 2026-08-20:
        // appsettings.json declara la clave con cadena VACÍA y `??` solo
        // cubre null. Con `??` la URL saldría sin contenedor.
        var servicio = ServicioBlob("");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";

        var uri = await servicio.SubirComprobanteAsync(
            nombre, Encoding.UTF8.GetBytes("respaldo"));

        uri.ShouldContain("/comprobantes-pago/");
    }

    [Fact]
    public async Task BorrarUnComprobanteInexistenteNoRevienta()
    {
        // El barrido oportunista puede intentar borrar dos veces el mismo
        // blob si dos consultas coinciden. No puede tumbar la petición.
        var servicio = ServicioBlob("comprobantes-test");

        await Should.NotThrowAsync(() =>
            servicio.BorrarComprobanteAsync($"no-existe-{Guid.NewGuid():N}.jpg"));
    }

    [Fact]
    public async Task DescargarUnComprobanteBorradoDevuelveNulo()
    {
        var servicio = ServicioBlob("comprobantes-test");
        var nombre = $"prueba-{Guid.NewGuid():N}.jpg";

        await servicio.SubirComprobanteAsync(
            nombre, Encoding.UTF8.GetBytes("captura"));
        await servicio.BorrarComprobanteAsync(nombre);

        var recuperado = await servicio.DescargarComprobanteAsync(nombre);

        recuperado.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ComprobantePagoTests" --logger "console;verbosity=normal"
```

Expected: FALLO de compilación — `'IBlobStorageService' does not contain a definition for 'SubirComprobanteAsync'`.

- [ ] **Step 3: Ampliar la interfaz**

En `Infrastructure/Storage/BlobStorageService.cs`, dentro de `IBlobStorageService`:

```csharp
    /// Sube una captura de transferencia al contenedor PRIVADO de
    /// comprobantes y devuelve su URI.
    Task<string> SubirComprobanteAsync(string nombre, byte[] imagen);

    /// Bytes de la captura, o null si el blob ya no existe.
    Task<byte[]?> DescargarComprobanteAsync(string nombre);

    /// Borra la captura. No lanza si ya no está: el barrido oportunista
    /// puede pisarse consigo mismo y no puede tumbar la petición que lo
    /// dispara.
    Task BorrarComprobanteAsync(string nombre);
```

- [ ] **Step 4: Implementar**

En la clase, junto a `_containerEvidencias`:

```csharp
    // Tercer contenedor, y no una carpeta dentro de evidencias: la política
    // de ciclo de vida se aplica POR CONTENEDOR. Compartirlo borraría las
    // evidencias clínicas a los 30 días en vez de a los 90.
    //
    // IsNullOrWhiteSpace y no `??`, por lo mismo que los otros dos.
    private readonly string _containerComprobantes =
        !string.IsNullOrWhiteSpace(configuration["AzureBlob:ContainerComprobantes"])
            ? configuration["AzureBlob:ContainerComprobantes"]!
            : "comprobantes-pago";
```

Y los tres métodos:

```csharp
    public async Task<string> SubirComprobanteAsync(string nombre, byte[] imagen)
    {
        var contenedor = await ContenedorComprobantesAsync();
        var blob = contenedor.GetBlobClient(nombre);

        using var stream = new MemoryStream(imagen);
        await blob.UploadAsync(stream, overwrite: true);

        return blob.Uri.ToString();
    }

    public async Task<byte[]?> DescargarComprobanteAsync(string nombre)
    {
        var contenedor = await ContenedorComprobantesAsync();
        var blob = contenedor.GetBlobClient(nombre);

        try
        {
            var respuesta = await blob.DownloadContentAsync();
            return respuesta.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Ya lo borró el barrido o la política de Azure. No es un error:
            // la fila del pago sobrevive al binario por diseño.
            return null;
        }
    }

    public async Task BorrarComprobanteAsync(string nombre)
    {
        var contenedor = await ContenedorComprobantesAsync();
        // DeleteIfExists y no Delete: dos consultas simultáneas pueden barrer
        // el mismo blob, y la segunda no puede reventar por llegar tarde.
        await contenedor.GetBlobClient(nombre).DeleteIfExistsAsync();
    }

    private async Task<BlobContainerClient> ContenedorComprobantesAsync()
    {
        var cliente = new BlobServiceClient(_connectionString);
        var contenedor = cliente.GetBlobContainerClient(_containerComprobantes);

        // None y no Blob: una captura de transferencia bancaria no puede ser
        // pública. Se sirve solo por el endpoint autenticado del API.
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.None);
        return contenedor;
    }
```

- [ ] **Step 5: Declarar la clave en `appsettings.json`**

En la sección `AzureBlob`, junto a `ContainerEvidencias`:

```json
    "ContainerComprobantes": "",
```

- [ ] **Step 6: Añadir la regla de ciclo de vida**

En `infra/politica-evidencias.json`, dentro de `rules`, añadir como segundo elemento:

```json
    {
      "enabled": true,
      "name": "borrar-comprobantes-pago-30d",
      "type": "Lifecycle",
      "definition": {
        "filters": {
          "blobTypes": [ "blockBlob" ],
          "prefixMatch": [ "comprobantes-pago/" ]
        },
        "actions": {
          "baseBlob": {
            "delete": { "daysAfterCreationGreaterThan": 30 }
          }
        }
      }
    }
```

Esta es la **red de seguridad**: cubre el caso de que nadie verifique nunca el pago. El borrado a los 5 días de la verificación lo hace el barrido de la Task 14, no esta regla — una política de Azure solo sabe de fechas de subida, no de eventos.

- [ ] **Step 7: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ComprobantePagoTests" --logger "console;verbosity=normal"
```

Expected: PASS, 4 pruebas.

- [ ] **Step 8: Commit**

```bash
git add Infrastructure/Storage/BlobStorageService.cs appsettings.json infra/politica-evidencias.json tests/CoopagcuyApi.Tests/Integracion/ComprobantePagoTests.cs
git commit -m "feat: contenedor propio para las capturas de transferencia"
```

**Nota para quien despliegue:** la política se aplica a mano, una sola vez, con el comando ya documentado en `infra/bootstrap.azcli`. No es parte del despliegue automático.

---

### Task 8: Bandeja de la planta y cuyes con novedad

**Files:**
- Modify: `Features/Pagos/DTOs/PagoDtos.cs`
- Modify: `Features/Pagos/Services/PagoService.cs`
- Modify: `Features/Pagos/Controllers/PagosController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/BandejaPlantaTests.cs`

**Interfaces:**
- Consumes: `Pago`, `EstadoPago` (Task 1); `DescuentoPago` (Task 1).
- Produces: `TicketPorPagarDto(int PagoId, int ProductoraId, string NombreProductora, string Cedula, int LoteId, string CodigoLote, string CentroAcopio, DateTime FechaRecepcion, int CuyesEntregados, decimal MontoUsd, DateTime FechaEmision)`; `CuyConNovedadDto(int CuyRegistroId, int NumeroEnLote, decimal PesoGramos, int NovedadId, string TipoNovedad, string Descripcion, bool TieneFoto)`; `IPagoService.ListarPorPagarAsync()` y `ListarCuyesConNovedadAsync(int pagoId)`; endpoints `GET /api/pagos/por-pagar` y `GET /api/pagos/{id}/cuyes-con-novedad`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/BandejaPlantaTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Lo que ve el operador de faenamiento: los tickets que le toca pagar, de
/// los TRES centros de acopio, y los cuyes con novedad de cada uno.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BandejaPlantaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Dos cédulas válidas distintas: una productora por centro
    private const string CedulaPat = "0104576277";
    private const string CedulaNie = "0102030405";

    [Fact]
    public async Task LaPlantaVeLosTicketsDeTodosLosCentros()
    {
        // La planta es única y atiende a los tres CAT: acotarla por centro
        // la dejaría sin ver la mitad de lo que tiene que pagar.
        await PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m);
        await PagoSembradoAsync(CentroAcopio.NIE, CedulaNie, 90m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<TicketPorPagarDto>>("/api/pagos/por-pagar");

        respuesta.ShouldNotBeNull();
        respuesta.Count.ShouldBe(2);
        respuesta.Select(t => t.CentroAcopio)
            .ShouldBe(new[] { "PAT", "NIE" }, ignoreOrder: true);
    }

    [Fact]
    public async Task UnTicketYaPagadoDesapareceDeLaBandeja()
    {
        var pagoId = await PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m);

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.Estado = EstadoPago.Pagado;
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<TicketPorPagarDto>>("/api/pagos/por-pagar");

        respuesta!.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaOperadoraDeCatNoEntraALaBandejaDeLaPlanta()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SoloSeListanLosCuyesConNovedadDeEsaProductora()
    {
        // 3 cuyes: uno con signos clínicos, dos sanos. Y una productora
        // distinta en el mismo lote con otro cuy con novedad, que NO debe
        // aparecer: el ticket es de una sola productora.
        var pagoId = await PagoConNovedadAsync();

        var cuyes = await api.ComoOperadorFaenamiento()
            .GetFromJsonAsync<List<CuyConNovedadDto>>(
                $"/api/pagos/{pagoId}/cuyes-con-novedad");

        cuyes.ShouldNotBeNull();
        cuyes.Count.ShouldBe(1);
        cuyes[0].TipoNovedad.ShouldBe("SignosClinicos");
        cuyes[0].Descripcion.ShouldContain("lesion-visible");
    }

    /// Entrega + ticket. Devuelve el Id del pago.
    private async Task<int> PagoSembradoAsync(
        CentroAcopio cat, string cedula, decimal monto, string? signos = null)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, cat, comunidadId: cat == CentroAcopio.PAT ? 1 : 2);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = signos },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = cat.ToString(),
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }

    private Task<int> PagoConNovedadAsync() =>
        PagoSembradoAsync(CentroAcopio.PAT, CedulaPat, 120m, "lesion-visible");
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~BandejaPlantaTests" --logger "console;verbosity=normal"
```

Expected: FALLO de compilación — `'TicketPorPagarDto' could not be found`.

- [ ] **Step 3: Añadir los DTO**

Al final de `Features/Pagos/DTOs/PagoDtos.cs`:

```csharp
/// <summary>
/// Ticket que la planta tiene pendiente de pagar. Sin filtro por centro: la
/// planta es única y atiende a los tres CAT.
/// </summary>
public record TicketPorPagarDto(
    int PagoId,
    int ProductoraId,
    string NombreProductora,
    string Cedula,
    int LoteId,
    string CodigoLote,
    string CentroAcopio,
    DateTime FechaRecepcion,
    int CuyesEntregados,
    decimal MontoUsd,
    DateTime FechaEmision
);

/// <summary>
/// Cuy de esa productora en ese lote que llegó con novedad del CAT. Es la
/// única lista sobre la que la planta puede descontar: `NovedadId` es lo que
/// después va a `DescuentoPago.NovedadCatId`.
/// </summary>
public record CuyConNovedadDto(
    int CuyRegistroId,
    int NumeroEnLote,
    decimal PesoGramos,
    int NovedadId,
    string TipoNovedad,
    string Descripcion,
    // Para decidir si pintar el visor de la foto sin pedirla de más
    bool TieneFoto
);
```

- [ ] **Step 4: Implementar en el servicio**

En `IPagoService`:

```csharp
    /// Tickets pendientes de pago, de TODOS los centros.
    Task<IEnumerable<TicketPorPagarDto>> ListarPorPagarAsync();

    /// Cuyes de esa productora en ese lote que traen novedad del CAT.
    Task<IEnumerable<CuyConNovedadDto>> ListarCuyesConNovedadAsync(int pagoId);
```

En `PagoService`:

```csharp
    public async Task<IEnumerable<TicketPorPagarDto>> ListarPorPagarAsync() =>
        await db.Pagos
            .Where(p => p.Estado == EstadoPago.Pendiente && p.LoteId != null)
            .OrderBy(p => p.FechaPago)
            .Select(p => new TicketPorPagarDto(
                p.Id,
                p.ProductoraId,
                p.Productora.NombreCompleto,
                p.Productora.Cedula,
                p.LoteId!.Value,
                p.Lote!.CodigoLote,
                p.Lote.CentroAcopio.ToString(),
                p.Lote.FechaRecepcion,
                // Aporte de ESTA productora, no el total de la jaula
                p.Lote.Cuyes.Count(c => c.ProductoraId == p.ProductoraId),
                p.MontoUsd,
                p.FechaPago))
            .AsNoTracking()
            .ToListAsync();

    public async Task<IEnumerable<CuyConNovedadDto>> ListarCuyesConNovedadAsync(
        int pagoId)
    {
        var pago = await db.Pagos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.LoteId is not int loteId)
            return [];

        var ahora = DateTime.UtcNow;

        // Se parte de Novedades y no de CuyRegistros porque lo que la planta
        // necesita es el Id de la NOVEDAD: es lo que va a citar al descontar.
        return await db.Novedades
            .Where(n => n.LoteId == loteId
                && n.CuyRegistro != null
                && n.CuyRegistro.ProductoraId == pago.ProductoraId)
            .OrderBy(n => n.CuyRegistro!.NumeroEnLote)
            .Select(n => new CuyConNovedadDto(
                n.CuyRegistroId!.Value,
                n.CuyRegistro!.NumeroEnLote,
                n.CuyRegistro.PesoGramos,
                n.Id,
                n.Tipo.ToString(),
                n.Descripcion,
                n.FotoUrl != null && n.FotoExpiraEn > ahora))
            .AsNoTracking()
            .ToListAsync();
    }
```

- [ ] **Step 5: Añadir los endpoints**

En `PagosController`:

```csharp
    /// <summary>
    /// Bandeja de la planta. Deliberadamente distinta de `lotes-pendientes`,
    /// que es de la CAT y responde a otra pregunta: qué lotes le faltan por
    /// cobrar a una productora, no qué tickets le tocan pagar a la planta.
    /// Sin FiltroCat: la planta atiende a los tres centros.
    /// </summary>
    [HttpGet("por-pagar")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> PorPagar() =>
        Ok(await service.ListarPorPagarAsync());

    [HttpGet("{id:int}/cuyes-con-novedad")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> CuyesConNovedad(int id)
    {
        try
        {
            return Ok(await service.ListarCuyesConNovedadAsync(id));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
```

**Importante:** el `[Authorize(Roles = "OperadorCAT,AdminCooperativa")]` de la clase debe seguir presente para los endpoints que no declaran el suyo. Los atributos de método lo sustituyen, no lo acumulan.

- [ ] **Step 6: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~BandejaPlantaTests" --logger "console;verbosity=normal"
```

Expected: PASS, 4 pruebas.

- [ ] **Step 7: Commit**

```bash
git add Features/Pagos tests/CoopagcuyApi.Tests/Integracion/BandejaPlantaTests.cs
git commit -m "feat: bandeja de tickets por pagar y cuyes con novedad"
```

---

### Task 9: Registrar el pago con descuentos trazables

Es el núcleo de la feature. Cuatro reglas de validación, una subida de blob y una transición de estado, todo en una sola petición.

**Files:**
- Create: `Common/Exceptions/TransicionInvalidaException.cs`
- Modify: `Features/Pagos/DTOs/PagoDtos.cs`
- Modify: `Features/Pagos/Services/PagoService.cs`
- Modify: `Features/Pagos/Controllers/PagosController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/DescuentoTrazableTests.cs`

**Interfaces:**
- Consumes: `CuyConNovedadDto` y `ListarCuyesConNovedadAsync` (Task 8); `SubirComprobanteAsync` (Task 7); `DescuentoPago` (Task 1).
- Produces: `DescuentoDto(int NovedadCatId, string Descripcion, decimal MontoUsd)`; `RegistrarPagoEfectivoDto` con `Descuentos`, `ComprobanteBase64`, `PagadoPor`; `IPagoService.RegistrarPagoEfectivoAsync(int, RegistrarPagoEfectivoDto) → Task<PagoResponseDto>`; endpoint `POST /api/pagos/{id}/pagar`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/DescuentoTrazableTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Azure.Storage.Blobs;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Un descuento solo puede apoyarse en una novedad que el CAT registró sobre
/// un cuy de ESA productora en ESE lote. Sin novedad de origen no hay
/// descuento: es la garantía entera de la feature.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class DescuentoTrazableTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaA = "0104576277";
    private const string CedulaB = "0102030405";

    // JPEG mínimo válido: SOI + APP0 + EOI
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    private static string Comprobante => Convert.ToBase64String(JpegMinimo);

    [Fact]
    public async Task ElMontoPagadoSaleDeLaRestaDelServidor()
    {
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "llegó con la lesión abierta",
                    montoUsd = 17m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);

        pago.MontoPagadoUsd.ShouldBe(103m);
        pago.Estado.ShouldBe(EstadoPago.Pagado);
        pago.ComprobanteUrl.ShouldNotBeNull();
        pago.PagadoPor.ShouldBe("Operador de planta");
    }

    [Fact]
    public async Task UnaNovedadDeOtraProductoraSeRechaza()
    {
        // El corazón de la trazabilidad: la planta no puede citar el defecto
        // de otra para descontarle a esta.
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);
        var (_, novedadAjena) = await TicketConNovedadAsync(CedulaB, 90m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadAjena,
                    descripcion = "defecto que no es de esta productora",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnaNovedadSinCuyAsociadoSeRechaza()
    {
        // Las novedades de entrega (SinAyuno) no pertenecen a ningún animal:
        // no se puede descontar un cuy que no existe.
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);

        int novedadDeEntrega;
        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
            var novedad = new CoopagcuyApi.Features.Recepcion.Models.Novedad
            {
                LoteId = pago.LoteId!.Value,
                Tipo = TipoNovedad.SinAyuno,
                Descripcion = "la entrega no venía en ayunas",
                RegistradoPor = "Operadora de prueba"
            };
            db.Novedades.Add(novedad);
            await db.SaveChangesAsync();
            novedadDeEntrega = novedad.Id;
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadDeEntrega,
                    descripcion = "descuento sin animal",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LaSumaDeDescuentosNoPuedeSuperarElTicket()
    {
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "descuento mayor que el ticket",
                    montoUsd = 500m
                }},
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoSePuedePagarDosVecesElMismoTicket()
    {
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        object Cuerpo() => new
        {
            descuentos = new[] { new
            {
                novedadCatId = novedadId,
                descripcion = "lesión",
                montoUsd = 10m
            }},
            comprobanteBase64 = Comprobante,
            pagadoPor = "Operador de planta"
        };

        var primera = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", Cuerpo());
        primera.StatusCode.ShouldBe(HttpStatusCode.OK);

        var segunda = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", Cuerpo());
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnComprobanteInvalidoNoDejaBlobsHuerfanos()
    {
        // Se cuenta por DIFERENCIA DE BLOBS y no por filas: una prueba que
        // solo mira filas pasa con el fallo presente. Ya ocurrió una vez.
        var (pagoId, novedadId) = await TicketConNovedadAsync(CedulaA, 120m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "lesión",
                    montoUsd = 10m
                }},
                comprobanteBase64 = "esto-no-es-base64-valido!!!",
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ContarBlobsAsync()).ShouldBe(antes);
    }

    [Fact]
    public async Task SinComprobanteNoSePuedeMarcarComoPagado()
    {
        // Un pago marcado sin su captura es peor que un error: la CAT no
        // tendría nada que verificar y el ticket quedaría bloqueado.
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = "",
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SinDescuentosSePagaElTicketCompleto()
    {
        var (pagoId, _) = await TicketConNovedadAsync(CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Comprobante,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.MontoPagadoUsd.ShouldBe(120m);
    }

    private static async Task<int> ContarBlobsAsync()
    {
        var cliente = new BlobServiceClient(ApiFactory.CadenaBlob);
        var contenedor = cliente.GetBlobContainerClient("comprobantes-pago");
        await contenedor.CreateIfNotExistsAsync();

        var total = 0;
        await foreach (var _ in contenedor.GetBlobsAsync()) total++;
        return total;
    }

    /// Entrega con un cuy con signos clínicos + ticket. Devuelve (pagoId, novedadId).
    private async Task<(int PagoId, int NovedadId)> TicketConNovedadAsync(
        string cedula, decimal monto)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "lesion-visible" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId, novedadId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();

            novedadId = await db.Novedades
                .Where(n => n.LoteId == loteId
                    && n.CuyRegistro != null
                    && n.CuyRegistro.ProductoraId == productora.Id
                    && n.Tipo == TipoNovedad.SignosClinicos)
                .Select(n => n.Id)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoId = await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();

        return (pagoId, novedadId);
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~DescuentoTrazableTests" --logger "console;verbosity=normal"
```

Expected: FAIL — todas con 404, el endpoint `/pagar` no existe.

- [ ] **Step 3: Crear la excepción de transición**

Crear `Common/Exceptions/TransicionInvalidaException.cs`:

```csharp
namespace CoopagcuyApi.Common.Exceptions;

/// <summary>
/// Señala un cambio de estado fuera de orden: pagar un ticket ya pagado,
/// verificar uno que nadie ha pagado.
///
/// Excepción propia y no InvalidOperationException porque el controlador
/// necesita distinguirla: el cuerpo de la petición es válido —lo que no
/// encaja es el momento— y eso es 409, no 400.
/// </summary>
public class TransicionInvalidaException(string mensaje) : Exception(mensaje);
```

- [ ] **Step 4: Añadir los DTO**

Al final de `Features/Pagos/DTOs/PagoDtos.cs`:

```csharp
/// <summary>
/// Rebaja que aplica la planta. `NovedadCatId` cita la novedad del CAT que la
/// justifica; sin ella el descuento no se acepta.
/// </summary>
public record DescuentoDto(int NovedadCatId, string Descripcion, decimal MontoUsd);

/// <summary>
/// Pago efectivo por la planta. Descuentos, captura y cambio de estado viajan
/// juntos a propósito: un ticket marcado como pagado sin su comprobante
/// dejaría a la CAT sin nada que verificar.
/// </summary>
public class RegistrarPagoEfectivoDto
{
    public List<DescuentoDto> Descuentos { get; set; } = [];
    public string ComprobanteBase64 { get; set; } = string.Empty;
    public string PagadoPor { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Implementar el servicio**

En `IPagoService`:

```csharp
    /// Registra la transferencia de la planta: valida los descuentos, sube la
    /// captura y pasa el ticket a Pagado. Todo o nada.
    Task<PagoResponseDto> RegistrarPagoEfectivoAsync(
        int pagoId, RegistrarPagoEfectivoDto dto);
```

Cambiar la firma de la clase a
`public class PagoService(AppDbContext db, IBlobStorageService blobs) : IPagoService`
y añadir:

```csharp
    private const int MaxBytesComprobante = 2 * 1024 * 1024;

    public async Task<PagoResponseDto> RegistrarPagoEfectivoAsync(
        int pagoId, RegistrarPagoEfectivoDto dto)
    {
        var pago = await db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.Estado != EstadoPago.Pendiente)
            throw new TransicionInvalidaException(
                $"El ticket ya está en estado {pago.Estado} y no admite un pago nuevo.");

        // ── Validación completa ANTES de tocar el blob ───────────────
        // Si se intercalara, un descuento inválido detectado después de la
        // subida dejaría una captura huérfana que nadie va a limpiar.

        var comprobante = ValidarComprobante(dto.ComprobanteBase64);
        var descuentos = await ValidarDescuentosAsync(pago, dto);

        var total = descuentos.Sum(d => d.MontoUsd);
        if (total > pago.MontoUsd)
            throw new TransicionInvalidaException(
                $"Los descuentos suman {total:N2} y el ticket es de " +
                $"{pago.MontoUsd:N2}. Un pago negativo no significa nada.");

        // ── Subida, fuera de la transacción ──────────────────────────
        // CreateExecutionStrategy REINTENTA el delegado ante fallos
        // transitorios de Neon: subir ahí dentro duplicaría el blob en cada
        // reintento.
        var nombre = $"pago-{pago.Id:D6}-{Guid.NewGuid():N}.jpg";
        var url = await blobs.SubirComprobanteAsync(nombre, comprobante);

        pago.Estado = EstadoPago.Pagado;
        pago.MontoPagadoUsd = pago.MontoUsd - total;
        pago.FechaPagoEfectivo = DateTime.UtcNow;
        pago.PagadoPor = dto.PagadoPor.Trim();
        pago.ComprobanteUrl = url;

        foreach (var d in descuentos) db.Descuentos.Add(d);

        await db.SaveChangesAsync();

        return Mapear(pago, pago.Productora.NombreCompleto, pago.Lote?.CodigoLote);
    }

    /// <summary>
    /// Decodifica y mide la captura. Excepción propia y no ArgumentException:
    /// esta última es la clase padre de ArgumentNullException, y capturarla a
    /// ciegas convertiría cualquier bug ajeno en un 400 que además expone el
    /// mensaje interno de .NET.
    /// </summary>
    private static byte[] ValidarComprobante(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new EvidenciaInvalidaException(
                "El pago debe adjuntar la captura de la transferencia.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new EvidenciaInvalidaException(
                "La captura de la transferencia no es base64 válido.");
        }

        if (bytes.Length > MaxBytesComprobante)
            throw new EvidenciaInvalidaException(
                $"La captura pesa {bytes.Length / 1024} KB y el máximo es " +
                $"{MaxBytesComprobante / 1024} KB.");

        return bytes;
    }

    /// <summary>
    /// Las cuatro reglas del descuento, menos la del tope que necesita la
    /// suma. Devuelve las filas listas para guardar, sin guardarlas.
    /// </summary>
    private async Task<List<DescuentoPago>> ValidarDescuentosAsync(
        Pago pago, RegistrarPagoEfectivoDto dto)
    {
        if (dto.Descuentos.Count == 0) return [];

        var citadas = dto.Descuentos.Select(d => d.NovedadCatId).ToList();

        // Regla 3: no repetir la misma novedad dentro de la propia petición.
        // El índice único cubre el caso de dos peticiones a la vez; esto
        // cubre el de una petición mal formada, con un mensaje legible.
        if (citadas.Distinct().Count() != citadas.Count)
            throw new TransicionInvalidaException(
                "Un mismo defecto no puede descontarse dos veces.");

        // Reglas 1 y 2: la novedad tiene que pertenecer a un cuy de ESA
        // productora en ESE lote. Las novedades sin cuy —las de entrega y las
        // filas históricas— quedan fuera por el CuyRegistro != null.
        var validas = await db.Novedades
            .Where(n => citadas.Contains(n.Id)
                && n.LoteId == pago.LoteId
                && n.CuyRegistro != null
                && n.CuyRegistro.ProductoraId == pago.ProductoraId)
            .Select(n => n.Id)
            .ToListAsync();

        var invalida = citadas.FirstOrDefault(id => !validas.Contains(id));
        if (invalida != 0)
            throw new TransicionInvalidaException(
                $"La novedad {invalida} no corresponde a un cuy de esta " +
                "productora en este lote, así que no puede justificar un " +
                "descuento.");

        foreach (var d in dto.Descuentos)
        {
            if (d.MontoUsd <= 0)
                throw new TransicionInvalidaException(
                    "Un descuento debe ser mayor a cero.");

            if (string.IsNullOrWhiteSpace(d.Descripcion))
                throw new TransicionInvalidaException(
                    "Cada descuento debe decir qué se observó en la planta.");
        }

        return [.. dto.Descuentos.Select(d => new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = d.NovedadCatId,
            Descripcion = d.Descripcion.Trim(),
            MontoUsd = d.MontoUsd,
            RegistradoPor = dto.PagadoPor.Trim()
        })];
    }
```

Añadir los `using` de `CoopagcuyApi.Common.Exceptions` y `CoopagcuyApi.Infrastructure.Storage`.

- [ ] **Step 6: Añadir el endpoint**

En `PagosController`:

```csharp
    [HttpPost("{id:int}/pagar")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> Pagar(
        int id, [FromBody] RegistrarPagoEfectivoDto dto)
    {
        try
        {
            return Ok(await service.RegistrarPagoEfectivoAsync(id, dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (EvidenciaInvalidaException ex)
        {
            // 400: el cuerpo viene mal
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (TransicionInvalidaException ex)
        {
            // 409: el cuerpo está bien, lo que no encaja es el momento
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

Añadir `using CoopagcuyApi.Common.Exceptions;`.

- [ ] **Step 7: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~DescuentoTrazableTests" --logger "console;verbosity=normal"
```

Expected: PASS, 8 pruebas.

- [ ] **Step 8: Comprobar que la prueba de blobs huérfanos sirve de algo**

Reintroducir el fallo a propósito: mover la línea `var url = await blobs.SubirComprobanteAsync(...)` ARRIBA del `var descuentos = await ValidarDescuentosAsync(...)`. Ejecutar solo esa prueba:

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~UnComprobanteInvalidoNoDejaBlobsHuerfanos" --logger "console;verbosity=normal"
```

Expected: **FAIL**. Si pasa, la prueba no está midiendo nada y hay que arreglarla antes de seguir. Deshacer el cambio y confirmar que vuelve a pasar.

- [ ] **Step 9: Commit**

```bash
git add Common/Exceptions/TransicionInvalidaException.cs Features/Pagos tests/CoopagcuyApi.Tests/Integracion/DescuentoTrazableTests.cs
git commit -m "feat: la planta paga con descuentos atados a la novedad del CAT"
```

---

### Task 10: Front — extraer `ImagenProtegida`

Refactor puro: sin cambio de conducta visible. Se hace ahora porque la Task 15 necesita el mismo visor para el comprobante.

**Files:**
- Create: `src/components/ui/ImagenProtegida.tsx`
- Modify: `src/components/ui/EvidenciaNovedad.tsx`

**Interfaces:**
- Consumes: nada.
- Produces: `<ImagenProtegida claveCache={unknown[]} descargar={() => Promise<Blob>} autoCargar?={boolean} textoBoton={string} textoCaducada={string} textoAlternativo={string} />`.

- [ ] **Step 1: Crear el componente genérico**

Crear `src/components/ui/ImagenProtegida.tsx`:

```tsx
import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";

interface Props {
    /** Clave de React Query. Dos vistas de la misma imagen la comparten. */
    claveCache: unknown[];
    descargar: () => Promise<Blob>;
    /**
     * Carga al montar, sin esperar a que la toquen. Se activa donde la lista
     * es corta y la imagen es lo que hay que ver; se deja apagado donde la
     * lista es larga y descargar de golpe decenas de imágenes que nadie va a
     * mirar no compensa.
     */
    autoCargar?: boolean;
    textoBoton: string;
    /** Qué decir cuando el servidor responde 404 porque ya se borró. */
    textoCaducada: string;
    textoAlternativo: string;
}

/**
 * Imagen que vive detrás de un endpoint autenticado.
 *
 * Se descarga por el cliente autenticado y se muestra desde un object URL: el
 * access token vive en memoria y lo pone un interceptor de axios, así que un
 * `<img src>` apuntando al endpoint recibiría 401.
 *
 * La descarga va por React Query y no por un efecto propio: un efecto que
 * dispara la petición tiene que encender el indicador de carga antes de
 * llamar al API, y eso es un setState síncrono dentro de un efecto —lo que
 * React desaconseja y la regla react-hooks/set-state-in-effect rechaza.
 */
export function ImagenProtegida({
    claveCache, descargar, autoCargar = false,
    textoBoton, textoCaducada, textoAlternativo,
}: Props) {
    // En modo bajo demanda la consulta nace apagada y la enciende el botón.
    // Encenderla es un manejador de evento, no un efecto.
    const [habilitada, setHabilitada] = useState(autoCargar);

    const { data, isFetching, isError, error, refetch } = useQuery({
        queryKey: claveCache,
        queryFn: descargar,
        enabled: habilitada,
        // La imagen no cambia mientras exista, y un 404 por caducidad no se
        // arregla reintentando solo: el reintento lo pide el operador.
        staleTime: Infinity,
        retry: false,
    });

    const url = useMemo(
        () => (data ? URL.createObjectURL(data) : null), [data]);

    // Los object URL no se liberan solos: sin esto, abrir muchas imágenes
    // mantiene los blobs vivos hasta recargar la página.
    useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);

    const status = (error as { response?: { status?: number } } | null)
        ?.response?.status;
    const caducada = isError && status === 404;

    // Hueco del mismo tamaño que la miniatura: sin esto la lista da un salto
    // cuando cada imagen termina de bajar.
    if (isFetching) {
        return (
            <div className="w-20 h-20 rounded-xl border-2 border-gray-200 bg-gray-50
                            animate-pulse flex items-center justify-center"
                aria-label="Cargando la imagen">
                <span className="text-xl opacity-40" aria-hidden="true">📷</span>
            </div>
        );
    }

    if (url) {
        return (
            <a href={url} target="_blank" rel="noreferrer"
                title="Abrir en tamaño completo"
                className="inline-block relative rounded-xl overflow-hidden
                           border-2 border-teja-300 shadow-sm">
                <img src={url} alt={textoAlternativo}
                    className="w-20 h-20 object-cover block" />
                <span className="absolute bottom-0 inset-x-0 bg-black/55 text-white
                                 text-[10px] font-semibold text-center py-0.5">
                    Ampliar
                </span>
            </a>
        );
    }

    // Caducada: no hay nada que reintentar, el blob ya no existe.
    if (caducada) {
        return <span className="text-xs text-gray-400">{textoCaducada}</span>;
    }

    // Objetivo táctil de verdad (44px), no un texto que parezca parte del
    // aviso — es el estándar del resto de la aplicación en tablet de 7".
    return (
        <button
            type="button"
            onClick={() => {
                if (habilitada) void refetch();
                else setHabilitada(true);
            }}
            className="min-h-[44px] px-3 rounded-xl border-2 border-teja-300
                       bg-white text-xs font-bold text-teja-700
                       hover:bg-teja-50 active:scale-95 transition
                       flex items-center gap-2"
        >
            <span className="text-base" aria-hidden="true">📷</span>
            {isError ? "Reintentar" : textoBoton}
        </button>
    );
}
```

- [ ] **Step 2: Reducir `EvidenciaNovedad` a una envoltura**

Sustituir `src/components/ui/EvidenciaNovedad.tsx` entero:

```tsx
import { recepcionApi } from "../../api/recepcion";
import { ImagenProtegida } from "./ImagenProtegida";

interface Props {
    novedadId: number;
    autoCargar?: boolean;
}

/**
 * Evidencia fotográfica de una novedad clínica.
 *
 * La evidencia caduca a los 90 días; pasada esa fecha el API responde 404 y
 * aquí se dice, en vez de dejar un hueco sin explicación.
 */
export function EvidenciaNovedad({ novedadId, autoCargar = false }: Props) {
    return (
        <ImagenProtegida
            claveCache={["novedad-foto", novedadId]}
            descargar={() => recepcionApi.fotoNovedad(novedadId)}
            autoCargar={autoCargar}
            textoBoton="Ver foto del defecto"
            textoCaducada="La evidencia ya no está disponible (se borra a los 90 días)."
            textoAlternativo="Evidencia fotográfica de la novedad clínica"
        />
    );
}
```

- [ ] **Step 3: Verificar**

```bash
cd ../CoopagcuyFront/coopagcuy-frontend && pnpm lint && pnpm exec tsc -b && pnpm build
```

Expected: los tres con salida 0. Los dos usos existentes (`Recepcion.tsx` bajo demanda, `FormFaenamiento.tsx` con `autoCargar`) siguen compilando sin tocarse.

- [ ] **Step 4: Commit**

```bash
git add src/components/ui/ImagenProtegida.tsx src/components/ui/EvidenciaNovedad.tsx
git commit -m "refactor: el visor de imágenes autenticadas se vuelve genérico"
```

---

### Task 11: Front — pestaña de pagos en faenamiento

**Files:**
- Create: `src/components/faenamiento/FormPagoProductora.tsx`
- Modify: `src/pages/Faenamiento.tsx`
- Modify: `src/api/pagos.ts`
- Modify: `src/types/productora.ts`

**Interfaces:**
- Consumes: `GET /api/pagos/por-pagar`, `GET /api/pagos/{id}/cuyes-con-novedad`, `POST /api/pagos/{id}/pagar` (Tasks 8 y 9); `EvidenciaNovedad` (Task 10).
- Produces: `pagosApi.porPagar()`, `pagosApi.cuyesConNovedad(id)`, `pagosApi.pagar(id, body)`.

- [ ] **Step 1: Añadir los tipos**

En `src/types/productora.ts`:

```typescript
export interface TicketPorPagar {
    pagoId: number;
    productoraId: number;
    nombreProductora: string;
    cedula: string;
    loteId: number;
    codigoLote: string;
    centroAcopio: string;
    fechaRecepcion: string;
    cuyesEntregados: number;
    montoUsd: number;
    fechaEmision: string;
}

export interface CuyConNovedad {
    cuyRegistroId: number;
    numeroEnLote: number;
    pesoGramos: number;
    novedadId: number;
    tipoNovedad: string;
    descripcion: string;
    tieneFoto: boolean;
}

export interface DescuentoRequest {
    novedadCatId: number;
    descripcion: string;
    montoUsd: number;
}

export interface RegistrarPagoEfectivoRequest {
    descuentos: DescuentoRequest[];
    comprobanteBase64: string;
    pagadoPor: string;
}
```

- [ ] **Step 2: Ampliar el cliente**

En `src/api/pagos.ts`, dentro de `pagosApi`:

```typescript
    // Bandeja de la planta. Distinta de lotesPendientes, que es de la CAT.
    porPagar: async () => {
        const { data } = await client.get<TicketPorPagar[]>(
            "/api/pagos/por-pagar");
        return data;
    },

    cuyesConNovedad: async (pagoId: number) => {
        const { data } = await client.get<CuyConNovedad[]>(
            `/api/pagos/${pagoId}/cuyes-con-novedad`);
        return data;
    },

    pagar: async (pagoId: number, body: RegistrarPagoEfectivoRequest) => {
        const { data } = await client.post<Pago>(
            `/api/pagos/${pagoId}/pagar`, body);
        return data;
    },
```

Y sus imports de tipo.

- [ ] **Step 3: Crear el formulario**

Crear `src/components/faenamiento/FormPagoProductora.tsx`:

```tsx
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { pagosApi } from "../../api/pagos";
import { useAuth } from "../../context/useAuth";
import { ModalShell } from "../ui/ModalShell";
import { EvidenciaNovedad } from "../ui/EvidenciaNovedad";
import type { TicketPorPagar, DescuentoRequest } from "../../types/productora";

interface Props {
    ticket: TicketPorPagar;
    onClose: () => void;
}

const MAX_BYTES_COMPROBANTE = 2 * 1024 * 1024;

/**
 * Registro de la transferencia por la planta.
 *
 * Los descuentos solo pueden apoyarse en un cuy que el CAT marcó: por eso la
 * lista no es libre, sale del servidor. El total se recalcula a la vista, y
 * el servidor lo vuelve a calcular al guardar sin fiarse de esta pantalla.
 */
export function FormPagoProductora({ ticket, onClose }: Props) {
    const qc = useQueryClient();
    const { auth } = useAuth();

    // Descuento por novedad, indexado por novedadId. Vacío = sin descuento.
    const [descuentos, setDescuentos] = useState<
        Record<number, { descripcion: string; montoUsd: number }>>({});
    const [comprobante, setComprobante] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const { data: cuyes = [], isLoading } = useQuery({
        queryKey: ["cuyes_con_novedad", ticket.pagoId],
        queryFn: () => pagosApi.cuyesConNovedad(ticket.pagoId),
    });

    const aplicados: DescuentoRequest[] = Object.entries(descuentos)
        .filter(([, d]) => d.montoUsd > 0)
        .map(([novedadId, d]) => ({
            novedadCatId: Number(novedadId),
            descripcion: d.descripcion,
            montoUsd: d.montoUsd,
        }));

    const totalDescuento = aplicados.reduce((s, d) => s + d.montoUsd, 0);
    const aPagar = ticket.montoUsd - totalDescuento;

    const mutation = useMutation({
        mutationFn: () => pagosApi.pagar(ticket.pagoId, {
            descuentos: aplicados,
            comprobanteBase64: comprobante ?? "",
            pagadoPor: auth.nombreCompleto ?? "",
        }),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["tickets_por_pagar"] });
            onClose();
        },
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje ?? "No se pudo registrar el pago.");
        },
    });

    const leerComprobante = (e: React.ChangeEvent<HTMLInputElement>) => {
        const archivo = e.target.files?.[0];
        if (!archivo) return;

        if (archivo.size > MAX_BYTES_COMPROBANTE) {
            setError(`La captura pesa ${Math.round(archivo.size / 1024)} KB y ` +
                `el máximo es ${MAX_BYTES_COMPROBANTE / 1024} KB.`);
            return;
        }

        const lector = new FileReader();
        lector.onload = () => {
            // readAsDataURL da "data:image/jpeg;base64,XXXX"; el API espera
            // solo la parte de después de la coma.
            const resultado = String(lector.result);
            setComprobante(resultado.slice(resultado.indexOf(",") + 1));
            setError(null);
        };
        lector.readAsDataURL(archivo);
    };

    const handleSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);

        if (!comprobante) {
            setError("Adjunta la captura de la transferencia.");
            return;
        }
        if (aPagar <= 0) {
            setError("Los descuentos no pueden igualar ni superar el ticket.");
            return;
        }
        mutation.mutate();
    };

    return (
        <ModalShell
            onClose={onClose}
            title={`Pagar a ${ticket.nombreProductora}`}
            footer={
                <div className="flex gap-3">
                    <button type="button" onClick={onClose}
                        className="flex-1 h-12 border-2 border-gray-200 rounded-2xl
                       text-sm font-semibold text-gray-700 hover:bg-gray-50 transition">
                        Cancelar
                    </button>
                    <button type="submit" form="form-pago-productora"
                        disabled={mutation.isPending}
                        className="flex-1 h-12 bg-primary-600 hover:bg-primary-700
                       disabled:bg-primary-300 text-white rounded-2xl
                       text-sm font-bold transition">
                        {mutation.isPending
                            ? "Guardando…"
                            : `Pagar $${aPagar.toFixed(2)}`}
                    </button>
                </div>
            }
        >
            <form id="form-pago-productora" onSubmit={handleSubmit}
                className="space-y-4">

                <div className="bg-gray-50 rounded-xl px-3 py-2 text-sm">
                    <p className="font-bold">{ticket.codigoLote}</p>
                    <p className="text-gray-600">
                        {ticket.cuyesEntregados} cuyes · {ticket.centroAcopio}
                    </p>
                    <p className="text-lg font-extrabold text-gray-900 mt-1">
                        Ticket: ${ticket.montoUsd.toFixed(2)}
                    </p>
                </div>

                <div>
                    <p className="text-xs font-bold uppercase tracking-wide
                      text-gray-500 mb-2">
                        Cuyes con novedad del centro de acopio
                    </p>

                    {isLoading && (
                        <p className="text-xs text-gray-400">Cargando…</p>
                    )}

                    {!isLoading && cuyes.length === 0 && (
                        <p className="text-xs text-gray-400">
                            Este lote no trae cuyes con novedad. No hay nada que
                            descontar.
                        </p>
                    )}

                    <div className="space-y-3">
                        {cuyes.map((c) => (
                            <div key={c.novedadId}
                                className="border-2 border-gray-100 rounded-xl p-3
                                    flex items-start gap-3">

                                {c.tieneFoto && (
                                    <EvidenciaNovedad autoCargar
                                        novedadId={c.novedadId} />
                                )}

                                <div className="flex-1 space-y-2">
                                    <p className="text-xs font-bold text-gray-700">
                                        Cuy #{c.numeroEnLote} · {c.tipoNovedad}
                                    </p>
                                    <p className="text-xs text-gray-500">
                                        {c.descripcion}
                                    </p>

                                    <input
                                        type="text"
                                        placeholder="¿Qué observaste en planta?"
                                        value={descuentos[c.novedadId]?.descripcion ?? ""}
                                        onChange={(e) => setDescuentos({
                                            ...descuentos,
                                            [c.novedadId]: {
                                                descripcion: e.target.value,
                                                montoUsd:
                                                    descuentos[c.novedadId]?.montoUsd ?? 0,
                                            },
                                        })}
                                        className="w-full h-10 px-2 rounded-lg border-2
                                            border-gray-200 text-xs
                                            focus:border-primary-500 focus:outline-none"
                                    />

                                    <div className="flex items-center gap-2">
                                        <span className="text-xs text-gray-500">
                                            Descontar $
                                        </span>
                                        <input
                                            type="number" min={0} step={0.01}
                                            inputMode="decimal"
                                            value={descuentos[c.novedadId]?.montoUsd || ""}
                                            onChange={(e) => setDescuentos({
                                                ...descuentos,
                                                [c.novedadId]: {
                                                    descripcion:
                                                        descuentos[c.novedadId]?.descripcion
                                                        ?? "",
                                                    montoUsd: Number(e.target.value),
                                                },
                                            })}
                                            placeholder="0.00"
                                            className="w-24 h-10 px-2 rounded-lg border-2
                                                border-gray-200 text-xs font-bold
                                                focus:border-primary-500
                                                focus:outline-none"
                                        />
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>

                {totalDescuento > 0 && (
                    <div className="bg-bayo-50 rounded-xl px-3 py-2 text-sm">
                        <p className="text-bayo-800">
                            Descuentos: −${totalDescuento.toFixed(2)}
                        </p>
                        <p className="font-extrabold text-gray-900">
                            A pagar: ${aPagar.toFixed(2)}
                        </p>
                    </div>
                )}

                <div>
                    <label className="block text-xs font-bold uppercase
                        tracking-wide text-gray-500 mb-1">
                        Captura de la transferencia
                    </label>
                    <input
                        type="file" accept="image/*" capture="environment"
                        onChange={leerComprobante}
                        className="w-full text-xs file:min-h-[44px] file:px-3
                            file:rounded-xl file:border-2 file:border-primary-200
                            file:bg-primary-50 file:text-primary-800
                            file:font-bold file:text-xs"
                    />
                    {comprobante && (
                        <p className="mt-1 text-xs text-primary-700 font-semibold">
                            ✓ Captura lista para subir
                        </p>
                    )}
                </div>

                {error && (
                    <div className="bg-teja-50 border border-teja-100 rounded-xl
                        px-3 py-2 text-sm text-teja-700">
                        {error}
                    </div>
                )}
            </form>
        </ModalShell>
    );
}
```

- [ ] **Step 4: Añadir la pestaña**

En `src/pages/Faenamiento.tsx`:

```tsx
type Tab = "faenamientos" | "llegadas" | "devoluciones" | "pagos";
```

Añadir la consulta:

```tsx
    const { data: ticketsPorPagar = [] } = useQuery({
        queryKey: ["tickets_por_pagar"],
        queryFn: () => pagosApi.porPagar(),
        enabled: tab === "pagos",
    });

    const [ticketAbierto, setTicketAbierto] = useState<TicketPorPagar | null>(null);
```

Y el bloque de la pestaña, siguiendo el patrón de las otras tres:

```tsx
            {/* ── Tab pagos: tickets emitidos por los CAT ── */}
            {tab === "pagos" && (
                <div className="space-y-3">
                    {ticketsPorPagar.length === 0 && (
                        <p className="text-sm text-gray-400">
                            No hay tickets pendientes de pago.
                        </p>
                    )}

                    {ticketsPorPagar.map((t) => (
                        <div key={t.pagoId}
                            className="bg-white rounded-2xl border-2 border-gray-100
                                p-4 flex items-center justify-between gap-3">
                            <div>
                                <p className="font-bold text-gray-900">
                                    {t.nombreProductora}
                                </p>
                                <p className="text-xs text-gray-500">
                                    {t.codigoLote} · {t.centroAcopio} ·
                                    {" "}{t.cuyesEntregados} cuyes
                                </p>
                                <p className="text-lg font-extrabold text-gray-900">
                                    ${t.montoUsd.toFixed(2)}
                                </p>
                            </div>
                            <div className="flex flex-col gap-2">
                                <button
                                    type="button"
                                    onClick={() => void imprimirTicket(t.pagoId)}
                                    className="min-h-[44px] px-3 rounded-xl border-2
                                        border-gray-200 bg-white text-xs font-bold
                                        text-gray-700 hover:bg-gray-50 transition"
                                >
                                    🧾 Ver ticket
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setTicketAbierto(t)}
                                    className="min-h-[44px] px-3 rounded-xl
                                        bg-primary-600 hover:bg-primary-700
                                        text-white text-xs font-bold transition"
                                >
                                    Registrar pago
                                </button>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {ticketAbierto && (
                <FormPagoProductora
                    ticket={ticketAbierto}
                    onClose={() => setTicketAbierto(null)}
                />
            )}
```

Añadir `"pagos"` a la lista de pestañas del componente `Tabs` con la etiqueta `"Pagos"`, y los imports de `pagosApi`, `imprimirTicket`, `FormPagoProductora` y el tipo `TicketPorPagar`.

- [ ] **Step 5: Verificar**

```bash
cd ../CoopagcuyFront/coopagcuy-frontend && pnpm lint && pnpm exec tsc -b && pnpm build
```

Expected: los tres con salida 0.

- [ ] **Step 6: Commit**

```bash
git add src/components/faenamiento/FormPagoProductora.tsx src/pages/Faenamiento.tsx src/api/pagos.ts src/types/productora.ts
git commit -m "feat: la planta registra el pago con sus descuentos y la captura"
```

---

# FASE C — Verificación y borrado

Al terminar esta fase la CAT ve la alerta, abre la captura, confirma el cobro, y la imagen desaparece a los cinco días.

---

### Task 12: Servir el comprobante, con caducidad

**Files:**
- Modify: `Features/Pagos/Services/PagoService.cs`
- Modify: `Features/Pagos/Controllers/PagosController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/VerificacionPagoTests.cs`

**Interfaces:**
- Consumes: `DescargarComprobanteAsync` (Task 7); `ComprobanteUrl` y `ComprobanteExpiraEn` (Task 1).
- Produces: `IPagoService.ObtenerComprobanteAsync(int pagoId, CentroAcopio? filtroCat) → Task<byte[]?>`; endpoint `GET /api/pagos/{id}/comprobante`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/VerificacionPagoTests.cs` con la infraestructura compartida y las cuatro pruebas del visor:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Cierre del ciclo: la CAT abre la captura de la transferencia y confirma
/// que el dinero llegó. La imagen deja de servirse a los 5 días.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VerificacionPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    [Fact]
    public async Task LaCatDelPagoDescargaElComprobante()
    {
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await respuesta.Content.ReadAsByteArrayAsync()).ShouldBe(JpegMinimo);
    }

    [Fact]
    public async Task UnaCatAjenaRecibe404()
    {
        // 404 y no 403: confirmar que el pago existe filtraría datos de otro
        // centro.
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PasadaLaCaducidadElApiDejaDeServirla()
    {
        // El blob puede seguir existiendo —Azure barre cuando le toca— pero
        // el API deja de servirlo en el momento exacto. Mismo patrón que la
        // evidencia clínica.
        var pagoId = await TicketPagadoAsync();

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnTicketSinPagarNoTieneComprobante()
    {
        var pagoId = await TicketSinPagarAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/comprobante");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// Ticket emitido y sin pagar. Devuelve el Id.
    private async Task<int> TicketSinPagarAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "lesion-visible" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }

    /// Ticket ya pagado por la planta, con su captura subida.
    private async Task<int> TicketPagadoAsync()
    {
        var pagoId = await TicketSinPagarAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Convert.ToBase64String(JpegMinimo),
                pagadoPor = "Operador de planta"
            });
        respuesta.EnsureSuccessStatusCode();

        return pagoId;
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VerificacionPagoTests" --logger "console;verbosity=normal"
```

Expected: FAIL — 404 en todas, el endpoint no existe.

- [ ] **Step 3: Implementar en el servicio**

En `IPagoService`:

```csharp
    /// Bytes de la captura, o null si no hay, si caducó, o si el pago es de
    /// otro centro. Un solo null para los tres casos: el controlador responde
    /// 404 sin distinguirlos, que es justo lo que se quiere.
    Task<byte[]?> ObtenerComprobanteAsync(int pagoId, CentroAcopio? filtroCat);
```

En `PagoService`:

```csharp
    public async Task<byte[]?> ObtenerComprobanteAsync(
        int pagoId, CentroAcopio? filtroCat)
    {
        var pago = await db.Pagos
            .Include(p => p.Productora)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId);

        if (pago is null) return null;

        // Centro ajeno: null, no excepción. El controlador responde 404 y no
        // 403 porque confirmar la existencia ya sería filtrar el dato.
        if (filtroCat is CentroAcopio cat && pago.Productora.CatAsignado != cat)
            return null;

        if (string.IsNullOrWhiteSpace(pago.ComprobanteUrl)) return null;

        // Caducada: el API deja de servirla en el momento exacto, sin esperar
        // a que pase el barrido ni la política de Azure.
        if (pago.ComprobanteExpiraEn is DateTime expira
            && expira <= DateTime.UtcNow)
            return null;

        var nombre = NombreDeBlob(pago.ComprobanteUrl);
        if (nombre is null) return null;

        return await blobs.DescargarComprobanteAsync(nombre);
    }

    /// <summary>
    /// Último segmento de la URI del blob. Devuelve null ante cualquier cosa
    /// que no sea una URI con contenedor y nombre: una fila corrupta no puede
    /// convertirse en un 500.
    /// </summary>
    private static string? NombreDeBlob(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Segments.Length < 3 ? null : uri.Segments[^1];
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
```

- [ ] **Step 4: Añadir el endpoint**

En `PagosController`:

```csharp
    /// <summary>
    /// Captura de la transferencia. La ve la CAT que tiene que verificarla y
    /// la planta que la subió. Se sirve por endpoint autenticado y no por URL
    /// pública: es una imagen de una operación bancaria.
    /// </summary>
    [HttpGet("{id:int}/comprobante")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> Comprobante(int id)
    {
        var bytes = await service.ObtenerComprobanteAsync(id, FiltroCat());
        // Un solo 404 para no existe, caducada y centro ajeno
        return bytes is null ? NotFound() : File(bytes, "image/jpeg");
    }
```

- [ ] **Step 5: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VerificacionPagoTests" --logger "console;verbosity=normal"
```

Expected: PASS, 4 pruebas.

- [ ] **Step 6: Commit**

```bash
git add Features/Pagos tests/CoopagcuyApi.Tests/Integracion/VerificacionPagoTests.cs
git commit -m "feat: la CAT descarga la captura de la transferencia"
```

---

### Task 13: Marcar el pago como recibido

**Files:**
- Modify: `Features/Pagos/DTOs/PagoDtos.cs`
- Modify: `Features/Pagos/Services/PagoService.cs`
- Modify: `Features/Pagos/Controllers/PagosController.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/VerificacionPagoTests.cs`

**Interfaces:**
- Consumes: `TransicionInvalidaException` (Task 9); `TicketPagadoAsync` (Task 12).
- Produces: `VerificarPagoDto` con `VerificadoPor`; `IPagoService.VerificarAsync(int, VerificarPagoDto, CentroAcopio?) → Task<PagoResponseDto>`; endpoint `POST /api/pagos/{id}/verificar`; la constante `DiasGraciaComprobante = 5`.

- [ ] **Step 1: Añadir las pruebas que fallan**

En `VerificacionPagoTests.cs`, añadir:

```csharp
    [Fact]
    public async Task AlVerificarSeFijaLaCaducidadACincoDias()
    {
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);

        pago.Estado.ShouldBe(EstadoPago.Recibido);
        pago.VerificadoPor.ShouldBe("Operadora de prueba");
        pago.FechaVerificacion.ShouldNotBeNull();
        pago.ComprobanteExpiraEn.ShouldNotBeNull();

        // Cinco días desde la verificación, con holgura de un minuto para no
        // atarse al instante exacto del reloj.
        var esperado = pago.FechaVerificacion!.Value.AddDays(5);
        pago.ComprobanteExpiraEn!.Value
            .ShouldBe(esperado, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task NoSePuedeVerificarUnTicketQueNadieHaPagado()
    {
        var pagoId = await TicketSinPagarAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoSePuedeVerificarDosVeces()
    {
        var pagoId = await TicketPagadoAsync();

        object Cuerpo() => new { verificadoPor = "Operadora de prueba" };

        var primera = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", Cuerpo());
        primera.StatusCode.ShouldBe(HttpStatusCode.OK);

        var segunda = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", Cuerpo());
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LaPlantaNoPuedeVerificarSuPropioPago()
    {
        // Quien paga no confirma que pagó: la verificación existe justamente
        // para que sea otro quien lo diga.
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnaCatAjenaNoPuedeVerificar()
    {
        var pagoId = await TicketPagadoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .PostAsJsonAsync($"/api/pagos/{pagoId}/verificar", new
            {
                verificadoPor = "Operadora de otro centro"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VerificacionPagoTests" --logger "console;verbosity=normal"
```

Expected: las 4 de la Task 12 en PASS, las 5 nuevas en FAIL con 404.

- [ ] **Step 3: Añadir el DTO**

Al final de `Features/Pagos/DTOs/PagoDtos.cs`:

```csharp
/// <summary>
/// Confirmación de la CAT de que el dinero llegó. Solo el nombre de quien
/// confirma: la fecha la sella el servidor, como todas las del sistema.
/// </summary>
public class VerificarPagoDto
{
    public string VerificadoPor { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Implementar en el servicio**

En `IPagoService`:

```csharp
    /// Marca el pago como recibido y arranca la cuenta atrás de la captura.
    Task<PagoResponseDto> VerificarAsync(
        int pagoId, VerificarPagoDto dto, CentroAcopio? filtroCat);
```

En `PagoService`, junto a `MaxBytesComprobante`:

```csharp
    // Días que la captura sigue disponible tras la verificación. Lo que la
    // borra de verdad es el barrido; esta fecha es lo que decide cuándo el
    // API deja de servirla.
    private const int DiasGraciaComprobante = 5;
```

Y el método:

```csharp
    public async Task<PagoResponseDto> VerificarAsync(
        int pagoId, VerificarPagoDto dto, CentroAcopio? filtroCat)
    {
        var pago = await db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        // Centro ajeno: KeyNotFound y no Unauthorized, para que el controlador
        // responda 404. Confirmar la existencia filtraría datos de otro CAT.
        if (filtroCat is CentroAcopio cat && pago.Productora.CatAsignado != cat)
            throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.Estado != EstadoPago.Pagado)
            throw new TransicionInvalidaException(
                pago.Estado == EstadoPago.Pendiente
                    ? "No se puede verificar un pago que la planta todavía no ha hecho."
                    : "Este pago ya estaba verificado.");

        var ahora = DateTime.UtcNow;
        pago.Estado = EstadoPago.Recibido;
        pago.FechaVerificacion = ahora;
        pago.VerificadoPor = dto.VerificadoPor.Trim();
        pago.ComprobanteExpiraEn = ahora.AddDays(DiasGraciaComprobante);

        await db.SaveChangesAsync();

        return Mapear(pago, pago.Productora.NombreCompleto, pago.Lote?.CodigoLote);
    }
```

- [ ] **Step 5: Añadir el endpoint**

En `PagosController`:

```csharp
    /// <summary>
    /// La CAT confirma que el dinero llegó. Sin OperadorFaenamiento a
    /// propósito: quien paga no confirma que pagó, esa es la razón de ser
    /// del paso.
    /// </summary>
    [HttpPost("{id:int}/verificar")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> Verificar(
        int id, [FromBody] VerificarPagoDto dto)
    {
        try
        {
            return Ok(await service.VerificarAsync(id, dto, FiltroCat()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (TransicionInvalidaException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

- [ ] **Step 6: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VerificacionPagoTests" --logger "console;verbosity=normal"
```

Expected: PASS, 9 pruebas.

- [ ] **Step 7: Commit**

```bash
git add Features/Pagos tests/CoopagcuyApi.Tests/Integracion/VerificacionPagoTests.cs
git commit -m "feat: la CAT marca el pago como recibido"
```

---

### Task 14: Barrido oportunista de capturas caducadas

**Files:**
- Modify: `Features/Pagos/Services/PagoService.cs`
- Modify: `Program.cs` (registro de `ILogger` ya existe; solo se inyecta)
- Test: `tests/CoopagcuyApi.Tests/Integracion/BarridoComprobantesTests.cs`

**Interfaces:**
- Consumes: `BorrarComprobanteAsync` (Task 7); `ComprobanteExpiraEn` (Task 1).
- Produces: `IPagoService.BarrerComprobantesCaducadosAsync() → Task<int>` (devuelve cuántos borró), invocado desde `ListarAsync`.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/BarridoComprobantesTests.cs`:

```csharp
using System.Net.Http.Json;
using Azure.Storage.Blobs;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El espacio se libera a los 5 días de verificar, no a los 30 de la política
/// de Azure. Como el contenedor del API se apaga sin tráfico, una tarea
/// programada dentro de ella no correría: el barrido se engancha al tráfico
/// que ya existe, la consulta de pagos de la CAT.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class BarridoComprobantesTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    [Fact]
    public async Task ConsultarLaListaBorraLosBlobsYaCaducados()
    {
        var pagoId = await TicketPagadoAsync();

        // Caducado hace un minuto
        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        (await ContarBlobsAsync()).ShouldBeGreaterThan(0);

        // La consulta normal de la CAT es la que dispara el barrido
        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        (await ContarBlobsAsync()).ShouldBe(0);

        // La fila sobrevive al binario: el pago no desaparece del historial
        await using var db2 = api.NuevoDbContext();
        var final = await db2.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        final.ComprobanteUrl.ShouldBeNull();
        final.MontoPagadoUsd.ShouldNotBeNull();
    }

    [Fact]
    public async Task UnComprobanteVigenteNoSeBorra()
    {
        var pagoId = await TicketPagadoAsync();

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn = DateTime.UtcNow.AddDays(3);
            await db.SaveChangesAsync();
        }

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        (await ContarBlobsAsync()).ShouldBe(antes);
    }

    [Fact]
    public async Task UnPagoSinVerificarNoSeBarre()
    {
        // Sin ComprobanteExpiraEn no hay cuenta atrás: la captura vive hasta
        // que Azure la borre a los 30 días. Barrerla aquí dejaría a la CAT
        // sin nada que verificar.
        var pagoId = await TicketPagadoAsync();

        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.FirstAsync(p => p.Id == pagoId);
            pago.ComprobanteExpiraEn.ShouldBeNull();
        }

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.EnsureSuccessStatusCode();

        (await ContarBlobsAsync()).ShouldBe(antes);
    }

    private static async Task<int> ContarBlobsAsync()
    {
        var cliente = new BlobServiceClient(ApiFactory.CadenaBlob);
        var contenedor = cliente.GetBlobContainerClient("comprobantes-pago");
        await contenedor.CreateIfNotExistsAsync();

        var total = 0;
        await foreach (var _ in contenedor.GetBlobsAsync()) total++;
        return total;
    }

    private async Task<int> TicketPagadoAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var emision = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        emision.EnsureSuccessStatusCode();

        int pagoId;
        await using (var db = api.NuevoDbContext())
        {
            pagoId = await db.Pagos
                .Where(p => p.ProductoraId == productora.Id)
                .Select(p => p.Id)
                .FirstAsync();
        }

        var pagado = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Convert.ToBase64String(JpegMinimo),
                pagadoPor = "Operador de planta"
            });
        pagado.EnsureSuccessStatusCode();

        return pagoId;
    }
}
```

- [ ] **Step 2: Ejecutar y comprobar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~BarridoComprobantesTests" --logger "console;verbosity=normal"
```

Expected: `ConsultarLaListaBorraLosBlobsYaCaducados` FAIL con `should be 0 but was 1`. Las otras dos ya pasan (no borrar es el comportamiento actual) — no son la prueba que guía, son las que impiden que el barrido se pase de listo.

- [ ] **Step 3: Implementar el barrido**

En `IPagoService`:

```csharp
    /// Borra los blobs de las capturas ya caducadas y limpia su referencia.
    /// Devuelve cuántas borró. Se invoca desde el listado: el contenedor del
    /// API se apaga sin tráfico, así que una tarea programada no correría.
    Task<int> BarrerComprobantesCaducadosAsync();
```

Cambiar la firma de la clase a
`public class PagoService(AppDbContext db, IBlobStorageService blobs, ILogger<PagoService> log) : IPagoService`
y añadir:

```csharp
    public async Task<int> BarrerComprobantesCaducadosAsync()
    {
        var ahora = DateTime.UtcNow;

        var caducados = await db.Pagos
            .Where(p => p.ComprobanteUrl != null
                && p.ComprobanteExpiraEn != null
                && p.ComprobanteExpiraEn <= ahora)
            // Tope por pasada: el barrido va colgado de una consulta que un
            // operador está esperando. Mejor barrer 20 por vez, muchas veces,
            // que dejar la lista congelada mientras se limpian doscientos.
            .Take(20)
            .ToListAsync();

        if (caducados.Count == 0) return 0;

        var borrados = 0;
        foreach (var pago in caducados)
        {
            var nombre = NombreDeBlob(pago.ComprobanteUrl!);
            if (nombre is null)
            {
                // Fila corrupta: se limpia la referencia igual, o volvería a
                // intentarlo en cada consulta para siempre.
                pago.ComprobanteUrl = null;
                continue;
            }

            try
            {
                await blobs.BorrarComprobanteAsync(nombre);
                pago.ComprobanteUrl = null;
                borrados++;
            }
            catch (Exception ex)
            {
                // La consulta de pagos NO puede caerse por un borrado. Si
                // Blob está caído o faltan permisos, se registra y se sigue:
                // la política de Azure lo borrará igual el día 30.
                log.LogWarning(ex,
                    "No se pudo borrar la captura del pago {PagoId}", pago.Id);
            }
        }

        await db.SaveChangesAsync();
        return borrados;
    }
```

- [ ] **Step 4: Engancharlo al listado**

Al principio de `ListarAsync`, antes de construir la consulta:

```csharp
        // Barrido oportunista. Va aquí y no en una tarea programada porque el
        // contenedor del API escala a cero: sin tráfico no hay proceso vivo
        // que ejecute nada. La CAT y la planta entran a diario, así que en la
        // práctica la captura se borra al día siguiente de vencer.
        await BarrerComprobantesCaducadosAsync();
```

- [ ] **Step 5: Ejecutar y comprobar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~BarridoComprobantesTests" --logger "console;verbosity=normal"
```

Expected: PASS, 3 pruebas.

- [ ] **Step 6: Batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Expected: PASS en todas.

- [ ] **Step 7: Commit**

```bash
git add Features/Pagos tests/CoopagcuyApi.Tests/Integracion/BarridoComprobantesTests.cs
git commit -m "feat: barrido oportunista de las capturas caducadas"
```

---

### Task 15: Front — alerta y verificación en Recepción

**Files:**
- Create: `src/components/recepcion/VerificarPago.tsx`
- Modify: `src/pages/Recepcion.tsx`
- Modify: `src/api/pagos.ts`

**Interfaces:**
- Consumes: `GET /api/pagos/{id}/comprobante` (Task 12), `POST /api/pagos/{id}/verificar` (Task 13); `ImagenProtegida` (Task 10); `Pago.estado` y `Pago.tieneComprobante` (Task 5).
- Produces: `pagosApi.comprobante(id)`, `pagosApi.verificar(id, body)`.

- [ ] **Step 1: Ampliar el cliente**

En `src/api/pagos.ts`:

```typescript
    // responseType blob: el endpoint devuelve image/jpeg, no JSON
    comprobante: async (pagoId: number): Promise<Blob> => {
        const { data } = await client.get<Blob>(
            `/api/pagos/${pagoId}/comprobante`, { responseType: "blob" });
        return data;
    },

    verificar: async (pagoId: number, verificadoPor: string) => {
        const { data } = await client.post<Pago>(
            `/api/pagos/${pagoId}/verificar`, { verificadoPor });
        return data;
    },
```

- [ ] **Step 2: Crear el bloque de verificación**

Crear `src/components/recepcion/VerificarPago.tsx`:

```tsx
import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { pagosApi } from "../../api/pagos";
import { useAuth } from "../../context/useAuth";
import { ImagenProtegida } from "../ui/ImagenProtegida";
import type { Pago } from "../../types/productora";

interface Props {
    pago: Pago;
}

/**
 * Cierre del ciclo para la operadora del CAT: mira la captura que subió la
 * planta y confirma que el dinero llegó.
 *
 * La captura se carga sola (`autoCargar`): la operadora abre esta fila
 * precisamente para verla, y la lista de pagos por verificar es corta —a
 * diferencia del historial completo, donde descargar todo sería un derroche.
 */
export function VerificarPago({ pago }: Props) {
    const qc = useQueryClient();
    const { auth } = useAuth();
    const [error, setError] = useState<string | null>(null);

    const mutation = useMutation({
        mutationFn: () => pagosApi.verificar(pago.id, auth.nombreCompleto ?? ""),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ["pagos"] });
        },
        onError: (e: unknown) => {
            const err = e as { response?: { data?: { mensaje?: string } } };
            setError(err.response?.data?.mensaje
                ?? "No se pudo marcar el pago como recibido.");
        },
    });

    return (
        <div className="bg-primary-50 border-2 border-primary-200 rounded-xl
            px-3 py-2 flex items-start gap-3">

            {pago.tieneComprobante && (
                <ImagenProtegida
                    autoCargar
                    claveCache={["comprobante-pago", pago.id]}
                    descargar={() => pagosApi.comprobante(pago.id)}
                    textoBoton="Ver comprobante"
                    textoCaducada="El comprobante ya no está disponible (se borra a los 5 días de verificarlo)."
                    textoAlternativo="Captura de la transferencia"
                />
            )}

            <div className="flex-1">
                <p className="text-xs font-bold text-primary-800">
                    💸 La planta transfirió ${(pago.montoPagadoUsd ?? 0).toFixed(2)}
                </p>
                {pago.montoPagadoUsd !== null
                    && pago.montoPagadoUsd < pago.montoUsd && (
                        <p className="text-xs text-bayo-700">
                            Con descuento: el ticket era de
                            {" "}${pago.montoUsd.toFixed(2)}
                        </p>
                    )}
                <p className="text-xs text-gray-500">
                    Registrado por {pago.pagadoPor}
                </p>

                <button
                    type="button"
                    onClick={() => mutation.mutate()}
                    disabled={mutation.isPending}
                    className="mt-2 min-h-[44px] px-4 rounded-xl bg-primary-600
                        hover:bg-primary-700 disabled:bg-primary-300 text-white
                        text-xs font-bold transition active:scale-95"
                >
                    {mutation.isPending ? "Guardando…" : "✓ Recibí el pago"}
                </button>

                {error && (
                    <p className="mt-1 text-xs text-teja-700">{error}</p>
                )}
            </div>
        </div>
    );
}
```

- [ ] **Step 3: Contador en la pestaña**

En `src/pages/Recepcion.tsx`, junto a la consulta de pagos:

```tsx
    // Tickets que la planta ya pagó y esta CAT todavía no ha verificado. El
    // servidor ya acota por centro, así que no hay que filtrar aquí.
    const porVerificar = pagos.filter((p) => p.estado === "Pagado").length;
```

En la definición de las pestañas, la de pagos pasa a llevar el contador:

```tsx
    { valor: "pagos", etiqueta: porVerificar > 0 ? `Pagos (${porVerificar})` : "Pagos" },
```

- [ ] **Step 4: Destacar las filas y pintar el bloque**

En la tabla de pagos, dar a la fila un fondo distinto cuando esté por verificar y añadir el bloque bajo ella:

```tsx
                            <tr className={p.estado === "Pagado"
                                ? "bg-primary-50/50"
                                : ""}>
```

Y tras las celdas de la fila, una fila adicional:

```tsx
                            {p.estado === "Pagado" && (
                                <tr>
                                    <td colSpan={5} className="px-3 pb-3">
                                        <VerificarPago pago={p} />
                                    </td>
                                </tr>
                            )}
```

Ajustar `colSpan` al número real de columnas de la tabla. Añadir el import de `VerificarPago`.

- [ ] **Step 5: Verificar**

```bash
cd ../CoopagcuyFront/coopagcuy-frontend && pnpm lint && pnpm exec tsc -b && pnpm build
```

Expected: los tres con salida 0.

- [ ] **Step 6: Commit**

```bash
git add src/components/recepcion/VerificarPago.tsx src/pages/Recepcion.tsx src/api/pagos.ts
git commit -m "feat: la CAT ve la alerta del pago y lo marca como recibido"
```

---

## Verificación final

Antes de dar por cerrada cualquier fase:

- [ ] **API — batería completa en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

- [ ] **Front — los tres comandos en verde**

```bash
cd ../CoopagcuyFront/coopagcuy-frontend && pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Repaso manual del flujo completo**

No hay Vitest ni Playwright en el front, así que este paso no lo cubre nada automático:

1. Como operadora de CAT, registrar una entrega con al menos un cuy con signos clínicos **y su foto**.
2. Registrar el pago de ese lote. Comprobar que solo aparece el botón de transferencia y que el lote es obligatorio.
3. Comprobar que el ticket se abre para imprimir y que el ancho corresponde al rollo de 80 mm.
4. Como operador de faenamiento, abrir la pestaña de pagos, comprobar que se ve la foto del defecto junto al cuy, aplicar un descuento y subir una captura.
5. Comprobar que el total a pagar baja en la pantalla y que coincide con lo guardado.
6. Como operadora de CAT, comprobar que la pestaña muestra el contador, que la captura se ve, y que el botón de recibir el pago cierra el ciclo.
7. Intentar verificar dos veces: debe rechazarse.

- [ ] **Aplicar la política de ciclo de vida en Azure**

Una sola vez, a mano, con el comando de `infra/bootstrap.azcli`. No forma parte del despliegue automático. Sin este paso el borrado a 30 días no existe y solo funciona el barrido de los 5 días.

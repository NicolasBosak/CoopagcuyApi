# Venta local — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que un lote pueda tener dos destinos parciales — lo que la CAT vende en la comunidad y lo que viaja a la planta — con los animales vendidos marcados uno a uno, restados del envío y visibles en el papel.

**Architecture:** No se crea una entidad `VentaLocal`. `CuyRegistro` ya lleva su propia productora, así que «vendí estos 10 de los 12» es marcar filas que ya existen con una clave foránea al `Pago` de la venta. La resta al movilizar se calcula **por animal**, que es lo que resuelve solo el caso de dos productoras en la misma jaula. Todo texto que va al papel se compone en funciones puras, porque del PDF no se puede afirmar nada.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, QuestPDF 2024.3.1, xUnit + Shouldly + Respawn, React 19 + TypeScript + Vite + Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-22-venta-local-design.md`

## Global Constraints

- **Rama del API:** crear `feat/venta-local` desde `origin/main`.
- **Rama del front:** crear `feat/venta-local` desde `origin/main`.
- **Nada de `dotnet test` ni `dotnet ef` directamente en Windows.** Smart App Control bloquea la carga del DLL desde OneDrive (error `0x800711C7`). Todo pasa por Docker.
  - Batería completa: `docker compose -f docker-compose.tests.yml run --rm tests`
  - Una clase: `docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"`
- **Punto de partida: 237 pruebas en verde.** Ninguna puede quedar roja.
- **Toda columna nueva NO anulable sobre una tabla con datos necesita `HasDefaultValue` en el `modelBuilder`**, no solo el inicializador de C#: EF no lee el inicializador y la migración saldría sin valor por defecto. Es una regla que este repositorio aprendió por las malas.
- **Respawn limpia la base antes de cada prueba** pero trunca **SIN RESTART IDENTITY**: nunca asumas que la primera fila sembrada tenga `Id` 1.
- **Azurite no se limpia entre pruebas**, solo Postgres.
- **Sembradores disponibles:** `Sembrador.ProductoraAsync(api, cedula, cat, comunidadId, activa)`, `Sembrador.PagoConNovedadAsync(api, cedula, monto)`, `Sembrador.ComprobanteBase64`.
- **Comunidades sembradas con `HasData`, Id estables:** 1 Patococha (PAT), 2 Las Nieves (NIE), 3 Huertas (HUE), 4 Nabón/El Progreso (NAB), 5 Pelincay (PEL).
- **Cédulas válidas** (el servicio revalida con el algoritmo ecuatoriano): `0104576277`, `0102030405`, `0111223343`.
- **Verificación del front:** `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los tres con salida 0. No hay Vitest ni Playwright.
- **Objetivos táctiles de 44 px** en el front: tablets de 7 pulgadas usadas en campo y con guantes.
- **Mutación obligatoria:** cada guarda nueva se comprueba quitándola, viendo la prueba en rojo y restaurándola. En los dos proyectos anteriores este paso encontró cuatro pruebas que pasaban con el fallo presente.
- **Mensajes de commit en castellano**, prefijo `feat:` / `fix:` / `test:` / `refactor:`, terminados en `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## File Structure

**API — se crean**

| Archivo | Responsabilidad |
|---|---|
| `Features/Pagos/Services/TextosVentaLocal.cs` | Composición pura de las líneas de venta local del ticket |
| `Infrastructure/Data/Migrations/*_VentaLocal.cs` | La migración |
| `tests/.../Integracion/VentaLocalTests.cs` | Marcado, trazabilidad, doble venta, concurrencia |
| `tests/.../Integracion/VentaLocalYPlantaTests.cs` | Resta al movilizar, cola de la planta, lotes pendientes |
| `tests/.../Unitarias/TextosVentaLocalTests.cs` | Los textos del ticket |

**API — se modifican**

| Archivo | Cambio |
|---|---|
| `Features/Recepcion/Models/CuyRegistro.cs` | + `VentaLocalPagoId`, `VentaLocalPago` |
| `Features/Pagos/Models/Pago.cs` | + `EsVentaLocal` |
| `Infrastructure/Data/AppDbContext.cs` | FK, índice y valor por defecto |
| `Features/Pagos/DTOs/PagoDtos.cs` | + `RegistrarVentaLocalDto`, `CuyDisponibleDto`; `PagoResponseDto` gana `EsVentaLocal` |
| `Features/Pagos/Services/PagoService.cs` | + registrar venta local y listar disponibles; guardas en `/pagar`, `/verificar`, cola y lotes pendientes |
| `Features/Pagos/Controllers/PagosController.cs` | + dos endpoints |
| `Features/Pagos/Services/TicketPagoService.cs` | El ticket de venta local |
| `Features/Recepcion/Services/MovilizacionService.cs` | Restar los vendidos, bloquear el lote agotado |
| `Features/Recepcion/Services/GuiaMovilizacionService.cs` | Bloque de vendidos localmente |
| `Features/Recepcion/Services/TextosGuia.cs` | + la línea de cada cuy vendido |
| `Features/Recepcion/DTOs/RecepcionDtos.cs` | `LoteResponseDto` gana el conteo de vendidos |

**Front — se modifican**

| Archivo | Cambio |
|---|---|
| `src/types/productora.ts` | + tipos de venta local |
| `src/types/recepcion.ts` | `LoteResponse` gana `cuyesVendidosLocal` |
| `src/api/pagos.ts` | + `cuyesDisponibles`, `registrarVentaLocal` |
| `src/components/recepcion/FormVentaLocal.tsx` | **nuevo** — el modal |
| `src/pages/Recepcion.tsx` | Botón, etiqueta «Venta local», renombrar la pestaña |

---

## Fase 1 · El modelo

### Task 1: La marca en la base

**Files:**
- Modify: `Features/Recepcion/Models/CuyRegistro.cs`
- Modify: `Features/Pagos/Models/Pago.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Create: `Infrastructure/Data/Migrations/*_VentaLocal.cs` (la genera la herramienta)

**Interfaces:**
- Consumes: nada.
- Produces:
  - `CuyRegistro.VentaLocalPagoId` (`int?`) y `CuyRegistro.VentaLocalPago` (`Pago?`)
  - `Pago.EsVentaLocal` (`bool`, por defecto `false`)

- [ ] **Step 1: Crear la rama**

```bash
git checkout -b feat/venta-local origin/main
```

- [ ] **Step 2: Añadir la marca a `CuyRegistro`**

En `Features/Recepcion/Models/CuyRegistro.cs`, añadir `using CoopagcuyApi.Features.Pagos.Models;` en la cabecera y estas propiedades al final de la clase:

```csharp
    // Venta local: la CAT vendió este animal en la comunidad en vez de
    // enviarlo a la planta, y queda atado al pago de esa venta. Nulo = sigue
    // disponible para movilizar.
    //
    // Vive aquí y no en una tabla intermedia porque la relación no tiene
    // ningún dato propio: el Pago ya lleva monto, fecha, responsable y
    // método, y lo único que faltaba era QUÉ animales.
    public int? VentaLocalPagoId { get; set; }
    public Pago? VentaLocalPago { get; set; }
```

- [ ] **Step 3: Añadir la bandera a `Pago`**

En `Features/Pagos/Models/Pago.cs`, después de `public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;`:

```csharp
    // La CAT vendió estos animales en la comunidad en vez de enviarlos a la
    // planta. Explícita y NO derivada de "tiene cuyes marcados": la cola de
    // trabajo de la planta se filtra con esto, y hacer esa decisión depender
    // de un Any() sobre otra tabla la vuelve una consulta en vez de un dato.
    //
    // Un pago de venta local nace ya cobrado (Estado = Recibido) porque no
    // queda nada que nadie tenga que hacer dentro del sistema: el dinero lo
    // recibió la propia CAT. PagadoPor, ComprobanteUrl, FechaVerificacion y
    // VerificadoPor se quedan nulos a propósito — rellenarlos afirmaría que
    // alguien transfirió y alguien verificó, y no ocurrió ninguna de las dos.
    public bool EsVentaLocal { get; set; } = false;
```

- [ ] **Step 4: Configurar el modelo**

En `Infrastructure/Data/AppDbContext.cs`, dentro del bloque `modelBuilder.Entity<CuyRegistro>(e => { … })`, después de la relación con `Productora`:

```csharp
            // Restrict y no Cascade: un pago no se borra nunca en este
            // sistema, pero con Cascade borrarlo desmarcaría los animales en
            // silencio y el lote volvería a parecer entero.
            e.HasOne(c => c.VentaLocalPago)
             .WithMany()
             .HasForeignKey(c => c.VentaLocalPagoId)
             .OnDelete(DeleteBehavior.Restrict);

            // Se consulta en cada movilización y en cada listado de lotes:
            // "los cuyes de este lote que siguen disponibles".
            e.HasIndex(c => c.VentaLocalPagoId);
```

Y dentro del bloque `modelBuilder.Entity<Pago>(e => { … })`, junto a las demás propiedades:

```csharp
            // Columna nueva NO anulable sobre una tabla con datos: el valor
            // por defecto va aquí y no solo en el inicializador de C#. EF no
            // lee el inicializador, y la migración saldría sin default
            // dejando indefinidas las filas que ya existen.
            e.Property(p => p.EsVentaLocal).HasDefaultValue(false);
```

- [ ] **Step 5: Generar la migración**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=placeholder;Username=postgres;Password=postgres" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet tool install --global dotnet-ef --version 8.* >/dev/null 2>&1; export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add VentaLocal --project CoopagcuyApi.csproj"
```

Esperado: `Done. To undo this action, use 'ef migrations remove'`.

- [ ] **Step 6: Leer la migración generada antes de seguir**

Abrir el `.cs` generado en `Infrastructure/Data/Migrations/` y comprobar tres cosas:

1. `EsVentaLocal` se añade con **`defaultValue: false`**. Si sale sin `defaultValue`, falta el `HasDefaultValue` del paso 4.
2. `VentaLocalPagoId` es **anulable** y **no** lleva valor por defecto.
3. Se crean la clave foránea con `ReferentialAction.Restrict` y el índice.

**Si algo está mal, no edites la migración a mano.** Bórrala junto con su `.Designer.cs`, restaura el snapshot con
`git checkout -- Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`,
corrige el `modelBuilder` y repite el paso 5. (`dotnet ef migrations remove`
**no sirve** aquí: intenta conectarse a la base y falla con la cadena de
marcador.)

- [ ] **Step 7: La batería sigue en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 237, Failed: 0`. La migración se aplica sola en el arranque de las pruebas; si alguna falla aquí, el esquema nuevo rompió algo y hay que resolverlo antes de seguir.

- [ ] **Step 8: Commit**

```bash
git add Features/ Infrastructure/
git commit -m "feat: la base sabe que un cuy se vendio en la comunidad

CuyRegistro gana la marca del pago de venta local y Pago gana la bandera
que la identifica. No hay entidad nueva: la relacion no tiene datos
propios, el Pago ya lleva monto, fecha, responsable y metodo.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 2 · Registrar la venta

### Task 2: El servicio y su endpoint

**Files:**
- Modify: `Features/Pagos/DTOs/PagoDtos.cs`
- Modify: `Features/Pagos/Services/PagoService.cs`
- Modify: `Features/Pagos/Controllers/PagosController.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/VentaLocalTests.cs` (crear)

**Interfaces:**
- Consumes: `CuyRegistro.VentaLocalPagoId` y `Pago.EsVentaLocal` (Tarea 1).
- Produces:
  - `POST /api/pagos/venta-local` con `RegistrarVentaLocalDto` → `PagoResponseDto`
  - `GET /api/pagos/cuyes-disponibles/{loteId:int}/{productoraId:int}` → `IEnumerable<CuyDisponibleDto>`
  - `IPagoService.RegistrarVentaLocalAsync(RegistrarVentaLocalDto, CentroAcopio?)`
  - `IPagoService.ListarCuyesDisponiblesAsync(int loteId, int productoraId, CentroAcopio?)`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/VentaLocalTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La CAT vende parte de una jaula en la comunidad en vez de enviarla a la
/// planta. Los animales vendidos quedan marcados uno a uno: es lo que después
/// permite restarlos del envío y decir en la guía cuáles se fueron.
///
/// La regla que sostiene la feature: solo se marcan cuyes DE ESA PRODUCTORA en
/// ESE LOTE que sigan disponibles. Es la misma trazabilidad que ya gobierna
/// los descuentos del pago a la planta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VentaLocalTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaA = "0104576277";
    private const string CedulaB = "0102030405";

    private sealed record RespuestaPago(int Id, bool EsVentaLocal, string Estado,
        decimal MontoUsd, decimal? MontoPagadoUsd, string MetodoPago);

    [Fact]
    public async Task LosCuyesVendidosQuedanAtadosAlPago()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 4);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(2).ToArray(),
                montoUsd = 30m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var pago = await respuesta.Content.ReadFromJsonAsync<RespuestaPago>();
        pago.ShouldNotBeNull();
        pago.EsVentaLocal.ShouldBeTrue();
        // Nace cobrada: el dinero lo recibió la propia CAT, no hay nada
        // pendiente que nadie tenga que hacer dentro del sistema.
        pago.Estado.ShouldBe("Recibido");
        pago.MontoPagadoUsd.ShouldBe(30m);

        await using var db = api.NuevoDbContext();
        var marcados = await db.CuyRegistros
            .Where(c => c.VentaLocalPagoId == pago.Id)
            .Select(c => c.Id)
            .ToListAsync();

        marcados.Count.ShouldBe(2);
        marcados.ShouldBe(cuyes.Ids.Take(2).ToList(), ignoreOrder: true);

        // Y los otros dos siguen libres para la planta.
        var libres = await db.CuyRegistros
            .CountAsync(c => c.LoteId == loteId && c.VentaLocalPagoId == null);
        libres.ShouldBe(2);
    }

    [Fact]
    public async Task NoSePuedenVenderCuyesDeOtraProductora()
    {
        // El corazón de la trazabilidad, igual que en los descuentos: la
        // operadora no puede cobrar por animales que entregó otra.
        var (loteId, deA) = await EntregaAsync(CedulaA, 2);
        var deB = await AgregarALoteAsync(loteId, CedulaB, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = deA.ProductoraId,
                loteId,
                cuyRegistroIds = deB.Ids,      // ← animales de la otra
                montoUsd = 30m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Pagos.AnyAsync()).ShouldBeFalse();
        (await db.CuyRegistros.AnyAsync(c => c.VentaLocalPagoId != null))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task UnCuyNoSePuedeVenderDosVeces()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 3);
        await VenderAsync(loteId, cuyes.ProductoraId, cuyes.Ids.Take(1).ToArray());

        var segunda = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),   // el mismo
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Pagos.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task NoSeVendeUnLoteQueYaSeMovilizo()
    {
        // Los animales ya no están en el centro: venderlos sería vender algo
        // que viaja hacia la planta.
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 3);
        await MovilizarAsync(loteId, 3);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnaVentaSinCuyesSeRechaza()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = Array.Empty<int>(),
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LasCuotasExigenElAcuerdo()
    {
        // Sin días ni valor por día, "a cuotas" no dice nada: el ticket que se
        // lleva la productora quedaría sin las condiciones que se pactaron.
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Cuotas",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnMetodoDePagoDesconocidoSeRechaza()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Trueque",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnaOperadoraDeOtroCentroNoPuedeVender()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("NIE")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora ajena"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DosVentasSimultaneasNoVendenElMismoCuyDosVeces()
    {
        // Comprobar que el cuy está libre y guardar después deja una ventana:
        // el último en escribir se lleva el animal y el otro pago cobra por
        // algo que no vendió. Es el mismo defecto que tuvo el ciclo de pago
        // con dos /pagar concurrentes.
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);
        var elMismo = cuyes.Ids.Take(1).ToArray();

        object Cuerpo(decimal monto) => new
        {
            productoraId = cuyes.ProductoraId,
            loteId,
            cuyRegistroIds = elMismo,
            montoUsd = monto,
            metodoPago = "Efectivo",
            responsable = "Operadora de prueba"
        };

        var a = api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", Cuerpo(15m));
        var b = api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", Cuerpo(20m));

        var respuestas = await Task.WhenAll(a, b);

        respuestas.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        await using var db = api.NuevoDbContext();

        // Y el perdedor NO deja un pago escrito cobrando por nada.
        (await db.Pagos.CountAsync()).ShouldBe(1);

        var pagoId = await db.Pagos.Select(p => p.Id).FirstAsync();
        var marcados = await db.CuyRegistros
            .CountAsync(c => c.VentaLocalPagoId == pagoId);
        marcados.ShouldBe(1);
    }

    [Fact]
    public async Task UnAnimalRechazadoSiSePuedeVenderLocalmente()
    {
        // Decisión del diseño: un cuy de bajo peso que la planta rechaza es
        // justo uno de los que tiene sentido colocar en la comunidad.
        // Prohibirlo obligaría a la CAT a sacarlo del sistema.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaA, CentroAcopio.PAT);

        // 900 g está por debajo del mínimo: el CAT lo marca Rechazado.
        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes = new[] { new
                {
                    pesoGramos = 900m,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Pequeño"
                }},
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId, cuyId;
        await using (var db = api.NuevoDbContext())
        {
            var cuy = await db.CuyRegistros
                .FirstAsync(c => c.ProductoraId == productora.Id);
            cuy.Estado.ShouldBe(EstadoLote.Rechazado);
            loteId = cuy.LoteId;
            cuyId = cuy.Id;
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = new[] { cuyId },
                montoUsd = 8m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task LosDisponiblesExcluyenLoYaVendido()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 3);
        await VenderAsync(loteId, cuyes.ProductoraId, cuyes.Ids.Take(1).ToArray());

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/cuyes-disponibles/{loteId}/{cuyes.ProductoraId}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        // Dos disponibles de los tres entregados.
        System.Text.Json.JsonDocument.Parse(cuerpo)
            .RootElement.GetArrayLength().ShouldBe(2);
    }

    // ── Sembrado ──────────────────────────────────────────────────────

    private sealed record CuyesDe(int ProductoraId, int[] Ids);

    /// Entrega real de N cuyes de esa productora en PAT. Devuelve el lote y
    /// los Id de los animales.
    private async Task<(int LoteId, CuyesDe Cuyes)> EntregaAsync(
        string cedula, int cantidad)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var ids = await EntregarAsync(productora.Id, cantidad);

        await using var db = api.NuevoDbContext();
        var loteId = await db.CuyRegistros
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.LoteId)
            .FirstAsync();

        return (loteId, new CuyesDe(productora.Id, ids));
    }

    /// Añade cuyes de OTRA productora a la jaula que ya está en armado.
    private async Task<CuyesDe> AgregarALoteAsync(
        int loteId, string cedula, int cantidad)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var ids = await EntregarAsync(productora.Id, cantidad);
        return new CuyesDe(productora.Id, ids);
    }

    private async Task<int[]> EntregarAsync(int productoraId, int cantidad)
    {
        var cuyes = Enumerable.Range(0, cantidad).Select(_ => new
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
                productoraId,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        return await db.CuyRegistros
            .Where(c => c.ProductoraId == productoraId)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToArrayAsync();
    }

    private async Task VenderAsync(int loteId, int productoraId, int[] cuyIds)
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId,
                loteId,
                cuyRegistroIds = cuyIds,
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        respuesta.EnsureSuccessStatusCode();
    }

    private async Task MovilizarAsync(int loteId, int cantidad)
    {
        string codigo;
        await using (var db = api.NuevoDbContext())
        {
            var lote = await db.Lotes.FirstAsync(l => l.Id == loteId);
            lote.Cerrado = true;
            await db.SaveChangesAsync();
            codigo = lote.CodigoLote;
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = cantidad,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });
        respuesta.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentaLocalTests"
```

Esperado: todas fallan con 404 — los endpoints no existen.

La ruta de movilización ya está verificada: `POST /api/recepcion/lotes/{codigoLote}/movilizacion`, en `RecepcionController.cs:231`.

- [ ] **Step 3: Los DTOs**

En `Features/Pagos/DTOs/PagoDtos.cs`:

```csharp
/// <summary>
/// Venta de parte de una jaula en la comunidad. Los animales viajan en la
/// petición uno a uno: la operadora elige cuáles, no cuántos, porque después
/// hay que decir en la guía exactamente qué se fue.
/// </summary>
public class RegistrarVentaLocalDto
{
    public int ProductoraId { get; set; }
    public int LoteId { get; set; }
    public List<int> CuyRegistroIds { get; set; } = [];
    public decimal MontoUsd { get; set; }

    // Efectivo | Transferencia | Cuotas
    public string MetodoPago { get; set; } = string.Empty;

    // Solo para "Cuotas": el acuerdo, sin seguimiento de qué cuota se pagó.
    public int? NumeroDias { get; set; }
    public decimal? ValorPorDia { get; set; }

    public string Responsable { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}

/// <summary>
/// Cuy de esa productora en ese lote que todavía puede venderse. Alimenta las
/// casillas del formulario de venta local.
/// </summary>
public record CuyDisponibleDto(
    int CuyRegistroId,
    int NumeroEnLote,
    decimal PesoGramos,
    string Estado,
    string? MotivoNovedad
);
```

Y añadir `bool EsVentaLocal` al final de `PagoResponseDto`, con su valor en el método `Mapear` de `PagoService`.

- [ ] **Step 4: El catálogo de métodos**

Al principio de la clase `PagoService`:

```csharp
    // Catálogo cerrado, como CondicionTransporte: el servidor no acepta un
    // método que no reconozca en vez de guardarlo. "Transferencia" también
    // vale en una venta local — alguien de la comunidad puede transferirle a
    // la CAT — y no se confunde con el pago de la planta porque eso lo
    // distingue EsVentaLocal, no el método.
    private static readonly HashSet<string> MetodosVentaLocal =
        new(StringComparer.OrdinalIgnoreCase)
        { "Efectivo", "Transferencia", "Cuotas" };
```

- [ ] **Step 5: Listar los disponibles**

Añadir a `IPagoService` y a `PagoService`:

```csharp
    /// Cuyes de esa productora en ese lote que todavía no se han vendido.
    Task<IEnumerable<CuyDisponibleDto>> ListarCuyesDisponiblesAsync(
        int loteId, int productoraId, CentroAcopio? filtroCat);
```

```csharp
    public async Task<IEnumerable<CuyDisponibleDto>> ListarCuyesDisponiblesAsync(
        int loteId, int productoraId, CentroAcopio? filtroCat)
    {
        if (filtroCat is CentroAcopio cat)
        {
            var productora = await db.Productoras.FindAsync(productoraId);
            if (productora is null || productora.CatAsignado != cat)
                throw new UnauthorizedAccessException(
                    "Tu usuario solo puede consultar productoras de su centro.");
        }

        return await db.CuyRegistros
            .Where(c => c.LoteId == loteId
                && c.ProductoraId == productoraId
                && c.VentaLocalPagoId == null)
            .OrderBy(c => c.NumeroEnLote)
            .Select(c => new CuyDisponibleDto(
                c.Id, c.NumeroEnLote, c.PesoGramos,
                c.Estado.ToString(), c.MotivoNovedad))
            .AsNoTracking()
            .ToListAsync();
    }
```

- [ ] **Step 6: Registrar la venta**

Añadir a `IPagoService`:

```csharp
    /// Registra una venta local: crea el pago ya cobrado y marca los animales.
    /// Todo o nada — si otro se llevó alguno mientras tanto, no queda pago.
    Task<PagoResponseDto> RegistrarVentaLocalAsync(
        RegistrarVentaLocalDto dto, CentroAcopio? filtroCat);
```

Y a `PagoService`:

```csharp
    public async Task<PagoResponseDto> RegistrarVentaLocalAsync(
        RegistrarVentaLocalDto dto, CentroAcopio? filtroCat)
    {
        var productora = await db.Productoras.FindAsync(dto.ProductoraId)
            ?? throw new KeyNotFoundException(
                $"Productora con Id {dto.ProductoraId} no encontrada.");

        if (filtroCat is CentroAcopio cat && productora.CatAsignado != cat)
            throw new UnauthorizedAccessException(
                "Tu usuario solo puede registrar ventas de productoras de su centro.");

        // ── Lo que se ve leyendo el cuerpo: 400 ──────────────────────
        if (dto.CuyRegistroIds.Count == 0)
            throw new CuerpoInvalidoException(
                "La venta local debe indicar al menos un cuy.");

        if (dto.MontoUsd <= 0)
            throw new CuerpoInvalidoException(
                "El monto de la venta debe ser mayor a cero.");

        if (!MetodosVentaLocal.Contains(dto.MetodoPago))
            throw new CuerpoInvalidoException(
                $"Método de pago no reconocido: '{dto.MetodoPago}'. " +
                $"Debe ser Efectivo, Transferencia o Cuotas.");

        var esCuotas = string.Equals(dto.MetodoPago, "Cuotas",
            StringComparison.OrdinalIgnoreCase);

        if (esCuotas && (dto.NumeroDias is not > 0 || dto.ValorPorDia is not > 0))
            throw new CuerpoInvalidoException(
                "Una venta a cuotas debe indicar el número de días y el valor por día.");

        if (string.IsNullOrWhiteSpace(dto.Responsable))
            throw new CuerpoInvalidoException("El responsable es obligatorio.");

        // Ids repetidos en la misma petición inflarían el conteo de filas
        // afectadas y harían pasar la comprobación de concurrencia de abajo.
        var ids = dto.CuyRegistroIds.Distinct().ToList();
        if (ids.Count != dto.CuyRegistroIds.Count)
            throw new CuerpoInvalidoException(
                "La venta local repite algún cuy.");

        // ── Lo que exige consultar el estado del servidor: 409 ───────
        var lote = await db.Lotes.FindAsync(dto.LoteId)
            ?? throw new KeyNotFoundException($"Lote con Id {dto.LoteId} no encontrado.");

        if (await db.Movilizaciones.AnyAsync(m => m.LoteId == lote.Id))
            throw new TransicionInvalidaException(
                "El lote ya se movilizó a la planta: sus animales ya no están en el centro.");

        // Misma regla que gobierna los descuentos: solo animales de ESA
        // productora en ESE lote, y que sigan disponibles.
        var validos = await db.CuyRegistros
            .Where(c => ids.Contains(c.Id)
                && c.LoteId == dto.LoteId
                && c.ProductoraId == dto.ProductoraId
                && c.VentaLocalPagoId == null)
            .Select(c => c.Id)
            .ToListAsync();

        var invalidos = ids.Except(validos).ToList();
        if (invalidos.Count > 0)
            throw new TransicionInvalidaException(
                $"Estos cuyes no pertenecen a la productora en ese lote, o ya se " +
                $"vendieron: {string.Join(", ", invalidos)}.");

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = dto.LoteId,
            MontoUsd = dto.MontoUsd,
            // Nace cobrada: el dinero lo recibió la propia CAT y no queda
            // nada que nadie tenga que hacer dentro del sistema.
            MontoPagadoUsd = dto.MontoUsd,
            Estado = EstadoPago.Recibido,
            EsVentaLocal = true,
            FechaPago = DateTime.UtcNow,
            MetodoPago = dto.MetodoPago,
            NumeroDias = esCuotas ? dto.NumeroDias : null,
            ValorPorDia = esCuotas ? dto.ValorPorDia : null,
            Responsable = dto.Responsable.Trim(),
            Observaciones = dto.Observaciones
        };

        await using var tx = await db.Database.BeginTransactionAsync();

        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        // El marcado es CONDICIONAL y se compara por filas afectadas. La
        // comprobación de arriba no basta: entre ella y esta escritura otra
        // venta puede llevarse el mismo animal, y sin esto el último en
        // escribir lo pisa y los dos pagos cobran por él.
        var afectadas = await db.CuyRegistros
            .Where(c => ids.Contains(c.Id) && c.VentaLocalPagoId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(
                c => c.VentaLocalPagoId, pago.Id));

        if (afectadas != ids.Count)
        {
            await tx.RollbackAsync();
            throw new TransicionInvalidaException(
                "Otra venta se registró sobre alguno de estos cuyes. " +
                "Vuelve a abrir la lista de disponibles.");
        }

        await tx.CommitAsync();

        return Mapear(pago, productora.NombreCompleto, lote.CodigoLote);
    }
```

Los nombres de las excepciones ya están verificados: `Common/Exceptions/` tiene
`CuerpoInvalidoException` (que el controlador traduce a 400) y
`TransicionInvalidaException` (a 409). Son los mismos que usa el ciclo de pago.

- [ ] **Step 7: Los endpoints**

En `PagosController`:

```csharp
    /// <summary>
    /// Cuyes de esa productora en ese lote que todavía pueden venderse.
    /// Alimenta las casillas del formulario de venta local.
    /// </summary>
    [HttpGet("cuyes-disponibles/{loteId:int}/{productoraId:int}")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> CuyesDisponibles(int loteId, int productoraId)
    {
        try
        {
            var resultado = await service.ListarCuyesDisponiblesAsync(
                loteId, productoraId, FiltroCat());
            return Ok(resultado);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Venta de parte de una jaula en la comunidad. No genera trabajo para la
    /// planta: nace cobrada.
    /// </summary>
    [HttpPost("venta-local")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> RegistrarVentaLocal(
        [FromBody] RegistrarVentaLocalDto dto)
    {
        try
        {
            var resultado = await service.RegistrarVentaLocalAsync(dto, FiltroCat());
            return CreatedAtAction(nameof(Listar), null, resultado);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (CuerpoInvalidoException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (TransicionInvalidaException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

- [ ] **Step 8: Ejecutar y ver que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentaLocalTests"
```

Esperado: `Passed: 11, Failed: 0`.

Si `DosVentasSimultaneasNoVendenElMismoCuyDosVeces` sale inestable —a veces dos 201—, **no la relajes**: significa que el marcado no es condicional de verdad. Revisa que el `ExecuteUpdateAsync` lleve el predicado `VentaLocalPagoId == null`.

- [ ] **Step 9: Comprobar por mutación**

Tres mutaciones, restaurando después de cada una:

1. Quitar `&& c.ProductoraId == dto.ProductoraId` del `Where` de validación.
   Esperado: falla `NoSePuedenVenderCuyesDeOtraProductora`.
2. Quitar `&& c.VentaLocalPagoId == null` del `ExecuteUpdateAsync`.
   Esperado: falla `DosVentasSimultaneasNoVendenElMismoCuyDosVeces`.
3. Sustituir el `if (afectadas != ids.Count)` por `if (false)`.
   Esperado: falla `DosVentasSimultaneasNoVendenElMismoCuyDosVeces`.

- [ ] **Step 10: Batería completa y commit**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 248, Failed: 0`.

```bash
git add Features/ tests/
git commit -m "feat: la CAT registra una venta local marcando los animales

Los cuyes vendidos quedan atados al pago uno a uno: es lo que despues
permite restarlos del envio y decir en la guia cuales se fueron. Solo se
marcan animales de esa productora en ese lote que sigan disponibles, la
misma trazabilidad que ya gobierna los descuentos.

El marcado es condicional y se compara por filas afectadas: comprobar y
guardar despues dejaba una ventana por la que dos ventas simultaneas
cobraban por el mismo animal.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Que la planta no toque una venta local

**Files:**
- Modify: `Features/Pagos/Services/PagoService.cs`
- Test: `tests/CoopagcuyApi.Tests/Integracion/VentaLocalYPlantaTests.cs` (crear)

**Interfaces:**
- Consumes: `RegistrarVentaLocalAsync` (Tarea 2).
- Produces: nada nuevo. Endurece `RegistrarPagoEfectivoAsync`, `VerificarAsync` y `ListarPorPagarAsync`.

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/VentaLocalYPlantaTests.cs` con esta clase y estas tres pruebas (el sembrado se completa en la Tarea 4, que añade más pruebas al mismo archivo):

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
/// Una venta local no genera trabajo para la planta y no se deja tocar por
/// ella: el dinero ya lo recibió la CAT. Y lo que se vendió deja de contar
/// para el envío.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VentaLocalYPlantaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task UnaVentaLocalNoApareceEnLaColaDeLaPlanta()
    {
        // Feature 2 del pedido: el operador de faenamiento no debe enterarse.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldNotContain($"\"id\":{venta.PagoId}");
    }

    [Fact]
    public async Task LaPlantaNoPuedePagarUnaVentaLocal()
    {
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{venta.PagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == venta.PagoId);
        pago.ComprobanteUrl.ShouldBeNull();
        pago.PagadoPor.ShouldBeNull();
    }

    [Fact]
    public async Task LaCatNoPuedeVerificarUnaVentaLocal()
    {
        // No hay nada que verificar: no existe transferencia ni captura.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{venta.PagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == venta.PagoId);
        pago.VerificadoPor.ShouldBeNull();
        pago.FechaVerificacion.ShouldBeNull();
    }

    // ── Sembrado compartido con la Tarea 4 ────────────────────────────

    private sealed record Venta(int PagoId, int LoteId, int ProductoraId, int[] CuyIds);

    /// Entrega de 5 cuyes en PAT y venta local de los `vendidos` primeros.
    private async Task<Venta> VenderAsync(int vendidos)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 5).Select(_ => new
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
        int[] ids;
        await using (var db = api.NuevoDbContext())
        {
            ids = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToArrayAsync();

            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0])
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids.Take(vendidos).ToArray(),
                montoUsd = 15m * vendidos,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoId = await db2.Pagos
            .Where(p => p.EsVentaLocal)
            .Select(p => p.Id)
            .FirstAsync();

        return new Venta(pagoId, loteId, productora.Id, ids);
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan las dos guardas**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentaLocalYPlantaTests"
```

Esperado: `UnaVentaLocalNoApareceEnLaColaDeLaPlanta` **pasa** (una venta local nace `Recibido`, y la cola filtra por `Pendiente`), y las otras dos **fallan**.

Que la primera pase ya es correcto: se escribe para que **siga** pasando cuando alguien cambie el filtro de la cola.

- [ ] **Step 3: Las dos guardas**

En `RegistrarPagoEfectivoAsync`, inmediatamente después de cargar el pago y **antes** de la comprobación de estado:

```csharp
        // La planta no participa en una venta local: el dinero ya lo recibió
        // la CAT. Sin esta guarda, un ticket de venta local aceptaría una
        // captura de transferencia y pasaría a un estado que no le
        // corresponde.
        if (pago.EsVentaLocal)
            throw new TransicionInvalidaException(
                "Es una venta local: la planta no tiene nada que pagar aquí.");
```

En `VerificarAsync`, en el mismo sitio relativo:

```csharp
        // Nada que verificar: no hubo transferencia ni captura.
        if (pago.EsVentaLocal)
            throw new TransicionInvalidaException(
                "Es una venta local: no hay pago de la planta que verificar.");
```

- [ ] **Step 4: La segunda defensa de la cola**

En `ListarPorPagarAsync`, añadir al `Where`:

```csharp
            .Where(p => p.Estado == EstadoPago.Pendiente
                && p.LoteId != null
                // Segunda defensa. Hoy basta con el estado —una venta local
                // nace Recibido— pero la cola es lo que decide qué trabajo ve
                // el operador de faenamiento, y no puede depender de un solo
                // predicado indirecto.
                && !p.EsVentaLocal)
```

- [ ] **Step 5: Ejecutar y comprobar por mutación**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentaLocalYPlantaTests"
```

Esperado: `Passed: 3, Failed: 0`.

Mutaciones, restaurando después de cada una:
1. Quitar la guarda de `RegistrarPagoEfectivoAsync` → falla `LaPlantaNoPuedePagarUnaVentaLocal`.
2. Quitar la guarda de `VerificarAsync` → falla `LaCatNoPuedeVerificarUnaVentaLocal`.

**La tercera —quitar `&& !p.EsVentaLocal` de la cola— NO pondrá roja ninguna prueba**, porque el filtro por estado ya la excluye. Eso es esperado y está bien: la línea es una segunda defensa deliberada, no la única. Déjalo anotado en el informe en vez de fingir que tiene dientes.

- [ ] **Step 6: Commit**

```bash
git add Features/ tests/
git commit -m "feat: la planta no ve ni toca una venta local

Ni aparece en su cola de trabajo, ni admite que la paguen, ni que la
verifiquen: el dinero ya lo recibio la CAT. El filtro de la cola gana
ademas una segunda defensa explicita, porque es lo que decide que trabajo
ve el operador de faenamiento.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: La resta al enviar a planta

**Files:**
- Modify: `Features/Recepcion/Services/MovilizacionService.cs`
- Modify: `Features/Pagos/Services/PagoService.cs` (`ListarLotesPendientesAsync`)
- Modify: `tests/CoopagcuyApi.Tests/Integracion/VentaLocalYPlantaTests.cs`

**Interfaces:**
- Consumes: el sembrado `VenderAsync` de la Tarea 3.
- Produces: nada nuevo.

- [ ] **Step 1: Añadir las pruebas que fallan**

Añadir a `VentaLocalYPlantaTests.cs`:

```csharp
    [Fact]
    public async Task ElEnvioSeLimitaALosCuyesQueQuedan()
    {
        // 5 entregados, 2 vendidos: la planta no puede recibir más de 3.
        var venta = await VenderAsync(2);
        var codigo = await CerrarYCodigoAsync(venta.LoteId);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 4,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConLoQueQuedaElEnvioSeAcepta()
    {
        var venta = await VenderAsync(2);
        var codigo = await CerrarYCodigoAsync(venta.LoteId);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 3,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });

        respuesta.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UnLoteVendidoEnteroYaNoSePuedeEnviar()
    {
        var venta = await VenderAsync(5);          // los cinco
        var codigo = await CerrarYCodigoAsync(venta.LoteId);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 1,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Movilizaciones.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task UnaVentaParcialNoDaElLotePorPagado()
    {
        // Vender 2 de 5 no salda lo que la planta debe pagar por los otros 3:
        // sin esto el lote desaparecía del selector de pago y esos animales
        // no se le cobraban a nadie.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{venta.ProductoraId}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain($"\"loteId\":{venta.LoteId}");
        // Y el conteo que ofrece es el de los que quedan, no el de la entrega.
        cuerpo.ShouldContain("\"cuyesEntregados\":3");
    }

    [Fact]
    public async Task UnLoteVendidoEnteroDejaDeEstarPendienteDePago()
    {
        var venta = await VenderAsync(5);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{venta.ProductoraId}");

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldNotContain($"\"loteId\":{venta.LoteId}");
    }

    private async Task<string> CerrarYCodigoAsync(int loteId)
    {
        await using var db = api.NuevoDbContext();
        var lote = await db.Lotes.FirstAsync(l => l.Id == loteId);
        lote.Cerrado = true;
        await db.SaveChangesAsync();
        return lote.CodigoLote;
    }
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentaLocalYPlantaTests"
```

Esperado: las cinco nuevas fallan; las tres de la Tarea 3 siguen verdes.

- [ ] **Step 3: La resta en la movilización**

En `MovilizacionService.RegistrarAsync`, sustituir el bloque:

```csharp
        if (dto.CantidadMovilizada > lote.CantidadAnimales)
            throw new InvalidOperationException(
                $"La cantidad movilizada ({dto.CantidadMovilizada}) supera la " +
                $"cantidad recibida en el lote ({lote.CantidadAnimales}).");
```

por:

```csharp
        // Lo que se vendió en la comunidad ya no está en el centro. El
        // cálculo es POR ANIMAL y no por productora: eso es lo que resuelve
        // solo la jaula compartida, donde una vendió lo suyo y la otra no.
        //
        // Se cuenta lo VENDIDO y se resta, en vez de contar lo disponible.
        // Parece lo mismo y no lo es: una jaula histórica cargada sin detalle
        // por animal no tiene filas en CuyRegistros, y contar disponibles ahí
        // daría cero — bloqueando el envío de un lote que nadie vendió.
        // Restando, esa jaula da vendidos = 0 y conserva exactamente la
        // conducta de hoy.
        var vendidos = await db.CuyRegistros
            .CountAsync(c => c.LoteId == lote.Id && c.VentaLocalPagoId != null);

        var disponibles = lote.CantidadAnimales - vendidos;

        if (disponibles <= 0)
            throw new InvalidOperationException(
                $"El lote {codigoLote} se vendió completo en la comunidad: " +
                $"no queda ningún animal que enviar a la planta.");

        if (dto.CantidadMovilizada > disponibles)
            throw new InvalidOperationException(
                $"La cantidad movilizada ({dto.CantidadMovilizada}) supera los " +
                $"animales disponibles del lote ({disponibles}): " +
                $"{vendidos} se vendieron en la comunidad.");
```

- [ ] **Step 4: Los lotes pendientes de pago**

En `ListarLotesPendientesAsync`, sustituir:

```csharp
        var pagados = db.Pagos
            .Where(p => p.ProductoraId == productoraId && p.LoteId != null)
            .Select(p => p.LoteId!.Value);
```

por:

```csharp
        // Solo los pagos de la PLANTA saldan el lote. Una venta local cobra
        // los animales que se quedaron en la comunidad; los que viajan siguen
        // pendientes de que alguien los pague, y sin esta distinción el lote
        // desaparecía del selector y esos animales no se le cobraban a nadie.
        var pagados = db.Pagos
            .Where(p => p.ProductoraId == productoraId
                && p.LoteId != null
                && !p.EsVentaLocal)
            .Select(p => p.LoteId!.Value);
```

Y en la proyección, sustituir el conteo y la suma de pesos para que excluyan lo
vendido:

```csharp
                // Lo que queda por enviar de ESTA productora: es la base
                // sobre la que la planta va a pagar.
                l.Cuyes.Count(c => c.ProductoraId == productoraId
                    && c.VentaLocalPagoId == null),
                l.Cuyes
                    .Where(c => c.ProductoraId == productoraId
                        && c.VentaLocalPagoId == null)
                    .Sum(c => (decimal?)c.PesoGramos) ?? 0))
```

Y añadir al `Where` de la consulta, para que un lote sin nada pendiente
desaparezca del selector:

```csharp
                && l.Cuyes.Any(c => c.ProductoraId == productoraId
                    && c.VentaLocalPagoId == null)
```

- [ ] **Step 5: Ejecutar y comprobar por mutación**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentaLocalYPlantaTests"
```

Esperado: `Passed: 8, Failed: 0`.

Mutaciones, restaurando después de cada una:
1. Volver `disponibles` a `lote.CantidadAnimales` → falla `ElEnvioSeLimitaALosCuyesQueQuedan`.
2. Quitar el `if (disponibles == 0)` → falla `UnLoteVendidoEnteroYaNoSePuedeEnviar`.
3. Quitar `&& !p.EsVentaLocal` de `pagados` → falla `UnaVentaParcialNoDaElLotePorPagado`.

- [ ] **Step 6: Batería completa y commit**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 256, Failed: 0`.

```bash
git add Features/ tests/
git commit -m "feat: lo vendido en la comunidad se resta del envio a planta

El tope de la movilizacion pasa a ser los animales disponibles, y un lote
vendido entero ya no se puede enviar. El calculo es por animal y no por
productora: eso resuelve solo la jaula compartida, donde una vendio lo
suyo y la otra no.

Se arregla de paso ListarLotesPendientes, que daba por pagado el lote
entero tras una venta parcial y dejaba sin cobrar los animales que si
viajaban.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 3 · El papel

### Task 5: El ticket de venta local

**Files:**
- Create: `Features/Pagos/Services/TextosVentaLocal.cs`
- Create: `tests/CoopagcuyApi.Tests/Unitarias/TextosVentaLocalTests.cs`
- Modify: `Features/Pagos/Services/TicketPagoService.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs`

**Interfaces:**
- Consumes: `Pago.EsVentaLocal`, `Pago.NumeroDias`, `Pago.ValorPorDia`.
- Produces:
  - `TextosVentaLocal.Encabezado(Pago)` → `"COMPROBANTE DE PAGO"` o `"VENTA LOCAL"`
  - `TextosVentaLocal.TextoEstado(Pago)` → el rótulo de estado
  - `TextosVentaLocal.LineaMetodo(Pago)` → `"Efectivo"` / `"A cuotas: 30 días × USD 2,50"`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Unitarias/TextosVentaLocalTests.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Pagos.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El ticket de una venta local tiene que decir que lo es. La productora se
/// lleva ese papel y es el único canal por el que sabe bajo qué condiciones se
/// le pagó — sobre todo si fue a cuotas, donde el dinero todavía no llegó.
///
/// Funciones puras porque del PDF no se puede afirmar nada: QuestPDF comprime
/// los flujos de texto del documento.
/// </summary>
public class TextosVentaLocalTests
{
    private static Pago Local(string metodo, int? dias = null, decimal? valor = null) =>
        new()
        {
            EsVentaLocal = true,
            Estado = EstadoPago.Recibido,
            MontoUsd = 75m,
            MontoPagadoUsd = 75m,
            MetodoPago = metodo,
            NumeroDias = dias,
            ValorPorDia = valor
        };

    [Fact]
    public void ElEncabezadoDistingueLaVentaLocal()
    {
        TextosVentaLocal.Encabezado(Local("Efectivo")).ShouldBe("VENTA LOCAL");
    }

    [Fact]
    public void UnPagoDeLaPlantaConservaSuEncabezado()
    {
        // Garantía de no regresión: el ticket del ciclo con la planta no
        // cambia ni una letra.
        var dePlanta = new Pago { EsVentaLocal = false, MontoUsd = 120m };

        TextosVentaLocal.Encabezado(dePlanta).ShouldBe("COMPROBANTE DE PAGO");
    }

    [Fact]
    public void UnaVentaEnEfectivoDiceQueYaSeCobro()
    {
        TextosVentaLocal.TextoEstado(Local("Efectivo"))
            .ShouldBe("VENDIDO EN LA COMUNIDAD — COBRADO");
    }

    [Fact]
    public void UnaVentaACuotasNoDiceQueYaSeCobro()
    {
        // El dinero no ha llegado: el papel no puede afirmar lo contrario,
        // aunque el estado interno del pago sea Recibido.
        var texto = TextosVentaLocal.TextoEstado(Local("Cuotas", 30, 2.5m));

        texto.ShouldBe("VENDIDO EN LA COMUNIDAD — A CUOTAS");
        texto.ShouldNotContain("COBRADO");
    }

    [Fact]
    public void LaLineaDeMetodoLlevaElAcuerdoDeCuotas()
    {
        TextosVentaLocal.LineaMetodo(Local("Cuotas", 30, 2.5m))
            .ShouldBe("A cuotas: 30 días × USD 2,50");
    }

    [Fact]
    public void LaLineaDeMetodoSinCuotasEsSoloElMetodo()
    {
        TextosVentaLocal.LineaMetodo(Local("Efectivo")).ShouldBe("Efectivo");
        TextosVentaLocal.LineaMetodo(Local("Transferencia")).ShouldBe("Transferencia");
    }

    [Fact]
    public void ElAcuerdoNoDependeDeLaCulturaDeLaMaquina()
    {
        // Mismo motivo que las fechas: el separador decimal cambia con la
        // cultura activa del contenedor.
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            TextosVentaLocal.LineaMetodo(Local("Cuotas", 30, 2.5m))
                .ShouldBe("A cuotas: 30 días × USD 2,50");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = anterior;
        }
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TextosVentaLocalTests"
```

Esperado: error de compilación — el tipo `TextosVentaLocal` no existe.

- [ ] **Step 3: Implementar**

Crear `Features/Pagos/Services/TextosVentaLocal.cs`:

```csharp
using System.Globalization;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;

namespace CoopagcuyApi.Features.Pagos.Services;

/// <summary>
/// Las líneas del ticket que cambian cuando el pago es una venta local.
///
/// Fuera del armado del PDF por el mismo motivo que TextosGuia y
/// TextosTicket: QuestPDF comprime los flujos de texto del documento, así que
/// del binario no se puede afirmar nada. Como funciones puras sí se comprueban.
/// </summary>
public static class TextosVentaLocal
{
    public static string Encabezado(Pago pago) =>
        pago.EsVentaLocal ? "VENTA LOCAL" : "COMPROBANTE DE PAGO";

    /// <summary>
    /// Rótulo de estado.
    ///
    /// Una venta a cuotas es Recibido por dentro —no queda nada que nadie
    /// tenga que hacer en el sistema— pero el dinero todavía no llegó. El
    /// papel que se lleva la productora no puede decir "cobrado" cuando no lo
    /// está: por eso las cuotas tienen su propio rótulo.
    /// </summary>
    public static string TextoEstado(Pago pago)
    {
        if (!pago.EsVentaLocal) return TicketPagoService.TextoEstado(pago.Estado);

        return EsCuotas(pago)
            ? "VENDIDO EN LA COMUNIDAD — A CUOTAS"
            : "VENDIDO EN LA COMUNIDAD — COBRADO";
    }

    /// <summary>
    /// "Efectivo", o "A cuotas: 30 días × USD 2,50".
    ///
    /// InvariantCulture por el mismo motivo que las fechas: el separador
    /// decimal cambia con la cultura activa del contenedor, y la cifra del
    /// acuerdo es de las que la productora mira primero.
    /// </summary>
    public static string LineaMetodo(Pago pago)
    {
        if (!EsCuotas(pago)) return pago.MetodoPago;

        var valor = (pago.ValorPorDia ?? 0)
            .ToString("N2", CultureInfo.InvariantCulture)
            .Replace('.', ',');

        return $"A cuotas: {pago.NumeroDias} días × USD {valor}";
    }

    private static bool EsCuotas(Pago pago) =>
        string.Equals(pago.MetodoPago, "Cuotas", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Ejecutar y ver que pasan**

Esperado: `Passed: 7, Failed: 0`.

- [ ] **Step 5: Maquetar en el ticket**

En `TicketPagoService.GenerarAsync`:

1. Sustituir el subtítulo fijo `"Comprobante de pago"` por
   `TextosVentaLocal.Encabezado(pago)`.
2. Sustituir `TextoEstado(pago.Estado)` por `TextosVentaLocal.TextoEstado(pago)`.
3. Añadir, justo debajo del rótulo de estado:

```csharp
                    col.Item().AlignCenter()
                        .Text(TextosVentaLocal.LineaMetodo(pago)).FontSize(8);
```

4. Cuando el pago es una venta local, añadir un bloque con los animales
   vendidos, después del bloque LOTE:

```csharp
                    if (pago.EsVentaLocal)
                    {
                        col.Item().Text("ANIMALES VENDIDOS").Bold();
                        col.Item().Text(string.Join(", ",
                            vendidos.Select(n => $"#{n}")));
                        col.Item().LineHorizontal(0.5f);
                    }
```

donde `vendidos` se consulta junto al resto de los datos del pago:

```csharp
        // Números de los animales que cubre esta venta. Van al papel para que
        // la productora pueda contrastarlos con los que entregó.
        var vendidos = pago.EsVentaLocal
            ? await db.CuyRegistros
                .Where(c => c.VentaLocalPagoId == pago.Id)
                .OrderBy(c => c.NumeroEnLote)
                .Select(c => c.NumeroEnLote)
                .ToListAsync()
            : [];
```

- [ ] **Step 6: La prueba de integración del ticket**

Añadir a `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs`:

```csharp
    [Fact]
    public async Task ElTicketDeUnaVentaLocalSeDescargaYEsMasLargo()
    {
        // Las unitarias de TextosVentaLocal construyen el Pago en memoria:
        // pasarían aunque la consulta de los animales vendidos faltara. Aquí
        // se comprueba que el bloque llega al documento.
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
        int[] ids;
        await using (var db = api.NuevoDbContext())
        {
            ids = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync();
            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0]).Select(c => c.LoteId).FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids,
                montoUsd = 45m,
                metodoPago = "Cuotas",
                numeroDias = 30,
                valorPorDia = 1.5m,
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        int pagoId;
        await using (var db = api.NuevoDbContext())
            pagoId = await db.Pagos.Where(p => p.EsVentaLocal)
                .Select(p => p.Id).FirstAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        bytes[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }
```

- [ ] **Step 7: Comprobar por mutación**

Quitar el `if (pago.EsVentaLocal)` del encabezado (dejarlo siempre en
`"COMPROBANTE DE PAGO"`). Esperado: falla
`ElEncabezadoDistingueLaVentaLocal`. **Restaurar.**

Cambiar `TextoEstado` para que las cuotas devuelvan el mismo texto que el
efectivo. Esperado: falla `UnaVentaACuotasNoDiceQueYaSeCobro`. **Restaurar.**

- [ ] **Step 8: Batería completa y commit**

Esperado: `Passed: 264, Failed: 0`.

```bash
git add Features/ tests/
git commit -m "feat: el ticket dice cuando la venta fue en la comunidad

Encabezado propio, la lista de animales vendidos y la linea del metodo,
con el acuerdo cuando es a cuotas. Una venta a cuotas NO dice cobrado en
el papel aunque su estado interno sea Recibido: el dinero no ha llegado.

El ticket del ciclo con la planta no cambia ni una letra.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: La guía lista lo vendido

**Files:**
- Modify: `Features/Recepcion/Services/TextosGuia.cs`
- Modify: `Features/Recepcion/Services/GuiaMovilizacionService.cs`
- Modify: `tests/CoopagcuyApi.Tests/Unitarias/TextosGuiaTests.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs`

**Interfaces:**
- Consumes: `CuyRegistro.VentaLocalPagoId`, `TextosGuia`.
- Produces: `TextosGuia.LineaVentaLocal(CuyRegistro, DateTime)` → `"#3 · María Quizhpi (Patococha) · 21/08/2026"`

- [ ] **Step 1: La prueba unitaria del texto**

Añadir a `tests/CoopagcuyApi.Tests/Unitarias/TextosGuiaTests.cs`:

```csharp
    [Fact]
    public void LineaVentaLocal_nombraAlAnimalYaSuProductora()
    {
        var cuy = new CuyRegistro
        {
            NumeroEnLote = 3,
            Productora = new Productora
            {
                NombreCompleto = "María Quizhpi",
                Comunidad = new Comunidad { Nombre = "Patococha" }
            }
        };
        var fecha = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);

        // La fecha va en hora local del piloto, como el resto del documento.
        TextosGuia.LineaVentaLocal(cuy, fecha)
            .ShouldBe("#3 · María Quizhpi (Patococha) · 21/08/2026");
    }

    [Fact]
    public void LineaVentaLocal_sinProductoraNoRevienta()
    {
        var cuy = new CuyRegistro { NumeroEnLote = 7, Productora = null };
        var fecha = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        TextosGuia.LineaVentaLocal(cuy, fecha).ShouldBe("#7 · — · 21/08/2026");
    }
```

Comprobar los `using` que ya tenga el archivo y añadir los que falten.

- [ ] **Step 2: Ejecutar y ver que falla**

Esperado: error de compilación — `LineaVentaLocal` no existe.

- [ ] **Step 3: Implementar el texto**

Añadir a `Features/Recepcion/Services/TextosGuia.cs`:

```csharp
    /// <summary>
    /// "#3 · María Quizhpi (Patococha) · 21/08/2026" — un animal que se vendió
    /// en la comunidad en vez de viajar a la planta.
    ///
    /// Reutiliza <see cref="Productora"/> para el nombre, así que hereda su
    /// manejo del caso sin productora y del de comunidad sin cargar.
    /// </summary>
    public static string LineaVentaLocal(CuyRegistro cuy, DateTime fechaVenta) =>
        $"#{cuy.NumeroEnLote} · {Productora(cuy)} · {FechaUtc.FechaLocal(fechaVenta)}";
```

Añadir `using CoopagcuyApi.Common;` si no está.

- [ ] **Step 4: El bloque en la guía**

En `GuiaMovilizacionService.GenerarGuiaPdfAsync`, ampliar la consulta del lote
para incluir la venta de cada cuy, y añadir el bloque después del detalle por
animal:

```csharp
                    // Los animales que no viajaron. Van en la guía porque es
                    // el documento que acompaña al transporte: sin esto, la
                    // diferencia entre lo recibido y lo movilizado no tiene
                    // explicación en el propio papel.
                    var vendidos = lote.Cuyes
                        .Where(c => c.VentaLocalPagoId != null)
                        .OrderBy(c => c.NumeroEnLote)
                        .ToList();

                    if (vendidos.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("VENDIDOS EN LA COMUNIDAD")
                            .Bold();
                        col.Item().Text(
                            $"{vendidos.Count} de {lote.CantidadAnimales} animales " +
                            $"no viajaron a la planta:");

                        foreach (var cuy in vendidos)
                            col.Item().Text(TextosGuia.LineaVentaLocal(
                                cuy, cuy.VentaLocalPago!.FechaPago)).FontSize(9);
                    }
```

El `Include` necesario: `.Include(l => l.Cuyes).ThenInclude(c => c.VentaLocalPago)`.

- [ ] **Step 5: La prueba de integración**

Añadir a `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs` una
prueba que siembre un lote con venta parcial, descargue la guía y afirme que el
PDF es **sensiblemente más largo** que el de un lote equivalente sin ventas.

Umbral: medir empíricamente cuánto crece con dos animales vendidos y dejar el
umbral holgadamente por debajo, con un comentario que diga el número medido.
Es la misma técnica que cerró el hueco del desglose de descuentos: del
contenido del PDF no se puede afirmar nada, pero de que el bloque llegó, sí.

Reutiliza el sembrador que ya tiene esa clase —`SembrarLoteAsync()`, que
devuelve el código del lote— en vez de duplicar el montaje. Necesitarás además
vender un par de animales de ese lote antes de descargar la guía.

- [ ] **Step 6: Comprobar por mutación**

Quitar el `.ThenInclude(c => c.VentaLocalPago)`. Esperado: la prueba de
integración falla con un 500. **Restaurar.**

Envolver el bloque en `if (false)`. Esperado: falla la prueba de integración
por longitud. **Restaurar.**

- [ ] **Step 7: Batería completa y commit**

Esperado: `Passed: 267, Failed: 0`.

```bash
git add Features/ tests/
git commit -m "feat: la guia lista los animales vendidos en la comunidad

Es el documento que acompana al transporte: sin este bloque, la
diferencia entre lo recibido y lo movilizado no tenia explicacion en el
propio papel. Una guia de un lote sin ventas locales sale identica.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 4 · La pantalla

### Task 7: El modal de venta local

**Files:**
- Modify: `src/types/productora.ts`
- Modify: `src/api/pagos.ts`
- Create: `src/components/recepcion/FormVentaLocal.tsx`

**Interfaces:**
- Consumes: `GET /api/pagos/cuyes-disponibles/{loteId}/{productoraId}` y `POST /api/pagos/venta-local`.
- Produces: `<FormVentaLocal lote={…} onClose={…} />`

- [ ] **Step 1: Crear la rama del front**

```bash
git checkout -b feat/venta-local origin/main
```

- [ ] **Step 2: Los tipos**

Añadir a `src/types/productora.ts`:

```ts
export interface CuyDisponible {
    cuyRegistroId: number;
    numeroEnLote: number;
    pesoGramos: number;
    estado: string;
    motivoNovedad: string | null;
}

export type MetodoVentaLocal = "Efectivo" | "Transferencia" | "Cuotas";

export interface RegistrarVentaLocalRequest {
    productoraId: number;
    loteId: number;
    cuyRegistroIds: number[];
    montoUsd: number;
    metodoPago: MetodoVentaLocal;
    numeroDias?: number;
    valorPorDia?: number;
    responsable: string;
    observaciones?: string;
}
```

Y añadir `esVentaLocal: boolean;` a la interfaz `Pago`.

- [ ] **Step 3: El cliente**

Añadir a `src/api/pagos.ts`:

```ts
    // Cuyes de esa productora en ese lote que todavía pueden venderse: el
    // servidor ya excluye los vendidos, así que no hace falta filtrar aquí
    cuyesDisponibles: async (loteId: number, productoraId: number) => {
        const { data } = await client.get<CuyDisponible[]>(
            `/api/pagos/cuyes-disponibles/${loteId}/${productoraId}`);
        return data;
    },

    registrarVentaLocal: async (body: RegistrarVentaLocalRequest) => {
        const { data } = await client.post<Pago>("/api/pagos/venta-local", body);
        return data;
    },
```

Con sus imports de tipos.

- [ ] **Step 4: El modal**

Crear `src/components/recepcion/FormVentaLocal.tsx`, siguiendo el patrón de
`FormPago.tsx` (mismo `ModalShell`, misma gestión de error, mismo
`useMutation`). Requisitos concretos:

- Selector de productora, cargado de las que aportaron al lote. Si solo hay una,
  preseleccionada.
- **Los cuyes se cargan con `useQuery` y `enabled`**, nunca desde un `useEffect`:
  la regla `react-hooks/set-state-in-effect` rechaza disparar peticiones desde
  un efecto —incluso indirectamente— y ya rompió un despliegue.
- Una casilla por cuy disponible, con su número, su peso y su estado. Los que
  vienen con novedad se marcan visualmente, pero **se pueden vender igual**: un
  animal de bajo peso que la planta rechaza es justo uno de los que tiene
  sentido vender en la comunidad.
- Un botón «Seleccionar todos».
- Monto, forma de pago (tres opciones) y, solo con «Cuotas», los campos de días
  y valor por día.
- El botón de guardar se deshabilita si no hay ningún cuy marcado, si el monto
  no es mayor que cero, o si con cuotas falta el acuerdo.
- **Objetivos táctiles de 44 px**: `h-12` como mínimo en casillas y botones.

- [ ] **Step 5: Verificar**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

Los tres con salida 0.

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "feat: modal de venta local con seleccion de animales

La operadora marca que cuyes vende, no cuantos: la guia tiene que decir
despues exactamente cuales se quedaron. Un animal con novedad se puede
vender igual — es justo uno de los que tiene sentido colocar en la
comunidad.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: El botón, la etiqueta y la pestaña

**Files:**
- Modify: `src/pages/Recepcion.tsx`
- Modify: `src/types/recepcion.ts`
- Modify: `Features/Recepcion/DTOs/RecepcionDtos.cs` (API)
- Modify: `Features/Recepcion/Services/RecepcionService.cs` (API)

**Interfaces:**
- Consumes: `FormVentaLocal` (Tarea 7).
- Produces: `LoteResponseDto.CuyesVendidosLocal` (`int`).

- [ ] **Step 1: El dato en el API**

`Recepcion.tsx` necesita saber cuántos animales del lote se vendieron para
decidir qué botones pinta. Añadir `int CuyesVendidosLocal` a `LoteResponseDto`
y rellenarlo en `RecepcionService.MapearLoteAsync` (y en cualquier otra
proyección que construya ese DTO — **búscalas todas con `grep`**):

```csharp
                lote.Cuyes.Count(c => c.VentaLocalPagoId != null),
```

Y añadir `cuyesVendidosLocal: number;` a `LoteResponse` en
`src/types/recepcion.ts`.

- [ ] **Step 2: Ejecutar la batería del API**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 267, Failed: 0`. Un campo nuevo en el DTO no rompe nada, pero
si alguna prueba deserializa ese DTO a un `record` propio, habrá que ampliarlo.

- [ ] **Step 3: El botón «Vender local»**

En `src/pages/Recepcion.tsx`, junto al botón «A planta», con las mismas
condiciones más la de que quede algo por vender:

```tsx
                                            {l.estado !== "Rechazado" && l.cerrado &&
                                                !l.tieneMovilizacion &&
                                                l.cuyesVendidosLocal < l.cuyes.length && (
                                                    <button
                                                        onClick={() => setLoteVentaLocal(l)}
                                                        title="Registrar una venta en la comunidad"
                                                        className="text-xs font-semibold text-primary-700
                                     hover:text-primary-600"
                                                    >
                                                        Vender local
                                                    </button>
                                                )}
```

Con su estado `const [loteVentaLocal, setLoteVentaLocal] = useState<LoteResponse | null>(null);`
y el renderizado del modal al final, igual que se hace con `loteMovilizar`.

- [ ] **Step 4: La etiqueta «Venta local»**

Sustituir la condición del botón «A planta» y del texto «Enviado ✓» para
contemplar el lote vendido entero:

```tsx
                                            {/* Vendido entero: no queda nada que
                                                enviar ni que vender. La guía sigue
                                                disponible, y ahora además lista lo
                                                que se quedó en la comunidad. */}
                                            {l.cuyesVendidosLocal > 0 &&
                                                l.cuyesVendidosLocal === l.cuyes.length && (
                                                    <span className="text-xs font-bold text-primary-700"
                                                        title="Todo el lote se vendió en la comunidad">
                                                        Venta local
                                                    </span>
                                                )}
```

Y añadir a la condición del botón «A planta» que solo se pinte si queda algo:
`&& l.cuyesVendidosLocal < l.cuyes.length`.

**El botón «Guía PDF» se queda siempre.**

- [ ] **Step 5: Renombrar la pestaña**

En la definición de pestañas, cambiar la etiqueta `Locales` por
`Sin sincronizar`, y actualizar el texto que la menciona:

```tsx
                            Sin conexión. Cambia a la pestaña "Sin sincronizar"
                            para ver
```

El `id: "local"` de la pestaña **no se toca**: es una clave interna y cambiarla
solo añadiría riesgo.

- [ ] **Step 6: Verificar**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

- [ ] **Step 7: Comprobación manual**

Con el API corriendo:
1. Un lote cerrado sin ventas: se ven «A planta», «Vender local» y «Guía PDF».
2. Vender parte: siguen los tres, y el envío a planta admite como mucho lo que queda.
3. Vender el resto: desaparecen «A planta» y «Vender local», aparece «Venta local», sigue «Guía PDF».
4. La pestaña dice «Sin sincronizar».

- [ ] **Step 8: Commit**

```bash
git add src/ Features/
git commit -m "feat: la lista de lotes ofrece vender local y marca el lote vendido

Un lote vendido entero deja de ofrecer envio a planta y venta, y muestra
"Venta local" en el sitio donde antes decia "Enviado". La pestana de
lotes capturados sin conexion pasa a llamarse "Sin sincronizar": se
llamaba "Locales" y chocaba de frente con esta feature.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Cierre

- [ ] **Batería completa del API en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Failed: 0`, con 267 pruebas (237 de partida + 30 nuevas).

- [ ] **Front verificado**

```bash
pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Verificación manual obligatoria**

Ninguna prueba cubre esto: el PDF es binario y su maquetación no se puede
afirmar desde código.

1. **Imprimir un ticket de venta local en efectivo.** Encabezado «VENTA LOCAL»,
   la lista de animales vendidos, el método, y el estado «COBRADO».
2. **Imprimir uno a cuotas.** El estado debe decir «A CUOTAS» y **no**
   «COBRADO», y la línea del acuerdo debe leerse `A cuotas: 30 días × USD 2,50`.
3. **Imprimir la guía de un lote con venta parcial.** El bloque «VENDIDOS EN LA
   COMUNIDAD» con un renglón por animal, y la cantidad movilizada contra la
   recibida.
4. **Imprimir la guía de un lote sin ventas locales.** Debe salir **idéntica** a
   las de antes: es la garantía de no regresión.

- [ ] **Abrir los dos PR**, el del API primero: el front consume dos endpoints
      y un campo del DTO que no existen todavía en producción.

## Lo que este plan deja fuera a propósito

- **Seguimiento de cuotas**: qué cuota se pagó y cuánto queda. Es un proyecto propio.
- **A quién se le vendió.** La trazabilidad termina donde el animal sale del CAT.
- **Deshacer una venta local.** Coherente con que un pago no se anula.
- **El precio por animal.** El monto lo sigue escribiendo la operadora.
- **El reporte de ganancias** es el Proyecto C, y este spec le deja escrita la
  obligación de separar las ventas a cuotas de lo realmente cobrado.

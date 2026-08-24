# Retención a 90 días — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que las pantallas operativas muestren por defecto los últimos 90 días —sin borrar nada y sin esconder trabajo pendiente— con un filtro de fechas para ampliar el rango cuando haga falta.

**Architecture:** Cinco de los seis listados afectados **ya aceptan `desde`/`hasta`**: lo que falta es un valor por defecto cuando no se envía `desde`, un helper único que lo calcule sobre el día local del piloto, y que el front exponga el filtro. Las colas de trabajo pendiente quedan fuera del límite a propósito.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, xUnit + Shouldly + Respawn, React 19 + TypeScript + Vite + Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-23-retencion-90-dias-design.md`

## Global Constraints

- **Rama del API y del front:** crear `feat/retencion-90-dias` desde `origin/main`. Este proyecto es **independiente** de A, B, C y D.
- **Nada se borra.** El límite es de visualización; la base conserva todo porque los reportes lo necesitan y porque es el registro de trazabilidad del sistema.
- **Nada de `dotnet test` directamente en Windows.** Smart App Control bloquea la carga del DLL desde OneDrive (`0x800711C7`).
  - Batería completa: `docker compose -f docker-compose.tests.yml run --rm tests`
  - Una clase: `docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"`
  - Puede tardar varios minutos; usa un timeout amplio.
- **Punto de partida: la batería de `origin/main` en verde.** Ejecútala antes de tocar nada y anota el número: es tu línea base.
- **Respawn limpia la base antes de cada prueba** pero trunca **SIN RESTART IDENTITY**.
- **Las pruebas siembran por diferencia contra `DateTime.UtcNow`, nunca con fechas fijas.** Una prueba que siembre «2026-05-20» empezará a fallar sola dentro de tres meses.
- **Verificación del front:** `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los tres con salida 0. No hay Vitest ni Playwright.
- **Objetivos táctiles de 44 px** en el front. La convención del repo es `min-h-[44px]`; **`min-h-12` no existe en este Tailwind y no aplicaría nada.**
- **Mutación obligatoria:** cada guarda nueva se comprueba quitándola, viendo la prueba en rojo y restaurándola. **Si una mutación no pone roja su prueba, para y avisa.**
- **Mensajes de commit en castellano**, terminados en `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Qué lleva el límite y qué no

**Esta tabla es normativa.** Si al implementar aparece un listado que no está en ella, **pregunta antes de decidir**.

| Listado | Método | ¿Límite? | Fecha que manda |
|---|---|---|---|
| Lotes | `RecepcionService.ListarLotesAsync` | **Sí** | `FechaRecepcion` |
| Pagos | `PagoService.ListarAsync` | **Sí** | `FechaPago` |
| Movilizaciones | `MovilizacionService.ListarAsync` | **Sí**, salvo las pendientes de recepción | `FechaDespacho` |
| Faenamientos | `FaenamientoService.ListarAsync` | **Sí** | `FechaFaenamiento` |
| Devoluciones | `FaenamientoService.ListarDevolucionesAsync` | **Sí** | `FechaDevolucion` |
| Despachos | `FaenamientoService.ListarDespachosAsync` | **Sí** | `FechaDespacho` |
| Tickets por pagar | `PagoService.ListarPorPagarAsync` | **NO** — cola de trabajo | — |
| Vinculaciones | `RecepcionService.ListarVinculacionesAsync` | **NO** — cola de trabajo | — |

**Por qué las colas quedan fuera.** «Pagos» en Faenamiento son los tickets que la planta **todavía no ha pagado**; Vinculaciones son entregas capturadas sin conexión esperando que un administrador las resuelva. Esconder lo que tiene más de 90 días significaría que **un ticket sin pagar de hace cien días desaparece para siempre en vez de cobrarse**. El trabajo pendiente no envejece: se resuelve.

Lo mismo con las movilizaciones **pendientes de recepción**: un camión que salió hace 91 días y cuya llegada nadie confirmó es un problema abierto, no historial.

**Los reportes no llevan el límite.** Ahí el rango lo elige quien consulta, y es el sitio donde se mira lo antiguo.

## File Structure

**API — se crean**

| Archivo | Responsabilidad |
|---|---|
| `tests/.../Unitarias/VentanaDeRetencionTests.cs` | El cálculo de la ventana |
| `tests/.../Integracion/RetencionPantallasTests.cs` | Los seis listados y las dos colas |

**API — se modifican**

| Archivo | Cambio |
|---|---|
| `Common/FechaUtc.cs` | + `DiasVisiblesEnPantalla`, + `DesdePorDefecto(...)` |
| `Features/Recepcion/Services/RecepcionService.cs` | Ventana por defecto en `ListarLotesAsync` |
| `Features/Pagos/Services/PagoService.cs` | Ventana por defecto en `ListarAsync` |
| `Features/Recepcion/Services/MovilizacionService.cs` | + `desde`/`hasta` y la ventana |
| `Features/Recepcion/Controllers/RecepcionController.cs` | Los parámetros nuevos de movilizaciones |
| `Features/Faenamiento/Services/FaenamientoService.cs` | Ventana en los tres listados |

**Front — se modifican**

| Archivo | Cambio |
|---|---|
| `src/api/recepcion.ts`, `src/api/pagos.ts`, `src/api/faenamiento.ts` | Parámetros de rango |
| `src/pages/Recepcion.tsx`, `src/pages/Faenamiento.tsx`, `src/pages/Despacho.tsx` | El filtro de rango |

---

## Fase 1 · La ventana

### Task 1: El cálculo, en un solo sitio

**Files:**
- Modify: `Common/FechaUtc.cs`
- Create: `tests/CoopagcuyApi.Tests/Unitarias/VentanaDeRetencionTests.cs`

**Interfaces:**
- Consumes: `FechaUtc.InicioDelDiaLocal`, ya existente.
- Produces:
  - `FechaUtc.DiasVisiblesEnPantalla` (`const int` = 90)
  - `FechaUtc.DesdePorDefecto(DateTime? desde, DateTime ahoraUtc)` → `DateTime`

- [ ] **Step 1: Crear la rama y anotar la línea base**

```bash
git checkout -b feat/retencion-90-dias origin/main
```

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Anota el número de pruebas.

- [ ] **Step 2: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Unitarias/VentanaDeRetencionTests.cs`:

```csharp
using CoopagcuyApi.Common;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Las pantallas operativas muestran por defecto los últimos 90 días, para
/// que no se llenen de historial. No se borra nada: quien necesite algo más
/// antiguo pide un rango explícito, y los reportes no llevan este límite.
/// </summary>
public class VentanaDeRetencionTests
{
    // 2026-08-23 a las 02:00 UTC son las 21:00 del 22 en el CAT.
    private static readonly DateTime AhoraUtc =
        new(2026, 8, 23, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SinRangoDevuelveLaVentanaDe90Dias()
    {
        var desde = FechaUtc.DesdePorDefecto(null, AhoraUtc);

        // 90 días atrás del día LOCAL, no del UTC.
        var esperado = FechaUtc.InicioDelDiaLocal(
            new DateTime(2026, 8, 22).AddDays(-FechaUtc.DiasVisiblesEnPantalla));

        desde.ShouldBe(esperado);
    }

    [Fact]
    public void ElCorteEsElDiaLocal_noElUtc()
    {
        // A las 02:00 UTC ya es día 23 en UTC pero todavía es 22 en el CAT.
        // Cortar por el día UTC desplazaría la frontera un día entero y
        // dejaría fuera registros que el operador considera de ayer.
        var desde = FechaUtc.DesdePorDefecto(null, AhoraUtc);

        var conDiaUtc = FechaUtc.InicioDelDiaLocal(
            new DateTime(2026, 8, 23).AddDays(-FechaUtc.DiasVisiblesEnPantalla));

        desde.ShouldNotBe(conDiaUtc);
    }

    [Fact]
    public void UnRangoExplicitoManda()
    {
        // La escotilla: quien pide un rango mayor lo obtiene entero.
        var pedido = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        FechaUtc.DesdePorDefecto(pedido, AhoraUtc).ShouldBe(pedido);
    }

    [Fact]
    public void UnRangoExplicitoMasEstrechoTambienManda()
    {
        // No es un mínimo de 90 días: es un valor por defecto.
        var pedido = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

        FechaUtc.DesdePorDefecto(pedido, AhoraUtc).ShouldBe(pedido);
    }

    [Fact]
    public void LaVentanaSonNoventaDias()
    {
        FechaUtc.DiasVisiblesEnPantalla.ShouldBe(90);
    }
}
```

- [ ] **Step 3: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~VentanaDeRetencionTests"
```

Esperado: error de compilación — `DesdePorDefecto` y `DiasVisiblesEnPantalla` no existen.

- [ ] **Step 4: Implementar**

Añadir a `Common/FechaUtc.cs`:

```csharp
    /// <summary>
    /// Días que las pantallas operativas muestran por defecto.
    ///
    /// No borra nada: es un valor por defecto, no un tope. Quien pida un
    /// rango explícito lo obtiene entero, y los reportes no lo aplican
    /// porque son justamente el sitio donde se consulta lo antiguo.
    /// </summary>
    public const int DiasVisiblesEnPantalla = 90;

    /// <summary>
    /// El "desde" efectivo de un listado de pantalla: el que pidió el
    /// cliente, o el inicio del día local de hace 90 días si no pidió nada.
    ///
    /// Se cuenta sobre el día LOCAL del piloto y no sobre el UTC: a las 02:00
    /// UTC ya es el día siguiente en UTC pero todavía es el anterior en el
    /// CAT, y cortar por el día equivocado desplaza la frontera un día
    /// entero. Es el mismo cuidado que InicioDelDiaLocal documenta para los
    /// filtros de reportes, y que allí corrigió un fallo real.
    ///
    /// `ahoraUtc` se recibe en vez de leerse de DateTime.UtcNow para que la
    /// ventana se pueda fijar por unidad sin depender del reloj.
    /// </summary>
    public static DateTime DesdePorDefecto(DateTime? desde, DateTime ahoraUtc)
    {
        if (desde is DateTime pedido) return pedido;

        var hoyLocal = (Normalizar(ahoraUtc) + DesfasePiloto).Date;
        return InicioDelDiaLocal(hoyLocal.AddDays(-DiasVisiblesEnPantalla));
    }
```

- [ ] **Step 5: Ejecutar y comprobar por mutación**

Esperado: `Passed: 5, Failed: 0`.

Mutación: sustituir `(Normalizar(ahoraUtc) + DesfasePiloto).Date` por `ahoraUtc.Date`. Esperado: fallan `SinRangoDevuelveLaVentanaDe90Dias` y `ElCorteEsElDiaLocal_noElUtc`. **Restaurar.**

- [ ] **Step 6: Commit**

```bash
git add Common/FechaUtc.cs tests/
git commit -m "feat: la ventana de 90 dias de las pantallas, en un solo sitio

Es un valor por defecto y no un tope: un rango explicito manda, y los
reportes no lo aplican. Se cuenta sobre el dia local del piloto, porque
cortar por el UTC desplaza la frontera un dia entero.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 2 · Los seis listados

### Task 2: Recepción — lotes y pagos

**Files:**
- Modify: `Features/Recepcion/Services/RecepcionService.cs` (`ListarLotesAsync`)
- Modify: `Features/Pagos/Services/PagoService.cs` (`ListarAsync`)
- Create: `tests/CoopagcuyApi.Tests/Integracion/RetencionPantallasTests.cs`

**Interfaces:**
- Consumes: `FechaUtc.DesdePorDefecto` (Tarea 1).
- Produces: nada nuevo. Las firmas no cambian: ambos métodos **ya reciben `desde`/`hasta`**.

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/RetencionPantallasTests.cs`. Empieza con estas cuatro; las tareas 3 y 4 añaden más al mismo archivo:

```csharp
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Las pantallas operativas muestran por defecto los últimos 90 días. Nada se
/// borra: quien necesite algo más antiguo pide un rango explícito.
///
/// Todo se siembra POR DIFERENCIA contra DateTime.UtcNow, nunca con fechas
/// fijas: una prueba con "2026-05-20" empezaría a fallar sola dentro de tres
/// meses.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class RetencionPantallasTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task UnLoteDeHace91DiasNoSaleEnElListado()
    {
        await SembrarLoteAsync("PAT-VIEJO-001", diasAtras: 91);
        await SembrarLoteAsync("PAT-NUEVO-001", diasAtras: 89);

        var cuerpo = await ListarAsync("/api/recepcion/lotes");

        cuerpo.ShouldContain("PAT-NUEVO-001");
        cuerpo.ShouldNotContain("PAT-VIEJO-001");
    }

    [Fact]
    public async Task ConUnRangoExplicitoElLoteViejoSiSale()
    {
        // La escotilla: el dato no se esconde del todo, solo deja de estorbar.
        await SembrarLoteAsync("PAT-VIEJO-001", diasAtras: 200);

        var desde = DateTime.UtcNow.AddDays(-365).ToString("O");
        var cuerpo = await ListarAsync($"/api/recepcion/lotes?desde={desde}");

        cuerpo.ShouldContain("PAT-VIEJO-001");
    }

    [Fact]
    public async Task UnPagoDeHace91DiasNoSaleEnElListado()
    {
        var (viejo, nuevo) = await SembrarDosPagosAsync();

        var cuerpo = await ListarAsync("/api/pagos");

        cuerpo.ShouldContain($"\"id\":{nuevo}");
        cuerpo.ShouldNotContain($"\"id\":{viejo}");
    }

    [Fact]
    public async Task ConUnRangoExplicitoElPagoViejoSiSale()
    {
        var (viejo, _) = await SembrarDosPagosAsync();

        var desde = DateTime.UtcNow.AddDays(-365).ToString("O");
        var cuerpo = await ListarAsync($"/api/pagos?desde={desde}");

        cuerpo.ShouldContain($"\"id\":{viejo}");
    }

    // ── Sembrado ──────────────────────────────────────────────────────

    private async Task<string> ListarAsync(string ruta)
    {
        var respuesta = await api.ComoOperadorCat("PAT").GetAsync(ruta);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsStringAsync();
    }

    /// Lote sembrado directo a la base con la antigüedad indicada. Sin filas
    /// en Cuyes: no hacen falta para este listado y así el sembrado es
    /// estable frente a cambios del detalle por animal.
    private async Task SembrarLoteAsync(string codigo, int diasAtras)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        db.Lotes.Add(new Lote
        {
            CodigoLote = codigo,
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 3,
            PesoTotalGramos = 3900,
            FechaRecepcion = DateTime.UtcNow.AddDays(-diasAtras),
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Operadora de prueba"
        });
        await db.SaveChangesAsync();
    }

    /// Dos pagos de la misma productora: uno de hace 91 días y otro de hace
    /// 89. Devuelve sus Id.
    private async Task<(int Viejo, int Nuevo)> SembrarDosPagosAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-PAGOS-001",
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 3,
            PesoTotalGramos = 3900,
            FechaRecepcion = DateTime.UtcNow.AddDays(-100),
            Estado = EstadoLote.Aceptado
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var viejo = new CoopagcuyApi.Features.Pagos.Models.Pago
        {
            ProductoraId = productora.Id,
            LoteId = lote.Id,
            MontoUsd = 50m,
            FechaPago = DateTime.UtcNow.AddDays(-91),
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        var nuevo = new CoopagcuyApi.Features.Pagos.Models.Pago
        {
            ProductoraId = productora.Id,
            LoteId = lote.Id,
            MontoUsd = 60m,
            FechaPago = DateTime.UtcNow.AddDays(-89),
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.AddRange(viejo, nuevo);
        await db.SaveChangesAsync();

        return (viejo.Id, nuevo.Id);
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan las dos de exclusión**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~RetencionPantallasTests"
```

Esperado: fallan `UnLoteDeHace91DiasNoSaleEnElListado` y `UnPagoDeHace91DiasNoSaleEnElListado`; las dos de la escotilla ya pasan, porque el filtro explícito ya funciona hoy.

**Antes de implementar, confirma las rutas reales** de los dos listados en sus controladores. Si no son `GET /api/recepcion/lotes` y `GET /api/pagos`, corrige **las URL de las pruebas**.

- [ ] **Step 3: Aplicar la ventana en los lotes**

En `RecepcionService.ListarLotesAsync`, sustituir el bloque:

```csharp
        if (desde.HasValue)
            query = query.Where(l => l.FechaRecepcion >= desde.Value.ToUniversalTime());
```

por:

```csharp
        // Sin rango explícito, la pantalla muestra los últimos 90 días. No se
        // borra nada: quien necesite lo antiguo lo pide, y los reportes no
        // aplican este límite.
        var desdeEfectivo = FechaUtc.DesdePorDefecto(desde, DateTime.UtcNow);
        query = query.Where(l => l.FechaRecepcion >= desdeEfectivo);
```

**Ojo:** el código original llamaba a `.ToUniversalTime()` sobre el valor del cliente. `DesdePorDefecto` devuelve el valor tal cual lo recibe, así que si el `desde` del cliente puede llegar sin zona, **normalízalo con `FechaUtc.Normalizar` antes de pasarlo**. Comprueba cómo llega en este endpoint y decide; deja escrito en el informe qué encontraste.

Añadir `using CoopagcuyApi.Common;` si no está.

- [ ] **Step 4: Aplicar la ventana en los pagos**

En `PagoService.ListarAsync`, sustituir:

```csharp
        if (desde.HasValue)
            query = query.Where(p =>
                p.FechaPago >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));
```

por:

```csharp
        var desdeEfectivo = FechaUtc.DesdePorDefecto(
            desde.HasValue ? DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc) : null,
            DateTime.UtcNow);
        query = query.Where(p => p.FechaPago >= desdeEfectivo);
```

- [ ] **Step 5: Ejecutar y comprobar por mutación**

Esperado: `Passed: 4, Failed: 0`.

Mutación: en `DesdePorDefecto`, devolver `DateTime.MinValue` cuando `desde` es nulo. Esperado: fallan las dos pruebas de exclusión y **no** las de la escotilla. **Restaurar.**

- [ ] **Step 6: Batería completa y commit**

Esperado: línea base + 9, 0 fallos.

```bash
git add Features/ tests/
git commit -m "feat: lotes y pagos muestran por defecto los ultimos 90 dias

Los dos listados ya aceptaban desde/hasta; lo que faltaba era el valor
por defecto. Un rango explicito sigue mandando y nada se borra.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Faenamiento — sesiones, devoluciones y despachos

**Files:**
- Modify: `Features/Faenamiento/Services/FaenamientoService.cs` (tres listados)
- Modify: `tests/CoopagcuyApi.Tests/Integracion/RetencionPantallasTests.cs`

**Interfaces:**
- Consumes: `FechaUtc.DesdePorDefecto` (Tarea 1).
- Produces: nada nuevo. Los tres métodos **ya reciben `desde`/`hasta`**.

- [ ] **Step 1: Añadir las pruebas**

Seis pruebas más en `RetencionPantallasTests.cs`, dos por listado. Esta es la
plantilla completa; las otras cinco salen de sustituir los valores de la tabla:

```csharp
    [Fact]
    public async Task UnFaenamientoDeHace91DiasNoSaleEnElListado()
    {
        var (viejo, nuevo) = await SembrarDosFaenamientosAsync();

        var cuerpo = await ListarComoPlantaAsync("/api/faenamiento");

        cuerpo.ShouldContain($"\"id\":{nuevo}");
        cuerpo.ShouldNotContain($"\"id\":{viejo}");
    }

    [Fact]
    public async Task ConUnRangoExplicitoElFaenamientoViejoSiSale()
    {
        var (viejo, _) = await SembrarDosFaenamientosAsync();

        var desde = DateTime.UtcNow.AddDays(-365).ToString("O");
        var cuerpo = await ListarComoPlantaAsync($"/api/faenamiento?desde={desde}");

        cuerpo.ShouldContain($"\"id\":{viejo}");
    }

    private async Task<string> ListarComoPlantaAsync(string ruta)
    {
        var respuesta = await api.ComoOperadorFaenamiento().GetAsync(ruta);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsStringAsync();
    }
```

| Listado | Nombres de las pruebas | Sembrador | Fecha que se mueve |
|---|---|---|---|
| Faenamientos | `UnFaenamientoDeHace91DiasNoSaleEnElListado` / `ConUnRangoExplicitoElFaenamientoViejoSiSale` | `SembrarDosFaenamientosAsync` | `RegistroFaenamiento.FechaFaenamiento` |
| Devoluciones | `UnaDevolucionDeHace91DiasNoSaleEnElListado` / `ConUnRangoExplicitoLaDevolucionViejaSiSale` | `SembrarDosDevolucionesAsync` | `Devolucion.FechaDevolucion` |
| Despachos | `UnDespachoDeHace91DiasNoSaleEnElListado` / `ConUnRangoExplicitoElDespachoViejoSiSale` | `SembrarDosDespachosAsync` | `Despacho.FechaDespacho` |

Los tres sembradores crean **dos filas**, una a `DateTime.UtcNow.AddDays(-91)` y
otra a `AddDays(-89)`, y devuelven sus Id. Siembran **directo a la base** y
**por diferencia contra `DateTime.UtcNow`**, nunca con fechas fijas.

Para los faenamientos y las devoluciones hace falta un `Lote` de apoyo, y para
los faenamientos además un `LoteFaenado`: copia ese montaje de
`DespachoExtremoAExtremoTests.SembrarDespachableAsync` en vez de inventar otro.
Para los despachos, `Sembrador.DespachoAsync(api, fechaDespacho)` ya existe y
recibe la fecha — úsalo.

**Confirma las rutas reales** de los tres listados en `FaenamientoController`
antes de escribir las URL.

- [ ] **Step 2: Ejecutar y ver que fallan las tres de exclusión**

- [ ] **Step 3: Aplicar la ventana en los tres**

En `ListarAsync`, `ListarDevolucionesAsync` y `ListarDespachosAsync`, sustituir cada bloque `if (desde.HasValue) …` por el mismo patrón de la Tarea 2, con la fecha que manda en cada uno:

| Método | Campo |
|---|---|
| `ListarAsync` | `f.FechaFaenamiento` |
| `ListarDevolucionesAsync` | `d.FechaDevolucion` |
| `ListarDespachosAsync` | `d.FechaDespacho` |

```csharp
        var desdeEfectivo = FechaUtc.DesdePorDefecto(
            desde.HasValue ? DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc) : null,
            DateTime.UtcNow);
        query = query.Where(f => f.FechaFaenamiento >= desdeEfectivo);
```

- [ ] **Step 4: Ejecutar, mutar y commitear**

Mutación: en cada uno por separado, volver al `if (desde.HasValue)` original. Esperado: falla **solo** la prueba de ese listado. Es lo que confirma que los tres son independientes y que ninguno se está apoyando en otro.

```bash
git add Features/ tests/
git commit -m "feat: los tres listados de la planta muestran 90 dias por defecto

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Movilizaciones, y las dos colas que NO llevan límite

**Files:**
- Modify: `Features/Recepcion/Services/MovilizacionService.cs`
- Modify: `Features/Recepcion/Controllers/RecepcionController.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/RetencionPantallasTests.cs`

**Interfaces:**
- Consumes: `FechaUtc.DesdePorDefecto` (Tarea 1).
- Produces: `IMovilizacionService.ListarAsync(bool? pendientesRecepcion, DateTime? desde, DateTime? hasta)` — **la firma cambia**, es el único listado que no recibía fechas.

- [ ] **Step 1: Añadir las pruebas**

Añadir a `RetencionPantallasTests.cs`:

```csharp
    [Fact]
    public async Task UnaMovilizacionDeHace91DiasNoSaleEnElListado()
    {
        var (vieja, nueva) = await SembrarDosMovilizacionesAsync(recibidas: true);

        var cuerpo = await ListarComoPlantaAsync("/api/recepcion/movilizaciones");

        cuerpo.ShouldContain($"\"id\":{nueva}");
        cuerpo.ShouldNotContain($"\"id\":{vieja}");
    }

    [Fact]
    public async Task UnaMovilizacionPendienteDeRecepcionNoSeEsconde()
    {
        // Un camión que salió hace 200 días y cuya llegada nadie confirmó es
        // un problema abierto, no historial. Esconderlo lo deja sin resolver
        // para siempre.
        var (vieja, _) = await SembrarDosMovilizacionesAsync(recibidas: false);

        var cuerpo = await ListarComoPlantaAsync(
            "/api/recepcion/movilizaciones?pendientes=true");

        cuerpo.ShouldContain($"\"id\":{vieja}");
    }

    [Fact]
    public async Task UnTicketPorPagarDeHace200DiasSigueApareciendo()
    {
        // La cola de trabajo de la planta NO lleva límite: esconder un ticket
        // sin pagar significa que no se cobra nunca.
        var pagoId = await SembrarTicketPendienteAsync(diasAtras: 200);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/pagos/por-pagar");
        respuesta.EnsureSuccessStatusCode();
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        cuerpo.ShouldContain($"\"id\":{pagoId}");
    }

    [Fact]
    public async Task UnaVinculacionDeHace200DiasSigueApareciendo()
    {
        // Misma razón: una entrega en cuarentena esperando a un administrador
        // no envejece, se resuelve.
        await SembrarVinculacionAsync(diasAtras: 200);

        var respuesta = await api.ComoAdmin().GetAsync("/api/recepcion/vinculaciones");
        respuesta.EnsureSuccessStatusCode();
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        cuerpo.ShouldContain("0104576277");
    }
```

Los cuatro sembradores se escriben con la misma técnica que los de la Tarea 2: directo a la base y **por diferencia contra `DateTime.UtcNow`**. Para el ticket pendiente, un `Pago` con `Estado = EstadoPago.Pendiente`; para la vinculación, una fila en `EntregasPendientesVinculacion` con `Estado = EstadoVinculacion.Pendiente` y su `CuyesJson`.

**Confirma las rutas y el nombre del parámetro de pendientes** (`pendientes`, `pendientesRecepcion`…) en `RecepcionController` antes de escribir las URL.

**Las dos últimas pruebas deben pasar desde el primer momento**, porque esas colas no se tocan. Están escritas para que **sigan** pasando el día que alguien decida «unificar» el criterio y aplicarles la ventana.

- [ ] **Step 2: Ejecutar**

Esperado: falla solo `UnaMovilizacionDeHace91DiasNoSaleEnElListado`. Las otras tres pasan.

- [ ] **Step 3: Ampliar la firma y aplicar la ventana**

En `IMovilizacionService` y `MovilizacionService`:

```csharp
    Task<IEnumerable<MovilizacionResponseDto>> ListarAsync(
        bool? pendientesRecepcion, DateTime? desde, DateTime? hasta);
```

Y en el cuerpo, después del filtro de pendientes:

```csharp
        // Las PENDIENTES de recepción no llevan ventana: un camión que salió
        // hace 200 días y cuya llegada nadie confirmó es un problema abierto,
        // no historial. Esconderlo lo deja sin resolver para siempre.
        if (pendientesRecepcion != true)
        {
            var desdeEfectivo = FechaUtc.DesdePorDefecto(
                desde.HasValue
                    ? DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc)
                    : null,
                DateTime.UtcNow);
            query = query.Where(m => m.FechaDespacho >= desdeEfectivo);
        }

        if (hasta.HasValue)
            query = query.Where(m =>
                m.FechaDespacho <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));
```

Y el controlador pasa los dos parámetros nuevos desde la query string.

- [ ] **Step 4: Ejecutar y comprobar por mutación**

Esperado: las cuatro en verde.

Mutaciones, restaurando después de cada una:
1. Quitar el `if (pendientesRecepcion != true)` y aplicar la ventana siempre → falla `UnaMovilizacionPendienteDeRecepcionNoSeEsconde`.
2. Aplicar `DesdePorDefecto` en `ListarPorPagarAsync` sobre `p.FechaPago` → falla `UnTicketPorPagarDeHace200DiasSigueApareciendo`. **Esta mutación es la más importante del plan**: es la que demuestra que la prueba protege de verdad la cola de trabajo.
3. Aplicar la ventana en `ListarVinculacionesAsync` sobre `FechaCreacion` → falla `UnaVinculacionDeHace200DiasSigueApareciendo`.

- [ ] **Step 5: Batería completa y commit**

Esperado: línea base + 19, 0 fallos.

```bash
git add Features/ tests/
git commit -m "feat: las movilizaciones llevan ventana; las colas de trabajo no

Un ticket sin pagar de hace cien dias o una entrega esperando vinculacion
no envejecen: se resuelven. Esconderlos significaria que no se resuelven
nunca. Lo mismo con un camion cuya llegada nadie confirmo.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 3 · El filtro en las pantallas

### Task 5: El rango en Recepción, Faenamiento y Despacho

**Files:**
- Modify: `src/api/recepcion.ts`, `src/api/pagos.ts`, `src/api/faenamiento.ts` (front)
- Modify: `src/pages/Recepcion.tsx`, `src/pages/Faenamiento.tsx`, `src/pages/Despacho.tsx` (front)

**Interfaces:**
- Consumes: los parámetros `desde`/`hasta` de los seis listados (Tareas 2-4).
- Produces: nada.

- [ ] **Step 1: Crear la rama del front**

```bash
git checkout -b feat/retencion-90-dias origin/main
```

- [ ] **Step 2: Los parámetros en el cliente**

Las funciones de listado de los tres archivos de API aceptan `{ desde?: string; hasta?: string }` y los pasan como query params. Sigue el patrón que ya usa `pagosApi.listar`, que **ya los acepta**.

- [ ] **Step 3: El filtro en las tres pantallas**

Cada pantalla gana un selector de rango sobre sus tablas, **reutilizando `FiltrosPeriodo`** —el componente que ya gobierna los reportes— en vez de inventar un segundo selector de fechas en el mismo sistema.

Estado inicial **vacío**, no «hace 90 días»: dejar los campos en blanco es lo que hace que el servidor aplique su ventana por defecto. Si el front mandara siempre un `desde`, la ventana del servidor no se ejercitaría nunca y el día que alguien llame al API sin parámetros se llevaría el historial entero.

Junto al filtro, una línea que explique el comportamiento por defecto: que se muestran los últimos 90 días y que ampliando el rango se ve más atrás.

**No** se añade filtro a las pestañas de **Vinculaciones** ni a la de **Pagos de Faenamiento**: son colas de trabajo y no llevan ventana.

**Objetivos táctiles de 44 px**: `min-h-[44px]`. **`min-h-12` no existe en este Tailwind.**

- [ ] **Step 4: Verificar**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

- [ ] **Step 5: Comprobación manual**

Con el API corriendo y datos de más de 90 días en la base:
1. Cada pantalla afectada abre mostrando solo lo reciente.
2. Ampliando el rango aparece lo antiguo.
3. **Vinculaciones y los tickets por pagar muestran todo**, sin filtro.
4. Los **reportes** siguen devolviendo el rango que se les pida, sin límite.

- [ ] **Step 6: Commit**

```bash
git add src/
git commit -m "feat: las pantallas operativas abren en 90 dias y dejan ampliar

El rango vacio es deliberado: es lo que hace que el servidor aplique su
ventana por defecto. Las colas de trabajo no llevan filtro.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Cierre

- [ ] **Batería completa del API en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: línea base + 19, con 0 fallos.

- [ ] **Front verificado**

```bash
pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Verificación manual obligatoria**

La que más importa es la tercera: **es la que distingue una limpieza de una pérdida de trabajo.**

1. Cada pantalla afectada abre mostrando solo los últimos 90 días.
2. Ampliando el rango, lo antiguo aparece.
3. **Un ticket sin pagar y una vinculación pendiente de hace más de 90 días siguen visibles**, sin necesidad de tocar ningún filtro.
4. Los reportes devuelven lo que se les pida, sin límite.

- [ ] **Abrir los dos PR**, el del API primero: el front pasa parámetros que solo existen ahí.

## Lo que este plan deja fuera a propósito

- **Borrar datos.** Nada se elimina de la base.
- **Archivar a una tabla fría.** El volumen del piloto no lo justifica.
- **Un límite distinto por pantalla o por usuario.** 90 días para todas.
- **Aplicar el corte a los reportes.** Son el sitio donde se consulta lo antiguo.

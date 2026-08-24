# Reportes de ganancias — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Registrar el precio de venta en el despacho y publicar un reporte con **dos cifras que nunca se suman**: lo que ganaron las productoras y el margen de la reventa.

**Architecture:** El despacho gana un precio unitario y el total se deriva, nunca se guarda. El costo de lo vendido no se estima: `DespachoCuy` enlaza cada animal despachado con su registro de faenamiento, y de ahí se llega a la jaula, a la productora y a su pago. Lo que no se sabe —despachos sin precio, animales cuya productora no ha cobrado— se declara aparte en vez de contarse como cero.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, ClosedXML, xUnit + Shouldly + Respawn, React 19 + TypeScript + Vite + Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-23-reportes-de-ganancias-design.md`

## Global Constraints

- **Rama del API y del front:** crear `feat/reportes-ganancias` desde **`feat/venta-local`**, no desde `origin/main`. **Este proyecto depende del Proyecto B**, que aún no está fusionado: sin él no existe `Pago.EsVentaLocal` ni `CuyRegistro.VentaLocalPagoId`, y las dos son imprescindibles aquí. Rama apilada: **el PR de C no se puede fusionar hasta que entren los de A y B**.
- **Nada de `dotnet test` ni `dotnet ef` directamente en Windows.** Smart App Control bloquea la carga del DLL desde OneDrive (`0x800711C7`).
  - Batería completa: `docker compose -f docker-compose.tests.yml run --rm tests`
  - Una clase: `docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"`
  - Puede tardar varios minutos; usa un timeout amplio.
- **Punto de partida: la batería de `feat/venta-local` en verde.** Ejecútala antes de tocar nada y anota el número: es tu línea base.
- **Toda columna nueva NO anulable sobre una tabla con datos necesita `HasDefaultValue` en el `modelBuilder`**, no solo el inicializador de C#. `PrecioUnitarioUsd` es anulable, así que no aplica.
- **Respawn limpia la base antes de cada prueba** pero trunca **SIN RESTART IDENTITY**.
- **Los días del filtro son LOCALES del piloto, no UTC.** `ReportesService.RangoUtc(filtro)` ya lo resuelve y **hay que usarlo**: tomarlos como UTC recortaba de todos los reportes las últimas cinco horas de cada día local, y ese fue el fallo que se reportó como «los despachos nuevos no aparecen en Salida».
- **Verificación del front:** `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los tres con salida 0. No hay Vitest ni Playwright.
- **Objetivos táctiles de 44 px** en el front. La convención del repo es `min-h-[44px]`; **`min-h-12` no existe en este Tailwind y no aplicaría nada.**
- **Mutación obligatoria:** cada guarda nueva se comprueba quitándola, viendo la prueba en rojo y restaurándola. En los proyectos A y B ese paso encontró trece problemas, siete de ellos suposiciones falsas del propio plan. **Si una mutación no pone roja su prueba, para y avisa.**
- **Mensajes de commit en castellano**, terminados en `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## La regla que gobierna todo el reporte

**Las dos cifras nunca se suman.** Un pago a una productora es **ingreso para ella y costo para la cooperativa**: la misma fila leída desde dos lados. Van en bloques separados, con rótulos distintos, y no hay ninguna columna que las combine.

**Y lo que no se sabe se declara.** Un despacho sin precio no se vendió gratis; un animal cuya productora no ha cobrado no costó cero. Los dos se cuentan aparte y se muestran junto a la cifra. Un margen que los ignorase sería **optimista justo cuando más falta pagar**.

## File Structure

**API — se crean**

| Archivo | Responsabilidad |
|---|---|
| `Features/Reportes/Services/CostoDeLoDespachado.cs` | Atribución del costo animal por animal |
| `tests/.../Unitarias/CostoDeLoDespachadoTests.cs` | El prorrateo y sus casos |
| `tests/.../Integracion/PrecioDeVentaTests.cs` | El precio en el despacho |
| `tests/.../Integracion/ReporteGananciasTests.cs` | Las dos cifras y sus advertencias |

**API — se modifican**

| Archivo | Cambio |
|---|---|
| `Features/Faenamiento/Models/Despacho.cs` | + `PrecioUnitarioUsd` |
| `Infrastructure/Data/AppDbContext.cs` | Precisión de la columna |
| `Features/Faenamiento/DTOs/FaenamientoDtos.cs` | El precio en el DTO de registro y en el de respuesta |
| `Features/Faenamiento/Services/FaenamientoService.cs` | Exigir precio en los despachos nuevos |
| `Features/Reportes/DTOs/ReportesDtos.cs` | Los DTOs del reporte |
| `Features/Reportes/Services/ReportesService.cs` | Las cuatro vistas y el Excel |
| `Features/Reportes/Controllers/ReportesController.cs` | Los endpoints |

**Front — se modifican**

| Archivo | Cambio |
|---|---|
| `src/types/faenamiento.ts` | El precio |
| `src/api/reportes.ts` | Las llamadas nuevas |
| `src/pages/Despacho.tsx` | El campo de precio |
| `src/pages/Reportes.tsx` | La pestaña |

---

## Fase 1 · El precio de venta

### Task 1: La columna y la obligatoriedad

**Files:**
- Modify: `Features/Faenamiento/Models/Despacho.cs`
- Modify: `Infrastructure/Data/AppDbContext.cs`
- Modify: `Features/Faenamiento/DTOs/FaenamientoDtos.cs`
- Modify: `Features/Faenamiento/Services/FaenamientoService.cs`
- Create: `tests/CoopagcuyApi.Tests/Integracion/PrecioDeVentaTests.cs`
- Create: la migración

**Interfaces:**
- Produces:
  - `Despacho.PrecioUnitarioUsd` (`decimal?`)
  - `RegistrarDespachoDto.PrecioUnitarioUsd` (`decimal?`)
  - `DespachoResponseDto` gana `PrecioUnitarioUsd` y `TotalVentaUsd` (derivado)

- [ ] **Step 1: Crear la rama y anotar la línea base**

```bash
git checkout -b feat/reportes-ganancias feat/venta-local
```

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Anota el número: es tu línea base.

- [ ] **Step 2: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/PrecioDeVentaTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El despacho registra a qué precio se vendió, porque sin eso el sistema no
/// puede decir nada del margen: hasta ahora solo sabía lo que pagaba a las
/// productoras, que es la mitad de la resta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class PrecioDeVentaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnDespachoSinPrecioSeRechaza()
    {
        var (loteFaenadoId, cuyIds) = await SembrarDespachableAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", CuerpoDespacho(
                loteFaenadoId, cuyIds, precio: null));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = api.NuevoDbContext();
        (await db.Despachos.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task UnPrecioNoPositivoSeRechaza()
    {
        var (loteFaenadoId, cuyIds) = await SembrarDespachableAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", CuerpoDespacho(
                loteFaenadoId, cuyIds, precio: 0m));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConPrecioSeGuardaYElTotalSeDeriva()
    {
        var (loteFaenadoId, cuyIds) = await SembrarDespachableAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync("/api/faenamiento/despachos", CuerpoDespacho(
                loteFaenadoId, cuyIds, precio: 8.50m));

        respuesta.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        var despacho = await db.Despachos.AsNoTracking().FirstAsync();

        despacho.PrecioUnitarioUsd.ShouldBe(8.50m);

        // El total NO se guarda: se deriva. Guardarlo abriría la puerta a que
        // las dos cifras se contradigan, que es el defecto que este sistema
        // ya sufrió con MontoPagadoUsd y sus descuentos.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("17.00");   // 8.50 x 2 animales
    }

    private static object CuerpoDespacho(
        int loteFaenadoId, List<int> cuyIds, decimal? precio) => new
    {
        loteFaenadoId,
        cuyFaenamientoIds = cuyIds,
        clienteDestino = "Mercado Central",
        fechaDespacho = DateTime.UtcNow,
        responsable = "Operador de prueba",
        tipoMercado = "Local",
        precioUnitarioUsd = precio
    };

    /// Lote faenado con dos animales listos para despachar.
    ///
    /// El cuerpo de este método se copia TAL CUAL de
    /// DespachoExtremoAExtremoTests.SembrarDespachableAsync, que ya monta la
    /// cadena Lote -> LoteFaenado -> RegistroFaenamiento -> dos
    /// CuyFaenamiento y devuelve exactamente esta tupla. No lo reinventes:
    /// ese montaje ya está probado y cualquier variación tuya sería una
    /// diferencia silenciosa entre dos pruebas que deberían sembrar igual.
    private async Task<(int LoteFaenadoId, List<int> CuyIds)> SembrarDespachableAsync()
    {
        // ← pegar aquí el cuerpo de DespachoExtremoAExtremoTests.SembrarDespachableAsync
    }
}
```

**El primer paso de este Step es copiar ese cuerpo**, antes de ejecutar nada:
abre `tests/CoopagcuyApi.Tests/Integracion/DespachoExtremoAExtremoTests.cs`,
localiza `SembrarDespachableAsync` y pega su cuerpo literal. Si prefieres subirlo
a `Sembrador` para compartirlo entre las dos clases, hazlo en un commit aparte y
comprueba que `DespachoExtremoAExtremoTests` sigue en verde.

- [ ] **Step 3: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~PrecioDeVentaTests"
```

Esperado: las tres fallan.

- [ ] **Step 4: La columna**

En `Features/Faenamiento/Models/Despacho.cs`, junto a `CantidadUnidades`:

```csharp
    // Precio por animal al que se vendió este despacho.
    //
    // Anulable en el esquema por los despachos anteriores a este cambio, pero
    // OBLIGATORIO en el servicio para los nuevos: mismo criterio que se
    // aplicó a Pago.LoteId cuando el ticket pasó a exigir lote.
    //
    // El TOTAL no se guarda: se deriva de precio x cantidad. Guardarlo
    // abriría la puerta a que las dos cifras se contradigan, que es el
    // defecto que este sistema ya sufrió con MontoPagadoUsd.
    public decimal? PrecioUnitarioUsd { get; set; }
```

En `AppDbContext`, dentro del bloque de `Despacho`:

```csharp
            e.Property(d => d.PrecioUnitarioUsd).HasPrecision(10, 2);
```

- [ ] **Step 5: Los DTOs y la validación**

Añadir `public decimal? PrecioUnitarioUsd { get; set; }` a `RegistrarDespachoDto`.

Añadir a `DespachoResponseDto` los campos `decimal? PrecioUnitarioUsd` y
`decimal? TotalVentaUsd`, y en el mapeo:

```csharp
        // Derivado, nunca almacenado.
        TotalVentaUsd: d.PrecioUnitarioUsd * d.CantidadUnidades,
```

En `FaenamientoService.RegistrarDespachoAsync`, antes de escribir:

```csharp
        // Obligatorio en los despachos nuevos. Sin precio, el reporte de
        // margen tendría un hueco que nadie recuerda llenar después.
        if (dto.PrecioUnitarioUsd is not > 0)
            throw new CuerpoInvalidoException(
                "El precio unitario de venta es obligatorio y debe ser mayor a cero.");
```

Y asignarlo al construir el `Despacho`.

**Comprueba** que el controlador traduce `CuerpoInvalidoException` a 400 en esa acción; si no, añade el `catch` como hacen los demás endpoints.

- [ ] **Step 6: Generar la migración**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "/c/Users/nicol/OneDrive/Documents/CoopagcuyApi:/src" -w /src -e ConnectionStrings__NeonDb="Host=localhost;Database=placeholder;Username=postgres;Password=postgres" mcr.microsoft.com/dotnet/sdk:8.0 bash -c "dotnet tool install --global dotnet-ef --version 8.* >/dev/null 2>&1; export PATH=\$PATH:/root/.dotnet/tools && dotnet ef migrations add PrecioDeVenta --project CoopagcuyApi.csproj"
```

Abrir el `.cs` generado y comprobar que la columna es `nullable: true`, con precisión 10,2, y que no toca nada más.

**Si algo está mal, no la edites a mano:** borra los dos archivos generados, restaura el snapshot con `git checkout -- Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`, corrige el `modelBuilder` y repite.

- [ ] **Step 7: Ejecutar y comprobar por mutación**

Esperado: `Passed: 3, Failed: 0`.

Mutación: quitar el `if (dto.PrecioUnitarioUsd is not > 0)`. Esperado: fallan `UnDespachoSinPrecioSeRechaza` y `UnPrecioNoPositivoSeRechaza`. **Restaurar.**

- [ ] **Step 8: Batería completa y commit**

**Alguna prueba existente puede romperse:** las que registran un despacho por HTTP sin precio. Si pasa, **amplía su cuerpo con un precio**, no relajes la validación. Deja escrito en el informe cuáles tocaste.

```bash
git add Features/ Infrastructure/ tests/
git commit -m "feat: el despacho registra a que precio se vendio

Sin esto el sistema solo sabia lo que pagaba a las productoras, que es la
mitad de la resta. El total no se guarda: se deriva de precio por
cantidad, para que las dos cifras no puedan contradecirse.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: El campo en la pantalla de despacho

**Files:**
- Modify: `src/types/faenamiento.ts` (front)
- Modify: `src/pages/Despacho.tsx` (front)

**Interfaces:**
- Consumes: `precioUnitarioUsd` en el cuerpo de registro (Tarea 1).
- Produces: nada.

- [ ] **Step 1: Crear la rama del front**

```bash
git checkout -b feat/reportes-ganancias feat/venta-local
```

- [ ] **Step 2: Los tipos**

Añadir `precioUnitarioUsd: number | null;` y `totalVentaUsd: number | null;` al tipo de respuesta de despacho, y `precioUnitarioUsd: number;` al de la petición de registro.

- [ ] **Step 3: El campo**

El formulario de despacho gana un campo numérico de precio unitario, **con el total calculado a la vista** mientras se escribe:

```tsx
                        {precioUnitario > 0 && (
                            <p className="mt-1 text-sm font-bold text-primary-700">
                                Total: USD {(precioUnitario * seleccionados.length).toFixed(2)}
                            </p>
                        )}
```

Ese total en pantalla no es decoración: es lo que permite que la operadora detecte un dedazo de un cero **antes** de confirmar, cuando todavía se puede corregir.

El botón de guardar se deshabilita si el precio no es mayor que cero, porque el servidor lo rechazaría con 400 y es un error evitable.

**Objetivos táctiles de 44 px**: `min-h-[44px]`. **`min-h-12` no existe en este Tailwind.**

Adapta los nombres de las variables a los que ya use ese archivo; míralo antes de escribir.

- [ ] **Step 4: Verificar y commitear**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

```bash
git add src/
git commit -m "feat: el despacho pide el precio unitario y muestra el total

El total a la vista deja detectar un dedazo de un cero antes de
confirmar, que es cuando todavia se puede corregir.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 2 · Lo que ganaron las productoras

### Task 3: Las tres vistas de pagos

**Files:**
- Modify: `Features/Reportes/DTOs/ReportesDtos.cs`
- Modify: `Features/Reportes/Services/ReportesService.cs`
- Modify: `Features/Reportes/Controllers/ReportesController.cs`
- Create: `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs`

**Interfaces:**
- Consumes: `ReportesService.RangoUtc(filtro)`, `Pago.EsVentaLocal`, `Pago.MontoPagadoUsd`.
- Produces:
  - `GananciaProductoraDto(int ProductoraId, string NombreProductora, string Comunidad, string CentroAcopio, decimal CobradoLocal, decimal PactadoCuotas, decimal PagadoPlanta, int TotalPagos)`
  - `GananciaCatDto(string CentroAcopio, decimal CobradoLocal, decimal PactadoCuotas, decimal PagadoPlanta, int TotalPagos)`
  - `GananciaMesDto(int Anio, int Mes, decimal CobradoLocal, decimal PactadoCuotas, decimal PagadoPlanta, int TotalPagos)`
  - `IReportesService.GananciasPorProductoraAsync/PorCatAsync/PorMesAsync(FiltroPeriodoDto)`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs` con estas cuatro. El sembrador crea, para una misma productora de PAT y dentro del período: una venta local en efectivo de 40, una venta local a cuotas de 30, y un pago de planta con `MontoUsd = 100` y `MontoPagadoUsd = 85` (15 de descuento). Más un pago **pendiente** de 200 que no debe contar.

```csharp
    [Fact]
    public async Task LasCuotasNoSeSumanConLoCobrado()
    {
        // Obligación que el Proyecto B le dejó a este: una CAT con muchas
        // ventas a plazo veria ganancias que todavia no tiene en caja.
        await SembrarPagosAsync();

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("cobradoLocal").GetDecimal().ShouldBe(40m);
        fila.GetProperty("pactadoCuotas").GetDecimal().ShouldBe(30m);
    }

    [Fact]
    public async Task SeSumaLoRealmentePagado_noElMontoDelTicket()
    {
        // La diferencia son los descuentos por novedades: contarlos como
        // pagados inflaría la cifra justo donde el sistema ya sabe que no lo
        // fueron.
        await SembrarPagosAsync();

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("pagadoPlanta").GetDecimal().ShouldBe(85m);
    }

    [Fact]
    public async Task UnTicketPendienteNoCuenta()
    {
        // Es un ticket emitido que la planta todavía no ha transferido. No es
        // dinero movido.
        await SembrarPagosAsync();

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("totalPagos").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task UnPagoDeLasVeinteHorasCaeEnSuPropioMes()
    {
        // Los días del filtro son LOCALES. Agrupar por UTC mandaría un pago
        // del último día del mes al mes siguiente: es el mismo fallo que se
        // reportó como "los despachos nuevos no aparecen en Salida".
        await SembrarPagoDeFinDeMesAsync();

        var meses = await PorMesAsync();

        // El pago se sembró a las 02:00 UTC del día 1, que son las 21:00 del
        // último día del mes anterior en el CAT.
        meses.Length.ShouldBe(1);
        meses[0].GetProperty("mes").GetInt32().ShouldBe(MesAnterior());
    }
```

Los sembradores escriben los `Pago` **directo a la base** —es más estable que montar todo el flujo— y las fechas van **por diferencia contra `DateTime.UtcNow`**, salvo la de fin de mes, que se construye explícitamente para ejercitar la frontera.

- [ ] **Step 2: Ejecutar y ver que fallan**

Esperado: 404, los endpoints no existen.

- [ ] **Step 3: Los DTOs**

Añadir a `Features/Reportes/DTOs/ReportesDtos.cs` los tres records de la sección **Interfaces**, cada uno con un comentario que diga **por qué las tres columnas van separadas**: cobrado es dinero en mano, pactado es dinero que no ha llegado, y lo de planta es la otra vía.

- [ ] **Step 4: El servicio**

Las tres vistas comparten la misma consulta base, así que va en un método privado:

```csharp
    // Pagos que cuentan como dinero movido: los pendientes son tickets que la
    // planta todavía no ha transferido, y no son dinero movido.
    //
    // Se suma MontoPagadoUsd y no MontoUsd: la diferencia son los descuentos
    // por novedades, y contarlos como pagados inflaría la cifra justo donde
    // el sistema ya sabe que no lo fueron. En las ventas locales los dos
    // valores coinciden —el servicio los iguala al registrar— así que la
    // regla es uniforme.
    private IQueryable<Pago> PagosDelPeriodo(FiltroPeriodoDto filtro)
    {
        var (desde, hasta) = RangoUtc(filtro);
        return db.Pagos
            .Where(p => p.FechaPago >= desde && p.FechaPago < hasta
                && p.Estado != EstadoPago.Pendiente);
    }
```

Y cada vista agrupa. Las tres columnas salen del mismo patrón:

```csharp
                CobradoLocal = g.Where(p => p.EsVentaLocal && p.MetodoPago != "Cuotas")
                                .Sum(p => p.MontoPagadoUsd ?? 0),
                PactadoCuotas = g.Where(p => p.EsVentaLocal && p.MetodoPago == "Cuotas")
                                 .Sum(p => p.MontoPagadoUsd ?? 0),
                PagadoPlanta = g.Where(p => !p.EsVentaLocal)
                                .Sum(p => p.MontoPagadoUsd ?? 0),
```

**Para la vista por mes**, el agrupamiento se hace sobre la fecha **local**:

```csharp
        // El mes se agrupa por el día local del piloto, no por el UTC: un
        // pago de las 20:00 del 31 de agosto pertenece a agosto, y agrupar
        // por UTC lo mandaría a septiembre.
        var local = FechaUtc.ALocal(p.FechaPago);
```

Como `ALocal` no se traduce a SQL, esta vista **materializa antes de agrupar**. El volumen del piloto lo permite de sobra; déjalo escrito en un comentario para que nadie lo «optimice» de vuelta a un `GroupBy` en base que rompería la frontera del mes.

- [ ] **Step 5: Los endpoints**

Tres acciones en `ReportesController`, con:

```csharp
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
```

**Es más de lo que pedía la petición original**, que nombraba solo a los dos administradores; el spec documenta la ampliación. El `OperadorCAT` **no** entra.

- [ ] **Step 6: Ejecutar y comprobar por mutación**

Esperado: `Passed: 4, Failed: 0`.

Mutaciones, restaurando después de cada una:
1. Sumar `PactadoCuotas` dentro de `CobradoLocal` → falla `LasCuotasNoSeSumanConLoCobrado`.
2. Cambiar `MontoPagadoUsd` por `MontoUsd` → falla `SeSumaLoRealmentePagado_noElMontoDelTicket`.
3. Quitar `p.Estado != EstadoPago.Pendiente` → falla `UnTicketPendienteNoCuenta`.
4. Agrupar por `p.FechaPago.Month` en vez de por la fecha local → falla `UnPagoDeLasVeinteHorasCaeEnSuPropioMes`.

- [ ] **Step 7: Batería completa y commit**

```bash
git add Features/ tests/
git commit -m "feat: el reporte dice cuanto cobraron las productoras

Tres columnas que no se suman entre si: lo cobrado en venta local, lo
pactado a cuotas —que todavia no ha llegado— y lo pagado por la planta.
Se suma MontoPagadoUsd y no MontoUsd: la diferencia son los descuentos.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 3 · El margen

### Task 4: La atribución del costo

**Files:**
- Create: `Features/Reportes/Services/CostoDeLoDespachado.cs`
- Create: `tests/CoopagcuyApi.Tests/Unitarias/CostoDeLoDespachadoTests.cs`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces:
  - `record AnimalDespachado(int LoteId, int NumeroEnLote, int? ProductoraId)`
  - `record PagoDeLote(int LoteId, int ProductoraId, decimal MontoPagado, int AnimalesCubiertos)`
  - `record CostoAtribuido(decimal Total, int AnimalesSinCosto)`
  - `CostoDeLoDespachado.Calcular(IReadOnlyList<AnimalDespachado>, IReadOnlyList<PagoDeLote>)` → `CostoAtribuido`

**Por qué es una función pura y no una consulta.** El cálculo tiene tres reglas
que conviene poder fijar sin montar media base de datos, y una de ellas —el
denominador— es la que más fácil se hace mal. La consulta que alimenta a esta
función es la Tarea 5.

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Unitarias/CostoDeLoDespachadoTests.cs`:

```csharp
using CoopagcuyApi.Features.Reportes.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El costo de lo vendido no se estima: se rastrea animal por animal hasta el
/// pago de su productora, y se reparte entre los animales que ese pago cubrió.
///
/// Lo que no se puede saber —un animal cuya productora todavía no ha
/// cobrado— vale DESCONOCIDO, no cero. Un margen calculado ignorando eso
/// sería optimista justo cuando más falta pagar.
/// </summary>
public class CostoDeLoDespachadoTests
{
    [Fact]
    public void ReparteElPagoEntreLosAnimalesQueCubrio()
    {
        // Una productora cobró 120 por 12 cuyes; se despacharon 3.
        var animales = new[]
        {
            new AnimalDespachado(LoteId: 1, NumeroEnLote: 1, ProductoraId: 7),
            new AnimalDespachado(1, 2, 7),
            new AnimalDespachado(1, 3, 7),
        };
        var pagos = new[] { new PagoDeLote(1, 7, 120m, AnimalesCubiertos: 12) };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(30m);
        costo.AnimalesSinCosto.ShouldBe(0);
    }

    [Fact]
    public void UnAnimalSinPagoNoValeCero()
    {
        // Su productora todavía no ha cobrado ese lote. El reporte lo declara,
        // no lo rellena.
        var animales = new[]
        {
            new AnimalDespachado(1, 1, 7),
            new AnimalDespachado(2, 1, 9),   // sin pago
        };
        var pagos = new[] { new PagoDeLote(1, 7, 100m, 10) };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(10m);
        costo.AnimalesSinCosto.ShouldBe(1);
    }

    [Fact]
    public void UnPagoQueNoCubrioAnimalesNoDivideEntreCero()
    {
        var animales = new[] { new AnimalDespachado(1, 1, 7) };
        var pagos = new[] { new PagoDeLote(1, 7, 100m, AnimalesCubiertos: 0) };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(0m);
        costo.AnimalesSinCosto.ShouldBe(1);
    }

    [Fact]
    public void UnAnimalSinProductoraSeCuentaComoSinCosto()
    {
        // Jaula antigua sin detalle por animal: no se sabe de quién era.
        var animales = new[] { new AnimalDespachado(1, 1, ProductoraId: null) };

        var costo = CostoDeLoDespachado.Calcular(animales, []);

        costo.Total.ShouldBe(0m);
        costo.AnimalesSinCosto.ShouldBe(1);
    }

    [Fact]
    public void CadaAnimalSeAtribuyeAlPagoDeSuPropiaProductora()
    {
        // Jaula compartida: dos productoras en el mismo lote, con pagos
        // distintos. Atribuir todo al primero sería el error fácil.
        var animales = new[]
        {
            new AnimalDespachado(1, 1, 7),
            new AnimalDespachado(1, 9, 8),
        };
        var pagos = new[]
        {
            new PagoDeLote(1, 7, 100m, 10),   // 10 por animal
            new PagoDeLote(1, 8, 40m, 4),     // 10 por animal… pero de otra
        };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(20m);
        costo.AnimalesSinCosto.ShouldBe(0);
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

Esperado: error de compilación.

- [ ] **Step 3: Implementar**

Crear `Features/Reportes/Services/CostoDeLoDespachado.cs`:

```csharp
namespace CoopagcuyApi.Features.Reportes.Services;

/// Un animal que salió en un despacho del período.
public record AnimalDespachado(int LoteId, int NumeroEnLote, int? ProductoraId);

/// <summary>
/// Lo que una productora cobró por un lote, y entre cuántos animales se
/// reparte.
///
/// `AnimalesCubiertos` NO incluye los vendidos en la comunidad: esos nunca
/// llegaron a la planta y su pago fue otro. Es además el mismo conteo que la
/// operadora vio al crear el pago.
/// </summary>
public record PagoDeLote(
    int LoteId, int ProductoraId, decimal MontoPagado, int AnimalesCubiertos);

/// El costo atribuido, y cuántos animales quedaron sin poder atribuirse.
public record CostoAtribuido(decimal Total, int AnimalesSinCosto);

/// <summary>
/// Reparte el costo de los animales despachados a partir de los pagos de sus
/// productoras.
///
/// Es un prorrateo: un pago es una cifra global por los animales de una
/// productora en una jaula, y repartirla a partes iguales asume que todos
/// valían lo mismo. Es la única atribución posible con los datos que hay, y
/// por eso el reporte no da margen por despacho individual — a esa escala el
/// redondeo pesa más que la señal.
/// </summary>
public static class CostoDeLoDespachado
{
    public static CostoAtribuido Calcular(
        IReadOnlyList<AnimalDespachado> animales,
        IReadOnlyList<PagoDeLote> pagos)
    {
        var porClave = pagos.ToDictionary(p => (p.LoteId, p.ProductoraId));

        decimal total = 0m;
        var sinCosto = 0;

        foreach (var animal in animales)
        {
            // Sin productora no hay a quién atribuirlo: jaula antigua sin
            // detalle por animal.
            if (animal.ProductoraId is not int productoraId)
            {
                sinCosto++;
                continue;
            }

            // Su productora todavía no ha cobrado este lote. Vale
            // DESCONOCIDO, no cero.
            if (!porClave.TryGetValue((animal.LoteId, productoraId), out var pago)
                || pago.AnimalesCubiertos <= 0)
            {
                sinCosto++;
                continue;
            }

            total += pago.MontoPagado / pago.AnimalesCubiertos;
        }

        return new CostoAtribuido(Math.Round(total, 2), sinCosto);
    }
}
```

- [ ] **Step 4: Ejecutar y comprobar por mutación**

Esperado: `Passed: 5, Failed: 0`.

Mutaciones, restaurando después de cada una:
1. Sustituir el `sinCosto++` del pago ausente por `continue` sin contar → falla `UnAnimalSinPagoNoValeCero`.
2. Quitar el `|| pago.AnimalesCubiertos <= 0` → falla `UnPagoQueNoCubrioAnimalesNoDivideEntreCero` con una excepción de división.
3. Buscar el pago solo por `LoteId`, ignorando la productora → falla `CadaAnimalSeAtribuyeAlPagoDeSuPropiaProductora`.

- [ ] **Step 5: Commit**

```bash
git add Features/Reportes/Services/CostoDeLoDespachado.cs tests/
git commit -m "feat: la atribucion del costo de lo despachado, como funcion pura

Reparte el pago de cada productora entre los animales que cubrio. Lo que
no se puede saber vale DESCONOCIDO y se cuenta aparte: un margen que lo
tratara como cero seria optimista justo cuando mas falta pagar.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Las dos vistas de margen

**Files:**
- Modify: `Features/Reportes/DTOs/ReportesDtos.cs`
- Modify: `Features/Reportes/Services/ReportesService.cs`
- Modify: `Features/Reportes/Controllers/ReportesController.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs`

**Interfaces:**
- Consumes: `CostoDeLoDespachado.Calcular` (Tarea 4), `Despacho.PrecioUnitarioUsd` (Tarea 1).
- Produces:
  - `MargenDto(string Agrupacion, decimal Ingreso, decimal CostoAtribuido, decimal Margen, int DespachosSinPrecio, int AnimalesSinCosto)`
  - `IReportesService.MargenPorMesAsync/PorClienteAsync(FiltroPeriodoDto)`

- [ ] **Step 1: Añadir las pruebas**

Cuatro pruebas más en `ReporteGananciasTests.cs`:

- **El ingreso sale de precio × cantidad.** Un despacho de 2 animales a 8.50 da 17.
- **Un despacho sin precio no baja el ingreso**: se cuenta en `despachosSinPrecio` y no como cero.
- **El costo sale del pago de la productora**: 3 animales de una que cobró 120 por 12 cuestan 30, y el margen es ingreso − 30.
- **Con 2 de esos 12 vendidos localmente, el costo por animal sale de dividir entre 10**, no entre 12 — el denominador excluye lo vendido en la comunidad.

El sembrado necesita la cadena completa: `Lote` → `CuyRegistro` (con su productora) → `LoteFaenado` → `RegistroFaenamiento` → `CuyFaenamiento` (con `NumeroEnLote` coincidente) → `Despacho` → `DespachoCuy`, más el `Pago` de la productora. **Móntalo directo a la base**, y para la última prueba marca dos `CuyRegistro` con `VentaLocalPagoId`.

- [ ] **Step 2: Ejecutar y ver que fallan**

- [ ] **Step 3: La consulta que alimenta la función pura**

En `ReportesService`, un método privado que, dado el rango, devuelve los animales despachados y los pagos que los cubren:

```csharp
    // Se materializa y se resuelve en memoria a propósito. La cadena
    // DespachoCuy -> CuyFaenamiento -> RegistroFaenamiento -> Lote, más el
    // salto de NumeroEnLote a CuyRegistro, produce en SQL una consulta que
    // nadie va a poder leer dentro de seis meses. Con el volumen del piloto
    // —cientos de animales por período— traerlo y resolverlo aquí es más
    // barato en mantenimiento que en milisegundos, y la parte con reglas
    // vive en una función pura que sí se puede fijar.
```

El paso clave es el denominador:

```csharp
        // Los animales que ese pago cubrió: los de esa productora en ese lote
        // que NO se vendieron en la comunidad. Esos nunca llegaron a la
        // planta y su pago fue otro; además es el mismo conteo que la
        // operadora vio al crear el pago.
        AnimalesCubiertos: cuyes.Count(c => c.LoteId == pago.LoteId
            && c.ProductoraId == pago.ProductoraId
            && c.VentaLocalPagoId == null)
```

Y solo cuentan los pagos **de planta**: `!p.EsVentaLocal`.

- [ ] **Step 4: Las dos vistas**

`MargenPorMesAsync` agrupa por el mes **local** (misma técnica que la Tarea 3) y `MargenPorClienteAsync` por `ClienteDestino` normalizado:

```csharp
        // ClienteDestino es texto libre, así que "Mercado Central" y "mercado
        // central" serían dos filas. Se normaliza para agrupar. Un catálogo
        // de clientes lo resolvería de raíz, pero es otro proyecto.
        var cliente = (d.ClienteDestino ?? string.Empty).Trim().ToUpperInvariant();
```

El ingreso solo suma los despachos **con** precio; los que no lo tienen se
cuentan en `DespachosSinPrecio`.

- [ ] **Step 5: Los endpoints, ejecutar y mutar**

Mismos roles que la Tarea 3.

Mutaciones, restaurando después de cada una:
1. Contar los despachos sin precio como cero en el ingreso → falla la prueba correspondiente.
2. Quitar `&& c.VentaLocalPagoId == null` del denominador → falla la prueba del denominador.
3. Sumar los pagos de venta local al costo → falla la del costo.

- [ ] **Step 6: Batería completa y commit**

```bash
git add Features/ tests/
git commit -m "feat: el reporte calcula el margen de la reventa

El costo se rastrea animal por animal hasta el pago de su productora, con
el denominador excluyendo lo vendido en la comunidad. Los despachos sin
precio y los animales sin pago se declaran aparte en vez de contarse como
cero.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 4 · Excel y pantalla

### Task 6: La descarga

**Files:**
- Modify: `Features/Reportes/Services/ReportesService.cs`
- Modify: `Features/Reportes/Controllers/ReportesController.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs`

**Interfaces:**
- Consumes: las cinco vistas (Tareas 3 y 5).
- Produces: `IReportesService.ExportarExcelGananciasAsync(FiltroPeriodoDto)` → `byte[]`

- [ ] **Step 1: La prueba**

Una prueba que descargue el Excel sobre un período con datos y afirme 200, el
tipo de contenido de hoja de cálculo, y un tamaño no trivial. Copia la forma de
las pruebas de Excel que ya existen en la batería.

- [ ] **Step 2: Implementar**

Un libro con **cinco hojas**: por CAT, por productora, por mes, margen por mes y
margen por cliente. Sigue el patrón de `ExportarExcelCATAsync`, que ya usa
ClosedXML en este archivo.

Las hojas de margen llevan, **debajo de la tabla**, las dos advertencias en
texto: cuántos despachos sin precio y cuántos animales sin costo. En un Excel que
alguien va a llevar a una reunión, esas dos cifras no pueden quedarse solo en la
pantalla.

- [ ] **Step 3: Ejecutar, mutar y commitear**

Mutación: devolver un libro vacío. Esperado: falla la prueba por tamaño.

---

### Task 7: La pestaña de Reportes

**Files:**
- Modify: `src/api/reportes.ts` (front)
- Modify: `src/pages/Reportes.tsx` (front)

**Interfaces:**
- Consumes: los cinco endpoints de vistas y el de Excel.
- Produces: nada.

- [ ] **Step 1: El cliente**

Cinco funciones nuevas en `src/api/reportes.ts` más la de descarga, siguiendo el
patrón de las que ya existen.

- [ ] **Step 2: La pestaña**

Una pestaña **«Ganancias»** en `Reportes.tsx`, junto a las siete actuales.
Reutiliza `FiltrosPeriodo` y el patrón de `PanelEstado`, que distingue «falló la
petición» de «no hay datos» — una distinción que ese archivo documenta como
aprendida de un bug reportado.

**La pestaña se compone de dos bloques visualmente separados**, con sus propios
títulos:

1. **«Lo que cobraron las productoras»** — las tres vistas de pagos.
2. **«Margen de la reventa»** — las dos de margen, con las dos advertencias
   visibles junto a las cifras, no en una nota al pie.

Que estén separados no es estética: **son dos cifras que no se suman**, y
ponerlas en la misma tabla invitaría a restarlas mal.

Debajo del margen, una línea que aclare que es **sobre el costo de los
animales** y no un resultado contable: no incluye transporte, faenamiento ni
empaque.

**La pestaña no se muestra al `OperadorCAT`**, que no tiene los endpoints. Mira
cómo `tabsVisibles(rol)` ya oculta pestañas al `AdminTecnico` y sigue ese
patrón.

- [ ] **Step 3: Verificar**

```bash
pnpm lint
```
```bash
pnpm exec tsc -b
```
```bash
pnpm build
```

- [ ] **Step 4: Comprobación manual**

1. Un período con ventas locales, cuotas y pagos de planta: las tres columnas
   cuadran y no se mezclan.
2. Un despacho sin precio en el período: aparece la advertencia con su número.
3. Un animal despachado cuya productora no ha cobrado: aparece la otra.
4. Entrar como `OperadorCAT`: la pestaña no está.

- [ ] **Step 5: Commit**

---

## Cierre

- [ ] **Batería completa del API en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: línea base + 21, 0 fallos.

- [ ] **Front verificado**

```bash
pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Verificación manual obligatoria**

1. **Registrar un despacho** y comprobar que el total en pantalla cuadra con
   precio × cantidad antes de confirmar.
2. **Las dos cifras del reporte no se suman en ningún sitio** ni en pantalla ni
   en el Excel.
3. **Las dos advertencias aparecen** cuando corresponde, con su número.
4. **El Excel abre** y sus cinco hojas tienen datos.

- [ ] **Abrir el PR**, después de los de A y B: esta rama va apilada sobre `feat/venta-local`.

## Lo que este plan deja fuera a propósito

- **Catálogo de clientes.** Resolvería de raíz la agrupación por nombre; es un CRUD propio.
- **Margen por despacho individual.** Descartado por precisión, no por esfuerzo: el costo por animal sale de repartir un pago que cubrió varios.
- **Otros costos de la cooperativa** —transporte, faenamiento, empaque—. El margen es sobre el costo de los animales, y el rótulo lo dice.
- **Gráficos.** Empieza en tablas.

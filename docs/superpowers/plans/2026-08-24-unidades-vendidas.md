# Unidades vendidas — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir a la pestaña Ganancias un tercer bloque con las unidades de cuyes vendidas por las dos vías —comunidad y despacho—, sus totales del período y una tabla por mes.

**Architecture:** Un endpoint nuevo devuelve una fila por mes con las dos columnas y su total. La venta local se cuenta por `CuyRegistro.VentaLocalPagoId` fechada por el pago; el despacho por `CantidadUnidades` neto de devoluciones, reutilizando `Devolucion.UnidadesPorDespachoAsync` para que el criterio de devoluciones no se duplique. Los totales del período los suma el front sobre esa misma respuesta, sin segunda llamada.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, ClosedXML, xUnit + Shouldly + Respawn, React 19 + TypeScript + Vite + Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-24-unidades-vendidas-design.md`

## Global Constraints

- **Ramas:** `feat/unidades-vendidas` en los dos repos, desde `origin/main`. **La del API ya existe y ya lleva el commit del spec** — no crear otra. La del front hay que crearla.
- **El Proyecto C ya está fusionado en `main`.** Todo lo que este plan consume (`RangoUtc`, `PagosDelPeriodo`, `Devolucion.UnidadesPorDespachoAsync`, la pestaña Ganancias, `FiltrosPeriodo`, `PanelEstado`) existe ya en `main`.
- **Punto de partida: 405/405 en verde.** Ejecuta la batería antes de tocar nada y anota el número: es tu línea base.
- **Nada de `dotnet test` ni `dotnet ef` directamente en Windows.** Smart App Control bloquea la carga del DLL desde OneDrive (`0x800711C7`).
  - Batería completa: `docker compose -f docker-compose.tests.yml run --rm tests`
  - Una clase: `docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"`
  - Puede tardar varios minutos; usa timeout amplio (600000 ms).
- **Los días del filtro son LOCALES del piloto, no UTC.** `ReportesService.RangoUtc(filtro)` ya lo resuelve y **hay que usarlo**. Tomarlos como UTC recortaba de todos los reportes las últimas cinco horas de cada día local — el fallo que se reportó como «los despachos nuevos no aparecen en Salida».
- **El mes se agrupa por la fecha LOCAL, materializando antes de agrupar**, porque `FechaUtc.ALocal` no se traduce a SQL. Deja el comentario que impide que alguien lo «optimice» de vuelta a un `GroupBy` en base de datos.
- **`FiltroPeriodoDto` normaliza el CAT a mayúsculas en su constructor** — es el borde único del feature. Aguas abajo basta con `filtro.CentroAcopio is not null`; **no** vuelvas a validar la forma ni a comparar contra la lista de códigos.
- **Respawn limpia la base antes de cada prueba pero trunca SIN RESTART IDENTITY.** No asumas que los IDs empiezan en 1.
- **Roles de todos los endpoints nuevos:** `[Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]`. El `OperadorCAT` **no** entra.
- **Verificación del front:** `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los tres con salida 0. No hay Vitest ni Playwright.
- **Objetivos táctiles de 44 px** en el front: `min-h-[44px]` para altura variable, `h-11` para inputs de una línea. **`min-h-12` no existe en este Tailwind y no aplicaría nada.**
- **Mutación obligatoria:** cada guarda nueva se comprueba quitándola, viendo la prueba en rojo y restaurándola. **Si una mutación no pone roja su prueba, para y avisa** — no ajustes la prueba.
- **Los números de cada sembrado tienen que distinguir lo correcto de lo incorrecto.** En el Proyecto C una prueba se escribió con datos donde el bien y el mal daban la misma cifra, y costó una ronda entera de revisión. Comprueba a mano que el valor esperado sería DISTINTO si la lógica estuviera mal, y ponlo en el informe.
- **Mensajes de commit en castellano**, terminados en `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Las dos reglas que gobiernan el bloque

**Aquí sumar SÍ es válido, y es la excepción de la pestaña.** El resto no suma nada porque un pago a una productora es ingreso para ella y costo para la cooperativa. Las unidades son distintas: un cuy vendido en la comunidad **no puede** acabar despachado —el sistema lo impide en la movilización, en el selector de lotes pendientes de pago, en el botón «A planta» y en el faenamiento—, así que no hay doble conteo. El rótulo del total tiene que dejar claro que suma **animales**, no dinero.

**Las unidades despachadas van netas de devoluciones**, igual que el ingreso. Si fueran brutas, las dos cifras se contradirían sobre el mismo despacho: el ingreso diría 140 unidades y las unidades dirían 200.

## File Structure

**API — se modifican**

| Archivo | Cambio |
|---|---|
| `Features/Reportes/DTOs/ReportesDtos.cs` | + `UnidadesMesDto` |
| `Features/Reportes/Services/ReportesService.cs` | + `UnidadesPorMesAsync`, + la sexta hoja |
| `Features/Reportes/Controllers/ReportesController.cs` | + el endpoint |
| `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs` | + las pruebas de unidades |
| `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs` | + el caso del endpoint nuevo |

**Front — se modifican**

| Archivo | Cambio |
|---|---|
| `src/types/reportes.ts` | + `UnidadesMesDto` |
| `src/api/reportes.ts` | + `unidadesPorMes` |
| `src/pages/Reportes.tsx` | + el tercer bloque |

---

## Fase 1 · El API

### Task 1: El conteo y el endpoint

**Files:**
- Modify: `Features/Reportes/DTOs/ReportesDtos.cs`
- Modify: `Features/Reportes/Services/ReportesService.cs`
- Modify: `Features/Reportes/Controllers/ReportesController.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`

**Interfaces:**
- Consumes: `ReportesService.RangoUtc(filtro)`, `ReportesService.PagosDelPeriodo(filtro)`, `Devolucion.UnidadesPorDespachoAsync(IQueryable<Devolucion>)`, `FechaUtc.ALocal(DateTime)`.
- Produces:
  - `UnidadesMesDto(string Agrupacion, int VendidasComunidad, int DespachadasClientes, int Total)`
  - `IReportesService.UnidadesPorMesAsync(FiltroPeriodoDto)` → `Task<IEnumerable<UnidadesMesDto>>`
  - `GET /api/reportes/unidades/mes`

- [ ] **Step 1: Anotar la línea base**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Total tests: 405, Passed: 405`.

Al cerrar esta tarea serán **412 más los casos de autorización del Step 8**: 405 de base, 7 `[Fact]` nuevos, y una fila por cada `[InlineData]` que añadas a la teoría de autorización —**cada `[InlineData]` cuenta como una prueba**—. Anota el número exacto que te salga; el plan no lo fija porque depende de cuántos roles cubras.

- [ ] **Step 2: Escribir las pruebas que fallan**

Añadir a `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs`, dentro de la clase existente:

```csharp
    private record UnidadesFila(
        string Agrupacion, int VendidasComunidad, int DespachadasClientes, int Total);

    /// Llama al endpoint de unidades por mes. `cat` nulo = sin filtro.
    private async Task<UnidadesFila[]> UnidadesPorMesAsync(string? cat = null)
    {
        var hoy = FechaUtc.ALocal(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var sufijo = cat is null ? "" : $"&cat={cat}";
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/unidades/mes?desde={hoy}&hasta={hoy}{sufijo}");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await respuesta.Content
            .ReadFromJsonAsync<UnidadesFila[]>())!;
    }

    [Fact]
    public async Task CuentaLosCuyesVendidosEnLaComunidad()
    {
        // 3 cuyes vendidos en la comunidad, y 2 del mismo lote que NO se
        // vendieron: si el conteo no mirara VentaLocalPagoId saldrían 5.
        await SembrarVentaLocalAsync(vendidos: 3, sinVender: 2);

        var filas = await UnidadesPorMesAsync();

        filas.Single().VendidasComunidad.ShouldBe(3);
    }

    [Fact]
    public async Task CuentaLasUnidadesDespachadas()
    {
        // Un despacho de 8 unidades, sin devoluciones.
        await SembrarDespachoAsync(unidades: 8, devueltas: 0);

        var filas = await UnidadesPorMesAsync();

        filas.Single().DespachadasClientes.ShouldBe(8);
    }

    [Fact]
    public async Task LasUnidadesDespachadasVanNetasDeDevoluciones()
    {
        // 8 despachadas, 3 devueltas -> 5. Bruto daría 8: los dos números
        // se distinguen sin ambigüedad.
        //
        // Neto y no bruto porque el Ingreso del margen ya es neto: si aquí
        // fueran brutas, las dos cifras se contradirían sobre el MISMO
        // despacho.
        await SembrarDespachoAsync(unidades: 8, devueltas: 3);

        var filas = await UnidadesPorMesAsync();

        filas.Single().DespachadasClientes.ShouldBe(5);
    }

    [Fact]
    public async Task ElTotalEsLaSumaDeLasDosVias()
    {
        // Aquí sumar SÍ es válido: un cuy vendido en la comunidad nunca
        // llega a la planta, así que no hay doble conteo. 3 + 5 = 8, y los
        // tres números son distintos entre sí.
        await SembrarVentaLocalAsync(vendidos: 3, sinVender: 2);
        await SembrarDespachoAsync(unidades: 8, devueltas: 3);

        var fila = (await UnidadesPorMesAsync()).Single();

        fila.VendidasComunidad.ShouldBe(3);
        fila.DespachadasClientes.ShouldBe(5);
        fila.Total.ShouldBe(8);
    }

    [Fact]
    public async Task UnDespachoDeLasVeinteHorasCaeEnSuPropioMes()
    {
        // Las 02:00 UTC del día 1 son las 21:00 del último día del mes
        // anterior en el CAT. Agrupar por el mes UTC lo mandaría al mes
        // siguiente: es el mismo fallo que se reportó como "los despachos
        // nuevos no aparecen en Salida".
        var mesAnterior = await SembrarDespachoDeFinDeMesAsync(unidades: 4);

        var filas = await UnidadesPorMesAsync();

        filas.Length.ShouldBe(1);
        filas[0].Agrupacion.ShouldBe(mesAnterior);
        filas[0].DespachadasClientes.ShouldBe(4);
    }

    [Fact]
    public async Task ElFiltroDeCatAcotaLaComunidadPeroNoElDespacho()
    {
        // La venta local SÍ filtra por CAT (el animal tiene productora, y la
        // productora su centro). El despacho NO: mezcla animales de varias
        // jaulas y por tanto de varios CAT.
        //
        // PAT vende 3 en comunidad, NIE vende 2. Filtrando por PAT: 3, no 5.
        // El despacho de 8-3=5 unidades no se toca.
        await SembrarVentaLocalAsync(vendidos: 3, sinVender: 0, cat: "PAT");
        await SembrarVentaLocalAsync(vendidos: 2, sinVender: 0, cat: "NIE",
            cedula: CedulaSecundaria);
        await SembrarDespachoAsync(unidades: 8, devueltas: 3);

        var fila = (await UnidadesPorMesAsync(cat: "PAT")).Single();

        fila.VendidasComunidad.ShouldBe(3);
        fila.DespachadasClientes.ShouldBe(5);
    }

    [Fact]
    public async Task UnaVentaLocalDeOtroMesNoCuenta()
    {
        // La venta se fecha por el PAGO, no por la entrega: la venta ocurre
        // cuando se cobra.
        await SembrarVentaLocalAsync(vendidos: 3, sinVender: 0);
        await SembrarVentaLocalDeOtroMesAsync(vendidos: 4);

        var filas = await UnidadesPorMesAsync();

        filas.Single().VendidasComunidad.ShouldBe(3);
    }
```

**Los sembradores: reutiliza los que ya existen, NO escribas otros.** Este archivo ya trae, probados, todo lo que necesitas:

| Ya existe | Firma | Qué monta |
|---|---|---|
| `SembrarLoteAsync` | `(string cedula, int cantidadAnimales, string cat = "PAT")` → `(Productora, Lote)` | Productora con su CAT + lote con sus `CuyRegistro` |
| `SembrarPagoVentaLocalAsync` | `(int productoraId, int loteId, decimal montoPagado)` → `int` (PagoId) | `Pago` con `EsVentaLocal = true`, `Estado = Recibido`, `FechaPago = UtcNow` |
| `SembrarDespachoAsync` | `(Lote lote, int[] numerosEnLote, decimal? precioUnitario, string cliente, DateTime? fechaDespacho = null)` → `int` (DespachoId) | `LoteFaenado` + `Despacho` con su cadena completa |
| `SembrarDevolucionAsync` | `(int despachoId, int cantidadUnidades, string cliente)` | `Devolucion` apuntando al despacho |
| `SembrarPagoDeFinDeMesAsync` | `()` | El patrón de frontera: construye la fecha **explícitamente**, no por diferencia contra `UtcNow` |

**Lo único que tienes que escribir es el paso que falta:** marcar los cuyes vendidos. `SembrarPagoVentaLocalAsync` crea el pago pero **no** pone `VentaLocalPagoId` en ningún `CuyRegistro` — eso es lo que distingue un cuy vendido de uno que no. Un helper corto:

```csharp
    /// Marca los `cantidad` primeros cuyes del lote como vendidos en ese pago.
    /// Los demás quedan sin marcar: son los que NO deben contarse.
    private async Task MarcarVendidosAsync(int loteId, int pagoId, int cantidad)
    {
        await using var db = api.NuevoDbContext();
        var cuyes = await db.CuyRegistros
            .Where(c => c.LoteId == loteId)
            .OrderBy(c => c.NumeroEnLote)
            .Take(cantidad)
            .ToListAsync();
        foreach (var cuy in cuyes) cuy.VentaLocalPagoId = pagoId;
        await db.SaveChangesAsync();
    }
```

Para el caso de «venta local de otro mes», retrasa el `FechaPago` del pago que devuelve `SembrarPagoVentaLocalAsync` con un `UPDATE` directo (`db.Pagos.FindAsync` + asignar + `SaveChangesAsync`), o añade un parámetro opcional de fecha a ese sembrador siguiendo cómo `SembrarDespachoAsync` ya acepta `fechaDespacho = null`. **La segunda opción es mejor** —deja el sembrador simétrico con su hermano— pero comprueba antes que no rompe a sus llamantes actuales.

Para el caso de frontera del despacho, pásale a `SembrarDespachoAsync` una `fechaDespacho` construida **explícitamente** a las 02:00 UTC del día 1 del mes actual, y compara contra la cadena `"yyyy-MM"` del mes **anterior**. Mira cómo `SembrarPagoDeFinDeMesAsync` construye la suya y sigue esa forma; **no la calcules por diferencia contra `UtcNow`**, que es lo que hizo inestable una prueba del Proyecto C y la dejó fallando media hora de cada día.

Para el caso de dos CAT, `SembrarLoteAsync` ya acepta el `cat`; usa una segunda cédula válida distinta de la que ya use el archivo.

**Adapta las llamadas de las pruebas del Step 2 a estas firmas reales** — las que escribí allí (`SembrarVentaLocalAsync(vendidos: 3, sinVender: 2)`, `SembrarDespachoAsync(unidades: 8, devueltas: 3)`) son la *intención*, no la firma. Componlas con los sembradores de la tabla y `MarcarVendidosAsync`, y **mantén los números tal cual**: están elegidos para que el valor correcto y el incorrecto se distingan sin ambigüedad.

- [ ] **Step 3: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ReporteGananciasTests"
```

Esperado: las siete nuevas fallan con 404 — el endpoint no existe.

- [ ] **Step 4: El DTO**

En `Features/Reportes/DTOs/ReportesDtos.cs`, junto a `MargenDto`:

```csharp
// ── Unidades vendidas ─────────────────────────────────────────────────
//
// Las dos vías por las que se vende un cuy, separadas. Un cuy va por UNA
// de las dos y nunca por las dos: el sistema impide que un animal vendido
// en la comunidad acabe despachado —lo comprueban la movilización, el
// selector de lotes pendientes de pago, el botón "A planta" y el
// faenamiento—, así que aquí NO hay doble conteo y Total es un número
// real.
//
// Es la única excepción de este reporte: las cifras de dinero nunca se
// suman entre sí, porque un pago a una productora es ingreso para ella y
// costo para la cooperativa. Estas son animales, y sí se suman.
//
// DespachadasClientes va NETA de devoluciones, igual que MargenDto.Ingreso:
// si fuera bruta, las dos cifras se contradirían sobre el mismo despacho.
public record UnidadesMesDto(
    string Agrupacion,
    int VendidasComunidad,
    int DespachadasClientes,
    int Total
);
```

- [ ] **Step 5: El servicio**

Añadir la firma a la interfaz `IReportesService`, junto a las de margen:

```csharp
    Task<IEnumerable<UnidadesMesDto>> UnidadesPorMesAsync(FiltroPeriodoDto filtro);
```

Y la implementación en `ReportesService`, después de `MargenPorClienteAsync`:

```csharp
    public async Task<IEnumerable<UnidadesMesDto>> UnidadesPorMesAsync(
        FiltroPeriodoDto filtro)
    {
        var (desdeUtc, hastaUtc) = RangoUtc(filtro);

        // ── Vendidas en la comunidad ──────────────────────────────────
        // Se fechan por el PAGO de la venta local, no por la entrega del
        // animal: la venta ocurre cuando se cobra. Es el mismo criterio que
        // usan las tres vistas de ganancias, que también van por FechaPago.
        //
        // SÍ filtra por CAT: el animal tiene productora y la productora su
        // centro asignado.
        IQueryable<CuyRegistro> comunidad = db.CuyRegistros
            .Where(c => c.VentaLocalPagoId != null
                && c.VentaLocalPago!.FechaPago >= desdeUtc
                && c.VentaLocalPago.FechaPago < hastaUtc
                && c.VentaLocalPago.Estado != EstadoPago.Pendiente);

        if (filtro.CentroAcopio is not null)
            comunidad = comunidad.Where(
                c => c.Productora!.CatAsignado == filtro.CentroAcopio);

        // Solo la fecha: es lo único que hace falta para agrupar, y una fila
        // por animal vendido es exactamente el conteo que se busca.
        var fechasComunidad = await comunidad
            .Select(c => c.VentaLocalPago!.FechaPago)
            .ToListAsync();

        // ── Despachadas a clientes ────────────────────────────────────
        // NO filtra por CAT, y es deliberado: un despacho mezcla animales de
        // varias jaulas y por tanto de varios CAT, así que filtrarlo o
        // duplicaría las unidades de un despacho mixto o las atribuiría a un
        // centro que solo puso una parte. Misma decisión, y mismo motivo, que
        // dejó las dos vistas de margen sin filtro de CAT.
        var despachos = await db.Despachos
            .Where(d => d.FechaDespacho >= desdeUtc && d.FechaDespacho < hastaUtc)
            .Select(d => new { d.Id, d.FechaDespacho, d.CantidadUnidades })
            .ToListAsync();

        // Mismo helper que el margen y que ListarDespachosAsync: el criterio
        // de qué cuenta como devuelto vive en un solo sitio y no puede
        // desincronizarse. Acota solo por DespachoId, no por FechaDevolucion
        // —a propósito, igual que en DatosDeMargenAsync—: una devolución de
        // marzo baja las unidades de enero al reejecutar ese reporte, en vez
        // de quedar varada en un mes al que no pertenece.
        var despachoIds = despachos.Select(d => d.Id).ToList();
        var devueltas = await Devolucion.UnidadesPorDespachoAsync(
            db.Devoluciones.Where(v => v.DespachoId != null
                && despachoIds.Contains(v.DespachoId.Value)));

        // ── Agrupación por el mes LOCAL ───────────────────────────────
        // FechaUtc.ALocal no se traduce a SQL, así que las dos consultas de
        // arriba materializan antes de agrupar. El volumen del piloto lo
        // permite de sobra: no cambiar esto por un GroupBy en base de datos,
        // que rompería la frontera del mes en silencio —un despacho de las
        // 20:00 del 31 de agosto pertenece a agosto, no a septiembre.
        static string Mes(DateTime utc)
        {
            var local = FechaUtc.ALocal(utc);
            return $"{local.Year:D4}-{local.Month:D2}";
        }

        var porMes = new SortedDictionary<string, (int Comunidad, int Despacho)>(
            StringComparer.Ordinal);

        foreach (var fecha in fechasComunidad)
        {
            var mes = Mes(fecha);
            var acumulado = porMes.GetValueOrDefault(mes);
            porMes[mes] = (acumulado.Comunidad + 1, acumulado.Despacho);
        }

        foreach (var d in despachos)
        {
            var mes = Mes(d.FechaDespacho);
            var acumulado = porMes.GetValueOrDefault(mes);
            // Math.Max por si una devolución corrupta superara lo despachado:
            // se muestra 0, no un negativo. Misma guarda que ConstruirMargen.
            var netas = Math.Max(
                0, d.CantidadUnidades - devueltas.GetValueOrDefault(d.Id));
            porMes[mes] = (acumulado.Comunidad, acumulado.Despacho + netas);
        }

        return porMes
            .Select(kv => new UnidadesMesDto(
                Agrupacion: kv.Key,
                VendidasComunidad: kv.Value.Comunidad,
                DespachadasClientes: kv.Value.Despacho,
                // Sumar aquí SÍ es válido: un cuy vendido en la comunidad no
                // puede acabar despachado, así que no hay doble conteo.
                Total: kv.Value.Comunidad + kv.Value.Despacho))
            .ToList();
    }
```

**Comprueba los `using`** que haga falta añadir al principio del archivo: `CuyRegistro` vive en `CoopagcuyApi.Features.Recepcion.Models` y `Devolucion` en `CoopagcuyApi.Features.Faenamiento.Models`. Mira cuáles ya están antes de añadir nada.

- [ ] **Step 6: El endpoint**

En `Features/Reportes/Controllers/ReportesController.cs`, después de los de margen:

```csharp
    /// <summary>
    /// Unidades de cuyes vendidas por las dos vías, por mes local del piloto.
    ///
    /// El filtro por CAT acota SOLO la columna de comunidad. La de despacho
    /// no se filtra: un despacho mezcla animales de varias jaulas y por tanto
    /// de varios CAT (mismo motivo que en las vistas de margen).
    /// </summary>
    [HttpGet("unidades/mes")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> UnidadesPorMes(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat = null) =>
        Ok(await servicio.UnidadesPorMesAsync(
            new FiltroPeriodoDto(desde, hasta, cat)));
```

**Mira antes cómo están escritos los endpoints de margen** en ese archivo —cómo reciben las fechas, cómo nombran el parámetro del servicio— y sigue exactamente esa forma. Si difieren de lo de arriba, gana lo del archivo.

- [ ] **Step 7: Ejecutar y comprobar por mutación**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ReporteGananciasTests"
```

Esperado: todas en verde.

Mutaciones, **restaurando después de cada una**:

1. Quitar `&& c.VentaLocalPagoId != null` del filtro de comunidad → falla `CuentaLosCuyesVendidosEnLaComunidad` (saldría 5 en vez de 3).
2. Usar `d.CantidadUnidades` sin restar `devueltas` → falla `LasUnidadesDespachadasVanNetasDeDevoluciones` (8 en vez de 5) y `ElTotalEsLaSumaDeLasDosVias`.
3. Agrupar por `utc.Year`/`utc.Month` en vez de por la fecha local → falla `UnDespachoDeLasVeinteHorasCaeEnSuPropioMes`.
4. Aplicar el filtro de CAT también a los despachos → falla `ElFiltroDeCatAcotaLaComunidadPeroNoElDespacho` (el despacho no tiene CAT que casar, así que saldría 0).
5. Fechar la comunidad por la entrega del cuy en vez de por el pago → falla `UnaVentaLocalDeOtroMesNoCuenta`.

**Si alguna mutación no pone roja su prueba, PARA y avisa.**

- [ ] **Step 8: La prueba de autorización**

En `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`, añadir el endpoint nuevo a la lista de casos que ya cubre los de exportación y ganancias. **Es una lista `[InlineData]` hardcodeada, no por reflexión**, así que un endpoint nuevo no queda cubierto solo con existir.

Sigue exactamente el patrón de las entradas vecinas. Incluye un caso que afirme que el `OperadorFaenamiento` recibe **200**, no solo que el `OperadorCAT` recibe 403: ese rol se añadió al proyecto a propósito y conviene que quede ejercitado.

Mutación: ampliar el `[Authorize]` del endpoint para incluir `OperadorCAT`, ver la prueba en rojo, restaurar.

- [ ] **Step 9: Batería completa y commit**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: 405 + 7 `[Fact]` + los casos de autorización del Step 8, y **0 fallos**. Anota el número exacto: es la línea base de la Tarea 2.

```bash
git add Features/ tests/
git commit -m "feat: el reporte cuenta los cuyes vendidos por las dos vias

La comunidad se cuenta por VentaLocalPagoId, fechada por el pago; el
despacho por CantidadUnidades neto de devoluciones, con el mismo helper
que usa el margen. Sumarlas es valido: un cuy vendido en la comunidad no
puede acabar despachado.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: La sexta hoja del Excel

**Files:**
- Modify: `Features/Reportes/Services/ReportesService.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/ReporteGananciasTests.cs`

**Interfaces:**
- Consumes: `UnidadesPorMesAsync` (Tarea 1), `EscribirEncabezadosGanancias`, `EscribirAlcanceCatAlInicio`, `EscribirAlcanceCatAlFinal`.
- Produces: nada que consuman tareas posteriores.

**Por qué va al Excel.** El libro es el que va a la reunión. Dejar fuera la cifra que motivó la feature obligaría a volver a la pantalla para leerla, y en una reunión eso no pasa: se decide con lo que hay delante.

- [ ] **Step 1: Ampliar la prueba estructural del Excel**

El archivo ya tiene una prueba que abre el libro con `XLWorkbook` y afirma las cinco hojas, sus nombres, que tienen filas y que las de margen llevan sus advertencias. **Amplíala** —no escribas otra— para que:

- afirme **seis** hojas en vez de cinco,
- afirme el nombre `"Unidades vendidas"`,
- afirme que esa hoja lleva su **línea de alcance de CAT**,
- afirme que lleva la línea de total, con la cifra.

Localízala buscando `XLWorkbook` en ese archivo y sigue la forma exacta de las aserciones que ya tiene.

- [ ] **Step 2: Ejecutar y ver que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ReporteGananciasTests"
```

Esperado: falla — el libro trae cinco hojas, no seis.

- [ ] **Step 3: La hoja**

En `ReportesService`, junto a `AgregarHojaMargen`:

```csharp
    // Sexta hoja. Lleva su línea de alcance como las tres de ganancias
    // porque la asimetría del filtro también la afecta, y de una forma que
    // no se ve en la propia hoja: la columna de comunidad SÍ está filtrada
    // por CAT y la de despacho NO. Quien abra el libro con ?cat= puesto
    // tiene que poder saberlo sin salir de esta pestaña.
    private static void AgregarHojaUnidades(
        XLWorkbook libro, IEnumerable<UnidadesMesDto> datos, string? cat)
    {
        var hoja = libro.Worksheets.Add("Unidades vendidas");
        EscribirAlcanceCatAlInicio(hoja, cat);
        EscribirEncabezadosGanancias(hoja, new[]
        {
            "Mes", "Vendidas en la comunidad", "Despachadas a clientes",
            "Total de animales"
        }, fila: 2);

        int fila = 3;
        var totalComunidad = 0;
        var totalDespacho = 0;
        foreach (var r in datos)
        {
            hoja.Cell(fila, 1).Value = r.Agrupacion;
            hoja.Cell(fila, 2).Value = r.VendidasComunidad;
            hoja.Cell(fila, 3).Value = r.DespachadasClientes;
            hoja.Cell(fila, 4).Value = r.Total;
            totalComunidad += r.VendidasComunidad;
            totalDespacho += r.DespachadasClientes;
            fila++;
        }

        // El rótulo dice ANIMALES a propósito: en el resto del libro nada se
        // suma, y sin esa palabra esta línea podría leerse como permiso para
        // sumar también las cifras de dinero de las otras hojas.
        var filaTotal = fila + 1;
        hoja.Cell(filaTotal, 1).Value =
            $"Total de animales vendidos en el período: {totalComunidad + totalDespacho} " +
            $"({totalComunidad} en la comunidad + {totalDespacho} despachados)";
        hoja.Cell(filaTotal, 1).Style.Font.Bold = true;

        hoja.Cell(filaTotal + 1, 1).Value =
            "La columna de comunidad respeta el filtro por centro de acopio; " +
            "la de despacho no, porque un despacho mezcla animales de varias " +
            "jaulas y por tanto de varios centros.";

        EscribirAlcanceCatAlFinal(hoja, filaTotal + 1, cat);

        // Al final, cuando ya está todo escrito: si se ajusta antes, las
        // líneas de abajo no se tienen en cuenta para el ancho.
        hoja.Columns().AdjustToContents();
    }
```

Y en `ExportarExcelGananciasAsync`, después de las dos hojas de margen:

```csharp
        var unidades = await UnidadesPorMesAsync(filtro);
```

```csharp
        AgregarHojaUnidades(libro, unidades, filtro.CentroAcopio);
```

**Comprueba el orden de `AdjustToContents()`** en las otras hojas antes de escribir la tuya: si alguna lo hace antes de escribir sus líneas de pie, la tuya no tiene por qué copiar ese fallo — hazlo al final, como arriba.

- [ ] **Step 4: Ejecutar y comprobar por mutación**

Mutaciones, restaurando después de cada una:

1. Quitar la hoja del libro → falla la aserción de seis hojas.
2. Quitar la línea de alcance de CAT → falla su aserción.
3. Quitar la línea de total → falla su aserción.

- [ ] **Step 5: Batería completa y commit**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: **el mismo conteo que dejó la Tarea 1**, con 0 fallos. No sube: has ampliado una prueba que ya existía, no añadido otra.

```bash
git add Features/ tests/
git commit -m "feat: el Excel de ganancias trae una sexta hoja con las unidades

El libro es el que va a la reunion: dejar fuera la cifra que motivo la
feature obligaria a volver a la pantalla. Lleva su linea de alcance
porque la columna de comunidad respeta el filtro por CAT y la de
despacho no.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 2 · La pantalla

### Task 3: El bloque de unidades

**Files:**
- Modify: `src/types/reportes.ts` (front)
- Modify: `src/api/reportes.ts` (front)
- Modify: `src/pages/Reportes.tsx` (front)

**Interfaces:**
- Consumes: `GET /api/reportes/unidades/mes` (Tarea 1).
- Produces: nada.

- [ ] **Step 1: Crear la rama del front**

```bash
git checkout -b feat/unidades-vendidas origin/main
```

- [ ] **Step 2: El tipo**

En `src/types/reportes.ts`, junto a `MargenDto`:

```ts
// Las dos vías por las que se vende un cuy, separadas. Aquí sumar SÍ vale
// —un cuy vendido en la comunidad nunca llega a la planta, así que no hay
// doble conteo—, y es la única excepción de esta pestaña: las cifras de
// dinero no se suman entre sí.
//
// despachadasClientes va neta de devoluciones, igual que margenDto.ingreso.
export interface UnidadesMesDto {
    agrupacion: string;
    vendidasComunidad: number;
    despachadasClientes: number;
    total: number;
}
```

- [ ] **Step 3: El cliente**

En `src/api/reportes.ts`, junto a las de ganancias. **Usa `FiltroPeriodo`, no `FiltroSinCat`**: este endpoint sí acepta `cat` —lo aplica a la columna de comunidad— y pasarle el filtro sin CAT perdería ese filtrado.

```ts
    // Acepta `cat`, a diferencia de las dos de margen: aquí el filtro sí
    // aplica, aunque solo a la columna de comunidad.
    unidadesPorMes: async (filtro: FiltroPeriodo) => {
        const { data } = await client.get<UnidadesMesDto[]>(
            "/api/reportes/unidades/mes", { params: filtro });
        return data;
    },
```

Añade `UnidadesMesDto` al import de tipos de ese archivo.

- [ ] **Step 4: La consulta**

En `src/pages/Reportes.tsx`, junto a las otras consultas del tab de ganancias. **Nunca desde un `useEffect`** — la convención es `useQuery` con `enabled`, y una violación de `react-hooks/set-state-in-effect` ya rompió un despliegue en este proyecto.

```tsx
    const qUnidades = useQuery({
        queryKey: ["unidades_mes", desde, hasta, cat],
        queryFn: () => reportesApi.unidadesPorMes(filtro),
        enabled: tab === "ganancias" && !!desde && !!hasta,
    });
```

Copia el `enabled` exacto de las consultas vecinas de esa pestaña si difiere del de arriba.

- [ ] **Step 5: El bloque**

Un tercer `<section>` en el tab de ganancias, **después del de margen** (que hoy cierra sobre la línea 1509). Con su propio `<h2>`, en el mismo estilo que los dos títulos que ya hay.

Contenido, en este orden:

1. **Las tres cifras del período**, sumadas en el front sobre la respuesta —sin segunda llamada, que sería una vía más por la que las dos cifras podrían discrepar:

```tsx
    const totalesUnidades = useMemo(() => {
        const filas = qUnidades.data ?? [];
        const comunidad = filas.reduce((n, r) => n + r.vendidasComunidad, 0);
        const despacho = filas.reduce((n, r) => n + r.despachadasClientes, 0);
        return { comunidad, despacho, total: comunidad + despacho };
    }, [qUnidades.data]);
```

2. **La tabla por mes**, con las columnas `Mes`, `Vendidas en la comunidad`, `Despachadas a clientes`, `Total`. Reutiliza los helpers de tabla que la pestaña ya tiene (`EncabezadoTabla` y los componentes de fila) y `nombreMesAgrupacion` para pintar `"2026-08"` como `"agosto 2026"`, como hacen las tablas de margen.

3. **El rótulo del total tiene que decir «animales»**, no solo «total». En el resto de la pestaña nada se suma, y sin esa palabra la columna podría leerse como permiso para sumar también el dinero de los bloques de arriba.

4. **El aviso de la asimetría del CAT.** El bloque de margen ya tiene un subtítulo fijo y un banner ámbar que aparece cuando hay una CAT elegida. **Reutiliza ese patrón** —míralo antes de escribir— adaptando el texto: aquí la columna de comunidad **sí** se filtra y la de despacho **no**, que es distinto de "todo el bloque ignora el filtro".

Usa `PanelEstado` para los estados de carga, error y vacío, como el resto de la pestaña: distingue «falló la petición» de «no hay datos», una distinción que ese archivo documenta como aprendida de un bug reportado.

**Objetivos táctiles de 44 px**: `min-h-[44px]` para altura variable, `h-11` para inputs de una línea. `min-h-12` no existe en este Tailwind.

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

Los tres con salida 0.

- [ ] **Step 7: Commit**

```bash
git add src/
git commit -m "feat: la pestana de Ganancias muestra las unidades vendidas

Un tercer bloque con las dos vias separadas, sus totales del periodo y
una tabla por mes. El rotulo del total dice animales a proposito: en el
resto de la pestana nada se suma.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Cierre

- [ ] **Batería completa del API en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: el conteo que dejó la Tarea 1, **0 fallos**.

- [ ] **Front verificado**

```bash
pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Verificación manual obligatoria**

1. Un período con ventas locales y despachos: las dos columnas cuadran y el total es su suma.
2. Un despacho con devoluciones: la columna de despacho muestra la cifra **neta**, y coincide con lo que el bloque de margen dice del mismo despacho.
3. Elegir una CAT: la columna de comunidad se acota, la de despacho no, **y el aviso aparece**.
4. El Excel abre, trae **seis** hojas, y la de unidades lleva su línea de alcance y su total.
5. Entrar como `OperadorCAT`: la pestaña Ganancias no está.

- [ ] **Abrir el PR** contra `develop`, y después a `main`. La rama es independiente: no va apilada sobre nada.

## Lo que este plan deja fuera a propósito

- **Desglose por CAT, por productora y por cliente.** Cinco vistas más, en buena parte solapadas con lo que las tablas de dinero ya ordenan. Se empieza por el mes, la única agrupación que sirve a las dos vías a la vez.
- **Unidades en el resto de pestañas.** «Salida» sigue listando fila por despacho sin totalizar.
- **Peso vendido.** El sistema tiene el peso de cada canal, pero la petición era de unidades.

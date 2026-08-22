# Correcciones puntuales — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corregir la hora de los documentos, hacer que el ticket explique el
descuento a la productora, liberar la comunidad en el alta de productoras,
adelgazar la página pública del QR, y fijar con pruebas dos comportamientos que
ya son correctos pero que nadie ha verificado.

**Architecture:** Todo texto que va a un PDF se compone en funciones puras,
públicas y estáticas, y se fija por unidad — QuestPDF comprime los flujos de
texto del documento, así que del binario no se puede afirmar nada. Es el patrón
que `TextosGuia` + `TextosGuiaTests` ya establecieron en este repositorio; aquí
se extiende, no se inventa. Los cambios de alcance (comunidad libre) y de
superficie pública (QR) sí son verificables de extremo a extremo y llevan
pruebas de integración de verdad.

**Tech Stack:** ASP.NET Core 8, EF Core + Npgsql, QuestPDF 2024.3.1, ClosedXML,
xUnit + Shouldly + Respawn, React 19 + TypeScript + Vite + Tailwind.

**Spec:** `docs/superpowers/specs/2026-08-22-correcciones-puntuales-design.md`

## Global Constraints

- **Rama del API:** `feat/correcciones-puntuales`, ya creada desde `origin/main`.
- **Rama del front:** crear `feat/correcciones-puntuales` desde `origin/main`.
- **Las pruebas del API solo corren dentro de Docker.** Smart App Control
  bloquea la carga del DLL desde OneDrive (0x800711C7), así que `dotnet test`
  no funciona en el Windows del equipo.
  - Batería completa: `docker compose -f docker-compose.tests.yml run --rm tests`
  - Una sola clase: `docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"`
- **Punto de partida: 211 pruebas en verde.** Al terminar deben seguir todas en
  verde salvo `AlcanceProductorasTests.OperadorCat_noCreaProductoraEnComunidadDeOtroCentro`,
  que se **reescribe a propósito** en la Tarea 6.
- **Respawn trunca SIN RESTART IDENTITY:** ninguna prueba puede asumir que la
  primera fila sembrada tenga `Id` 1. Usar siempre el `Id` que devuelve
  `Sembrador`.
- **Azurite NO se limpia entre pruebas**, solo Postgres. Cualquier aserción
  sobre blobs se hace por diferencia, nunca sobre un conteo absoluto.
- **Cédulas de prueba válidas** según el algoritmo ecuatoriano — `ProductoraService`
  las revalida: `0104576277`, `0102030405`, `0111223343`.
- **Comunidades sembradas con `HasData`, Ids estables:** 1 Patococha (PAT),
  2 Las Nieves (NIE), 3 Huertas (HUE), 4 Nabón/El Progreso (NAB), 5 Pelincay (PEL).
- **Verificación del front:** `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`, los
  tres con salida 0. No hay Vitest ni Playwright.
- **Mutación obligatoria:** cada guarda nueva se comprueba quitándola, viendo la
  prueba en rojo y restaurándola. En el ciclo de pago este paso detectó tres
  pruebas que pasaban con el fallo presente.
- **Mensajes de commit en castellano**, prefijo `feat:` / `fix:` / `test:` /
  `refactor:`, y terminados en:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

## File Structure

**API — se crean**

| Archivo | Responsabilidad |
|---|---|
| `Features/Pagos/Services/TextosTicket.cs` | Composición pura de las líneas del ticket que dependen de una regla |
| `tests/CoopagcuyApi.Tests/Unitarias/FechaLocalTests.cs` | Fija el desfase, el cruce de día y el nulo |
| `tests/CoopagcuyApi.Tests/Unitarias/TextosTicketTests.cs` | Fija las cuatro funciones del ticket |
| `tests/CoopagcuyApi.Tests/Integracion/PaginaPublicaTests.cs` | Fija qué NO expone el endpoint público |
| `tests/CoopagcuyApi.Tests/Integracion/AlcancePagosTests.cs` | Fija el filtro por CAT en pagos |
| `tests/CoopagcuyApi.Tests/Integracion/ResolucionPorCedulaTests.cs` | Fija la resolución offline por cédula |

**API — se modifican**

| Archivo | Cambio |
|---|---|
| `Common/FechaUtc.cs` | + `ALocal`, `FechaHoraLocal`, `FechaLocal` |
| `Common/Auth/AlcanceUsuario.cs` | − `ComunidadFueraDeAlcance` (queda muerta) |
| `Features/Pagos/Services/TicketPagoService.cs` | Hora local, `Include` de descuentos, bloque de desglose |
| `Features/Recepcion/Services/GuiaMovilizacionService.cs` | Hora local (4 sitios) |
| `Features/Faenamiento/Services/FaenamientoService.cs` | Hora local (4 sitios) |
| `Features/Reportes/Services/ReportesService.cs` | Hora local (13 sitios, PDF y Excel) |
| `Features/Productoras/Controllers/ProductorasController.cs` | − las dos guardas de comunidad |
| `Features/Productoras/Services/ProductoraService.cs` | − `CatDeComunidadAsync` (queda muerta) |
| `Features/QR/DTOs/QRDtos.cs` | − `ObservacionesProceso`, `DetalleCuyes`, `CuyPublicoDto` |
| `Features/QR/Services/QRService.cs` | Deja de exponerlos; **conserva** el cálculo de `conNovedad` |
| `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs` | + `PagoConNovedadAsync`, + `ComprobanteBase64` |
| `tests/CoopagcuyApi.Tests/Integracion/DescuentoTrazableTests.cs` | Usa el sembrador común |
| `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs` | + prueba del ticket con descuentos |
| `tests/CoopagcuyApi.Tests/Integracion/AlcanceProductorasTests.cs` | Reescribe una prueba |

**Front — se modifican**

| Archivo | Cambio |
|---|---|
| `src/components/productoras/FormProductora.tsx` | Todas las comunidades; el CAT del token manda |
| `src/pages/QRPublico.tsx` | − las dos tarjetas |
| `src/types/faenamiento.ts` | − `observacionesProceso`, `detalleCuyes` |

---

## Fase 1 · La hora local en los documentos

### Task 1: Las funciones de hora local

**Files:**
- Modify: `Common/FechaUtc.cs`
- Test: `tests/CoopagcuyApi.Tests/Unitarias/FechaLocalTests.cs` (crear)

**Interfaces:**
- Consumes: `FechaUtc.DesfasePiloto` y `FechaUtc.Normalizar`, ya existentes.
- Produces:
  - `public static DateTime FechaUtc.ALocal(DateTime valor)`
  - `public static string FechaUtc.FechaHoraLocal(DateTime? valor)` → `"21/08/2026 15:30"` o `"—"`
  - `public static string FechaUtc.FechaLocal(DateTime? valor)` → `"21/08/2026"` o `"—"`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Unitarias/FechaLocalTests.cs`:

```csharp
using CoopagcuyApi.Common;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Los documentos se imprimían con la hora UTC cruda: un faenamiento
/// registrado a las 15:30 salía en el informe como las 20:30. Todo lo que se
/// persiste sigue siendo UTC —eso está bien—; lo que cambia es cómo se
/// traduce en el momento de imprimirlo.
/// </summary>
public class FechaLocalTests
{
    [Fact]
    public void ALocal_restaLasCincoHorasDelPiloto()
    {
        var utc = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);

        FechaUtc.ALocal(utc).ShouldBe(new DateTime(2026, 8, 21, 15, 30, 0));
    }

    [Fact]
    public void ALocal_cruzaElDiaHaciaAtras()
    {
        // Las 02:00 UTC del 22 son las 21:00 del 21 en el CAT. Es el caso que
        // hacía que un registro de la tarde apareciera fechado al día
        // siguiente, no solo con la hora corrida.
        var utc = new DateTime(2026, 8, 22, 2, 0, 0, DateTimeKind.Utc);

        FechaUtc.FechaHoraLocal(utc).ShouldBe("21/08/2026 21:00");
    }

    [Fact]
    public void ALocal_trataUnKindNoEspecificadoComoUtc()
    {
        // Un valor que no venga de Npgsql llega como Unspecified. Se
        // interpreta como UTC y no como hora del servidor, que en un
        // contenedor puede estar en cualquier zona.
        var sinZona = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Unspecified);

        FechaUtc.ALocal(sinZona).ShouldBe(new DateTime(2026, 8, 21, 15, 30, 0));
    }

    [Fact]
    public void FechaLocal_omiteLaHora()
    {
        var utc = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);

        FechaUtc.FechaLocal(utc).ShouldBe("21/08/2026");
    }

    [Fact]
    public void FechaHoraLocal_sinFechaDevuelveGuion()
    {
        // Interpolar un DateTime? nulo produce cadena vacía, y en el papel eso
        // deja un renglón que dice "Recibido:" y nada más: indistinguible de
        // un fallo de maquetación.
        FechaUtc.FechaHoraLocal(null).ShouldBe("—");
    }

    [Fact]
    public void FechaLocal_sinFechaDevuelveGuion()
    {
        FechaUtc.FechaLocal(null).ShouldBe("—");
    }

    [Fact]
    public void ElFormatoNoDependeDeLaCulturaDeLaMaquina()
    {
        // "dd/MM/yyyy" usa el separador de fecha de la cultura activa, no una
        // barra literal. Sin CultureInfo.InvariantCulture, la misma línea sale
        // distinta según dónde corra el contenedor.
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var utc = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);
            FechaUtc.FechaLocal(utc).ShouldBe("21/08/2026");
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
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~FechaLocalTests"
```

Esperado: error de compilación — `ALocal`, `FechaHoraLocal` y `FechaLocal` no
existen en `FechaUtc`.

- [ ] **Step 3: Implementar las tres funciones**

Añadir a `Common/FechaUtc.cs`, al final de la clase (y `using System.Globalization;`
en la cabecera):

```csharp
    /// <summary>
    /// El mismo instante, expresado en la hora local del piloto.
    ///
    /// Normaliza el Kind antes de restar: un valor que llegue como
    /// Unspecified —una ruta que no venga de Npgsql, un objeto construido en
    /// memoria— hay que tratarlo como UTC y no como hora del servidor.
    ///
    /// El resultado se marca Unspecified a propósito: ya NO es un instante
    /// UTC, y dejarlo marcado como Utc invitaría a que alguien lo volviera a
    /// convertir más abajo.
    /// </summary>
    public static DateTime ALocal(DateTime valor) =>
        DateTime.SpecifyKind(Normalizar(valor) + DesfasePiloto,
            DateTimeKind.Unspecified);

    /// <summary>
    /// "21/08/2026 15:30" en hora local del piloto, o "—" si no hay fecha.
    ///
    /// InvariantCulture no es una precaución vacía: en un formato
    /// personalizado la barra es el MARCADOR de separador de fecha, no una
    /// barra literal, así que la misma línea saldría distinta según la
    /// cultura activa del contenedor.
    ///
    /// El guion tampoco es adorno: interpolar un DateTime? nulo produce
    /// cadena vacía, y en el papel eso deja un rótulo sin valor detrás.
    /// </summary>
    public static string FechaHoraLocal(DateTime? valor) =>
        valor is DateTime v
            ? ALocal(v).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : "—";

    /// <summary>Igual que <see cref="FechaHoraLocal"/> pero sin la hora.</summary>
    public static string FechaLocal(DateTime? valor) =>
        valor is DateTime v
            ? ALocal(v).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : "—";
```

- [ ] **Step 4: Ejecutar y ver que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~FechaLocalTests"
```

Esperado: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 5: Comprobar por mutación**

Cambiar `+ DesfasePiloto` por `- DesfasePiloto` y volver a ejecutar. Esperado:
fallan `ALocal_restaLasCincoHorasDelPiloto`, `ALocal_cruzaElDiaHaciaAtras`,
`ALocal_trataUnKindNoEspecificadoComoUtc` y `FechaLocal_omiteLaHora`.
**Restaurar el signo.**

Quitar `CultureInfo.InvariantCulture` de `FechaLocal` y volver a ejecutar.
Esperado: falla `ElFormatoNoDependeDeLaCulturaDeLaMaquina`. **Restaurarlo.**

- [ ] **Step 6: Commit**

```bash
git add Common/FechaUtc.cs tests/CoopagcuyApi.Tests/Unitarias/FechaLocalTests.cs
git commit -m "feat: FechaUtc sabe expresar un instante en la hora del piloto

Los documentos imprimían el DateTime UTC crudo. Estas tres funciones son
el único sitio donde se traduce, y son puras para poder fijarlas: del PDF
no se puede afirmar nada.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Aplicar la hora local a los 23 sitios

**Files:**
- Modify: `Features/Pagos/Services/TicketPagoService.cs` (2 sitios)
- Modify: `Features/Recepcion/Services/GuiaMovilizacionService.cs` (4 sitios)
- Modify: `Features/Faenamiento/Services/FaenamientoService.cs` (4 sitios)
- Modify: `Features/Reportes/Services/ReportesService.cs` (13 sitios)

**Interfaces:**
- Consumes: `FechaUtc.FechaHoraLocal` y `FechaUtc.FechaLocal` de la Tarea 1.
- Produces: nada nuevo. Es sustitución en sitio.

**Nota sobre los números de línea:** son los del árbol antes de esta tarea.
Cambian a medida que se edita el archivo — localizar por el texto, no por la
línea.

- [ ] **Step 1: Sustituir en `TicketPagoService.cs`**

Añadir `using CoopagcuyApi.Common;` si no está (ya lo está, por `EstadoPago`).

| Antes | Después |
|---|---|
| `$"Emitido: {pago.FechaPago:dd/MM/yyyy HH:mm}"` | `$"Emitido: {FechaUtc.FechaHoraLocal(pago.FechaPago)}"` |
| `$"Recibido: {pago.Lote?.FechaRecepcion:dd/MM/yyyy}"` | `$"Recibido: {FechaUtc.FechaLocal(pago.Lote?.FechaRecepcion)}"` |

- [ ] **Step 2: Sustituir en `GuiaMovilizacionService.cs`**

Añadir `using CoopagcuyApi.Common;` si no está.

| Antes | Después |
|---|---|
| `$"Emitida: {DateTime.Now:dd/MM/yyyy HH:mm}"` | `$"Emitida: {FechaUtc.FechaHoraLocal(DateTime.UtcNow)}"` |
| `$"Recepción: {lote.FechaRecepcion:dd/MM/yyyy HH:mm}"` | `$"Recepción: {FechaUtc.FechaHoraLocal(lote.FechaRecepcion)}"` |
| `$"Despacho: {movilizacion.FechaDespacho:dd/MM/yyyy HH:mm}"` | `$"Despacho: {FechaUtc.FechaHoraLocal(movilizacion.FechaDespacho)}"` |
| `$"Recibido en planta: {movilizacion.FechaRecepcionPlanta:dd/MM/yyyy HH:mm} "` | `$"Recibido en planta: {FechaUtc.FechaHoraLocal(movilizacion.FechaRecepcionPlanta)} "` |

`DateTime.Now` → `DateTime.UtcNow`: en el Windows del equipo `Now` ya daba la
hora correcta, y en el contenedor Linux daba UTC. Por eso este fallo concreto
solo se veía en producción.

- [ ] **Step 3: Sustituir en `FaenamientoService.cs`**

Los cuatro están en `InkJetCodigoDto` — el código que se imprime sobre el
producto.

| Antes | Después |
|---|---|
| `loteFaenado.FechaFaenamiento.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(loteFaenado.FechaFaenamiento)` |
| `loteFaenado.FechaFaenamiento.AddDays(5).ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(loteFaenado.FechaFaenamiento.AddDays(5))` |
| `faenamiento.FechaFaenamiento.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(faenamiento.FechaFaenamiento)` |
| `faenamiento.FechaFaenamiento.AddDays(5).ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(faenamiento.FechaFaenamiento.AddDays(5))` |

- [ ] **Step 4: Sustituir en `ReportesService.cs`**

Trece sitios. Los ocho primeros son celdas de Excel, los cinco últimos van a un
PDF.

| Antes | Después |
|---|---|
| `r.UltimaEntrega?.ToString("dd/MM/yyyy") ?? "-"` | `FechaUtc.FechaLocal(r.UltimaEntrega)` |
| `r.FechaRegistro.ToString("dd/MM/yyyy HH:mm")` | `FechaUtc.FechaHoraLocal(r.FechaRegistro)` |
| `r.FechaRecepcion.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(r.FechaRecepcion)` |
| `d.FechaDevolucion.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(d.FechaDevolucion)` |
| `r.FechaRetorno.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(r.FechaRetorno)` |
| `r.FechaLlegada.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(r.FechaLlegada)` |
| `r.FechaFaenamiento.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(r.FechaFaenamiento)` |
| `r.FechaDespacho.ToString("dd/MM/yyyy")` | `FechaUtc.FechaLocal(r.FechaDespacho)` |
| `DateTime.Now.ToString("dd/MM/yyyy")` (×2) | `FechaUtc.FechaLocal(DateTime.UtcNow)` |
| `$"Fecha: {loteFaenado.FechaFaenamiento:dd/MM/yyyy HH:mm}"` | `$"Fecha: {FechaUtc.FechaHoraLocal(loteFaenado.FechaFaenamiento)}"` |
| `$"Fecha: {lote.FechaRecepcion:dd/MM/yyyy}"` | `$"Fecha: {FechaUtc.FechaLocal(lote.FechaRecepcion)}"` |
| `$"• {sesion.FechaFaenamiento:dd/MM/yyyy}: "` | `$"• {FechaUtc.FechaLocal(sesion.FechaFaenamiento)}: "` |

**Cambio de conducta consciente en la primera fila:** la celda vacía pasa de
`"-"` a `"—"`. Se unifica a propósito con el resto de los documentos; es una
celda de Excel, no un dato que nadie parsee.

- [ ] **Step 5: Comprobar que no queda ningún sitio crudo**

```bash
grep -rn "ToString(\"dd/MM\|:dd/MM\|DateTime\.Now" Features/Pagos/Services/TicketPagoService.cs Features/Recepcion/Services/GuiaMovilizacionService.cs Features/Faenamiento/Services/FaenamientoService.cs Features/Reportes/Services/ReportesService.cs
```

Esperado: **sin salida**. Es el gate más cercano a una prueba que este cambio
admite — ninguna aserción puede demostrar que se cubrieron todos los sitios.

- [ ] **Step 6: Batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 218, Failed: 0` (211 previas + 7 de la Tarea 1).

- [ ] **Step 7: Commit**

```bash
git add Features/
git commit -m "fix: los documentos imprimen la hora del CAT, no la UTC

Un faenamiento registrado a las 15:30 salía en el informe como las 20:30.
Todo se sigue guardando en UTC; lo que cambia es la traducción al
imprimir. Los DateTime.Now se van con ello: en el Windows del equipo
daban la hora correcta y en el contenedor Linux no, que es justo por lo
que este fallo solo se veía en producción.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 2 · El desglose del descuento en el ticket

### Task 3: Las funciones del ticket

**Files:**
- Create: `Features/Pagos/Services/TextosTicket.cs`
- Test: `tests/CoopagcuyApi.Tests/Unitarias/TextosTicketTests.cs` (crear)

**Interfaces:**
- Consumes: `DescuentoPago`, `Pago`, `Novedad`, `CuyRegistro`, `TipoNovedad`.
- Produces:
  - `public static string TextosTicket.EtiquetaTipo(TipoNovedad tipo)`
  - `public static string TextosTicket.LineaNovedad(DescuentoPago descuento)`
  - `public static decimal TextosTicket.MontoDestacado(Pago pago)`
  - `public static bool TextosTicket.HayDesglose(Pago pago)`

- [ ] **Step 1: Escribir las pruebas que fallan**

Crear `tests/CoopagcuyApi.Tests/Unitarias/TextosTicketTests.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Pagos.Services;
using CoopagcuyApi.Features.Recepcion.Models;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El ticket imprimía siempre el monto original y no mencionaba los
/// descuentos: una productora con un ticket reimpreso leía "USD 120,00" y
/// "PAGADO" cuando lo que le llegaron fueron 103. No tiene cuenta en el
/// sistema — el papel es el único canal por el que puede enterarse.
/// </summary>
public class TextosTicketTests
{
    private static DescuentoPago Descuento(
        TipoNovedad tipo, int? numeroEnLote, decimal monto = 8m) =>
        new()
        {
            MontoUsd = monto,
            Descripcion = "motivo de prueba",
            NovedadCat = new Novedad
            {
                Tipo = tipo,
                CuyRegistro = numeroEnLote is int n
                    ? new CuyRegistro { NumeroEnLote = n }
                    : null
            }
        };

    [Fact]
    public void LineaNovedad_nombraElAnimalYElTipo()
    {
        TextosTicket.LineaNovedad(Descuento(TipoNovedad.OrejaDura, 3))
            .ShouldBe("Cuy #3 · Oreja dura");
    }

    [Fact]
    public void LineaNovedad_sinAnimalNoInventaUnNumero()
    {
        // PagoService rechaza con 409 los descuentos cuya novedad no cuelga de
        // un animal, así que por la vía de escritura actual esto no llega
        // aquí. Se contempla igual: el modelo lo admite, y una función que
        // revienta con un nulo legal convierte el ticket en un error 500.
        TextosTicket.LineaNovedad(Descuento(TipoNovedad.SinAyuno, null))
            .ShouldBe("Sin ayuno");
    }

    [Fact]
    public void EtiquetaTipo_cubreTodoElEnum()
    {
        // Sin esto, añadir un TipoNovedad nuevo dejaría el rótulo por defecto
        // impreso en un ticket sin que nada avisara.
        foreach (var tipo in Enum.GetValues<TipoNovedad>())
            TextosTicket.EtiquetaTipo(tipo).ShouldNotBe("Novedad");
    }

    [Fact]
    public void EtiquetaTipo_noImprimeElNombreDelEnum()
    {
        // "BajoPeso" no es algo que se le entregue impreso a una productora.
        TextosTicket.EtiquetaTipo(TipoNovedad.BajoPeso).ShouldBe("Bajo peso");
        TextosTicket.EtiquetaTipo(TipoNovedad.SignosClinicos)
            .ShouldBe("Signos clínicos");
    }

    [Fact]
    public void MontoDestacado_pendiente_esElDelTicket()
    {
        var pago = new Pago { MontoUsd = 120m, MontoPagadoUsd = null };

        TextosTicket.MontoDestacado(pago).ShouldBe(120m);
    }

    [Fact]
    public void MontoDestacado_pagado_esLoQueLaProductoraCobro()
    {
        // El fallo entero de esta feature en una línea: imprimir 120 en un
        // ticket donde llegaron 103.
        var pago = new Pago { MontoUsd = 120m, MontoPagadoUsd = 103m };

        TextosTicket.MontoDestacado(pago).ShouldBe(103m);
    }

    [Fact]
    public void HayDesglose_sinDescuentosEsFalso()
    {
        // Un ticket pendiente debe salir EXACTAMENTE igual que antes de esta
        // feature.
        TextosTicket.HayDesglose(new Pago()).ShouldBeFalse();
    }

    [Fact]
    public void HayDesglose_conDescuentosEsVerdadero()
    {
        var pago = new Pago();
        pago.Descuentos.Add(Descuento(TipoNovedad.BajoPeso, 1));

        TextosTicket.HayDesglose(pago).ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TextosTicketTests"
```

Esperado: error de compilación — el tipo `TextosTicket` no existe.

- [ ] **Step 3: Implementar `TextosTicket`**

Crear `Features/Pagos/Services/TextosTicket.cs`:

```csharp
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;

namespace CoopagcuyApi.Features.Pagos.Services;

/// <summary>
/// Composición de las líneas del ticket cuyo contenido depende de una regla.
///
/// Vive fuera del armado del PDF por el mismo motivo que TextosGuia: QuestPDF
/// comprime los flujos de texto del documento, así que del binario no hay
/// forma razonable de afirmar nada. Como funciones puras sí se comprueban.
/// </summary>
public static class TextosTicket
{
    /// <summary>
    /// Etiqueta legible del tipo de novedad.
    ///
    /// El API no tenía ninguna: el único mapa que existía vivía en el front
    /// (AnilloNovedades.tsx). El nombre del enum tal cual —"BajoPeso"— no es
    /// algo que se le entregue impreso a una productora.
    /// </summary>
    public static string EtiquetaTipo(TipoNovedad tipo) => tipo switch
    {
        TipoNovedad.BajoPeso => "Bajo peso",
        TipoNovedad.OrejaDura => "Oreja dura",
        TipoNovedad.ColorNoConforme => "Color no conforme",
        TipoNovedad.SinAyuno => "Sin ayuno",
        TipoNovedad.SobrePeso => "Sobre peso",
        TipoNovedad.SignosClinicos => "Signos clínicos",
        TipoNovedad.Otro => "Otro",
        _ => "Novedad"
    };

    /// <summary>
    /// "Cuy #3 · Oreja dura", o solo el tipo si la novedad no cuelga de un
    /// animal.
    ///
    /// PagoService rechaza con 409 los descuentos cuya novedad no tiene cuy
    /// asociado, así que por la vía de escritura actual el nulo no llega
    /// aquí. Se contempla igual: el modelo lo admite, y reventar con un nulo
    /// legal convertiría el ticket en un 500 al pulsar "Imprimir".
    /// </summary>
    public static string LineaNovedad(DescuentoPago descuento)
    {
        var tipo = EtiquetaTipo(descuento.NovedadCat.Tipo);
        var numero = descuento.NovedadCat.CuyRegistro?.NumeroEnLote;
        return numero is int n ? $"Cuy #{n} · {tipo}" : tipo;
    }

    /// <summary>
    /// La cifra que va en grande: lo que la productora cobra de verdad.
    ///
    /// Mientras el ticket está pendiente no hay monto pagado y se imprime el
    /// del ticket, igual que siempre. Una vez pagado con descuentos, imprimir
    /// MontoUsd sería darle una cifra que nadie le entregó.
    /// </summary>
    public static decimal MontoDestacado(Pago pago) =>
        pago.MontoPagadoUsd ?? pago.MontoUsd;

    /// <summary>
    /// Si hay que imprimir el bloque de descuentos. Sin descuentos, el ticket
    /// sale exactamente igual que antes de esta feature.
    /// </summary>
    public static bool HayDesglose(Pago pago) => pago.Descuentos.Count > 0;
}
```

- [ ] **Step 4: Ejecutar y ver que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TextosTicketTests"
```

Esperado: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 5: Comprobar por mutación**

Cambiar `MontoDestacado` a `pago.MontoUsd`. Esperado: falla
`MontoDestacado_pagado_esLoQueLaProductoraCobro`. **Restaurar.**

Quitar el caso `TipoNovedad.SobrePeso` del `switch`. Esperado: falla
`EtiquetaTipo_cubreTodoElEnum`. **Restaurar.**

- [ ] **Step 6: Commit**

```bash
git add Features/Pagos/Services/TextosTicket.cs tests/CoopagcuyApi.Tests/Unitarias/TextosTicketTests.cs
git commit -m "feat: composicion de las lineas de descuento del ticket

Funciones puras porque del PDF no se puede afirmar nada, igual que
TextosGuia. Todavia no las usa nadie: el maquetado va en el commit
siguiente.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: El ticket imprime el desglose

**Files:**
- Modify: `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/DescuentoTrazableTests.cs`
- Modify: `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs`
- Modify: `Features/Pagos/Services/TicketPagoService.cs`

**Interfaces:**
- Consumes: las cuatro funciones de `TextosTicket` (Tarea 3).
- Produces: `Sembrador.PagoConNovedadAsync(ApiFactory api, string cedula, decimal monto)`
  → `Task<(int PagoId, int NovedadId)>`, y `Sembrador.ComprobanteBase64` → `string`.

- [ ] **Step 1: Mover el sembrador de pagos a `Sembrador` (refactor puro)**

`DescuentoTrazableTests` tiene un helper privado que la nueva prueba del ticket
necesita igual. Se sube a `Sembrador`, que es su sitio, en vez de duplicar
cuarenta líneas.

Añadir a `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs` (con
`using CoopagcuyApi.Features.Recepcion.Models;` y `using System.Net.Http.Json;`
en la cabecera):

```csharp
    /// JPEG mínimo válido —SOI + APP0 + EOI— en base64. Sirve como captura de
    /// transferencia en cualquier prueba que tenga que pagar un ticket.
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    public static string ComprobanteBase64 => Convert.ToBase64String(JpegMinimo);

    /// <summary>
    /// Entrega real de dos cuyes en PAT —uno con signos clínicos, para que el
    /// CAT le genere una novedad ligada a ese animal— y su ticket por el monto
    /// indicado. Devuelve el Id del pago y el de la novedad, que es lo que
    /// hace falta para citar un descuento trazable.
    /// </summary>
    public static async Task<(int PagoId, int NovedadId)> PagoConNovedadAsync(
        ApiFactory api, string cedula, decimal monto)
    {
        var productora = await ProductoraAsync(api, cedula, CentroAcopio.PAT);

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
```

En `DescuentoTrazableTests.cs`: borrar el método privado
`TicketConNovedadAsync`, borrar el array `JpegMinimo` y la propiedad
`Comprobante`, y repuntar todas sus llamadas:
- `TicketConNovedadAsync(X, Y)` → `Sembrador.PagoConNovedadAsync(api, X, Y)`
- `Comprobante` → `Sembrador.ComprobanteBase64`

- [ ] **Step 2: Comprobar que el refactor no rompió nada**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~DescuentoTrazableTests"
```

Esperado: todas en verde, el mismo número que antes del refactor.

- [ ] **Step 3: Commit del refactor, por separado**

```bash
git add tests/
git commit -m "refactor: el sembrador de pagos con novedad sube a Sembrador

La prueba del ticket con descuentos necesita exactamente el mismo
montaje. Subirlo evita duplicar cuarenta lineas de sembrado.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Escribir la prueba de integración que falla**

Añadir a `tests/CoopagcuyApi.Tests/Integracion/TicketPagoTests.cs` (y
`using System.Net.Http.Json;` en la cabecera si no está):

```csharp
    [Fact]
    public async Task UnTicketConDescuentosSeSigueDescargando()
    {
        // Las unitarias de TextosTicket construyen los objetos en memoria con
        // la navegación ya poblada: pasarían igual aunque el Include faltara.
        // Lo que ocurre en ese caso no es un texto feo, es un
        // NullReferenceException al componer el PDF — un 500 al pulsar
        // "Imprimir", y justo en el ticket que sí lleva descuentos.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(
            api, CedulaProductora, 120m);

        var pagado = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "oreja calcificada, canal fuera de norma",
                    montoUsd = 17m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });
        pagado.EnsureSuccessStatusCode();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        bytes[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }
```

- [ ] **Step 5: Ejecutar y ver que pasa (todavía no prueba nada)**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TicketPagoTests"
```

Esperado: **PASA**. Es correcto y es el orden bueno: el ticket actual no toca
los descuentos, así que no puede fallar. Esta prueba existe para el paso 6, que
es cuando empieza a poder romperse.

- [ ] **Step 6: Añadir el `Include` y el bloque de desglose**

En `TicketPagoService.GenerarAsync`, ampliar la consulta:

```csharp
        var pago = await db.Pagos
            .Include(p => p.Productora).ThenInclude(pr => pr.Comunidad)
            .Include(p => p.Lote)
            .Include(p => p.Descuentos).ThenInclude(d => d.NovedadCat)
                .ThenInclude(n => n.CuyRegistro)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");
```

Y sustituir la línea del monto grande:

```csharp
                    col.Item().AlignCenter().Text($"USD {pago.MontoUsd:N2}")
                        .FontSize(18).Bold();
```

por:

```csharp
                    if (TextosTicket.HayDesglose(pago))
                    {
                        col.Item().Text($"Subtotal: USD {pago.MontoUsd:N2}");
                        col.Item().PaddingTop(2).Text("DESCUENTOS").Bold();

                        // Orden por Id: dos reimpresiones del mismo ticket
                        // tienen que salir iguales.
                        foreach (var descuento in pago.Descuentos.OrderBy(d => d.Id))
                        {
                            col.Item().Text(TextosTicket.LineaNovedad(descuento));

                            // Sin truncar. Es el motivo por el que se le pagó
                            // menos: media frase la deja sin poder reclamar, y
                            // eso es peor que no imprimirlo. QuestPDF envuelve
                            // solo dentro del ancho del rollo.
                            col.Item().PaddingLeft(4)
                                .Text(descuento.Descripcion).FontSize(7);

                            col.Item().AlignRight()
                                .Text($"-USD {descuento.MontoUsd:N2}");
                        }

                        col.Item().LineHorizontal(0.5f);
                    }

                    col.Item().AlignCenter()
                        .Text($"USD {TextosTicket.MontoDestacado(pago):N2}")
                        .FontSize(18).Bold();
```

- [ ] **Step 7: Ejecutar la clase entera**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~TicketPagoTests"
```

Esperado: `Passed: 6, Failed: 0`.

- [ ] **Step 8: Comprobar por mutación que la prueba nueva sirve de algo**

Quitar `.ThenInclude(n => n.CuyRegistro)` del `Include`. Volver a ejecutar.
Esperado: **falla** `UnTicketConDescuentosSeSigueDescargando` con un 500 —
ninguna unitaria se entera. **Restaurar el `ThenInclude`.**

- [ ] **Step 9: Batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 227, Failed: 0`.

- [ ] **Step 10: Commit**

```bash
git add Features/Pagos/Services/TicketPagoService.cs tests/
git commit -m "fix: el ticket explica por que se pago menos

Imprimia siempre el monto original: una productora con un ticket
reimpreso leia USD 120,00 y PAGADO cuando le habian llegado 103, y el
motivo del descuento estaba en la base sin llegar nunca al papel. No
tiene cuenta en el sistema — el papel es el unico canal.

Un ticket sin descuentos sale identico a antes.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 3 · Comunidad libre en el alta de productora

### Task 5: El servidor deja de restringir la comunidad

**Files:**
- Modify: `tests/CoopagcuyApi.Tests/Integracion/AlcanceProductorasTests.cs`
- Modify: `Features/Productoras/Controllers/ProductorasController.cs`
- Modify: `Common/Auth/AlcanceUsuario.cs`
- Modify: `Features/Productoras/Services/ProductoraService.cs`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: nada. Se eliminan `ClaimsPrincipal.ComunidadFueraDeAlcance` e
  `IProductoraService.CatDeComunidadAsync`.

**Contexto:** esto **invierte una regla deliberada**, no arregla un descuido. El
controlador rechaza a propósito las comunidades de otro CAT, con un comentario
que defiende la regla. El criterio nuevo es que la comunidad es dónde vive la
productora y el CAT es dónde entrega, y no tienen por qué coincidir.

- [ ] **Step 1: Reescribir la prueba que afirma lo contrario**

En `AlcanceProductorasTests.cs`, sustituir **el método completo**
`OperadorCat_noCreaProductoraEnComunidadDeOtroCentro` por:

```csharp
    [Fact]
    public async Task OperadorCat_creaProductoraDeCualquierComunidad()
    {
        // Cambio de criterio de 2026-08: la comunidad es donde vive la
        // productora y el CAT es donde entrega. Antes esto respondía 403 para
        // no "ensuciar el catálogo de otro centro"; resultó que la realidad
        // del piloto es justo esa — hay productoras que viven en una comunidad
        // y entregan en el CAT de al lado.
        //
        // Lo que NO cambia: el centro lo sigue sellando el token.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, CatReferencia = NIE
                catAsignado = "PAT",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var creada = await respuesta.Content
            .ReadFromJsonAsync<RespuestaProductora>();
        creada.ShouldNotBeNull();
        creada.Comunidad.ShouldBe("Las Nieves");
        creada.CatAsignado.ShouldBe("PAT");
    }

    [Fact]
    public async Task UnaComunidadInexistenteSeSigueRechazando()
    {
        // La guarda retirada también cubría este caso de rebote: devolvía 403
        // cuando la comunidad no existía. Al quitarla, quien lo rechaza es
        // ProductoraService, y hay que asegurarse de que sigue habiendo un
        // rechazo limpio y no un 500 de la clave foránea.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaDos,
                comunidadId = 99999,
                catAsignado = "PAT",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
```

- [ ] **Step 2: Ejecutar y ver que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcanceProductorasTests"
```

Esperado: fallan las dos nuevas — `OperadorCat_creaProductoraDeCualquierComunidad`
recibe 403 en vez de 201, y `UnaComunidadInexistenteSeSigueRechazando` recibe
403 en vez de 404. Las demás de la clase, en verde.

- [ ] **Step 3: Retirar las dos guardas del controlador**

En `Features/Productoras/Controllers/ProductorasController.cs`, borrar de
`Crear` el bloque completo:

```csharp
        // La comunidad también entra en el alcance: sin esta comprobación, el
        // operador de PAT registraría productoras de Las Nieves con el sello
        // "PAT", ensuciando el catálogo de otro centro.
        var catComunidad = await service.CatDeComunidadAsync(dto.ComunidadId);
        if (User.ComunidadFueraDeAlcance(catComunidad))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede registrar productoras de " +
                          "las comunidades de su centro de acopio."
            });
```

Y de `Actualizar` el bloque completo:

```csharp
        // Y alcance sobre el destino: una edición no puede ser la puerta por
        // la que una productora sale del centro de quien la edita.
        var catComunidad = await service.CatDeComunidadAsync(dto.ComunidadId);
        if (User.ComunidadFueraDeAlcance(catComunidad))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "No puedes mover una productora a una comunidad " +
                          "de otro centro de acopio."
            });
```

**No tocar** el sellado del CAT con el token (`if (User.CatRestringido() is
string catOperador …)`) ni la guarda `User.FueraDeAlcance(actual.CatAsignado)`
de `Actualizar`: esas dos son el alcance que sí se conserva.

- [ ] **Step 4: Ejecutar y ver que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcanceProductorasTests"
```

Esperado: toda la clase en verde. Si `UnaComunidadInexistenteSeSigueRechazando`
no da 404, **parar y revisar** cómo mapea el manejador de excepciones el
`KeyNotFoundException` de `ProductoraService` — no ajustar la prueba al
resultado.

- [ ] **Step 5: Borrar el código que quedó muerto**

Comprobar que no queda ningún uso:

```bash
grep -rn "ComunidadFueraDeAlcance\|CatDeComunidadAsync" --include=*.cs . | grep -v "/bin/\|/obj/"
```

Esperado: solo las declaraciones. Entonces borrar:
- `ComunidadFueraDeAlcance` completo, con su docstring, de `Common/Auth/AlcanceUsuario.cs`
- `CatDeComunidadAsync` de `IProductoraService` y su implementación en `ProductoraService`

Volver a ejecutar el `grep`. Esperado: **sin salida**.

- [ ] **Step 6: Batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 228, Failed: 0` (227 + 1 prueba nueva neta).

- [ ] **Step 7: Commit**

```bash
git add Features/ Common/ tests/
git commit -m "feat: la productora puede ser de cualquier comunidad

La comunidad es donde vive y el CAT es donde entrega: en el piloto no
siempre coinciden. Comunidad.CatReferencia pasa a ser informativo. El
centro lo sigue sellando el token, que es la restriccion que importa.

La prueba que afirmaba lo contrario se reescribe en vez de borrarse: una
prueba borrada deja un hueco silencioso, una reescrita deja constancia
del cambio de criterio.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: El formulario ofrece todas las comunidades

**Files:**
- Modify: `src/components/productoras/FormProductora.tsx` (repo del front)

**Interfaces:**
- Consumes: el API de la Tarea 5, ya sin el 403.
- Produces: nada.

- [ ] **Step 1: Crear la rama del front**

```bash
git checkout -b feat/correcciones-puntuales origin/main
```

- [ ] **Step 2: Dejar de filtrar el catálogo por CAT**

Sustituir el bloque de `comunidadesVisibles` y su comentario. Antes:

```tsx
    // El operador de CAT queda fijado a su centro. Solo se le ofrecen las
    // comunidades de ese centro: el servidor rechaza las demás con 403, así
    // que mostrarlas solo serviría para que eligiera algo que va a fallar.
    const catFijo = auth.rol === "OperadorCAT" ? auth.catAsignado : null;
```

Después:

```tsx
    // El operador de CAT queda fijado a su centro, pero NO a las comunidades
    // de ese centro: la comunidad es dónde vive la productora y el CAT es
    // dónde entrega, y en el piloto no siempre coinciden. El servidor dejó de
    // rechazar las demás en 2026-08.
    const catFijo = auth.rol === "OperadorCAT" ? auth.catAsignado : null;
```

Y eliminar `comunidadesVisibles` por completo, junto con el `useMemo` y —si
queda sin uso— el import de `useMemo`:

```tsx
    const comunidadesVisibles = useMemo(
        () => catFijo
            ? comunidades.filter((c) => c.catReferencia === catFijo)
            : comunidades,
        [comunidades, catFijo]);
```

- [ ] **Step 3: Que elegir comunidad no pise el CAT del token**

Sustituir `elegirComunidad`. Antes:

```tsx
    // Al elegir comunidad del catálogo se propone su CAT de referencia
    const elegirComunidad = (id: number) => {
        const c = comunidadesVisibles.find((x) => x.id === id);
        setForm({
            ...form,
            comunidadId: id,
            catAsignado: c?.catReferencia ?? form.catAsignado,
        });
    };
```

Después:

```tsx
    // Al elegir comunidad se propone su CAT de referencia, PERO nunca por
    // encima del CAT del token: para un operador de CAT ese campo está
    // sellado, y el servidor lo va a sobrescribir de todos modos.
    const elegirComunidad = (id: number) => {
        const c = comunidades.find((x) => x.id === id);
        setForm({
            ...form,
            comunidadId: id,
            catAsignado: catFijo ?? c?.catReferencia ?? form.catAsignado,
        });
    };
```

- [ ] **Step 4: Repuntar el `select` y su mensaje de vacío**

Sustituir `comunidadesVisibles.map` por `comunidades.map`, y el bloque de
mensaje vacío. Antes:

```tsx
                        {comunidadesVisibles.length === 0 && (
                            <p className="mt-1 text-xs text-teja-700">
                                {catFijo
                                    ? "Tu centro de acopio no tiene comunidades en el " +
                                      "catálogo. Pide a un administrador que registre una."
                                    : "No hay comunidades en el catálogo. Crea una en " +
                                      "Administración antes de registrar productoras."}
                            </p>
                        )}
```

Después:

```tsx
                        {comunidades.length === 0 && (
                            <p className="mt-1 text-xs text-teja-700">
                                No hay comunidades en el catálogo. Crea una en
                                Administración antes de registrar productoras.
                            </p>
                        )}
```

El mensaje que hablaba del centro del operador desaparece: ya no puede darse
ese caso, porque el catálogo no se filtra.

- [ ] **Step 5: Verificar**

```bash
pnpm lint
```

Esperado: salida 0, sin avisos de import sin usar (si `useMemo` quedó
huérfano, quitarlo del import).

```bash
pnpm exec tsc -b
```

Esperado: salida 0.

```bash
pnpm build
```

Esperado: salida 0.

- [ ] **Step 6: Comprobación manual**

Arrancar el front, entrar como operador de PAT y abrir «Nueva productora».
Comprobar los tres puntos:
1. El desplegable de comunidad lista **todas** las del catálogo, incluidas las
   de otros cantones.
2. Al elegir una de otro cantón, el **cantón** se actualiza y sigue siendo de
   solo lectura.
3. El **centro de acopio** sigue mostrando PAT y sigue deshabilitado.

- [ ] **Step 7: Commit**

```bash
git add src/components/productoras/FormProductora.tsx
git commit -m "feat: el alta ofrece todas las comunidades, con el CAT sellado

Se bloquea el canton y el centro; se libera la comunidad. El servidor
dejo de responder 403 a las comunidades de otro CAT, asi que filtrarlas
aqui solo escondia opciones validas.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 4 · La página pública del QR

### Task 7: El QR público deja de exponer el detalle por animal

**Files:**
- Test: `tests/CoopagcuyApi.Tests/Integracion/PaginaPublicaTests.cs` (crear)
- Modify: `Features/QR/DTOs/QRDtos.cs`
- Modify: `Features/QR/Services/QRService.cs`
- Modify: `src/pages/QRPublico.tsx` (front)
- Modify: `src/types/faenamiento.ts` (front)

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `PaginaPublicaDto` sin `ObservacionesProceso` ni `DetalleCuyes`;
  `CuyPublicoDto` deja de existir.

- [ ] **Step 1: Escribir la prueba que falla**

Crear `tests/CoopagcuyApi.Tests/Integracion/PaginaPublicaTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La página que abre el consumidor al escanear el QR es anónima: cualquiera
/// con el código ve su contenido. El detalle animal por animal y las
/// observaciones del proceso salieron de ahí — no aportan al consumidor y son
/// datos de producción sobre una comunidad identificable.
///
/// Esto sí es comprobable de verdad: es JSON, no un PDF.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class PaginaPublicaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";
    private const string CodigoFaenado = "FAE-20260818-001";

    [Fact]
    public async Task NoExponeElDetalleAnimalPorAnimal()
    {
        await SembrarPaginaAsync();

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{CodigoFaenado}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Aserción sobre el cuerpo y no sobre el tipo de C#: lo que importa es
        // lo que sale por el cable.
        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("detalleCuyes", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("observacionesProceso", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task SigueDiciendoSiElLoteTuvoNovedad()
    {
        // conNovedad se calcula A PARTIR de detalleCuyes. Borrar la variable
        // al quitar el campo del DTO rompería el indicador sin que se note al
        // leer el diff: este lote lleva un animal con novedad a propósito.
        await SembrarPaginaAsync();

        var respuesta = await api.ComoAnonimo()
            .GetAsync($"/api/qr/publico/{CodigoFaenado}");

        var json = await respuesta.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("estadoCalidad").GetString()
            .ShouldBe("ConNovedad");
        doc.RootElement.GetProperty("estadoCanal").GetString()
            .ShouldBe("ConNovedad");
    }

    /// Lote faenado con QR activo y dos animales, uno de ellos con novedad.
    private async Task SembrarPaginaAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT, comunidadId: 1);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-20260818-001",
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 2,
            PesoTotalGramos = 2600,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Operadora de prueba"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var loteFaenado = new LoteFaenado
        {
            Codigo = CodigoFaenado,
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba"
        };
        db.LotesFaenados.Add(loteFaenado);
        await db.SaveChangesAsync();

        var sesion = new RegistroFaenamiento
        {
            LoteId = lote.Id,
            LoteFaenadoId = loteFaenado.Id,
            NumeroSesion = 1,
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba",
            UnidadesFaenadas = 2,
            PesoTotalCanalGramos = 1200,
            EstadoCanal = EstadoCanal.ConNovedad
        };
        db.Faenamientos.Add(sesion);
        await db.SaveChangesAsync();

        db.CuyFaenamientos.AddRange(
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 1,
                PesoCanalGramos = 600, Estado = EstadoCanal.Apto
            },
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 2,
                PesoCanalGramos = 600, Estado = EstadoCanal.ConNovedad,
                Motivo = "hematoma en el costado"
            });

        // Sin un QR activo, ObtenerPaginaPublicaAsync devuelve null y el
        // endpoint responde 404.
        db.CodigosQR.Add(new CodigoQR
        {
            LoteFaenadoId = loteFaenado.Id,
            UrlPublica = $"https://localhost/qr/{CodigoFaenado}",
            Activo = true
        });

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Ejecutar y ver que falla la primera**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~PaginaPublicaTests"
```

Esperado: `NoExponeElDetalleAnimalPorAnimal` **falla** (los dos campos siguen
ahí) y `SigueDiciendoSiElLoteTuvoNovedad` **pasa**. Si la segunda falla, el
sembrado no está produciendo la novedad — arreglar el sembrado antes de seguir.

- [ ] **Step 3: Quitar los dos campos del DTO**

En `Features/QR/DTOs/QRDtos.cs`, borrar de `PaginaPublicaDto`:

```csharp
    // Novedades registradas en planta sobre los animales faenados
    List<string> ObservacionesProceso,
```

```csharp
    // Estado individual de cada animal faenado
    List<CuyPublicoDto> DetalleCuyes
```

Cuidar la coma del último parámetro que quede (`ComunidadesAporte` pasa a ser
el último). Y borrar el record `CuyPublicoDto` completo.

- [ ] **Step 4: Ajustar `QRService`**

En `ObtenerPaginaPublicaAsync`, borrar las dos líneas del `return`:

```csharp
            ObservacionesProceso: observacionesProceso,
```
```csharp
            DetalleCuyes: detalleCuyes
```

Borrar el cálculo de `observacionesProceso`, que ya no lo usa nadie.

**Conservar `detalleCuyes`**, cambiando su proyección a un tipo anónimo, porque
`CuyPublicoDto` deja de existir y `conNovedad` depende de él:

```csharp
        // Ya no se expone —salió de la página pública en 2026-08— pero el
        // indicador de novedad se calcula a partir de aquí. Borrar esta
        // variable rompería `estadoCalidad` sin que se note al leer el diff.
        var detalleCuyes = animales
            .Select(a => new
            {
                Estado = a.Faenado.Estado == EstadoCanal.ConNovedad
                    ? "Con novedad" : "Apto"
            })
            .ToList();
```

El `OrderBy`/`ThenBy` se puede quitar: solo servía para el orden de impresión.

- [ ] **Step 5: Ejecutar y ver que pasan las dos**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~PaginaPublicaTests"
```

Esperado: `Passed: 2, Failed: 0`.

- [ ] **Step 6: Comprobar por mutación**

Sustituir `var conNovedad = detalleCuyes.Any(c => c.Estado != "Apto");` por
`var conNovedad = false;`. Esperado: falla `SigueDiciendoSiElLoteTuvoNovedad`.
**Restaurar.**

- [ ] **Step 7: Batería completa del API y commit**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 230, Failed: 0`.

```bash
git add Features/QR/ tests/
git commit -m "feat: la pagina publica del QR deja de exponer el detalle animal

Es un endpoint anonimo: cualquiera con el codigo lo lee. El detalle
animal por animal y las observaciones del proceso no le aportan nada al
consumidor y son datos de produccion de una comunidad identificable.

El calculo de conNovedad se conserva: depende de esa proyeccion, y
borrarla habria roto el indicador sin que se notara en el diff.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Quitar las dos tarjetas del front**

En `src/pages/QRPublico.tsx`, borrar los dos bloques completos: el
`{data.detalleCuyes.length > 0 && ( … )}` con su `<Tarjeta titulo="Detalle de
los animales">`, y el `{data.observacionesProceso.length > 0 && ( … )}` con su
`<Tarjeta titulo="Observaciones del proceso">`.

En `src/types/faenamiento.ts`, borrar del tipo de la página pública:

```ts
    observacionesProceso: string[];
```

y el bloque completo:

```ts
    detalleCuyes: {
```
…hasta su llave de cierre.

- [ ] **Step 9: Verificar el front**

```bash
pnpm lint
```

Esperado: salida 0.

```bash
pnpm exec tsc -b
```

Esperado: salida 0. Si señala un `Tarjeta` importado y sin usar, comprobar
primero si otras tarjetas de la página lo siguen usando antes de quitar el
import.

```bash
pnpm build
```

Esperado: salida 0.

- [ ] **Step 10: Commit del front**

```bash
git add src/pages/QRPublico.tsx src/types/faenamiento.ts
git commit -m "feat: la pagina del QR ya no muestra el detalle de los animales

Acompana al cambio del API, que dejo de enviar esos dos campos.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fase 5 · Las dos verificaciones

### Task 8: Fijar el alcance por CAT en los pagos

**Files:**
- Test: `tests/CoopagcuyApi.Tests/Integracion/AlcancePagosTests.cs` (crear)

**Interfaces:**
- Consumes: `Sembrador.PagoConNovedadAsync` (Tarea 4).
- Produces: nada. **No se modifica código de producción en esta tarea.**

**Contexto:** el filtro ya existe y funciona — `IPagoService` recibe un
`CentroAcopio? filtroCat` en las cinco operaciones. Lo que falta es que alguien
lo haya fijado. Si alguna de estas pruebas falla, **es un hallazgo**: parar y
reportarlo, no ajustar la prueba.

- [ ] **Step 1: Escribir las pruebas**

Crear `tests/CoopagcuyApi.Tests/Integracion/AlcancePagosTests.cs`:

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
/// Una operadora de CAT ve y registra los pagos de SU centro. El filtro ya
/// estaba implementado —IPagoService lo recibe en las cinco operaciones— pero
/// nadie lo había fijado más allá de la descarga del ticket. Estas pruebas son
/// el entregable: no acompañan a un cambio, lo verifican.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AlcancePagosTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaPat = "0104576277";
    private const string CedulaNie = "0102030405";

    private sealed record FilaPago(int Id, int ProductoraId, decimal MontoUsd);

    [Fact]
    public async Task ElListadoNoTraeLosPagosDeOtroCentro()
    {
        var (pagoPat, _) = await Sembrador.PagoConNovedadAsync(api, CedulaPat, 120m);

        // Una productora de NIE con su propio pago, creado por su operadora.
        var productoraNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);
        var pagoNie = await PagoDirectoAsync(productoraNie.Id, CentroAcopio.NIE);

        var respuesta = await api.ComoOperadorCat("PAT").GetAsync("/api/pagos");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaPago>>();
        filas.ShouldNotBeNull();

        filas.Select(f => f.Id).ShouldContain(pagoPat);
        filas.Select(f => f.Id).ShouldNotContain(pagoNie);
    }

    [Fact]
    public async Task NoSePuedeRegistrarUnPagoAUnaProductoraDeOtroCentro()
    {
        var productoraNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        int loteNie;
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = "NIE-20260818-001",
                ProductoraId = productoraNie.Id,
                CentroAcopio = CentroAcopio.NIE,
                CantidadAnimales = 1,
                PesoTotalGramos = 1300,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
            loteNie = lote.Id;
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productoraNie.Id,
                loteId = loteNie,
                montoUsd = 90m,
                responsable = "Operadora de PAT"
            });

        respuesta.StatusCode.ShouldNotBe(HttpStatusCode.Created);
        respuesta.StatusCode.ShouldNotBe(HttpStatusCode.OK);

        await using var db2 = api.NuevoDbContext();
        (await db2.Pagos.AnyAsync(p => p.ProductoraId == productoraNie.Id))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task LosLotesPendientesDeOtroCentroNoSeConsultan()
    {
        var productoraNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/lotes-pendientes/{productoraNie.Id}");

        // Da igual si el servidor lo resuelve con 403, con 404 o con una lista
        // vacía: lo que no puede es devolver los lotes de otro centro.
        if (respuesta.StatusCode == HttpStatusCode.OK)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            cuerpo.ShouldBe("[]");
        }
        else
        {
            respuesta.StatusCode.ShouldBeOneOf(
                HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        }
    }

    /// Pago de una productora, creado por la operadora de su propio centro.
    private async Task<int> PagoDirectoAsync(int productoraId, CentroAcopio cat)
    {
        int loteId;
        await using (var db = api.NuevoDbContext())
        {
            var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
            {
                CodigoLote = $"{cat}-20260818-002",
                ProductoraId = productoraId,
                CentroAcopio = cat,
                CantidadAnimales = 1,
                PesoTotalGramos = 1300,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado
            };
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
            loteId = lote.Id;
        }

        var respuesta = await api.ComoOperadorCat(cat.ToString())
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId,
                loteId,
                montoUsd = 90m,
                responsable = $"Operadora de {cat}"
            });
        respuesta.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productoraId)
            .Select(p => p.Id)
            .FirstAsync();
    }
}
```

- [ ] **Step 2: Ejecutar**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcancePagosTests"
```

Esperado: `Passed: 3, Failed: 0` **a la primera**, porque el filtro ya existe.

Si alguna falla por el alcance —un pago de otro centro que sí aparece, un
registro que sí se acepta— **parar: es un hallazgo de seguridad y hay que
reportarlo antes de seguir.** No relajar la aserción.

- [ ] **Step 3: Comprobar por mutación que las pruebas sirven**

En `PagosController`, hacer que `FiltroCat()` devuelva siempre `null`. Volver a
ejecutar. Esperado: fallan `ElListadoNoTraeLosPagosDeOtroCentro` y
`NoSePuedeRegistrarUnPagoAUnaProductoraDeOtroCentro`. **Restaurar.**

Sin este paso no se sabe si las pruebas verifican algo o simplemente pasan.

- [ ] **Step 4: Commit**

```bash
git add tests/CoopagcuyApi.Tests/Integracion/AlcancePagosTests.cs
git commit -m "test: fija que una operadora solo ve y registra pagos de su CAT

El filtro por centro ya estaba implementado en las cinco operaciones de
IPagoService, pero solo estaba verificado en la descarga del ticket.
Comprobado por mutacion: con FiltroCat devolviendo null, dos de las tres
se ponen rojas.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Fijar la resolución offline por cédula

**Files:**
- Test: `tests/CoopagcuyApi.Tests/Integracion/ResolucionPorCedulaTests.cs` (crear)

**Interfaces:**
- Consumes: `Sembrador.ProductoraAsync`.
- Produces: nada. **No se modifica código de producción en esta tarea.**

**Contexto — la respuesta es más fuerte de lo que decía el spec.** No es solo
que el nombre no entre en el `Where` de `ResolverProductoraPorCedulaAsync`: es
que **`RegistrarEntregaDto` no tiene ningún campo de nombre**. Solo
`CedulaProductora`. El nombre que la operadora vea o escriba en la tablet no
llega al servidor por ninguna vía, así que no puede desviar nada. No hay ni una
prueba que lo diga.

**Contrato del endpoint, ya verificado:**
- Ruta: `POST /api/recepcion/sync-entregas`
- `SyncEntregasDto`: `DispositivoId` (raíz) + `Entregas` (lista de `RegistrarEntregaDto`)
- `RegistrarEntregaDto` en la vía offline necesita `FechaCapturaOffline`, que es
  obligatoria ahí porque la entrega pudo capturarse días antes de recuperar señal.

- [ ] **Step 1: Escribir las dos pruebas**

Crear `tests/CoopagcuyApi.Tests/Integracion/ResolucionPorCedulaTests.cs`:

```csharp
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Sin señal el operador no tiene catálogo y captura la entrega por cédula. La
/// pregunta que motiva estas pruebas: si la cédula coincide con una productora
/// real pero el nombre está mal escrito, ¿a quién se le asigna el lote?
///
/// A la de la cédula — y por un motivo más fuerte que "el nombre no entra en
/// la búsqueda": RegistrarEntregaDto NO TIENE campo de nombre. Lo que la
/// operadora vea o escriba en la tablet no viaja al servidor, así que no puede
/// desviar nada. Estas pruebas fijan esa conducta, que no tenía ninguna.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ResolucionPorCedulaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task LaCedulaAsignaElLoteAunqueLaTabletNoSepaElNombre()
    {
        // La productora se siembra con el nombre "Productora 0104576277". La
        // entrega viaja SOLO con la cédula: es todo lo que el DTO admite.
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        await SincronizarAsync(cedula: Cedula, centro: "PAT");

        await using var db = api.NuevoDbContext();

        // El animal quedó con la productora de la cédula, no en cuarentena.
        var cuyes = await db.CuyRegistros
            .Where(c => c.ProductoraId == productora.Id)
            .CountAsync();
        cuyes.ShouldBe(1);

        var enCuarentena = await db.EntregasPendientesVinculacion
            .AnyAsync(v => v.Cedula == Cedula);
        enCuarentena.ShouldBeFalse();
    }

    [Fact]
    public async Task UnaCedulaDeOtroCentroVaALaBandejaDeVinculacion()
    {
        // La cédula es válida y la productora existe, pero pertenece a NIE y
        // la entrega se capturó en PAT. Queda en cuarentena para que un
        // administrador la resuelva. Es lo correcto —el lote es de un centro y
        // la productora de otro— pero no estaba escrito en ningún lado.
        await Sembrador.ProductoraAsync(api, Cedula, CentroAcopio.NIE,
            comunidadId: 2);

        await SincronizarAsync(cedula: Cedula, centro: "PAT");

        await using var db = api.NuevoDbContext();

        var pendiente = await db.EntregasPendientesVinculacion
            .FirstOrDefaultAsync(v => v.Cedula == Cedula);

        pendiente.ShouldNotBeNull();
        pendiente.Estado.ShouldBe(EstadoVinculacion.Pendiente);
        pendiente.CentroAcopio.ShouldBe(CentroAcopio.PAT);

        // Y NO se coló como entrega de la productora de NIE.
        var cuyes = await db.CuyRegistros.CountAsync();
        cuyes.ShouldBe(0);
    }

    /// Una entrega de un cuy capturada sin conexión, identificada solo por
    /// cédula. FechaCapturaOffline es obligatoria en esta vía: la entrega pudo
    /// capturarse días antes de recuperar señal.
    private async Task SincronizarAsync(string cedula, string centro)
    {
        var respuesta = await api.ComoOperadorCat(centro)
            .PostAsJsonAsync("/api/recepcion/sync-entregas", new
            {
                dispositivoId = "tablet-de-prueba",
                entregas = new[] { new
                {
                    idCliente = Guid.NewGuid().ToString(),
                    dispositivoId = "tablet-de-prueba",
                    centroAcopio = centro,
                    productoraId = 0,
                    cedulaProductora = cedula,
                    fechaCapturaOffline = DateTime.UtcNow.AddHours(-2),
                    enAyunas = true,
                    responsableRecepcion = "Operadora de prueba",
                    sincronizadoOffline = true,
                    cuyes = new[] { new
                    {
                        pesoGramos = 1300m,
                        colorPelaje = "Blanco",
                        estadoOreja = "Blanda",
                        tamanoAnimal = "Normal"
                    }}
                }}
            });

        // El sync responde 200 con un resultado POR entrega: una entrega que
        // va a cuarentena no es un fallo HTTP.
        respuesta.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Ejecutar**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ResolucionPorCedulaTests"
```

Esperado: `Passed: 2, Failed: 0`.

Si `LaCedulaAsignaElLoteAunqueLaTabletNoSepaElNombre` falla porque el lote acabó
en cuarentena o en otra productora, **parar: es un hallazgo real** y hay que
reportarlo, porque contradice lo que dice el spec.

- [ ] **Step 3: Comprobar por mutación**

En `ResolverProductoraPorCedulaAsync`, quitar `dto.ProductoraId = productora.Id;`
del bloque `if (productora is not null)`. Volver a ejecutar. Esperado: falla
`LaCedulaAsignaElLoteAunqueLaTabletNoSepaElNombre` — la entrega acaba en la
bandeja de vinculación pese a que la cédula sí existía. **Restaurar.**

Luego quitar `&& p.CatAsignado == dto.CentroAcopio` del `Where`. Esperado: falla
`UnaCedulaDeOtroCentroVaALaBandejaDeVinculacion` — una entrega capturada en PAT
se adjudicaría a una productora de NIE, que es una fuga entre centros.
**Restaurar.**

- [ ] **Step 4: Batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Passed: 235, Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add tests/CoopagcuyApi.Tests/Integracion/ResolucionPorCedulaTests.cs
git commit -m "test: fija que la cedula manda en la sincronizacion offline

Un nombre mal escrito en la tablet no puede desviar el lote, y por un
motivo mas fuerte que "el nombre no entra en la busqueda":
RegistrarEntregaDto no tiene campo de nombre, asi que ese dato no viaja
al servidor. Tambien queda fijado que una cedula de otro centro va a la
bandeja de vinculacion en vez de colarse.

Comprobado por mutacion las dos.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Cierre

- [ ] **Batería completa del API en verde**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: `Failed: 0`, con 235 pruebas (211 de partida + 24 nuevas netas).

- [ ] **Front verificado**

```bash
pnpm lint && pnpm exec tsc -b && pnpm build
```

- [ ] **Verificación manual obligatoria**

Ninguna prueba puede cubrir esto — el PDF es binario y su maquetación no se
puede afirmar desde código:

1. **Imprimir un ticket pendiente.** Debe salir idéntico a los de antes, y con
   la hora del CAT, no cinco horas adelantada.
2. **Pagar ese ticket con dos descuentos** de descripciones largas y volver a
   imprimirlo. Comprobar: el subtotal, las dos líneas `Cuy #N · Tipo`, las
   descripciones **enteras y envueltas, sin cortar**, y el total pagado en
   grande.
3. **Imprimir una guía de movilización** y comprobar la hora de emisión, la de
   recepción y la de despacho.
4. **Escanear un QR** y comprobar que ya no aparecen «Detalle de los animales»
   ni «Observaciones del proceso», y que el indicador de novedad sigue siendo
   correcto.
5. **Registrar una productora** de una comunidad de otro cantón desde un
   operador de CAT.

- [ ] **Abrir los dos PR**, el del API primero: el front consume el DTO
      adelgazado del QR y fallaría en tiempo de ejecución contra un API que
      todavía envíe los campos viejos… pero al revés no: el front viejo contra
      el API nuevo dejaría de pintar las tarjetas sin romperse. **El API va
      primero de todos modos**, por coherencia con el orden del ciclo de pago.

## Lo que este plan deja fuera a propósito

- **El código de lote sigue componiéndose desde la fecha UTC**
  (`GenerarCodigoLoteAsync`). Una jaula recibida a las 20:00 del 21 se llama
  `PAT-20260822-001`. Es el mismo fallo de fondo que la hora de los documentos,
  pero toca identificadores y el contador secuencial. Deuda registrada en el
  spec, decisión aparte.
- **Los mensajes de error de `RecepcionService` sobre la fecha de captura.** Uno
  ya rotula «UTC» explícitamente y el otro compara días.
- **Los otros cuatro proyectos** del pedido original: venta local, reportes de
  ganancias, trazabilidad del transporte y retención a 90 días. Cada uno con su
  propio spec.

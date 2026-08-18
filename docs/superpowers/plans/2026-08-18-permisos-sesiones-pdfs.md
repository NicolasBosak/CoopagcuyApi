# Permisos por rol, sesiones por dispositivo y guías PDF — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usa superpowers:subagent-driven-development
> (recomendado) o superpowers:executing-plans para implementar este plan tarea a
> tarea. Los pasos usan casillas (`- [ ]`) para el seguimiento.

**Objetivo:** acotar el admin técnico a soporte, dar al operador de CAT la
gestión de las productoras de su centro, mostrar el dispositivo en la pantalla
de sesiones y evitar sus duplicados, corregir dos defectos de la guía de
movilización, poner el logo en los PDF, y averiguar por qué el reporte de
Salida no refleja los despachos nuevos.

**Arquitectura:** las reglas de alcance por usuario viven en un único sitio
(`Common/Auth/AlcanceUsuario.cs`), y los controladores las invocan; nunca se
reimplementa la comprobación en línea. Las restricciones de rol se aplican
siempre en la API y, solo después, se reflejan en la navegación del front:
ocultar un enlace no protege un endpoint. La marca de los PDF se resuelve con
un recurso embebido en el ensamblado, leído una sola vez y cacheado en memoria.

**Stack:** .NET 8 · EF Core 8 + Npgsql · QuestPDF 2024.3.1 · ClosedXML ·
xUnit + Shouldly + Respawn · React 19 + TypeScript + Vite + TanStack Query.

**Especificación:** [`docs/superpowers/specs/2026-08-18-permisos-sesiones-pdfs-design.md`](../specs/2026-08-18-permisos-sesiones-pdfs-design.md)

## Restricciones globales

- **Las pruebas NO se corren con `dotnet test` en Windows.** Smart App Control
  bloquea la carga del DLL desde OneDrive (error `0x800711C7`). Toda la
  batería corre en contenedor. Comando de una sola clase:

  ```bash
  docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~NombreDeLaClase"
  ```

  Se apunta al `.csproj`, **nunca** al `.slnx`: el contenedor usa `sdk:8.0` y
  el formato `.slnx` exige 9.0.200 o superior (falla con `MSB4068`).
  La primera corrida tarda varios minutos descargando NuGet; no la mates.
- **Docker Desktop debe estar en marcha** antes de cualquier tarea con pruebas.
- Las clases de prueba comparten una `ApiFactory` (ver `ColeccionApi`) y **no
  corren en paralelo**. Toda clase nueva lleva `[Collection(ColeccionApi.Nombre)]`
  y llama a `api.LimpiarAsync()` en `InitializeAsync`.
- **Respawn trunca SIN RESTART IDENTITY:** ninguna prueba puede asumir que la
  primera fila sembrada tenga `Id` 1. Usa siempre el `Id` que devuelve el
  sembrador.
- Aserciones con **Shouldly** (`ShouldBe`), no FluentAssertions.
- **El repo del frontend no tiene marco de pruebas.** Sus scripts son `dev`,
  `build`, `lint`, `preview`. La verificación de las tareas de front es
  `pnpm build` (compila TypeScript con `tsc -b`) más `pnpm lint`. La garantía
  de comportamiento de los permisos vive en las pruebas de la API.
- Ruta del frontend: `C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`.
  Gestor de paquetes: **pnpm**.
- Comentarios y nombres en **español**, siguiendo el estilo del repo: los
  comentarios explican *por qué*, no *qué*.
- Las comunidades están sembradas con `HasData` y sus `Id` son estables:
  **1** Patococha (PAT) · **2** Las Nieves (NIE) · **3** Huertas (HUE) ·
  **4** Nabón / El Progreso (NAB) · **5** Pelincay (PEL).

## Alcance de este plan

Cubre la Fase 0 (diagnóstico) y las Fases 1 a 3 de la especificación.

**La Fase 4 no está aquí y no puede estarlo:** su contenido depende de lo que
revele la Tarea 1. Cuando esa tarea entregue su hallazgo, se escribe un plan
aparte para el arreglo. No inventes el arreglo antes de tener la evidencia.

## Mapa de archivos

### API — `CoopagcuyApi`

| Archivo | Responsabilidad | Tareas |
|---|---|---|
| `Common/Auth/AlcanceUsuario.cs` | Definición única del alcance por usuario. Gana la comprobación de comunidad. | 4 |
| `Common/Auth/DescripcionDispositivo.cs` | **Nuevo.** Función pura `UserAgent` → texto legible. | 8 |
| `Common/Auth/AuthDtos.cs` | `SesionActivaDto` gana el campo `Dispositivo`. | 8 |
| `Common/Auth/SesionService.cs` | Una sesión por dispositivo; rellena `Dispositivo`. | 8, 9 |
| `Common/Branding/BrandingAssets.cs` | **Nuevo.** Lee el logo embebido una sola vez. | 12 |
| `Common/Branding/coopagcuy-logo.png` | **Nuevo.** Recurso embebido. | 12 |
| `Features/Recepcion/Controllers/RecepcionController.cs` | Quitar `AdminTecnico` salvo en vinculaciones. | 2 |
| `Features/Faenamiento/Controllers/FaenamientoController.cs` | Quitar `AdminTecnico`. | 2 |
| `Features/Productoras/Controllers/PagosController.cs` | Quitar `AdminTecnico`. | 2 |
| `Features/Reportes/Controllers/ReportesController.cs` | Quitar `AdminTecnico` del flujo operativo; pasar el indicador al Excel general. | 2, 3 |
| `Features/Reportes/Services/ReportesService.cs` | `ExportarExcelGeneralAsync` omite hojas del flujo. | 3 |
| `Features/Productoras/Controllers/ProductorasController.cs` | Alcance del operador de CAT en las cuatro operaciones. | 2, 4, 5 |
| `Features/Productoras/Services/ProductoraService.cs` | Consulta del CAT de una comunidad. | 4 |
| `Features/Recepcion/Services/GuiaMovilizacionService.cs` | Los dos defectos de la guía + logo. | 11, 12 |
| `CoopagcuyApi.csproj` | Declarar el `EmbeddedResource`. | 12 |

### Pruebas — `tests/CoopagcuyApi.Tests`

| Archivo | Tareas |
|---|---|
| `Infra/Sembrador.cs` | Gana `ProductoraAsync` y `DespachoAsync`. | 1, 4 |
| `Integracion/ReporteSalidaTests.cs` | **Nuevo.** Diagnóstico. | 1 |
| `Integracion/AutorizacionAdminTests.cs` | Amplía el admin técnico acotado. | 2, 3 |
| `Integracion/AlcanceProductorasTests.cs` | **Nuevo.** Operador de CAT. | 4, 5 |
| `Integracion/SesionesPorDispositivoTests.cs` | **Nuevo.** | 9 |
| `Unitarias/DescripcionDispositivoTests.cs` | **Nuevo.** | 8 |
| `Integracion/GuiaMovilizacionTests.cs` | **Nuevo.** | 11, 12 |

### Frontend — `coopagcuy-frontend`

| Archivo | Responsabilidad | Tareas |
|---|---|---|
| `src/components/layout/MainLayout.tsx` | Menú por rol. | 6 |
| `src/App.tsx` | Rutas por rol. | 6 |
| `src/pages/Reportes.tsx` | Pestañas por rol y pestaña inicial. | 6 |
| `src/pages/Productoras.tsx` | `puedeGestionar` en vez de `esAdmin`. | 7 |
| `src/components/productoras/FormProductora.tsx` | Comunidades y CAT fijados al operador. | 7 |
| `src/pages/Sesiones.tsx` | Muestra dispositivo, ID corto e IP. | 10 |
| `src/api/auth.ts` | `SesionActiva` gana `dispositivo`. | 10 |

---

## Tarea 1: Diagnóstico del reporte de Salida (Fase 0)

Esta tarea **no arregla nada**. Su entregable es una prueba que aísla el fallo y
un hallazgo escrito. Si la prueba pasa a la primera, eso también es información
valiosa: significa que la consulta del reporte es correcta y el problema está en
lo que se guardó.

**Archivos:**
- Modificar: `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`
- Crear: `tests/CoopagcuyApi.Tests/Integracion/ReporteSalidaTests.cs`

**Interfaces:**
- Produce: `Sembrador.DespachoAsync(ApiFactory api, DateTime fechaDespacho, string cliente = "Cliente de prueba")` → `Task<Despacho>`

- [ ] **Paso 1: Leer la base de datos real**

Esto es lo primero y no requiere código. Conéctate a la base del entorno donde
el usuario registró los despachos y ejecuta:

```sql
SELECT "Id", "FechaDespacho", "ClienteDestino", "LoteFaenadoId", "LoteId"
FROM "Despachos"
ORDER BY "Id" DESC
LIMIT 10;
```

Anota el resultado literal. Es la evidencia sobre la que descansa todo lo demás.

- [ ] **Paso 2: Añadir el sembrador de despachos**

Inserta un `Despacho` **directamente**, sin pasar por `RegistrarDespachoAsync`.
Eso es deliberado: aísla la consulta del reporte del camino de registro. Si un
despacho insertado a mano con fecha de hoy sí aparece en el reporte, la consulta
está sana y el fallo está aguas arriba.

Añade a `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`:

```csharp
    /// <summary>
    /// Inserta un despacho directamente en la base, sin pasar por
    /// RegistrarDespachoAsync. Aísla la consulta del reporte del camino de
    /// registro: si el reporte encuentra este despacho pero no los de
    /// producción, el fallo no está en la consulta.
    ///
    /// LoteFaenadoId y LoteId quedan nulos a propósito: ReporteSalidaAsync
    /// contempla ese caso y devuelve "—" como código de lote, así que no hace
    /// falta montar toda la cadena de faenamiento para ejercitar el filtro
    /// por fecha, que es lo que se está investigando.
    /// </summary>
    public static async Task<Despacho> DespachoAsync(
        ApiFactory api,
        DateTime fechaDespacho,
        string cliente = "Cliente de prueba")
    {
        await using var db = api.NuevoDbContext();

        var despacho = new Despacho
        {
            ClienteDestino = cliente,
            FechaDespacho = DateTime.SpecifyKind(fechaDespacho, DateTimeKind.Utc),
            CantidadUnidades = 3,
            Responsable = "Responsable de prueba",
            Chofer = "Chofer de prueba",
            Ruta = "Ruta de prueba",
            TipoMercado = "Local",
            Ciudad = "Cuenca",
            Pais = "Ecuador"
        };

        db.Despachos.Add(despacho);
        await db.SaveChangesAsync();
        return despacho;
    }
```

Y añade el `using` que falta arriba del archivo:

```csharp
using CoopagcuyApi.Features.Faenamiento.Models;
```

- [ ] **Paso 3: Escribir la prueba que reproduce el fallo**

Crea `tests/CoopagcuyApi.Tests/Integracion/ReporteSalidaTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El reporte de Salida dejó de mostrar los despachos nuevos: el último
/// visible era del 04/08/2026 pese a haberse registrado despachos el 18/08.
/// Los despachos SÍ aparecen en la pantalla Despacho, que consulta sin filtro
/// de fecha, así que la fila existe y la diferencia entre ambas vistas es el
/// rango de fechas.
///
/// Estas pruebas separan las dos mitades del problema: si pasan, la consulta
/// del reporte es correcta y el fallo está en el valor de FechaDespacho que
/// se guarda; si fallan, el fallo está en la consulta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ReporteSalidaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record FilaSalida(
        string CodigoLote, DateTime FechaDespacho, string ClienteDestino,
        string Chofer, string Ruta, string TipoMercado, string Destino,
        int CantidadUnidades, string Responsable);

    [Fact]
    public async Task UnDespachoDeHoy_apareceEnElReporteDelMesEnCurso()
    {
        var hoy = DateTime.UtcNow;
        await Sembrador.DespachoAsync(api, hoy, "Cliente de hoy");

        var desde = new DateTime(hoy.Year, hoy.Month, 1).ToString("yyyy-MM-dd");
        var hasta = hoy.ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/salida?desde={desde}&hasta={hasta}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        filas.ShouldContain(f => f.ClienteDestino == "Cliente de hoy");
    }

    [Fact]
    public async Task UnDespachoDeHoy_apareceAunqueSeaElUltimoDiaDelRango()
    {
        // El límite superior del filtro es exclusivo (día siguiente a las
        // 00:00 UTC). Un despacho de esta tarde cae dentro del último día del
        // rango: si RangoUtc cortara a medianoche, esta prueba lo detectaría.
        var hoy = DateTime.UtcNow;
        await Sembrador.DespachoAsync(api, hoy, "Cliente del borde");

        var dia = hoy.ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/salida?desde={dia}&hasta={dia}");

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        filas.ShouldContain(f => f.ClienteDestino == "Cliente del borde");
    }

    [Fact]
    public async Task UnDespachoFueraDelRango_noApareceEnElReporte()
    {
        // Control negativo: si esta prueba también fallara, el filtro no
        // estaría filtrando nada y las dos anteriores no probarían gran cosa.
        await Sembrador.DespachoAsync(
            api, DateTime.UtcNow.AddDays(-90), "Cliente antiguo");

        var hoy = DateTime.UtcNow;
        var desde = new DateTime(hoy.Year, hoy.Month, 1).ToString("yyyy-MM-dd");
        var hasta = hoy.ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/salida?desde={desde}&hasta={hasta}");

        var filas = await respuesta.Content.ReadFromJsonAsync<List<FilaSalida>>();
        filas.ShouldNotBeNull();
        filas.ShouldNotContain(f => f.ClienteDestino == "Cliente antiguo");
    }
}
```

- [ ] **Paso 4: Correr las pruebas y anotar el resultado**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ReporteSalidaTests"
```

Dos desenlaces, y los dos son resultados válidos de esta tarea:

- **Alguna prueba falla** → la consulta de `ReporteSalidaAsync` descarta filas
  que sí cumplen el `Where`. Sospecha de los `Include` encadenados: esa
  consulta no usa `AsSplitQuery()` mientras `ListarDespachosAsync` sí lo hace.
  Captura el SQL generado activando el registro de EF y compáralo con el de
  `ListarDespachosAsync`.
- **Las tres pruebas pasan** → la consulta es correcta. El fallo está en el
  valor de `FechaDespacho` almacenado, y el resultado del Paso 1 dice cuál es.
  Compara ese valor con la hora real del registro para medir el desfase.

- [ ] **Paso 5: Escribir el hallazgo**

Crea `docs/superpowers/plans/2026-08-18-hallazgo-reporte-salida.md` con: el
resultado literal de la consulta SQL del Paso 1, cuáles pruebas pasaron y
cuáles no, y una frase con la causa identificada. Ese documento es la entrada
del plan de la Fase 4.

- [ ] **Paso 6: Confirmar**

```bash
git add tests/CoopagcuyApi.Tests/Infra/Sembrador.cs tests/CoopagcuyApi.Tests/Integracion/ReporteSalidaTests.cs docs/superpowers/plans/2026-08-18-hallazgo-reporte-salida.md
git commit -m "test: aislar el fallo del reporte de Salida con despachos insertados a mano"
```

---

## Tarea 2: El admin técnico pierde la operación en la API

**Archivos:**
- Modificar: `Features/Recepcion/Controllers/RecepcionController.cs` (líneas 50, 94, 180, 200, 221, 248)
- Modificar: `Features/Faenamiento/Controllers/FaenamientoController.cs` (líneas 14, 25, 38, 102, 116, 128, 160)
- Modificar: `Features/Productoras/Controllers/PagosController.cs` (línea 16)
- Modificar: `Features/Productoras/Controllers/ProductorasController.cs` (líneas 19, 53, 76, 98, 110)
- Modificar: `Features/Reportes/Controllers/ReportesController.cs` (endpoints `dashboard`, `entrada`, `transito`, `salida` y sus tres exportaciones)
- Modificar: `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`

**Interfaces:**
- Consume: `api.ComoAdminTecnico()`, `api.ComoAdmin()`, `api.ComoOperadorCat(cat)` de `ApiFactory`.

- [ ] **Paso 1: Escribir las pruebas que fallan**

Añade a `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`, dentro
de la clase:

```csharp
    // ── El admin técnico queda acotado a soporte ──────────────────────
    // Su trabajo es atender usuarios, no operar la cadena. Pierde recepción,
    // faenamiento, despacho, productoras, pagos y los reportes del flujo
    // físico; conserva vinculaciones, reportes administrativos, usuarios y
    // sesiones.

    [Theory]
    [InlineData("/api/recepcion/lotes")]
    [InlineData("/api/faenamiento/despachos")]
    [InlineData("/api/productoras")]
    [InlineData("/api/pagos")]
    [InlineData("/api/reportes/dashboard")]
    public async Task AdminTecnico_pierdeLaOperacion(string ruta)
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync(ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/reportes/entrada")]
    [InlineData("/api/reportes/transito")]
    [InlineData("/api/reportes/salida")]
    [InlineData("/api/reportes/exportar/excel/entrada")]
    [InlineData("/api/reportes/exportar/excel/transito")]
    [InlineData("/api/reportes/exportar/excel/salida")]
    public async Task AdminTecnico_pierdeLosReportesDelFlujoOperativo(string ruta)
    {
        var respuesta = await api.ComoAdminTecnico()
            .GetAsync($"{ruta}?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/reportes/productoras")]
    [InlineData("/api/reportes/cat")]
    [InlineData("/api/reportes/novedades")]
    [InlineData("/api/reportes/devoluciones")]
    public async Task AdminTecnico_conservaLosReportesAdministrativos(string ruta)
    {
        var respuesta = await api.ComoAdminTecnico()
            .GetAsync($"{ruta}?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminTecnico_conservaLaBandejaDeVinculaciones()
    {
        // Los endpoints de vinculación viven dentro de RecepcionController.
        // Retirar el rol de "todo el controlador" le rompería una de las
        // cuatro pantallas que conserva: esta prueba es la red que lo evita.
        var respuesta = await api.ComoAdminTecnico()
            .GetAsync("/api/recepcion/vinculaciones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminCooperativa_conservaLaOperacion()
    {
        // Control: el recorte es del técnico, no de los dos administradores.
        var respuesta = await api.ComoAdmin().GetAsync("/api/productoras");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
```

Actualiza también el comentario de la clase, que hoy afirma lo contrario de lo
que va a pasar a ser cierto. Reemplaza el bloque `<summary>` de arriba del todo
por:

```csharp
/// <summary>
/// Los dos roles de administración no son intercambiables. El técnico atiende
/// soporte: conserva vinculaciones, reportes administrativos, administración de
/// usuarios y sesiones activas, y pierde toda la operación de la cadena. El de
/// cooperativa opera y pierde las sesiones activas.
///
/// Se comprueba en el API y no solo en las rutas del front: una ruta protegida
/// sin su [Authorize] correspondiente es una falsa sensación de seguridad —con
/// el token se llama igual.
/// </summary>
```

- [ ] **Paso 2: Correr las pruebas y verificar que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AutorizacionAdminTests"
```

Esperado: FALLAN las de `pierdeLaOperacion` y `pierdeLosReportesDelFlujoOperativo`
(devuelven `OK` en vez de `Forbidden`). Las de `conserva…` deben pasar ya.

- [ ] **Paso 3: Quitar `AdminTecnico` de los controladores de operación**

En `Features/Recepcion/Controllers/RecepcionController.cs`, en las líneas
**50, 94, 180, 200 y 221** cambia:

```csharp
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,AdminTecnico")]
```

por:

```csharp
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
```

y en la línea **248**:

```csharp
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
```

**No toques las líneas 308, 320 y 345.** Son la bandeja de vinculación
(`GET vinculaciones`, `POST vinculaciones/{id}/resolver` y el descarte) y deben
seguir siendo `AdminCooperativa,AdminTecnico`.

En `Features/Faenamiento/Controllers/FaenamientoController.cs`, en las líneas
**14** (atributo de clase), **25, 38, 102, 116, 128 y 160**, cambia todas las
apariciones de:

```csharp
[Authorize(Roles = "OperadorFaenamiento,AdminCooperativa,AdminTecnico")]
```

por:

```csharp
[Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
```

En `Features/Productoras/Controllers/PagosController.cs`, línea **16**:

```csharp
[Authorize(Roles = "OperadorCAT,AdminCooperativa")]
```

En `Features/Productoras/Controllers/ProductorasController.cs`, línea **19**:

```csharp
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
```

y en las líneas **53, 76, 98 y 110**:

```csharp
    [Authorize(Roles = "AdminCooperativa")]
```

(La Tarea 4 volverá a tocar estas cuatro para añadir `OperadorCAT` donde
corresponde. Aquí solo sale `AdminTecnico`.)

- [ ] **Paso 4: Quitar `AdminTecnico` de los reportes del flujo operativo**

En `Features/Reportes/Controllers/ReportesController.cs`, en los endpoints
`dashboard`, `entrada`, `transito`, `salida`, `exportar/excel/entrada`,
`exportar/excel/transito` y `exportar/excel/salida`, cambia:

```csharp
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
```

por:

```csharp
    [Authorize(Roles = "AdminCooperativa,OperadorFaenamiento")]
```

**No toques** `productoras`, `cat`, `novedades`, `devoluciones` ni sus
exportaciones: el admin técnico las conserva. `exportar/excel/general` se trata
aparte en la Tarea 3; déjalo como está por ahora.

- [ ] **Paso 5: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AutorizacionAdminTests"
```

Esperado: PASAN todas.

- [ ] **Paso 6: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: PASAN todas. Un cambio de permisos puede romper pruebas ajenas que
usaban el token de admin técnico por comodidad; si alguna falla, cámbiala a
`ComoAdmin()` en lugar de revertir el permiso.

- [ ] **Paso 7: Confirmar**

```bash
git add Features/ tests/
git commit -m "feat: acotar el admin tecnico a soporte, sin operacion de la cadena"
```

---

## Tarea 3: La exportación General respeta el recorte

`ExportarExcelGeneralAsync` arma un libro con una hoja por reporte, Salida
incluida. Sin este cambio, el admin técnico descarga por ahí lo que se le acaba
de negar por la puerta principal.

**Archivos:**
- Modificar: `Features/Reportes/Services/ReportesService.cs:875` (interfaz en la línea 34)
- Modificar: `Features/Reportes/Controllers/ReportesController.cs` (endpoint `exportar/excel/general`)
- Modificar: `tests/CoopagcuyApi.Tests/Integracion/AutorizacionAdminTests.cs`

**Interfaces:**
- Produce: `IReportesService.ExportarExcelGeneralAsync(FiltroPeriodoDto filtro, bool incluirFlujoOperativo = true)` → `Task<byte[]>`

- [ ] **Paso 1: Escribir la prueba que falla**

Añade a `AutorizacionAdminTests.cs`:

```csharp
    [Fact]
    public async Task AdminTecnico_descargaElLibroGeneralSinLasHojasDelFlujo()
    {
        // La restricción no puede escaparse por la descarga: el libro general
        // llevaba una hoja de Salida, que es justo lo que este rol perdió.
        var respuesta = await api.ComoAdminTecnico().GetAsync(
            "/api/reportes/exportar/excel/general?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contenido = await respuesta.Content.ReadAsStreamAsync();
        using var libro = new XLWorkbook(contenido);
        var hojas = libro.Worksheets.Select(h => h.Name).ToList();

        hojas.ShouldNotContain("Salida");
        hojas.ShouldNotContain("Entrada");
        hojas.ShouldNotContain("Transito");
        hojas.ShouldContain("Productoras");
    }

    [Fact]
    public async Task AdminCooperativa_descargaElLibroGeneralCompleto()
    {
        var respuesta = await api.ComoAdmin().GetAsync(
            "/api/reportes/exportar/excel/general?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contenido = await respuesta.Content.ReadAsStreamAsync();
        using var libro = new XLWorkbook(contenido);
        var hojas = libro.Worksheets.Select(h => h.Name).ToList();

        hojas.ShouldContain("Salida");
        hojas.ShouldContain("Productoras");
    }
```

Añade los `using` que faltan arriba del archivo:

```csharp
using ClosedXML.Excel;
```

**Antes de escribir el código**, confirma los nombres literales de las hojas:

```bash
grep -n "Worksheets.Add" Features/Reportes/Services/ReportesService.cs
```

Usa en las aserciones exactamente los nombres que devuelva ese comando. Si
`Transito` lleva tilde en el código, ponla también en la prueba.

- [ ] **Paso 2: Correr las pruebas y verificar que la primera falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AutorizacionAdminTests"
```

Esperado: FALLA `descargaElLibroGeneralSinLasHojasDelFlujo` porque el libro
todavía trae la hoja `Salida`.

- [ ] **Paso 3: Añadir el parámetro al servicio**

En `Features/Reportes/Services/ReportesService.cs`, línea **34** de la interfaz:

```csharp
    Task<byte[]> ExportarExcelGeneralAsync(
        FiltroPeriodoDto filtro, bool incluirFlujoOperativo = true);
```

Y reemplaza el cuerpo del método (línea 875 en adelante) por:

```csharp
    public async Task<byte[]> ExportarExcelGeneralAsync(
        FiltroPeriodoDto filtro, bool incluirFlujoOperativo = true)
    {
        // El orden sigue el flujo de la cadena: primero los tres eslabones
        // de trazabilidad, después los desgloses y las devoluciones.
        //
        // Los tres primeros se omiten para quien no puede consultarlos por
        // separado (el admin técnico): si no, la restricción de rol se
        // escaparía por esta descarga, que es una sola llamada a un endpoint
        // que sí conserva.
        var partes = new List<byte[]>();

        if (incluirFlujoOperativo)
        {
            partes.Add(await ExportarExcelEntradaAsync(filtro));
            partes.Add(await ExportarExcelTransitoAsync(filtro));
            partes.Add(await ExportarExcelSalidaAsync(filtro));
        }

        partes.Add(await ExportarExcelProductorasAsync(filtro));
        partes.Add(await ExportarExcelCATAsync(filtro));
        partes.Add(await ExportarExcelNovedadesAsync(filtro));
        partes.Add(await ExportarExcelCuyesAsync(filtro));
        // Aporta dos hojas: devoluciones de clientes y retornos
        partes.Add(await ExportarExcelDevolucionesAsync(filtro));

        using var libro = new XLWorkbook();
        foreach (var bytes in partes)
        {
            using var origen = new MemoryStream(bytes);
            using var libroOrigen = new XLWorkbook(origen);
            foreach (var hoja in libroOrigen.Worksheets)
                hoja.CopyTo(libro, hoja.Name);
        }

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }
```

- [ ] **Paso 4: Decidirlo en el controlador**

En `Features/Reportes/Controllers/ReportesController.cs`, endpoint
`exportar/excel/general`:

```csharp
    [HttpGet("exportar/excel/general")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]
    public async Task<IActionResult> ExcelGeneral(
        [FromQuery] DateTime desde, [FromQuery] DateTime hasta,
        [FromQuery] string? cat)
    {
        // La decisión de permisos se toma aquí, no en el servicio: el servicio
        // arma libros, no sabe de roles.
        var incluirFlujoOperativo = !User.IsInRole("AdminTecnico");

        var bytes = await service.ExportarExcelGeneralAsync(
            new FiltroPeriodoDto(desde, hasta, cat), incluirFlujoOperativo);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Reporte-General-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
    }
```

- [ ] **Paso 5: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AutorizacionAdminTests"
```

Esperado: PASAN todas.

- [ ] **Paso 6: Confirmar**

```bash
git add Features/Reportes/ tests/
git commit -m "feat: el libro general omite el flujo operativo para el admin tecnico"
```

---

## Tarea 4: El operador de CAT crea productoras de su centro

**Archivos:**
- Modificar: `Common/Auth/AlcanceUsuario.cs`
- Modificar: `Features/Productoras/Services/ProductoraService.cs` (interfaz y clase)
- Modificar: `Features/Productoras/Controllers/ProductorasController.cs:52-73`
- Modificar: `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`
- Crear: `tests/CoopagcuyApi.Tests/Integracion/AlcanceProductorasTests.cs`

**Interfaces:**
- Consume: `AlcanceUsuario.CatRestringido(ClaimsPrincipal)` → `string?`, `AlcanceUsuario.FueraDeAlcance(ClaimsPrincipal, string?)` → `bool` (ya existen).
- Produce: `IProductoraService.CatDeComunidadAsync(int comunidadId)` → `Task<CentroAcopio?>`
- Produce: `Sembrador.ProductoraAsync(ApiFactory api, string cedula, CentroAcopio cat = CentroAcopio.PAT, int comunidadId = 1, bool activa = true)` → `Task<Productora>`

- [ ] **Paso 1: Añadir el sembrador de productoras**

En `tests/CoopagcuyApi.Tests/Infra/Sembrador.cs`, añade:

```csharp
    /// <summary>
    /// Alta de productoras para las pruebas de alcance. Las comunidades están
    /// sembradas con HasData y sus Id son estables: 1 Patococha (PAT),
    /// 2 Las Nieves (NIE), 3 Huertas (HUE), 4 Nabón/El Progreso (NAB),
    /// 5 Pelincay (PEL).
    ///
    /// La cédula debe ser válida según el algoritmo ecuatoriano: ProductoraService
    /// la revalida al crear, así que un número inventado reventaría la prueba por
    /// un motivo que no tiene que ver con lo que verifica.
    /// </summary>
    public static async Task<Productora> ProductoraAsync(
        ApiFactory api,
        string cedula,
        CentroAcopio cat = CentroAcopio.PAT,
        int comunidadId = 1,
        bool activa = true)
    {
        await using var db = api.NuevoDbContext();

        var productora = new Productora
        {
            NombreCompleto = $"Productora {cedula}",
            Cedula = cedula,
            ComunidadId = comunidadId,
            CatAsignado = cat,
            Activa = activa
        };

        db.Productoras.Add(productora);
        await db.SaveChangesAsync();
        return productora;
    }
```

Y el `using` correspondiente:

```csharp
using CoopagcuyApi.Features.Productoras.Models;
```

- [ ] **Paso 2: Escribir las pruebas que fallan**

Crea `tests/CoopagcuyApi.Tests/Integracion/AlcanceProductorasTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El operador de CAT gestiona las productoras de su propio centro. El alcance
/// se comprueba contra el claim "cat" del token y nunca contra lo que mande el
/// cuerpo de la petición: si el cliente pudiera elegir su propio alcance, no
/// habría alcance.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AlcanceProductorasTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Cédulas ecuatorianas válidas (provincia y dígito verificador correctos).
    // ProductoraService las revalida al crear.
    private const string CedulaUno = "0102030405";
    private const string CedulaDos = "0102030496";

    private sealed record RespuestaProductora(
        int Id, string NombreCompleto, string Cedula, int ComunidadId,
        string Comunidad, string Canton, string CatAsignado, string? Telefono,
        bool Activa, DateTime FechaRegistro, int TotalRetornos);

    [Fact]
    public async Task OperadorCat_creaProductoraEnSuCentro()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 1,          // Patococha, CatReferencia = PAT
                catAsignado = "PAT",
                telefono = "0999999999"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OperadorCat_noCreaProductoraEnComunidadDeOtroCentro()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, CatReferencia = NIE
                catAsignado = "PAT",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_noEligeElCentroDeLaProductoraQueCrea()
    {
        // Manda "NIE" en el cuerpo teniendo "PAT" en el token: el servidor
        // debe ignorar el cuerpo y sellar la productora con el CAT del token.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaUno,
                comunidadId = 1,
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var creada = await respuesta.Content
            .ReadFromJsonAsync<RespuestaProductora>();
        creada.ShouldNotBeNull();
        creada.CatAsignado.ShouldBe("PAT");
    }

    [Fact]
    public async Task AdminCooperativa_sigueCreandoEnCualquierCentro()
    {
        // Control: el admin no queda atrapado por la regla del operador.
        var respuesta = await api.ComoAdmin()
            .PostAsJsonAsync("/api/productoras", new
            {
                nombreCompleto = "María Quizhpi",
                cedula = CedulaDos,
                comunidadId = 2,
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
```

**Antes de correr**, verifica que las dos cédulas son válidas para
`ValidadorCedula`. Si alguna no lo es, la prueba dará 400 en vez de 201 y
perderás tiempo buscando el fallo donde no está:

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ValidadorCedulaTests"
```

Si necesitas otras, toma cédulas válidas de las que ya usan las pruebas
existentes (`grep -rn "cedula" tests/CoopagcuyApi.Tests/Integracion/`).

- [ ] **Paso 3: Correr las pruebas y verificar que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcanceProductorasTests"
```

Esperado: FALLAN las tres del operador con `Forbidden` (el rol todavía no está
en el `[Authorize]` del `POST`). La del admin pasa.

- [ ] **Paso 4: Ampliar `AlcanceUsuario`**

En `Common/Auth/AlcanceUsuario.cs`, añade dentro de la clase:

```csharp
    /// <summary>
    /// true si el usuario NO puede operar sobre la comunidad indicada. Una
    /// comunidad pertenece a un centro de acopio (Comunidad.CatReferencia),
    /// así que el alcance por CAT alcanza también a las comunidades sin
    /// necesidad de un campo nuevo en el usuario.
    ///
    /// Vive aquí y no en el controlador por la misma razón que el resto de
    /// este archivo: la respuesta a "qué puede tocar este usuario" tiene un
    /// solo sitio, o el próximo endpoint se olvidará de preguntarla.
    /// </summary>
    public static bool ComunidadFueraDeAlcance(
        this ClaimsPrincipal user, CentroAcopio? catDeLaComunidad)
    {
        var catUsuario = user.CatRestringido();
        if (catUsuario is null) return false;              // sin restricción
        if (catDeLaComunidad is null) return true;         // comunidad inexistente
        return !string.Equals(catUsuario, catDeLaComunidad.Value.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
```

Y el `using` que falta arriba del archivo:

```csharp
using CoopagcuyApi.Common;
```

- [ ] **Paso 5: Consultar el CAT de una comunidad**

En `Features/Productoras/Services/ProductoraService.cs`, añade a la interfaz
`IProductoraService`:

```csharp
    /// CAT de referencia de una comunidad del catálogo, o null si no existe.
    Task<CentroAcopio?> CatDeComunidadAsync(int comunidadId);
```

Y a la clase `ProductoraService`:

```csharp
    public async Task<CentroAcopio?> CatDeComunidadAsync(int comunidadId)
    {
        var comunidad = await db.Comunidades
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == comunidadId);
        return comunidad?.CatReferencia;
    }
```

Añade el `using` si falta:

```csharp
using CoopagcuyApi.Common;
```

- [ ] **Paso 6: Aplicar la regla en el `POST`**

En `Features/Productoras/Controllers/ProductorasController.cs`, reemplaza el
método `Crear` completo (líneas 52-73) por:

```csharp
    [HttpPost]
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> Crear([FromBody] CrearProductoraDto dto)
    {
        // El operador no elige el centro de la productora que registra: se
        // sella con el CAT de su token. Se hace ANTES de validar para que el
        // validador vea el objeto definitivo, y para que un cuerpo con otro
        // centro no sea un error sino sencillamente un dato ignorado.
        if (User.CatRestringido() is string catOperador &&
            Enum.TryParse<CentroAcopio>(catOperador, out var catDelToken))
            dto.CatAsignado = catDelToken;

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

        var validacion = await validator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

        try
        {
            var result = await service.CrearAsync(dto);
            return CreatedAtAction(nameof(ObtenerPorId), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
```

Añade los `using` que falten arriba del archivo:

```csharp
using CoopagcuyApi.Common;
```

- [ ] **Paso 7: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcanceProductorasTests"
```

Esperado: PASAN las cuatro.

- [ ] **Paso 8: Confirmar**

```bash
git add Common/Auth/AlcanceUsuario.cs Features/Productoras/ tests/
git commit -m "feat: el operador de CAT registra productoras de las comunidades de su centro"
```

---

## Tarea 5: El operador de CAT edita, desactiva y reactiva

**Archivos:**
- Modificar: `Features/Productoras/Controllers/ProductorasController.cs` (métodos `Listar`, `Actualizar`, `CambiarEstado`)
- Modificar: `tests/CoopagcuyApi.Tests/Integracion/AlcanceProductorasTests.cs`

**Interfaces:**
- Consume: `Sembrador.ProductoraAsync(...)`, `AlcanceUsuario.FueraDeAlcance(...)`, `AlcanceUsuario.ComunidadFueraDeAlcance(...)`, `IProductoraService.CatDeComunidadAsync(...)` (Tarea 4).

- [ ] **Paso 1: Escribir las pruebas que fallan**

Añade a `AlcanceProductorasTests.cs`, dentro de la clase:

```csharp
    // ── Edición, baja y alta ──────────────────────────────────────────

    [Fact]
    public async Task OperadorCat_editaProductoraDeSuCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Nombre corregido",
                cedula = CedulaUno,
                comunidadId = 1,
                catAsignado = "PAT",
                telefono = "0988888888"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OperadorCat_noEditaProductoraDeOtroCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.NIE, comunidadId: 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Intento de edición",
                cedula = CedulaUno,
                comunidadId = 2,
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_noMueveUnaProductoraAOtroCentro()
    {
        // Sin esta comprobación, una edición sacaría a la productora de su
        // alcance de un solo golpe: entra siendo de PAT y sale siendo de NIE,
        // fuera de la vista de quien la movió y dentro de la de otro centro.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PutAsJsonAsync($"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = "Intento de traslado",
                cedula = CedulaUno,
                comunidadId = 2,          // Las Nieves, de otro centro
                catAsignado = "NIE",
                telefono = (string?)null
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_desactivaYReactivaProductoraDeSuCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);
        var cliente = api.ComoOperadorCat("PAT");

        var baja = await cliente.PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = false });
        var alta = await cliente.PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = true });

        baja.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        alta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OperadorCat_noCambiaElEstadoDeOtroCentro()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.NIE, comunidadId: 2);

        var respuesta = await api.ComoOperadorCat("PAT").PatchAsJsonAsync(
            $"/api/productoras/{productora.Id}/estado", new { activa = false });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperadorCat_veLasInactivasDeSuCentroYNingunaDeOtro()
    {
        // Sin las inactivas a la vista no hay forma de reactivar ninguna.
        await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1, activa: false);
        await Sembrador.ProductoraAsync(
            api, CedulaDos, CentroAcopio.NIE, comunidadId: 2, activa: false);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/productoras?incluirInactivas=true");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var lista = await respuesta.Content
            .ReadFromJsonAsync<List<RespuestaProductora>>();
        lista.ShouldNotBeNull();
        lista.ShouldContain(p => p.Cedula == CedulaUno);
        lista.ShouldNotContain(p => p.Cedula == CedulaDos);
    }

    [Fact]
    public async Task OperadorCat_noVeElHistorialDeCambios()
    {
        // Es información de auditoría: sigue siendo de administradores.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaUno, CentroAcopio.PAT, comunidadId: 1);

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/productoras/{productora.Id}/historial");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

`PatchAsJsonAsync` vive en `System.Net.Http.Json`, que ya está importado.

- [ ] **Paso 2: Correr las pruebas y verificar que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcanceProductorasTests"
```

Esperado: FALLAN `editaProductoraDeSuCentro`,
`desactivaYReactivaProductoraDeSuCentro` (dan `Forbidden`, falta el rol) y
`veLasInactivasDeSuCentroYNingunaDeOtro` (la lista llega vacía porque hoy el
operador nunca recibe inactivas).

- [ ] **Paso 3: Dejar que el operador vea las inactivas de su centro**

En `Features/Productoras/Controllers/ProductorasController.cs`, método `Listar`,
cambia la última línea del cuerpo. Antes:

```csharp
        var result = await service.ObtenerTodasAsync(
            comunidad, catEfectivo, incluirInactivas && !esOperador);
```

Después:

```csharp
        var result = await service.ObtenerTodasAsync(
            comunidad, catEfectivo, incluirInactivas);
```

Y reemplaza el comentario que hay justo encima por:

```csharp
        // El operador de CAT solo ve las productoras de su centro —el filtro
        // por catEfectivo lo garantiza—, incluidas las inactivas: desde que
        // puede reactivarlas, ocultárselas le quitaría la mitad del trabajo.
```

- [ ] **Paso 4: Aplicar la regla en `PUT` y `PATCH`**

Reemplaza los métodos `Actualizar` y `CambiarEstado` completos por:

```csharp
    [HttpPut("{id:int}")]
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] CrearProductoraDto dto)
    {
        var actual = await service.ObtenerPorIdAsync(id);
        if (actual is null) return NotFound();

        // Alcance sobre la productora tal como está HOY: si se comprobara
        // contra el DTO, bastaría con mandar el propio CAT en el cuerpo para
        // editar la de cualquier otro centro.
        if (User.FueraDeAlcance(actual.CatAsignado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede editar productoras de su centro."
            });

        // Y alcance sobre el destino: una edición no puede ser la puerta por
        // la que una productora sale del centro de quien la edita.
        var catComunidad = await service.CatDeComunidadAsync(dto.ComunidadId);
        if (User.ComunidadFueraDeAlcance(catComunidad))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "No puedes mover una productora a una comunidad " +
                          "de otro centro de acopio."
            });

        if (User.CatRestringido() is string catOperador &&
            Enum.TryParse<CentroAcopio>(catOperador, out var catDelToken))
            dto.CatAsignado = catDelToken;

        var validacion = await validator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

        // La auditoría identifica al usuario por su cédula (claim del token)
        var modificadoPor = User.FindFirstValue("cedula") ?? "desconocido";
        var ok = await service.ActualizarAsync(id, dto, modificadoPor);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Activa o desactiva una productora (baja lógica). Conserva su
    /// historial de lotes, pagos y trazabilidad.
    /// </summary>
    [HttpPatch("{id:int}/estado")]
    [Authorize(Roles = "AdminCooperativa,OperadorCAT")]
    public async Task<IActionResult> CambiarEstado(
        int id, [FromBody] CambiarEstadoProductoraDto dto)
    {
        var actual = await service.ObtenerPorIdAsync(id);
        if (actual is null) return NotFound();

        // Vale para las dos direcciones: dar de baja y volver a activar.
        if (User.FueraDeAlcance(actual.CatAsignado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede cambiar el estado de las " +
                          "productoras de su centro."
            });

        var ok = await service.CambiarEstadoAsync(id, dto.Activa);
        return ok ? NoContent() : NotFound();
    }
```

**No toques** `Historial`: se queda en `[Authorize(Roles = "AdminCooperativa")]`.

- [ ] **Paso 5: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~AlcanceProductorasTests"
```

Esperado: PASAN las once.

- [ ] **Paso 6: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: PASAN todas.

- [ ] **Paso 7: Confirmar**

```bash
git add Features/Productoras/ tests/
git commit -m "feat: el operador de CAT edita, desactiva y reactiva las productoras de su centro"
```

---

## Tarea 6: Menú, rutas y pestañas de reportes del admin técnico

Trabajo en el repo del **frontend**:
`C:\Users\nicol\OneDrive\Documents\CoopagcuyFront\coopagcuy-frontend`.

**Archivos:**
- Modificar: `src/components/layout/MainLayout.tsx:6-15`
- Modificar: `src/App.tsx:44-99`
- Modificar: `src/pages/Reportes.tsx:14`, `81`, y la lista de pestañas (~línea 278)

- [ ] **Paso 1: Ajustar el menú**

En `src/components/layout/MainLayout.tsx`, reemplaza el arreglo `navItems`
completo por:

```tsx
// Cada ítem declara qué roles pueden verlo. El admin técnico atiende soporte:
// no aparece en la operación de la cadena. Ocultarlo aquí es cosmética — la
// restricción de verdad vive en los [Authorize] de la API.
const navItems: { to: string; label: string; roles: string[] | null }[] = [
    { to: "/dashboard", label: "Panel", roles: ["AdminCooperativa", "OperadorCAT", "OperadorFaenamiento"] },
    { to: "/productoras", label: "Productoras", roles: ["AdminCooperativa", "OperadorCAT"] },
    { to: "/recepcion", label: "Recepción CAT", roles: ["OperadorCAT", "AdminCooperativa"] },
    { to: "/faenamiento", label: "Faenamiento", roles: ["OperadorFaenamiento", "AdminCooperativa"] },
    { to: "/despacho", label: "Despacho", roles: ["OperadorFaenamiento", "AdminCooperativa"] },
    { to: "/reportes", label: "Reportes", roles: ["AdminCooperativa", "AdminTecnico", "OperadorFaenamiento"] },
    { to: "/vinculaciones", label: "Vinculaciones", roles: ["AdminCooperativa", "AdminTecnico"] },
    { to: "/administracion", label: "Administración", roles: ["AdminCooperativa", "AdminTecnico"] },
    { to: "/sesiones", label: "Sesiones", roles: ["AdminTecnico"] },
];
```

El Panel pasa de `roles: null` a una lista explícita. `null` significa «todos»,
y el admin técnico ya no es todos.

- [ ] **Paso 2: Ajustar las rutas**

En `src/App.tsx`, quita `"AdminTecnico"` de los `rolesPermitidos` de las rutas
`/dashboard`, `/productoras`, `/recepcion`, `/faenamiento` y `/despacho`
(líneas 44-72 aproximadamente). Deja intactas `/reportes`, `/vinculaciones`,
`/administracion` y `/sesiones`.

Las listas resultantes, ruta por ruta:

```tsx
// /dashboard
["AdminCooperativa", "OperadorCAT", "OperadorFaenamiento"]
// /productoras
["AdminCooperativa", "OperadorCAT"]
// /recepcion
["OperadorCAT", "AdminCooperativa"]
// /faenamiento
["OperadorFaenamiento", "AdminCooperativa"]
// /despacho
["OperadorFaenamiento", "AdminCooperativa"]
```

- [ ] **Paso 3: Revisar a dónde cae el admin técnico tras entrar**

Busca a dónde redirige la aplicación después del login y cuando `PrivateRoute`
rechaza un rol:

```bash
grep -rn "Navigate\|navigate(" src/components/PrivateRoute.tsx src/pages/Login.tsx src/App.tsx
```

Si cualquiera de los dos apunta a `/dashboard` de forma fija, ahora dejaría al
admin técnico en una pantalla que ya no puede abrir —y, si `PrivateRoute`
redirige a `/dashboard` al rechazar, en un bucle de redirecciones. Sustituye el
destino fijo por una función que elija según el rol:

```tsx
/** Primera pantalla de cada rol. El admin técnico ya no puede abrir el panel. */
export function rutaInicial(rol: string | null): string {
    return rol === "AdminTecnico" ? "/reportes" : "/dashboard";
}
```

Colócala en `src/utils/rutaInicial.ts` y úsala en los dos sitios.

- [ ] **Paso 4: Filtrar las pestañas de reportes**

En `src/pages/Reportes.tsx`, justo debajo del tipo `Tab` (línea 14), añade:

```tsx
// El flujo físico del producto (entrada → tránsito → salida) es operación. El
// admin técnico conserva los reportes de gestión y calidad, no esos tres: la
// API le devuelve 403 en ellos, así que mostrárselos solo produciría un error
// de carga sin explicación.
const TABS_FLUJO: Tab[] = ["entrada", "transito", "salida"];

function tabsVisibles(rol: string | null): Tab[] {
    const todas: Tab[] = ["entrada", "transito", "salida",
        "productoras", "cat", "novedades", "devoluciones"];
    return rol === "AdminTecnico"
        ? todas.filter((t) => !TABS_FLUJO.includes(t))
        : todas;
}
```

Dentro del componente, reemplaza la inicialización del estado `tab` (línea 81):

```tsx
    const { auth } = useAuth();
    const visibles = useMemo(() => tabsVisibles(auth.rol), [auth.rol]);
    // La pestaña inicial es la primera visible: abrir en "entrada" dejaría al
    // admin técnico mirando un error de carga nada más entrar.
    const [tab, setTab] = useState<Tab>(visibles[0]);
```

Añade el import de `useAuth` arriba del archivo:

```tsx
import { useAuth } from "../context/useAuth";
```

Y en el `Segmentado` de las pestañas (~línea 278), filtra las opciones por
`visibles`:

```tsx
opciones={[
    { id: "entrada", label: "Entrada" },
    { id: "transito", label: "Tránsito" },
    { id: "salida", label: "Salida" },
    { id: "productoras", label: "Productoras" },
    { id: "cat", label: "CAT" },
    { id: "novedades", label: "Novedades" },
    { id: "devoluciones", label: "Devoluciones" },
].filter((o) => visibles.includes(o.id as Tab))}
```

**Antes de escribir esto**, abre el archivo y comprueba el nombre real de la
prop del componente `Segmentado` y el formato de sus opciones. Adáptalo a lo que
haya; no inventes una API que no existe.

- [ ] **Paso 5: Compilar y pasar el linter**

```bash
pnpm build
```

Esperado: compila sin errores de TypeScript.

```bash
pnpm lint
```

Esperado: sin errores.

- [ ] **Paso 6: Comprobarlo en el navegador**

Arranca el servidor de desarrollo, entra con un usuario `AdminTecnico` y
confirma tres cosas: el menú muestra solo Reportes, Vinculaciones,
Administración y Sesiones; Reportes abre en la pestaña Productoras; y escribir
`/despacho` a mano en la barra de direcciones no deja entrar ni provoca un bucle
de redirecciones.

- [ ] **Paso 7: Confirmar**

```bash
git add src/
git commit -m "feat: el admin tecnico solo ve soporte en el menu, las rutas y los reportes"
```

---

## Tarea 7: Pantalla de productoras para el operador de CAT

Trabajo en el repo del **frontend**.

**Archivos:**
- Modificar: `src/pages/Productoras.tsx:13-28` y los controles de gestión
- Modificar: `src/components/productoras/FormProductora.tsx`

- [ ] **Paso 1: Sustituir `esAdmin` por `puedeGestionar`**

En `src/pages/Productoras.tsx`, reemplaza las líneas 13-28 por:

```tsx
    const esAdmin = auth.rol === "AdminCooperativa";
    const esOperadorCat = auth.rol === "OperadorCAT";
    // Desde que el operador de CAT crea, edita, desactiva y reactiva, "ser
    // admin" dejó de ser lo que habilita la gestión. esAdmin se queda solo
    // donde la distinción sigue siendo real: el historial de auditoría.
    const puedeGestionar = esAdmin || esOperadorCat;
    // El operador de CAT solo ve su centro; el backend ya lo fuerza
    const catFijo = esOperadorCat ? auth.catAsignado : null;

    const [showForm, setShowForm] = useState(false);
    const [productoraEditar, setProductoraEditar] = useState<Productora | null>(null);
    const [filtroCat, setFiltroCat] = useState<CentroAcopio | "">("");
    const [filtroBusq, setFiltroBusq] = useState("");

    const { data = [], isLoading } = useQuery({
        queryKey: ["productoras", filtroCat, puedeGestionar],
        // Quien puede gestionar ve también las inactivas: sin ellas a la vista
        // no hay forma de reactivar ninguna.
        queryFn: () => productorasApi.listar({
            cat: filtroCat || undefined,
            incluirInactivas: puedeGestionar,
        }),
    });
```

- [ ] **Paso 2: Habilitar los controles**

En el mismo archivo, cambia `{esAdmin && (` por `{puedeGestionar && (` en el
botón «+ Nueva productora» (línea 53 aproximadamente). Busca el resto de
apariciones y decide una por una:

```bash
grep -n "esAdmin" src/pages/Productoras.tsx
```

Regla: el botón de editar y el interruptor de estado pasan a `puedeGestionar`;
el enlace al historial de cambios se queda en `esAdmin`, porque la API lo sigue
restringiendo a administradores y mostrarlo produciría un 403.

- [ ] **Paso 3: Fijar comunidad y centro en el formulario**

En `src/components/productoras/FormProductora.tsx`, después de la consulta de
comunidades, añade:

```tsx
    const { auth } = useAuth();
    const catFijo = auth.rol === "OperadorCAT"
        ? (auth.catAsignado as CentroAcopio | null) : null;

    // Al operador solo se le ofrecen las comunidades de su propio centro: el
    // servidor rechaza las demás con 403, así que mostrarlas solo serviría
    // para que eligiera algo que va a fallar.
    const comunidadesVisibles = useMemo(
        () => catFijo
            ? comunidades.filter((c) => c.catReferencia === catFijo)
            : comunidades,
        [comunidades, catFijo]);
```

Usa `comunidadesVisibles` en el desplegable de comunidades. En el selector de
CAT, cuando `catFijo` no sea nulo, muéstralo fijo y deshabilitado con ese valor
—mismo patrón que ya usa `FormLote.tsx` con su propio `catFijo`— y arranca el
estado del formulario con `catAsignado: catFijo ?? "PAT"`.

Añade los imports que falten:

```tsx
import { useMemo } from "react";
import { useAuth } from "../../context/useAuth";
import type { CentroAcopio } from "../../types/productora";
```

**Antes de escribir el filtro**, confirma cómo se llama el campo del CAT en el
DTO de comunidad que devuelve la API:

```bash
grep -rn "catReferencia\|CatReferencia" src/types/ ../../CoopagcuyApi/Features/Catalogos/DTOs/CatalogosDtos.cs
```

Si el nombre no es `catReferencia`, usa el que sea. Si el DTO **no** expone ese
campo, hay que añadirlo en `CatalogosDtos.cs` y en el tipo del front antes de
continuar: sin él no se puede filtrar.

- [ ] **Paso 4: Compilar y pasar el linter**

```bash
pnpm build
```

Esperado: compila sin errores.

```bash
pnpm lint
```

Esperado: sin errores.

- [ ] **Paso 5: Comprobarlo en el navegador**

Entra con un `OperadorCAT` de PAT. Confirma: aparece «+ Nueva productora»; el
desplegable de comunidades solo ofrece las de PAT; el CAT sale fijo; se crea una
productora sin error; el interruptor de estado la desactiva y la vuelve a
activar; y las inactivas aparecen en la lista.

- [ ] **Paso 6: Confirmar**

```bash
git add src/
git commit -m "feat: el operador de CAT gestiona productoras desde la pantalla, acotado a su centro"
```

---

## Tarea 8: Descripción legible del dispositivo

**Archivos:**
- Crear: `Common/Auth/DescripcionDispositivo.cs`
- Crear: `tests/CoopagcuyApi.Tests/Unitarias/DescripcionDispositivoTests.cs`

**Interfaces:**
- Produce: `DescripcionDispositivo.Describir(string? userAgent)` → `string`

- [ ] **Paso 1: Escribir la prueba que falla**

Crea `tests/CoopagcuyApi.Tests/Unitarias/DescripcionDispositivoTests.cs`:

```csharp
using CoopagcuyApi.Common.Auth;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// La pantalla de sesiones muestra un User-Agent traducido a algo que un
/// administrador pueda leer de un vistazo. No se usa una librería: son cinco
/// coincidencias de texto para las tablets del piloto, y una dependencia más
/// es una dependencia más que auditar.
/// </summary>
public class DescripcionDispositivoTests
{
    [Theory]
    // Tablet Android con Chrome — el caso mayoritario del piloto
    [InlineData(
        "Mozilla/5.0 (Linux; Android 13; SM-X200) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome · Android")]
    // iPad
    [InlineData(
        "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.0 Safari/605.1.15",
        "Safari · iPad")]
    // Escritorio Windows con Edge
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
        "Edge · Windows")]
    // Firefox en Windows
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) " +
        "Gecko/20100101 Firefox/121.0",
        "Firefox · Windows")]
    public void Describir_traduceLosUserAgentDelPiloto(string ua, string esperado)
    {
        DescripcionDispositivo.Describir(ua).ShouldBe(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("algo-que-no-es-un-user-agent")]
    public void Describir_noRevientaConEntradaInutil(string? ua)
    {
        // Un cliente puede no mandar User-Agent, o mandar cualquier cosa. La
        // pantalla de sesiones no puede caerse por eso.
        DescripcionDispositivo.Describir(ua).ShouldBe("Dispositivo desconocido");
    }
}
```

- [ ] **Paso 2: Correr la prueba y verificar que falla**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~DescripcionDispositivoTests"
```

Esperado: FALLA la compilación con `CS0103: El nombre 'DescripcionDispositivo'
no existe`.

- [ ] **Paso 3: Escribir el helper**

Crea `Common/Auth/DescripcionDispositivo.cs`:

```csharp
namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Traduce un User-Agent al nombre corto que se muestra en la pantalla de
/// sesiones activas ("Chrome · Android"). Función pura, sin dependencias.
///
/// Deliberadamente simple: son cinco coincidencias de texto para las tablets
/// del piloto, no un analizador completo de User-Agent. Lo que no reconozca
/// devuelve un texto neutro; nunca lanza, porque un cliente puede no mandar
/// User-Agent y la pantalla no puede caerse por eso.
///
/// El orden de las comprobaciones importa: Edge se anuncia como Chrome y
/// Chrome como Safari, así que lo más específico va primero.
/// </summary>
public static class DescripcionDispositivo
{
    public const string Desconocido = "Dispositivo desconocido";

    public static string Describir(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return Desconocido;

        var navegador = Navegador(userAgent);
        var sistema = Sistema(userAgent);

        if (navegador is null && sistema is null) return Desconocido;
        if (navegador is null) return sistema!;
        if (sistema is null) return navegador;
        return $"{navegador} · {sistema}";
    }

    private static string? Navegador(string ua) =>
        // Edge antes que Chrome: su User-Agent contiene "Chrome" además de "Edg"
        ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
        : ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
        : ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
        : ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
        // Safari va al final: Chrome y Edge también dicen "Safari"
        : ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
        : null;

    private static string? Sistema(string ua) =>
        // iPad antes que iPhone y que Mac: su cadena menciona "Mac OS X"
        ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
        : ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
        // Android antes que Linux: todo Android es también Linux
        : ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
        : ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
        : ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ? "macOS"
        : ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
        : null;
}
```

- [ ] **Paso 4: Correr la prueba y verificar que pasa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~DescripcionDispositivoTests"
```

Esperado: PASAN las ocho.

- [ ] **Paso 5: Confirmar**

```bash
git add Common/Auth/DescripcionDispositivo.cs tests/CoopagcuyApi.Tests/Unitarias/DescripcionDispositivoTests.cs
git commit -m "feat: traducir el User-Agent al nombre del dispositivo"
```

---

## Tarea 9: Una sesión activa por dispositivo

**Archivos:**
- Modificar: `Common/Auth/AuthDtos.cs:38-53` (`SesionActivaDto`)
- Modificar: `Common/Auth/SesionService.cs:38-60` (`EmitirAsync`) y `~140-165` (`ListarActivasAsync`)
- Crear: `tests/CoopagcuyApi.Tests/Integracion/SesionesPorDispositivoTests.cs`

**Interfaces:**
- Consume: `DescripcionDispositivo.Describir(string?)` → `string` (Tarea 8).
- Produce: `SesionActivaDto` gana el campo `string Dispositivo` **al final** del
  registro, después de `EsSesionActual`. Va al final para no desplazar los
  parámetros posicionales existentes.

- [ ] **Paso 1: Escribir las pruebas que fallan**

Crea `tests/CoopagcuyApi.Tests/Integracion/SesionesPorDispositivoTests.cs`:

```csharp
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La pantalla de sesiones mostraba cinco filas del mismo usuario: cada inicio
/// de sesión insertaba una fila nueva sin mirar si esa misma tablet ya tenía
/// sesión abierta. Una tablet, una sesión.
///
/// Las filas no se borran, se revocan: el rastro de auditoría se conserva.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class SesionesPorDispositivoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0102030405";

    private sealed record Sesion(
        int Id, int UsuarioId, string NombreUsuario, string Cedula, string Rol,
        string? CatAsignado, string? DispositivoId, string? UserAgent,
        string? IpCreacion, DateTime FechaCreacion, DateTime FechaUltimoUso,
        DateTime FechaExpiracion, bool EsSesionActual, string Dispositivo);

    private async Task EntrarAsync(string? dispositivoId, string? userAgent = null)
    {
        var cliente = api.ComoAnonimo();
        if (userAgent is not null)
            cliente.DefaultRequestHeaders.Add("User-Agent", userAgent);

        var respuesta = await cliente.PostAsJsonAsync("/api/auth/login", new
        {
            cedula = Cedula,
            password = Sembrador.PasswordPorDefecto,
            dispositivoId
        });
        respuesta.EnsureSuccessStatusCode();
    }

    private async Task<List<Sesion>> SesionesAsync()
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync("/api/auth/sesiones");
        respuesta.EnsureSuccessStatusCode();
        var sesiones = await respuesta.Content.ReadFromJsonAsync<List<Sesion>>();
        sesiones.ShouldNotBeNull();
        return sesiones;
    }

    [Fact]
    public async Task DosIniciosDeSesionDelMismoDispositivo_dejanUnaSolaSesion()
    {
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01");
        await EntrarAsync("tablet-pat-01");

        var sesiones = await SesionesAsync();

        sesiones.Count(s => s.Cedula == Cedula).ShouldBe(1);
    }

    [Fact]
    public async Task DosDispositivosDistintos_mantienenSusDosSesiones()
    {
        // La regla no puede cerrar tablets legítimas de otros compañeros.
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01");
        await EntrarAsync("tablet-pat-02");

        var sesiones = await SesionesAsync();

        sesiones.Count(s => s.Cedula == Cedula).ShouldBe(2);
    }

    [Fact]
    public async Task UnInicioSinDispositivo_noCierraLasSesionesExistentes()
    {
        // Sin identificador no hay forma de saber de qué tablet se trata:
        // revocar por usuario cerraría sesiones de otras tablets legítimas.
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01");
        await EntrarAsync(null);

        var sesiones = await SesionesAsync();

        sesiones.Count(s => s.Cedula == Cedula).ShouldBe(2);
    }

    [Fact]
    public async Task LaSesionDescribeElDispositivo()
    {
        await Sembrador.UsuarioAsync(api, Cedula, RolUsuario.OperadorCAT);

        await EntrarAsync("tablet-pat-01",
            "Mozilla/5.0 (Linux; Android 13; SM-X200) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var sesiones = await SesionesAsync();
        var mia = sesiones.Single(s => s.Cedula == Cedula);

        mia.Dispositivo.ShouldBe("Chrome · Android");
        mia.DispositivoId.ShouldBe("tablet-pat-01");
    }
}
```

- [ ] **Paso 2: Correr las pruebas y verificar que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~SesionesPorDispositivoTests"
```

Esperado: FALLA la compilación (el registro `Sesion` de la prueba tiene un
campo `Dispositivo` que el DTO todavía no devuelve, así que llegaría nulo y la
última prueba fallaría; las dos primeras fallan por contar 2 y 1 en vez de 1 y 2).

- [ ] **Paso 3: Añadir el campo al DTO**

En `Common/Auth/AuthDtos.cs`, reemplaza `SesionActivaDto` por:

```csharp
// Fila de la pantalla de administración de sesiones activas. Nunca incluye
// el token ni su hash: solo metadatos para identificar y revocar.
public record SesionActivaDto(
    int Id,
    int UsuarioId,
    string NombreUsuario,
    string Cedula,
    string Rol,
    string? CatAsignado,
    string? DispositivoId,
    string? UserAgent,
    string? IpCreacion,
    DateTime FechaCreacion,
    DateTime FechaUltimoUso,
    DateTime FechaExpiracion,
    // La sesión de quien está viendo la pantalla, para no auto-desconectarse
    bool EsSesionActual,
    // User-Agent traducido a algo legible ("Chrome · Android"). El User-Agent
    // crudo se conserva encima para auditoría; este es el que se enseña.
    string Dispositivo
);
```

Va **al final** a propósito: los parámetros posicionales anteriores no se
mueven, así que ninguna construcción existente del DTO cambia de significado.

- [ ] **Paso 4: Revocar la sesión previa del mismo dispositivo**

En `Common/Auth/SesionService.cs`, reemplaza `EmitirAsync` por:

```csharp
    public async Task<AuthTokensResultado> EmitirAsync(
        Usuario usuario, string? dispositivoId, string? userAgent, string? ip)
    {
        var plano = TokenSeguro.GenerarTokenPlano();
        var ahora = DateTime.UtcNow;
        var dispositivo = Recortar(dispositivoId, 100);

        // Una tablet, una sesión. Sin esto, cada inicio de sesión dejaba una
        // fila más y la pantalla de sesiones mostraba cinco entradas del mismo
        // usuario, imposible de interpretar para quien tiene que revocar una.
        //
        // Se revoca, no se borra: el rastro de auditoría se conserva.
        //
        // Sin identificador de dispositivo no se revoca nada: no hay forma de
        // saber de qué tablet viene, y revocar por usuario cerraría las
        // sesiones legítimas de las demás.
        if (dispositivo is not null)
        {
            await db.RefreshTokens
                .Where(t => t.UsuarioId == usuario.Id
                         && t.DispositivoId == dispositivo
                         && !t.Revocado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Revocado, true)
                    .SetProperty(t => t.FechaRevocacion, ahora));
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = TokenSeguro.Hash(plano),
            DispositivoId = dispositivo,
            UserAgent = Recortar(userAgent, 300),
            IpCreacion = Recortar(ip, 60),
            FechaCreacion = ahora,
            FechaUltimoUso = ahora,
            FechaExpiracion = ahora.Add(DuracionRefresh)
        });
        await db.SaveChangesAsync();

        return ConstruirResultado(usuario, plano, ahora.Add(DuracionRefresh));
    }
```

- [ ] **Paso 5: Rellenar el campo `Dispositivo` al listar**

En `ListarActivasAsync`, la proyección va a LINQ-to-SQL y no puede llamar a
`DescripcionDispositivo.Describir` dentro del `Select`: EF no sabe traducirlo a
SQL. Se materializa primero y se traduce después. Reemplaza el cuerpo por:

```csharp
    public async Task<List<SesionActivaDto>> ListarActivasAsync(string? refreshTokenActualPlano)
    {
        var hashActual = string.IsNullOrWhiteSpace(refreshTokenActualPlano)
            ? null : TokenSeguro.Hash(refreshTokenActualPlano);
        var ahora = DateTime.UtcNow;

        // Se materializa antes de describir el dispositivo: Describir es
        // código C# y EF no puede traducirlo a SQL dentro del Select.
        var filas = await db.RefreshTokens
            .AsNoTracking()
            .Include(t => t.Usuario)
            .Where(t => !t.Revocado && t.FechaExpiracion > ahora)
            .OrderByDescending(t => t.FechaUltimoUso)
            .Select(t => new
            {
                t.Id,
                t.UsuarioId,
                t.Usuario.NombreCompleto,
                t.Usuario.Cedula,
                Rol = t.Usuario.Rol.ToString(),
                CatAsignado = t.Usuario.CatAsignado == null
                    ? null : t.Usuario.CatAsignado.ToString(),
                t.DispositivoId,
                t.UserAgent,
                t.IpCreacion,
                t.FechaCreacion,
                t.FechaUltimoUso,
                t.FechaExpiracion,
                EsActual = hashActual != null && t.TokenHash == hashActual
            })
            .ToListAsync();

        return filas.Select(f => new SesionActivaDto(
            f.Id, f.UsuarioId, f.NombreCompleto, f.Cedula, f.Rol,
            f.CatAsignado, f.DispositivoId, f.UserAgent, f.IpCreacion,
            f.FechaCreacion, f.FechaUltimoUso, f.FechaExpiracion,
            f.EsActual,
            DescripcionDispositivo.Describir(f.UserAgent))).ToList();
    }
```

- [ ] **Paso 6: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~SesionesPorDispositivoTests"
```

Esperado: PASAN las cuatro.

- [ ] **Paso 7: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: PASAN todas. Si alguna prueba de sesiones existente contaba filas,
ajústala: ahora hay menos por diseño.

- [ ] **Paso 8: Confirmar**

```bash
git add Common/Auth/ tests/
git commit -m "feat: una sesion activa por dispositivo y descripcion legible en la lista"
```

---

## Tarea 10: La pantalla de sesiones muestra el dispositivo

Trabajo en el repo del **frontend**.

**Archivos:**
- Modificar: `src/api/auth.ts` (tipo `SesionActiva`)
- Modificar: `src/pages/Sesiones.tsx`

- [ ] **Paso 1: Añadir el campo al tipo**

En `src/api/auth.ts`, añade `dispositivo: string;` al tipo `SesionActiva`, junto
a los campos que ya existen (`dispositivoId`, `userAgent`, `ipCreacion`).

- [ ] **Paso 2: Pintarlo en la tarjeta**

En `src/pages/Sesiones.tsx`, añade encima del componente:

```tsx
/** Últimos 6 caracteres del identificador: distingue una tablet de otra sin
 *  volcar un UUID entero en una pantalla que se lee en un móvil. */
function tabletCorta(id: string | null): string | null {
    return id ? `#${id.slice(-6)}` : null;
}
```

Y dentro de la tarjeta, justo debajo del párrafo del rol y la cédula, añade:

```tsx
                                <p className="text-xs text-gray-500">
                                    {s.dispositivo}
                                    {tabletCorta(s.dispositivoId)
                                        ? ` · ${tabletCorta(s.dispositivoId)}` : ""}
                                    {s.ipCreacion ? ` · ${s.ipCreacion}` : ""}
                                </p>
```

- [ ] **Paso 3: Compilar y pasar el linter**

```bash
pnpm build
```

Esperado: compila sin errores.

```bash
pnpm lint
```

Esperado: sin errores.

- [ ] **Paso 4: Comprobarlo en el navegador**

Entra como `AdminTecnico` y abre Sesiones. Cada tarjeta debe mostrar navegador,
sistema, identificador corto e IP. Cierra sesión y vuelve a entrar desde la
misma tablet: el número de filas de ese usuario no debe crecer.

- [ ] **Paso 5: Confirmar**

```bash
git add src/
git commit -m "feat: la pantalla de sesiones identifica el dispositivo de cada sesion"
```

---

## Tarea 11: Corregir la guía de movilización

**Archivos:**
- Modificar: `Features/Recepcion/Services/GuiaMovilizacionService.cs:28-33` (consulta) y `:188-201` (celdas de la tabla)
- Crear: `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs`

**Interfaces:**
- Consume: `Sembrador.ProductoraAsync(...)` (Tarea 4).

- [ ] **Paso 1: Escribir la prueba que falla**

La guía es un PDF binario. Comprobar su contenido textual exige extraer el
texto, y el proyecto no tiene librería para eso. Se comprueba lo que sí se puede
comprobar sin añadir dependencias: que el documento se genera, que no está
vacío, y —sobre el objeto de dominio, no sobre el PDF— que la celda se compone
con el nombre de la comunidad.

Crea `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs`:

```csharp
using System.Net;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La guía de movilización imprimía el nombre de la CLASE de la comunidad
/// —"CoopagcuyApi.Features.Catalogos.Models.Comunidad"— porque interpolaba el
/// objeto de navegación en vez de su propiedad Nombre.
///
/// El PDF es binario y el proyecto no tiene extractor de texto, así que estas
/// pruebas cubren dos cosas distintas: que el documento se genera de punta a
/// punta, y que la consulta carga la comunidad de cada cuy —que es la
/// condición sin la cual la celda no puede componerse bien.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class GuiaMovilizacionTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0102030405";

    /// <summary>
    /// Jaula mínima con un animal: lo justo para que la guía tenga que
    /// componer la fila de "DETALLE POR ANIMAL", que es donde estaba el fallo.
    /// </summary>
    private async Task<string> SembrarLoteAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT, comunidadId: 1);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-20260818-001",
            ProductoraId = productora.Id,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 1,
            PesoTotalGramos = 900,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Nicolas Nieves"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        db.CuyRegistros.Add(new CuyRegistro
        {
            LoteId = lote.Id,
            ProductoraId = productora.Id,
            NumeroEnLote = 1,
            PesoGramos = 900,
            ColorPelaje = "Bayo",
            EstadoOreja = "Semiblanda",
            TamanoAnimal = "Grande",
            Estado = EstadoLote.Aceptado
        });
        await db.SaveChangesAsync();

        return lote.CodigoLote;
    }

    [Fact]
    public async Task LaGuiaSeGeneraParaUnLoteConDetallePorAnimal()
    {
        var codigo = await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigo}/guia");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public async Task LaConsultaDeLaGuiaCargaLaComunidadDeCadaCuy()
    {
        // Es la condición previa a que la celda se componga bien: si la
        // comunidad llegara nula, .Nombre reventaría al generar el PDF.
        var codigo = await SembrarLoteAsync();

        await using var db = api.NuevoDbContext();
        var lote = await db.Lotes
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
                .ThenInclude(p => p!.Comunidad)
            .AsNoTracking()
            .FirstAsync(l => l.CodigoLote == codigo);

        var cuy = lote.Cuyes.Single();
        cuy.Productora.ShouldNotBeNull();
        cuy.Productora.Comunidad.ShouldNotBeNull();
        cuy.Productora.Comunidad.Nombre.ShouldBe("Patococha");
    }
}
```

**Antes de correrla**, comprueba los nombres reales de las propiedades de `Lote`:

```bash
grep -n "public" Features/Productoras/Models/Lote.cs
```

Ajusta el sembrado a lo que exista. Si `Lote` tiene campos obligatorios que aquí
faltan, añádelos; si `ProductoraId` es nulable, déjalo igual.

- [ ] **Paso 2: Correr las pruebas y verificar el punto de partida**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~GuiaMovilizacionTests"
```

Anota cuáles pasan. Es probable que las dos pasen ya, porque el defecto es de
formato y no de excepción: el PDF se genera, solo que con texto equivocado. Eso
está bien —estas pruebas son la red que evita que el arreglo rompa la
generación—, pero significa que la corrección del texto se verifica a ojo en el
Paso 5.

- [ ] **Paso 3: Cargar la comunidad explícitamente**

En `Features/Recepcion/Services/GuiaMovilizacionService.cs`, reemplaza la
consulta del lote (líneas 28-33) por:

```csharp
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            // El ThenInclude de Comunidad es explícito a propósito. Funciona
            // igual sin él, porque Productora.Comunidad está marcada como
            // AutoInclude en AppDbContext, pero que un PDF no reviente no
            // debería depender de una configuración que vive en otro archivo.
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
                .ThenInclude(p => p!.Comunidad)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");
```

- [ ] **Paso 4: Corregir las dos celdas**

En el mismo archivo, dentro del bucle `foreach (var cuy in lote.Cuyes...)`,
reemplaza las dos celdas afectadas.

La de la productora — antes:

```csharp
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(cuy.Productora is not null
                                            ? $"{cuy.Productora.NombreCompleto} ({cuy.Productora.Comunidad})"
                                            : "—").FontSize(7);
```

Después:

```csharp
                                    // .Comunidad.Nombre, no .Comunidad: lo
                                    // segundo interpola la ENTIDAD y su
                                    // ToString() imprime el nombre de la clase
                                    // dentro del paréntesis.
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(cuy.Productora is not null
                                            ? $"{cuy.Productora.NombreCompleto} " +
                                              $"({cuy.Productora.Comunidad.Nombre})"
                                            : "—").FontSize(7);
```

La de las características — antes:

```csharp
                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"{cuy.ColorPelaje} · {cuy.EstadoOreja} · {cuy.TamanoAnimal}")
                                        .FontSize(7);
```

Después:

```csharp
                                    // Con rótulo: sin él, "Blanco · Blanda ·
                                    // Normal" se lee como una lista de opciones
                                    // disponibles y no como los datos de ESTE
                                    // animal, que es lo que son.
                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"Pelaje: {cuy.ColorPelaje} · " +
                                              $"Oreja: {cuy.EstadoOreja} · " +
                                              $"Tamaño: {cuy.TamanoAnimal}")
                                        .FontSize(7);
```

La columna de características es `RelativeColumn(2)` en una página A5. El texto
rotulado es bastante más largo. Sube su peso en `ColumnsDefinition` para que no
se estreche a costa del resto:

```csharp
                                    cols.ConstantColumn(25);   // N°
                                    cols.RelativeColumn(3);    // Productora
                                    cols.ConstantColumn(55);   // Peso
                                    cols.RelativeColumn(4);    // Características
                                    cols.ConstantColumn(65);   // Estado
```

- [ ] **Paso 5: Correr las pruebas y revisar el PDF con los ojos**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~GuiaMovilizacionTests"
```

Esperado: PASAN las dos.

Después, arranca la API contra la base de desarrollo, descarga la guía de un
lote real y ábrela. Comprueba: en «DETALLE POR ANIMAL» el paréntesis dice
«(Patococha)» y no el nombre de una clase; las características llevan sus tres
rótulos; y ninguna columna desborda ni parte palabras a mitad.

- [ ] **Paso 6: Confirmar**

```bash
git add Features/Recepcion/ tests/
git commit -m "fix: la guia imprimia el nombre de la clase Comunidad y las caracteristicas sin rotulo"
```

---

## Tarea 12: El logo de COOPAGCUY en los tres PDF

**Archivos:**
- Crear: `Common/Branding/coopagcuy-logo.png`
- Crear: `Common/Branding/BrandingAssets.cs`
- Modificar: `CoopagcuyApi.csproj`
- Modificar: `Features/Recepcion/Services/GuiaMovilizacionService.cs` (cabecera)
- Modificar: `Features/Reportes/Services/ReportesService.cs:965` y `:1220` (cabeceras)
- Modificar: `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs`

**Interfaces:**
- Produce: `BrandingAssets.Logo` → `byte[]` (propiedad estática, cacheada).

- [ ] **Paso 1: Copiar y reducir el logo**

El original está en el repo del frontend
(`public/brand/cuy-logo-full.png`, 254 KB). 254 KB incrustados en cada PDF A5
son excesivos para un documento que se imprime; se reduce a unos 300 px de
ancho.

```bash
mkdir -p Common/Branding
```

Reduce la imagen con la herramienta que tengas a mano y guárdala como
`Common/Branding/coopagcuy-logo.png`. Con ImageMagick:

```bash
magick "C:/Users/nicol/OneDrive/Documents/CoopagcuyFront/coopagcuy-frontend/public/brand/cuy-logo-full.png" -resize 300x Common/Branding/coopagcuy-logo.png
```

Si no tienes ImageMagick, copia el archivo tal cual y anótalo como pendiente;
funciona igual, solo pesa más.

Comprueba el resultado:

```bash
ls -la Common/Branding/coopagcuy-logo.png
```

- [ ] **Paso 2: Declarar el recurso embebido**

En `CoopagcuyApi.csproj`, dentro del primer `<ItemGroup>`, añade:

```xml
    <!-- El logo va embebido en el ensamblado y no como archivo suelto: la
         imagen de la API se publica sobre aspnet SIN wwwroot, así que un
         archivo que no se copiara funcionaría en desarrollo y fallaría en
         producción — y fallaría al generar un PDF, no al arrancar. -->
    <EmbeddedResource Include="Common\Branding\coopagcuy-logo.png" />
```

- [ ] **Paso 3: Escribir la prueba que falla**

Añade a `tests/CoopagcuyApi.Tests/Integracion/GuiaMovilizacionTests.cs`:

```csharp
    [Fact]
    public async Task ElLogoEstaEmbebidoEnElEnsamblado()
    {
        // Si el recurso no se declara en el .csproj, esto falla aquí y no
        // dentro de la generación de un PDF en producción.
        var logo = CoopagcuyApi.Common.Branding.BrandingAssets.Logo;

        logo.ShouldNotBeNull();
        logo.Length.ShouldBeGreaterThan(0);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LaGuiaConLogoPesaMasQueElUmbralMinimo()
    {
        // Comprobación indirecta pero real: sin extractor de PDF no se puede
        // afirmar "hay una imagen", pero un PDF con el logo incrustado pesa
        // claramente más que uno de solo texto.
        var codigo = await SembrarLoteAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/recepcion/lotes/{codigo}/guia");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(10_000);
    }
```

- [ ] **Paso 4: Correr las pruebas y verificar que fallan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~GuiaMovilizacionTests"
```

Esperado: FALLA la compilación con `CS0234` o `CS0103` — `BrandingAssets` no
existe todavía.

- [ ] **Paso 5: Escribir el lector del recurso**

Crea `Common/Branding/BrandingAssets.cs`:

```csharp
using System.Reflection;

namespace CoopagcuyApi.Common.Branding;

/// <summary>
/// Imágenes de marca embebidas en el ensamblado, disponibles para los
/// documentos que genera el sistema.
///
/// Se leen una sola vez y se guardan en memoria: los PDF se generan por
/// petición y volver a leer el flujo del ensamblado en cada uno sería trabajo
/// repetido para unos pocos kilobytes que no cambian nunca.
///
/// Si el recurso no está declarado en el .csproj, esto lanza al primer acceso
/// con un mensaje que dice exactamente qué falta — mejor que un PDF sin logo
/// que nadie nota hasta que el documento ya está impreso.
/// </summary>
public static class BrandingAssets
{
    private const string RecursoLogo = "CoopagcuyApi.Common.Branding.coopagcuy-logo.png";

    private static readonly Lazy<byte[]> _logo = new(() => Leer(RecursoLogo));

    /// Logo de COOPAGCUY en PNG, para la cabecera de los documentos.
    public static byte[] Logo => _logo.Value;

    private static byte[] Leer(string nombre)
    {
        var ensamblado = Assembly.GetExecutingAssembly();

        using var flujo = ensamblado.GetManifestResourceStream(nombre)
            ?? throw new InvalidOperationException(
                $"El recurso embebido '{nombre}' no existe. Comprueba que " +
                $"CoopagcuyApi.csproj lo declara como <EmbeddedResource>. " +
                $"Recursos disponibles: " +
                $"{string.Join(", ", ensamblado.GetManifestResourceNames())}");

        using var memoria = new MemoryStream();
        flujo.CopyTo(memoria);
        return memoria.ToArray();
    }
}
```

El nombre del recurso lo forma MSBuild como
`<RootNamespace>.<ruta con puntos>.<archivo>`. Si la prueba falla, el mensaje de
la excepción lista los nombres reales: copia de ahí el correcto.

- [ ] **Paso 6: Pintar el logo en la guía**

En `Features/Recepcion/Services/GuiaMovilizacionService.cs`, reemplaza el
`page.Header()` completo por:

```csharp
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // El logo va primero y con ancho fijo: en A5 una
                        // imagen que crezca con el contenido desplazaría el
                        // código de lote fuera de la página.
                        row.ConstantItem(48).PaddingRight(8).AlignMiddle()
                            .Image(BrandingAssets.Logo).FitWidth();

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("GUÍA DE MOVILIZACIÓN")
                                .FontSize(15).Bold().FontColor("#2E7D32");
                            c.Item().Text("COOPAGCUY — Cuy Azuayito")
                                .FontSize(10).FontColor("#555555");
                        });
                        row.ConstantItem(110).AlignRight().Column(c =>
                        {
                            c.Item().Text(lote.CodigoLote)
                                .FontSize(13).Bold().FontColor("#B71C1C");
                            c.Item().Text($"Emitida: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(7).FontColor("#777777");
                        });
                    });
                    col.Item().PaddingTop(4).BorderBottom(2).BorderColor("#2E7D32");
                });
```

Añade el `using` arriba del archivo:

```csharp
using CoopagcuyApi.Common.Branding;
```

- [ ] **Paso 7: Pintar el logo en los dos PDF de reportes**

En `Features/Reportes/Services/ReportesService.cs` hay dos `page.Header()`, uno
en cada generador (a partir de las líneas 965 y 1220). En ambos, añade como
**primer** elemento de la fila de la cabecera:

```csharp
                        row.ConstantItem(60).PaddingRight(10).AlignMiddle()
                            .Image(BrandingAssets.Logo).FitWidth();
```

Son 60 y no 48 porque estos dos documentos son A4, no A5.

Añade el `using` arriba del archivo:

```csharp
using CoopagcuyApi.Common.Branding;
```

- [ ] **Paso 8: Correr las pruebas y verificar que pasan**

```bash
docker compose -f docker-compose.tests.yml run --rm tests dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~GuiaMovilizacionTests"
```

Esperado: PASAN las cuatro.

- [ ] **Paso 9: Revisar los tres documentos con los ojos**

Descarga los tres PDF (guía de movilización, ficha de lote y ficha de lote
faenado) y ábrelos. El logo debe verse nítido, alineado con el título y sin
deformarse. Si sale pixelado, el redimensionado del Paso 1 se pasó de agresivo:
rehazlo a 400 px.

- [ ] **Paso 10: Correr la batería completa**

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

Esperado: PASAN todas.

- [ ] **Paso 11: Confirmar**

```bash
git add CoopagcuyApi.csproj Common/Branding/ Features/ tests/
git commit -m "feat: incorporar el logo de COOPAGCUY a los documentos PDF"
```

---

## Cierre

Al terminar las doce tareas:

1. Corre la batería completa una última vez y comprueba que pasa entera.
2. Compila el frontend (`pnpm build`) y pasa el linter (`pnpm lint`).
3. Recupera el hallazgo de la Tarea 1 y escribe el plan de la Fase 4 —el
   arreglo del reporte de Salida— como plan aparte.
4. Usa superpowers:finishing-a-development-branch para decidir cómo integrar
   el trabajo.

### Una desviación consciente respecto a la especificación

La especificación pedía dos pruebas que este plan **no** implementa tal cual:
«la guía no contiene la cadena `CoopagcuyApi.Features`» y «la guía contiene los
rótulos `Pelaje:`, `Oreja:` y `Tamaño:`». Ambas exigen extraer el texto de un
PDF, y el proyecto no tiene librería para eso; añadir una solo para esto es
una dependencia más que auditar con Trivy por dos aserciones.

En su lugar, la Tarea 11 comprueba lo que sí es comprobable sin dependencias
nuevas —que el documento se genera y que la consulta carga la comunidad de cada
cuy, sin lo cual la celda no puede componerse bien— y el texto se verifica a
ojo abriendo el PDF (Paso 5). Si más adelante se quiere la comprobación
automática, el paquete a valorar es `PdfPig`, que es de licencia permisiva.

Quedan dos decisiones abiertas que la especificación deja anotadas y el usuario
todavía no ha resuelto (sección «Cierre» de la conversación de diseño):

- Si el admin técnico debe conservar la descarga de la guía de movilización
  (`GET /api/recepcion/lotes/{codigo}/guia`) para poder reimprimirla a petición
  de un operador. Este plan asume que **no** la conserva.
- Si el botón de exportación General debe desaparecerle en vez de generar un
  libro recortado. Este plan implementa el libro recortado (Tarea 3).

Ninguna de las dos bloquea la ejecución; si el usuario decide lo contrario, son
cambios de una línea cada uno.

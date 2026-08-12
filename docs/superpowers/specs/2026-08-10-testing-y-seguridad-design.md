# Pruebas automatizadas e integridad del sistema — Diseño

**Fecha:** 2026-08-10
**Alcance:** repos `CoopagcuyApi` y `coopagcuy-frontend`
**Objetivo:** robustez real de producción. Pocos tests, profundos, sobre los
invariantes que corrompen datos en campo. No se busca cobertura cosmética.

---

## 1. Estado de partida

| | Hoy |
|---|---|
| API (.NET 8) | Un solo `.csproj`. Cero proyectos de test. CI = `dotnet build`. |
| Front (React 19 / Vite 8) | Cero tests. `pnpm lint` existe pero **no corre en CI**; solo `tsc -b` + `vite build`. |
| Seguridad | Sin CodeQL, sin Trivy, sin auditoría automática de dependencias. |
| Ramas | Commits directos a `main`. Los workflows disparan en `develop` y `main`. |

Superficie crítica sin ninguna verificación automática: tres advisory locks de
PostgreSQL, la idempotencia del sync offline, el control de stock de despacho,
el tope de devolución, la rotación de refresh tokens con reuse-detection y la
segmentación por centro de acopio (CAT).

## 2. Decisiones tomadas

1. **Objetivo:** robustez de producción, no evidencia documental.
2. **Alcance:** las tres capas — integración del API, unitarias del front, E2E.
3. **Ejecución local:** dentro de Docker, igual que ya se hace con `dotnet ef`,
   para esquivar el bloqueo de Smart App Control sobre DLLs en OneDrive.
4. **Gates:** bloquean en Pull Request; en push directo a `main` solo reportan.
5. **Base de datos de test:** Postgres real levantado por `docker compose` en
   local y como *service container* en CI. **No** Testcontainers: dentro de un
   contenedor exigiría montar `/var/run/docker.sock`.
6. **Ubicación del E2E:** repo del API, como workflow reutilizable que ambos
   repos invocan con `workflow_call`.
7. **Visibilidad de los repos:** públicos → CodeQL y subida de SARIF sin costo.

### Enfoques descartados

**EF InMemory + mocks.** No implementa advisory locks, no aplica índices únicos
y no traduce SQL. Habría dejado pasar todos los bugs reales registrados en este
proyecto, incluida la regresión de `GroupBy` por instancia de entidad.

**Testcontainers.** Más elegante, pero incompatible con la decisión 3 sin
docker-in-docker. El *fixture* se escribe de modo que migrar después sea un
cambio localizado en `BaseDatosFixture`.

---

## 3. Capa 1 — Pruebas del API

### Estructura

```
CoopagcuyApi/
├── CoopagcuyApi.slnx                    ← + referencia al proyecto de tests
├── docker-compose.tests.yml
└── tests/CoopagcuyApi.Tests/
    ├── CoopagcuyApi.Tests.csproj        xUnit · Shouldly · Respawn
    ├── Infra/
    │   ├── ApiFactory.cs
    │   ├── BaseDatosFixture.cs
    │   ├── ColeccionApi.cs
    │   ├── Jwt.cs
    │   └── Semillas.cs
    ├── Unitarias/
    │   ├── ValidadorCedulaTests.cs
    │   ├── AlcanceUsuarioTests.cs
    │   └── FechaUtcTests.cs
    └── Integracion/
        ├── EntregasTests.cs
        ├── SyncOfflineTests.cs
        ├── VinculacionTests.cs
        ├── FaenamientoTests.cs
        ├── DespachoTests.cs
        ├── DevolucionTests.cs
        ├── PagosTests.cs
        ├── SesionesTests.cs
        ├── SegmentacionCatTests.cs
        └── ReportesTests.cs
```

### Componentes de `Infra/`

| Componente | Responsabilidad | Depende de |
|---|---|---|
| `ApiFactory` | Deriva de `WebApplicationFactory<Program>`. Sustituye la cadena de conexión de Neon por `TEST_DB_CONNECTION`. Expone clientes HTTP ya autenticados por rol y una fábrica de `AppDbContext` para aserciones directas. | `BaseDatosFixture`, `Jwt` |
| `BaseDatosFixture` | Aplica migraciones una vez por colección. Entre tests trunca las tablas con Respawn. | `TEST_DB_CONNECTION` |
| `ColeccionApi` | `[CollectionDefinition]` que comparte una sola instancia de `ApiFactory`. Serializa los tests: obligatorio, porque comparten una base de datos. | — |
| `Jwt` | Emite tokens firmados con la misma `Jwt:Key` de la configuración de test, para Admin, OperadorCAT (con claim `cat`) y OperadorFaenamiento. | — |
| `Semillas` | Constructores de escenario: jaula recibida → llegada confirmada → lote faenado. Devuelven los Ids que los tests necesitan. | `ApiFactory` |

Cada uno se entiende y se cambia sin leer los demás; los tests solo tocan
`ApiFactory` y `Semillas`.

### Aislamiento entre tests

El código de producción abre sus propias transacciones (`BeginTransactionAsync`
dentro de `CreateExecutionStrategy`), así que envolver cada test en una
transacción y hacer rollback **no funciona**. Se usa Respawn: migrar una vez,
truncar entre tests.

### Invariantes cubiertos

**Unitarias** (sin base de datos):

- `ValidadorCedula` — algoritmo ecuatoriano completo, contra el fixture compartido.
- `AlcanceUsuario` — `FueraDeAlcance` devuelve `false` para admin, `true` para un
  operador de otro CAT, y `true` para un operador sin claim `cat`.
- `FechaUtc.Normalizar` — comportamiento con `Npgsql.EnableLegacyTimestampBehavior`.

**Integración** (Postgres real):

| Archivo | Invariante |
|---|---|
| `EntregasTests` | El advisory lock `entrega-CAT` serializa dos entregas simultáneas: no se abren dos jaulas ni se repiten números de cuy. |
| `SyncOfflineTests` | La misma entrega enviada dos veces por el mismo dispositivo se marca sincronizada sin duplicar (índice único `DispositivoId, IdCliente`). Los resultados se emparejan por `idCliente`. |
| `VinculacionTests` | Cédula válida sin productora → cuarentena, no entra a la jaula. El admin resuelve o descarta. |
| `FaenamientoTests` | El lock `fae-yyyyMMdd` no repite códigos FAE. Una jaula sin `FechaRecepcionPlanta` **no** aparece en `lotes-disponibles`. Se verifica que la traducción EF del pre-filtro de `LotesDisponibles` ejecuta sin excepción — hoy nunca se probó en runtime. |
| `DespachoTests` | Dos despachos simultáneos del mismo cuy: uno 200, otro 409, y una sola fila en `DespachoCuys`. Un cuy rechazado en planta no se despacha. Un cuy de otro lote FAE no se despacha. |
| `DevolucionTests` | Sin despacho previo → 409. Pasarse de `enviadas − ya devueltas` → 409. Una devolución **no** devuelve animales al saldo despachable. |
| `PagosTests` | Crédito con 1 letra → 409. Crédito de $100 en 4 letras → `valorPorLetra = 25`. |
| `SesionesTests` | El refresh rota el token. Reusar un refresh ya rotado revoca **toda la familia**. La expiración absoluta de 7 días se respeta. |
| `SegmentacionCatTests` | Un OperadorCAT de "PAT" recibe 403 al leer lotes, pagos, productoras y movilizaciones de "NIE". Cubre los endpoints listados en `AlcanceUsuario`. |
| `ReportesTests` | Tres animales de la misma productora se agrupan como una fila con cantidad 3, no tres filas de 1 — la regresión de `AsNoTracking` + `GroupBy` por instancia. Los tres reportes (entrada, tránsito, salida) generan Excel válido. |

### Test de referencia

```csharp
[Collection(ColeccionApi.Nombre)]
public class DespachoTests(ApiFactory api) : IAsyncLifetime
{
    [Fact]
    public async Task DosDespachosSimultaneosDelMismoCuy_soloUnoGana()
    {
        var escenario = await Semillas.LoteFaenadoAsync(api, cat: "PAT", animales: 10);
        var cuyes = escenario.CuyIds.Take(4).ToArray();

        var cliente = api.ComoOperadorFaenamiento();
        var cuerpo = new
        {
            loteFaenadoId     = escenario.LoteFaenadoId,
            cuyFaenamientoIds = cuyes,
            clienteDestino    = "Mercado Feria Libre",
            fechaDespacho     = DateTime.UtcNow,
            responsable       = "test",
            chofer = "J. Pérez", ruta = "Cuenca-Azogues",
            tipoMercado = "Local", ciudad = "Cuenca", pais = "Ecuador"
        };

        var respuestas = await Task.WhenAll(
            cliente.PostAsJsonAsync("/api/faenamiento/despachos", cuerpo),
            cliente.PostAsJsonAsync("/api/faenamiento/despachos", cuerpo));

        respuestas.Select(r => r.StatusCode).OrderBy(c => c)
            .ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);

        await using var db = api.NuevoDbContext();
        (await db.DespachoCuys.CountAsync(dc => cuyes.Contains(dc.CuyFaenamientoId)))
            .ShouldBe(4);
    }

    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync()    => Task.CompletedTask;
}
```

Este único test ejercita el advisory lock, el índice único como red final y el
mapeo a 409 del exception handler global. Ninguna de las tres cosas existe fuera
de Postgres.

### Ejecución

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

`docker-compose.tests.yml` levanta `postgres:16-alpine` con healthcheck y corre
`dotnet test` en `mcr.microsoft.com/dotnet/sdk:8.0` con el repo montado. En CI el
Postgres es un *service container* y el comando es `dotnet test` a secas: **el
código de los tests es idéntico en ambos entornos**.

---

## 4. Capa 2 — Pruebas del front

Stack: **Vitest 3 + @testing-library/react + happy-dom**, con `fake-indexeddb`
y `msw`.

| Archivo bajo prueba | Invariante |
|---|---|
| `utils/validarCedula.ts` | Coincide exactamente con `ValidadorCedula.cs`. |
| `services/db.ts` | Cola offline y `productoras_cache` sobre IndexedDB v2; la migración desde v1 no pierde datos. |
| `hooks/useOfflineSync.ts` | El guardia `useRef` impide dos syncs concurrentes. Los resultados se emparejan por `idCliente`. Lo marcado `en_revision` no se reenvía. |
| `api/client.ts` | El interceptor 401 refresca y reintenta una sola vez, y **excluye** `/api/auth/login`. |
| `api/tokenStore.ts`, `api/session.ts` | El access token nunca se persiste en `localStorage`; la identidad no-secreta sí. |

Los componentes de formulario no llevan pruebas unitarias: su valor está en el
flujo completo, que cubre Playwright.

### Fixture compartido de cédulas

`validarCedula.ts` y `ValidadorCedula.cs` son espejos que pueden desincronizarse
en silencio. Un único archivo `tests/fixtures/cedulas.json` — con casos válidos,
inválidos y el motivo de cada uno — lo consumen **las dos suites**. Cambiar una
regla en un lado pone en rojo el otro.

El original vive en el repo del API, en `tests/fixtures/cedulas.json`. El front
mantiene una copia en `src/utils/__tests__/cedulas.json` que consume su suite de
Vitest. Como ambos repos son públicos, un paso del CI del front descarga el
original y lo compara:

```bash
curl -fsSL https://raw.githubusercontent.com/NicolasBosak/CoopagcuyApi/main/tests/fixtures/cedulas.json \
  | diff - src/utils/__tests__/cedulas.json
```

Si difieren, el job falla. Así el front no depende de un checkout del repo del
API, pero la copia tampoco puede quedar desactualizada en silencio.

### Cierre de hueco

`pnpm lint` se añade al workflow del front. Hoy existe el script pero el
pipeline solo ejecuta `pnpm build`.

---

## 5. Capa 3 — E2E con Playwright

Vive en el repo del **API**, en `tests/e2e/`. La pila se levanta con compose:
`postgres` + el API construido desde el `Dockerfile` local + el `dist` del front
servido por nginx.

En local, el front se toma del repo hermano
(`../../CoopagcuyFront/coopagcuy-frontend`), que ya existe en esa ruta.

### Disparo desde ambos repos: workflow reutilizable

Un E2E que solo corre en PRs del API dejaría sin cubrir justo los cambios de
interfaz. Se resuelve definiendo el E2E **una vez** en el repo del API como
workflow reutilizable (`on: workflow_call`) e invocándolo desde **los dos**
repos. Ambos son públicos, así que no hace falta PAT ni configurar acceso.

Un workflow invocado con `uses:` se ejecuta en el contexto del que llama, de
modo que sus jobs aparecen como checks nativos en el PR del front y branch
protection puede exigirlos como cualquier otro. Por eso se descarta
`repository_dispatch`: dispara, pero obliga a publicar el estado a mano con la
API de commit statuses y a mantener un PAT de larga vida.

El workflow recibe los dos refs como entradas y hace checkout explícito de
ambos repos en subdirectorios:

```yaml
# CoopagcuyApi/.github/workflows/e2e.yml
on:
  workflow_call:
    inputs:
      api_ref:   { type: string, default: develop }
      front_ref: { type: string, default: develop }

jobs:
  e2e:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          repository: NicolasBosak/CoopagcuyApi
          ref: ${{ inputs.api_ref }}
          path: api
      - uses: actions/checkout@v4
        with:
          repository: NicolasBosak/coopagcuy-frontend
          ref: ${{ inputs.front_ref }}
          path: front
      # mkcert · compose up · pnpm playwright test
```

Cada repo lo invoca pasando **su** SHA y dejando el otro lado en su rama
estable, de forma que cada cambio se prueba contra el contrato vigente del otro:

```yaml
# PR del front
jobs:
  e2e:
    uses: NicolasBosak/CoopagcuyApi/.github/workflows/e2e.yml@main
    with:
      front_ref: ${{ github.event.pull_request.head.sha }}
      api_ref:   develop
```

En el PR del API los refs se invierten.

**Costos aceptados.** El `uses:` apunta a `@main` del repo del API: un cambio al
propio workflow de E2E no puede probarse desde un PR del front sin mergearlo
antes. Y el PR del front pasa a durar lo que dure el E2E, porque construye la
imagen del API; se mitiga con `paths-ignore` para cambios que no tocan `src/`.

### Alternativas descartadas

**Repo `coopagcuy-e2e` independiente.** Simétrico, pero seguiría necesitando
`workflow_call` para invocarse, así que no resuelve nada adicional; suma un
tercer repo que mantener sincronizado y empeora el ciclo local.

**Monorepo.** Es lo único que elimina el problema de raíz, y el caso es
defendible: casi toda feature de este proyecto toca API y front a la vez — el
bug de `api/reportes.ts` del 2026-07-08 fue exactamente eso, el front desplegado
llamando endpoints que la imagen del API no tenía. Un PR atómico lo habría hecho
imposible. Pero es una migración seria (historia, dos pipelines de despliegue,
filtros de rutas, cableado de SWA y Container Apps) y no cabe dentro de este
trabajo. Queda registrada como decisión aparte, a evaluar cuando termine el
pilotaje en campo.

### Escenarios

1. **Login por cédula** → landing correcto según rol.
2. **Armar jaula multi-productora → cerrar lote.** Cubre `FormLote.tsx` y levanta
   el bloqueo que impedía partir ese archivo de 822 líneas.
3. **Wizard de faenamiento** → lote FAE generado.
4. **Despacho con mercado → devolución** rechazada al superar el tope.
5. **Modo offline.** `context.setOffline(true)`, registrar entregas, volver
   online, verificar que sincroniza sin duplicar. El de mayor valor de los cinco.

### Restricción de HTTPS

El refresh token viaja en cookie `httpOnly + Secure + SameSite=None`. Sobre
`http://localhost` el navegador la descarta y todo el E2E falla en el login. La
pila E2E se sirve por HTTPS con certificado generado por `mkcert`, y su origen
se añade a `Cors:AllowedOrigins` en la configuración de test.

---

## 6. Capa 4 — CodeQL

Un workflow `codeql.yml` por repo. Dispara en PR a `develop`/`main`, en push a
`main`, y semanalmente por cron.

- **API:** `language: csharp`, **`build-mode: manual`** ejecutando
  `dotnet build CoopagcuyApi.csproj -c Release`. No se usa `autobuild`: la
  solución es un `.slnx`, formato nuevo que el autobuild puede no resolver.
- **Front:** `language: javascript-typescript`, `build-mode: none`.
- **Suite:** `security-extended`. El sistema maneja autenticación, SQL crudo y
  multi-tenancy por CAT; la suite por defecto omite flujos de taint relevantes.

Los repos son públicos, así que los resultados se publican en la pestaña
*Security* sin costo.

---

## 7. Capa 5 — Trivy y dependencias

| Escaneo | Qué atrapa | Gate en PR |
|---|---|---|
| `image` sobre la imagen construida, antes del push a ghcr | CVEs del SO de `aspnet:8.0` y de las DLLs publicadas | HIGH/CRITICAL con parche disponible (`--ignore-unfixed`) |
| `fs` sobre el repo | Dependencias vulnerables de NuGet y pnpm | HIGH/CRITICAL con parche |
| `secret` | Cadena de Neon o `Jwt:Key` commiteada | Cualquier hallazgo |
| `config` sobre el `Dockerfile` | Malas prácticas de imagen | Informativo |

Se suman `dotnet list package --vulnerable --include-transitive` y
`pnpm audit --audit-level high`, y un `dependabot.yml` por repo para nuget, npm,
github-actions y docker.

### Dos correcciones que este bloque exige

1. **`trivy fs` no puede auditar el proyecto .NET hoy.** No existe
   `packages.lock.json` y sin él Trivy no ve las dependencias transitivas. Se
   activa `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` en
   el `.csproj`, lo que además vuelve reproducibles los `restore`.
2. **El contenedor corre como root.** El `Dockerfile` no tiene instrucción
   `USER`. Se añade un usuario sin privilegios.

---

## 8. Capa 6 — Cableado en CI y gates

### Repo API

```
build-test  ──►  e2e  ──►  deploy
     ▲            ▲          │
     └── codeql ──┘          ├─ build imagen (load: true, sin push)
     └── seguridad ──────────┤  └─ trivy image   ◄── gate
                             ├─ docker push a ghcr
                             ├─ migraciones EF
                             ├─ az containerapp update
                             └─ smoke test /health
```

`docker/build-push-action` hoy construye y publica en un solo paso, así que no
hay dónde insertar Trivy. Se parte en `load: true` → escanear → `docker push`.

### Repo front

`build` (con `pnpm lint` y `pnpm test` añadidos) → `codeql` → `seguridad` →
`e2e` (invocando el workflow reutilizable del repo del API) → `deploy`.

### Política de gates

| Evento | Tests | CodeQL | Trivy |
|---|---|---|---|
| PR a `develop`/`main` | bloquea | bloquea severidad *error* | bloquea HIGH/CRITICAL con parche |
| Push directo a `main` | bloquea el deploy | solo reporta | solo reporta |

Se implementa condicionando `exit-code` al valor de `github.event_name`, sin
duplicar workflows. Requiere activar **branch protection** en `develop` y `main`
con esos checks marcados como obligatorios; sin eso los gates son decorativos.

---

## 9. Cambios en código de producción

Este diseño toca el código de producción lo mínimo. Cuatro cambios, todos
justificados por una capa concreta:

| Cambio | Archivo | Motivo |
|---|---|---|
| `public partial class Program;` al final | `Program.cs` | `WebApplicationFactory<T>` necesita un tipo público al que anclarse; hoy son *top-level statements*. |
| `<RestorePackagesWithLockFile>true</...>` | `CoopagcuyApi.csproj` | Sin `packages.lock.json`, `trivy fs` no audita NuGet. |
| Instrucción `USER` sin privilegios | `Dockerfile` | Hallazgo de `trivy config`. |
| Excluir `tests/` del contexto | `.dockerignore` | Evita invalidar la caché de capas al tocar tests. |

---

## 10. Manejo de errores del andamiaje

- **Postgres no disponible al arrancar los tests:** el healthcheck del compose y
  el `service container` de CI retrasan el arranque. Si aun así falla, el
  `BaseDatosFixture` reintenta la conexión durante 30 s antes de abortar con un
  mensaje que nombra `TEST_DB_CONNECTION`.
- **Migración fallida en el fixture:** aborta la colección entera con el error de
  EF sin enmascarar. No se hace fallback a `EnsureCreated`: usar un esquema
  distinto al de producción invalidaría los tests.
- **Test que deja datos:** Respawn trunca en `InitializeAsync`, no en
  `DisposeAsync`, para que un test que revienta a mitad no contamine al siguiente.
- **E2E inestable:** los cinco escenarios corren con `retries: 1` en CI y `0` en
  local. Un test que solo pasa al reintentar se trata como fallo a investigar, no
  como verde.
- **Trivy con CVE sin parche:** `--ignore-unfixed` evita bloquear el deploy por
  algo que no se puede arreglar. Las CVEs sin parche siguen apareciendo en el
  reporte.

---

## 11. Orden de implementación

| Fase | Contenido | Justificación del orden |
|---|---|---|
| **0** | Los cuatro cambios de la sección 9, proyecto de tests vacío con un test trivial, `docker-compose.tests.yml`, job de CI que lo ejecuta | Valida la tubería completa antes de escribir un test real. Si el compose o Smart App Control fallan, se descubre aquí y no con 40 tests escritos. |
| **1** | CodeQL ×2, Trivy, Dependabot, `pnpm lint` en CI | Lo primero con valor: horas de trabajo, sin dependencias. Da control de integridad en marcha mientras avanza la fase 3. |
| **2** | Unitarias del API, `cedulas.json` compartido, Vitest en el front | Rápidas, sin infraestructura. |
| **3** | Integración del API: locks, idempotencia, stock, tope, sesiones, CAT, reportes | El grueso del esfuerzo y del valor. |
| **4** | Playwright: cinco flujos, incluido offline | El más caro; requiere HTTPS con `mkcert`. |
| **5** | Branch protection y activación de los gates | Al final, cuando todo pasa en verde, para no bloquearse a uno mismo. |

## 12. Criterios de aceptación

1. `docker compose -f docker-compose.tests.yml run --rm tests` pasa en verde en
   la máquina del desarrollador, sin tocar Neon.
2. Los diez archivos de integración cubren los invariantes de la tabla de la
   sección 3.
3. Un PR con un cambio que rompa cualquiera de esos invariantes queda bloqueado.
4. `pnpm test` y `pnpm lint` pasan y corren en el CI del front.
5. Los cinco escenarios de Playwright pasan contra la pila levantada por compose,
   y el mismo workflow aparece como check obligatorio en los PRs de **ambos**
   repos.
6. La pestaña *Security* de ambos repos muestra resultados de CodeQL y Trivy.
7. `trivy image` no reporta HIGH/CRITICAL con parche disponible sobre la imagen
   que se publica.
8. `develop` y `main` tienen branch protection con los checks obligatorios.

## 13. Fuera de alcance

- Pruebas de carga o rendimiento.
- Mutation testing.
- Umbral de cobertura como gate. Se recolecta cobertura para orientar, pero un
  porcentaje como puerta empuja a escribir tests inútiles, justo lo contrario
  del objetivo de esta especificación.
- Migrar el andamiaje a Testcontainers.
- Unificar ambos repos en un monorepo. Registrado en la sección 5 como decisión
  pendiente, a evaluar tras el pilotaje.

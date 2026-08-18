# Permisos por rol, sesiones por dispositivo, guías PDF y reporte de Salida — Diseño

**Fecha:** 2026-08-18
**Alcance:** repos `CoopagcuyApi` y `coopagcuy-frontend`
**Objetivo:** ajustar el alcance de dos roles al trabajo que realmente hacen,
hacer legible la pantalla de sesiones, corregir dos defectos de la guía de
movilización, poner la marca de la organización en los PDF, y averiguar por qué
el reporte de Salida no refleja los despachos nuevos.

---

## 1. Problema y contexto

Seis peticiones llegaron juntas desde el uso real del piloto. Tres son de
permisos y presentación, dos son defectos confirmados en el código, y una es un
fallo que todavía no tiene causa identificada.

No comparten módulo, así que este documento las trata como cambios
independientes con un orden de ejecución, no como una sola función.

### Estado del diagnóstico al escribir este diseño

| # | Petición | Estado |
|---|---|---|
| 1 | Acotar el admin técnico a soporte | Causa localizada |
| 2 | Sesiones duplicadas de un mismo usuario | Causa localizada |
| 3 | Operador CAT gestiona productoras de su CAT | Función inexistente, hay que construirla |
| 4 | Guía de movilización muestra basura de la BDD | Causa localizada |
| 5 | Logo de COOPAGCUY en los PDF | Función inexistente, hay que construirla |
| 6 | Reporte de Salida no muestra despachos nuevos | **Sin causa identificada** |

El punto 6 no se parchea a ciegas: la fase 0 obtiene la evidencia antes de
tocar código.

---

## 2. Decisiones tomadas

1. **El admin técnico pierde el Panel y toda la operación**, no solo del menú:
   también de los `[Authorize]` de la API. Ocultar la navegación sin cerrar los
   endpoints no es una restricción de permisos, es una restricción cosmética.
2. **De Reportes conserva solo la parte administrativa**: Productoras, CAT,
   Novedades y Devoluciones. Pierde Entrada, Tránsito y Salida, que siguen el
   flujo físico del producto.
3. **El alcance del operador de CAT es su centro de acopio**, no una comunidad
   suelta. `Usuario.CatAsignado` y el claim `cat` ya existen; una comunidad
   pertenece a un CAT vía `Comunidad.CatReferencia`. No se añade ningún campo
   ni migración.
4. **El operador de CAT gestiona productoras de su CAT por completo**: crear,
   editar, desactivar y reactivar. El historial de auditoría sigue siendo solo
   de administradores.
5. **Una sesión por dispositivo**: al iniciar sesión se revoca la sesión activa
   previa del mismo dispositivo. No se borran filas; se revocan, para no perder
   el rastro de auditoría.
6. **El logo viaja como recurso embebido en el ensamblado**, no como archivo en
   disco. La imagen de la API es `mcr.microsoft.com/dotnet/aspnet` sin
   `wwwroot`; un archivo suelto que falte rompería la generación del PDF en
   producción y no en desarrollo.
7. **La fase 0 es diagnóstico, no implementación.** Su entregable es una prueba
   que falla, no un arreglo.

---

## 3. Fase 0 — Diagnóstico del reporte de Salida (#6)

### Lo que ya se descartó leyendo el código

| Hipótesis | Por qué se descarta |
|---|---|
| Caché del service worker sobre la API | `vite.config.ts` cachea solo `png/jpg/jpeg/svg/woff2?`; `runtimeCaching` no toca `/api/`. |
| Caché de respuesta en el servidor | No hay `UseResponseCaching` ni `UseOutputCache` en `Program.cs`. |
| Caché de react-query | `staleTime: 30_000`. Y el Excel es una descarga nueva en cada clic. |
| Rango de fechas del front | `inicioMes()` → `hoy()` da `2026-08-01`…`2026-08-18`; `RangoUtc` convierte el límite superior en `2026-08-19T00:00Z` exclusivo. Cubre hoy. |
| El filtro por CAT descarta filas | `ReporteSalidaAsync` ignora `filtro.CentroAcopio`; no filtra por centro. |
| Normalización de la fecha al guardar | `FechaUtc.Normalizar` trata `Unspecified` como UTC y `Local` lo convierte. El front manda ISO con `Z`. |

### El dato que reduce el espacio de búsqueda

Los despachos nuevos **sí** aparecen en la pantalla Despacho. Esa pantalla llama
a `listarDespachos()` **sin parámetros**, así que `ListarDespachosAsync` no
aplica ningún filtro de fecha. `ReporteSalidaAsync` sí lo aplica. Ambas leen
`db.Despachos`.

Conclusión: la fila existe. La diferencia entre las dos consultas es el rango de
fechas y la forma de los `Include`.

### Procedimiento

**Paso 1 — leer el valor almacenado.**

```sql
SELECT "Id", "FechaDespacho", "ClienteDestino", "LoteFaenadoId", "LoteId"
FROM "Despachos"
ORDER BY "Id" DESC
LIMIT 10;
```

**Paso 2 — clasificar según lo que devuelva.**

| Observación | Causa | Arreglo |
|---|---|---|
| `FechaDespacho` fuera de `[2026-08-01, 2026-08-19)` | Desfase de zona horaria u hora futura al guardar | Corregir el punto donde se produce el desfase, en `FormDespacho.tsx` o en `FechaUtc.Normalizar` |
| `FechaDespacho` dentro del rango | La consulta EF del reporte descarta filas que sí cumplen el `Where` | Comparar el SQL generado por las dos consultas; sospechar de los `Include` de `ReporteSalidaAsync`, que no usa `AsSplitQuery()` mientras `ListarDespachosAsync` sí |
| Las filas nuevas no están | El listado y el reporte no leen la misma fuente | Rastrear la transacción de `RegistrarDespachoAsync` |

**Paso 3 — escribir la prueba que falla.** Una prueba de integración que
inserta un despacho con fecha de hoy y afirma que `ReporteSalidaAsync` lo
devuelve. Debe fallar antes del arreglo. Si pasa, la reproducción es incorrecta
y hay que volver al paso 1 con más datos: registros de la API y la respuesta
cruda de `GET /api/reportes/salida`.

**Entregable de la fase:** una prueba en rojo y una línea de código señalada.
No se escribe el arreglo en esta fase.

---

## 4. Fase 1 — Permisos por rol (#1 y #3)

### 4.1 Admin técnico acotado a soporte

Pantallas que conserva: **Reportes (parte administrativa), Vinculaciones,
Administración, Sesiones**.

**API — retirar `AdminTecnico` de:**

| Controlador | Endpoints |
|---|---|
| `RecepcionController` | Todos **menos** los tres de vinculaciones (ver aviso abajo) |
| `FaenamientoController` | Todos, incluido el `[Authorize]` a nivel de clase (línea 14) |
| `ProductorasController` | Todos |
| `PagosController` | Todos (`[Authorize]` a nivel de clase, línea 16) |
| `ReportesController` | `dashboard`, `entrada`, `transito`, `salida`, `exportar/excel/entrada`, `exportar/excel/transito`, `exportar/excel/salida` |

Conserva en `ReportesController`: `productoras`, `cat`, `novedades`,
`devoluciones` y sus tres exportaciones Excel.

**Aviso: la bandeja de vinculación vive en `RecepcionController`.** Tres
endpoints de ese controlador —`GET vinculaciones`,
`POST vinculaciones/{id}/resolver` y el descarte, en las líneas 308, 320 y 345—
son ya `AdminCooperativa,AdminTecnico` y alimentan la pantalla de Vinculaciones,
que el admin técnico **conserva**. Retirar el rol de «todo el controlador»
rompería una de sus cuatro pantallas. Solo pierde los endpoints de operación
(registro de entregas, sincronización offline, guía de movilización, recepción
en planta).

**El caso especial de la exportación General.** `ExportarExcelGeneralAsync` arma
un libro con una hoja por cada reporte, Salida incluida. Si se deja intacto, la
restricción se escapa por la descarga. `ExcelGeneral` pasa a recibir un
indicador de si el solicitante puede ver el flujo operativo, derivado del rol en
el controlador, y el servicio omite las hojas de Entrada, Tránsito y Salida
cuando es `false`. La decisión de permisos se toma en el controlador; el
servicio solo obedece.

**Frontend:**

- `MainLayout.tsx`: quitar `AdminTecnico` de Productoras, Recepción CAT,
  Faenamiento y Despacho. El Panel (`roles: null`) pasa a declarar
  explícitamente `["AdminCooperativa", "OperadorCAT", "OperadorFaenamiento"]`.
- `App.tsx`: los mismos cambios en `rolesPermitidos` de cada `PrivateRoute`.
- Redirección tras login: `AdminTecnico` va a `/reportes`, no a `/dashboard`,
  que ya no puede abrir. Revisar también la redirección de `PrivateRoute`
  cuando el rol no está permitido, para que no deje al usuario en un bucle.
- `Reportes.tsx`: la lista de pestañas se filtra por rol y la pestaña inicial
  pasa de `entrada` a la primera visible (`productoras` para el admin técnico).
  Sin esto, la pantalla abriría en una pestaña que el rol no puede consultar y
  mostraría un error de carga.

### 4.2 Operador de CAT gestiona las productoras de su centro

Alcance: crear, editar, desactivar y reactivar productoras cuyo `CatAsignado`
sea su propio CAT. Consultar el historial de cambios sigue siendo solo de
administradores.

**API — `ProductorasController`:**

| Endpoint | Cambio |
|---|---|
| `GET /api/productoras` | Quitar `&& !esOperador` de `incluirInactivas && !esOperador` (línea 31). El filtro por `catEfectivo` ya lo mantiene en su centro, y sin las inactivas no podría reactivar ninguna. |
| `POST /api/productoras` | Añadir `OperadorCAT`. Antes de crear: forzar `CatAsignado` al claim `cat` del token, ignorando lo que mande el cliente, y rechazar con 403 si la `ComunidadId` elegida tiene `CatReferencia` distinto de su CAT. |
| `PUT /api/productoras/{id}` | Añadir `OperadorCAT`. Guarda `FueraDeAlcance` sobre el `CatAsignado` **actual** de la productora, y además rechazar si el DTO intenta cambiar `CatAsignado` a otro centro: sin esa segunda comprobación, una edición la sacaría de su alcance de un solo golpe. |
| `PATCH /api/productoras/{id}/estado` | Añadir `OperadorCAT`. Guarda `FueraDeAlcance` sobre el `CatAsignado` actual. Sirve para las dos direcciones, activar y desactivar. |
| `GET /api/productoras/{id}/historial` | Sin cambios: solo administradores. |

**Dónde vive la regla.** Las comprobaciones de alcance se expresan con los
métodos que ya existen en `Common/Auth/AlcanceUsuario.cs` y las que hagan falta
se añaden ahí, no repartidas por el controlador. `AlcanceUsuario` gana un método
para validar que una comunidad pertenece al CAT del usuario, con el resto de la
lógica de alcance. Ese archivo es la definición única de «qué puede tocar este
usuario» y debe seguir siéndolo.

**Frontend:**

- `Productoras.tsx`: `esAdmin` deja de gobernar la interfaz. Se introduce
  `puedeGestionar = esAdmin || esOperadorCat` para el botón «+ Nueva
  productora», el botón de editar y el interruptor de estado. La consulta pide
  `incluirInactivas: puedeGestionar`. `esAdmin` se conserva solo donde la
  distinción siga siendo real: el enlace al historial.
- `FormProductora.tsx`: cuando el rol es `OperadorCAT`, el desplegable de
  comunidades se filtra por `catReferencia === auth.catAsignado` y el selector
  de CAT se muestra fijo y deshabilitado, igual que ya hace `FormLote.tsx` con
  `catFijo`. El servidor lo fuerza de todos modos; el filtro del formulario
  evita que el usuario elija algo que va a ser rechazado.
- `catalogosApi.listarComunidades()` ya es accesible para cualquier usuario
  autenticado (`[Authorize]` sin roles en `CatalogosController`), y el DTO de
  comunidad ya trae `catReferencia`. No hay cambio de API.

---

## 5. Fase 2 — Sesiones por dispositivo (#2)

### Por qué se ven duplicadas

`SesionService.EmitirAsync` inserta una fila nueva de `RefreshToken` en cada
inicio de sesión, sin mirar si ese mismo dispositivo ya tenía una sesión activa.
Cinco inicios de sesión desde la misma tablet en siete días dejan cinco filas
vigentes, todas del mismo usuario. La rotación no es la causa: `RefrescarAsync`
marca `Revocado` el token anterior, y `ListarActivasAsync` filtra por
`!t.Revocado`.

El dato del dispositivo **no falta**. `RefreshToken` guarda `DispositivoId`,
`UserAgent` e `IpCreacion`; `SesionActivaDto` los devuelve los tres. `Sesiones.tsx`
nunca los pinta.

### Cambios

**API:**

- `EmitirAsync` revoca las sesiones activas previas del mismo
  `(UsuarioId, DispositivoId)` antes de insertar la nueva. Cuando
  `dispositivoId` llega nulo no se revoca nada: no hay forma de saber de qué
  dispositivo se trata, y revocar por usuario cerraría sesiones legítimas de
  otras tablets.
- `SesionActivaDto` gana un campo `Dispositivo`: una descripción corta derivada
  del `UserAgent`, del estilo `Chrome · Android` o `Safari · iPad`. Se calcula
  en un helper propio, `Common/Auth/DescripcionDispositivo.cs`, con una función
  pura y sus pruebas unitarias. Sin librerías nuevas: cinco o seis
  coincidencias de texto cubren las tablets del piloto, y lo que no coincida
  devuelve `"Dispositivo desconocido"`.

**Frontend — `Sesiones.tsx`:** cada tarjeta añade una línea con el dispositivo,
los últimos 6 caracteres del `DispositivoId` (identifica la tablet sin volcar un
UUID entero en pantalla) y la IP de creación.

**Las filas duplicadas que ya existen** no se migran: expiran solas en siete
días, y el botón «Cerrar sesión» que ya funciona permite quitarlas antes.

---

## 6. Fase 3 — Guía de movilización y marca en los PDF (#4 y #5)

### 6.1 Los dos defectos de la guía

**Nombre de clase en los paréntesis.** En
`GuiaMovilizacionService.cs`, la celda de productora de la tabla por animal
interpola el objeto de navegación en lugar de su nombre:

```csharp
$"{cuy.Productora.NombreCompleto} ({cuy.Productora.Comunidad})"
```

`Comunidad` es una entidad, así que `ToString()` devuelve
`CoopagcuyApi.Features.Catalogos.Models.Comunidad`. Pasa a `.Comunidad.Nombre`.

La cabecera del mismo documento sí funciona porque `AppDbContext` declara
`e.Navigation(p => p.Comunidad).AutoInclude()` y usa `.Nombre` explícitamente.
Aun así se añade el `ThenInclude(p => p.Comunidad)` explícito en la consulta del
lote: depender de un `AutoInclude` para que un PDF no reviente es frágil, y el
`Include` explícito documenta la intención.

**Características sin rótulo.** La celda muestra
`Blanco · Blanda · Normal`. Los tres valores son correctos y corresponden a ese
animal —`CuyRegistro` guarda `ColorPelaje`, `EstadoOreja` y `TamanoAnimal` por
separado, y `FormLote.tsx` los captura uno a uno—, pero sin rótulo se leen como
una lista de opciones disponibles y no como los datos del cuy. Pasa a:

```
Pelaje: Blanco · Oreja: Blanda · Tamaño: Normal
```

La columna de características es `RelativeColumn(2)` en A5; hay que verificar
que el texto rotulado no desborde y, si aprieta, subir el peso relativo de esa
columna o repartir los rótulos en dos líneas.

### 6.2 El logo en los tres documentos

Hay tres generadores QuestPDF: la guía de movilización, la ficha de lote y la
ficha de lote faenado (ambas en `ReportesService`). Ninguno lleva imagen.

El PNG existe en el frontend (`public/brand/cuy-logo-full.png`, 254 KB) y no en
la API.

**Diseño:** se copia una versión reducida a
`Common/Branding/coopagcuy-logo.png`, declarada como `EmbeddedResource` en el
`.csproj`. Un `BrandingAssets` estático lo lee del ensamblado una sola vez y lo
guarda en memoria; los tres documentos piden los bytes a esa clase. La cabecera
de cada PDF pasa a una fila de tres partes: logo a la izquierda, título en el
centro, código y fecha a la derecha.

**Por qué embebido y no un archivo en disco:** el `Dockerfile` publica sobre
`aspnet` sin `wwwroot`. Un archivo suelto que no se copie funcionaría en
desarrollo y fallaría en producción, y fallaría además en el momento de generar
un documento, no al arrancar.

**Tamaño:** 254 KB en cada PDF es excesivo para un documento A5 que se imprime.
Se reduce el PNG a un ancho de unos 300 px antes de embeberlo.

---

## 7. Fase 4 — Arreglo del reporte de Salida (#6)

Se ejecuta con el resultado de la fase 0: convertir en verde la prueba que allí
quedó en rojo. El contenido depende de lo que muestre la consulta, por lo que no
se especifica aquí.

Si el paso 2 apunta al desfase de zona horaria, el arreglo debe cubrir también
los despachos ya guardados con la fecha desviada: una consulta de corrección de
datos, no solo el cambio de código.

---

## 8. Orden de ejecución

```
Fase 0 (diagnóstico #6)  ──────────────────────────► Fase 4 (arreglo #6)
Fase 1 (permisos #1 #3)  ──┐
Fase 2 (sesiones #2)     ──┼── independientes entre sí
Fase 3 (PDF #4 #5)       ──┘
```

La fase 0 va primero porque es solo lectura y su resultado condiciona el
trabajo restante. Las fases 1 a 3 no se tocan entre sí y pueden ir en cualquier
orden.

Cada fase se implementa con prueba primero. Las fases 1 y 2 tocan seguridad, así
que sus pruebas deben incluir los casos negativos: el admin técnico recibe 403
en los endpoints que perdió, y el operador de CAT recibe 403 al tocar una
productora de otro centro.

---

## 9. Pruebas

| Fase | Prueba | Qué demuestra |
|---|---|---|
| 0 | Integración: despacho de hoy → `ReporteSalidaAsync` lo devuelve | Reproduce el fallo antes de arreglarlo |
| 1 | `AdminTecnico` → 403 en recepción, faenamiento, productoras, pagos, `reportes/salida` | La restricción vive en la API, no solo en el menú |
| 1 | `AdminTecnico` → 200 en los tres endpoints de `recepcion/vinculaciones` | La restricción no se llevó por delante una pantalla que conserva |
| 1 | `AdminTecnico` → 200 en `reportes/productoras`, `cat`, `novedades`, `devoluciones` | No se restringió de más |
| 1 | `ExportarExcelGeneralAsync` sin flujo operativo omite las hojas Entrada/Tránsito/Salida | La restricción no se escapa por la descarga |
| 1 | `OperadorCAT` crea productora: `CatAsignado` se fuerza al del token aunque el DTO diga otro | El cliente no elige su propio alcance |
| 1 | `OperadorCAT` con comunidad de otro CAT → 403 | El alcance cubre la comunidad, no solo el centro |
| 1 | `OperadorCAT` edita, desactiva y reactiva productora de su CAT → 204 | La función pedida existe |
| 1 | `OperadorCAT` sobre productora de otro CAT → 403 en `PUT` y en `PATCH estado` | El alcance se respeta en las dos operaciones |
| 1 | `OperadorCAT` intenta mover `CatAsignado` a otro centro vía `PUT` → 403 | No hay fuga por edición |
| 1 | `GET /api/productoras?incluirInactivas=true` como `OperadorCAT` devuelve las inactivas de su CAT y ninguna de otro | Puede reactivar sin ver de más |
| 2 | Dos inicios de sesión con el mismo `dispositivoId` → una sola sesión activa | Ya no se duplican |
| 2 | Dos inicios con `dispositivoId` distinto → dos sesiones activas | No se cierran tablets legítimas |
| 2 | Inicio con `dispositivoId` nulo no revoca sesiones existentes | No hay cierre en cascada por un cliente sin identificador |
| 2 | Unitaria: `DescripcionDispositivo` sobre varios `UserAgent` reales y sobre uno vacío | El helper es correcto y no lanza |
| 3 | La guía de un lote no contiene la cadena `CoopagcuyApi.Features` | El defecto no vuelve |
| 3 | La guía contiene los rótulos `Pelaje:`, `Oreja:` y `Tamaño:` | Las características se leen como datos |
| 3 | Los tres PDF se generan sin excepción y pesan más que sin logo | El recurso embebido se resuelve en tiempo de ejecución |

---

## 10. Fuera de alcance

- Añadir un campo «comunidad asignada» al usuario. El alcance por centro de
  acopio cubre la necesidad con el modelo actual; un campo nuevo pediría
  migración, cambio en el formulario de usuarios y un claim más en el token.
- Migrar o borrar las filas de sesión duplicadas que ya existen.
- Dar al operador de CAT acceso al historial de cambios de productoras.
- Rediseñar la guía de movilización más allá de los dos defectos y el logo.
- Cualquier otro reporte que no sea Salida.

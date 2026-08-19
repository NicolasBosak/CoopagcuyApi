# Reglas de recepción y declaraciones de movilización — diseño

**Fecha:** 2026-08-19
**Repos afectados:** `CoopagcuyApi` y `CoopagcuyFront/coopagcuy-frontend`
**Estado:** aprobado, pendiente de plan de implementación

## Contexto

Seis cambios solicitados sobre el eslabón de recepción (registro de cuyes en el
CAT) y el de movilización (envío a la planta de faenamiento). Tres son ajustes de
parámetros que hoy están duplicados y dispersos; uno agrega infraestructura nueva
(evidencia fotográfica con caducidad); dos modifican la declaración sanitaria de
la guía de movilización.

El punto 7 de la solicitud original quedó vacío. Se acordó cerrar el alcance en
los seis puntos escritos; si aparece un séptimo, se trata en otro ciclo.

## Decisiones tomadas

| Decisión | Elegido | Descartado y por qué |
|---|---|---|
| Bandas de peso fuera del rango 1200–1500 | Mismo esquema de hoy, corrido: `<1200` rechazo, `1200–1500` aceptado, `>1500` sobrepeso informativo | Rango duro (rechazaría animales sanos por pesados, hoy se aceptan); "fuera = novedad, nunca rechazo" (perdería el control de mínimo) |
| Foto sin señal | Sí, offline como el resto del wizard | Solo con conexión: en el CAT sin cobertura nunca habría fotos, justo donde se ven los defectos |
| Retención de la foto | 90 días | 30 días (el reclamo al proveedor puede tardar más); 180 días (6× almacenamiento sin necesidad demostrada) |
| Aviso de antibióticos | Declaración con casilla obligatoria, registrada | Letrero informativo (la guía PDF perdería la línea y no quedaría constancia); casilla opcional (en campo se queda vacía) |
| Mecanismo de borrado | Política de ciclo de vida de Azure Blob | Barrido perezoso (depende del tráfico; con escala a cero los bytes sobreviven indefinidamente si nadie usa la app); cron de GitHub Actions (otro secreto que rotar + endpoint destructivo expuesto) |
| Dónde viven las reglas | Un módulo de reglas por repo | Cambio quirúrgico (mantiene la dispersión: el `20` está hoy en 4 sitios del front); API sirve las reglas (rompe el registro offline: sin catálogo no hay con qué evaluar) |

## Estado actual del código

Reglas duplicadas y dispersas:

- `Features/Recepcion/Services/RecepcionService.cs:34` — `private const int CapacidadJaula = 20`
- `Features/Recepcion/Services/RecepcionService.cs:575-596` — bandas de peso (`EvaluarCuyIndividual`)
- `Features/Recepcion/Services/RecepcionService.cs:598` — regla de color negro (`ColorNoConforme`)
- `src/components/recepcion/FormLote.tsx:24-30` — lista `COLORES`
- `src/components/recepcion/FormLote.tsx:55` — `MAX_ENTREGA = 40` ("dos jaulas completas")
- `src/components/recepcion/FormLote.tsx:78-110` — `evaluarCuy()`, espejo de las bandas del backend
- `src/components/recepcion/FormLote.tsx:499` — texto "máximo 20 por jaula"
- `src/components/recepcion/FormLote.tsx:813-814` — texto "supera los 1300 g"
- `src/components/recepcion/JaulaEnArmado.tsx:41,77,112` — el `20` hardcodeado tres veces
- `src/types/recepcion.ts:3` — `type ColorPelaje`
- `src/components/reportes/graficos/AnilloNovedades.tsx:17` — etiqueta `"Sobre peso (>1300g)"` del gráfico de reportes

Movilización:

- `Features/Recepcion/Models/Movilizacion.cs:25-26` — `TipoForraje`, `DiasRetiroMedicamentos`
- `Features/Recepcion/Services/GuiaMovilizacionService.cs:236-241` — se imprimen en la guía PDF
- `src/components/recepcion/FormMovilizacion.tsx:15-23` — `TIPOS_FORRAJE`
- `src/components/recepcion/FormMovilizacion.tsx:205-220` — campo de días de retiro
- `src/pages/Faenamiento.tsx:294-296` — se muestran en la ficha del lote

Hechos verificados durante el análisis:

- `Features/Recepcion/Validators/` **existe pero está vacío**: no hay validador de
  movilización que reutilizar.
- `Novedad` no tiene FK a `CuyRegistro`; el animal se identifica por el texto
  `"Cuy #N: …"` de la descripción. La evidencia se ancla a la novedad, no al cuy.
- `IBlobStorageService` solo expone `SubirQRAsync`, y su contenedor se crea con
  `PublicAccessType.Blob`.
- El API corre en Azure Container Apps **con escala a cero**: un `IHostedService`
  con temporizador no se ejecuta de forma fiable.

## Diseño

### 1. Módulo de reglas por repo

Base de los puntos 1–3. No elimina la duplicación *entre* repos —imposible sin
romper la evaluación offline— pero sí la dispersión *dentro* de cada uno.

**API** — nuevo `Common/ReglasRecepcion.cs`:

```csharp
public static class ReglasRecepcion
{
    public const int CapacidadJaula = 15;
    public const decimal PesoMinimoGramos = 1200m;
    public const decimal PesoMaximoGramos = 1500m;
}
```

`RecepcionService` consume la clase y elimina su constante privada.

**Front** — nuevo `src/domain/reglasRecepcion.ts`: espejo de los tres valores,
la lista `COLORES`, y la función `evaluarCuy()` movida desde `FormLote.tsx`.
`FormLote` la importa. Efecto lateral buscado: alivia el archivo de 822 líneas
registrado como deuda consciente, sin abrir un refactor aparte.

Una prueba unitaria fija los tres valores para que un cambio accidental rompa CI.

### 2. Capacidad de jaula: 20 → 15

Constante en el API; en el front, los cuatro sitios pasan a leer
`CAPACIDAD_JAULA`. `MAX_ENTREGA` pasa de 40 a `CAPACIDAD_JAULA * 2` (30),
conservando su intención original de "dos jaulas completas".

**Transición de jaulas abiertas con más de 15 animales.** Verificado sobre el
código: el acumulador se auto-cura. Con un lote abierto de 18,
`espacio = 15 - 18 = -3`, el bucle `for` no itera, `RecalcularEstadoLote` corre,
`18 >= 15` cierra el lote, y como `ObtenerOCrearJaulaAbiertaAsync` filtra por
`!l.Cerrado` la siguiente vuelta crea una jaula nueva. No hay bucle infinito ni
pérdida de animales, y **no hace falta migración de datos**.

Queda un defecto cosmético: ese lote entra en `lotesAfectados` sin haber recibido
ningún animal. Se corrige con `var aTomar = Math.Min(Math.Max(0, espacio), pendientes.Count)`
y moviendo el `lotesAfectados.Add` a después de comprobar que `aTomar > 0`.

### 3. Rango de peso: 1200–1500 g

Bandas nuevas, idénticas en ambos repos:

| Peso (g) | Resultado | Novedad generada |
|---|---|---|
| `< 1200` | Rechazado | `BajoPeso` — "Peso {n}g por debajo del mínimo (1200g). Animal rechazado." |
| `1200 – 1500` | Aceptado | — |
| `> 1500` | Aceptado | `SobrePeso` — "Peso {n}g sobre el rango operativo (máx. 1500g)." |

Desaparece la banda ámbar de 850–874 g ("peso justo"). El nivel `novedad` de
`evaluarCuy()` sigue existiendo: lo producen la oreja dura y los signos clínicos.

Los valores `BajoPeso` y `SobrePeso` de `TipoNovedad` **se conservan**: hay filas
históricas que los usan y el gráfico `AnilloNovedades` los mapea.

Esa etiqueta del gráfico (`AnilloNovedades.tsx:17`) dice hoy `"Sobre peso (>1300g)"`
y pasa a `"Sobre peso (>1500g)"`. Es texto visible en Reportes: si se olvida, el
gráfico contradice a la pantalla de recepción sin que nada falle.

**Fuera de alcance explícito:** `Features/QR/Services/QRService.cs:221`
(`promedio >= 880`) y `src/components/faenamiento/FormFaenamiento.tsx:182,594`
(`peso >= 880`) operan sobre **peso de canal** post-faenamiento, otra escala.
No se tocan.

### 4. Colores de pelaje

Lista nueva: `Blanco`, `Amarillo`, `Rojo`, `Combinado`.

- `Bayo` → `Amarillo` (renombre).
- `Rojo` → nuevo.
- `Plomo` y `Negro` → eliminados de la captura.
- `Combinado` y `Blanco` → sin cambios.

Al desaparecer `Negro` se elimina la regla que generaba `ColorNoConforme`, tanto
en `RecepcionService.cs:598` como en `FormLote.tsx:96`. El valor del enum
permanece para las filas históricas.

**Sin lista blanca en el servidor.** Hoy `ColorPelaje` es texto libre y no se
valida; se mantiene así a propósito. Una tablet con el bundle antiguo en caché
todavía puede tener entregas capturadas con `Plomo`; rechazarlas en el sync
perdería trabajo de campo real. Las lecturas muestran el valor tal como se
guardó, así que los registros heredados siguen siendo legibles.

En el front, `type ColorPelaje` sí se restringe a los cuatro valores nuevos: es
lo único que se puede capturar de aquí en adelante.

### 5. Evidencia fotográfica de novedad clínica

**Modelo.** Dos columnas anulables en `Novedad`:

```csharp
public string? FotoUrl { get; set; }        // HasMaxLength(500)
public DateTime? FotoExpiraEn { get; set; }
```

Migración `EvidenciaFotograficaNovedad`: aditiva, sin alteraciones.

**Captura.** En el paso 3 de `FormLote`, el botón de cámara aparece **solo cuando
el campo de signos clínicos tiene texto** — es una evidencia clínica, no una foto
suelta. Se usa `<input type="file" accept="image/*" capture="environment">`, que
en la tablet abre la cámara trasera directamente.

Compresión en el cliente antes de guardar: canvas, lado mayor 1024 px, JPEG
calidad 0.6. Resultado esperado ~100 KB por foto.

**Transporte y offline.** La foto viaja como base64 en un campo nuevo
`FotoBase64` de `CuyRegistroDto`, dentro del `cuyes[]` que ya existe.

Esto no cambia nada del protocolo de sync: la petición sigue siendo el mismo
JSON, la idempotencia por `idCliente` cubre la foto gratis, y no pueden quedar
fotos huérfanas porque la evidencia y la entrega se guardan en la misma
transacción. En IndexedDB la foto se persiste como parte del objeto
`EntregaOffline`, sin cambio de versión del esquema.

Guarda de tamaño: el servidor rechaza con 400 cualquier foto que supere 2 MB una
vez decodificada.

**Almacenamiento.** `IBlobStorageService` gana
`SubirEvidenciaAsync(string nombre, byte[] jpeg)`, que escribe en un contenedor
**distinto y privado**: `AzureBlob:ContainerEvidencias`, por defecto
`evidencias-clinicas`.

Dos razones para no reutilizar el contenedor de QR:

1. El de QR es público a propósito (tiene que escanearse desde fuera). Una foto
   de defectos atribuida a un proveedor no debe ser legible por cualquiera.
2. La política de caducidad se aplica por contenedor: compartirlo borraría
   también los QR de los lotes a los 90 días.

**Lectura.** `GET /api/recepcion/novedades/{id}/foto`, con `[Authorize]`, hace de
proxy: devuelve el JPEG, o 404 si `FotoExpiraEn` ya pasó. Así la UI nunca enlaza
a un blob borrado, aunque la política de Azure y la fecha en BD se desincronicen.

`NovedadResponseDto` gana `bool TieneFoto` (calculado: `FotoUrl != null && FotoExpiraEn > now`)
para que la ficha del lote sepa si mostrar la miniatura sin pedir el binario.

**Borrado automático.** Política de ciclo de vida sobre `evidencias-clinicas`:
borrar blobs con más de 90 días desde su creación. Se declara en
`infra/bootstrap.azcli` con `az storage account management-policy create`.

Corre dentro de Azure Storage, independiente del ciclo de vida de la aplicación
— que es justo lo que hace falta con escala a cero. Azurite no implementa
políticas de ciclo de vida: en desarrollo local las fotos no se borran solas, y
es aceptable.

### 6. Tipo de forraje: concentrado sin proteína animal

Se agrega `"Concentrado sin proteína animal"` a `TIPOS_FORRAJE` en
`FormMovilizacion.tsx`. El servidor guarda texto libre de hasta 200 caracteres,
así que no hay cambio de backend, DTO ni migración.

### 7. Declaración de antibióticos

Reemplaza la pregunta por días de retiro.

**Modelo.** Columna nueva en `Movilizacion`:

```csharp
public bool? SinAntibioticos7Dias { get; set; }
```

Anulable a propósito: `null` distingue "movilización anterior al cambio, nunca se
preguntó" de una declaración explícita. Migración `DeclaracionAntibioticos`,
aditiva.

`DiasRetiroMedicamentos` **sale de `RegistrarMovilizacionDto`** (ya no se captura)
pero **permanece en la columna y en `MovilizacionResponseDto`**, para que las
movilizaciones históricas sigan mostrándose en `Faenamiento.tsx` y en sus guías.

**Formulario.** El bloque "Declaración de tratamientos" pierde el campo numérico
y gana un aviso destacado más una casilla obligatoria:

> ⚠️ Los cuyes registrados **no debieron recibir antibióticos en los últimos 7 días**.
>
> ☐ Confirmo que los cuyes de este lote no recibieron antibióticos en los últimos 7 días.

El botón de registrar queda deshabilitado mientras la casilla no esté marcada.

**Validación de servidor.** Nuevo `Features/Recepcion/Validators/RegistrarMovilizacionValidator.cs`
que exige `SinAntibioticos7Dias == true`. La carpeta está vacía hoy, así que el
validador se cablea siguiendo el patrón ya usado en `FaenamientoController` y
`ProductorasController`: inyección manual de `IValidator<T>` y respuesta
`{ mensaje }` con 400.

**Guía PDF.** `GuiaMovilizacionService.cs:238-241` pasa a:

- `SinAntibioticos7Dias == true` → `"Sin antibióticos últimos 7 días: declarado por {ResponsableDespacho}"`
- `null` → se conserva la línea antigua `"Retiro de medicamentos: {N} días"` o `"sin declaración"`

Así ninguna guía histórica pierde información al reimprimirse.

## Pruebas

Integración (`docker compose -f docker-compose.tests.yml run --rm tests`):

- Entrega de 16 cuyes → dos jaulas, la primera con 15 cerrada y la segunda con 1 abierta.
- Jaula heredada con 18 animales + entrega nueva → la vieja se cierra sin recibir nada, los animales entran en una jaula nueva, y `lotesAfectados` no incluye la vieja.
- Cuy de 1199 g → rechazado; 1200 g → aceptado sin novedad; 1501 g → aceptado con `SobrePeso`.
- Movilización sin `sinAntibioticos7Dias` → 400 con `{ mensaje }`.
- Movilización con la declaración → 201 y guía PDF con la línea nueva.
- Movilización heredada (`null`) → guía PDF con la línea antigua.
- Novedad clínica con foto → blob subido y `FotoExpiraEn` a 90 días.
- `GET /novedades/{id}/foto` con `FotoExpiraEn` en el pasado → 404.
- Foto de más de 2 MB → 400.

Unitarias: valores de `ReglasRecepcion` fijados; textos de la guía (se amplía el
`TextosGuiaTests.cs` existente).

Front: `tsc -b` y `vite build`. No hay Vitest todavía — sigue siendo Fase 2 del
plan de pruebas, fuera de este alcance.

## Despliegue

Orden: **migraciones → API → front**. Las dos migraciones son aditivas y
compatibles con la imagen anterior del API, así que no hay ventana de
incompatibilidad.

Paso manual único: aplicar la política de ciclo de vida sobre el contenedor
`evidencias-clinicas` y crear el contenedor si no existe.

Recordatorio del entorno: el último paso del despliegue sigue siendo manual
(`az containerapp update`) mientras la cuenta Azure for Students no tenga el rol
Application Developer para crear la identidad OIDC.

**Tablets con caché antigua.** Un dispositivo que aún no actualizó el service
worker seguirá *mostrando* las bandas viejas y ofreciendo Plomo y Negro. Los
datos que quedan guardados son correctos igualmente: `EvaluarCuyIndividual`
reevalúa cada animal en el servidor al sincronizar, con las reglas nuevas. La
discrepancia es solo de pantalla y se resuelve sola al actualizarse la PWA.

## Fuera de alcance

- El punto 7 de la solicitud original, que quedó vacío.
- Los umbrales de peso de canal (880 g) en QR y faenamiento.
- Vitest y las fases 2–5 del plan de pruebas.
- Partir `FormLote.tsx` más allá de extraer `evaluarCuy()` y las constantes.

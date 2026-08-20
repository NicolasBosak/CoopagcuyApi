# Evidencia clínica visible en faenamiento — diseño

**Fecha:** 2026-08-20
**Repos afectados:** `CoopagcuyApi` y `CoopagcuyFront/coopagcuy-frontend`
**Estado:** aprobado, pendiente de plan de implementación
**Antecede:** `2026-08-19-reglas-recepcion-y-movilizacion-design.md`, que introdujo la evidencia fotográfica

## Contexto

La evidencia fotográfica de una novedad clínica se captura en el centro de acopio
y hoy solo la puede ver quien trabaja allí: el endpoint que la sirve está
restringido a `OperadorCAT` y `AdminCooperativa`, y el visor solo se renderiza en
la pantalla de Recepción.

El operador de faenamiento es quien tiene el animal delante en el momento de
procesarlo. Si el CAT documentó un defecto con foto, esa foto le sirve a él tanto
o más que a quien la tomó. Hoy no puede verla.

## Estado actual del código

- `RecepcionController.FotoDeNovedad` — `[Authorize(Roles = "OperadorCAT,AdminCooperativa")]`.
  `OperadorFaenamiento` se retiró deliberadamente en la revisión final del ciclo
  anterior, tras comprobar que no tenía ninguna pantalla desde la que pedir la foto.
  Este diseño le da esa pantalla, así que el rol vuelve.
- `RecepcionService.ObtenerFotoNovedadAsync(id, catEfectivo)` — filtra por centro
  de acopio cuando `catEfectivo` no es nulo.
- `RecepcionController.CatDelOperador()` — devuelve `null` para todo rol que no
  sea `OperadorCAT`.
- `Novedad` — tiene `LoteId`, `FotoUrl` y `FotoExpiraEn`, pero **ninguna relación
  con `CuyRegistro`**: el animal se identifica por el texto `"Cuy #N: …"` que
  `NovedadDeCuy` compone en la descripción.
- `FaenamientoService.LotesDisponiblesAsync` — ya materializa `l.Cuyes` con su
  productora y proyecta `CuyDisponibleDto`, que incluye `MotivoNovedad` (un texto
  de `CuyRegistro`, no de la tabla `Novedad`).
- `FormFaenamiento.tsx` — ya muestra la novedad de recepción por animal
  (`Novedad en recepción: {a.novedadRecepcion}`) y avisa en la tarjeta del lote
  ("⚠ Trae cuyes con novedad del CAT").
- `EvidenciaNovedad.tsx` — vive en `components/recepcion/`, se usa solo desde
  `Recepcion.tsx`. Descarga por el cliente autenticado (el token está en memoria,
  un `<img src>` daría 401), revoca sus object URL y distingue 404 (caducada) de
  cualquier otro fallo.

## Decisiones tomadas

| Decisión | Elegido | Descartado y por qué |
|---|---|---|
| Momento y granularidad | Al elegir el lote, **por animal** | Por lote (el operador ve fotos sin saber de qué cuy es cada una); en las alertas tras registrar (llega tarde: el animal ya se faenó); solo abrir el endpoint (sin pantalla, en la práctica no la vería) |
| Cómo se relaciona la foto con el animal | Clave foránea `Novedad.CuyRegistroId` | Emparejar por el texto `"Cuy #N:"` de la descripción: frágil justo donde más duele — adjudicar un defecto al animal equivocado es peor que no mostrar nada |

## Diseño

### 1. `Novedad` gana su clave foránea al cuy

```csharp
public int? CuyRegistroId { get; set; }
public CuyRegistro? CuyRegistro { get; set; }
```

Se enlaza por **navegación** (`novedad.CuyRegistro = cuy`), no asignando el id:
cuy y novedad se insertan en el mismo `SaveChanges` y el cuy todavía no tiene Id.
EF resuelve la clave al guardar.

**Dónde va el enlace, exactamente.** Dentro de `EvaluarCuyIndividual`, las
novedades se construyen ANTES que el cuy (`var cuy = new CuyRegistro` está al
final del método). Así que el enlace no puede hacerse donde se crea cada novedad
—ahí `cuy` todavía no existe— sino **después de construir el cuy y antes del
`return`**, recorriendo las novedades ya creadas:

```csharp
foreach (var n in novedades.Where(n => n.Tipo != TipoNovedad.SinAyuno))
    n.CuyRegistro = cuy;
```

El filtro de `SinAyuno` es **defensivo**: hoy este método no genera esa novedad
—se añade en el bucle de la entrega, una vez por productora, no por animal— así
que en la práctica no descarta nada. Se deja escrito porque documenta que esa
novedad no pertenece a ningún cuy, y evita enlazarla por error si algún día se
mueve de sitio.

**Comportamiento de borrado:** configurar la relación explícitamente en
`AppDbContext` con `DeleteBehavior.Cascade`, igual que la FK a `Lote`. En este
sistema no se borran cuyes, así que la regla no se ejercita; se fija para que no
quede a merced del valor por defecto de EF, que para una FK opcional sería
`ClientSetNull` y dejaría filas huérfanas si algún día se borrara un cuy.

Migración `NovedadPorCuy`: aditiva, una columna anulable más su índice.

**Anulable a propósito, por dos motivos distintos:**

1. Las novedades de lote no pertenecen a ningún animal — `SinAyuno` se registra
   por entrega, no por cuy.
2. Las filas históricas se quedan sin enlace.

El segundo motivo no cuesta nada aquí: **ninguna novedad existente tiene foto**,
porque la evidencia fotográfica es una función nueva del ciclo anterior. No hay
datos que rellenar, y toda novedad que llegue a tener foto tendrá también su FK.

### 2. `lotes-disponibles` expone la evidencia por animal

`FaenamientoService.LotesDisponiblesAsync` añade `.Include(l => l.Novedades)` y,
en la proyección de cada cuy, resuelve:

```csharp
NovedadFotoId: l.Novedades.FirstOrDefault(n =>
    n.CuyRegistroId == c.Id &&
    n.Tipo == TipoNovedad.SignosClinicos &&
    n.FotoUrl != null &&
    n.FotoExpiraEn > DateTime.UtcNow)?.Id
```

El emparejamiento es en memoria sobre las novedades ya materializadas del lote
—una lista de pocas filas— así que no añade consultas.

`CuyDisponibleDto` gana `int? NovedadFotoId`. Devuelve `null` cuando el animal no
tiene novedad clínica, cuando la tiene pero sin foto, y **cuando la foto ya
caducó**: así el front nunca ofrece un botón que al pulsarlo daría 404.

### 3. El endpoint admite al operador de faenamiento

`FotoDeNovedad` pasa a `[Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]`.

**El filtro por centro no se toca.** El endpoint ya filtra con `CatDelOperador()`,
que devuelve `null` para todo rol que no sea `OperadorCAT`. Es exactamente el
comportamiento que hace falta: la planta recibe jaulas de los cinco centros y debe
poder ver la evidencia de todas, mientras que un operador de CAT sigue sin poder
descargar la de otro centro.

### 4. El visor aparece en el asistente de faenamiento

En `FormFaenamiento`, dentro de la lista de cuyes del lote seleccionado, el animal
que traiga `novedadFotoId` muestra `<EvidenciaNovedad novedadId={...} />` junto a
su motivo de novedad. Se reutiliza sin cambios: ya resuelve la descarga
autenticada, la liberación de los object URL y la distinción entre evidencia
caducada y fallo de conexión.

`EvidenciaNovedad.tsx` se mueve de `components/recepcion/` a `components/ui/`,
junto a `ModalShell` y `SelloDeTiempo`: pasa a consumirlo una segunda feature y
deja de pertenecer a una sola.

## Pruebas

Integración (`docker compose -f docker-compose.tests.yml run --rm tests`):

- La novedad clínica queda enlazada al cuy correcto cuando la entrega se **parte
  entre dos jaulas** — mismo escenario que ya destapó el emparejamiento
  índice→foto en el ciclo anterior.
- Una novedad de lote (`SinAyuno`) se guarda con `CuyRegistroId` nulo.
- `lotes-disponibles` devuelve `novedadFotoId` en el cuy con foto y `null` en los
  demás cuyes del mismo lote.
- Una foto caducada (`FotoExpiraEn` en el pasado) devuelve `novedadFotoId` nulo,
  no un id que daría 404 al pulsarlo.
- Un `OperadorFaenamiento` descarga la foto de un lote de un CAT cualquiera —
  hoy esa petición devuelve 403.
- Un `OperadorCAT` sigue recibiendo 404 al pedir la foto de otro centro (no debe
  romperse al ampliar los roles).

Front: `pnpm exec tsc -b` y `pnpm build`. No hay pruebas automatizadas en el
front; Vitest sigue siendo fase posterior del plan de pruebas del proyecto.

## Despliegue

Migración aditiva, compatible con la imagen anterior del API: **migración → API →
front**, el mismo orden del ciclo anterior. El último paso sigue siendo manual
(`az containerapp update`) mientras la cuenta Azure for Students no tenga el rol
Application Developer.

## Fuera de alcance

- Rellenar `CuyRegistroId` en las novedades históricas. No aporta nada: ninguna
  tiene foto, y su texto `"Cuy #N: …"` sigue siendo legible donde ya se mostraba.
- Mostrar la evidencia en la confirmación de llegada a planta o en el bloque de
  alertas posterior al registro. Se evaluaron y se descartaron frente al momento
  de elegir el lote, que es cuando el dato es accionable.
- Dar a `OperadorFaenamiento` acceso a la pantalla de Recepción.

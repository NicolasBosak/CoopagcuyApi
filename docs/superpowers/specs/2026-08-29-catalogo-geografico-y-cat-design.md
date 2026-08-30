# Catálogo geográfico y CAT gestionable — diseño

**Fecha:** 2026-08-29
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

La cooperativa opera hoy en una sola provincia, Azuay, con cinco centros de
acopio fijos. El sistema da eso por sentado en dos lugares distintos y por dos
razones distintas, y ninguna de las dos sobrevive a que se sume una provincia
nueva a la organización.

## El problema, en dos frases

**El cantón es texto libre y la provincia no existe.** `Comunidad` guarda
`Canton` como `string`, sin catálogo detrás, y la palabra «Azuay» está escrita a
mano en la página pública del QR, en la ficha de faenamiento y en la guía de
movilización. Un cuy de otra provincia le mentiría al consumidor.

**El CAT es un `enum` de cinco valores compilado en el binario.** Crear un
centro de acopio nuevo exige hoy recompilar y desplegar.

## Los tres hallazgos que gobiernan el diseño

### 1 · El código del CAT ya es la clave real en base

Las cinco columnas que guardan un CAT están mapeadas con
`HasConversion<string>()`:

| Tabla | Columna |
|---|---|
| `Usuarios` | `CatAsignado` |
| `Productoras` | `CatAsignado` |
| `Lotes` | `CentroAcopio` |
| `EntregasPendientesVinculacion` | `CentroAcopio` |
| `Comunidades` | `CatReferencia` |

En base ya dice `'PAT'`, `'NIE'`, `'HUE'`, `'NAB'`, `'PEL'`. El `enum` es una
comodidad de C#, no la forma del dato. **Por eso la tabla de CATs se clava sobre
el código de tres letras y no sobre un `Id` entero**: convierte una migración de
datos con backfill de cinco columnas en un simple `ADD CONSTRAINT`.

### 2 · La capa de autorización ya trabaja con strings

`AlcanceUsuario.CatRestringido()` devuelve `string?` —lo saca del claim JWT
`"cat"`— y `FueraDeAlcance` compara strings. El único sitio que vuelve a
convertir a `enum` es `PagosController.FiltroCat()`. El refactor de `enum` a
`string` **elimina código**, no lo agrega: se van un `Enum.TryParse` y unos
treinta `.ToString()`.

### 3 · El origen del cuy no es el CAT

Una comunidad entrega en el CAT que le queda más cerca, **aunque esté en otra
provincia**. Por eso el CAT tiene un cantón —dónde está físicamente— pero eso
no restringe quién entrega ahí.

> **Regla:** la procedencia del cuy se deriva siempre de la comunidad de la
> productora, nunca del CAT. Un cuy criado en la provincia A y acopiado en un
> CAT de la provincia B es, en el QR público y en la guía, de la provincia A. El
> CAT es el eslabón logístico, no el origen.

Los reportes ya son compatibles con esto: `ReportesService` agrupa por CAT
(`:213`, `:751`) y por comunidad (`:184`, `:711`) en columnas separadas, sin
suponer nunca que compartan geografía.

## El modelo

### Entidades nuevas — `Features/Catalogos/Models/`

| Entidad | Campos | Reglas |
|---|---|---|
| `Provincia` | `Id`, `Nombre`, `Activa` | `Nombre` único |
| `Canton` | `Id`, `Nombre`, `ProvinciaId`, `Activo` | único por (`ProvinciaId`, `Nombre`) |
| `CentroAcopio` | `Codigo` (PK, `string(3)`), `Nombre`, `CantonId`, `Activo` | `^[A-Z]{3}$`, único, **inmutable** |

El `enum CentroAcopio` de `Common/Enums.cs` **se elimina**; la entidad toma ese
nombre.

El código del CAT es inmutable porque prefija el identificador de cada jaula
(`PAT-20260615-001`). Cambiarlo dejaría jaulas históricas con un prefijo que ya
no corresponde a ningún centro, y códigos ya impresos que no se pueden leer. El
nombre y el cantón sí se editan.

### `Comunidad` cambia

- pierde `Canton` (`string` libre), gana `CantonId` (FK)
- `CatReferencia` pasa de `enum` a `string` con FK a `CentrosAcopio.Codigo`
- gana `Latitud`, `Longitud`, `AltitudMinM`, `AltitudMaxM`, todos nullable

Las coordenadas suben desde `src/domain/comunidades/coordenadas.ts`, cuya
cabecera ya anticipaba este momento: *«si algún día la cooperativa empieza a dar
de alta comunidades nuevas desde Administración, este archivo deja de alcanzar»*.
Son nullable porque una comunidad nueva nace sin ellas y el mapa público tiene
que tolerarlo.

### El nombre de comunidad deja de ser único a nivel global

Hoy `AppDbContext` declara `e.HasIndex(c => c.Nombre).IsUnique()` y
`CatalogosService.CrearComunidadAsync` rechaza cualquier nombre repetido en todo
el sistema. Con una sola provincia eso nunca molestó; con varias, bloquea altas
legítimas —«San José» existe en más de una provincia del Ecuador—.

El índice pasa a ser único por (`CantonId`, `Nombre`) y la comprobación del
servicio se acota al cantón. Los cinco nombres sembrados siguen sin colisionar.

### Integridad

Nada se borra nunca, solo se desactiva —igual que `Comunidad.Activa` hoy—.
Desactivado significa «no aparece al crear cosas nuevas»; los lotes, tickets y
páginas QR históricos siguen resolviendo su origen. En un sistema de
trazabilidad el pasado no se reescribe.

Devuelven **409** con mensaje:

- desactivar un CAT con jaulas abiertas (`Lote.Cerrado == false`) o productoras
  activas
- desactivar un cantón con comunidades o CATs activos
- desactivar una provincia con cantones activos
- crear un CAT con código repetido o que no case con `^[A-Z]{3}$`
- editar el código de un CAT ya creado

**No** hay validación de coherencia entre la comunidad y su CAT: puede referenciar
cualquier CAT activo, de cualquier cantón y cualquier provincia. Ver hallazgo 3.

## Las migraciones

**Dos migraciones**, una por cada fase que toca el modelo. No cabe una sola: la
fase 1 no debe depender de que el `enum` ya haya desaparecido.

### Migración A — geografía (fase 1)

1. Crea `Provincias` y `Cantones`.
2. Siembra las 24 provincias y los 221 cantones del Ecuador desde
   `Infrastructure/Data/Seed/GeografiaEcuador.cs` —archivo aparte, consumido por
   `HasData`, para no inflar `AppDbContext`—.
3. Añade `Comunidad.CantonId` y hace el backfill cruzando el texto actual contra
   los cantones sembrados, **ignorando tildes y mayúsculas**. Recién entonces
   `DROP` de la columna `Canton`.
4. Reemplaza el índice único de `Comunidad.Nombre` por uno sobre
   (`CantonId`, `Nombre`).
5. Añade las cuatro coordenadas nullable a `Comunidad`.

### Migración B — CAT (fase 2)

6. Crea `CentrosAcopio`.
7. Siembra los cinco CATs actuales con su código intacto y su cantón real,
   derivado del `HasData` de `Comunidad`: PAT→Pucará, PEL→Pucará, NIE→Nabón,
   NAB→Nabón, HUE→Santa Isabel.
8. Crea las FK hacia `CentrosAcopio.Codigo` desde las cinco columnas del
   hallazgo 1. **Sin backfill**: los valores ya están ahí.

### El cantón «Nabon»

Existe una comunidad dada de alta desde Administración cuyo cantón se escribió
sin tilde. El cruce insensible a tildes del paso 3 la resuelve sola: cae en
«Nabón» y el texto mal escrito desaparece con la columna. **La comunidad, sus
productoras, lotes, pagos y páginas QR quedan intactos** — se va el error, no el
dato.

Si algún otro cantón escrito a mano **no** cruza contra el catálogo, la migración
**se detiene** con un error que lo nombra. No inventa un cantón ni pierde la
fila.

## La API — `CatalogosController`

Lectura para cualquier autenticado; escritura para `AdminCooperativa` y
`AdminTecnico`, igual que comunidades hoy.

| Ruta | Verbos |
|---|---|
| `/api/catalogos/provincias` | GET, POST, PUT, PATCH estado |
| `/api/catalogos/cantones?provinciaId=` | GET, POST, PUT, PATCH estado |
| `/api/catalogos/centros-acopio` | GET (ya existe), POST, PUT, PATCH estado |

`CentroAcopioDto(Codigo, Nombre)` se amplía a
`(Codigo, Nombre, CantonId, Canton, Provincia, Activo)`. El cambio es **aditivo**,
así que el front actual no se rompe mientras se migra.

`ComunidadResponseDto` sustituye `canton: string` por `cantonId` más `canton` y
`provincia` de solo lectura, y suma las cuatro coordenadas.

## El refactor `enum` → `string`

Va **solo en su commit**, sin mezclarse con cambios de modelo.

- las cinco propiedades de la tabla del hallazgo 1 pasan a `string`
- en `AppDbContext`, los cinco `HasConversion<string>()` pasan a
  `HasMaxLength(3)` + FK
- las firmas `CentroAcopio? filtroCat` de `PagoService`, `RecepcionService`,
  `ReportesService` y `ProductoraService` pasan a `string?`
- todo código entrante se normaliza a mayúsculas **en el borde** (el DTO), no en
  cada consulta: Postgres compara strings con distinción de mayúsculas
- 25 archivos de test más `Sembrador.cs`: `CentroAcopio.PAT` → `"PAT"`

Son unos 300 sitios, pero mecánicos. La red de seguridad son las pruebas de
integración contra Postgres real que ya existen.

## El front

- se borran `CENTROS_ACOPIO` y el union `type CentroAcopio` de
  `src/types/productora.ts`; los ocho componentes que hoy los importan pasan a un
  hook `useCentrosAcopio()` con react-query
- la pestaña «Comunidades» de `Administracion.tsx` se convierte en «Catálogos»
  con cuatro sub-pestañas: Provincias, Cantones, CATs, Comunidades
- `FormComunidad`: el cantón deja de ser input libre y pasa a selector Provincia
  → selector Cantón dependiente
- el selector de CAT, en `FormComunidad` y `FormProductora`, lista **todos** los
  CATs activos etiquetados `Nombre (Cantón, Provincia)`. Sin ese sufijo, en
  cuanto haya dos provincias el operador no sabe cuál está eligiendo
- `coordenadas.ts` se elimina y `MapaOrigen.tsx` lee lat/lon/altitud de la API

## El QR y los PDFs

- «Azuay» a mano en `QRService.cs:180,191,214,261` y
  `FaenamientoService.cs:814,854` → se deriva de `Comunidad → Canton →
  Provincia`. `PaginaPublicaDto` gana el campo `Provincia`
- `GuiaMovilizacionService.cs:129` imprime hoy `(Comunidad, Cantón)`; pasa a
  `(Comunidad, Cantón, Provincia)`
- la dirección de la planta (`GuiaMovilizacionService.cs:25`) se queda literal:
  la planta es una sola y no es un dato de catálogo

## Pruebas

Integración, contra Postgres real:

- alta encadenada provincia → cantón → CAT → comunidad → productora → jaula, y
  la jaula sale con el prefijo del CAT nuevo
- código de CAT inválido, duplicado, y el rechazo al intentar editarlo
- 409 al desactivar un CAT con jaula abierta, y un cantón con comunidades activas
- una comunidad referenciando un CAT de otra provincia **pasa** (no es un error)
- el QR de esa comunidad muestra **su** provincia, no la del CAT
- un `OperadorCAT` asignado a un CAT creado en caliente ve solo lo suyo
- la migración: una comunidad con cantón sin tilde queda apuntando al cantón
  correcto y conserva sus productoras

## Orden de trabajo

Cuatro fases en una rama, cada una con las pruebas verdes:

1. **Geografía** — `Provincia`, `Canton`, migración, FK en `Comunidad`. No toca
   el `enum`.
2. **CAT a tabla** — entidad, endpoints, y el refactor `enum` → `string` en API
   y tests.
3. **Front** — catálogos, sub-pestañas y selectores dependientes.
4. **Provincia real** — QR, fichas, guía, y coordenadas al catálogo.

Se descartó hacerlo todo de una: un diff de unos 350 archivos entre API, tests y
front no es revisable y no deja punto de reversión. Y se descartó partirlo en dos
entregas independientes, porque obligaría a tocar `FormComunidad` y
`Administracion` dos veces y a una migración intermedia que no le sirve a nadie.

## Fuera de alcance

- la dirección de la planta de faenamiento sigue siendo literal
- los centros de acopio no ganan coordenadas propias en esta entrega; el mapa
  público dibuja comunidades, no CATs
- no se toca el formato del identificador de jaula ni el de lote faenado

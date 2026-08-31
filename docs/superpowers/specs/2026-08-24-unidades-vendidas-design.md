# Unidades vendidas — diseño

**Fecha:** 2026-08-24
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

La pestaña **Ganancias** publica dinero: lo que cobraron las productoras y el
margen de la reventa. No dice **cuántos cuyes se vendieron**, y ese número no
existe agregado en ningún sitio del sistema.

La pestaña **Salida** lista unidades, pero **una fila por despacho**, sin
totalizar. Y `N.º de pagos`, en las tablas de ganancias, es un conteo de
**pagos**, no de animales.

Este proyecto añade un tercer bloque a la pestaña Ganancias con las unidades.

**Depende del Proyecto C**, ya terminado: reutiliza `RangoUtc`, el patrón de
agrupación por mes local, el bloque de filtros y el aviso de asimetría de CAT.

## Las dos vías, y por qué van separadas

Un cuy se vende por **una de dos vías**, nunca por las dos:

| Vía | Qué es | De dónde sale el número |
|---|---|---|
| **Venta local** | Se vende en la comunidad, en el CAT. Nunca llega a la planta. | `CuyRegistro.VentaLocalPagoId`, fechado por el `Pago` |
| **Despacho** | Sale faenado de la planta hacia un cliente. | `Despacho.CantidadUnidades`, por `FechaDespacho` |

Son las dos mitades que el reporte ya trata por separado en el dinero, y se
mantienen separadas aquí.

## Aquí sumar SÍ es válido, y es la excepción

El resto de la pestaña no suma nada: las dos cifras de dinero **nunca** se
suman, porque un pago a una productora es ingreso para ella y costo para la
cooperativa —la misma fila leída desde dos lados.

**Las unidades son distintas.** Un cuy vendido en la comunidad **no puede**
acabar despachado: el sistema lo impide en cuatro sitios —la movilización, el
selector de lotes pendientes de pago, el botón «A planta» y el faenamiento—, y
esa guarda está probada. Así que no hay doble conteo y `comunidad + despachado`
es un número real.

Va una **columna de total**, porque es la pregunta que motivó la feature. El
rótulo tiene que dejar claro que suma **animales**, no dinero, para que no se
lea como permiso para sumar las cifras de arriba.

## Neto de devoluciones

Las unidades despachadas se cuentan **descontando las devoluciones**, igual que
el ingreso desde la revisión final del Proyecto C.

Si fueran brutas, las dos cifras se contradirían sobre el mismo despacho: el
ingreso diría que se vendieron 140 unidades y las unidades dirían 200. Es el
defecto que ese proyecto dedicó tres rondas de revisión a eliminar, y no se
reintroduce aquí.

## La asimetría del filtro por CAT se repite

La venta local **sí** filtra por CAT: el animal tiene productora, y la
productora tiene su centro asignado.

El despacho **no**: un despacho mezcla animales de varias jaulas y por tanto de
varios CAT, así que filtrarlo o duplicaría unidades de un despacho mixto o las
atribuiría a un centro que solo puso una parte. Es la misma decisión —y el mismo
motivo— que dejó las vistas de margen sin filtro de CAT.

**Se reutiliza el aviso que la pestaña ya tiene** para el bloque de margen: el
subtítulo fijo y el banner ámbar que aparece cuando hay una CAT elegida. No se
inventa un aviso nuevo.

## Lo que se construye

### API — un endpoint

`GET /api/reportes/unidades/mes` → `UnidadesMesDto(string Agrupacion, int VendidasComunidad, int DespachadasClientes, int Total)`

Mismos roles que el resto del proyecto:
`[Authorize(Roles = "AdminCooperativa,AdminTecnico,OperadorFaenamiento")]`.
El `OperadorCAT` **no** entra.

Acepta `desde`, `hasta` y `cat` — este último aplicándose **solo** a la columna
de comunidad, por lo dicho arriba.

**Los totales del período no llevan endpoint propio:** salen de sumar esa misma
respuesta en el front. Una segunda llamada para un dato derivado sería una vía
más por la que las dos cifras podrían discrepar.

### Agrupación por el mes local

Igual que las vistas existentes: se agrupa por el día **local** del piloto, no
por el UTC, y como `FechaUtc.ALocal` no se traduce a SQL, **se materializa antes
de agrupar**. Con el comentario que impide que alguien lo «optimice» de vuelta a
un `GroupBy` en base de datos, que rompería la frontera del mes en silencio.

Un despacho de las 20:00 del 31 de agosto pertenece a agosto.

### Front — un bloque

Tercer bloque en `Reportes.tsx`, bajo los dos actuales, con su propio título
**«Unidades vendidas»**:

1. Las **dos cifras del período** arriba, más el total.
2. Una **tabla por mes** con las tres columnas.

Reutiliza `FiltrosPeriodo` y el patrón de `PanelEstado`, que distingue «falló la
petición» de «no hay datos».

### Excel — una sexta hoja

El libro pasa de cinco hojas a seis. **El Excel es el que va a la reunión**, y
dejar fuera la cifra que motivó esta petición obligaría a volver a la pantalla
para leerla.

La hoja lleva, como las de margen, su **línea de alcance**: que la columna de
comunidad está filtrada por CAT y la de despacho no.

## Plan de pruebas

| Qué | Tipo | Qué fija |
|---|---|---|
| Conteo de comunidad | Integración | Solo cuenta cuyes con `VentaLocalPagoId`, fechados por su pago |
| Conteo de despacho | Integración | Suma `CantidadUnidades` por `FechaDespacho` |
| Neto de devoluciones | Integración | Un despacho con devoluciones cuenta las unidades netas, **con números donde neto y bruto difieren sin ambigüedad** |
| El total | Integración | Es exactamente la suma de las dos columnas |
| Frontera del mes | Integración | Un despacho de las 20:00 del último día cae en **su** mes, no en el siguiente |
| El filtro de CAT | Integración | Filtra la columna de comunidad y **no** la de despacho |
| Autorización | Integración | Los tres roles entran; el `OperadorCAT` no |
| La sexta hoja | Integración | El libro trae seis hojas con sus nombres, y la de unidades lleva su línea de alcance |

Cada guarda nueva se comprueba **por mutación**: romperla, verla en rojo,
restaurarla. **Si una mutación no pone roja su prueba, se para y se avisa** — no
se ajusta la prueba.

**Los números de cada sembrado tienen que distinguir lo correcto de lo
incorrecto.** En el Proyecto C una prueba del plan se escribió con datos donde
el bien y el mal daban la misma cifra, y costó una ronda entera de revisión.

## Riesgos y límites

- **La cifra de comunidad se fecha por el pago, no por la entrega.** Es lo
  correcto —la venta ocurre cuando se cobra— pero significa que un cuy entregado
  en julio y vendido en agosto cuenta en agosto. Coherente con las tres vistas de
  ganancias, que también van por `FechaPago`.
- **Las devoluciones no están acotadas por fecha**, igual que en el margen: una
  devolución de marzo baja las unidades de enero si enero se vuelve a consultar.
  Es el comportamiento honesto —lo alternativo dejaría la devolución en un mes al
  que no pertenece— y ya lleva su comentario en el código.
- **El front no tiene Vitest ni Playwright.** El bloque se verifica a mano,
  además de `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`.

## Fuera de alcance, a propósito

- **Desglose por CAT, por productora y por cliente.** Cinco vistas más, en buena
  parte solapadas con lo que las tablas de dinero ya ordenan. Se empieza por el
  mes, que es la única agrupación que sirve a las dos vías a la vez.
- **Unidades en el resto de pestañas.** «Salida» sigue listando fila por
  despacho sin totalizar; cambiarlo es otro proyecto.
- **Peso vendido.** El sistema tiene el peso de cada canal, pero la petición era
  de unidades.

# Venta local — diseño

**Fecha:** 2026-08-22
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

Este es el **Proyecto B** de la descomposición en cinco del pedido original.
Cubre las features 1, 2, 3, 6 y 8: registrar que un lote —o parte de él— se
vendió en la comunidad en vez de ir a la planta de faenamiento.

| # | Proyecto | Estado |
|---|---|---|
| A | Correcciones puntuales | terminado, pendiente de verificación manual y PR |
| **B** | **Venta local** | **este documento** |
| C | Reportes de ganancias | depende de B |
| D | Trazabilidad del transporte | independiente |
| E | Retención a 90 días | independiente |

Hoy el sistema asume un único destino para cada jaula: la planta. `Movilizacion`
es 1:1 con `Lote` y valida la cantidad movilizada contra el total recibido. La
venta local rompe ese supuesto: un lote pasa a poder tener **dos destinos
parciales**.

## Un choque de nombres, primero

La pestaña **«Locales»** de Recepción ya existe y significa **lotes capturados
sin conexión que esperan sincronizar**. No tiene ninguna relación con vender en
la comunidad, y dos cosas distintas no pueden llamarse igual en la misma
pantalla.

**Se renombra a «Sin sincronizar»**, que es literalmente lo que muestra. Es un
cambio de etiqueta, y de paso la pestaña se explica sola.

## El modelo: marcar cuyes, no inventar entidades

`CuyRegistro` ya lleva su propia productora —la jaula es multi-productora—, así
que «vendí estos 10 de los 12» es **marcar filas que ya existen**.

| Cambio | Qué es |
|---|---|
| `CuyRegistro.VentaLocalPagoId` | FK anulable al `Pago` de la venta. Nulo = disponible para la planta |
| `Pago.EsVentaLocal` | bandera explícita, `false` por defecto |
| `Pago.NumeroDias`, `Pago.ValorPorDia` | se reactivan para el pago a cuotas |

**No hay entidad `VentaLocal`.** El `Pago` ya tiene monto, fecha, responsable y
método; lo único que faltaba es *qué animales*, y eso son filas marcadas. Una
tabla nueva solo añadiría un intermediario sin datos propios.

**`EsVentaLocal` es explícita y no derivada** a propósito. Derivarla —«es venta
local si tiene cuyes marcados»— obligaría a una consulta por pago cada vez que
hay que decidir si entra en la cola de la planta, y dejaría el filtro de la cola
dependiendo de un `Any()` sobre otra tabla.

**Índice:** `CuyRegistro.VentaLocalPagoId` va indexado y con borrado `Restrict`.
Un pago no se borra nunca en este sistema, pero dejar `Cascade` significaría que
borrarlo desmarcaría los animales en silencio.

### Las invariantes que el servidor hace cumplir

1. Un pago con `EsVentaLocal = true` tiene **al menos un cuy marcado**.
2. Los cuyes marcados son **de esa productora y de ese lote**. Es la misma regla
   de trazabilidad que ya gobierna los descuentos: sin ella, una operadora
   podría marcar animales de otra productora.
3. Un cuy **no se puede vender dos veces**: solo se marcan los que tienen
   `VentaLocalPagoId` nulo.
4. **No se vende un lote que ya se movilizó.** Los animales ya no están.
5. `POST /pagos/{id}/pagar` y `POST /pagos/{id}/verificar` **rechazan con 409** un
   pago de venta local. La planta no tiene nada que hacer ahí, y sin esta guarda
   podría «pagar» una venta que ya está cobrada.

### La carrera que hay que cerrar

Dos ventas locales simultáneas sobre el mismo lote pueden seleccionar el mismo
cuy. Comprobar que está libre y guardar después deja una ventana: el último en
escribir se lleva el animal y el otro pago queda cobrando por algo que no
vendió.

**El marcado se hace condicional**: se actualizan solo las filas cuyo
`VentaLocalPagoId` siga siendo nulo, y **se compara el número de filas afectadas
contra el número de cuyes seleccionados**. Si no coinciden, la operación entera
se deshace con 409.

Es el mismo tipo de defecto que el ciclo de pago tuvo con dos `/pagar`
concurrentes, y se cierra igual: en la escritura, no en la comprobación previa.

### Un animal rechazado sí se puede vender localmente

`CuyRegistro.Estado` puede ser `Rechazado` —típicamente por bajo peso—, y esos
animales no van a la planta. **Se permite venderlos localmente a propósito**: es
justamente uno de los destinos razonables de un animal que no cumple el estándar
de faenamiento, y prohibirlo obligaría a la CAT a llevarlo fuera del sistema.

## La resta al enviar a planta

`MovilizacionService.RegistrarAsync` valida hoy
`CantidadMovilizada <= lote.CantidadAnimales`. Pasa a calcular **disponibles** =
cuyes del lote con `VentaLocalPagoId` nulo, y:

- si `disponibles == 0` → **se rechaza el envío**: el lote entero se vendió;
- si quedan, el tope es **disponibles**, no el total de la jaula.

Esto resuelve solo el caso de las dos productoras: se restan los 3 de una y los
12 de la otra siguen viajando, **porque el cálculo es por animal y no por
productora**.

## Una consecuencia que hay que arreglar de paso

`ListarLotesPendientesAsync` considera hoy pagado un lote si existe **cualquier**
pago de esa productora sobre él:

```csharp
var pagados = db.Pagos
    .Where(p => p.ProductoraId == productoraId && p.LoteId != null)
    .Select(p => p.LoteId!.Value);
```

Con la venta local eso rompe: vender 3 de 15 haría **desaparecer el lote de
«pendientes de pago»** aunque los otros 12 vayan a la planta y aún haya que
cobrarlos.

Pasa a mirar solo los pagos **que no son venta local**, y el conteo de cuyes que
devuelve pasa a ser el de los **no vendidos** — que es la base sobre la que la
planta va a pagar.

## Que la planta no se entere

`ListarPorPagarAsync` filtra por `Estado == Pendiente`, y una venta local nace
`Recibido`, así que ya quedaría fuera. Se añade igualmente
`&& !p.EsVentaLocal` como segunda defensa, con su prueba: la cola de la planta
es lo que decide qué trabajo ve el operador de faenamiento, y no puede depender
de un solo predicado indirecto.

## El ciclo del pago local

Una venta local **nace cobrada**:

| Campo | Valor |
|---|---|
| `Estado` | `Recibido` |
| `MontoPagadoUsd` | igual a `MontoUsd` — no hay descuentos, la planta no participa |
| `EsVentaLocal` | `true` |
| `MetodoPago` | `Efectivo`, `Transferencia` o `Cuotas` |
| `PagadoPor`, `ComprobanteUrl`, `FechaVerificacion`, `VerificadoPor` | **nulos** |

Los cuatro últimos se quedan nulos a propósito: rellenarlos sería afirmar que
alguien transfirió y alguien verificó, y no ocurrió ninguna de las dos cosas.

**No se anula una venta local.** El sistema ya declara que las transiciones de
pago son de un solo sentido —«no se anula un pago, se corrige con otro»— y aquí
vale igual.

### La decisión sobre las cuotas, y lo que cuesta

Una venta a cuotas **también nace `Recibido`**, el día que se pacta, con el
dinero todavía sin llegar. Se acepta a sabiendas, por dos motivos: dentro del
sistema no queda nada pendiente que nadie tenga que hacer, y construir
seguimiento de cuotas es un proyecto propio con su modelo y su pantalla.

**El papel no miente:** el ticket dice «A cuotas: N días × USD X».

**Lo que esto obliga al Proyecto C:** el reporte de ganancias **debe mostrar el
dinero de las ventas a cuotas en una columna aparte**, no sumado con lo cobrado
en efectivo. Sin esa separación, una CAT con muchas ventas a plazo vería
ganancias que todavía no tiene en caja. Queda escrito aquí porque es una
obligación que este proyecto le impone al siguiente.

## El papel

### El ticket

Un ticket de venta local dice qué es y bajo qué condiciones. Siguiendo la regla
que ya rige en este repositorio —**del PDF no se puede afirmar nada, así que
todo texto que va al papel se compone en una función pura**—, `TextosTicket`
crece con:

- el rótulo de estado propio de la venta local, en vez de los tres del ciclo con
  la planta;
- la línea del método de pago, con el acuerdo de cuotas cuando lo hay;
- la lista de los animales vendidos.

### La guía de movilización

Aparece un bloque nuevo con los cuyes vendidos localmente —número, productora y
fecha de la venta— y la cantidad movilizada deja de leerse sola: se ve contra la
recibida, para que la diferencia tenga explicación en el mismo papel.

Una guía de un lote sin ventas locales **sale idéntica a la de hoy**. Es la
garantía de no regresión.

## La pantalla

**Recepción → lista de lotes.** Un botón **«Vender local»** junto a «A planta»,
visible mientras el lote esté **cerrado, no rechazado, sin movilización y con
cuyes disponibles**. Las mismas condiciones que «A planta», más la última.

El modal pide: la productora (si la jaula es multi-productora), las casillas de
**sus** cuyes disponibles, el monto, y la forma de pago —transferencia, efectivo
o cuotas—. Con cuotas aparecen los dos campos del acuerdo.

Al registrar, **el ticket se abre para imprimir**, igual que el pago a la planta.

**Cuando el lote entero se ha vendido**, desaparecen «A planta» y «Vender
local» —no queda nada que hacer con ese lote— y en el sitio donde hoy dice
«Enviado ✓» queda la etiqueta **«Venta local»**. **El botón «Guía PDF» se
queda**: la guía sigue siendo el documento del lote y ahora además lista lo que
se vendió.

**Objetivos táctiles de 44 px**: es una tablet de 7 pulgadas que se usa en campo
y con guantes.

## Plan de pruebas

| Qué | Tipo | Qué fija |
|---|---|---|
| Marcado de cuyes | Integración | Los cuyes seleccionados quedan atados al pago y dejan de estar disponibles |
| Trazabilidad | Integración | No se pueden vender cuyes de otra productora ni de otro lote |
| Doble venta | Integración | Un cuy ya vendido no se puede vender otra vez |
| **Concurrencia** | Integración | Dos ventas simultáneas sobre el mismo cuy: una gana, la otra recibe 409 y **no deja el pago escrito** |
| Resta al movilizar | Integración | El tope es el de disponibles; con cero disponibles el envío se rechaza |
| Lote a medias | Integración | 3 vendidos de 15 → se movilizan 12 y el lote sigue en «pendientes de pago» |
| Cola de la planta | Integración | Una venta local **no** aparece en `/api/pagos/por-pagar` |
| Guardas del ciclo | Integración | `/pagar` y `/verificar` responden 409 sobre una venta local |
| Textos del ticket | Unitaria | Rótulo, método y acuerdo de cuotas, como funciones puras |

Cada guarda nueva se comprueba **por mutación**: quitarla, ver la prueba en
rojo, restaurarla. En los dos proyectos anteriores este paso encontró cuatro
pruebas que pasaban con el fallo presente.

Las pruebas corren solo dentro de Docker
(`docker compose -f docker-compose.tests.yml run --rm tests`) porque Smart App
Control bloquea la carga del DLL desde OneDrive.

## Riesgos y límites

- **La guía y el ticket acaban en papel.** Se pueden fijar las funciones, no la
  maquetación. El bloque de vendidos localmente y el ticket de venta local
  **necesitan que alguien los imprima y los mire**.
- **El front no tiene Vitest ni Playwright.** El modal de selección de cuyes se
  verifica a mano, además de `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`.
- **Una venta a cuotas cuenta como cobrada.** Documentado arriba, con la
  obligación que impone al Proyecto C.

## Fuera de alcance, a propósito

- **Seguimiento de cuotas**: qué cuota se pagó y cuánto queda. Es un proyecto
  propio.
- **A quién se le vendió.** La trazabilidad del sistema termina donde el animal
  sale del CAT; añadir comprador obligaría a decidir catálogo o texto libre.
- **Deshacer una venta local.** Coherente con que un pago no se anula.
- **El precio por animal.** El monto lo sigue escribiendo la operadora, como en
  todos los pagos: no existe tabla de precios en el sistema.

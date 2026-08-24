# Reportes de ganancias — diseño

**Fecha:** 2026-08-23
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

Este es el **Proyecto C** de la descomposición del pedido original. Cubre la
feature 7 —la vista de reportes con la información económica— **y la captura del
precio de venta en la pantalla de despacho**, sin la cual la mitad de esa
información no existe.

| # | Proyecto | Estado |
|---|---|---|
| A | Correcciones puntuales | terminado, pendiente de verificación manual y PR |
| B | Venta local | terminado, pendiente de verificación manual y PR |
| **C** | **Reportes de ganancias + precio de venta** | **este documento** |
| D | Trazabilidad del transporte | independiente |
| E | Retención a 90 días | independiente |

**Depende del Proyecto B.** Sin la venta local no existe la mitad de lo que este
reporte suma, ni la bandera `Pago.EsVentaLocal` que distingue las dos vías.

**Cambio de alcance respecto al borrador anterior de este spec.** La primera
versión declaraba el margen fuera de alcance, porque el sistema no registraba el
precio de venta. Ahora se registra, y el margen entra: son **dos cifras
distintas** y el reporte no las mezcla.

## Las dos cifras, y por qué son distintas

| Cifra | Qué mide | Para quién |
|---|---|---|
| **Lo que ganaron las productoras** | El dinero que la cooperativa movió hacia ellas | Es su ingreso, y el egreso de la cooperativa |
| **El margen de la reventa** | Lo cobrado a los clientes menos el costo de los animales vendidos | Es lo que le queda a la cooperativa |

Confundirlas es el error fácil: un pago a una productora es **ingreso para ella y
costo para la cooperativa**, la misma fila leída desde dos lados. El reporte las
presenta por separado y nunca las suma.

---

# Parte 1 · El precio de venta

## El campo

`Despacho` gana **`PrecioUnitarioUsd`** (`decimal?`, precisión 10,2).

- **Anulable en el esquema** por los despachos que ya existen, que no lo tienen.
- **Obligatorio en el servicio** para los despachos nuevos: mismo criterio que se
  aplicó a `Pago.LoteId` cuando el ticket pasó a exigir lote.

El **total de la venta no se guarda: se deriva** como
`PrecioUnitarioUsd × CantidadUnidades`. Guardarlo abriría la puerta a que las dos
cifras se contradigan —el defecto que este sistema ya sufrió con
`MontoPagadoUsd` y sus descuentos—, y derivarlo lo hace imposible.

Se multiplica por `CantidadUnidades` y no por `Cuyes.Count` porque
`CantidadUnidades` es **lo que la operadora vio y confirmó al registrar**. Si
alguna vez ambas divergieran, el importe tiene que corresponder a lo que ella
aceptó, no a un recuento que no miró.

## Los despachos sin precio no valen cero

Un despacho anterior a este cambio **no tiene precio**, y eso no es lo mismo que
haberse vendido gratis. El reporte los cuenta aparte y los muestra como
**«sin precio registrado»**, con su número. Sumarlos como cero rebajaría el
ingreso del período con una cifra inventada.

## La pantalla

El formulario de despacho gana el campo de precio unitario, con el total
calculado a la vista mientras se escribe — para que la operadora vea la cifra
final antes de confirmar y detecte un dedazo de un cero.

**Objetivos táctiles de 44 px**: tablets de 7 pulgadas en campo y con guantes. La
convención del repositorio es `min-h-[44px]`; **`min-h-12` no existe en este
Tailwind y no aplicaría nada.**

---

# Parte 2 · Lo que ganaron las productoras

La cifra es el dinero que la cooperativa movió hacia ellas, por período.

## Tres columnas que no se suman entre sí

| Columna | Qué es | Por qué va aparte |
|---|---|---|
| **Cobrado en venta local** | Ventas locales con método `Efectivo` o `Transferencia` | El dinero cambió de manos delante de la operadora |
| **Pactado a cuotas** | Ventas locales con método `Cuotas` | **El dinero no ha llegado.** Nace `Recibido` porque no queda nada pendiente *dentro del sistema*, que no es lo mismo que estar cobrado |
| **Pagado por la planta** | Pagos que no son venta local, en estado `Pagado` o `Recibido` | Es la vía del ciclo con la planta |

Esta separación es una **obligación explícita** que el spec de la venta local le
dejó a este proyecto: sin ella, una CAT con muchas ventas a plazo vería
ganancias que todavía no tiene en caja.

### Se suma `MontoPagadoUsd`, no `MontoUsd`

Para los pagos de la planta, la diferencia entre ambos son **los descuentos por
novedades**. Sumar `MontoUsd` contaría como pagado un dinero que el propio
sistema sabe que no se pagó. En las ventas locales los dos coinciden —el servicio
los iguala al registrar—, así que la regla es uniforme.

### Los pagos pendientes no cuentan

Un pago en estado `Pendiente` es un ticket emitido que la planta todavía no ha
transferido. No es dinero movido.

---

# Parte 3 · El margen de la reventa

## Ingreso

La suma de `PrecioUnitarioUsd × CantidadUnidades` de los despachos del período,
**más el conteo de los que no tienen precio**, que se declara junto a la cifra y
no dentro de ella.

## Costo: se rastrea animal por animal, no se estima

El modelo ya permite llegar del producto vendido a quien lo crió:

```
DespachoCuy → CuyFaenamiento → RegistroFaenamiento → Lote (jaula)
                                    │
              CuyFaenamiento.NumeroEnLote → CuyRegistro → Productora
                                                              │
                                              Pago de esa productora en ese lote
```

El costo de **un animal despachado** es el pago de su productora por ese lote,
dividido entre los animales que ese pago cubrió:

```
costoPorAnimal = Pago.MontoPagadoUsd / (cuyes de esa productora en ese lote
                                        que NO se vendieron localmente)
```

**El denominador excluye lo vendido en la comunidad** a propósito: esos animales
nunca llegaron a la planta y su pago fue otro. Es además el mismo conteo que la
operadora vio al crear el pago, después de que el Proyecto B corrigiera
`ListarLotesPendientesAsync`.

## Lo que el reporte no puede saber, y dice

Un animal despachado cuya productora **todavía no ha cobrado** ese lote no tiene
costo conocido. No vale cero: vale *desconocido*.

El reporte declara, junto al margen, **cuántos animales del período tienen costo
incompleto**. Un margen calculado ignorando eso sería optimista justo cuando más
falta pagar. Es la misma honestidad que la columna de despachos sin precio.

## Dos vistas

- **Por período (mes).** Ingreso, costo atribuido, margen, y las dos advertencias.
- **Por cliente.** Lo mismo agrupado por `ClienteDestino`, para saber cuál deja
  más.

**`ClienteDestino` es texto libre**, así que «Mercado Central» y «mercado
central» serían dos filas. Se agrupa normalizando espacios y mayúsculas, y queda
anotado que un catálogo de clientes resolvería esto de raíz — pero es otro
proyecto.

## Por qué no hay margen por despacho

Se descartó a propósito. El costo por animal sale de **repartir un pago que
cubrió varios**, así que a nivel de un despacho suelto el redondeo pesa más que
la señal. Agregado por mes o por cliente, ese ruido se compensa. Prometer una
cifra por despacho sería dar una precisión que el dato no sostiene.

---

# Lo común a todo el reporte

## Período y agrupación

Sobre el mismo `FiltroPeriodoDto` que ya usan los demás reportes, con
`FechaUtc.InicioDelDiaLocal` para traducir los días que elige el usuario.

**El mes se agrupa por el día local del piloto**, no por el UTC: un despacho
registrado a las 20:00 del 31 de agosto pertenece a agosto, y agrupar por UTC lo
mandaría a septiembre. Es el cuidado que `FechaUtc` ya documenta, y que corrigió
un fallo real.

## Vistas

1. **Por centro de acopio** — lo pagado a las productoras de cada CAT.
2. **Por productora** — lo que cobró cada una.
3. **Por mes** — ambas cifras: lo pagado y el margen.
4. **Por cliente** — ingreso, costo atribuido y margen.

## Acceso

`AdminCooperativa`, `AdminTecnico` **y `OperadorFaenamiento`**.

**Es más de lo que pedía la petición original**, que nombraba solo a los dos
administradores. Se amplió a propósito, y queda escrito para que el cambio sea
visible.

Sin filtro por CAT: es información agregada de todos los centros.

## Descarga

Excel con ClosedXML, como el resto. Una hoja por vista.

## La pantalla

Una pestaña nueva en `Reportes.tsx`, junto a las siete que ya existen. Reutiliza
`FiltrosPeriodo` y el patrón de `PanelEstado`, que distingue «falló la petición»
de «no hay datos» — una distinción que ese archivo documenta como aprendida de un
bug reportado.

No se muestra al `OperadorCAT`, que no tiene el endpoint.

## Plan de pruebas

| Qué | Tipo | Qué fija |
|---|---|---|
| Precio obligatorio | Integración | Un despacho nuevo sin precio se rechaza; uno con precio se acepta |
| Total derivado | Integración | El ingreso es precio × cantidad, y no hay columna almacenada que pueda descuadrarse |
| Sin precio ≠ cero | Integración | Un despacho antiguo no baja el ingreso; se cuenta aparte |
| Separación de columnas | Integración | Una venta a cuotas **no** se suma con lo cobrado |
| Monto correcto | Integración | Un pago con descuento suma `MontoPagadoUsd`, no `MontoUsd` |
| Pendientes fuera | Integración | Un ticket emitido y no pagado no aparece en ninguna columna |
| Costo atribuido | Integración | Un despacho de 3 animales de una productora que cobró 120 por 12 cuesta 30 |
| Denominador | Integración | Con 2 de esos 12 vendidos localmente, el costo por animal sale de dividir entre 10, no entre 12 |
| Costo desconocido | Integración | Un animal cuya productora no ha cobrado se cuenta como incompleto, no como cero |
| Frontera del mes | Integración | Un despacho de las 20:00 del último día cae en **ese** mes |
| Alcance | Integración | El `OperadorCAT` recibe 403; los tres roles previstos, 200 |
| Excel | Integración | El archivo se descarga y no está vacío |

Cada guarda se comprueba **por mutación**: romperla, verla en rojo, restaurarla.
En los proyectos A y B ese paso encontró trece problemas, siete de ellos
suposiciones falsas del propio plan.

Las pruebas corren solo dentro de Docker
(`docker compose -f docker-compose.tests.yml run --rm tests`) porque Smart App
Control bloquea la carga del DLL desde OneDrive.

## Riesgos y límites

- **Todo depende de cifras tecleadas.** El monto del pago y el precio del
  despacho los escribe una persona, y no hay tabla de precios en el sistema. Un
  error de dedo se propaga al reporte sin que nada lo detecte.
- **El costo por animal es un prorrateo.** Un pago es una cifra global por los
  animales de una productora en una jaula; repartirla a partes iguales asume que
  todos valían lo mismo. Es la única atribución posible con los datos que hay, y
  el reporte no promete más precisión de la que tiene: por eso no hay margen por
  despacho.
- **Una venta a cuotas cuenta en su columna el día que se pacta**, no cuando se
  cobra. El sistema no hace seguimiento de cuotas —decisión del Proyecto B— así
  que el rótulo «pactado» es la única defensa.
- **`ClienteDestino` es texto libre.** La agrupación normaliza, pero dos formas de
  escribir el mismo cliente pueden separarse igual.

## Fuera de alcance, a propósito

- **Catálogo de clientes.** Resolvería de raíz la agrupación por nombre, pero es
  un CRUD propio con su pantalla.
- **Margen por despacho individual.** Descartado por precisión, no por esfuerzo.
- **Seguimiento de cuotas.** Declarado fuera de alcance en el Proyecto B.
- **Otros costos de la cooperativa** —transporte, faenamiento, empaque—. El
  margen que este reporte da es sobre el costo de los animales, no un resultado
  contable. El rótulo tiene que decirlo.
- **Gráficos.** Empieza en tablas; si hacen falta, se añaden viendo el uso real.

# Reportes de ganancias — diseño

**Fecha:** 2026-08-23
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

Este es el **Proyecto C** de la descomposición en cinco del pedido original.
Cubre la feature 7: una vista de reportes con la información económica de las
ventas locales y de los pagos del centro de faenamiento.

| # | Proyecto | Estado |
|---|---|---|
| A | Correcciones puntuales | terminado, pendiente de verificación manual y PR |
| B | Venta local | terminado, pendiente de verificación manual y PR |
| **C** | **Reportes de ganancias** | **este documento** |
| D | Trazabilidad del transporte | independiente |
| E | Retención a 90 días | independiente |

**Depende del Proyecto B.** Sin la venta local no existe la mitad de lo que este
reporte tiene que sumar, ni la bandera `Pago.EsVentaLocal` que las distingue.

## Qué mide, y qué no

La cifra es **el dinero que la cooperativa movió hacia las productoras**,
agrupado por período.

**No es un margen, y no puede serlo.** El sistema **no registra a qué precio se
revende el producto**: `Despacho` no lleva importe. Un reporte que dijera
«ganancia» como diferencia entre ingreso y coste estaría inventando la mitad de
la resta. Este reporte dice lo que el sistema sabe: cuánto se pagó, a quién, por
qué vía y cuándo.

Si en el futuro se quiere el margen real, hace falta primero registrar el precio
de venta en el despacho. Es un proyecto propio, y este documento no lo prejuzga.

## La separación que el Proyecto B dejó obligada

Las tres cifras **no se suman en una sola columna**:

| Columna | Qué es | Por qué va aparte |
|---|---|---|
| **Cobrado en venta local** | Ventas locales con método `Efectivo` o `Transferencia` | El dinero cambió de manos delante de la operadora |
| **Pactado a cuotas** | Ventas locales con método `Cuotas` | **El dinero no ha llegado.** Nace `Recibido` porque no queda nada pendiente *dentro del sistema*, pero eso no es lo mismo que estar cobrado |
| **Pagado por la planta** | Pagos que no son venta local, en estado `Pagado` o `Recibido` | Es la vía del ciclo con la planta de faenamiento |

Esta separación es una **obligación explícita** que el spec de la venta local le
dejó a este proyecto: sin ella, una CAT con muchas ventas a plazo vería
ganancias que todavía no tiene en caja.

### Se suma `MontoPagadoUsd`, no `MontoUsd`

Para los pagos de la planta, la diferencia entre ambos son **los descuentos por
novedades**. Sumar `MontoUsd` contaría como pagado un dinero que el propio
sistema sabe que no se pagó, e inflaría la cifra justo donde ya tiene el dato
correcto.

Para las ventas locales los dos valores coinciden —el servicio los iguala al
registrar— así que la regla es uniforme: **siempre `MontoPagadoUsd`**.

### Los pagos pendientes no cuentan

Un pago en estado `Pendiente` es un ticket emitido que la planta todavía no ha
transferido. No es dinero movido. Queda fuera de las tres columnas.

## Las tres vistas

Sobre el mismo `FiltroPeriodoDto` que ya usan los demás reportes, y con el mismo
`FechaUtc.InicioDelDiaLocal` para traducir los días que elige el usuario:

1. **Por centro de acopio** — una fila por CAT. Responde «cuánto movió cada
   centro» y encaja con el reporte «Por CAT» que ya existe.
2. **Por productora** — una fila por productora, con lo que cobró en el período.
3. **Por mes** — una fila por mes, para ver la estacionalidad.

Las tres llevan las mismas tres columnas más el total, y el conteo de pagos que
las compone.

**El mes se agrupa por el día local del piloto**, no por el UTC: un pago
registrado a las 20:00 del 31 de agosto pertenece a agosto, y agrupar por UTC lo
mandaría a septiembre. Es el mismo cuidado que `FechaUtc` ya documenta para los
filtros.

## Acceso

`AdminCooperativa`, `AdminTecnico` **y `OperadorFaenamiento`**.

**Esto es más de lo que pedía la petición original**, que nombraba solo a los dos
administradores. Se amplió a propósito: casi todos los reportes del sistema ya
incluyen al operador de faenamiento, y él paga los tickets, así que ve parte de
esas cifras de todos modos. Queda escrito aquí para que el cambio sea visible y
no parezca un descuido.

No hay filtro por CAT: es información agregada de todos los centros, y los tres
roles que entran ya operan sin acotación de centro en el resto de reportes.

## Descarga

Excel con ClosedXML, como el resto de los reportes. Una hoja por vista.

## La pantalla

Una pestaña nueva en `Reportes.tsx`, junto a las siete que ya existen. Reutiliza
`FiltrosPeriodo`, que ya gobierna las demás, y el patrón de `PanelEstado` que
distingue «falló la petición» de «no hay datos» — una distinción que ese archivo
documenta como aprendida de un bug reportado.

La pestaña **no se muestra al `OperadorCAT`**, que no tiene el endpoint.

## Plan de pruebas

| Qué | Tipo | Qué fija |
|---|---|---|
| Separación de columnas | Integración | Una venta a cuotas **no** se suma con lo cobrado |
| Monto correcto | Integración | Un pago con descuento suma `MontoPagadoUsd`, no `MontoUsd` |
| Pendientes fuera | Integración | Un ticket emitido y no pagado no aparece en ninguna columna |
| Frontera del mes | Integración | Un pago de las 20:00 del último día del mes cae en **ese** mes, no en el siguiente |
| Alcance | Integración | El `OperadorCAT` recibe 403; los tres roles previstos, 200 |
| Excel | Integración | El archivo se descarga y no está vacío |

Cada guarda nueva se comprueba **por mutación**. En los dos proyectos
anteriores ese paso encontró trece problemas, siete de ellos suposiciones falsas
del propio plan.

## Riesgos y límites

- **La cifra depende de que el monto esté bien escrito.** Lo teclea la operadora
  y no hay tabla de precios en el sistema: un error de dedo se propaga al
  reporte sin que nada lo detecte. Es una limitación heredada, no introducida
  aquí.
- **Una venta a cuotas cuenta en su columna el día que se pacta**, no cuando se
  cobra. El sistema no hace seguimiento de cuotas —decisión del Proyecto B— así
  que la columna dice «pactado», no «cobrado», y el rótulo es la única defensa.

## Fuera de alcance, a propósito

- **El margen entre compra y venta.** Requiere registrar el precio de venta en el
  despacho. Otro proyecto.
- **Seguimiento de cuotas.** Declarado fuera de alcance en el Proyecto B.
- **Filtro por CAT en este reporte.** Es información agregada por diseño.
- **Gráficos.** Los reportes existentes tienen algunos; este empieza en tablas, y
  si hacen falta se añaden viendo el uso real.

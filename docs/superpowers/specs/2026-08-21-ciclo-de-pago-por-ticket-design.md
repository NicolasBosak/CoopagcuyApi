# Ciclo de pago por ticket — diseño

**Fecha:** 2026-08-21
**Repos afectados:** `CoopagcuyApi` y `CoopagcuyFront/coopagcuy-frontend`
**Estado:** aprobado, pendiente de plan de implementación
**Antecede:** `2026-08-20-evidencia-clinica-en-faenamiento-design.md`, cuya evidencia
fotográfica se reutiliza aquí como apoyo del descuento

## Contexto

Hoy el pago a una productora es un apunte plano: la operadora del centro de acopio
abre un formulario, escribe un monto, elige entre efectivo al contado y pago a
crédito, y se guarda una fila. No hay nada antes ni después. La productora se va
sin papel, y quien realmente paga —la planta de faenamiento— no participa del
registro.

El proceso real tiene tres momentos y dos actores. La CAT reconoce lo que se le
debe a la productora por los cuyes que aportó a un lote. La planta transfiere ese
dinero, a veces menos de lo reconocido porque algún animal llegó defectuoso. Y la
CAT confirma que el dinero llegó. Ninguno de los tres momentos existe en el
sistema.

Este diseño convierte el apunte en un ciclo con estados, le da a la productora un
comprobante impreso de lo que se le debe, y ata cualquier descuento a un defecto
que el centro de acopio documentó — de modo que la planta no pueda pagar de menos
por un problema que nadie vio.

## Estado actual del código

- `Features/Productoras/Models/Pago.cs` — `ProductoraId`, `LoteId` **anulable**,
  `MontoUsd`, `FechaPago`, `MetodoPago` (`"Contado"` | `"Credito"`, con
  `"Efectivo"`/`"Transferencia"` como valores legados), `NumeroDias`,
  `ValorPorDia`, `Responsable`, `Observaciones`. Sin estado ni ciclo de vida.
- `Features/Productoras/Services/PagoService.cs` — 195 líneas. `RegistrarAsync`
  valida que la productora participó en el lote (entregó cuyes, o abrió la
  jaula), calcula `ValorPorDia` en el servidor, y guarda.
  `ListarLotesPendientesAsync` decide qué se debe por ausencia de fila: un lote
  desaparece del selector en cuanto existe un pago de **esa** productora por
  **ese** lote, porque la jaula es multi-productora.
- `Features/Productoras/Controllers/PagosController.cs` —
  `[Authorize(Roles = "OperadorCAT,AdminCooperativa")]` a nivel de clase.
  `FiltroCat()` acota al operador a su propio centro.
- `Features/Productoras/DTOs/ProductoraDto.cs` — alberga `RegistrarPagoDto`,
  `PagoResponseDto` y `LotePendientePagoDto` mezclados con los DTO de productora.
- `Features/Recepcion/Models/Novedad.cs` — tiene `CuyRegistroId` anulable (nulo
  en las novedades de entrega y en las filas anteriores a 2026-08-20), `FotoUrl`
  y `FotoExpiraEn`.
- `Infrastructure/Storage/BlobStorageService.cs` — dos contenedores:
  `qr-coopagcuy` (público) y `evidencias-clinicas` (privado, servido solo por
  endpoint autenticado). Ambos nombres se leen con `IsNullOrWhiteSpace`, no con
  `??`, por el incidente de la cadena vacía.
- `infra/politica-evidencias.json` — una regla de ciclo de vida, borrado a 90
  días con `prefixMatch` sobre `evidencias-clinicas/`.
- `Features/Recepcion/Services/GuiaMovilizacionService.cs` — patrón de PDF a
  seguir: QuestPDF 2024.3.1, `PageSizes.A5`, y `TextoDeclaracionSanitaria` como
  método estático público precisamente porque del binario del PDF no se puede
  afirmar nada en una prueba.
- `src/components/recepcion/FormPago.tsx` — la constante `METODOS` con los dos
  botones (`"💵 Efectivo al contado"`, `"🧾 Pago a crédito"`) y el bloque
  condicional de días de crédito.
- `src/pages/Recepcion.tsx` — pestañas `server | local | pagos`.
- `src/pages/Faenamiento.tsx` — pestañas `faenamientos | llegadas | devoluciones`.
- `src/components/ui/EvidenciaNovedad.tsx` — descarga un blob autenticado con
  React Query, lo muestra como miniatura ampliable, revoca sus object URL y
  distingue caducado de fallo reintentable. Está acoplado al endpoint de
  novedades.

## Decisiones tomadas

| Decisión | Elegido | Descartado y por qué |
|---|---|---|
| Origen del monto | Lo escribe la operadora, como hoy | Precio por cuy o por kilo: no existe ningún precio en el sistema y crearlo era una feature aparte. **Contrapartida asumida: un error de dedo en el monto no lo detecta nadie.** |
| Pago a crédito | Desaparece | Mantenerlo: con una sola transferencia y una sola captura, un pago diferido en cuotas no tiene comprobante único que subir |
| Modelado | Una entidad `Pago` con estados | Dos tablas (`TicketPago` + `Pago`): fiel a la contabilidad, pero la relación es siempre uno a uno y solo añadiría un join por consulta |
| Descuento | La planta registra su propia novedad, validada contra la del CAT | Marcar con casillas las novedades del CAT: más simple, pero pierde lo que observó la planta. Registro libre: rompe la trazabilidad que es el objetivo |
| Cálculo del descuento | Un importe por novedad, el servidor suma | Descontar el cuy entero automáticamente: no admite descuentos parciales. Escribir el total a mano: no se puede desglosar después |
| Sin verificar | Red de seguridad a los 30 días | 90 días: demasiado espacio por pagos olvidados. Nunca: crecimiento sin límite |
| Ancho del ticket | 80 mm | 58 mm: obliga a abreviar y a apilar el desglose |

## Máquina de estados

```
Pendiente ──(faenamiento paga)──> Pagado ──(CAT verifica)──> Recibido
    │                                │                           │
 emite la CAT                 sube la captura +            el comprobante
 imprime el ticket            registra descuentos          expira a 5 días
```

Las transiciones son de un solo sentido y no hay vuelta atrás: no se anula un
pago, se corrige con otro. Cada intento de transición desde un estado que no
corresponde responde `409 Conflict`, no `400` — el cuerpo de la petición es
válido, lo que no encaja es el momento.

## Modelo de datos

### `Pago` — columnas nuevas

| Campo | Tipo | Notas |
|---|---|---|
| `Estado` | `EstadoPago` | `Pendiente` \| `Pagado` \| `Recibido` |
| `MontoPagadoUsd` | `decimal?` | **Lo calcula el servidor**: `MontoUsd` − suma de descuentos. Nulo mientras esté pendiente |
| `FechaPagoEfectivo` | `DateTime?` | Cuándo transfirió la planta |
| `PagadoPor` | `string?` | Operador de faenamiento |
| `ComprobanteUrl` | `string?` | URI del blob de la captura |
| `ComprobanteExpiraEn` | `DateTime?` | Verificación + 5 días |
| `FechaVerificacion` | `DateTime?` | Cuándo confirmó la CAT |
| `VerificadoPor` | `string?` | Operadora que confirmó |

`MontoPagadoUsd` nunca viaja desde el cliente. Es la misma disciplina que ya
aplica `ValorPorDia`, y aquí importa más: es la cifra que la productora cobra.

`NumeroDias` y `ValorPorDia` dejan de escribirse pero las columnas permanecen,
igual que se hizo con el color `Negro` en `TipoNovedad`. `MetodoPago` pasa a
escribirse siempre como `"Transferencia"`.

### `DescuentoPago` — tabla nueva

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | `int` | |
| `PagoId` | `int` | El ticket al que descuenta |
| `NovedadCatId` | `int` | **Obligatorio.** La novedad que registró el CAT |
| `Descripcion` | `string` | Lo que observó la planta |
| `MontoUsd` | `decimal` | Cuánto descuenta |
| `RegistradoPor` | `string` | |
| `FechaRegistro` | `DateTime` | |

`NovedadCatId` es obligatorio y no anulable. Ahí vive toda la trazabilidad de la
feature: una fila de descuento sin novedad de origen sería exactamente el caso
que este diseño existe para impedir.

### Las cuatro reglas del descuento

1. La novedad debe pertenecer a un cuy **de esa productora y de ese lote**. Se
   comprueba contra el `ProductoraId` y el `LoteId` del propio ticket, navegando
   `Novedad.CuyRegistro`.
2. Las novedades sin cuy asociado quedan fuera. Cae por sí sola de la regla 1:
   son las de entrega (`SinAyuno`) y las filas históricas.
3. Índice único `(PagoId, NovedadCatId)` — un mismo defecto no se descuenta dos
   veces. Se defiende en el índice y no solo en el servicio, porque dos
   peticiones simultáneas pasarían las dos por la validación.
4. La suma de descuentos no puede superar `MontoUsd`. Un pago negativo no
   significa nada.

Las cuatro se validan **antes** de tocar el blob del comprobante, por la lección
de las evidencias huérfanas del ciclo anterior: validar todo primero, subir
después.

## El ticket

`GET /api/pagos/{id}/ticket` → `application/pdf`.

QuestPDF con ancho continuo de 80 mm y alto variable: el papel térmico no tiene
páginas, y fijar un alto dejaría avances en blanco o cortaría el pie.

Contenido, en este orden: cabecera COOPAGCUY, número de ticket, fecha de emisión,
productora con cédula y comunidad, lote con su centro de acopio y fecha de
recepción, cuyes aportados y peso total, el monto en cuerpo grande, y la leyenda
de que el documento acredita un pago **pendiente** y no es una factura.

Las líneas cuyo texto depende de una regla —el estado, la leyenda, el desglose de
descuentos si los hubiera— se componen en métodos estáticos públicos, como
`TextoDeclaracionSanitaria` en la guía de movilización, para poder fijarlas por
unidad.

El front lo descarga como blob por el cliente autenticado y lo abre, igual que ya
hace con la guía en `recepcionApi.descargarGuia`. El diálogo de impresión del
sistema operativo se encarga de la impresora térmica.

## El pago desde faenamiento

Pestaña nueva en `Faenamiento.tsx`, junto a las tres existentes. La sirve
`GET /api/pagos/por-pagar`, deliberadamente distinto de `lotes-pendientes`: ese
ya existe, es de la CAT y responde a otra pregunta —qué lotes le faltan por
cobrar a una productora, no qué tickets le tocan pagar a la planta. Dos rutas
que se leen igual y devuelven cosas distintas serían una trampa.

Lista los tickets en estado `Pendiente` de **los tres centros de acopio**: la planta es
única y central, así que aquí no se aplica el `FiltroCat()` que gobierna las
pantallas de la CAT.

Al abrir un ticket, el operador ve los cuyes de esa productora en ese lote que
traen novedad del CAT, **con su evidencia fotográfica**, reutilizando el visor
que ya existe. Por cada uno puede añadir un descuento con su descripción. El
total a pagar se recalcula a la vista, y el servidor lo recalcula de nuevo al
guardar sin fiarse de lo que muestre la pantalla.

Después sube la captura de la transferencia y confirma. Todo ocurre en una sola
petición: descuentos, comprobante y cambio de estado son atómicos. Un pago
marcado sin su captura, o con descuentos a medio guardar, sería peor que un
error.

## El comprobante y su borrado

Contenedor propio `comprobantes-pago`, separado de `evidencias-clinicas` porque
la política de caducidad se aplica por contenedor y los plazos son distintos —
compartirlo borraría las evidencias clínicas a los 30 días.

El borrado combina dos mecanismos, porque ninguno basta solo:

- **Azure** borra a los 30 días de la subida, mediante una segunda regla en
  `infra/politica-evidencias.json`. Es la única garantía dura: no depende de que
  la API esté encendida.
- **La API** deja de servir la imagen en cuanto pasa `ComprobanteExpiraEn`, que
  se fija en verificación + 5 días. Mismo patrón que `FotoExpiraEn`.
- Para que el espacio se libere a los 5 días y no a los 30, la API borra los
  blobs caducados **aprovechando el tráfico**: cada vez que se consulta la lista
  de pagos, barre los que ya expiraron. La CAT y la planta entran a diario, así
  que en la práctica el borrado ocurre al día siguiente de vencer.

Este barrido oportunista existe porque lo obvio no funciona: el contenedor de la
API se apaga cuando no hay tráfico, y una tarea programada dentro de ella no
correría de forma fiable. Si el barrido falla —Blob caído, permisos— se registra
y se sigue: **la consulta de pagos no puede caerse por un borrado**, y Azure
borrará igual el día 30.

## La alerta de la CAT

Contador en la pestaña de pagos de `Recepcion.tsx` con los tickets en estado
`Pagado` de su centro. Dentro, esas filas destacadas, con la miniatura del
comprobante y el botón de marcarlo como recibido.

`EvidenciaNovedad` ya resuelve el problema de mostrar un blob autenticado
—descarga por React Query, miniatura ampliable, revocación de object URL,
caducado distinguido de fallo reintentable— pero está acoplado al endpoint de
novedades. Se extrae su interior a un `ImagenProtegida` genérico que reciba la
función de descarga y los textos, y `EvidenciaNovedad` queda como una envoltura
de tres líneas. Es refactor al servicio del objetivo, no oportunista.

## Endpoints y autorización

`PagosController` deja de tener un único `[Authorize]` de clase. Cada endpoint
declara su rol:

| Endpoint | Roles | Acota por CAT |
|---|---|---|
| `POST /api/pagos` | OperadorCAT, AdminCooperativa | sí |
| `GET /api/pagos` | OperadorCAT, AdminCooperativa | sí |
| `GET /api/pagos/lotes-pendientes/{productoraId}` | OperadorCAT, AdminCooperativa | sí |
| `GET /api/pagos/{id}/ticket` | OperadorCAT, AdminCooperativa, OperadorFaenamiento | sí para CAT, no para planta |
| `GET /api/pagos/por-pagar` | OperadorFaenamiento, AdminCooperativa | no |
| `GET /api/pagos/{id}/cuyes-con-novedad` | OperadorFaenamiento, AdminCooperativa | no |
| `POST /api/pagos/{id}/pagar` | OperadorFaenamiento, AdminCooperativa | no |
| `GET /api/pagos/{id}/comprobante` | OperadorCAT, AdminCooperativa, OperadorFaenamiento | sí para CAT |
| `POST /api/pagos/{id}/verificar` | OperadorCAT, AdminCooperativa | sí |

Donde el acceso se deniega por pertenecer a otro centro, se responde `404` y no
`403`, como ya se hace con la foto de novedad: confirmar que el recurso existe
sería filtrar información de otro CAT.

## Reorganización del módulo

El pago se muda de `Features/Productoras/` a `Features/Pagos/`:

```
Features/Pagos/
  Models/Pago.cs, DescuentoPago.cs
  DTOs/PagoDtos.cs              ← extraídos de ProductoraDto.cs
  Services/PagoService.cs        ← ciclo de vida y descuentos
  Services/TicketPagoService.cs  ← generación del PDF
  Controllers/PagosController.cs
  Validators/PagoValidators.cs
```

`PagoService` ya tiene 195 líneas; con estados, descuentos, comprobante y
verificación pasaría del doble. La división evita el archivo de mil líneas y deja
el generador de PDF comprobable por separado.

## Migración de datos

Todo aditivo. Las filas existentes:

- `Estado` = `Recibido`. Son transacciones cerradas del flujo antiguo en
  efectivo; no tienen comprobante y nunca lo tendrán.
- `MontoPagadoUsd` = `MontoUsd`. Se pagó lo que se reconoció.
- `MetodoPago` conserva su valor histórico. No se reescribe.

`LoteId` sigue anulable **en el esquema** por las filas viejas, pero pasa a ser
obligatorio **en el servicio** para los pagos nuevos: un ticket que dice "por los
cuyes que aportó a cierto lote" no puede existir sin lote, y sin lote no hay
novedades que trazar. Es el mismo criterio que se aplicó a `Novedad.CuyRegistroId`.

## Pruebas

Sobre `docker compose -f docker-compose.tests.yml run --rm tests`, con Azurite ya
disponible en el compose y en el workflow.

Ninguna prueba puede asumir `Id == 1`: Respawn trunca sin `RESTART IDENTITY`.

- **Estados:** verificar un pago no pagado responde 409; pagar dos veces
  responde 409; verificar dos veces responde 409.
- **Descuentos:** novedad de otra productora, rechazada; de otro lote,
  rechazada; sin cuy asociado, rechazada; duplicada sobre el mismo pago,
  rechazada; suma mayor al monto, rechazada.
- **Cálculo:** `MontoPagadoUsd` sale de la suma del servidor y no de lo que
  mande el cliente — la prueba envía un valor falso y comprueba que se ignora.
- **Ticket:** el PDF se genera con bytes no vacíos; los textos condicionales se
  fijan por unidad sobre los métodos estáticos.
- **Comprobante:** una imagen inválida no deja blobs huérfanos. Se cuenta por
  diferencia de blobs antes y después, no por filas — una prueba que solo mira
  filas pasa con el fallo presente, como ya ocurrió una vez.
- **Caducidad:** pasado `ComprobanteExpiraEn` el endpoint responde 404 aunque el
  blob siga existiendo.
- **Autorización:** cada endpoint con un rol que no le corresponde; y un
  OperadorCAT pidiendo un pago de otro centro, que debe recibir 404.

En el front no hay Vitest ni Playwright: la verificación es `pnpm lint`,
`tsc -b` y `pnpm build`, más una pasada manual del flujo completo.

## Fases

Tres entregas independientes, cada una desplegable por sí sola:

**A. Ticket y estados.** Modelo, migración, botón único de transferencia,
generación e impresión del ticket. Al terminar, la productora ya se lleva su
papel.

**B. Pago desde faenamiento.** Pestaña de la planta, descuentos trazables,
subida del comprobante, transición a `Pagado`.

**C. Verificación y borrado.** Alerta en la CAT, visor del comprobante,
transición a `Recibido`, política de 30 días y barrido oportunista.

## Fuera de alcance

- Precios por cuy o por kilo. El monto lo sigue escribiendo la operadora.
- Anulación o corrección de un pago ya emitido.
- Pagos parciales o en cuotas — el crédito desaparece.
- Notificación fuera de la aplicación (correo, SMS). La alerta es un contador en
  pantalla.

## Pregunta abierta que este diseño no resuelve

Los umbrales de peso de canal (907 g y 880 g en `FormFaenamiento.tsx` y
`QRService.cs`) siguen sin revisarse tras el cambio del rango de peso vivo a
1200–1500 g. Con un rendimiento del 70 %, 907 g de canal equivalen a unos 1300 g
en pie — el máximo anterior. No afecta a este diseño, pero sí afectaría a
cualquier cálculo de pago basado en peso, que es la puerta que quedó abierta al
decidir que el monto se escribe a mano.

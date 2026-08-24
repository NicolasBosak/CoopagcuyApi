# Correcciones puntuales — diseño

**Fecha:** 2026-08-22
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

Este es el **Proyecto A** de una descomposición en cinco. El pedido original
reunía catorce puntos que no son un proyecto sino cuatro, porque la venta local
—el grueso— cambia el ciclo de vida del lote y arrastra la guía, el bloqueo de
envío y los pagos. Mezclar eso con «quitar dos tarjetas del QR público» hace que
lo pequeño espere por lo grande y que lo grande se diseñe con prisa.

El corte acordado:

| # | Proyecto | Cubre | Depende de |
|---|---|---|---|
| **A** | Correcciones puntuales | Feat 9, Extras 1, 2, 4, 5 + verificar Feat 4 | — |
| B | Venta local | Feats 1, 2, 3, 6, 8 | — |
| C | Reportes de ganancias | Feat 7 | B |
| D | Trazabilidad del transporte | Feat 5 | — |
| E | Retención a 90 días | Extra 3 | — |

Este documento cubre **solo A**. Cada uno de los demás tendrá su propio spec.

Dos de los seis puntos de A resultaron estar **ya implementados**; su entregable
es la prueba que lo demuestre, no el código.

## Principio rector

Cinco de los seis puntos terminan en un PDF, y este repositorio ya aprendió por
las malas que **del PDF no se puede afirmar nada**. `TextosGuia.cs` lo documenta
en su propio comentario: dos defectos llegaron a producción —el objeto
`Comunidad` interpolado en vez de su nombre, y tres valores sin rótulo que se
leían como una lista de opciones— y ninguno era detectable desde una prueba,
porque QuestPDF comprime los flujos de texto del documento.

De ahí la regla del proyecto:

> Todo texto que vaya al papel se compone en una función **pura, pública y
> estática**, y se fija por unidad. El armado del PDF queda como pura
> maquetación.

No es un patrón nuevo: es el que `TextosGuia` + `TextosGuiaTests` ya
establecieron, y el que `TicketPagoService.TextoEstado` / `LeyendaLegal` ya
siguen. Este proyecto lo extiende, no lo inventa.

---

## A1 · La hora local en los documentos

### Síntoma

Un registro hecho a las 15:30 aparece en el informe como las 20:30.

### Causa raíz

Son **dos fallos distintos con la misma cara**:

1. Todo se persiste con `DateTime.UtcNow` —que es correcto— y los documentos
   formatean el valor crudo: `{pago.FechaPago:dd/MM/yyyy HH:mm}` imprime UTC en
   cualquier máquina, la de desarrollo incluida.
2. La guía usa `{DateTime.Now:...}` para la fecha de emisión. En un Windows
   ecuatoriano eso da la hora correcta; en el contenedor Linux, que corre en
   UTC, da la incorrecta. **Por eso el fallo parecía no existir hasta verlo en
   producción.**

### Diseño

Tres funciones en `Common/FechaUtc.cs`, junto al `DesfasePiloto` que ya vive
ahí y que ya documenta por qué −5 es un valor fijo y no una zona del sistema
operativo:

| Función | Devuelve |
|---|---|
| `ALocal(DateTime)` | el instante desplazado a −5, **normalizando el `Kind` primero** |
| `FechaHoraLocal(DateTime?)` | `"21/08/2026 15:30"`, o `"—"` si es nulo |
| `FechaLocal(DateTime?)` | `"21/08/2026"`, o `"—"` si es nulo |

`ALocal` debe pasar por `FechaUtc.Normalizar` antes de restar: un valor que
llegue como `Unspecified` (una ruta de lectura que no venga de Npgsql, un
objeto construido en memoria) se desplazaría igual, y hay que tratarlo como UTC
y no como hora del servidor.

El `"—"` de las variantes anulables **no es adorno**. Hoy
`{pago.Lote?.FechaRecepcion:dd/MM/yyyy}` con un lote nulo interpola cadena
vacía, y en el papel eso deja un renglón que dice `Recibido:` y nada más.

### Sitios de llamada

Aproximadamente 23, todos por reemplazo directo:

- `Features/Pagos/Services/TicketPagoService.cs` — 2
- `Features/Recepcion/Services/GuiaMovilizacionService.cs` — 4 (una es `DateTime.Now`)
- `Features/Faenamiento/Services/FaenamientoService.cs` — 4
- `Features/Reportes/Services/ReportesService.cs` — 13 (dos son `DateTime.Now`), tanto en los PDF como en las hojas Excel

Todo `DateTime.Now` pasa a `DateTime.UtcNow` y de ahí al helper. No queda
ningún `DateTime.Now` en el código de documentos.

### Deliberadamente fuera de alcance

- **`RecepcionService.cs:87` y `:92`** — mensajes de error sobre la fecha de
  captura, no documentos. El primero ya rotula «UTC» explícitamente, así que no
  miente; el segundo compara días y un desfase de cinco horas es inmaterial
  ahí. Tocarlos solo añadiría riesgo de romper una aserción de mensaje.
- **Los códigos de lote y de lote faenado.** `GenerarCodigoLoteAsync`
  (`RecepcionService.cs:826`) y `GenerarCodigoFaenadoAsync`
  (`FaenamientoService.cs:170`) usan la fecha UTC tanto para componer
  `PAT-AAAAMMDD-SEC` / `FAE-AAAAMMDD-SEC` como para contar el secuencial del
  día. Una jaula recibida a las 20:00 del 21 de agosto se llama
  `PAT-20260822-001`. **Es el mismo fallo de fondo**, pero arreglarlo cambia
  identificadores ya emitidos y la semántica del contador diario, no texto
  impreso.

  **Decisión tomada (2026-08-22): no se tocan, y se declara qué son.** El
  segmento `AAAAMMDD` de estos códigos es parte de un **identificador**, no una
  fecha: nombra el día UTC en que se abrió la fila y sirve para que el
  secuencial no colisione. No es un dato que se lea como «el día en que pasó
  esto», y no debe usarse como tal en ningún informe.

  **Lo que esto cuesta, dicho sin adornos.** Al corregir la hora de los
  documentos, esta contradicción pasó de invisible a visible: antes todo mentía
  igual, y ahora un ticket puede decir `PAT-20260822-001` y dos renglones más
  abajo «Recibido: 21/08/2026». Lo mismo en la etiqueta que se imprime sobre el
  producto, donde `FAE-20260822-001` convive con «Faenado: 21/08/2026». Afecta
  solo a los registros de después de las 19:00 hora local, y solo a quien
  compare el código con la fecha. Se acepta a sabiendas; corregirlo es un
  proyecto propio, con su migración y su decisión sobre los códigos ya
  emitidos.

### Límite honesto de la verificación

Las tres funciones quedan fijadas por unidad, incluido el caso de cruce de día
(`2026-08-22T02:00Z` → `21/08/2026 21:00`) y el nulo. Pero **ninguna prueba
puede demostrar que se cambiaron todos los sitios de llamada**: si mañana
alguien añade un `{fecha:HH:mm}` nuevo, el fallo vuelve y nada se pone rojo.
Se deja escrito en lugar de fingir cobertura.

---

## A2 · El desglose del descuento en el ticket

### El hueco real

`DescuentoPago.Descripcion` **ya existe y ya es obligatoria**: `PagoService`
rechaza la vacía antes de escribir la fila. El punto 9 del pedido no es que el
dato no se capture — es que **nunca llega al papel**.

El ticket imprime siempre `pago.MontoUsd`, el monto original, y no menciona los
descuentos. Una productora con un ticket reimpreso lee «USD 120,00» y
«PAGADO», cuando lo que le llegó fueron 103. El documento que existe para
darle constancia le está dando una cifra falsa.

La productora **no tiene cuenta en el sistema**: el papel es el único canal.

### Diseño

`TicketPagoService.GenerarAsync` amplía su consulta con
`.Include(p => p.Descuentos).ThenInclude(d => d.NovedadCat).ThenInclude(n => n.CuyRegistro)`.

Nuevo `Features/Pagos/Services/TextosTicket.cs`, pura y estática, espejo de
`TextosGuia`:

| Función | Responsabilidad |
|---|---|
| `LineaNovedad(DescuentoPago)` | `"Cuy #3 · Oreja dura"` |
| `EtiquetaTipo(TipoNovedad)` | `BajoPeso` → `"Bajo peso"` |
| `MontoDestacado(Pago)` | `MontoPagadoUsd ?? MontoUsd` |
| `HayDesglose(Pago)` | si el bloque de descuentos se imprime |

`EtiquetaTipo` es necesaria porque **el API no tiene ningún mapa de etiquetas
legibles para `TipoNovedad`**; el único que existe vive en el front, en
`AnilloNovedades.tsx`. Debe cubrir todos los valores del enum, incluido el
`ColorNoConforme` histórico que ya no se genera.

### El papel resultante

Sobre 80 mm, entre el bloque LOTE y el número grande:

```
Subtotal              USD 120,00

DESCUENTOS
Cuy #3 · Oreja dura
  Oreja calcificada, canal fuera
  de norma
                      -USD 8,00
Cuy #7 · Bajo peso
  1.080 g al faenar
                      -USD 9,00
------------------------------
       USD 103,00
   PAGADO — POR VERIFICAR
```

El número grande pasa de `MontoUsd` a `MontoDestacado(pago)`.

**Con el ticket pendiente no hay descuentos, `HayDesglose` es falso, el bloque
no se imprime y `MontoDestacado` devuelve `MontoUsd`: el papel sale idéntico al
de hoy.** El cambio solo se nota donde hoy miente.

### Dos detalles que no se pueden perder

- **La descripción se envuelve, no se trunca.** Es texto libre que escribe la
  planta. Truncar un motivo de descuento a mitad de frase es peor que no
  imprimirlo: deja a la productora con media explicación y sin forma de
  reclamar. QuestPDF envuelve por defecto dentro de un `Text`; lo que hay que
  evitar es introducir un truncado «para que quepa».
- **`Novedad.CuyRegistro` es anulable en el modelo, pero no en la práctica.**
  `PagoService` **rechaza con 409** todo descuento cuya novedad no tenga animal
  asociado —lo fija `DescuentoTrazableTests.UnaNovedadSinCuyAsociadoSeRechaza`—,
  así que ningún `DescuentoPago` escrito por la vía actual puede apuntar a una
  novedad de entrega. Aun así `LineaNovedad` resuelve el nulo sin imprimir
  `Cuy #`: el tipo lo admite, y una función que revienta con un nulo legal es
  una excepción no controlada al pulsar «Imprimir». Se fija por unidad como
  defensa, no como caso de uso.

### El `Include` necesita su propia prueba

Las unitarias de `TextosTicket` construyen los objetos en memoria con la
navegación ya poblada, así que **pasarían igual aunque el `Include` estuviera
mal escrito o faltara**. Y lo que ocurre en ese caso no es un texto feo: es un
`NullReferenceException` al componer el PDF, es decir un 500 al pulsar
«Imprimir», y justo en el ticket que sí lleva descuentos.

Por eso A2 necesita **también** una prueba de integración: emitir un pago,
pagarlo con al menos un descuento cuya novedad esté ligada a un animal, y
descargar el ticket comprobando que responde 200 con un PDF no vacío. No afirma
nada sobre el contenido —no se puede— pero sí que la consulta trae todo lo que
el maquetado va a tocar.

---

## A3 · Comunidad libre en el alta de productora

### Esto invierte una regla deliberada

No es un descuido: `ProductorasController` **rechaza a propósito** las
comunidades de otro CAT, con un comentario que defiende la regla («sin esta
comprobación, el operador de PAT registraría productoras de Las Nieves con el
sello PAT, ensuciando el catálogo de otro centro»).

El criterio nuevo es que **la comunidad es dónde vive y el CAT es dónde
entrega**, y no tienen por qué coincidir. `Comunidad.CatReferencia` deja de ser
una restricción y pasa a ser un dato informativo.

### Diseño

**Servidor**

- Se retiran las dos guardas `User.ComunidadFueraDeAlcance(...)` de `Crear`
  (línea ~70) y `Actualizar` (línea ~115).
- El sellado del CAT con el token —ya presente en las líneas 62-64— es lo único
  que queda, y es lo único que hace falta: el operador sigue sin poder elegir
  el centro de la productora que registra.
- `ComunidadFueraDeAlcance` (en `Common/Auth/AlcanceUsuario.cs`) y
  `IProductoraService.CatDeComunidadAsync` quedan sin uso. Se eliminan, no se
  dejan muertas.

**Front (`FormProductora.tsx`)**

- `comunidadesVisibles` deja de filtrar por `catFijo`: se listan todas las
  comunidades activas del catálogo.
- `elegirComunidad` deja de escribir `catAsignado: c?.catReferencia` cuando el
  CAT está fijado por el token. Con `catFijo` presente, el CAT no se toca.
- El cantón sigue derivándose de la comunidad elegida y sigue siendo de solo
  lectura. Es exactamente lo pedido: se bloquea el cantón y el CAT, se libera
  la comunidad.
- Se retira el mensaje «Tu centro de acopio no tiene comunidades en el
  catálogo», que deja de tener sentido.

### Una prueba existente se vuelve falsa

`AlcanceProductorasTests.OperadorCat_noCreaProductoraEnComunidadDeOtroCentro`
afirma hoy justo lo contrario del criterio nuevo.

**No se borra: se reescribe.** La versión nueva afirma que el alta funciona y
que la productora queda sellada con el CAT del token, no con el de la
comunidad. Una prueba borrada deja un hueco silencioso; una reescrita deja
constancia de que el criterio cambió a propósito.

Las demás pruebas de ese archivo —que el operador no elige el centro, que no
mueve una productora a otro centro, que no edita las de otro centro— siguen
válidas y **deben seguir en verde**: son el alcance que sí se conserva.

---

## A4 · El QR público adelgaza

`ObservacionesProceso` y `DetalleCuyes` salen del `TrazabilidadPublicaDto`, de
`QRService` y de las dos tarjetas de `QRPublico.tsx`. `CuyPublicoDto` y el tipo
equivalente del front se eliminan si no queda otro consumidor.

### La trampa

`QRService.cs:266` calcula `conNovedad` **a partir de** `detalleCuyes`:

```csharp
var conNovedad = detalleCuyes.Any(c => c.Estado != "Apto");
```

La variable local se queda y el cálculo se conserva intacto. Lo que desaparece
es la **exposición** en el DTO, no el cómputo. Borrar la variable rompería el
indicador de novedad de la página pública sin que se note al leer el diff.

### El único punto con garantía real

Esto es JSON, no PDF. Una prueba de integración afirma que la respuesta del
endpoint público ya no trae datos por animal ni observaciones de proceso, y esa
prueba **sí se pone roja** si alguien lo revierte. Es el único de los seis
puntos con esa propiedad, y conviene aprovecharla: la aserción va sobre el
cuerpo de la respuesta, no sobre el tipo de C#.

---

## A5 · Verificación — pagos solo del CAT propio (Feature 4)

**No hay nada que construir.** `IPagoService` ya recibe un `CentroAcopio? filtroCat`
en registro, listado, lotes pendientes, comprobante y verificación, y
`PagosController.FiltroCat()` lo saca de `User.CatRestringido()`. El contrato
está documentado en la propia interfaz.

Lo que falta son las pruebas que lo fijen. Hoy la única que toca el tema es
`TicketPagoTests.UnOperadorDeOtroCentroRecibe404`, que cubre una esquina.

Se añaden:

- Un operador de PAT **no ve** los pagos de una productora de NIE en
  `GET /api/pagos`.
- Un operador de PAT **no puede registrar** un pago a una productora de NIE.
- `ListarLotesPendientes` viene acotado al centro del token.

El entregable de este punto es la evidencia, no el código.

---

## A6 · Verificación — cédula offline con el nombre mal escrito (Extra 2)

**Tampoco hay nada que construir, y la garantía es más fuerte de lo que parecía.**

`RecepcionService.ResolverProductoraPorCedulaAsync` busca por
`p.Cedula == cedula && p.CatAsignado == dto.CentroAcopio && p.Activa`. Pero el
motivo de fondo no es que el nombre quede fuera del `Where`: es que
**`RegistrarEntregaDto` no tiene ningún campo de nombre**. Solo
`CedulaProductora`. El nombre que la operadora vea o escriba en la tablet **no
viaja al servidor por ninguna vía**, así que estructuralmente no puede desviar
una entrega. El front lo confirma: `FormLote.tsx` en modo sin señal solo captura
`cedulaManual`.

Pero **no existe ni una sola prueba de esto**. Se añaden dos:

1. Una entrega sincronizada solo con la cédula se asigna a la productora
   correcta y no cae en cuarentena.
2. Una cédula válida que pertenece a una productora de **otro** CAT: cae en la
   bandeja de vinculación en vez de asignarse. El comportamiento es correcto —el
   lote es de un centro y la productora de otro—, pero ahora mismo no está
   escrito en ningún lado. La prueba lo fija, y su mutación (quitar
   `p.CatAsignado == dto.CentroAcopio` del `Where`) expone una fuga entre
   centros.

---

## Plan de pruebas

| Punto | Tipo | Qué fija |
|---|---|---|
| A1 | Unitaria | `ALocal`, `FechaHoraLocal`, `FechaLocal`: desfase, cruce de día, nulo, `Kind` no especificado |
| A2 | Unitaria | `LineaNovedad` con y sin animal, `EtiquetaTipo` para todo el enum, `MontoDestacado` en los tres estados, `HayDesglose` |
| A2 | Integración | Un ticket con descuentos se descarga 200 y no vacío — cubre el `Include`, que las unitarias no pueden ver |
| A3 | Integración | `AlcanceProductorasTests` reescrita + las existentes en verde |
| A4 | Integración | El cuerpo público no trae datos por animal ni observaciones; `conNovedad` sigue correcto |
| A5 | Integración | Tres aserciones de alcance por CAT sobre pagos |
| A6 | Integración | Dos aserciones sobre la resolución por cédula |

Las 211 pruebas actuales siguen en verde, **salvo la de `AlcanceProductorasTests`
que se reescribe a propósito**. Se suman del orden de doce.

Cada guarda nueva se comprueba por mutación: quitarla, ver la prueba en rojo,
restaurarla. Es lo que en el ciclo de pago detectó tres pruebas huecas que
pasaban con el fallo presente.

Las pruebas corren solo dentro de Docker
(`docker compose -f docker-compose.tests.yml run --rm tests`) porque Smart App
Control bloquea la carga del DLL desde OneDrive.

## Riesgos y lo que este proyecto no cubre

- **A1 y A2 acaban en papel.** Se pueden fijar las funciones; no la
  maquetación. El ticket con desglose y la guía con la hora corregida
  **necesitan que alguien los imprima y los mire** antes de dar el proyecto por
  cerrado. Es una verificación manual, y es obligatoria.
- **A1 no puede garantizar que no falte un sitio de llamada.** Ver el límite
  honesto de A1.
- **Los dos códigos —de jaula y de lote faenado— siguen usando la fecha UTC**, y
  esta rama volvió la contradicción visible en el papel. Decisión tomada y
  documentada arriba: son identificadores, no fechas.
- **Un truncado reintroducido sobre `descuento.Descripcion` no lo detectaría
  ninguna prueba**, porque el texto de un PDF no es afirmable. Se descartó a
  propósito meterlo en una función pura: el truncado se reintroduciría en el
  sitio de la maquetación, donde esa función no lo vería, así que solo daría
  falsa seguridad. La mitigación real es el comentario puesto justo donde
  alguien lo rompería.
- **Olvidar `.ThenInclude(n => n.CuyRegistro)` en el ticket** haría perder el
  «Cuy #3 · » de cada línea sin que nada avise. Se acepta: la degradación es
  cosmética y el motivo y el monto del descuento siguen imprimiéndose. Su
  hermano mayor —perder el `Include(p => p.Descuentos)` entero— sí quedó
  cubierto, comparando la longitud del PDF antes y después de pagar.
- **El front no tiene Vitest ni Playwright.** El cambio de `FormProductora` y
  el de `QRPublico` se verifican a mano, además de `pnpm lint`,
  `pnpm exec tsc -b` y `pnpm build`.

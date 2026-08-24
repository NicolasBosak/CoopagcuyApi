# Trazabilidad del transporte — diseño

**Fecha:** 2026-08-23
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

Este es el **Proyecto D** de la descomposición en cinco del pedido original.
Cubre la feature 5: que las condiciones de transporte **no verificadas** dejen
rastro, que la pregunta de llegada sea obligatoria cuando el checklist salió
incompleto, y que una llegada en mal estado abra un cuestionario.

Es **independiente** de los proyectos A, B, C y E.

## El problema, en una frase

Hoy el operador del CAT marca un checklist de siete condiciones antes de enviar
la jaula, y **si deja alguna sin marcar eso no se refleja en ningún lado**. La
guía imprime lo que sí se verificó; lo que faltó desaparece.

## El hallazgo que gobierna el diseño

`Movilizacion.CondicionesTransporte` es un `string` que guarda **una frase ya
compuesta** con las condiciones marcadas:

```csharp
CondicionesTransporte = CondicionTransporte.Describir(dto.CondicionesTransporte)
// -> "Jaulas limpias y desinfectadas, Ventilación adecuada"
```

Las **claves se pierden en la escritura**. Reconstruirlas parseando esa frase
sería frágil —se rompe en cuanto alguien corrija una tilde de una etiqueta— y el
propio catálogo advierte de que las etiquetas cambian mientras las claves no
(`"Maximo20"` sigue llamándose así aunque el tope sea 15).

**Se añade una columna con las claves marcadas.** Las que faltaron se derivan del
catálogo por diferencia. La columna de la frase **se conserva intacta**: es lo
único que tienen las movilizaciones anteriores a este cambio, y reimprimir una
guía antigua no puede perder ese dato.

Para una movilización histórica, la lista de claves es nula —no vacía— y eso se
distingue: **«no se registró» no es lo mismo que «no se verificó ninguna»**, y la
guía no puede decir lo segundo cuando lo cierto es lo primero.

## Las tres piezas

### 1 · La guía imprime lo que no se verificó

Junto a la línea de condiciones aparece, cuando falta alguna, un bloque que las
nombra. Con las siete marcadas, **la guía sale idéntica a la de hoy** — es la
garantía de no regresión.

El texto de cada renglón se compone en una función pura de `TextosGuia`, porque
**del binario del PDF no se puede afirmar nada**: QuestPDF comprime los flujos
de texto. Es el patrón que ese archivo ya documenta y que los proyectos A y B
siguieron.

### 2 · La pregunta de llegada se vuelve obligatoria

Cuando el operador de la planta confirma la recepción, se le pregunta **siempre**
si los animales llegaron en buen estado. La respuesta pasa a ser un booleano,
`LlegaronEnBuenEstado`, y es **obligatoria cuando el checklist de transporte
salió incompleto**.

Anulable en el esquema por las movilizaciones anteriores, obligatoria en el
servicio para ese caso. Mismo criterio que ya se aplicó a
`SinAntibioticos7Dias`.

### 3 · Un «no» abre un cuestionario de catálogo cerrado

Las condiciones de llegada pasan a ser **una lista de claves de un catálogo
fijo**, más una observación libre aparte.

Nuevo `CondicionLlegadaCatalogo`, hermano de `CondicionTransporte` y con su misma
mecánica: **el servidor solo acepta claves que reconoce y rechaza cualquier otra
con 400**, y el texto que se guarda e imprime lo pone él, no el operador. Es lo
que impide que cada planta escriba lo suyo y que después no se pueda contar nada.

El catálogo de partida, con las condiciones que un operador puede constatar al
abrir la jaula:

| Clave | Etiqueta |
|---|---|
| `AnimalesGolpeados` | Animales con golpes o heridas |
| `AnimalesDeshidratados` | Animales deshidratados o decaídos |
| `AnimalesMuertos` | Animales muertos |
| `JaulasSucias` | Jaulas sucias o con excretas |
| `Hacinamiento` | Hacinamiento en la jaula |
| `JaulasDanadas` | Jaulas rotas o mal aseguradas |
| `Otro` | Otra condición (ver observación) |

Es una lista de trabajo: si el dueño del producto quiere añadir o quitar alguna
al implementar, es un cambio de una línea en el catálogo. Lo que **no** cambia es
que sea cerrada.

**`CondicionLlegada` no se reescribe: se añade una columna nueva para las
claves.** La actual guarda texto libre de las recepciones ya confirmadas, y
reinterpretar esa columna perdería ese dato o lo haría ilegible. Es exactamente
el mismo trato que reciben las condiciones de transporte, y por el mismo motivo.

La observación libre se conserva: el catálogo dice *qué* pasó, y el texto dice
*qué vio*.

## El envío no se bloquea

Un checklist incompleto **registra el envío igual** y deja constancia.

Bloquearlo tendría el efecto contrario al que se busca: empujaría a marcar las
siete casillas sin mirarlas, que es exactamente el problema que este checklist
vino a resolver cuando sustituyó al texto libre. Y un camión que sale sin poder
registrarse deja un hueco de trazabilidad peor que uno que sale con constancia
de lo que faltó.

Es una decisión distinta de la que gobierna `SinAntibioticos7Dias`, que **sí**
bloquea. La diferencia: la declaración de antibióticos es una afirmación
sanitaria que da valor probatorio a la guía, y sin ella el documento afirma un
transporte del que nadie responde. Un checklist incompleto no invalida el
documento: lo matiza.

## La pantalla

**En Recepción**, el formulario de movilización ya muestra «N de 7 verificadas».
Cuando N es menor que 7, se avisa de que la falta quedará registrada en la guía
— para que sea una decisión y no un descuido.

**En Faenamiento**, la confirmación de llegada gana la pregunta de buen estado.
Si el checklist venía incompleto, no se puede confirmar sin responderla. Con un
«no», se despliegan las casillas del catálogo y el campo de observación.

**Objetivos táctiles de 44 px**: tablets de 7 pulgadas en campo y con guantes. La
convención del repositorio es `min-h-[44px]`; **`min-h-12` no existe en este
Tailwind y no aplicaría nada.**

## Plan de pruebas

| Qué | Tipo | Qué fija |
|---|---|---|
| Claves guardadas | Integración | Las claves marcadas se persisten y se recuperan |
| Derivación | Unitaria | Las no verificadas salen del catálogo por diferencia, y una lista nula no es una lista vacía |
| Texto de la guía | Unitaria | El renglón de lo no verificado, como función pura |
| Guía con faltas | Integración | La guía de un lote con checklist incompleto se descarga y no revienta. **El contenido no se afirma**: ver los límites |
| Obligatoriedad | Integración | Con checklist incompleto, confirmar sin responder da **409**; con checklist completo, se admite |
| Movilización histórica | Integración | Con las claves sin registrar, confirmar sin responder **sí** se admite |
| Catálogo cerrado | Integración | Una clave de llegada desconocida se rechaza con 400 |
| Cuestionario | Integración | Un «no» sin ninguna condición marcada se rechaza |

Cada guarda nueva se comprueba **por mutación**: romperla, verla en rojo,
restaurarla. En los proyectos A y B ese paso encontró trece problemas, siete de
ellos suposiciones falsas del propio plan.

Las pruebas corren solo dentro de Docker
(`docker compose -f docker-compose.tests.yml run --rm tests`) porque Smart App
Control bloquea la carga del DLL desde OneDrive.

## Riesgos y límites

- **La guía acaba en papel.** Se pueden fijar las funciones puras y el
  crecimiento del documento, no la maquetación. Necesita que alguien imprima una
  guía con faltas y otra sin ellas y las mire.
- **Comparar longitudes de PDF NO sirve a esta escala.** Medido durante la
  implementación: el subconjunto de fuentes embebido introduce hasta **~238
  bytes de variación entre dos guías equivalentes**, mientras que el bloque
  nuevo mide ~100. La señal queda por debajo del ruido. La técnica funcionó en
  proyectos anteriores porque allí el bloque medía ~2600 bytes y lo aplastaba;
  aquí no. **Se retiraron las dos pruebas que lo intentaban**, y la aparición
  del bloque en el papel pasa a ser verificación manual.

  La garantía de no regresión no se pierde: se sostiene **por construcción**,
  porque `LineaNoVerificadas` devuelve nulo en los dos casos en que no hay nada
  que imprimir, y eso lo fijan las unitarias de forma determinista.
- **El front no tiene Vitest ni Playwright.** Las dos pantallas se verifican a
  mano, además de `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`.

## Fuera de alcance, a propósito

- **Contar cuántos animales afectó cada condición de llegada.** Más preciso para
  reclamar al transportista, pero más trabajo en una tablet en campo; se decidió
  el catálogo simple.
- **Bloquear el envío por condiciones críticas.** Requiere decidir cuáles lo son,
  y esa es una decisión sanitaria que el diseño no puede inventar.
- **Reclamar al transportista.** El sistema deja constancia; el proceso de
  reclamación no está modelado.

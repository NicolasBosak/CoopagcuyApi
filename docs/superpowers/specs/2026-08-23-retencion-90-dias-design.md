# Retención a 90 días — diseño

**Fecha:** 2026-08-23
**Estado:** aprobado
**Repositorios:** `CoopagcuyApi`, `CoopagcuyFront`

## Contexto

Este es el **Proyecto E** de la descomposición en cinco del pedido original.
Cubre el extra 3: que las pantallas operativas muestren solo los últimos 90 días,
para mantenerlas limpias, **sin que se pierda nada**.

Es **independiente** de los proyectos A, B, C y D.

## Nada se borra

El límite es de **visualización**. La base conserva todo, porque los reportes lo
necesitan y porque es el registro de trazabilidad del que depende el sistema
entero.

Esto no es una política de ciclo de vida como la de los blobs de evidencias o
comprobantes: ahí sí se borran archivos, aquí no se borra ninguna fila.

## Va en el servidor, no en la pantalla

Filtrar en el cliente traería igual todas las filas por la red. Estas tablets van
por datos móviles en el campo: el motivo declarado de la feature es que las
pantallas estén limpias, pero **el beneficio real es que carguen menos**, y ese
solo se obtiene filtrando en el servidor.

Los listados afectados ya tienen topes de tamaño (`Take(300)` en varios), que se
conservan: el corte por fecha y el tope por cantidad resuelven cosas distintas.

## La distinción que cambia el alcance

No todas las pestañas que enumeró la petición son historial. **Dos son colas de
trabajo pendiente**, y aplicarles el corte tendría una consecuencia grave.

| Listado | Qué es | ¿Corte de 90 días? |
|---|---|---|
| Recepción → Sincronizados (lotes) | Historial | **Sí** |
| Recepción → Sin sincronizar | Local del dispositivo | No aplica: vive en IndexedDB |
| Recepción → Pagos | Historial | **Sí** |
| Faenamiento → Faenamientos | Historial | **Sí** |
| Faenamiento → Llegadas de CAT | Historial de movilizaciones | **Sí**, salvo las pendientes de recepción |
| Faenamiento → Devoluciones | Historial | **Sí** |
| Faenamiento → Pagos (por pagar) | **Cola de trabajo** | **No** |
| Despacho | Historial | **Sí** |
| Vinculaciones | **Cola de trabajo** | **No** |

**Por qué las colas quedan fuera.** «Pagos» en Faenamiento son los tickets que la
planta todavía **no ha pagado**. Vinculaciones son las entregas capturadas sin
conexión que esperan que un administrador las asigne a una productora. En los dos
casos, esconder lo que tiene más de 90 días significa que **un ticket sin pagar
de hace cien días desaparece para siempre en vez de cobrarse**, y que una entrega
en cuarentena se queda ahí sin que nadie la vuelva a ver. El trabajo pendiente no
envejece: se resuelve.

Lo mismo con las movilizaciones **pendientes de recepción**: un camión que salió
hace 91 días y cuya llegada nadie confirmó es un problema abierto, no historial.

Regla, en una frase: **el corte se aplica a lo que ya terminó, nunca a lo que
está esperando a alguien.**

## El corte usa el día local del piloto

«Hace 90 días» se calcula sobre el día local, reutilizando
`FechaUtc.InicioDelDiaLocal`. Restar 90 días al instante UTC desplazaría la
frontera cinco horas y dejaría fuera —o dentro— los registros de la tarde del
día límite. Es el mismo cuidado que ese archivo ya documenta para los filtros de
reportes, y que corrigió un fallo real: un despacho registrado a las 20:00 no
aparecía en el reporte de su propio día.

## La escotilla: un filtro de fechas

Por defecto, 90 días. Cada pantalla afectada ofrece **elegir un rango mayor**
cuando haga falta. El dato nunca se esconde del todo: deja de estorbar.

El filtro reutiliza el componente `FiltrosPeriodo` que ya gobierna los reportes,
para no inventar un segundo selector de fechas en el mismo sistema.

**Los reportes no llevan este límite.** Ahí el rango lo elige quien consulta, y
ese es justamente el sitio donde se va a mirar lo antiguo.

## Qué fecha manda en cada listado

Cada entidad se corta por la fecha que representa **cuándo ocurrió el hecho**, no
cuándo se sincronizó:

| Listado | Fecha |
|---|---|
| Lotes | `FechaRecepcion` |
| Pagos | `FechaPago` |
| Movilizaciones | `FechaDespacho` |
| Faenamientos | `FechaFaenamiento` |
| Devoluciones | `FechaDevolucion` |
| Despachos | `FechaDespacho` |

Importa por el sync offline: una entrega capturada hace 100 días y sincronizada
ayer **es antigua**, y cortarla por la fecha de sincronización la haría aparecer
como reciente.

## Plan de pruebas

| Qué | Tipo | Qué fija |
|---|---|---|
| Corte por defecto | Integración | Un registro de hace 91 días no aparece; uno de hace 89, sí |
| Frontera local | Integración | Un registro de las 20:00 del día 90 cae del lado correcto |
| Escotilla | Integración | Con un rango explícito mayor, el registro antiguo **sí** aparece |
| Colas intactas | Integración | Un ticket por pagar y una vinculación de hace 200 días **siguen apareciendo** |
| Movilización pendiente | Integración | Una salida sin recepción confirmada de hace 200 días sigue visible |
| Reportes intactos | Integración | Un reporte sobre un rango antiguo devuelve los mismos datos que antes |

La prueba de las colas es la más importante de esta lista: es la que impide que
una «limpieza» esconda trabajo que nadie ha hecho.

Cada guarda se comprueba **por mutación**. En los proyectos A y B ese paso
encontró trece problemas.

Las pruebas corren solo dentro de Docker
(`docker compose -f docker-compose.tests.yml run --rm tests`) porque Smart App
Control bloquea la carga del DLL desde OneDrive.

## Riesgos y límites

- **Es un cambio transversal a siete listados.** El riesgo no está en ninguno por
  separado, sino en aplicar el corte donde no toca. La tabla de arriba es
  normativa: si al implementar aparece un listado que no está en ella, **se
  pregunta antes de decidir**.
- **Una prueba que siembre «hace 91 días» depende de la fecha de ejecución.** Hay
  que sembrar por diferencia contra `DateTime.UtcNow`, nunca con una fecha fija,
  o la batería empezará a fallar sola dentro de tres meses.
- **El front no tiene Vitest ni Playwright.** El filtro de fechas se verifica a
  mano, además de `pnpm lint`, `pnpm exec tsc -b` y `pnpm build`.

## Fuera de alcance, a propósito

- **Borrar datos.** Nada se elimina de la base.
- **Archivar a una tabla fría.** El volumen del piloto no lo justifica, y añadiría
  un camino de lectura nuevo para cada consulta.
- **Cambiar el límite por pantalla o por usuario.** 90 días para todas; si alguna
  necesita otro valor, se verá con el uso real.
- **Aplicar el corte a los reportes.** Son justamente el sitio donde se consulta
  lo antiguo.

# Hallazgo — el reporte de Salida no muestra los despachos nuevos

**Fecha:** 2026-08-18
**Tarea:** Fase 0 del plan [2026-08-18-permisos-sesiones-pdfs.md](2026-08-18-permisos-sesiones-pdfs.md)
**Estado:** el código queda descartado. Falta una consulta a la base del entorno real.

## Síntoma reportado

En Reportes → Salida, el despacho más reciente que aparece es del **04/08/2026**.
Se registraron dos despachos el **18/08/2026** y no aparecen ni en la pantalla web
ni en la exportación a Excel. Los dos **sí** aparecen en la pantalla Despacho.

## Qué se comprobó

### 1. La consulta del reporte es correcta

`tests/CoopagcuyApi.Tests/Integracion/ReporteSalidaTests.cs` inserta despachos
**directamente en la tabla**, sin pasar por `RegistrarDespachoAsync`, y consulta
`GET /api/reportes/salida` con el mismo rango que usa el front por defecto
(primer día del mes → hoy).

| Prueba | Resultado |
|---|---|
| Un despacho de hoy aparece en el rango del mes en curso | **Pasa** |
| Un despacho de hoy aparece cuando el rango es solo el día de hoy | **Pasa** |
| Un despacho de hace 90 días no aparece en el rango del mes | **Pasa** |

La tercera es el control negativo: descarta que el filtro esté sencillamente
inactivo y las otras dos pasen por casualidad.

**Conclusión:** `ReporteSalidaAsync` encuentra lo que hay dentro del rango, y el
límite superior exclusivo de `RangoUtc` cubre bien el día en curso. La sospecha
inicial sobre los `Include` encadenados sin `AsSplitQuery()` queda descartada.

### 2. La fecha entrante no se desplaza

`tests/CoopagcuyApi.Tests/Unitarias/FechaDespachoEntranteTests.cs` deserializa
el cuerpo JSON contra el DTO real (`RegistrarDespachoDto`) con las mismas
opciones que aplica ASP.NET Core, y le pasa el resultado por
`FechaUtc.Normalizar`.

| Formato enviado | Resultado |
|---|---|
| `2026-08-18T11:09:00.000Z` — el que manda el front | **Pasa**, queda 11:09 UTC |
| `2026-08-18T06:09:00.000-05:00` — con desfase de Ecuador | **Pasa**, queda 11:09 UTC |
| `2026-08-18T11:09:00` — sin zona horaria | **Pasa**, queda 11:09 UTC |

**Conclusión:** la fecha sobrevive intacta el viaje desde el cuerpo de la
petición hasta el valor que se guarda. No hay desplazamiento por zona horaria
ni por `DateTimeKind`.

## Un tropiezo que conviene anotar

La primera corrida dio 2 fallos de 3, y parecía la confirmación del defecto. No
lo era: el registro de la prueba nombraba los campos `ClienteDestino`,
`CodigoLote`, `Destino` y `CantidadUnidades`, mientras que `ReporteSalidaDto`
los llama `Cliente`, `CodigoLoteFaenado`, `Ubicacion` y `Unidades`.
System.Text.Json empareja **por nombre**, así que `ClienteDestino` se
deserializaba como `null` en silencio y la aserción fallaba con el sistema sano.

Vale la pena recordarlo para las pruebas que vengan: un registro de prueba con
nombres inventados no da error de compilación ni de deserialización, solo
resultados falsos.

## Qué queda por descartar

El código está limpio en las dos mitades del recorrido, así que lo que queda son
explicaciones sobre los datos y el entorno. En orden de probabilidad:

1. **La fecha guardada está fuera del rango.** El formulario de despacho
   (`FormDespacho.tsx`) trae un campo de fecha y hora **editable**, precargado
   con el momento actual del dispositivo. Si el reloj de la tablet está
   desajustado, o si quien registró el despacho tocó ese campo, la fila queda
   con una fecha que el rango del mes no cubre. Es la explicación que mejor
   encaja con «aparece en Despacho pero no en Salida»: la pantalla Despacho
   consulta **sin filtro de fecha**, así que muestra la fila sea cual sea su
   fecha.
2. **La API desplegada es anterior a este código.** Explicaría cualquier
   diferencia entre lo que se lee aquí y lo que hace el servidor.
3. **El front y la API apuntan a bases distintas.** Menos probable, porque
   ambas pantallas usan el mismo cliente HTTP.

## Lo que hace falta para cerrarlo

Una consulta contra la base del entorno donde se registraron los despachos:

```sql
SELECT "Id", "FechaDespacho", "ClienteDestino", "LoteFaenadoId", "LoteId"
FROM "Despachos"
ORDER BY "Id" DESC
LIMIT 10;
```

Con eso se resuelve en un vistazo:

- **Si `FechaDespacho` de las dos filas nuevas cae fuera de agosto de 2026** →
  hipótesis 1 confirmada. El arreglo no es de consulta sino de captura: sellar
  la fecha en el servidor, o avisar en el formulario cuando la fecha elegida se
  aleja del momento actual.
- **Si cae dentro del rango y aun así el reporte no la devuelve** → hipótesis 2
  o 3. Toca comparar la versión desplegada y la cadena de conexión.
- **Si las dos filas no existen** → el registro no está confirmando la
  transacción pese a responder con éxito, y hay que mirar `RegistrarDespachoAsync`.

Hasta tener ese dato, escribir un arreglo sería adivinar.

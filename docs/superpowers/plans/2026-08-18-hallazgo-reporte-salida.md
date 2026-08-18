# Hallazgo — fechas: el reporte de Salida y las horas de sesión

**Fecha:** 2026-08-18
**Estado:** resuelto. Dos fallos distintos con la misma familia de causa.

## Fallo 1 — las últimas cinco horas de cada día no salían en ningún reporte

### Síntoma

En Reportes → Salida no aparecían los despachos nuevos, ni en la web ni en el
Excel. Sí aparecían en la pantalla Despacho.

### Causa

`ReportesService.RangoUtc` tomaba las fechas del filtro —que el usuario elige
pensando en días **locales**— y las trataba como días **UTC**:

```csharp
var desde = DateTime.SpecifyKind(filtro.Desde.Date, DateTimeKind.Utc);
var hasta = DateTime.SpecifyKind(filtro.Hasta.Date.AddDays(1), DateTimeKind.Utc);
```

Ecuador es UTC-5, así que el día local termina a las **05:00 UTC del día
siguiente**, no a las 00:00. El rango se cerraba cinco horas antes de tiempo y
se llevaba por delante todo lo registrado entre las **19:00 y la medianoche**
hora local.

Un despacho de las 20:00 en el CAT se guarda como las 01:00 UTC del día
siguiente. El reporte "hasta hoy" lo excluía, aunque la fila estuviera
perfectamente guardada — por eso sí se veía en la pantalla Despacho, que
consulta sin filtro de fechas.

Afectaba a **todos** los reportes, no solo a Salida.

### Por qué la pantalla Despacho sí los mostraba

`ListarDespachosAsync` no filtra por fecha salvo que se le pidan parámetros, y
el front no le pasa ninguno. Esa asimetría es la que hacía parecer que el dato
se perdía.

### Arreglo

`FechaUtc.InicioDelDiaLocal` traduce un día local del piloto al instante UTC en
que empieza, y `RangoUtc` lo usa en los dos extremos. El desfase es una
constante fija (`FechaUtc.DesfasePiloto` = -5): Ecuador no aplica horario de
verano, así que es exacto todo el año.

En el frontend, `inicioMes()`, `hoy()` y los rangos rápidos usaban
`toISOString().slice(0, 10)`, que convierte a UTC antes de recortar y por tanto
devolvía el día siguiente a partir de las 19:00. Ahora usan `fechaLocal()`.

### Consecuencia a tener presente

El desfase está fijado a Ecuador continental. Si alguien consulta reportes desde
un equipo en otra zona horaria, el selector le mostrará sus días locales pero el
servidor los interpretará como días de Ecuador. Para el piloto es lo correcto
—la cooperativa opera en Azuay— pero deja de serlo si el sistema se usa desde
otro huso.

### Cobertura

- `DespachoExtremoAExtremoTests` registra un despacho **por el endpoint real** a
  las 19:30 hora de Ecuador y lo busca en el reporte del día local. Falla sin el
  arreglo, pasa con él.
- `ReporteSalidaTests` cubre la consulta con filas insertadas a mano, incluido
  un control negativo.
- `FechaDespachoEntranteTests` cubre la deserialización de la fecha entrante en
  los tres formatos que puede mandar un cliente.

## Fallo 2 — las horas se mostraban cinco horas en el futuro

### Síntoma

La pantalla de sesiones mostraba "Último uso: 9:51 p. m." con el reloj del
equipo marcando las 4:56 p. m.

### Causa

Una cadena de tres piezas, ninguna de ellas un error por sí sola:

1. `Program.cs` activa `Npgsql.EnableLegacyTimestampBehavior`, así que las
   columnas `timestamp without time zone` devuelven sus `DateTime` con
   `Kind=Unspecified`.
2. System.Text.Json serializa un `Unspecified` **sin la `Z` final**:
   `"2026-08-18T21:51:00"` en vez de `"2026-08-18T21:51:00Z"`.
3. En el navegador, `new Date("2026-08-18T21:51:00")` interpreta una fecha sin
   zona como hora **local**, y pinta 21:51 tal cual.

Era UTC mostrado como si fuera hora local. Afectaba a todas las fechas del
sistema; se notó en las sesiones porque es donde la hora exacta se mira con
lupa.

### Arreglo

`FechaUtcJsonConverter` (y su versión nullable) serializan todo `DateTime` como
instante UTC explícito. Se registran en `Program.cs` junto al converter de
enums.

La versión nullable hace falta aparte: System.Text.Json **no** deriva el
converter de `DateTime?` del de `DateTime`, y sin ella las fechas opcionales
—`fechaRevocacion`, `fechaRecepcionPlanta`— habrían seguido saliendo mal
mientras las obligatorias ya salían bien.

### Cobertura

`FechasSerializadasTests` comprueba sobre el **JSON crudo** que las fechas
terminan en `Z`. Deserializar a `DateTime` habría ocultado el fallo: .NET
rellena la zona que falta y la prueba pasaría con el navegador leyendo mal.

## Nota sobre el formato de calendario de EEUU

No influye. El frontend envía y recibe fechas en ISO-8601, que es independiente
del formato de presentación del sistema operativo. Lo que sí importa es el
**desfase horario**, y el del equipo desde el que se reportó el fallo es UTC-5,
el mismo que Ecuador: no añadía ningún desplazamiento extra.

## Un tropiezo que conviene recordar

La primera versión de `ReporteSalidaTests` dio 2 fallos de 3 y pareció confirmar
un defecto en la consulta. No lo era: el registro de la prueba nombraba los
campos `ClienteDestino`, `CodigoLote`, `Destino` y `CantidadUnidades`, mientras
que `ReporteSalidaDto` los llama `Cliente`, `CodigoLoteFaenado`, `Ubicacion` y
`Unidades`. System.Text.Json empareja **por nombre**: los campos inventados se
deserializaban como `null` en silencio y las aserciones fallaban con el sistema
sano.

Un registro de prueba con nombres que no existen no da error de compilación ni
de deserialización, solo resultados falsos.

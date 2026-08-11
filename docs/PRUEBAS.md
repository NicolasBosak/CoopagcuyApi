# Cómo correr las pruebas

## Requisito

Docker Desktop en marcha. **No** se ejecuta `dotnet test` directamente en
Windows: Smart App Control bloquea la carga del DLL recién compilado desde
OneDrive (error `0x800711C7`). Por eso la batería corre dentro de un
contenedor Linux (`docker-compose.tests.yml`).

## Batería completa

    docker compose -f docker-compose.tests.yml run --rm tests

**La primera corrida (caché frío) tarda varios minutos** descargando
paquetes NuGet: lánzala con paciencia y no la mates a mitad. Las siguientes
reutilizan el volumen `nuget-cache` y tardan segundos.

## Una sola clase

    docker compose -f docker-compose.tests.yml run --rm tests \
      dotnet test tests/CoopagcuyApi.Tests/CoopagcuyApi.Tests.csproj --filter "FullyQualifiedName~ArranqueBaseDatosTests"

Se apunta al `.csproj` de pruebas, **no** a `CoopagcuyApi.slnx`: el formato
`.slnx` solo lo entiende el SDK 9.0.200 o superior y el contenedor usa
`sdk:8.0` (falla con `MSB4068`). El `.csproj` de pruebas arrastra el
proyecto del API por referencia, así que no hace falta el `.slnx` para
compilar nada.

## Bajar el Postgres al terminar

    docker compose -f docker-compose.tests.yml down

Basta con `down` a secas. Postgres no tiene volumen propio que perder: usa
`tmpfs`, así que sus datos ya desaparecen al apagar el contenedor, con o
sin `-v`. El único volumen nombrado que existe en este compose es
`nuget-cache`, y ese sí lo borraría el `-v` — obligando a la próxima
corrida a volver a descargar los paquetes NuGet desde cero. Si alguna vez
hace falta forzar ese caché limpio (por ejemplo, tras un cambio de versión
de paquetes), ahí sí:

    docker compose -f docker-compose.tests.yml down -v

## Cómo está montado

- `postgres:16-alpine` efímero (`tmpfs`), publicado en el puerto **5433**
  del host para no chocar con un Postgres instalado localmente.
- Las migraciones se aplican una vez por corrida (al arrancar la primera
  clase de prueba); entre pruebas se truncan las tablas con Respawn,
  excepto `__EFMigrationsHistory`.
- Se usa Respawn (truncar tablas) y no el patrón habitual de envolver cada
  prueba en una transacción con rollback. La razón está en
  `BaseDatosFixture.cs`: el código de producción abre sus propias
  transacciones dentro de `CreateExecutionStrategy`, y anidar una
  transacción de prueba alrededor de esas no funciona — el rollback de
  afuera no deshace lo que la aplicación ya confirmó por dentro. Por eso
  Respawn, que trunca en vez de hacer rollback.
- Las clases de prueba comparten una sola `ApiFactory` (ver
  `ColeccionApi`) y por tanto **no corren en paralelo**: al compartir una
  única base de datos, la limpieza de una prueba borraría los datos de
  otra si corrieran a la vez.
- Los tokens se firman en el propio test (`Infra/Jwt.cs`) en vez de llamar
  a `/api/auth/login`, para no depender de usuarios sembrados y para no
  agotar el rate limiter de `auth` (10 peticiones por minuto y por IP:
  todas las pruebas salen de la misma IP y lo agotarían enseguida).
- Las pruebas **nunca** tocan Neon. La cadena de conexión sale de
  `TEST_DB_CONNECTION` (fijada por el propio compose); fuera del
  contenedor, `ApiFactory` cae por defecto al Postgres de 5433.

## Secuencias de identidad: no asumas `Id == 1`

Respawn trunca las tablas entre pruebas **sin** `RESTART IDENTITY` (el
fixture no fija `WithReseed` en `RespawnerOptions`). Eso significa que los
contadores de identidad de Postgres **no se reinician** aunque las filas
desaparezcan: si una prueba anterior insertó y truncó una fila con
`Id == 1`, la siguiente inserción en esa tabla saldrá con `Id == 2`, no
`1`, aunque la tabla esté vacía al empezar.

Ninguna prueba puede asumir que la primera fila que inserte tendrá un id
concreto. Si una prueba necesita referenciar el id de una fila recién
insertada, debe leerlo de vuelta (por ejemplo, del resultado de
`SaveChangesAsync` o de la respuesta del endpoint), nunca asumirlo por
convención.

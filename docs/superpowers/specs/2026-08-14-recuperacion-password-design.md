# Recuperación de contraseña asistida por administrador — Diseño

**Fecha:** 2026-08-14
**Alcance:** repos `CoopagcuyApi` y `coopagcuy-frontend`
**Objetivo:** que un operador que olvidó su contraseña vuelva a entrar al
sistema sin depender del correo electrónico, dejando registro auditable de
quién pidió el restablecimiento y quién lo concedió.

---

## 1. Problema y contexto

El sistema abandonó el login por correo: los usuarios de campo entran **solo con
cédula** y `Usuario.Email` es un dato de contacto opcional. En la práctica, la
mayoría de operadores de CAT no tiene correo registrado, así que el flujo
estándar de la industria —enlace de un solo uso enviado por email— no es
aplicable: no hay dónde enviarlo.

Hoy no existe ninguna vía de recuperación. Un operador que olvida su contraseña
queda fuera del sistema hasta que un administrador edite su usuario a mano, y no
queda constancia de que ocurrió.

### Restricciones que condicionan el diseño

| Restricción | Consecuencia |
|---|---|
| API en Azure Container Apps con `--min-replicas 0` | No hay procesos de fondo continuos. Nada que dependa de un consumidor siempre despierto. |
| Presupuesto objetivo ~$0–2/mes | Sin servicios externos de pago (correo, SMS, broker). |
| Usuarios sin correo | La contraseña se entrega por canal humano (teléfono/presencial). |
| Neon serverless, arranque en frío | Evitar trabajo asíncrono que dependa de latencia predecible. |

---

## 2. Decisiones tomadas

1. **Notificación al administrador:** bandeja dentro del sistema, no correo ni
   mensajería. Coste cero, durable, sin credenciales nuevas.
2. **Entrega de la contraseña:** el sistema genera una temporal legible, se la
   muestra al admin **una sola vez** para que la dicte, y obliga al usuario a
   cambiarla al entrar. El admin nunca conoce la contraseña definitiva.
3. **Respuesta al solicitante:** el front valida formato y dígito verificador de
   la cédula; si el formato es válido, la respuesta es siempre la misma exista o
   no el usuario. No se revela quién tiene cuenta.
4. **Ubicación en la interfaz:** módulo propio en el API, tercera pestaña dentro
   de `Administración` en el front. El menú ya tiene nueve entradas y son
   tablets de campo.
5. **Autorización:** `AdminCooperativa` y `AdminTecnico` pueden restablecer
   contraseñas. Como parte de este cambio, `AdminCooperativa` **pierde** el
   acceso a la pantalla de sesiones activas, que queda solo para `AdminTecnico`.
6. **Sin RabbitMQ ni ningún gestor de colas.** Ver anexo A.

### Enfoques descartados

**Enlace de un solo uso al correo.** Es el estándar y sería lo correcto en otro
producto. Aquí exige un dato que los usuarios objetivo no tienen y un proveedor
de envío que el presupuesto no admite.

**Que el admin teclee la contraseña a mano.** Es lo que `UsuarioService.
ActualizarAsync` ya permite hoy. Lleva en la práctica a contraseñas débiles y
repetidas entre operadores, y deja al admin conociendo permanentemente las
credenciales de su equipo.

**Sin bandeja: solo marcar al usuario en la lista existente.** El menor código
posible, pero destruye la trazabilidad de quién pidió qué y cuándo se resolvió.
En un sistema cuya razón de ser es la trazabilidad certificable, esa auditoría
no es opcional.

**Caducidad automática de solicitudes pendientes.** Requeriría un trabajo
programado; con escalado a cero eso significa infraestructura nueva. En su lugar
la bandeja muestra la antigüedad de cada solicitud y el admin descarta las
viejas, igual que en la bandeja de Vinculaciones.

---

## 3. Modelo de datos

### Tabla nueva `SolicitudesRestablecerPassword`

Espejo estructural de `EntregaPendienteVinculacion`, que ya resuelve el mismo
problema de forma (cola de trabajo persistente revisada por un administrador).

| Campo | Tipo | Nota |
|---|---|---|
| `Id` | `int` | PK |
| `UsuarioId` | `int` FK → `Usuarios` | Nunca nulo |
| `CedulaSolicitada` | `string` | Copiada al crear: la auditoría sobrevive aunque el usuario cambie o se desactive |
| `Estado` | `EstadoSolicitudPassword` | `Pendiente` / `Resuelta` / `Descartada`. Persistido como texto con `HasConversion<string>()`, igual que el resto de enums del proyecto |
| `FechaCreacion` | `DateTime` | UTC |
| `FechaResolucion` | `DateTime?` | |
| `ResueltaPor` | `string?` | Cédula del administrador |
| `IpSolicitud` | `string?` | IP real del solicitante, ya reescrita por `UseForwardedHeaders` |

### Columna nueva en `Usuarios`

| Campo | Tipo | Nota |
|---|---|---|
| `DebeCambiarPassword` | `bool` | Default `false` |

### Índice único parcial

```
HasIndex(s => s.UsuarioId).IsUnique().HasFilter("\"Estado\" = 'Pendiente'")
```

Garantiza **en la base de datos**, no en código, que un usuario no acumule
solicitudes pendientes. Si el operador pulsa el botón cinco veces seguidas, el
administrador ve una sola entrada. El choque se captura en el servicio y se
traduce a la misma respuesta de éxito genérica: desde fuera, "ya tenías una
solicitud pendiente" y "acabo de crear tu solicitud" deben ser indistinguibles.

### No se registran solicitudes de cédulas inexistentes

El endpoint de solicitud es público. Si cada cédula inventada creara una fila,
cualquiera podría inflar la tabla desde internet. La respuesta genérica que ve
el usuario es una decisión de interfaz, no un registro que haya que persistir.
La protección contra abuso es el rate limiter `auth` (10 peticiones/minuto por
IP) que ya existe en `Program.cs`, aplicado también a este endpoint.

### Migración

Nombre: `RecuperacionPassword`. **Aditiva**: una tabla nueva y una columna
nueva con valor por defecto, cero alteraciones de columnas existentes. No toca
datos.

Debe generarse dentro del contenedor Linux de SDK montando el proyecto —Smart
App Control bloquea `dotnet ef` sobre DLLs en OneDrive— y la
`AppDbContextFactory` debe conservar `Npgsql.EnableLegacyTimestampBehavior`
o la migración generará un `AlterColumn` masivo de todas las fechas.

---

## 4. Arquitectura y flujo

### Módulos

**API** — `Common/Auth/Recuperacion/`:

| Archivo | Responsabilidad |
|---|---|
| `SolicitudRestablecerPassword.cs` | Modelo y enum de estado |
| `RecuperacionDtos.cs` | DTOs de petición y respuesta |
| `RecuperacionService.cs` | Toda la lógica: crear, listar, resolver, descartar, cambiar contraseña |
| `RecuperacionController.cs` | Endpoints y autorización |
| `GeneradorPasswordTemporal.cs` | Generación de la temporal dictable |

`RecuperacionService` depende de `AppDbContext` y de `ISesionService` (para
revocar sesiones al restablecer). Se registra como `Scoped` en `Program.cs`
junto al resto de servicios de autenticación.

**Front** — tres pantallas y un componente:

| Archivo | Responsabilidad |
|---|---|
| `pages/RecuperarPassword.tsx` | Pantalla pública de solicitud |
| `pages/CambiarPassword.tsx` | Cambio de contraseña |
| `components/admin/SolicitudesPassword.tsx` | Pestaña del administrador |
| `api/recuperacion.ts` | Cliente HTTP del módulo |

`pages/CambiarPassword.tsx` sirve a dos usos con la misma pantalla: el cambio
**obligatorio** tras entrar con una temporal (llega redirigido, sin poder
navegar a otro sitio) y el cambio **voluntario** de cualquier usuario que quiera
cambiar la suya (llega desde el menú de usuario, con navegación libre). La
diferencia es únicamente si `debeCambiarPassword` está activo; el endpoint es el
mismo.

`Administracion.tsx` pasa de dos pestañas a tres. Como parte de este cambio, sus
pestañas se extraen a componentes propios (`components/admin/TablaUsuarios.tsx`
y `components/admin/TablaComunidades.tsx`, junto al nuevo
`SolicitudesPassword.tsx`). El archivo ya mezcla dos responsabilidades; añadir
una tercera sin partirlo lo empeora, y las tres pestañas quedan revisables por
separado.

### Endpoints

| Método | Ruta | Acceso | Devuelve |
|---|---|---|---|
| `POST` | `/api/auth/recuperacion` | Anónimo, rate limiter `auth` | Mensaje genérico |
| `GET` | `/api/auth/recuperacion` | `AdminCooperativa,AdminTecnico` | Pendientes; con `?incluirResueltas=true`, el historial completo |
| `POST` | `/api/auth/recuperacion/{id}/resolver` | `AdminCooperativa,AdminTecnico` | La contraseña temporal |
| `POST` | `/api/auth/recuperacion/{id}/descartar` | `AdminCooperativa,AdminTecnico` | `204`; deja la solicitud en `Descartada` |
| `POST` | `/api/auth/cambiar-password` | Cualquier usuario autenticado | `204` |

`RecuperacionController` declara su ruta explícitamente con
`[Route("api/auth/recuperacion")]`, **no** con el `[Route("api/[controller]")]`
que usa el resto del proyecto: la convención por nombre de controlador daría
`api/recuperacion`, fuera del prefijo `/api/auth`. Ese prefijo importa porque la
cookie del refresh token está limitada a `Path=/api/auth`, y agrupar ahí los
endpoints de autenticación mantiene coherente el modelo mental del módulo.
`cambiar-password` vive en el mismo controlador con su propia ruta absoluta.

`descartar` es la salida para solicitudes que el administrador decide no
atender: una cédula de alguien que ya no trabaja en la cooperativa, o una
solicitud duplicada resuelta por teléfono. Deja constancia en lugar de borrar la
fila.

### Recorrido completo

**1 · El operador solicita.**
Botón "¿Olvidaste tu contraseña?" bajo el formulario de `Login.tsx`, que navega
a la ruta pública `/recuperar-password`. La pantalla pide la cédula y la valida
localmente con `utils/validarCedula.ts` antes de llamar al API: un dígito
verificador incorrecto se rechaza al instante sin tocar la red, lo que en una
tablet con mala señal es la diferencia entre respuesta inmediata y quince
segundos de espera.

**2 · El servidor registra.**
`POST /api/auth/recuperacion` revalida con `ValidadorCedula.EsValida` —nunca se
confía en el cliente— y devuelve 400 si el formato es inválido. Con formato
válido busca un usuario activo por cédula:

- No existe → responde 200 genérico, sin crear fila.
- Existe y ya tiene pendiente → responde 200 genérico, sin crear otra.
- Existe y no tiene pendiente → crea la solicitud y responde 200 genérico.

El cuerpo es siempre: `{ "mensaje": "Tu solicitud fue enviada al administrador.
Te contactará para darte una contraseña nueva." }`

**3 · El administrador ve la bandeja.**
Pestaña "Contraseñas" en `Administración`, con las solicitudes pendientes:
nombre, cédula, rol, CAT asignado y antigüedad de la solicitud.

**4 · El administrador restablece.**
`POST /api/auth/recuperacion/{id}/resolver` ejecuta **en una sola transacción**:

1. Genera la contraseña temporal.
2. La hashea con BCrypt y la escribe en `usuario.PasswordHash`.
3. Marca `usuario.DebeCambiarPassword = true`.
4. Cierra la solicitud: `Estado = Resuelta`, `FechaResolucion`, `ResueltaPor`.
5. **Revoca todas las sesiones activas del usuario** vía
   `ISesionService.RevocarUsuarioAsync`.

El paso 5 no es opcional. Si la solicitud se originó porque alguien tomó la
tablet del operador, restablecer sin revocar dejaría al intruso dentro con su
sesión de 7 días intacta.

Antes de todo ello se verifica que el usuario **siga activo**. Solo se crean
solicitudes para usuarios activos (§4, paso 2), pero un usuario puede ser dado
de baja entre la solicitud y su resolución; restablecerle la contraseña
entonces sería devolverle acceso a alguien que la cooperativa acaba de apartar.
En ese caso el endpoint responde 409 y el administrador descarta la solicitud.

La respuesta trae la contraseña temporal en claro. Es la **única vez** que
existe fuera del hash: el front la muestra en un modal con botón de copiar y un
aviso explícito de que no se volverá a mostrar. El administrador la dicta al
operador por teléfono o en persona.

**5 · El operador entra y cambia.**
Login normal con la temporal. `LoginResponseDto` gana el campo
`DebeCambiarPassword`; cuando llega `true`, `PrivateRoute` redirige a
`/cambiar-password` desde cualquier ruta y bloquea la navegación al resto de la
aplicación hasta completarlo. `POST /api/auth/cambiar-password` recibe
contraseña actual y nueva, aplica la política existente de `ValidarPassword`
(8+ caracteres, al menos una letra y un dígito), y baja la bandera.

### Contraseña temporal

Formato: palabra corta del dominio + guion + 5 dígitos criptográficamente
aleatorios (`RandomNumberGenerator`). Ejemplo: `cuy-48213`.

Cumple la política existente y, sobre todo, **se puede dictar por teléfono sin
ambigüedad**, que es el requisito real de este sistema. Una cadena como
`xK7mQ2vP` es más fuerte sobre el papel e inservible cuando hay que
deletreársela a un operador en el campo con mala cobertura.

La menor entropía se compensa con tres factores: la temporal vive minutos, el
endpoint de login ya limita a 10 intentos por minuto y por IP, y queda
inutilizada en cuanto el operador la cambia. El diccionario de palabras se
mantiene corto y en `GeneradorPasswordTemporal.cs`, sin caracteres ambiguos
(sin `0`/`O`, sin `1`/`l`/`I`).

---

## 5. Cambios en la autorización existente

| Recurso | Antes | Ahora |
|---|---|---|
| `GET /api/auth/sesiones` | `AdminCooperativa,AdminTecnico` | `AdminTecnico` |
| `DELETE /api/auth/sesiones/{id}` | `AdminCooperativa,AdminTecnico` | `AdminTecnico` |
| `DELETE /api/auth/sesiones/usuario/{usuarioId}` | `AdminCooperativa,AdminTecnico` | `AdminTecnico` |
| Ruta `/sesiones` en `App.tsx` | ambos admins | `AdminTecnico` |
| Entrada de menú "Sesiones" en `MainLayout.tsx` | ambos admins | `AdminTecnico` |

Son **cinco sitios**. El spec los enumera porque una ruta protegida en el front
sin su `[Authorize]` correspondiente en el API es una falsa sensación de
seguridad: cualquiera con el token puede llamar al endpoint directamente.

`AdminTecnico` conserva acceso a todo el sistema. `AdminCooperativa` pierde
sesiones activas y gana la bandeja de contraseñas.

### Riesgo aceptado

Un `AdminCooperativa` puede restablecer la contraseña de un `AdminTecnico` y
entrar con la temporal que él mismo generó. Es el precio inevitable de eliminar
el bloqueo por diseño: si solo el técnico pudiera restablecer contraseñas y
olvidara la suya, no habría forma de rescatarlo salvo editar Neon con SQL a
mano.

Lo que hace tolerable el riesgo es que **no es sigiloso**: queda `ResueltaPor`
con fecha en la tabla, y al técnico se le revocan todas las sesiones y deja de
funcionarle su contraseña, así que se entera de inmediato. Es un riesgo
auditable y detectable, no una puerta trasera silenciosa.

---

## 6. Manejo de errores

Todas las respuestas de error usan `{ mensaje }`, el formato uniforme del
sistema, servido por el exception handler global de `Program.cs`.

| Caso | Código | Mensaje |
|---|---|---|
| Cédula con formato o dígito verificador inválido | 400 | "El número de cédula ingresado no es válido." |
| Solicitud ya resuelta por otro admin en paralelo | 409 | "Otro administrador ya atendió esta solicitud." |
| Usuario desactivado entre solicitud y resolución | 409 | "El usuario está desactivado. Reactívalo antes de restablecer su contraseña." |
| Solicitud inexistente | 404 | — |
| Contraseña nueva que incumple la política | 400 | Texto de `ValidarPassword` |
| Contraseña actual incorrecta al cambiar | 401 | "La contraseña actual no es correcta." |
| Choque del índice único parcial | 200 | Respuesta genérica de éxito (ver §3) |

El último caso es deliberado: el conflicto se captura en `RecuperacionService`
antes de que llegue al handler global, para no filtrar por la vía del código de
estado la existencia de una solicitud previa.

---

## 7. Estrategia de pruebas

Se añaden a `tests/CoopagcuyApi.Tests` (xUnit + Shouldly + Respawn contra
Postgres real). Ejecución:

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

### Casos que cubren los invariantes reales

1. Cédula inexistente y cédula existente producen **cuerpo de respuesta
   idéntico** (no filtración de existencia).
2. Cédula con dígito verificador inválido → 400.
3. Dos solicitudes seguidas del mismo usuario producen **una sola fila** en
   estado `Pendiente`.
4. Resolver genera una temporal que cumple la política, marca
   `DebeCambiarPassword` y **revoca las sesiones activas** del usuario.
5. Login con la temporal responde `debeCambiarPassword: true`.
6. Cambiar contraseña con la actual correcta baja la bandera; con la actual
   incorrecta responde 401 y no la baja.
7. Un `AdminCooperativa` recibe **403 en `/api/auth/sesiones`** y **200 en la
   bandeja de contraseñas** (verifica el cambio de autorización de §5).
8. Resolver dos veces la misma solicitud → la segunda responde 409.
9. Resolver la solicitud de un usuario desactivado después de crearla → 409, y
   la contraseña del usuario **no cambia**.

### Restricciones del entorno de pruebas

- Respawn trunca **sin** `RESTART IDENTITY`: ninguna prueba puede asumir
  `Id == 1`.
- `Program.cs` lee su configuración antes de `builder.Build()`, así que
  `ApiFactory` usa variables de entorno reales, no `AddInMemoryCollection`.
- `Enum.TryParse<CentroAcopio>` distingue mayúsculas: usar `PAT`/`NIE`/`HUE`/
  `NAB`/`PEL` en los datos de prueba.

---

## Anexo A · Por qué no se usa RabbitMQ

La pregunta de partida era si un gestor de colas optimizaría el sistema sin
afectar mucho el presupuesto. La respuesta es que aquí no rinde, por tres
razones independientes, cualquiera de ellas suficiente.

**1 · Incompatibilidad con el escalado a cero.** El API corre con
`--min-replicas 0` (`infra/bootstrap.azcli`). Un consumidor de RabbitMQ es un
proceso que debe estar permanentemente escuchando. Con escala a cero no hay
nadie consumiendo: los mensajes se acumulan hasta que alguien llame al API por
otro motivo. Corregirlo exige `--min-replicas 1`, que elimina precisamente el
ahorro que hace que hoy el coste sea casi nulo.

**2 · Coste.** Auto-hospedar RabbitMQ en Container Apps requiere una réplica
siempre encendida más almacenamiento persistente: estimado **~$10–15/mes**,
por encima de la cuota gratuita mensual de la plataforma (cifra a confirmar con
la calculadora de Azure antes de comprometerla). CloudAMQP tiene plan gratuito,
pero con topes bajos de mensajes en cola y conexiones, y **no resuelve el
problema 1**. Sumado el `--min-replicas 1` del consumidor, el presupuesto
objetivo de $0–2/mes queda descartado.

**3 · El beneficio no aplica a esta escala.** Los beneficios de un broker
—desacoplar productor y consumidor, amortiguar picos, reintentar con backoff,
fan-out a varios consumidores— se materializan con volumen. Este sistema tiene
cinco centros de acopio, un puñado de tablets y decenas de escrituras al día.
Los cuellos de botella reales documentados son el arranque en frío de Neon y el
escalado a cero; un broker no mejora ninguno de los dos y añade un salto de
latencia más.

**Dónde sí existe trabajo asíncrono legítimo:** generación de PDF y Excel de
reportes, generación del PNG del QR y su subida a Blob, y notificaciones. Los
tres son de volumen bajísimo. Si en el futuro justifican tratamiento asíncrono,
el paso barato es una cola de trabajos en PostgreSQL (patrón *outbox*) disparada
por un cron gratuito de GitHub Actions — sin broker, sin réplica siempre
encendida y sin credenciales nuevas.

**El patrón elegido para este diseño es esa misma idea en su forma más simple:**
una tabla de solicitudes pendientes revisada por un humano. Es durable
—sobrevive al escalado a cero, cosa que una cola en memoria no—, cuesta cero, y
replica un patrón que el repositorio ya tiene funcionando en
`EntregaPendienteVinculacion`.

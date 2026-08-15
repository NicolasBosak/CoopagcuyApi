# Contraseña temporal al crear la cuenta — Diseño

**Fecha:** 2026-08-14
**Alcance:** repos `CoopagcuyApi` y `coopagcuy-frontend`
**Objetivo:** que ningún administrador llegue a conocer la contraseña con la que
un usuario opera el sistema, ni al crear su cuenta ni después.

**Depende de:** `2026-08-14-recuperacion-password-design.md`. Este diseño reutiliza
`GeneradorPasswordTemporal`, la bandera `Usuario.DebeCambiarPassword`, la pantalla
`/cambiar-password` y la tabla `SolicitudesRestablecerPassword` que aquel introdujo.

---

## 1. Problema

El sistema de recuperación de contraseña cerró el caso del usuario que olvida la
suya: el administrador genera una temporal, la dicta, y el sistema obliga a
cambiarla. El administrador nunca conoce la contraseña definitiva.

Ese mismo cuidado no existe en el alta. Hoy el administrador **teclea** la
contraseña del usuario nuevo en el formulario, así que la conoce desde el primer
día y nada obliga a cambiarla nunca.

Y hay una segunda puerta, menos visible: el formulario de **editar** usuario
incluye un campo "Nueva contraseña" (`ActualizarUsuarioDto.NuevaPassword`) que
permite al administrador reemplazar la contraseña de cualquiera en cualquier
momento, sin que esa persona haya pedido nada.

Cerrar solo el alta no cumpliría el objetivo: el administrador crearía la cuenta,
el sistema generaría `cuy-48213`, y acto seguido el administrador abriría Editar,
escribiría `clave12345` y volvería a conocer la contraseña de esa persona.

---

## 2. Decisiones tomadas

1. **Al crear una cuenta, la contraseña la genera el sistema.** `CrearUsuarioDto`
   pierde su campo `Password`.
2. **El formulario de edición pierde su campo de contraseña.** En su lugar, la
   lista de usuarios gana un botón "Restablecer contraseña" que genera una
   temporal. El administrador conserva la capacidad de desbloquear a alguien por
   teléfono, pero deja de poder *elegir* la contraseña.
3. **El restablecimiento por iniciativa del administrador queda auditado.** Se
   añade la columna `Origen` (`Usuario` / `Administrador`) a
   `SolicitudesRestablecerPassword`.
4. **La lógica compartida vive en un ayudante puro**, no en un servicio con
   banderas. Ver §4.
5. **El endpoint `/api/auth/setup` no cambia.** Ver §6.

### Enfoques descartados

**Arreglar solo el alta.** Menos trabajo, pero deja la segunda puerta abierta y
convierte el objetivo de privacidad en apariencia: el administrador puede fijar
la contraseña de cualquiera en dos clics desde Editar.

**Quitar el campo de las dos pantallas sin añadir nada.** Lo más hermético, pero
deja al administrador sin forma de ayudar a quien no logre usar la pantalla de
recuperación por su cuenta — un operador de campo con poca experiencia digital
que llama por teléfono. La bandeja solo atiende solicitudes que nacen del propio
usuario.

**Centralizar todo en `RecuperacionService` y que `UsuarioService` le delegue.**
Invierte la dependencia natural (crear un usuario pasaría a depender del módulo
de recuperar contraseñas) y obliga a que `ResolverAsync` acepte parámetros para
distinguir "usuario nuevo sin sesiones que revocar" de "usuario existente". Es el
camino a un método con tres banderas booleanas.

**Repetir las tres líneas en cada sitio.** Son tres copias de una regla de
seguridad; el día que la temporal deba caducar a las 24 horas hay que acordarse
de las tres.

---

## 3. Modelo de datos

### Columna nueva en `SolicitudesRestablecerPassword`

| Campo | Tipo | Nota |
|---|---|---|
| `Origen` | `OrigenSolicitudPassword` | `Usuario` / `Administrador`. Persistido como texto con `HasConversion<string>()`, `HasMaxLength(20)`, por defecto `Usuario` |

Migración `OrigenSolicitudPassword`: **aditiva**, una sola columna con valor por
defecto. Las filas existentes nacieron todas de una solicitud del usuario, así
que el valor por defecto ya es el correcto para el histórico — no hace falta
arreglo de datos.

El índice único parcial existente filtra por `Estado = 'Pendiente'`, y los
restablecimientos proactivos crean filas ya `Resuelta`, así que no chocan con él.

### Regla: un restablecimiento proactivo sobre una solicitud pendiente

Si el administrador restablece por iniciativa propia a alguien que **ya tenía una
solicitud pendiente**, no se crea una fila nueva: se resuelve la que había,
conservando `Origen = Usuario`.

Es lo que de verdad ocurrió — esa persona sí pidió el cambio, y el administrador
la atendió sin pasar por el botón de la bandeja. Crear una segunda fila dejaría
la pendiente colgada para siempre, y el administrador vería en su bandeja una
solicitud fantasma de alguien a quien ya atendió.

---

## 4. Arquitectura

### El ayudante compartido

`Common/Auth/Recuperacion/CredencialTemporal.cs`:

```
CredencialTemporal.Asignar(Usuario usuario) → string
```

Genera la contraseña con `GeneradorPasswordTemporal`, escribe su hash BCrypt en
`usuario.PasswordHash`, activa `usuario.DebeCambiarPassword` y devuelve el texto
plano.

**No toca la base de datos y no guarda nada**: solo transforma la entidad que le
pasan. Eso lo hace comprobable con una prueba unitaria sin Postgres, y deja que
cada llamador decida cuándo persistir, si revoca sesiones y si escribe auditoría
— que es justo lo que difiere entre los tres casos de uso.

### Los tres puntos de uso

| Llamador | Qué hace además del ayudante |
|---|---|
| `UsuarioService.CrearAsync` | Guarda el usuario. **No** revoca sesiones (no las hay) ni escribe fila de auditoría (el propio registro del usuario, con su `FechaCreacion`, ya es el rastro) |
| `RecuperacionService.ResolverAsync` | Cierra la solicitud pendiente y revoca sesiones. Sustituye sus líneas propias por el ayudante: misma conducta, un solo sitio donde vive la regla |
| `RecuperacionService.RestablecerPorAdminAsync` | Resuelve la pendiente si la hay, o crea una fila `Resuelta` con `Origen = Administrador`; revoca sesiones |

### Cambios en los contratos

| Qué | Cambio |
|---|---|
| `CrearUsuarioDto` | **Se elimina** `Password`, y con él la llamada a `PoliticaPassword.Validar` en `CrearAsync`: ya no hay contraseña de entrada que validar |
| `ActualizarUsuarioDto` | **Se elimina** `NuevaPassword`, y con él la rama que la aplicaba en `ActualizarAsync` |
| `UsuarioCreadoDto` | **Nuevo.** `(UsuarioResponseDto Usuario, string PasswordTemporal)` |
| `SolicitudPasswordDto` | Gana `Origen` (texto) |

### Endpoints

| Método | Ruta | Acceso | Cambio |
|---|---|---|---|
| `POST` | `/api/usuarios` | `AdminCooperativa,AdminTecnico` | Sin `password` en el cuerpo; devuelve `UsuarioCreadoDto` |
| `PUT` | `/api/usuarios/{id}` | `AdminCooperativa,AdminTecnico` | Sin `nuevaPassword` en el cuerpo |
| `POST` | `/api/auth/recuperacion/usuario/{usuarioId}` | `AdminCooperativa,AdminTecnico` | **Nuevo.** Devuelve `PasswordTemporalDto` |

El endpoint del restablecimiento proactivo vive en `RecuperacionController` y no
en `UsuariosController` aunque el botón esté en la lista de usuarios: la
operación escribe en `SolicitudesRestablecerPassword` y revoca sesiones, así que
mantener toda la lógica de contraseñas en un módulo pesa más que la proximidad a
la pantalla que la dispara.

`RestablecerPorAdminAsync` rechaza con 409 al usuario desactivado, por la misma
razón que `ResolverAsync`: sería devolverle el acceso a alguien que la
cooperativa acaba de apartar.

**Y rechaza con 409 que un administrador se restablezca a sí mismo.** No es una
restricción de seguridad sino de coherencia: el restablecimiento revoca todas las
sesiones del usuario afectado, así que un administrador que se lo aplicara
quedaría desconectado en mitad de la operación, con la temporal a medio leer en
un modal que su propia sesión acaba de invalidar. Para eso ya existe la pantalla
`/cambiar-password`, accesible a cualquier usuario autenticado, y el mensaje del
409 remite a ella. Es el mismo criterio que ya aplica `CambiarEstadoAsync` al
impedir que un administrador se desactive a sí mismo.

---

## 5. Front

### El modal de contraseña temporal se extrae

Hoy vive dentro de `SolicitudesPassword.tsx`. Ahora lo necesitan tres pantallas,
así que pasa a `components/admin/ModalPasswordTemporal.tsx`, recibiendo el
`PasswordTemporal` y un `onClose`. Es la misma extracción que se hizo con las
pestañas de Administración, y por el mismo motivo: la tercera copia es la que
convierte un descuido en una divergencia.

| Archivo | Cambio |
|---|---|
| `components/admin/ModalPasswordTemporal.tsx` | **Crear.** El modal, extraído sin cambios de conducta |
| `components/admin/FormUsuario.tsx` | **Quitar** el campo de contraseña y su validación local; al crear, abrir el modal con la temporal |
| `components/admin/TablaUsuarios.tsx` | Botón "Restablecer" por fila → endpoint nuevo → mismo modal. **No se muestra** en usuarios inactivos ni en la fila del propio administrador, porque el servidor rechazaría ambos casos con 409: un botón que siempre falla es peor que un botón ausente |
| `components/admin/SolicitudesPassword.tsx` | Usa el modal extraído; muestra el origen de cada solicitud |
| `api/admin.ts` | `crear` devuelve `{ usuario, passwordTemporal }` |
| `api/recuperacion.ts` | `restablecerPorUsuario(usuarioId)` |
| `types/admin.ts` | `UsuarioCreado`; `CrearUsuario` sin `password`; `ActualizarUsuario` sin `nuevaPassword` |
| `types/recuperacion.ts` | `origen` en `SolicitudPassword` |

Crear una cuenta pasa a ser nombre, cédula, correo opcional, rol y CAT: un campo
menos que teclear y una decisión menos que tomar mal.

### El modo de fallo nuevo, y por qué es tolerable

El administrador crea un usuario, cierra el modal sin anotar la temporal, y esa
persona no puede entrar. Antes no ocurría, porque el administrador sabía la
contraseña: la había escrito él.

No se evita con confirmaciones ni con un segundo aviso — se hace **recuperable**:
el botón "Restablecer" de la lista genera otra en dos clics. Es la misma razón
por la que el modal ya advierte de que no se volverá a mostrar. Un flujo que se
puede repetir sin consecuencias no necesita defenderse de un despiste.

---

## 6. Lo que queda fuera

**El endpoint `/api/auth/setup`**, que crea el administrador inicial con
`Setup:Key`, conserva su parámetro de contraseña. Ahí quien ejecuta la
instalación **es** el usuario que se está creando: no hay nadie a quien dictarle
una temporal, y obligarle a cambiarla acto seguido sería ceremonia sin
beneficio.

**Caducidad de la contraseña temporal.** Igual que en el diseño anterior,
requeriría trabajo programado y el Container App escala a cero. La temporal se
inutiliza en cuanto el usuario la cambia, y el rate limiter de `auth` frena la
fuerza bruta mientras tanto.

---

## 7. Manejo de errores

| Caso | Código | Mensaje |
|---|---|---|
| Cédula duplicada al crear | 409 | "Ya existe un usuario registrado con esa cédula." (actual) |
| Cédula inválida al crear | 409 | "El número de cédula ingresado no es válido." (actual) |
| Restablecer un usuario inexistente | 404 | — |
| Restablecer un usuario desactivado | 409 | "El usuario está desactivado. Reactívalo antes de restablecer su contraseña." |
| Un administrador se restablece a sí mismo | 409 | "No puedes restablecer tu propia contraseña desde aquí. Usa la pantalla de cambiar contraseña." |

Todo con el formato `{ mensaje }` del resto del sistema.

---

## 8. Estrategia de pruebas

Una unitaria y siete de integración, sobre las 43 que ya están en verde.
Ejecución:

```bash
docker compose -f docker-compose.tests.yml run --rm tests
```

1. **Unitaria de `CredencialTemporal.Asignar`**: activa `DebeCambiarPassword`, el
   hash resultante verifica contra el texto devuelto, y ese texto cumple
   `PoliticaPassword`.
2. Crear un usuario devuelve una contraseña temporal válida y lo deja con
   `DebeCambiarPassword` activo.
3. **Enviar `password` en el JSON de creación no tiene efecto**: la contraseña
   guardada es la generada, no la enviada. Es la prueba que demuestra que la
   puerta quedó cerrada de verdad y no solo escondida en el formulario.
4. **Enviar `nuevaPassword` al actualizar no cambia el hash.** Misma idea para la
   segunda puerta.
5. Restablecer por administrador genera una temporal, **revoca las sesiones
   activas** del usuario y crea la fila con `Origen = Administrador`.
6. Restablecer por administrador a alguien con solicitud pendiente **resuelve esa
   fila y no crea una segunda**, conservando `Origen = Usuario`.
7. Login del usuario recién creado trae `debeCambiarPassword: true`, cerrando el
   circuito contra la pantalla `/cambiar-password` que ya existe.
8. Un administrador que intenta restablecerse a sí mismo recibe 409 y **su
   contraseña no cambia**; un usuario inexistente, 404.

Las pruebas 3 y 4 son las que dan valor real a este diseño: comprueban la
**ausencia** de una capacidad, que es lo que un formulario sin campo no
demuestra por sí solo.

### Restricciones del entorno

Las de siempre, que cuestan horas si se olvidan: las cédulas de prueba deben
tener dígito verificador válido (`0104576277`, `0111223343`, `0102030400`);
Respawn trunca sin `RESTART IDENTITY`, así que ninguna prueba puede asumir
`Id == 1`; y cada cliente de prueba lleva su propia IP por `X-Forwarded-For`
para no agotar el rate limiter compartido de `auth`.

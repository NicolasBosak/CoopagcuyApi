# Despliegue y CI/CD — Cuy Azuayito

Guía operativa del despliegue en Azure con GitHub Actions. La provisión
inicial de la nube está en [`infra/bootstrap.azcli`](../infra/bootstrap.azcli).

## Arquitectura

| Pieza | Servicio | Entornos |
|---|---|---|
| API (.NET 8) | Azure Container Apps (escala a cero) | staging, producción |
| Front (React/Vite) | Azure Static Web Apps (Free) | staging, producción |
| Base de datos | Neon PostgreSQL (rama por entorno) | `staging`, `main` |
| Imágenes QR | Azure Blob Storage (contenedor por entorno) | `qr-staging`, `qr-prod` |
| Registro de imágenes | GitHub Container Registry (ghcr.io) | — |

Dominios: se usan los gratuitos de Azure (`*.azurestaticapps.net`,
`*.azurecontainerapps.io`) con HTTPS incluido. El dominio propio solo se
agrega cuando se impriman etiquetas QR comerciales (cambia `QR__BaseUrl`).

## Flujo

```
Pull Request        -> compila y verifica (no despliega)
push a develop      -> staging   (automático)
push a main         -> producción (requiere aprobación en el Environment)
```

Cada despliegue del API: construye la imagen (etiquetada con el SHA) ->
**aplica migraciones EF contra la rama Neon del entorno** -> actualiza la
revisión del Container App -> smoke test a `/health`. Si las migraciones
fallan, la versión anterior queda intacta.

## Configuración en GitHub (una sola vez)

Crea dos **Environments** en cada repo: `staging` y `production`. En
`production`, activa *Required reviewers* (tú) para el gate de aprobación.

### Repo `CoopagcuyApi`

Secretos a nivel de repo (o de cada Environment):

| Secreto | Valor |
|---|---|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | los que imprime `bootstrap.azcli` |

Por **Environment** (staging / production):

| Tipo | Nombre | Valor |
|---|---|---|
| Secret | `NEON_CONNECTION_STRING` | cadena de la rama Neon del entorno |
| Variable | `CONTAINERAPP_NAME` | `coopagcuy-api-staging` / `coopagcuy-api-prod` |
| Variable | `RESOURCE_GROUP` | `rg-cuyazuayito` |
| Variable | `API_URL` | URL pública del Container App del entorno |

> El registro OIDC en `bootstrap.azcli` crea *federated credentials* para
> los sujetos `environment:staging` y `environment:production`. Si nombras
> los Environments distinto, ajusta esos `subject`.

### Repo `coopagcuy-frontend`

Por **Environment** (staging / production):

| Tipo | Nombre | Valor |
|---|---|---|
| Secret | `SWA_DEPLOY_TOKEN` | token de despliegue de la Static Web App del entorno |
| Variable | `VITE_API_URL` | URL del Container App del entorno (sin `/` final) |

## Secretos de runtime del API

No viven en GitHub: son secretos del Container App (los setea
`bootstrap.azcli`). Para rotarlos:

```bash
az containerapp secret set -n coopagcuy-api-prod -g rg-cuyazuayito \
  --secrets jwtkey=<nuevo-valor>
az containerapp update -n coopagcuy-api-prod -g rg-cuyazuayito  # nueva revisión
```

## Usuario maestro (primer arranque)

Tras el primer despliegue exitoso, la base está migrada pero vacía de
usuarios. Crea el maestro con `scripts/reiniciar-usuarios.sql` en la rama
Neon del entorno, o vía `POST /api/auth/setup` con la `Setup:Key` de ese
entorno. (El login es por cédula — ver el propio script.)

## Rollback

**API** — volver a la revisión anterior en segundos:
```bash
az containerapp revision list -n coopagcuy-api-prod -g rg-cuyazuayito -o table
az containerapp revision set-mode -n coopagcuy-api-prod -g rg-cuyazuayito --mode single
az containerapp update -n coopagcuy-api-prod -g rg-cuyazuayito \
  --image ghcr.io/nicolasbosak/coopagcuyapi:<sha-anterior>
```
Como las migraciones son **aditivas y compatibles hacia atrás**, revertir
la app no exige revertir la base. Regla: nunca renombrar/borrar una columna
en la misma migración que despliega el código que deja de usarla (patrón
expand/contract).

**Front** — redesplegar el commit anterior (re-ejecuta el workflow sobre
ese SHA) o usa el historial de despliegues de la Static Web App.

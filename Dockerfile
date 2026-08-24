# ── Etapa de compilación ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaurar dependencias primero: aprovecha la caché de capas de Docker
# mientras no cambien el .csproj o el lock. packages.lock.json tiene que
# venir con el .csproj: sin él, este restore ignora el bloqueo de versiones
# y puede resolver un árbol de dependencias distinto al que Trivy y
# `dotnet list --vulnerable` auditaron en CI sobre el lock committeado.
# RestoreLockedMode hace que ese desacuerdo sea imposible: si el lock queda
# desactualizado respecto al .csproj, este restore falla con NU1004 en vez
# de regenerar el lock en silencio.
COPY CoopagcuyApi.csproj packages.lock.json .
RUN dotnet restore -p:RestoreLockedMode=true

# Compilar y publicar en Release
COPY . .
RUN dotnet publish CoopagcuyApi.csproj -c Release -o /app --no-restore

# ── Etapa de ejecución ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# La imagen aspnet:8.0 no incluye fuentes de texto por defecto.
# Instalamos fontconfig y fonts-liberation para que QuestPDF pueda renderizar texto en los PDF.
RUN apt-get update && apt-get install -y fontconfig fonts-liberation && rm -rf /var/lib/apt/lists/*

# La imagen aspnet:8.0 escucha en 8080 por defecto; ese es el targetPort
# que espera el ingress de Azure Container Apps
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Correr como root dentro del contenedor no aporta nada aquí y amplía el daño
# de cualquier ejecución remota de código. La imagen aspnet:8.0 ya trae el
# usuario "app" (UID 1654) creado por Microsoft.
USER app

ENTRYPOINT ["dotnet", "CoopagcuyApi.dll"]

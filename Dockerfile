# ── Etapa de compilación ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restaurar dependencias primero: aprovecha la caché de capas de Docker
# mientras no cambie el .csproj
COPY CoopagcuyApi.csproj .
RUN dotnet restore

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

ENTRYPOINT ["dotnet", "CoopagcuyApi.dll"]

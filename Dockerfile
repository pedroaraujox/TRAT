FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY src/ControlPlane/ControlPlane.Api/ControlPlane.Api.csproj src/ControlPlane/ControlPlane.Api/
RUN dotnet restore src/ControlPlane/ControlPlane.Api/ControlPlane.Api.csproj
COPY src/ControlPlane/ControlPlane.Api/ src/ControlPlane/ControlPlane.Api/
RUN dotnet publish src/ControlPlane/ControlPlane.Api/ControlPlane.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ARG AGENT_PACKAGE_PATH=artifacts/agent-package-hml/
COPY --from=build /out/ ./
COPY ${AGENT_PACKAGE_PATH} ./artifacts/agent-package/
RUN mkdir -p /app/data/keys /app/backups /app/logs \
    && chown -R app:app /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl --fail --silent http://127.0.0.1:8080/api/v1/health || exit 1
ENTRYPOINT ["dotnet", "ControlPlane.Api.dll"]

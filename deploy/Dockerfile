# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

# Copy solution and projects
COPY . .

# Restore and build
RUN dotnet restore "src/ControlPlane/ControlPlane.Api/ControlPlane.Api.csproj"
RUN dotnet build "src/ControlPlane/ControlPlane.Api/ControlPlane.Api.csproj" -c Release -o /app/build

# Publish stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
WORKDIR /app

# Install required packages (curl for health checks, postgresql client)
RUN apk add --no-cache curl postgresql-client

# Copy artifacts and artifacts folder
COPY --from=builder /app/build .
COPY artifacts /app/artifacts

# Create required directories
RUN mkdir -p /app/data/keys /app/logs

# Expose port
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5000/api/v1/health || exit 1

# Run application
ENTRYPOINT ["dotnet", "ControlPlane.Api.dll"]

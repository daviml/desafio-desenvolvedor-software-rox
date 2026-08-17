# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/CashFlow.SharedKernel/CashFlow.SharedKernel.csproj src/BuildingBlocks/CashFlow.SharedKernel/
COPY src/BuildingBlocks/CashFlow.Messaging/CashFlow.Messaging.csproj src/BuildingBlocks/CashFlow.Messaging/
COPY src/BuildingBlocks/CashFlow.Messaging.RabbitMq/CashFlow.Messaging.RabbitMq.csproj src/BuildingBlocks/CashFlow.Messaging.RabbitMq/
COPY src/BuildingBlocks/CashFlow.Web/CashFlow.Web.csproj src/BuildingBlocks/CashFlow.Web/
COPY src/Consolidation/CashFlow.Consolidation.Domain/CashFlow.Consolidation.Domain.csproj src/Consolidation/CashFlow.Consolidation.Domain/
COPY src/Consolidation/CashFlow.Consolidation.Application/CashFlow.Consolidation.Application.csproj src/Consolidation/CashFlow.Consolidation.Application/
COPY src/Consolidation/CashFlow.Consolidation.Infrastructure/CashFlow.Consolidation.Infrastructure.csproj src/Consolidation/CashFlow.Consolidation.Infrastructure/
COPY src/Consolidation/CashFlow.Consolidation.Api/CashFlow.Consolidation.Api.csproj src/Consolidation/CashFlow.Consolidation.Api/
RUN dotnet restore src/Consolidation/CashFlow.Consolidation.Api/CashFlow.Consolidation.Api.csproj

COPY src/ src/
RUN dotnet publish src/Consolidation/CashFlow.Consolidation.Api/CashFlow.Consolidation.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# The runtime image ships with no HTTP client at all, so a container healthcheck has nothing to
# call with. Installing curl is what makes the probe below real instead of always failing.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

# Runs as the image's non-root "app" user: nothing here needs elevated privileges.
USER app
EXPOSE 8080

# Liveness only: a broker outage must not make the orchestrator restart a healthy process.
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CashFlow.Consolidation.Api.dll"]

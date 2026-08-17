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

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

USER app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CashFlow.Consolidation.Api.dll"]

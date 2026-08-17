# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the files that affect the dependency graph, so editing source code
# does not invalidate the (slow) restore layer.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/CashFlow.SharedKernel/CashFlow.SharedKernel.csproj src/BuildingBlocks/CashFlow.SharedKernel/
COPY src/BuildingBlocks/CashFlow.Messaging/CashFlow.Messaging.csproj src/BuildingBlocks/CashFlow.Messaging/
COPY src/BuildingBlocks/CashFlow.Messaging.RabbitMq/CashFlow.Messaging.RabbitMq.csproj src/BuildingBlocks/CashFlow.Messaging.RabbitMq/
COPY src/BuildingBlocks/CashFlow.Web/CashFlow.Web.csproj src/BuildingBlocks/CashFlow.Web/
COPY src/Launches/CashFlow.Launches.Domain/CashFlow.Launches.Domain.csproj src/Launches/CashFlow.Launches.Domain/
COPY src/Launches/CashFlow.Launches.Application/CashFlow.Launches.Application.csproj src/Launches/CashFlow.Launches.Application/
COPY src/Launches/CashFlow.Launches.Infrastructure/CashFlow.Launches.Infrastructure.csproj src/Launches/CashFlow.Launches.Infrastructure/
COPY src/Launches/CashFlow.Launches.Api/CashFlow.Launches.Api.csproj src/Launches/CashFlow.Launches.Api/
RUN dotnet restore src/Launches/CashFlow.Launches.Api/CashFlow.Launches.Api.csproj

COPY src/ src/
RUN dotnet publish src/Launches/CashFlow.Launches.Api/CashFlow.Launches.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

# Runs as the image's non-root "app" user: nothing here needs elevated privileges.
USER app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CashFlow.Launches.Api.dll"]

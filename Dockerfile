FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ImmunizationSystem.slnx global.json Directory.Build.props ./
COPY src/ImmunizationSystem.Api/ImmunizationSystem.Api.csproj src/ImmunizationSystem.Api/
COPY tests/ImmunizationSystem.UnitTests/ImmunizationSystem.UnitTests.csproj tests/ImmunizationSystem.UnitTests/
COPY tests/ImmunizationSystem.IntegrationTests/ImmunizationSystem.IntegrationTests.csproj tests/ImmunizationSystem.IntegrationTests/
RUN dotnet restore ImmunizationSystem.slnx

COPY . .
RUN dotnet publish src/ImmunizationSystem.Api/ImmunizationSystem.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080

CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet ImmunizationSystem.Api.dll"]

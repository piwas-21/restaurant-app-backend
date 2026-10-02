# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY RestaurantSystem.Channels.Domain/RestaurantSystem.Channels.Domain.csproj RestaurantSystem.Channels.Domain/
COPY RestaurantSystem.Channels.Infrastructure/RestaurantSystem.Channels.Infrastructure.csproj RestaurantSystem.Channels.Infrastructure/
COPY RestaurantSystem.Channels.Api/RestaurantSystem.Channels.Api.csproj RestaurantSystem.Channels.Api/
RUN dotnet restore RestaurantSystem.Channels.Api/RestaurantSystem.Channels.Api.csproj
COPY RestaurantSystem.Channels.Domain/*.cs RestaurantSystem.Channels.Domain/
COPY RestaurantSystem.Channels.Infrastructure/*.cs RestaurantSystem.Channels.Infrastructure/
COPY RestaurantSystem.Channels.Infrastructure/Migrations/ RestaurantSystem.Channels.Infrastructure/Migrations/
COPY RestaurantSystem.Channels.Api/*.cs RestaurantSystem.Channels.Api/
COPY RestaurantSystem.Channels.Api/appsettings.json RestaurantSystem.Channels.Api/
COPY RestaurantSystem.Channels.Api/wwwroot/ RestaurantSystem.Channels.Api/wwwroot/
COPY RestaurantSystem.Channels.Api/Fixtures/ RestaurantSystem.Channels.Api/Fixtures/
RUN dotnet publish RestaurantSystem.Channels.Api/RestaurantSystem.Channels.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/RestaurantSystem.Channels.Infrastructure/Migrations/ /app/migrations/
USER $APP_UID
ARG GIT_SHA=unknown
ARG BUILD_TIME=unknown
ENV GIT_SHA=$GIT_SHA BUILD_TIME=$BUILD_TIME ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "RestaurantSystem.Channels.Api.dll"]

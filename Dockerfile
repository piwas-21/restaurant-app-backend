# syntax=docker/dockerfile:1.7

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files first for layer caching
COPY ["RestaurantSystem.Api/RestaurantSystem.Api.csproj", "RestaurantSystem.Api/"]
COPY ["RestaurantSystem.Domain/RestaurantSystem.Domain.csproj", "RestaurantSystem.Domain/"]
COPY ["RestaurantSystem.Infrastructure/RestaurantSystem.Infrastructure.csproj", "RestaurantSystem.Infrastructure/"]
COPY ["RestaurantSystem.ServiceDefaults/RestaurantSystem.ServiceDefaults.csproj", "RestaurantSystem.ServiceDefaults/"]

RUN dotnet restore "RestaurantSystem.Api/RestaurantSystem.Api.csproj"

# Copy everything else and publish
COPY . .
RUN dotnet publish "RestaurantSystem.Api/RestaurantSystem.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Create keys directory and chown to the non-root user (APP_UID is 64198 in .NET 10 images)
RUN mkdir -p /app/keys && chown -R $APP_UID:$APP_UID /app/keys

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "RestaurantSystem.Api.dll"]
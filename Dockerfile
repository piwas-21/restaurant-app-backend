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

# Create writable dirs for the non-root user (APP_UID is 64198 in .NET 10 images):
#   /app/keys           — DataProtection keys (PersistKeysToFileSystem)
#   /app/wwwroot/uploads — Local file-storage provider target (FileStorage:Provider=Local)
# Both are bind/named-volume mount points in production; pre-creating them owned by
# APP_UID lets the non-root process write (a fresh volume otherwise mounts root-owned).
RUN mkdir -p /app/keys /app/wwwroot/uploads \
    && chown -R $APP_UID:$APP_UID /app/keys /app/wwwroot

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "RestaurantSystem.Api.dll"]

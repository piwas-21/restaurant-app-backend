# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files first for layer caching
COPY ["RestaurantSystem.Api/RestaurantSystem.Api.csproj", "RestaurantSystem.Api/"]
COPY ["RestaurantSystem.Domain/RestaurantSystem.Domain.csproj", "RestaurantSystem.Domain/"]
COPY ["RestaurantSystem.Infrastructure/RestaurantSystem.Infrastructure.csproj", "RestaurantSystem.Infrastructure/"]

RUN dotnet restore "RestaurantSystem.Api/RestaurantSystem.Api.csproj"

# Copy source and publish
COPY . .
RUN dotnet publish "RestaurantSystem.Api/RestaurantSystem.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "RestaurantSystem.Api.dll"]
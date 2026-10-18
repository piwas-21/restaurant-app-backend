# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY Sofra.MobileService/Sofra.MobileService.csproj Sofra.MobileService/
RUN dotnet restore Sofra.MobileService/Sofra.MobileService.csproj
COPY Sofra.MobileService/*.cs Sofra.MobileService/
COPY Sofra.MobileService/Directory/*.cs Sofra.MobileService/Directory/
RUN dotnet publish Sofra.MobileService/Sofra.MobileService.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sofra.MobileService.dll"]

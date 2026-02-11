FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Backend/*.csproj ./Backend/
RUN dotnet restore "./Backend/CryptoFutureMonitor.csproj"

COPY Backend/. ./Backend/
WORKDIR "/src/Backend"
RUN dotnet build "CryptoFutureMonitor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CryptoFutureMonitor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Default port 10000 for Render, fallback to 5000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=publish /app/publish .
COPY Frontend/. ./wwwroot/

ENTRYPOINT ["dotnet", "CryptoFutureMonitor.dll"]

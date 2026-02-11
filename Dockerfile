FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY Backend/*.csproj ./Backend/
RUN dotnet restore "./Backend/CryptoFutureMonitor.csproj"

# Copy everything else and build
COPY Backend/. ./Backend/
WORKDIR "/src/Backend"
RUN dotnet build "CryptoFutureMonitor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CryptoFutureMonitor.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5000

# Set environment variables
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=publish /app/publish .

# Copy Frontend files to wwwroot
COPY Frontend/. ./wwwroot/

ENTRYPOINT ["dotnet", "CryptoFutureMonitor.dll"]

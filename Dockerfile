#build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/AssetForge.Core/AssetForge.Core.csproj src/AssetForge.Core/
COPY src/AssetForge.Service/AssetForge.Service.csproj src/AssetForge.Service/

RUN dotnet restore src/AssetForge.Service/AssetForge.Service.csproj

COPY src/ src/

RUN dotnet publish src/AssetForge.Service/AssetForge.Service.csproj \
    --configuration Release \
    --no-restore \
    --output /app

#runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Output and cache live on a mounted volume
RUN mkdir -p /data/output && chown -R $APP_UID /data

COPY --from=build /app .

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AssetForge.Service.dll"]

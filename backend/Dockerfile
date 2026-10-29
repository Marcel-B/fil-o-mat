# Baut aus dem Wurzelordner des Repos (siehe docker-compose.yml), damit die Web-Oberfläche aus web/ mit hineinkommt
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/src/Filomat.Api/Filomat.Api.csproj src/Filomat.Api/
RUN dotnet restore src/Filomat.Api/Filomat.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Filomat.Api/Filomat.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Filomat="Data Source=/data/filomat.db" \
    Filomat__UpdateDirectory=/update
# Das Volume übernimmt beim ersten Anlegen den Besitzer des Ordners; ohne das kann die App (kein root) die Datenbank nicht schreiben
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Filomat.Api.dll"]

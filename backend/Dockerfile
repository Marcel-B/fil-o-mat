FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Filomat.Api/Filomat.Api.csproj src/Filomat.Api/
RUN dotnet restore src/Filomat.Api/Filomat.Api.csproj
COPY src/ src/
RUN dotnet publish src/Filomat.Api/Filomat.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Filomat="Data Source=/data/filomat.db"
# Das Volume übernimmt beim ersten Anlegen den Besitzer des Ordners; ohne das kann die App (kein root) die Datenbank nicht schreiben
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Filomat.Api.dll"]

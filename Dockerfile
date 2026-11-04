# syntax=docker/dockerfile:1
# Tags are explicit; resolve/pin tested digests before a real release.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY src/MglConnect.Web/MglConnect.Web.csproj src/MglConnect.Web/
RUN dotnet restore src/MglConnect.Web/MglConnect.Web.csproj
COPY src/MglConnect.Web/ src/MglConnect.Web/
RUN dotnet publish src/MglConnect.Web/MglConnect.Web.csproj --no-restore -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
# PDF native font dependencies; installation is inside the image, never on the developer host.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /app/keys \
    && chown -R "$APP_UID:$APP_UID" /app/data /app/keys
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DatabaseProvider=Sqlite \
    ConnectionStrings__DefaultConnection="Data Source=/app/data/mglconnect.db;Foreign Keys=True" \
    DataProtection__KeysPath=/app/keys \
    DemoData__Enabled=false
USER $APP_UID
EXPOSE 8080
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=6s --start-period=60s --retries=3 \
    CMD ["dotnet", "MglConnect.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "MglConnect.Web.dll"]

#!/usr/bin/env bash
set -euo pipefail
umask 077

# Local SQLite development only. No production URL or inherited provider is used.
project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
data_root="${MGL_LOCAL_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/mglconnect/local-preview}"
local_port="${MGL_LOCAL_PORT:-5284}"
if [[ "${MGL_DEMO_ROUTES:-false}" != true && "${MGL_DEMO_ROUTES:-false}" != false ]]; then
    echo 'MGL_DEMO_ROUTES deve ser true ou false.' >&2
    exit 1
fi
if [[ ! "$local_port" =~ ^[0-9]+$ ]] || (( local_port < 1024 || local_port > 65535 )); then
    echo 'MGL_LOCAL_PORT deve estar entre 1024 e 65535.' >&2
    exit 1
fi
if [[ -x "$HOME/.local/share/mglconnect-dotnet10/dotnet" ]]; then
    dotnet_bin="$HOME/.local/share/mglconnect-dotnet10/dotnet"
    export DOTNET_ROOT="$HOME/.local/share/mglconnect-dotnet10"
elif command -v dotnet >/dev/null 2>&1; then
    dotnet_bin="$(command -v dotnet)"
else
    echo 'Instale/configure o SDK .NET 10 para executar o projeto.' >&2
    exit 1
fi
if [[ -f "$data_root/mglconnect.db" && ! -f "$data_root/.mgl-local-preview" ]]; then
    echo 'Banco preexistente sem marcador de demonstração. Use outro MGL_LOCAL_DATA_DIR.' >&2
    exit 1
fi
mkdir -p -- "$data_root/keys"
touch -- "$data_root/.mgl-local-preview"
"$dotnet_bin" build "$project_root/MglConnect.sln" --verbosity minimal
echo "M.G.L Connect local: http://127.0.0.1:$local_port"
echo "Banco de desenvolvimento: $data_root/mglconnect.db"
exec env -u POSTGRES_URL -u DATABASE_URL -u POSTGRES_PRISMA_URL -u PORT -u VERCEL -u GOOGLE_MAPS_API_KEY \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://127.0.0.1:$local_port" \
    DatabaseProvider=Sqlite \
    ConnectionStrings__DefaultConnection="Data Source=$data_root/mglconnect.db;Foreign Keys=True" \
    DataProtection__KeysPath="$data_root/keys" \
    RoutingDemo__Enabled="${MGL_DEMO_ROUTES:-false}" \
    RoutingDemo__Failure="${MGL_DEMO_ROUTE_FAILURE:-}" \
    DemoData__Enabled=true \
    DemoData__AdminPassword=admin123 \
    DemoData__DriverPassword=motorista123 \
    DemoData__ClientPassword=cliente123 \
    Geocoding__PhotonEnabled=false Routing__OsrmBaseUrl= \
    GoogleMaps__ApiKey= GoogleMaps__GeocodingApiKey= \
    "$dotnet_bin" "$project_root/src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll" \
    --contentRoot "$project_root/src/MglConnect.Web"

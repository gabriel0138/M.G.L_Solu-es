#!/usr/bin/env bash
set -euo pipefail
umask 077
project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
pg_bin="${MGL_POSTGRES_BIN:-/usr/lib/postgresql/18/bin}"
dotnet_bin="${MGL_DOTNET_BIN:-$HOME/.local/share/mglconnect-dotnet10/dotnet}"
test_parent="${MGL_TEST_ARTIFACTS_ROOT:-${TMPDIR:-/tmp}}"
mkdir -p -- "$test_parent"
test_root="$(mktemp -d "$test_parent/mgl-stage6-pg-XXXXXXXX")"
mkdir "$test_root/socket"
"$pg_bin/initdb" -D "$test_root/cluster" -U mgl_fixture --auth-local=trust --auth-host=reject --no-locale -E UTF8 > "$test_root/init.log"
cleanup() { "$pg_bin/pg_ctl" -D "$test_root/cluster" -m fast -w stop > "$test_root/stop.log"; }
trap cleanup EXIT
"$pg_bin/pg_ctl" -D "$test_root/cluster" -l "$test_root/server.log" -o "-k $test_root/socket -p 55439 -c listen_addresses=''" -w start
echo "PostgreSQL isolado; evidências preservadas: $test_root"
export MGLCONNECT_POSTGRES_TEST_CONNECTION="Host=$test_root/socket;Port=55439;Username=mgl_fixture;Database=postgres;Pooling=false"
if (( $# == 0 )); then set -- --filter 'FullyQualifiedName~PostgresIntegrityTests'; fi
"$dotnet_bin" test "$project_root/MglConnect.sln" --no-restore "$@"

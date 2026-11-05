#!/usr/bin/env bash
# Explicit, disposable OSM/OSRM preparation. Never touches application databases.
set -euo pipefail
umask 077
command=${1:-probe}
osrm_dir=${MGL_OSRM_DATA_DIR:-$HOME/.local/share/mglconnect/osrm-delivery4}
port=${MGL_OSRM_PORT:-5007}
[[ "$port" =~ ^[0-9]+$ ]] && ((port>=1024 && port<=65535)) || { echo 'Invalid local port' >&2;exit 1; }
image=ghcr.io/project-osrm/osrm-backend@sha256:98f7d637b703fb165a9f3f0381d31c008e3dcfc3929cdc18b885b0162bf233e2
run_engine() {
  if docker info >/dev/null 2>&1; then
    docker run --rm --cpus=4 --memory=2g -v "$osrm_dir:/data" "$image" "$@"
  else
    test -f "$osrm_dir/rootfs/usr/local/bin/osrm-routed" || { echo 'Run prepare first (requires Python 3.12+ and bubblewrap).' >&2;exit 1; }
    bwrap --unshare-user --unshare-pid --die-with-parent --new-session --uid 0 --gid 0 --cap-drop ALL --clearenv --setenv PATH /usr/local/bin:/usr/bin:/bin \
      --ro-bind "$osrm_dir/rootfs" / --bind "$osrm_dir" /data --proc /proc --dev /dev --tmpfs /tmp "/usr/local/bin/$1" "${@:2}"
  fi
}
case "$command" in
 prepare)
    mkdir -p "$osrm_dir"
    python3 "$(dirname "$0")/prepare-local-osrm.py" "$osrm_dir"
    run_engine osrm-extract -t 4 -p /opt/car.lua /data/central-sp.osm
    run_engine osrm-partition -t 4 /data/central-sp.osrm
    run_engine osrm-customize -t 4 /data/central-sp.osrm
    ;;
 serve)
    if docker info >/dev/null 2>&1; then
      exec docker run --rm --cpus=2 --memory=1g -p "127.0.0.1:$port:5000" -v "$osrm_dir:/data:ro" "$image" osrm-routed --algorithm mld -t 2 /data/central-sp.osrm
    fi
    run_engine osrm-routed --algorithm mld --ip 127.0.0.1 --port "$port" -t 2 /data/central-sp.osrm
    ;;
 probe)
    curl --fail --silent --show-error "http://127.0.0.1:$port/route/v1/driving/-46.6559,-23.5614;-46.6333,-23.5505?overview=full&geometries=geojson&steps=true&alternatives=true"
    ;;
 *) echo 'Usage: local-osrm.sh prepare|serve|probe' >&2;exit 1;;
esac

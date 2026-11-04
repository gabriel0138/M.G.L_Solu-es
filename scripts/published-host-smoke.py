#!/usr/bin/env python3
"""Run a published .NET package on loopback with an exclusively owned SQLite fixture; no Docker or deployment."""
import argparse
import json
import os
from pathlib import Path
import signal
import socket
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--package", type=Path, required=True)
parser.add_argument("--dotnet", type=Path, required=True)
args = parser.parse_args()
package = args.package.resolve()
if not (package / "MglConnect.Web.dll").is_file():
    parser.error("Publish the application first")
directory = Path(tempfile.mkdtemp(prefix="mgl-8a-published-"))
os.chmod(directory, 0o700)
secrets_dir = directory / "secrets"
subprocess.run(["python3", str(Path(__file__).with_name("create-demo-secrets.py")), str(secrets_dir)], check=True, stdout=subprocess.DEVNULL)
with socket.socket() as probe:
    probe.bind(("127.0.0.1", 0))
    port = probe.getsockname()[1]
base = "http://127.0.0.1:" + str(port)
environment = dict(os.environ)
for name in list(environment):
    if name in ["POSTGRES_URL", "POSTGRES_PRISMA_URL", "DATABASE_URL", "PORT", "VERCEL", "GOOGLE_MAPS_API_KEY", "ASPNETCORE_FORWARDEDHEADERS_ENABLED"] or name.startswith(("Hosting__", "ReverseProxy__")):
        environment.pop(name)
environment.update({"ASPNETCORE_ENVIRONMENT": "Production", "ASPNETCORE_URLS": base, "DatabaseProvider": "Sqlite",
                    "ConnectionStrings__DefaultConnection": "Data Source=" + str(directory / "database.sqlite") + ";Foreign Keys=True",
                    "DataProtection__KeysPath": str(directory / "keys"), "Hosting__SecretsPath": str(secrets_dir), "DemoData__Enabled": "true",
                    "Geocoding__PhotonEnabled": "false", "Routing__OsrmBaseUrl": "", "Routing__Valhalla__Enabled": "false",
                    "GoogleMaps__ApiKey": "", "GoogleMaps__GeocodingApiKey": ""})
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
process = None
log = (directory / "server.log").open("ab")
def request(path, data=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    r = urllib.request.Request(base + path, json.dumps(data).encode() if data is not None else None, headers=headers)
    return opener.open(r, timeout=5)
def start():
    global process
    process = subprocess.Popen([str(args.dotnet), str(package / "MglConnect.Web.dll"), "--contentRoot", str(package)], env=environment, cwd=package, stdout=log, stderr=log)
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError("Owned published server exited; inspect private server.log")
        try:
            with request("/health/ready") as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, TimeoutError):
            pass
        time.sleep(0.1)
    raise RuntimeError("Owned published server readiness timed out")
def stop():
    global process
    if process and process.poll() is None:
        before = time.monotonic()
        process.send_signal(signal.SIGTERM)
        process.wait(timeout=25)
        assert process.returncode == 0, "Published host must handle SIGTERM normally"
        return round((time.monotonic() - before) * 1000)
try:
    start()
    for path in ["/health/live", "/health/ready"]:
        with request(path) as response:
            assert response.status == 200 and "no-store" in response.headers["Cache-Control"]
    assert subprocess.run([str(args.dotnet), str(package / "MglConnect.Web.dll"), "--healthcheck", base + "/health/ready"], stdout=subprocess.DEVNULL).returncode == 0
    with request("/api/auth/login", {"Login": "admin", "Senha": (secrets_dir / "DemoData__AdminPassword").read_text()}) as response:
        token = json.load(response)["token"]
    with sqlite3.connect(directory / "database.sqlite") as database:
        completed = database.execute('SELECT Id FROM Entregas WHERE DataHoraRecebimento IS NOT NULL').fetchone()[0]
        driver = database.execute("SELECT Id FROM Usuarios WHERE Login='carlos.motorista'").fetchone()[0]
        initial_users = database.execute("SELECT COUNT(*) FROM Usuarios").fetchone()[0]
    with request(f"/api/entregas/{completed}/pod-pdf", token=token) as response:
        assert response.status == 200 and response.read(4) == b"%PDF", "Published native PDF/font dependencies must work"
    with request("/api/auth/login", {"Login": "carlos.motorista", "Senha": (secrets_dir / "DemoData__DriverPassword").read_text()}) as response:
        driver_token = json.load(response)["token"]
    with request(f"/api/entregas/motorista/{driver}", token=driver_token) as response:
        assert response.status == 200 and isinstance(json.load(response), list)
    try:
        request(f"/api/entregas/motorista/{driver + 9999}", token=driver_token)
        raise AssertionError("Foreign driver must be denied")
    except urllib.error.HTTPError as error:
        assert error.code == 403
    stopped_ms = stop()
    assert subprocess.run([str(args.dotnet), str(package / "MglConnect.Web.dll"), "--healthcheck", base + "/health/ready"], stdout=subprocess.DEVNULL).returncode == 1
    start()
    with request(f"/api/entregas/{completed}/pod-pdf", token=token) as response:
        assert response.status == 200 and response.read(4) == b"%PDF", "Keys and database must survive normal restart"
    with sqlite3.connect(directory / "database.sqlite") as database:
        assert database.execute("SELECT COUNT(*) FROM Usuarios").fetchone()[0] == initial_users
    result = {"mode": "published-native-not-container", "passed": True, "checks": ["health", "health-cli-exit-codes", "mounted-secret-login", "driver-api", "foreign-driver-denied", "native-pdf", "sigterm", "persistent-db-and-keys-restart"], "gracefulStopMs": stopped_ms, "evidence": str(directory), "dockerExecuted": False, "cloudDeployed": False}
    (directory / "evidence.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result))
finally:
    stop()
    log.close()

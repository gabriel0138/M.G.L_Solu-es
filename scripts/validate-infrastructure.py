#!/usr/bin/env python3
"""Static checks only. Does NOT assert that an image built, a container ran or a deployment exists."""
import json
from pathlib import Path
import re
import yaml

root = Path(__file__).resolve().parent.parent
checks = []
def check(condition, name):
    if not condition:
        raise ValueError("Static infrastructure validation failed: " + name)
    checks.append(name)

docker = (root / "Dockerfile").read_text()
compose = yaml.safe_load((root / "docker-compose.yml").read_text())
sql = yaml.safe_load((root / "deploy/compose.sqlserver.yml").read_text())
ignore = (root / ".dockerignore").read_text()
web = compose["services"]["mglconnect-web"]
check(len(re.findall(r"^FROM ", docker, re.M)) == 2 and "sdk:10.0-noble" in docker and "aspnet:10.0-noble" in docker, "two-stage .NET 10 build/runtime")
check("USER $APP_UID" in docker and 'ENTRYPOINT ["dotnet", "MglConnect.Web.dll"]' in docker, "non-root runtime and exec entrypoint")
check("--healthcheck" in docker and "STOPSIGNAL SIGTERM" in docker and web["stop_grace_period"] == "30s", "health and controlled termination configured")
check(web["read_only"] and web["cap_drop"] == ["ALL"] and web["security_opt"] == ["no-new-privileges:true"], "restricted runtime configured")
check(all(p.startswith("127.0.0.1:") for p in web["ports"]) and "container_name" not in web, "loopback demo and isolated Compose naming")
check(set(web["volumes"]) == {"mgl_data:/app/data", "mgl_keys:/app/keys"}, "data and authentication key volumes")
check(len(web["secrets"]) == 3 and not any("Password" in k for k in web["environment"]), "demo passwords supplied as mounted files")
check("**/appsettings*.json" in ignore and "**/.env*" in ignore and "**/*.db*" in ignore and ignore.splitlines()[1] == "**", "build-context allowlist excludes configuration and host data")
check("ports" not in sql["services"]["sqlserver"] and sql["services"]["sqlserver"]["environment"]["MSSQL_PID"] == "Developer", "optional SQL Server demo is private")
check("${MGL_SQLSERVER_ACCEPT_EULA:?" in sql["services"]["sqlserver"]["environment"]["ACCEPT_EULA"], "explicit SQL Server license acceptance required")
check(sql["services"]["mglconnect-web"]["depends_on"]["sqlserver"]["condition"] == "service_healthy", "SQL Server readiness dependency")
check(all("${MGL_SECRETS_DIR:?" in entry["file"] for entry in compose["secrets"].values()), "secret source paths are required, not embedded values")
print(json.dumps({"mode": "static-only", "passed": len(checks), "checks": checks, "imageBuild": "not-validated", "containerExecution": "not-validated", "cloudDeployment": "not-performed"}, indent=2))

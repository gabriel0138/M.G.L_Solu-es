#!/usr/bin/env python3
"""Generate synthetic demo credentials in a NEW private directory. Never prints values."""
import argparse
import json
import os
from pathlib import Path
import secrets

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path)
parser.add_argument("--sqlserver", action="store_true", help="Also generate credentials for an isolated Developer SQL Server demo")
args = parser.parse_args()
destination = args.directory.expanduser().absolute()
destination.mkdir(mode=0o700, parents=True, exist_ok=False)
os.chmod(destination, 0o700)
values = {"DemoData__" + role + "Password": "Mgl8A!" + secrets.token_urlsafe(30) for role in ["Admin", "Driver", "Client"]}
if args.sqlserver:
    password = "Mgl8A!" + secrets.token_urlsafe(30)
    values["SqlServerSaPassword"] = password
    values["ConnectionStrings__DefaultConnection"] = "Server=sqlserver;Database=MglConnectDemo;User Id=sa;Password=" + password + ";Encrypt=True;TrustServerCertificate=True"
for name, value in values.items():
    target = destination / name
    with target.open("x", encoding="utf-8") as output:
        output.write(value)
    # Compose file secrets retain host ownership. The private parent prevents host access;
    # the mounted leaf must be readable by the non-root container UID.
    os.chmod(target, 0o444)
print(json.dumps({"directory": str(destination), "files": list(values), "synthetic": True, "valuesPrinted": False}))

#!/usr/bin/env python3
"""Offline, bounded OSM extract import. Does not call public APIs or overwrite files."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("input", type=Path, help="Previously downloaded public OSM map XML")
parser.add_argument("output", type=Path, help="New JSON file; must not already exist")
parser.add_argument("--captured-at", required=True, help="UTC capture timestamp with explicit timezone")
args = parser.parse_args()
capture = datetime.datetime.fromisoformat(args.captured_at)
if capture.utcoffset() != datetime.timedelta(0):
    parser.error("Capture timestamp must explicitly use UTC")
if args.input.stat().st_size > 2 * 1024 * 1024:
    parser.error("Public sample exceeds 2 MiB")
raw = args.input.read_bytes()
if b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper():
    parser.error("DTD/entity declarations are not accepted")
root = ET.fromstring(raw)
bounds = root.find("bounds")
if bounds is None:
    parser.error("Explicit public sample bounds required")
bbox = [float(bounds.attrib[key]) for key in ["minlon", "minlat", "maxlon", "maxlat"]]
if bbox != [-46.6575, -23.5635, -46.6540, -23.5600]:
    parser.error("This importer is restricted to the documented MASP pilot bbox")
keys = {"highway", "maxheight", "maxheight:physical", "maxweight", "maxwidth", "maxlength", "maxaxleload", "maxaxles", "hgv", "goods", "access", "motor_vehicle", "vehicle", "hazmat", "bridge", "tunnel", "oneway"}
allowed = {"motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential", "service", "living_street", "road", "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link"}
ways = []
for way in root.findall("way"):
    tags = {t.attrib["k"]: t.attrib["v"] for t in way.findall("tag")}
    if tags.get("highway") not in allowed:
        continue
    ways.append({"id": way.attrib["id"], "version": way.attrib.get("version"), "timestamp": way.attrib.get("timestamp"),
                 "nodes": [int(n.attrib["ref"]) for n in way.findall("nd")],
                 "tags": {k: v for k, v in tags.items() if k in keys or k.endswith(":conditional") and k[:-12] in keys}})
data = {"territory": "Piloto MASP / Bela Vista, São Paulo — bbox pequeno, não cobertura do Brasil", "bbox": bbox,
        "source": "https://api.openstreetmap.org/api/0.6/map?bbox=-46.6575,-23.5635,-46.6540,-23.5600",
        "capturedAtUtc": capture.isoformat(), "responseSha256": hashlib.sha256(raw).hexdigest(),
        "attribution": "© OpenStreetMap contributors — ODbL 1.0, https://www.openstreetmap.org/copyright",
        "selection": "Somente ways highway motorizáveis. Nós ordenados e versões OSM; não associados a grafo Valhalla/OSRM. Sem conversão de tags em autorização legal.", "ways": ways}
with args.output.open("x", encoding="utf-8") as output:
    json.dump(data, output, ensure_ascii=False, indent=2)
    output.write("\n")
print(f"{len(ways)} vias; SHA256 da resposta original: {data['responseSha256']}")

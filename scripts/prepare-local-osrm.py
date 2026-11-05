#!/usr/bin/env python3
"""Download four bounded OSM public-map quadrants and a digest-pinned official OSRM image.
Files stay in the explicit disposable directory. Requires Python >=3.12 for safe tar filters.
Never downloads a planet, installs packages or changes Docker/group permissions.
"""
from pathlib import Path
import urllib.request,xml.etree.ElementTree as E,json,datetime,hashlib,sys
base=Path(sys.argv[1]).resolve();base.mkdir(parents=True,exist_ok=True)
boxes=[(-46.658,-23.564,-46.645,-23.556),(-46.645,-23.564,-46.632,-23.556),(-46.658,-23.556,-46.645,-23.548),(-46.645,-23.556,-46.632,-23.548)]
items={};sources=[];downloaded=False
for i,box in enumerate(boxes):
 u='https://api.openstreetmap.org/api/0.6/map?bbox='+','.join(map(str,box));p=base/f'tile{i}.osm';sources.append(u)
 if not p.exists():
  downloaded=True
  print('download',i,flush=True);p.write_bytes(urllib.request.urlopen(urllib.request.Request(u,headers={'User-Agent':'MglConnect-LocalResearch/4.0'}),timeout=90).read())
 for item in E.parse(p).getroot():
  if item.tag in ('node','way','relation'):items[(item.tag,item.get('id'))]=item
root=E.Element('osm',version='0.6',generator='MGL controlled extract of OSM API public map')
# Keep highway ways with all original node references/tags. Nodes are unchanged OSM observations.
ways=[v for (t,k),v in items.items() if t=='way' and any(x.tag=='tag' and x.get('k')=='highway' for x in v)]
refs={x.get('ref') for w in ways for x in w if x.tag=='nd'}
for n in sorted([v for (t,k),v in items.items() if t=='node' and k in refs],key=lambda x:int(x.get('id'))):root.append(n)
for w in sorted(ways,key=lambda x:int(x.get('id'))):root.append(w)
# Turn-restriction relations whose referenced highway ways/nodes are present.
ids={w.get('id') for w in ways}
for (t,k),r in sorted(items.items(),key=lambda item:(item[0][0],int(item[0][1]))):
 if t=='relation' and any(x.tag=='tag' and x.get('k')=='type' and x.get('v')=='restriction' for x in r):
  if all(x.get('ref') in (ids if x.get('type')=='way' else refs) for x in r if x.tag=='member'):root.append(r)
p=base/'central-sp.osm';E.ElementTree(root).write(p,encoding='utf-8',xml_declaration=True)
manifest={'sourceUrls':sources,'downloadedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat() if downloaded else None,'preparedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'license':'ODbL 1.0','attribution':'© OpenStreetMap contributors','bbox':[-46.658,-23.564,-46.632,-23.548],'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'nodes':len(refs),'highwayWays':len(ways),'filter':'original highway ways, referenced nodes and available complete turn restrictions; small incomplete boundary, generic car profile only'}
existing=base/'dataset.json'
if existing.exists():
 old=json.loads(existing.read_text())
 if not downloaded and old.get('sourceUrls')==sources: manifest['downloadedAtUtc']=old.get('downloadedAtUtc')
(base/'dataset.json').write_text(json.dumps(manifest,indent=2));print(json.dumps(manifest),flush=True)

# Rootless image extraction for environments without daemon access.
import tarfile,subprocess
try:
    docker_ok=subprocess.run(['docker','info'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0
except FileNotFoundError:
    docker_ok=False
if docker_ok:
    sys.exit(0)
root=base/'rootfs';root.mkdir(exist_ok=True)
repo='project-osrm/osrm-backend';registry='ghcr.io'
t=json.load(urllib.request.urlopen('https://ghcr.io/token?service=ghcr.io&scope=repository:'+repo+':pull'))['token']
def get(p,accept=None):
 return urllib.request.urlopen(urllib.request.Request('https://'+registry+'/v2/'+repo+'/'+p,headers={'Authorization':'Bearer '+t,**({'Accept':accept} if accept else {})}),timeout=90)
a='application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json'
b=get('manifests/sha256:98f7d637b703fb165a9f3f0381d31c008e3dcfc3929cdc18b885b0162bf233e2',a).read();m=json.loads(b)
if 'manifests' in m:
 d=next(x['digest'] for x in m['manifests'] if x.get('platform',{}).get('architecture')=='amd64' and x['platform']['os']=='linux');b=get('manifests/'+d,a).read();m=json.loads(b)
assert hashlib.sha256(b).hexdigest()=='98f7d637b703fb165a9f3f0381d31c008e3dcfc3929cdc18b885b0162bf233e2'
(base/'manifest.json').write_bytes(b);print('manifest',hashlib.sha256(b).hexdigest(),flush=True)
for layer in m['layers']:
 digest=layer['digest'];p=base/(digest.split(':')[1]+'.tar.gz')
 if not p.exists():
  with get('blobs/'+digest) as r,p.open('wb') as f:
   while chunk:=r.read(1024*1024): f.write(chunk)
 assert hashlib.sha256(p.read_bytes()).hexdigest()==digest.split(':')[1]
 print('extract',p.stat().st_size,flush=True)
 with tarfile.open(p) as archive: archive.extractall(root,filter='tar')

(root/'data').mkdir(exist_ok=True)

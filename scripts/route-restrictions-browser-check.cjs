// Complete browser journey on an exclusively owned temporary SQLite database.
// Four Chromium profiles keep administrator, customer, driver and public cookies separate.
const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {mkdtempSync,createWriteStream} = require('node:fs');
const {tmpdir} = require('node:os');
const {join,resolve} = require('node:path');
const net = require('node:net');
const {createBrowserClient} = require('./command-center-browser-check.cjs');
const directory = mkdtempSync(join(tmpdir(),'mgl-stage7a-browser-'));
const pause = ms => new Promise(resolve=>setTimeout(resolve,ms));
let server;
const experimental = process.env.MGL_VALHALLA_CONTRACT === '1';
const controlledServers = [];
let providerMode = 'success', providerRequests = [], photonUrl, providerUrl;
let profiles = {};
let stage = 'start isolated server';
const errors = [];
async function until(client, expression, label=expression, timeout=20000) {
    const deadline=Date.now()+timeout;
    while(Date.now()<deadline) {
        try { if(await client.evaluate(expression)) return; }
        catch(error) { if(!/context.*destroyed|cannot find context/i.test(error.message)) throw error; }
        await pause(100);
    }
    throw new Error('Journey timed out: '+label);
}
async function startServer(reusePort) {
    const port=reusePort || await new Promise((resolve,reject)=>{
        const probe=net.createServer();probe.once('error',reject);
        probe.listen(0,'127.0.0.1',()=>{const port=probe.address().port;probe.close(()=>resolve(port));});
    });
    const log=createWriteStream(join(directory,'server.log'),{flags:'a'});
    const environment={...process.env,
        MGL_LOCAL_PORT:String(port),MGL_LOCAL_DATA_DIR:join(directory,'database'),GpsHistory__DiasAntesCompactar:'0',RouteTrust__FixturePath:join(directory,'route-evidence.json')},stdio=['ignore','pipe','pipe'];
    if (experimental) {
        const fs=require('node:fs'),data=join(directory,'database');fs.mkdirSync(join(data,'keys'),{recursive:true,mode:0o700});fs.writeFileSync(join(data,'.mgl-local-preview'),'synthetic-owned-browser-test');
        Object.assign(environment,{ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:'http://127.0.0.1:'+port,
            DatabaseProvider:'Sqlite',ConnectionStrings__DefaultConnection:'Data Source='+join(data,'mglconnect.db')+';Foreign Keys=True',DataProtection__KeysPath:join(data,'keys'),
            DemoData__Enabled:'true',DemoData__AdminPassword:'admin123',DemoData__DriverPassword:'motorista123',DemoData__ClientPassword:'cliente123',
            Geocoding__PhotonEnabled:'true',Geocoding__PhotonBaseUrl:photonUrl,Routing__OsrmBaseUrl:'',Routing__Valhalla__Enabled:'true',Routing__Valhalla__BaseUrl:providerUrl,
            GoogleMaps__ApiKey:'',GoogleMaps__GeocodingApiKey:'',SSL_CERT_FILE:join(directory,'fixture-cert.pem'),DOTNET_ROOT:join(require('node:os').homedir(),'.local/share/mglconnect-dotnet10')});
        for(const key of ['POSTGRES_URL','DATABASE_URL','POSTGRES_PRISMA_URL','PORT','VERCEL','GOOGLE_MAPS_API_KEY','RouteTrust__FixturePath'])delete environment[key];
        server=spawn(join(environment.DOTNET_ROOT,'dotnet'),[resolve(__dirname,'../src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll'),'--contentRoot',resolve(__dirname,'../src/MglConnect.Web')],{cwd:resolve(__dirname,'..'),env:environment,stdio});
    } else server=spawn('bash',['scripts/dev-local.sh'],{cwd:resolve(__dirname,'..'),env:environment,stdio});
    await new Promise((resolve,reject)=>{
        const timer=setTimeout(()=>reject(new Error('Owned development server did not start.')),60000);
        server.on('error',reject);
        server.on('exit',code=>{clearTimeout(timer);reject(new Error('Owned server exited before readiness: '+code));});
        let output='';
        server.stdout.on('data',chunk=>{log.write(chunk);output+=chunk;
            if(output.includes('Now listening on: http://127.0.0.1:'+port)){clearTimeout(timer);resolve();}
        });
        server.stderr.on('data',chunk=>log.write(chunk));
    });
    return 'http://127.0.0.1:'+port;
}
async function login(client,name,password,landing) {
    await client.navigate('/Account/Login');
    assert.ok(await client.evaluate(`document.querySelector('.cc-demo-access')?.textContent.includes(${JSON.stringify(name)})`),'Use only the owned Development demo.');
    await client.evaluate(`document.getElementById('Login').value=${JSON.stringify(name)};document.getElementById('Senha').value=${JSON.stringify(password)};document.querySelector('.cc-login-form').requestSubmit()`);
    await until(client,`location.pathname===${JSON.stringify(landing)} && document.readyState==='complete'`,name+' login');
}
async function issueCode(id) {
    const client=profiles.admin;
    await client.navigate('/Entregas/Details/'+id);
    const code=await client.evaluate(`(async()=>{
        const form=[...document.forms].find(form=>new URL(form.action).pathname==='/Entregas/EmitirCodigoTurno');
        if(!form?.querySelector('[name="__RequestVerificationToken"]')) throw new Error('Missing actual code form or CSRF.');
        const response=await fetch(form.action,{method:'POST',credentials:'same-origin',body:new FormData(form)});
        if(!response.ok) throw new Error('Code was not issued: '+response.status);
        return new DOMParser().parseFromString(await response.text(),'text/html').querySelector('output[aria-label="Código temporário"]')?.textContent.trim();
    })()`);
    assert.ok(code,'A real server-issued shift code is required.');return code;
}
async function startShift(id,code) {
    const client=profiles.driver;
    await client.navigate('/Entregas');
    await client.evaluate(`(()=>{
        const form=[...document.forms].find(form=>new URL(form.action).pathname==='/Entregas/IniciarTurno' && form.elements.entregaId?.value===${JSON.stringify(String(id))});
        if(!form) throw new Error('Assigned trip was not offered to the driver.');
        form.elements.codigo.value=${JSON.stringify(code)};form.requestSubmit();
    })()`);
    await until(client,`location.pathname==='/Entregas/Navegar/${id}' && document.readyState==='complete'`,'Driver navigates after validating shift');
    await until(client,`document.getElementById('telemetryConnectionState')?.dataset.state==='connected'`,'Authorized GPS hub');
    assert.equal(await client.evaluate(`!!document.getElementById('btnSimular')`),false);
}
async function rows() {
    return profiles.driver.evaluate(`(async()=>{
        const store=new MglIndexedGpsStore();const db=await store.open();
        return new Promise((resolve,reject)=>{
            const tx=db.transaction('events','readonly');const request=tx.objectStore('events').getAll();
            request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);
        });
    })()`);
}
async function history() {return profiles.admin.evaluate(`fetch('/api/gps/historico/1',{cache:'no-store'}).then(async r=>{if(!r.ok)throw new Error('History denied '+r.status);return r.json();})`);}
async function capture(position) {await profiles.driver.evaluate(`window.mglGpsOutbox.capture(${JSON.stringify(position)})`);}
async function matrix(client,path,label) {
    await client.navigate(path);
    if(label.includes('history'))await until(client,`document.querySelector('[data-gps-history-status]')?.textContent.includes('pontos nesta página')`,'Confirmed history map loads');
    for(const theme of ['dark','light']) {
        await client.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)};document.documentElement.style.colorScheme=${JSON.stringify(theme)}`);
        for(const width of [1440,1280,768,390,360]) {
            await client.viewport(width,width<500?844:1000);
            assert.ok(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),label+' horizontal overflow at '+width);
            if(label==='driver') {
                const layout=await client.evaluate(`(()=>{const r=document.querySelector('[data-bs-target="#driverEmergencyModal"]').getBoundingClientRect();return{top:r.top,bottom:r.bottom,height:document.documentElement.scrollHeight,viewport:innerHeight};})()`);
                assert.ok(layout.height<=layout.viewport+1,'GPS state must not push cockpit below viewport');
                assert.ok(layout.top>=0&&layout.bottom<=layout.viewport+1,'Emergency must remain inside viewport');
            }
            await client.screenshot('gps-'+label+'-'+theme+'-'+width);
        }
    }
}
async function drawAndSubmitPod(id,name) {
    const client=profiles.driver;
    await client.evaluate(`document.querySelector('[data-bs-target="#modalPODNavegacao"]').click()`);
    await until(client,`document.getElementById('modalPODNavegacao').contains(document.activeElement)`,'POD focus');
    await client.evaluate(`(()=>{
        const form=document.getElementById('formPODNavegacao');
        if(form.elements.entregaId.value!==${JSON.stringify(String(id))}) throw new Error('Wrong POD trip.');
        form.elements.nomeRecebedor.value=${JSON.stringify(name)};form.elements.documentoRecebedor.value='PRIVATE-BROWSER-DOC';
        document.getElementById('sigCanvasNav').scrollIntoView({block:'center',behavior:'instant'});
    })()`);
    const rect=await client.evaluate(`(()=>{const r=document.getElementById('sigCanvasNav').getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height};})()`);
    const x=rect.x+rect.width*.15,y=rect.y+rect.height*.45;
    await client.command('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button:'left',buttons:1,clickCount:1});
    for(let i=1;i<=6;i++) await client.command('Input.dispatchMouseEvent',{type:'mouseMoved',x:x+i*rect.width*.08,y:y+(i%2?15:-10),buttons:1});
    await client.command('Input.dispatchMouseEvent',{type:'mouseReleased',x:x+rect.width*.48,y:y-10,button:'left',clickCount:1});
    await client.evaluate(`document.getElementById('formPODNavegacao').requestSubmit()`);
    await until(client,`document.getElementById('assinaturaBase64Nav').value.startsWith('data:image/png;base64,')`,'Actual canvas signature is submitted');
}

async function adminForm(path) {
    await profiles.admin.evaluate(`(()=>{const form=[...document.forms].find(f=>new URL(f.action).pathname===${JSON.stringify(path)});if(!form)throw new Error('Missing real administrative form '+${JSON.stringify(path)});const confirmation=form.elements.confirmar;if(confirmation)confirmation.checked=true;form.requestSubmit();})()`);
    await until(profiles.admin,`document.readyState==='complete' && location.pathname==='/GpsHistorico'`);
    await pause(300);
}
async function detailed() {return profiles.admin.evaluate(`fetch('/GpsHistorico/Dados?entregaId=1').then(async r=>{if(!r.ok)throw new Error('Detailed history denied '+r.status);return r.json();})`);}

async function main() {
    if(experimental)return experimentalMain();
    const {readFileSync,writeFileSync,existsSync}=require('node:fs');const {execFileSync}=require('node:child_process');
    const captured=JSON.parse(readFileSync(resolve(__dirname,'../src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json'),'utf8'));
    const evidence=structuredClone(captured),file=join(directory,'route-evidence.json');
    evidence.GeographicVersion='fixture-7a-public-capture-v1 (não é homologação da base real)';
    function rules(height=4.5) {
        const kinds=['Height','GrossWeight','Width','Length','AxleLoad','AxleCount','Category','Access','Time','Hazardous'];
        const limits={Height:height,GrossWeight:100000,Width:3,Length:25,AxleLoad:100000,AxleCount:10};
        return kinds.map(kind=>({Id:'fixture-'+kind,Kind:kind,Source:'fixture://synthetic-restrictions',Version:'fixture-v1',Territory:evidence.Territory,
            GeometrySha256:'__FIXTURE_CAPTURE__',FirstEdge:0,LastEdge:evidence.RecordedPlan.TrajetoCoordenadas.length-2,
            ValidFrom:new Date(Date.now()-3600000).toISOString(),ValidUntil:new Date(Date.now()+7200000).toISOString(),Maximum:limits[kind]??null,
            Unit:['Height','Width','Length'].includes(kind)?'m':['GrossWeight','AxleLoad'].includes(kind)?'kg':kind==='AxleCount'?'eixos':null,
            Allowed:kind==='Category'?['caminhao']:kind==='Hazardous'?['nao-perigosa']:null,RequiredPermit:kind==='Access'?'PERMIT-FIXTURE':null,
            ExplicitlyUnrestricted:kind==='Time'}));
    }
    const save=()=>writeFileSync(file,JSON.stringify(evidence));evidence.Rules=rules();save();
    const baseUrl=await startServer();
    for(const role of ['admin','driver','client']){profiles[role]=createBrowserClient({baseUrl});await profiles[role].openBrowser();await profiles[role].command('Runtime.enable');profiles[role].onEvent('Runtime.exceptionThrown',e=>errors.push(role+': '+(e.exceptionDetails.exception?.description||e.exceptionDetails.text)));}
    const admin=profiles.admin,driver=profiles.driver;
    await login(admin,'admin','admin123','/Dashboard');await login(driver,'carlos.motorista','motorista123','/Entregas');await login(profiles.client,'cliente.demo','cliente123','/Cliente');
    const path='/Rotas/Entrega/1';
    async function calculate() {
        await admin.evaluate(`document.querySelector('form[action="/Rotas/Entrega/1/Calcular"]').requestSubmit()`);
        await until(admin,`document.readyState==='complete'&&!!document.querySelector('#routeTrustResult[data-confidence]:not([data-confidence="SemAvaliacao"])')`);
        await pause(350);
    }
    async function matrixRoute(label){await matrix(admin,path,label);}
    stage='insufficient unqualified legacy vehicle data';
    await admin.navigate(path);await calculate();
    assert.ok(await admin.evaluate(`document.getElementById('routeTrustResult').textContent.includes('Dados faltantes')`));
    await matrixRoute('restriction-missing');
    assert.ok(existsSync(join(directory,'database','.mgl-local-preview')));
    const payload=Number(execFileSync('python3',['-c',"import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(c.execute('SELECT PesoKg FROM Pedidos WHERE Id=(SELECT PedidoId FROM Entregas WHERE Id=1)').fetchone()[0]);c.close()",join(directory,'database','mglconnect.db')],{encoding:'utf8'}).trim());
    const gross=10000+payload,each=Math.floor(gross/6*100)/100,loads=[...Array(5).fill(each),Number((gross-5*each).toFixed(2))];
    async function saveProfile() {
        await admin.evaluate(`(()=>{const f=document.getElementById('routeProfileForm');for(const[k,v]of Object.entries(${JSON.stringify({Height:'4.2',Width:'2.6',Length:'18.6',Tare:'10000',Payload:String(payload),Pbt:String(gross),Axles:'6',AxleLoadsKg:loads.join(';'),Category:'caminhao',Composition:'simples',Cargo:'seca',Hazardous:'false',Permits:'PERMIT-FIXTURE'})}))f.elements[k].value=v;f.requestSubmit();})()`);
        await until(admin,`document.readyState==='complete'&&!!document.getElementById('routeProfileForm')`);await pause(350);
    }
    stage='explicit declared profile then estimated generic geometry';
    await saveProfile();evidence.Rules=[];save();await calculate();
    assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.confidence`),'Estimada');
    await matrixRoute('restriction-estimated');
    assert.ok(await admin.evaluate(`document.querySelector('#routeAssessmentMap .leaflet-overlay-pane path')!==null`),'Recorded geometry must render, without an invented GPS vehicle');
    stage='known incompatible height blocks the route';
    evidence.Rules=rules(3.8);save();await calculate();
    assert.ok(await admin.evaluate(`document.querySelector('#routeTrustResult h2').textContent==='BLOQUEADA'`));
    await matrixRoute('restriction-blocked');
    stage='complete fixture validation remains synthetic with navigation suspended';
    evidence.Rules=rules(4.5);save();await calculate();
    assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.confidence`),'Validada');
    assert.ok(await admin.evaluate(`document.getElementById('routeTrustResult').textContent.includes('ENSAIO SINTÉTICO')&&document.getElementById('routeTrustResult').textContent.includes('Suspensa')`));
    await matrixRoute('restriction-synthetic-validated');
    stage='provider/fixture outage never supplies a made-up line';
    writeFileSync(file,'invalid-json');await calculate();
    assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.confidence`),'Indisponivel');
    assert.equal(await admin.evaluate(`!!document.getElementById('routeAssessmentMap')`),false);
    await matrixRoute('restriction-provider-failure');
    evidence.Rules=rules(3.8);save();await calculate();
    stage='concurrent actual HTTP recalculations never expose a superseded result as current';
    const competing=await admin.evaluate(`(async()=>{const f=document.querySelector('form[action="/Rotas/Entrega/1/Calcular"]');return Promise.all([0,1].map(async()=>{const r=await fetch(f.action,{method:'POST',body:new FormData(f)});return r.status;}));})()`);
    assert.ok(competing.some(s=>s===200),JSON.stringify(competing));assert.ok(competing.every(s=>s===200||s===503),JSON.stringify(competing));
    await admin.navigate(path);
    const latest=JSON.parse(execFileSync('python3',['-c',"import sqlite3,sys,json;c=sqlite3.connect(sys.argv[1]);r=c.execute('SELECT a.Id,a.Obsoleta FROM Entregas e LEFT JOIN RotaAvaliacoes a ON a.Id=e.RotaCalculoAtualId WHERE e.Id=1').fetchone();print(json.dumps(r));c.close()",join(directory,'database','mglconnect.db')],{encoding:'utf8'}));
    if(latest[0])assert.equal((await admin.evaluate(`document.getElementById('routeTrustResult').dataset.calculationId`)).replaceAll('-','').toLowerCase(),latest[0].replaceAll('-','').toLowerCase());
    else assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.obsolete`),'true');
    stage='new actual vehicle assignment makes the prior evaluation obsolete';
    await admin.navigate('/Frota/NovoVeiculo');
    const newVehicle=await admin.evaluate(`(async()=>{const f=[...document.forms].find(f=>new URL(f.action).pathname==='/Frota/NovoVeiculo');for(const[k,v]of Object.entries({Modelo:'Synthetic route reassignment',Placa:'TST7A01',CapacidadeCargaKg:'25000',AlturaMetros:'4.2',LarguraMetros:'2.6',ComprimentoMetros:'18.6',PesoToneladas:'35',QtdEixos:'6',TipoCarga:'seca'}))f.elements[k].value=v;const r=await fetch(f.action,{method:'POST',body:new FormData(f)});if(!r.ok)throw new Error('New vehicle failed '+r.status);return Number(new URL(r.url).pathname.split('/').pop());})()`);
    assert.ok(newVehicle>3);await admin.navigate('/Entregas/Details/1');
    await admin.evaluate(`(async()=>{const f=[...document.forms].find(f=>new URL(f.action).pathname==='/Entregas/AtribuirVeiculo');f.elements.veiculoId.value=${JSON.stringify(String(newVehicle))};const r=await fetch(f.action,{method:'POST',body:new FormData(f)});if(!r.ok)throw new Error('Reassignment failed '+r.status);})()`);
    await admin.navigate(path);assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.obsolete`),'true');
    await saveProfile();
    stage='assigned driver shift, blocked guidance and observed synthetic native GPS';
    await startShift(1,await issueCode(1));await until(driver,`!!window.mglGpsOutbox?.queue`);
    assert.equal(await driver.evaluate(`document.getElementById('routeTrustBadge').dataset.navigationAllowed`),'false');
    await driver.command('Browser.grantPermissions',{origin:baseUrl,permissions:['geolocation']});await driver.command('Emulation.setGeolocationOverride',{latitude:-23.65,longitude:-46.72,accuracy:8});
    await driver.evaluate(`document.getElementById('btnToggleGps').click()`);
    await until(driver,`document.getElementById('positionSourceBadge').dataset.state==='received'&&document.getElementById('driverGpsSync').dataset.pending==='0'`);
    assert.equal(await driver.evaluate(`document.getElementById('etaValue').textContent`),'--:--');
    assert.equal(await driver.evaluate(`document.getElementById('distanceRemaining').textContent.trim()`),'—');
    const observed=await history();assert.ok(observed.items.some(p=>p.latitude===-23.65&&p.longitude===-46.72));
    await matrix(driver,'/Entregas/Navegar/1','driver');
    stage='client still has only authorized tracking and signed POD';
    const denied=await profiles.client.evaluate(`fetch('/Rotas/Entrega/1').then(r=>({redirected:r.redirected,url:r.url}))`);assert.ok(denied.redirected&&denied.url.includes('AccessDenied'));
    await drawAndSubmitPod(1,'Synthetic restriction journey receiver');await until(driver,`document.getElementById('btnSubmitPODNav').textContent==='Entrega confirmada'`);
    const pdf=await profiles.client.evaluate(`fetch('/Cliente/Pedido/1/Comprovante').then(async r=>({status:r.status,magic:new TextDecoder().decode((await r.arrayBuffer()).slice(0,4))}))`);assert.deepEqual(pdf,{status:200,magic:'%PDF'});
    assert.deepEqual(errors,[]);
    console.log('Stage 7A Chromium passed: missing/estimated/blocked/synthetic validation, recorded public geometry, source failure, concurrent HTTP calculations/current IDs, real vehicle reassignment invalidates history, driver/native synthetic off-route fix stays observed with ETA suspended, customer scope and actual signed POD/PDF. Six scenarios x five widths x two themes; no synthetic navigation grant.');
    console.log('Owned evidence: '+directory+'; screenshots: '+JSON.stringify(Object.fromEntries(Object.entries(profiles).map(([role,c])=>[role,c.directory]))));
}
async function experimentalMain() {
    const fs=require('node:fs'),{execFileSync}=require('node:child_process');
    const fixture=fs.readFileSync(resolve(__dirname,'../src/MglConnect.Tests/Fixtures/valhalla-contract-synthetic.json'));
    execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-keyout',join(directory,'fixture-key.pem'),'-out',join(directory,'fixture-cert.pem'),'-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1'],{stdio:'ignore'});
    fs.chmodSync(join(directory,'fixture-key.pem'),0o600);
    const listen=async listener=>{controlledServers.push(listener);await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));return listener.address().port;};
    const photon=require('node:https').createServer({key:fs.readFileSync(join(directory,'fixture-key.pem')),cert:fs.readFileSync(join(directory,'fixture-cert.pem'))},(req,res)=>{
        const q=new URL(req.url,'https://localhost').searchParams.get('q')||'';res.setHeader('Content-Type','application/json');
        res.end(JSON.stringify({features:[{geometry:{coordinates:q.includes('MASP')?[-46.6559,-23.5614]:[-46.6333,-23.5505]}}]}));
    });photonUrl='https://127.0.0.1:'+await listen(photon);
    const provider=require('node:http').createServer(async(req,res)=>{
        let body='';for await(const chunk of req)body+=chunk;
        assert.equal(req.url,'/route');providerRequests.push(JSON.parse(body));res.setHeader('Content-Type','application/json');
        if(providerMode==='limited'){res.statusCode=429;res.setHeader('Retry-After','120');return res.end('{}');}
        if(providerMode==='outage'){res.statusCode=503;return res.end('{}');}
        if(providerMode==='uncovered')return res.end('{"error_code":171}');
        res.end(fixture);
    });providerUrl='http://127.0.0.1:'+await listen(provider);
    const baseUrl=await startServer();assert.ok(fs.existsSync(join(directory,'database','.mgl-local-preview')));
    const database=join(directory,'database','mglconnect.db');
    // Exclusively owned synthetic records, with public landmark locations. No real customer data.
    execFileSync('python3',['-c',"import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);c.execute(\"UPDATE Pedidos SET OrigemEndereco='MASP, Avenida Paulista',OrigemCidade='São Paulo',OrigemEstado='SP' WHERE Id=1\");c.execute(\"UPDATE Entregas SET EnderecoDestino='Praça da Sé',CidadeDestino='São Paulo',EstadoDestino='SP' WHERE Id=1\");c.commit();c.close()",database]);
    for(const role of ['admin','driver','client']){profiles[role]=createBrowserClient({baseUrl});await profiles[role].openBrowser();await profiles[role].command('Runtime.enable');profiles[role].onEvent('Runtime.exceptionThrown',e=>errors.push(role+': '+(e.exceptionDetails.exception?.description||e.exceptionDetails.text)));}
    const admin=profiles.admin,driver=profiles.driver,path='/Rotas/Entrega/1';
    await login(admin,'admin','admin123','/Dashboard');await login(driver,'carlos.motorista','motorista123','/Entregas');await login(profiles.client,'cliente.demo','cliente123','/Cliente');
    async function calculate(){await admin.evaluate(`(async()=>{const f=document.querySelector('form[action="/Rotas/Entrega/1/Calcular"]');const r=await fetch(f.action,{method:'POST',body:new FormData(f)});if(!r.ok)throw new Error('Calculation failed '+r.status);})()`);await admin.navigate(path);}
    stage='experimental missing physical data';await admin.navigate(path);await calculate();
    assert.equal(providerRequests.length,0);assert.ok(await admin.evaluate(`document.getElementById('providerCoverage').dataset.providerState==='MissingVehicleData'`));await matrix(admin,path,'experimental-missing');
    const payload=Number(execFileSync('python3',['-c',"import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(c.execute('SELECT PesoKg FROM Pedidos WHERE Id=1').fetchone()[0])",database],{encoding:'utf8'}).trim());
    const gross=10000+payload,each=Math.floor(gross/6*100)/100,loads=[...Array(5).fill(each),Number((gross-5*each).toFixed(2))];
    await admin.evaluate(`(async()=>{const f=document.getElementById('routeProfileForm');for(const[k,v]of Object.entries(${JSON.stringify({Height:'4.2',Width:'2.6',Length:'18.6',Tare:'10000',Payload:String(payload),Pbt:String(gross),Axles:'6',AxleLoadsKg:loads.join(';'),Category:'caminhao',Composition:'simples',Cargo:'seca',Hazardous:'false',Permits:'nenhuma'})}))f.elements[k].value=v;const r=await fetch(f.action,{method:'POST',body:new FormData(f)});if(!r.ok)throw new Error('Profile failed '+r.status);})()`);await admin.navigate(path);
    stage='actual adapter HTTP against a synthetic controlled Valhalla protocol';await calculate();
    assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.confidence`),'Estimada');
    assert.ok(await admin.evaluate(`document.getElementById('providerCoverage').dataset.providerState==='None'&&document.getElementById('providerCoverage').textContent.includes('Desconhecida')`));
    assert.equal(providerRequests.length,1);assert.equal(providerRequests[0].costing,'truck');assert.equal(providerRequests[0].costing_options.truck.weight,gross/1000);assert.equal(providerRequests[0].costing_options.truck.height,4.2);
    assert.ok(await admin.evaluate(`!!document.querySelector('#routeAssessmentMap .leaflet-overlay-pane path')`));await matrix(admin,path,'experimental-estimated');
    for(const [mode,status] of [['uncovered','NoCoverage'],['outage','HttpError'],['limited','RateLimited']]){
        stage='experimental '+mode;providerMode=mode;await calculate();
        assert.equal(await admin.evaluate(`document.getElementById('routeTrustResult').dataset.confidence`),'Indisponivel');
        assert.ok(await admin.evaluate(`document.getElementById('providerCoverage').dataset.providerState===${JSON.stringify(status)}`));await matrix(admin,path,'experimental-'+mode);
    }
    providerMode='success';await calculate();stage='driver observed GPS and POD with experimental guidance blocked';
    await startShift(1,await issueCode(1));await until(driver,`!!window.mglGpsOutbox?.queue`);
    assert.equal(await driver.evaluate(`document.getElementById('routeTrustBadge').dataset.navigationAllowed`),'false');
    await driver.command('Browser.grantPermissions',{origin:baseUrl,permissions:['geolocation']});await driver.command('Emulation.setGeolocationOverride',{latitude:-23.65,longitude:-46.72,accuracy:8});await driver.evaluate(`document.getElementById('btnToggleGps').click()`);
    await until(driver,`document.getElementById('positionSourceBadge').dataset.state==='received'&&document.getElementById('driverGpsSync').dataset.pending==='0'`);
    assert.equal(await driver.evaluate(`document.getElementById('etaValue').textContent`),'--:--');assert.ok((await history()).items.some(p=>p.latitude===-23.65&&p.longitude===-46.72));
    await matrix(driver,'/Entregas/Navegar/1','driver');await drawAndSubmitPod(1,'Synthetic experimental routing receiver');await until(driver,`document.getElementById('btnSubmitPODNav').textContent==='Entrega confirmada'`);
    assert.deepEqual(await profiles.client.evaluate(`fetch('/Cliente/Pedido/1/Comprovante').then(async r=>({status:r.status,magic:new TextDecoder().decode((await r.arrayBuffer()).slice(0,4))}))`),{status:200,magic:'%PDF'});
    assert.deepEqual(errors,[]);console.log('Stage 7B.1 Chromium passed: actual HTTP adapter against controlled synthetic protocol, physical units, unknown road coverage, missing data, no coverage, HTTP outage, rate limit, observed synthetic GPS, blocked ETA and actual POD/PDF. 60 layout combinations; no live Valhalla engine or real GPS accuracy claim.');
    console.log('Owned evidence: '+directory+'; screenshots: '+JSON.stringify(Object.fromEntries(Object.entries(profiles).map(([role,c])=>[role,c.directory]))));
}
main().catch(async error=>{console.error('Failed stage: '+stage+'\n'+error.stack);for(const[role,c]of Object.entries(profiles))try{await c.screenshot('route-failed-'+role);console.error(role+': '+c.directory);}catch{}console.error('Owned evidence: '+directory);process.exitCode=1;}).finally(async()=>{for(const c of Object.values(profiles))await c.closeBrowser();server?.kill('SIGTERM');for(const listener of controlledServers)listener.closeAllConnections?.();await Promise.all(controlledServers.map(s=>new Promise(resolve=>s.close(resolve))));});

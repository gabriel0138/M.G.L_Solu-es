// Complete browser journey on an exclusively owned temporary SQLite database.
// Four Chromium profiles keep administrator, customer, driver and public cookies separate.
const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {mkdtempSync,createWriteStream} = require('node:fs');
const {tmpdir} = require('node:os');
const {join,resolve} = require('node:path');
const net = require('node:net');
const {createBrowserClient} = require('./command-center-browser-check.cjs');
const directory = mkdtempSync(join(tmpdir(),'mgl-gps-history-'));
const pause = ms => new Promise(resolve=>setTimeout(resolve,ms));
let server;
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
    server=spawn('bash',['scripts/dev-local.sh'],{cwd:resolve(__dirname,'..'),env:{...process.env,
        MGL_LOCAL_PORT:String(port),MGL_LOCAL_DATA_DIR:join(directory,'database'),GpsHistory__DiasAntesCompactar:'0'},stdio:['ignore','pipe','pipe']});
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
    const baseUrl=await startServer();
    for(const role of ['admin','driver','client']) {
        profiles[role]=createBrowserClient({baseUrl});await profiles[role].openBrowser();await profiles[role].command('Runtime.enable');
        profiles[role].onEvent('Runtime.exceptionThrown',event=>errors.push(role+': '+(event.exceptionDetails.exception?.description||event.exceptionDetails.text)));
    }
    const {execFileSync}=require('node:child_process');
    const {existsSync,mkdirSync,readFileSync}=require('node:fs');
    const driver=profiles.driver,admin=profiles.admin;
    // Other harness exercises the real worker; this one intercepts GPS request transport.
    await driver.command('Network.setBypassServiceWorker',{bypass:true});
    await login(admin,'admin','admin123','/Dashboard');
    await login(driver,'carlos.motorista','motorista123','/Entregas');
    await login(profiles.client,'cliente.demo','cliente123','/Cliente');
    await startShift(1,await issueCode(1));
    await until(driver,`!!window.mglGpsOutbox?.queue`);
    assert.ok(existsSync(join(directory,'database','.mgl-local-preview')));
    execFileSync('python3',['-c',`import sqlite3,sys,datetime
c=sqlite3.connect(sys.argv[1])
c.execute('UPDATE TurnosMotorista SET IniciadoEmUtc=? WHERE EntregaId=1 AND Ativo=1',((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(days=8)).strftime('%Y-%m-%d %H:%M:%S.%f'),))
c.commit()
c.close()`,join(directory,'database','mglconnect.db')]);
    stage='valid, approximate, audit-only and out-of-order positions';
    const now=Date.now();
    for(const [age,accuracy] of [[86400000,8],[60000,8],[70000,80],[71000,900]]) {
        await capture({latitude:-23.55-age/1e11,longitude:-46.63,precisaoMetros:accuracy,capturadoEmUtc:new Date(now-age).toISOString()});
        await until(driver,`document.getElementById('driverGpsSync').dataset.pending==='0'`);
    }
    await admin.navigate('/GpsHistorico?entregaId=1');
    await until(admin,`document.querySelector('[data-gps-history-status]').textContent.includes('4 pontos nesta página')`);
    await admin.evaluate(`(async()=>{const c=new signalR.HubConnectionBuilder().withUrl('/telemetryHub').build();await c.start();try{const code=document.getElementById('gpsTrip').selectedOptions[0].textContent.split(' · ')[0];await c.invoke('SendVehicleTelemetry',code,-23.551,-46.631,20,90,8,true);}finally{await c.stop();}})()`);
    await admin.evaluate(`document.querySelector('[data-gps-history-reload]').click()`);
    await until(admin,`document.querySelector('[data-gps-history-table]').textContent.includes('SIMULAÇÃO')`);
    const initial=await detailed();assert.equal(initial.total,5);
    assert.equal((await profiles.client.evaluate(`fetch('/api/gps/historico/1').then(r=>r.json())`)).total,4,'Customer projection excludes simulations');
    assert.ok(initial.items.some(p=>p.qualidade==='baixa_precisao'&&!p.adequadaMapa));
    assert.ok(initial.items.some(p=>p.foraDeOrdem));assert.ok(initial.items.some(p=>p.lacunaAnteriorSegundos>300));
    assert.equal(await admin.evaluate(`document.querySelectorAll('[data-gps-history-map] path.leaflet-interactive').length`),4,'900m audit point must not be plotted');
    assert.ok(await admin.evaluate(`document.querySelector('[data-gps-history-table]').textContent.includes('fora de ordem')`));
    stage='period filter, actual search and authorized CSV';
    await admin.evaluate(`document.getElementById('gpsPeriodStart').value=${JSON.stringify(new Date(now-120000).toISOString().slice(0,19))};document.getElementById('gpsTripSearch').value='MGL';document.querySelector('.cc-gps-filters').requestSubmit()`);
    await until(admin,`document.readyState==='complete'&&location.search.includes('de=')`);
    await until(admin,`document.querySelector('[data-gps-history-status]')?.textContent.includes('4 pontos nesta página')`);
    const csv=await admin.evaluate(`fetch(document.querySelector('form[action="/GpsHistorico/Exportar"]').action+'?'+new URLSearchParams(new FormData(document.querySelector('form[action="/GpsHistorico/Exportar"]')))).then(async r=>({status:r.status,type:r.headers.get('content-type'),text:await r.text()}))`);
    assert.equal(csv.status,200);assert.ok(csv.type.startsWith('text/csv'));assert.equal(csv.text.trim().split('\r\n').length,5);
    const denied=await profiles.client.evaluate(`fetch('/GpsHistorico/Dados?entregaId=1').then(r=>({status:r.status,redirected:r.redirected,path:new URL(r.url).pathname,type:r.headers.get('content-type')}))`);
    assert.ok(denied.status===403 || denied.redirected&&denied.path.includes('AccessDenied'),JSON.stringify(denied));

    stage='seven-day offline window and actual browser process restart';
    const blockGps=async()=>{
        await driver.command('Fetch.enable',{patterns:[{urlPattern:'*/api/gps/sincronizar',requestStage:'Request'}]});
    };
    driver.onEvent('Fetch.requestPaused',e=>driver.command('Fetch.failRequest',{requestId:e.requestId,errorReason:'ConnectionClosed'}));
    await blockGps();
    const oldest=new Date(Date.now()-(7*24-1)*3600000).toISOString();
    await capture({latitude:-23.56,longitude:-46.64,precisaoMetros:10,capturadoEmUtc:oldest});
    const pending=(await rows()).find(r=>r.state==='pending');assert.ok(pending);
    await driver.restartBrowser();
    await blockGps();
    // A browser restart also expires the nonpersistent login cookie. Reauthenticate
    // the same owner; IndexedDB must survive without storing any credential.
    await login(driver,'carlos.motorista','motorista123','/Entregas');
    await driver.navigate('/Entregas/Navegar/1');
    await until(driver,`!!window.mglGpsOutbox?.queue`);
    const restored=(await rows()).find(r=>r.id===pending.id);assert.equal(restored.state,'pending');assert.deepEqual(restored.body,pending.body);
    stage='expired events preserved, backup download, explicit cleanup';
    const terminal=await driver.evaluate(`(async()=>{
        const q=window.mglGpsOutbox.queue,shift=document.getElementById('driverGpsSync').dataset.shift;
        const old=new MglDriverGpsQueue(q.scope,{now:()=>Date.now()-8*86400000});
        const row=await old.enqueue(1,shift,{latitude:-23.57,longitude:-46.65,precisaoMetros:12,capturadoEmUtc:new Date(Date.now()-8*86400000).toISOString()});
        await q.maintain();await q.snapshot();return row.id;
    })()`);
    assert.equal((await rows()).find(r=>r.id===terminal).state,'expired');
    const download=join(directory,'downloads');mkdirSync(download);
    await driver.command('Browser.setDownloadBehavior',{behavior:'allow',downloadPath:download});
    await driver.evaluate(`document.querySelector('[data-gps-recovery]').open=true;document.querySelector('[data-gps-export]').click()`);
    const backup=join(download,'mgl-gps-recuperacao.json');for(let n=0;n<100&&!existsSync(backup);n++)await pause(100);
    assert.ok(existsSync(backup),'Actual local backup file is downloaded');
    const saved=JSON.parse(readFileSync(backup,'utf8'));assert.ok(saved.events.some(e=>e.id===terminal));assert.ok(saved.events.some(e=>e.id===pending.id));
    assert.equal(saved.format,'mgl-gps-recovery-v1');assert.ok(!/Bearer|SenhaHash|accessToken/.test(JSON.stringify(saved)));
    driver.onEvent('Page.javascriptDialogOpening',()=>driver.command('Page.handleJavaScriptDialog',{accept:true}));
    await driver.evaluate(`document.querySelector('[data-gps-clear]').click()`);
    await until(driver,`document.getElementById('driverGpsSync').dataset.expired==='0'`);
    assert.ok((await rows()).some(r=>r.id===pending.id&&r.state==='pending'),'Cleanup never removes pending');
    stage='manual recovery retries the original UUID then terminal trip accepts captured history';
    await driver.evaluate(`(async()=>{const q=window.mglGpsOutbox.queue;await q.store.update(${JSON.stringify(pending.id)},q.owner,r=>({...r,state:'rejected',reason:'turno_indisponivel'}));document.querySelector('[data-gps-recover]').click();})()`);
    await until(driver,`document.getElementById('driverGpsSync').dataset.pending==='1'`);
    await driver.viewport(390,844);
    await drawAndSubmitPod(1,'Synthetic stage five receiver');
    await until(driver,`document.getElementById('btnSubmitPODNav').textContent==='Entrega confirmada'`,'POD complete despite preserved GPS failure');
    await driver.command('Fetch.disable');
    await driver.navigate('/Entregas');
    await until(driver,`!!window.mglGpsOutbox?.queue`);await driver.evaluate(`window.mglGpsOutbox.queue.retry()`);
    await until(driver,`document.getElementById('driverGpsReplayState').dataset.pending==='0'`);
    assert.equal((await rows()).find(r=>r.id===pending.id).state,'confirmed');assert.equal((await rows()).find(r=>r.id===pending.id).body.capturadoEmUtc,oldest);
    assert.equal((await detailed()).total,6);
    stage='archive verified package, keep source, then explicit compaction';
    await admin.navigate('/GpsHistorico?entregaId=1');await adminForm('/GpsHistorico/Arquivar');
    await until(admin,`!!document.querySelector('form[action="/GpsHistorico/Compactar"]')`);
    assert.equal((await detailed()).total,6,'Archive and online copy must not appear twice');
    const file=await admin.evaluate(`(async()=>{const link=document.querySelector('a[href^="/GpsHistorico/Arquivo"]');const r=await fetch(link.href);return{status:r.status,sha:r.headers.get('x-content-sha256'),bytes:Array.from(new Uint8Array(await r.arrayBuffer()))};})()`);
    const {gunzipSync}=require('node:zlib'),{createHash}=require('node:crypto');
    assert.equal(file.status,200);const uncompressed=gunzipSync(Buffer.from(file.bytes));assert.equal(createHash('sha256').update(uncompressed).digest('hex').toUpperCase(),file.sha.toUpperCase());
    assert.ok(JSON.parse(uncompressed).length===6,'Manifest and record count');
    await adminForm('/GpsHistorico/Compactar');
    await until(admin,`document.querySelector('.cc-gps-archive-table').textContent.includes('Cópia online compactada')`);
    const archived=await detailed();assert.equal(archived.total,6);assert.ok(archived.items.every(p=>p.arquivado));
    await driver.evaluate(`(async()=>{const q=window.mglGpsOutbox.queue;await q.store.update(${JSON.stringify(pending.id)},q.owner,r=>({...r,state:'pending',attempts:0,nextAt:0}));await q.retry();})()`);
    assert.equal((await rows()).find(r=>r.id===pending.id).receipt.estado,'duplicado');
    assert.equal((await detailed()).total,6);
    assert.equal((await profiles.client.evaluate(`fetch('/api/gps/historico/1').then(r=>r.json())`)).total,5);
    stage='both themes, desktop/tablet/mobile confirmed and archived histories';
    await matrix(admin,'/GpsHistorico?entregaId=1','console-history');
    await matrix(profiles.client,'/Cliente/Pedido/1','client-history');
    // Recovery is available on the list after final delivery, no tracking implied.
    await matrix(driver,'/Entregas','driver-list');
    stage='corruption never leaves a stale map or produces a successful partial CSV';
    execFileSync('python3',['-c',"import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);c.execute('UPDATE GpsArquivos SET ConteudoGzip=?',(bytes([1,2,3]),));c.commit();c.close()",join(directory,'database','mglconnect.db')]);
    await admin.evaluate(`document.querySelector('[data-gps-history-reload]').click()`);
    await until(admin,`document.querySelector('[data-gps-history-status]').textContent.includes('Integridade do histórico não confirmada')`);
    assert.equal(await admin.evaluate(`document.querySelector('[data-gps-history-map]').hidden&&document.querySelector('[data-gps-history-table]').hidden`),true);
    assert.equal(await admin.evaluate(`fetch('/GpsHistorico/Exportar?entregaId=1').then(r=>r.status)`),409);
    assert.deepEqual(errors,[]);
    console.log('Stage 5 Chromium passed: accuracy/chronology/gaps, period/search/CSV, near-seven-day original capture, process restart preserves IndexedDB followed by fresh authentication, expiration preserved, real local backup, explicit cleanup, manual recovery, closed-shift sync after actual signed POD, gzip/SHA/manifest, compaction and archived UUID duplicate/customer history; three screens x five widths x two themes.');
    console.log('Owned evidence: '+directory+'; screenshots: '+JSON.stringify(Object.fromEntries(Object.entries(profiles).map(([role,c])=>[role,c.directory]))));
}
main().catch(async error=>{
    console.error('Failed stage: '+stage+'\n'+error.stack);
    for(const[role,client]of Object.entries(profiles))try{await client.screenshot('stage5-failed-'+role);console.error(role+': '+client.directory);}catch{}
    console.error('Owned evidence: '+directory);process.exitCode=1;
}).finally(async()=>{for(const client of Object.values(profiles))await client.closeBrowser();server?.kill('SIGTERM');});

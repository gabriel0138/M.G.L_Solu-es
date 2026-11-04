// Complete browser journey on an exclusively owned temporary SQLite database.
// Four Chromium profiles keep administrator, customer, driver and public cookies separate.
const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {mkdtempSync,createWriteStream} = require('node:fs');
const {tmpdir} = require('node:os');
const {join,resolve} = require('node:path');
const net = require('node:net');
const {createBrowserClient} = require('./command-center-browser-check.cjs');
const directory = mkdtempSync(join(tmpdir(),'mgl-gps-offline-'));
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
        MGL_LOCAL_PORT:String(port),MGL_LOCAL_DATA_DIR:join(directory,'database')},stdio:['ignore','pipe','pipe']});
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
async function main() {
    const baseUrl=await startServer();
    for(const role of ['admin','driver','client']) {
        profiles[role]=createBrowserClient({baseUrl});await profiles[role].openBrowser();await profiles[role].command('Runtime.enable');
        profiles[role].onEvent('Runtime.exceptionThrown',event=>errors.push(role+': '+(event.exceptionDetails.exception?.description||event.exceptionDetails.text)));
    }
    await login(profiles.admin,'admin','admin123','/Dashboard');
    await login(profiles.driver,'carlos.motorista','motorista123','/Entregas');
    await login(profiles.client,'cliente.demo','cliente123','/Cliente');
    stage='start actual assigned shift';await startShift(1,await issueCode(1));
    const driver=profiles.driver,admin=profiles.admin;
    await until(driver,`!!window.mglGpsOutbox && !!document.getElementById('driverGpsSync').dataset.shift`);
    // ONLY the owned synthetic fixture's shift is backdated, allowing an old
    // capture to be tested without waiting ten minutes. No existing database is opened.
    const {execFileSync}=require('node:child_process');const {existsSync}=require('node:fs');
    assert.ok(existsSync(join(directory,'database','.mgl-local-preview')));
    execFileSync('python3',['-c',`import sqlite3,sys,datetime
p=sys.argv[1]
c=sqlite3.connect(p)
c.execute('UPDATE TurnosMotorista SET IniciadoEmUtc=? WHERE EntregaId=1 AND Ativo=1',((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(hours=1)).strftime('%Y-%m-%d %H:%M:%S.%f'),))
c.commit()
c.close()`,join(directory,'database','mglconnect.db')]);
    stage='old capture received now remains stale and has no ETA';
    await capture({latitude:-23.55,longitude:-46.63,precisaoMetros:8,capturadoEmUtc:new Date(Date.now()-600000).toISOString(),velocidadeKmH:30,direcao:90});
    await until(driver,`document.getElementById('driverGpsSync').dataset.pending==='0' && document.getElementById('positionSourceBadge').dataset.state==='stale'`);
    assert.equal(await driver.evaluate(`document.getElementById('etaValue').textContent`),'--:--');
    await admin.navigate('/Operacoes');
    await until(admin,`document.getElementById('fleetTripList').textContent.includes('desatualizado')`,'Administrative map uses capture age');
    const oldSnapshot=await admin.evaluate(`fetch('/Operacoes/Posicoes').then(r=>r.json()).then(r=>r.items.find(p=>p.id===1))`);
    assert.ok(Date.now()-Date.parse(oldSnapshot.capturadoEmUtc)>120000);assert.ok(Date.now()-Date.parse(oldSnapshot.recebidoEmUtc)<60000);
    stage='native geolocation enters the durable outbox';
    await driver.command('Browser.grantPermissions',{origin:baseUrl,permissions:['geolocation']});
    await driver.command('Emulation.setGeolocationOverride',{latitude:-23.5505,longitude:-46.6333,accuracy:8});
    await driver.evaluate(`document.getElementById('btnToggleGps').click()`);
    await until(driver,`document.getElementById('positionSourceBadge').dataset.state==='received' && document.getElementById('driverGpsSync').dataset.pending==='0'`);
    stage='offline capture persists before send and page reload';
    await driver.evaluate('navigator.serviceWorker.ready');
    await until(driver,`!!navigator.serviceWorker.controller`,'Service worker owns the page');
    await driver.command('Network.enable');
    await driver.command('Network.emulateNetworkConditions',{offline:true,latency:0,downloadThroughput:0,uploadThroughput:0});
    await until(driver,`!navigator.onLine`);
    await driver.command('Emulation.setGeolocationOverride',{latitude:-23.551,longitude:-46.634,accuracy:9});
    // Offline sampling deliberately waits 60 seconds before persisting another fix.
    await pause(61000);
    await driver.command('Emulation.setGeolocationOverride',{latitude:-23.552,longitude:-46.635,accuracy:9});
    await until(driver,`Number(document.getElementById('driverGpsSync').dataset.pending)>=1`,'Offline device fix is persisted');
    await capture({latitude:-23.54,longitude:-46.62,precisaoMetros:9,capturadoEmUtc:new Date(Date.now()-60000).toISOString(),velocidadeKmH:null,direcao:null});
    const pendingBefore=(await rows()).filter(r=>r.state==='pending');assert.ok(pendingBefore.length>=2);
    const ids=pendingBefore.map(r=>r.id);const captures=Object.fromEntries(pendingBefore.map(r=>[r.id,r.body.capturadoEmUtc]));
    await driver.screenshot('gps-offline-cockpit-pending');
    // CDP page network emulation does not necessarily affect the worker target.
    // Stop this owned server as well: the worker must handle a real network failure.
    const stoppedServer=server;
    stoppedServer.kill('SIGTERM');
    await new Promise(resolve=>stoppedServer.once('exit',resolve));
    await driver.command('Page.reload');
    await until(driver,`document.readyState==='complete' && document.getElementById('offlineGpsStatus')`,'Offline reload uses safe public fallback');
    const stored=await rows();assert.deepEqual(stored.filter(r=>r.state==='pending').map(r=>r.id).sort(),ids.sort());
    assert.ok(await driver.evaluate(`document.body.textContent.includes('A coleta GPS não continua nesta página')`));
    stage='restart the same owned server and database on the same origin';
    await startServer(Number(new URL(baseUrl).port));
    assert.equal((await history()).total,stored.filter(r=>r.state==='confirmed').length,'History survives actual server restart');
    stage='lose response AFTER database commit, preserve same event ids';
    // Bypass the worker only for deterministic CDP response-loss interception.
    // The previous stage exercised the real Service Worker and IndexedDB.
    await driver.command('Network.setBypassServiceWorker',{bypass:true});let dropped=0;
    driver.onEvent('Fetch.requestPaused',async event=>{
        if(event.request.url.includes('/api/gps/sincronizar')&&event.responseStatusCode===200&&dropped===0){dropped++;await driver.command('Fetch.failRequest',{requestId:event.requestId,errorReason:'ConnectionClosed'});}
        else await driver.command('Fetch.continueRequest',{requestId:event.requestId});
    });
    await driver.command('Fetch.enable',{patterns:[{urlPattern:'*/api/gps/sincronizar',requestStage:'Response'}]});
    await driver.command('Network.emulateNetworkConditions',{offline:false,latency:0,downloadThroughput:-1,uploadThroughput:-1});
    await until(driver,`navigator.onLine`);
    for(let n=0;n<150&&dropped===0;n++)await pause(100);assert.equal(dropped,1);
    const afterCommit=await history();for(const id of ids)assert.ok(afterCommit.items.some(p=>p.eventoId===id));
    assert.ok((await rows()).filter(r=>ids.includes(r.id)&&r.state==='pending').length>0,'Lost receipt must remain pending');
    const total=afterCommit.total;
    stage='automatic reconnection and page reload acknowledge without duplicates';
    await pause(2200);await driver.evaluate(`document.querySelector('button[onclick]').click()`);
    await until(driver,`document.readyState==='complete' && document.getElementById('driverGpsSync')?.dataset.pending==='0'`,'Reload replays durable GPS');
    const confirmed=await rows();for(const id of ids){const row=confirmed.find(r=>r.id===id);assert.equal(row.state,'confirmed');assert.equal(row.body.capturadoEmUtc,captures[id]);}
    assert.equal((await history()).total,total);
    const latest=await admin.evaluate(`fetch('/Operacoes/Posicoes').then(r=>r.json()).then(r=>r.items.find(p=>p.id===1))`);
    assert.equal(latest.latitude,-23.552);assert.equal(latest.longitude,-46.635);
    assert.equal(await driver.evaluate(`document.getElementById('positionSourceBadge').dataset.state`),'received');
    stage='replay from the driver trip list without enabling collection outside navigation';
    const activeShift=(await rows()).find(r=>r.body.entregaId===1).body.turnoId;
    await driver.navigate('/Entregas');
    await until(driver,`!!window.mglGpsOutbox?.queue`);
    const collectionBlocked=await driver.evaluate(`window.mglGpsOutbox.capture({}).then(()=>false,error=>error.message.includes('turno ativo'))`);
    assert.equal(collectionBlocked,true);
    await driver.evaluate(`window.mglGpsOutbox.queue.enqueue(1,${JSON.stringify(activeShift)}, {latitude:-23.553,longitude:-46.636,precisaoMetros:8,capturadoEmUtc:new Date().toISOString()}).then(()=>window.dispatchEvent(new Event('online')))`);
    await until(driver,`document.getElementById('driverGpsReplayState')?.dataset.pending==='0'`,'Trip-list replay is available before POD');
    assert.equal((await history()).total,total+1);
    await driver.navigate('/Entregas/Navegar/1');
    await until(driver,`!!window.mglGpsOutbox?.queue`);
    stage='real IndexedDB global cap and atomic reservation';
    const quota=await driver.evaluate(`(async()=>{
        const store=new MglIndexedGpsStore(),db=await store.open();
        const count=await new Promise(resolve=>{const r=db.transaction('events','readonly').objectStore('events').count();r.onsuccess=()=>resolve(r.result);});
        await new Promise((resolve,reject)=>{const tx=db.transaction('events','readwrite'),s=tx.objectStore('events');
            for(let n=count;n<1999;n++)s.add({id:'__quota_'+n,owner:'__quota_fixture__',state:'pending',body:{capturadoEmUtc:new Date().toISOString()}});
            tx.oncomplete=resolve;tx.onerror=tx.onabort=()=>reject(tx.error);
        });
        const position={latitude:-23.7,longitude:-46.7,precisaoMetros:8,capturadoEmUtc:new Date().toISOString()};
        const queues=['__quota_racer_a__','__quota_racer_b__'].map(userId=>new MglDriverGpsQueue({userId,tenantId:'__quota_fixture__'}));
        const results=await Promise.allSettled(queues.map(q=>q.enqueue(1,document.getElementById('driverGpsSync').dataset.shift,position)));
        const final=await new Promise(resolve=>{const r=db.transaction('events','readonly').objectStore('events').count();r.onsuccess=()=>resolve(r.result);});
        await new Promise((resolve,reject)=>{const tx=db.transaction('events','readwrite'),s=tx.objectStore('events'),cursor=s.openCursor();
            cursor.onsuccess=()=>{const c=cursor.result;if(!c)return;if(c.value.id.startsWith('__quota_')||c.value.owner.startsWith('__quota_racer_'))c.delete();c.continue();};
            tx.oncomplete=resolve;tx.onerror=tx.onabort=()=>reject(tx.error);
        });
        return{count:final,success:results.filter(r=>r.status==='fulfilled').length,rejected:results.filter(r=>r.status==='rejected').length};
    })()`);
    assert.deepEqual(quota,{count:2000,success:1,rejected:1});assert.equal((await rows()).filter(r=>r.state==='pending').length,0);
    stage='authorized observed history and mobile cockpit';
    await matrix(driver,'/Entregas/Navegar/1','driver');
    await matrix(admin,'/Entregas/Details/1','admin-history');
    await matrix(profiles.client,'/Cliente/Pedido/1','client-history');
    await matrix(admin,'/Operacoes','operations');
    assert.deepEqual(errors,[]);
    console.log('GPS Chromium passed: old capture stays stale/no ETA, native synthetic GPS, durable offline queue, actual server stop/restart and offline reload/Service Worker, dropped response after commit, automatic replay with original ids/times, no duplicates, real IndexedDB cap/race; four screens × five widths × two themes.');
    console.log('Owned evidence: '+directory+'; screenshots: '+JSON.stringify(Object.fromEntries(Object.entries(profiles).map(([role,c])=>[role,c.directory]))));
}
main().catch(async error=>{
    console.error('Failed stage: '+stage+'\n'+error.stack);
    for(const[role,client]of Object.entries(profiles))try{await client.screenshot('gps-failed-'+role);console.error(role+': '+client.directory);}catch{}
    console.error('Owned evidence: '+directory);process.exitCode=1;
}).finally(()=>{for(const client of Object.values(profiles))client.closeBrowser();server?.kill('SIGTERM');});

// Complete browser journey on an exclusively owned temporary SQLite database.
// Four Chromium profiles keep administrator, customer, driver and public cookies separate.
const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {mkdtempSync,createWriteStream} = require('node:fs');
const {tmpdir} = require('node:os');
const {join,resolve} = require('node:path');
const net = require('node:net');
const {createBrowserClient} = require('./command-center-browser-check.cjs');
const directory = mkdtempSync(join(tmpdir(),'mgl-e2e-journey-'));
const pause = ms => new Promise(resolve=>setTimeout(resolve,ms));
let server;
let roadProvider;let roadMode="ok";
let photonProvider;
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
async function startServer(restored, reusePort) {
    if((process.env.MGL_ROUTING_UI_CHECK==='provider'||process.env.MGL_ROAD_PROXY==='true') && !roadProvider){
        const http=require('node:http'),fs=require('node:fs');
        const fixture=JSON.parse(fs.readFileSync(resolve(__dirname,'../src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json'),'utf8'));
        roadProvider=http.createServer((req,res)=>{
            res.setHeader('Content-Type','application/json');
            if(roadMode==='unavailable'){res.statusCode=503;res.end('{}');return;}
            if(roadMode==='no-route'){res.end(JSON.stringify({code:'NoRoute',routes:[]}));return;}
            if(process.env.MGL_ROAD_PROXY==='true') {
                const upstream=new URL(process.env.MGL_OSRM_BASE_URL);
                if(!['127.0.0.1','localhost'].includes(upstream.hostname))throw new Error('Road proxy requires a local engine');
                http.get(new URL(req.url,upstream),remote=>{res.statusCode=remote.statusCode;remote.pipe(res);}).on('error',()=>{res.statusCode=503;res.end('{}');});
                return;
            }
            const reply={code:'Ok',data_version:'local-protocol-fixture',routes:[{distance:4962,duration:410.25,geometry:{type:'LineString',coordinates:fixture.RecordedPlan.TrajetoCoordenadas.map(p=>[p.Longitude,p.Latitude])},legs:[]}]};
            if(roadMode==='invalid')reply.routes[0].geometry.coordinates=[];
            const body=JSON.stringify(reply);
            if(roadMode==='slow')setTimeout(()=>res.end(body),1500);else res.end(body);
        });
        await new Promise(resolve=>roadProvider.listen(0,'127.0.0.1',resolve));
    }
    if(process.env.MGL_GEOGRAPHIC_CHECK==='true'&&!photonProvider){
        const http=require('node:http'),fs=require('node:fs');
        const recorded=JSON.parse(fs.readFileSync(resolve(__dirname,'../src/MglConnect.Tests/Fixtures/geocoding/photon-public-landmarks.json'),'utf8'));
        photonProvider=http.createServer((req,res)=>{
            const query=new URL(req.url,'http://127.0.0.1').searchParams.get('q')?.toLowerCase()||'';
            res.setHeader('Content-Type','application/json');
            res.end(JSON.stringify(query.includes('sé')?recorded.destination:query.includes('museu')||query.includes('paulista')?recorded.origin:{features:[]}));
        });await new Promise(resolve=>photonProvider.listen(0,'127.0.0.1',resolve));
    }
    const port=reusePort||await new Promise((resolve,reject)=>{
        const probe=net.createServer();probe.once('error',reject);
        probe.listen(0,'127.0.0.1',()=>{const port=probe.address().port;probe.close(()=>resolve(port));});
    });
    const log=createWriteStream(join(directory,'server.log'));
    const env={...process.env,MGL_LOCAL_PORT:String(port),MGL_LOCAL_DATA_DIR:join(directory,'database'),GpsHistory__DiasAntesCompactar:'0'};
    if(process.env.MGL_ROUTING_UI_CHECK==='historical')env.MGL_ROAD_CAPTURE=resolve(__dirname,'../src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json');
    if(photonProvider){env.MGL_PHOTON_ENABLED='true';env.MGL_PHOTON_BASE_URL='http://127.0.0.1:'+photonProvider.address().port;}
    if(roadProvider)env.MGL_OSRM_BASE_URL='http://127.0.0.1:'+roadProvider.address().port;
    if(restored) {
        for(const name of ['POSTGRES_URL','DATABASE_URL','POSTGRES_PRISMA_URL','PORT','VERCEL','GOOGLE_MAPS_API_KEY'])delete env[name];
        Object.assign(env,{ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:'http://127.0.0.1:'+port,
            DatabaseProvider:'Sqlite',ConnectionStrings__DefaultConnection:'Data Source='+join(restored,'database.sqlite')+';Foreign Keys=True',
            DataProtection__KeysPath:join(restored,'keys'),DemoData__Enabled:'false',Geocoding__PhotonEnabled:'false',Routing__OsrmBaseUrl:'',GoogleMaps__ApiKey:'',GoogleMaps__GeocodingApiKey:''});
    }
    const dotnet=join(process.env.HOME,'.local/share/mglconnect-dotnet10/dotnet');
    server=spawn(restored?dotnet:'bash',restored?[resolve(__dirname,'../src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll'),'--contentRoot',resolve(__dirname,'../src/MglConnect.Web')]:['scripts/dev-local.sh'],
        {cwd:resolve(__dirname,'..'),env,stdio:['ignore','pipe','pipe']});
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
    assert.ok(await client.evaluate(`document.querySelector('.cc-demo-access')?.textContent.includes(${JSON.stringify(name==='motorista.qa'?'carlos.motorista':name)})`),'Use only the owned Development demo.');
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
async function repeatCurrentAssignments(id) {
    for(const action of ['AtribuirMotorista','AtribuirVeiculo']) {
        for(let retry=0;retry<2;retry++) {
            const result=await profiles.admin.evaluate(`(async()=>{
                const form=[...document.forms].find(form=>new URL(form.action).pathname==='/Entregas/${action}');
                if(!form || form.elements.entregaId.value!==${JSON.stringify(String(id))}) throw new Error('Missing current assignment form.');
                const response=await fetch(form.action,{method:'POST',credentials:'same-origin',body:new FormData(form)});
                const page=new DOMParser().parseFromString(await response.text(),'text/html');
                return {ok:response.ok,path:new URL(response.url).pathname,status:page.getElementById('adminDeliveryDetails')?.dataset.deliveryStatus};
            })()`);
            assert.ok(result.ok);
            assert.equal(result.path,'/Entregas/Details/'+id);
            assert.equal(result.status,'Pendente');
        }
    }
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
async function ownedQueueCount() {
    return profiles.driver.evaluate(`(async()=>{
        const shell=document.getElementById('driverOfflineQueueState');
        const owner=shell.dataset.offlineUser+':'+shell.dataset.offlineTenant.toLowerCase();
        return (await new MglIndexedPodStore().list(owner)).length;
    })()`);
}
async function responsive(client, role) {
    await client.command('Accessibility.enable');
    const tree=await client.command('Accessibility.getFullAXTree');
    const unnamed=tree.nodes.filter(n=>!n.ignored&&['button','link','textbox','combobox','checkbox','spinbutton'].includes(n.role?.value)&&!n.name?.value?.trim());
    assert.deepEqual(unnamed.map(n=>({role:n.role?.value,node:n.backendDOMNodeId})),[],role+' controls must have accessible names');
    assert.ok(await client.evaluate(`document.documentElement.lang.toLowerCase().startsWith('pt')`),role+' language');
    for(const theme of ['dark','light']) {
        await client.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)};document.documentElement.style.colorScheme=${JSON.stringify(theme)}`);
        for(const width of [1440,1280,768,390,360]) {
            await client.viewport(width,width<500?844:1000);
            const layout=await client.evaluate(`({width:innerWidth,scroll:document.documentElement.scrollWidth,offenders:[...document.querySelectorAll('main *')].filter(element=>{const r=element.getBoundingClientRect();return r.width && r.right>innerWidth+1 && !element.closest('.leaflet-pane')}).slice(0,3).map(element=>element.tagName+'.'+element.className)})`);
            assert.ok(layout.scroll<=layout.width,role+' overflow: '+JSON.stringify(layout));
            if(role==='driver') assert.ok(await client.evaluate('document.documentElement.scrollHeight<=innerHeight+1'), 'Cockpit alerts must not push navigation and emergency actions below the viewport.');
            await client.screenshot('journey-final-'+role+'-'+theme+'-'+width);
        }
    }
    await client.key('Tab');
    assert.equal(await client.evaluate(`!!document.activeElement?.closest('[inert]')`),false,role+' keyboard focus must stay outside inert content');
}
async function confirmGeography(client,id,{via=false}={}) {
    await client.navigate('/Roteamento?entregaId='+id);
    const before=await client.evaluate("document.getElementById('linkedRoadPlan')?.dataset.planId||null");
    for(const index of [0,1]) {
        if(process.env.MGL_PREVIEW_READY_DIR)await client.evaluate(`(()=>{const input=document.querySelectorAll('#geographicPlanning fieldset')[${index}].querySelector('input[type=text]');input.value=${JSON.stringify(index===0?'Museu de Arte de São Paulo Assis Chateaubriand, São Paulo':'Praça da Sé, São Paulo')};input.dispatchEvent(new Event('input'));})()`);
        await client.evaluate(`document.querySelectorAll('#geographicPlanning fieldset')[${index}].querySelector('button').click()`);
        await until(client,`document.querySelectorAll('#geographicPlanning fieldset')[${index}].querySelectorAll('.cc-geographic-results button').length>0`,'Photon candidates '+index);
        assert.equal(await client.evaluate(`document.querySelectorAll('#geographicPlanning fieldset')[${index}].querySelector('input[type=checkbox]').checked`),false);
        await client.evaluate(`(()=>{const f=document.querySelectorAll('#geographicPlanning fieldset')[${index}];f.querySelector('.cc-geographic-results button').click();f.querySelector('input[type=checkbox]').click();})()`);
    }
    if(via) for(const [offset,latitude,longitude] of (process.env.MGL_DRAFT_ROUTE_CHECK==='true'?[[0,-23.5555,-46.6535],[1,-23.557,-46.648]]:[[0,-23.5555,-46.6535]])) {
        await client.evaluate("document.getElementById('addGeographicStop').click()");
        await client.evaluate(`(()=>{const f=document.querySelectorAll('#geographicPlanning fieldset')[${offset+1}],input=f.querySelector('input[type=text]');input.value='Parada fictícia ${offset+1} · Bela Vista (seleção no mapa)';input.dispatchEvent(new Event('input'));f.querySelectorAll('button')[1].click();window.mglPlanningMap.stop();window.mglPlanningMap.setView([${latitude},${longitude}],16,{animate:false,reset:true});})()`);
        await until(client,"!window.mglPlanningMap._animatingZoom&&!window.mglPlanningMap._panAnim?._inProgress",'Map animation settled');
        await client.pause(150);
        const center=await client.evaluate(`(()=>{const map=window.mglPlanningMap,r=document.getElementById('truckMap').getBoundingClientRect(),p=map.latLngToContainerPoint([${latitude},${longitude}]);if(p.x<0||p.x>r.width||p.y<0||p.y>r.height)throw new Error('Intended waypoint outside current viewport');return {x:r.x+p.x,y:r.y+p.y};})()`);
        await client.command('Input.dispatchMouseEvent',{type:'mousePressed',...center,button:'left',buttons:1,clickCount:1});
        await client.command('Input.dispatchMouseEvent',{type:'mouseReleased',...center,button:'left',clickCount:1});
        assert.equal(await client.evaluate(`document.querySelectorAll('#geographicPlanning fieldset')[${offset+1}].querySelector('input[type=checkbox]').checked`),false);
        await client.evaluate(`document.querySelectorAll('#geographicPlanning fieldset')[${offset+1}].querySelector('input[type=checkbox]').click()`);
    }
    assert.equal(await client.evaluate("document.getElementById('saveConfirmedRoute').disabled"),true,'Publishing requires a fresh route preview');
    const preview=async()=>{await client.evaluate("document.getElementById('previewConfirmedRoute').click()");await until(client,"['ready','unavailable','error'].includes(document.getElementById('geographicPreview').dataset.state)",'Draft preview completed');};
    await preview();
    assert.equal(await client.evaluate("document.getElementById('geographicPreview').dataset.state"),'ready');
    assert.equal(await client.evaluate("document.getElementById('linkedRoadPlan')?.dataset.planId||null"),before,'Preview cannot publish or replace the saved plan');
    assert.ok(await client.evaluate("[...document.querySelectorAll('#truckMap path')].some(p=>p.getAttribute('stroke-dasharray')==='8 10')"),'Draft geometry is visible and distinct');
    if(process.env.MGL_DRAFT_ROUTE_CHECK==='true') {
        await client.evaluate("document.querySelector('#geographicPlanning input[type=checkbox]').click()");
        assert.equal(await client.evaluate("document.getElementById('saveConfirmedRoute').disabled"),true);
        assert.equal(await client.evaluate("document.getElementById('geographicPreview').hidden"),true);
        await client.evaluate("document.querySelector('#geographicPlanning input[type=checkbox]').click()");
        roadMode='unavailable';await preview();
        assert.equal(await client.evaluate("document.getElementById('geographicPreview').dataset.state"),'unavailable');
        assert.equal(await client.evaluate("document.getElementById('saveConfirmedRoute').disabled"),true);
        assert.equal(await client.evaluate("document.getElementById('linkedRoadPlan')?.dataset.planId||null"),before);
        await client.screenshot('delivery8-preview-unavailable');roadMode='ok';
        await client.evaluate("window.__draftFetch=window.fetch;window.fetch=async(...a)=>{const r=await window.__draftFetch(...a);if(String(a[0]).includes('/Roteamento/Previa'))await new Promise(resolve=>setTimeout(resolve,600));return r;};document.getElementById('previewConfirmedRoute').click()");
        await until(client,"document.getElementById('geographicPreview').dataset.state==='calculating'",'Preview request in flight');
        await client.evaluate("document.querySelector('#geographicPlanning input[type=checkbox]').click()");await client.pause(850);
        assert.equal(await client.evaluate("document.getElementById('saveConfirmedRoute').disabled"),true,'Late draft cannot authorize a changed confirmation');
        assert.equal(await client.evaluate("document.getElementById('geographicPreview').hidden"),true);
        await client.evaluate("window.fetch=window.__draftFetch;delete window.__draftFetch;document.querySelector('#geographicPlanning input[type=checkbox]').click()");await preview();
        assert.equal(await client.evaluate("document.getElementById('geographicPreview').dataset.state"),'ready');
    }
    assert.equal(await client.evaluate("document.getElementById('saveConfirmedRoute').disabled"),false);
    await client.screenshot(via?'delivery8-preview-with-stop':'delivery8-preview-origin-destination');
    await client.evaluate("document.getElementById('viewDraftOnMap').click()");
    await until(client,"(()=>{const r=document.getElementById('truckMap').getBoundingClientRect();return r.bottom>0&&r.top<innerHeight;})()",'Draft map reachable from confirmation');
    await client.evaluate("document.querySelector('#geographicPreviewMap a').click()");
    if(process.env.MGL_DRAFT_ROUTE_CHECK==='true'&&via) {
        for(const theme of ['dark','light'])for(const width of [320,360,390,428,768,1024,1440]) {
            await client.viewport(width,width<500?844:1000);await client.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)}`);
            assert.ok(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),'Draft planning layout fits '+width);
            assert.ok(await client.evaluate("(()=>{const e=document.getElementById('saveConfirmedRoute');e.scrollIntoView({block:'center',behavior:'instant'});const r=e.getBoundingClientRect();return r.height>=44&&e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));})()"),'Publish action reachable '+width);
            if(width===390)await client.screenshot('delivery8-preview-mobile-'+theme);
        }
    }
    await client.pause(300);
    await client.screenshot(via?'delivery5-three-points-confirmed':'delivery5-photon-points-confirmed');
    await client.evaluate("document.getElementById('saveConfirmedRoute').click()");
    await until(client,`document.readyState==='complete'&&!!document.getElementById('linkedRoadPlan')&&document.getElementById('linkedRoadPlan').dataset.planId!==${JSON.stringify(before)}`,'Confirmed road planning persisted');
    assert.equal(await client.evaluate("document.getElementById('routeResult').dataset.routeState"),'GenericUnverifiedTruck');
}

async function checkOperationalCrud(client) {
    const fill=async(selector,fields)=>client.evaluate(`(()=>{const f=document.querySelector(${JSON.stringify(selector)});for(const[k,v]of Object.entries(${JSON.stringify(fields)})){if(!f.elements[k])throw new Error('Missing CRUD field '+k);f.elements[k].value=v;}f.requestSubmit();})()`);
    await client.navigate('/Clientes/Create');
    await fill('form[action="/Clientes/Create"]',{RazaoSocial:'ClienteSinteticoOperacionalSemEspacosParaTesteMobile',NomeFantasia:'Empresa fictícia QA',Cnpj:'00.000.000/0000-06',Telefone:'00000000000',Email:'qa-ficticio@example.invalid',Cidade:'Cidade de teste',Estado:'SP'});
    await until(client,"location.pathname==='/Clientes'&&document.readyState==='complete'",'Administrative customer created');
    const customer=await client.evaluate("(()=>{const row=[...document.querySelectorAll('tbody tr')].find(r=>r.textContent.includes('ClienteSinteticoOperacional'));return Number(new URL(row.querySelector('a[href*=\"/Clientes/Details/\"]').href).pathname.split('/').pop());})()");
    await client.navigate('/Clientes/Edit/'+customer);await fill('form[action^="/Clientes/Edit"]',{NomeFantasia:'Empresa QA editada'});
    await until(client,"location.pathname==='/Clientes'&&document.readyState==='complete'",'Customer update');
    await client.navigate('/Clientes/Details/'+customer);
    await client.screenshot('delivery6-before-customer-details');
    assert.equal(await client.evaluate("document.querySelector('main').textContent.includes('<span')"),false,'Customer status renders a badge, not escaped HTML');
    for(const theme of ['dark','light'])for(const width of [320,360,390,428,768,1024]) {
        await client.viewport(width,width<500?844:1000);await client.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)}`);
        await client.evaluate("Promise.all(document.getElementById('commandSidebar').getAnimations().map(a=>a.finished.catch(()=>{})))");
        await client.screenshot('delivery6-customer-'+theme+'-'+width);
        assert.ok(await client.evaluate("(()=>{const b=document.querySelector('a[href*=\"/Clientes/Edit/\"]');b.scrollIntoView({block:'center',behavior:'instant'});const r=b.getBoundingClientRect();return r.height>=44&&b.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));})()"),'Customer edit action reachable '+width);
        assert.ok(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),'Customer mobile layout fits '+width);
    }
    await client.viewport(1440,1000);
    await client.navigate('/Frota/NovoVeiculo');
    await fill('#fleetVehicleForm',{Modelo:'Veículo fictício E2E',Placa:'QAQ6A01',CapacidadeCargaKg:'6000.50',PesoToneladas:'12.50',AlturaMetros:'3.20',LarguraMetros:'2.40',ComprimentoMetros:'8.50',QtdEixos:'2',TipoCarga:'Carga Geral Seca'});
    await until(client,"new RegExp('^/Frota/Veiculo/[0-9]+$').test(location.pathname)&&document.readyState==='complete'",'Vehicle created');
    const vehicle=await client.evaluate("Number(location.pathname.split('/').pop())");
    await client.navigate('/Frota/EditarVeiculo/'+vehicle);await fill('#fleetVehicleForm',{Modelo:'Veículo fictício E2E editado',CapacidadeCargaKg:'6500.75'});
    await until(client,`location.pathname==='/Frota/Veiculo/${vehicle}'&&document.readyState==='complete'`,'Vehicle edited');
    await client.navigate('/Frota/NovoMotorista');
    await fill('#fleetDriverForm',{Nome:'Motorista E2E cadastrado',Login:'motorista.qa',SenhaInicial:'Synthetic-driver-123!',Telefone:'00000000000'});
    await until(client,"new RegExp('^/Frota/Motorista/[0-9]+$').test(location.pathname)&&document.readyState==='complete'",'Driver created');
    const driver=await client.evaluate("Number(location.pathname.split('/').pop())");
    await client.navigate('/Frota/EditarMotorista/'+driver);await fill('#fleetDriverForm',{Nome:'Motorista E2E editado'});
    await until(client,`location.pathname==='/Frota/Motorista/${driver}'&&document.readyState==='complete'`,'Driver edited');
    await client.screenshot('delivery6-driver-created-edited');
    return {driverLogin:'motorista.qa',driverName:'Motorista E2E editado',vehicleName:'Veículo fictício E2E editado'};
}

async function main() {
    let baseUrl;
    if(process.env.MGL_PREVIEW_READY_DIR) {
        const {existsSync}=require('node:fs');const preview=resolve(process.env.MGL_PREVIEW_READY_DIR);
        assert.ok(existsSync(join(preview,'.mgl-local-preview'))&&existsSync(join(preview,'mglconnect.db')),'Explicit disposable preview marker/database required');
        baseUrl=process.env.MGL_UI_BASE_URL;assert.ok(baseUrl&&['127.0.0.1','localhost'].includes(new URL(baseUrl).hostname));
        assert.notEqual(process.env.MGL_ROAD_PROXY,'true');
    }else baseUrl=await startServer();
    stage='open independent Chromium profiles';
    profiles=Object.fromEntries((process.env.MGL_OPERATIONS_CHECK==='true'?['admin','client','driver','public','broker']:['admin','client','driver','public']).map(role=>[role,createBrowserClient({baseUrl})]));
    // Stagger startup; all four authenticated profiles remain independent and live.
    for(const [role,client] of Object.entries(profiles)) {
        await client.openBrowser();await client.command('Runtime.enable');
        await client.command('Network.enable');
        client.onEvent('Network.responseReceived',event=>{
            if(event.response.status>=500 && new URL(event.response.url).origin===baseUrl)
                errors.push(role+': HTTP '+event.response.status+' '+new URL(event.response.url).pathname);
        });
        client.onEvent('Runtime.exceptionThrown',event=>errors.push(role+': '+(event.exceptionDetails.exception?.description||event.exceptionDetails.text)));
        await client.viewport(role==='driver'||role==='public'?390:1440,role==='driver'||role==='public'?844:1000);
    }
    // Keep CDP response-loss interception on the isolated driver page. Production
    // Service Worker rules have their own tests; IndexedDB and actual HTTP remain real.
    await profiles.driver.command('Page.addScriptToEvaluateOnNewDocument',{source:`
        window.__mglTestSockets=[];
        const NativeWebSocket=window.WebSocket;
        window.WebSocket=class extends NativeWebSocket {
            constructor(...args){super(...args);window.__mglTestSockets.push(this);}
        };
    `});
    await profiles.driver.command('Network.setBypassServiceWorker',{bypass:true});
    await profiles.driver.command('Page.addScriptToEvaluateOnNewDocument',{source:`if(navigator.serviceWorker)navigator.serviceWorker.register=async()=>{throw new Error('Journey transport fixture');};`});
    stage='authenticate the three isolated roles';
    await login(profiles.admin,'admin','admin123','/Dashboard');
    await login(profiles.client,'cliente.demo','cliente123','/Cliente');
    await login(profiles.driver,'carlos.motorista','motorista123','/Entregas');
    if(profiles.broker)await login(profiles.broker,'intermediador.demo','cliente123','/Cliente');
    if(process.env.MGL_ROUTING_UI_CHECK){
        stage='routing page and provider protocol regression';
        await require('./route-restrictions-browser-check.cjs').checkRoutePlanning(profiles.admin,{historical:process.env.MGL_ROUTING_UI_CHECK==='historical',setMode:mode=>{roadMode=mode;}});
    }
    stage='direct server authorization checks';
    await profiles.public.navigate('/Account/Login');
    for(const [role,path,status] of [
        ['public','/api/gps/historico/1',401],
        ['driver','/api/gps/historico/999999999',404],
        ['client','/api/gps/historico/999999999',404]
    ]) {
        assert.equal(await profiles[role].evaluate(`fetch(${JSON.stringify(path)}).then(r=>r.status)`),status,role+' '+path);
    }
    for(const role of (profiles.broker?['driver','client','broker']:['driver','client'])) {
        const denied=await profiles[role].evaluate(`fetch('/Dashboard').then(r=>({status:r.status,path:new URL(r.url).pathname}))`);
        assert.ok(denied.status===403 || denied.path==='/Account/AccessDenied',role+' administrative access denied');
    }
    stage='release seeded synthetic assignment through actual shift and POD';
    await startShift(1,await issueCode(1));
    stage='driver GPS hit testing, geometry and responsive regression';
    await require('./driver-navigation-browser-check.cjs').checkDriverNavigation(profiles.driver);
    if(process.env.MGL_DEMO_ROUTE_FAILURE){
        console.log('Controlled provider failure scenario passed: '+process.env.MGL_DEMO_ROUTE_FAILURE);
        console.log('Owned evidence: '+directory+'; driver screenshots: '+profiles.driver.directory);
        assert.deepEqual(errors,[]);return;
    }
    await drawAndSubmitPod(1,'Synthetic warmup receiver');
    await until(profiles.driver,`document.getElementById('btnSubmitPODNav').textContent==='Entrega confirmada'`,'Warmup POD confirmed');
    assert.equal(await ownedQueueCount(),0);

    let resources;
    if(process.env.MGL_OPERATIONS_CHECK==='true'){
        stage='administrative customers, vehicles and drivers CRUD';resources=await checkOperationalCrud(profiles.admin);
        await profiles.driver.navigate('/Entregas');
        await profiles.driver.evaluate("document.querySelector('form[action=\"/Account/Logout\"]').requestSubmit()");
        await until(profiles.driver,"location.pathname==='/Account/Login'&&document.readyState==='complete'",'Seed driver logout');
        await login(profiles.driver,resources.driverLogin,'Synthetic-driver-123!','/Entregas');
    }
    const requester=process.env.MGL_REQUESTER==='broker'?profiles.broker:profiles.client;
    assert.ok(requester);
    stage='customer creates a new order';
    const cargo='Browser journey '+Date.now();
    await requester.navigate('/Cliente/Pedido/Novo');
    const fields={Descricao:cargo,TipoCarga:'Carga Geral Seca',PesoKg:'1200.50',ValorTotal:'5000',
        OrigemEndereco:'Rua de coleta sintética',OrigemNumero:'10',OrigemBairro:'Distrito de teste',OrigemCidade:'Campinas',OrigemEstado:'SP',OrigemCep:'00000-000',
        DestinoEndereco:'Rua de destino sintético',DestinoNumero:'20',DestinoBairro:'Distrito de teste',DestinoCidade:'Santos',DestinoEstado:'SP',DestinoCep:'00000-000'};
    if(process.env.MGL_LINKED_ROAD==='true')Object.assign(fields,{
        OrigemEndereco:'Avenida Paulista · MASP (marco público de teste)',OrigemNumero:'1578',OrigemBairro:'Bela Vista',OrigemCidade:'São Paulo',OrigemEstado:'SP',OrigemCep:'01310-200',
        DestinoEndereco:'Praça da Sé · marco público de teste',DestinoNumero:'s/n',DestinoBairro:'Sé',DestinoCidade:'São Paulo',DestinoEstado:'SP',DestinoCep:'01001-000'});
    const replayFields=await requester.evaluate(`(()=>{const form=document.getElementById('clientOrderForm');for(const[name,value]of Object.entries(${JSON.stringify(fields)})){if(!form.elements[name])throw new Error('Missing field '+name);form.elements[name].value=value;}const replay=[...new FormData(form).entries()];form.requestSubmit();return replay;})()`);
    await until(requester,`new RegExp('^/Cliente/Pedido/[0-9]+$').test(location.pathname) && document.readyState==='complete'`,'Customer order saved');
    const orderId=await requester.evaluate(`Number(location.pathname.split('/').pop())`);
    assert.equal(await requester.evaluate(`document.getElementById('clientOrderDetails').dataset.hasDelivery`),'false');
    await requester.screenshot('journey-customer-order-created');
    if(profiles.broker) {
        const outsider=requester===profiles.broker?profiles.client:profiles.broker;
        assert.equal(await outsider.evaluate(`fetch('/Cliente/Pedido/${orderId}').then(r=>r.status)`),404,'Other customer cannot inspect order');
        assert.equal(await outsider.evaluate(`fetch('/Cliente/Pedido/${orderId}/Comprovante').then(r=>r.status)`),404,'Other customer cannot read POD');
        const replay=await requester.evaluate(`fetch('/Cliente/Pedido/Novo',{method:'POST',body:new URLSearchParams(${JSON.stringify(replayFields)})}).then(r=>({status:r.status,path:new URL(r.url).pathname}))`);
        assert.equal(replay.path,'/Cliente/Pedido/'+orderId,'Duplicate order submission keeps the original order');
        assert.equal(replay.status,200);
    }


    stage='administrator validates and dispatches';
    await profiles.admin.navigate('/Pedidos?termo='+encodeURIComponent(cargo));
    assert.ok(await profiles.admin.evaluate(`document.querySelector('main').textContent.includes(${JSON.stringify(cargo)})`));
    await profiles.admin.navigate('/Pedidos/Details/'+orderId);
    await profiles.admin.evaluate(`document.querySelector('button[name="status"][value="EmProcessamento"]').click()`);
    await until(profiles.admin,`document.readyState==='complete' && !document.querySelector('button[value="EmProcessamento"]') && document.querySelector('main').textContent.includes('Em processamento')`,'Administrative approval');
    await profiles.admin.navigate('/Entregas/Create?pedidoId='+orderId);
    await profiles.admin.evaluate(`(()=>{
        const form=document.getElementById('dispatchOrderForm');
        const driver=[...form.elements.MotoristaId.options].find(option=>option.textContent.includes(${JSON.stringify(resources?.driverName||'Motorista de Demonstração')}));
        const vehicle=[...form.elements.VeiculoId.options].find(option=>option.textContent.includes(${JSON.stringify(resources?.vehicleName||'Scania')}));
        if(!driver||!vehicle)throw new Error('Expected owned demo resources.');
        form.elements.MotoristaId.value=driver.value;form.elements.VeiculoId.value=vehicle.value;
        form.elements.MotoristaId.dispatchEvent(new Event('change'));form.elements.VeiculoId.dispatchEvent(new Event('change'));
        form.elements.DestinatarioNome.value='Browser Journey Recipient';form.requestSubmit();
    })()`);
    await until(profiles.admin,`new RegExp('^/Entregas/Details/[0-9]+$').test(location.pathname) && document.readyState==='complete'`,'Dispatch persisted');
    const deliveryId=await profiles.admin.evaluate(`Number(location.pathname.split('/').pop())`);
    const trackingCode=await profiles.admin.evaluate(`new URL(document.querySelector('a[href^="/Rastreio/"]').href).pathname.split('/').pop()`);
    assert.ok(deliveryId>1 && trackingCode);
    await profiles.admin.screenshot('journey-dispatch-created');
    if(process.env.MGL_LINKED_ROAD==='true') {
        stage='calculate road route and bind to the dispatched delivery';
        await profiles.admin.navigate('/Roteamento?entregaId='+deliveryId);
        await profiles.admin.screenshot('delivery4-before-linked-planning');
        if(process.env.MGL_GEOGRAPHIC_CHECK==='true')await confirmGeography(profiles.admin,deliveryId);
        else await profiles.admin.evaluate(`(()=>{const f=document.getElementById('deliveryRoadPlanForm');f.elements.coordenadas.value='-46.6559,-23.5614;-46.6333,-23.5505';f.requestSubmit();})()`);
        await until(profiles.admin,"!!document.getElementById('linkedRoadPlan')&&document.readyState==='complete'",'Road planning persisted');
        assert.equal(await profiles.admin.evaluate("document.getElementById('routeResult').dataset.routeState"),'GenericUnverifiedTruck');
        for(const theme of ['dark','light']) {
            await profiles.admin.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)}`);
            for(const width of [360,390,428,768,1024,1440]) {
                await profiles.admin.viewport(width,width<500?844:1000);
                assert.ok(await profiles.admin.evaluate('document.documentElement.scrollWidth<=innerWidth'));
                await profiles.admin.screenshot('delivery4-linked-admin-'+theme+'-'+width);
            }
        }
    }

    stage='assigned driver starts shift and streams synthetic device GPS';
    const shiftCode=await issueCode(deliveryId);
    await repeatCurrentAssignments(deliveryId);
    await startShift(deliveryId,shiftCode);
    if(process.env.MGL_PREVIEW_READY_DIR) {
        const plan=await profiles.driver.evaluate(`fetch('/Entregas/${deliveryId}/Planejamento').then(r=>r.json()).then(x=>x.plan)`);
        assert.equal(plan.geometrySource,'RoadNetwork');assert.ok(plan.rotaDisponivel);assert.equal(plan.demonstracaoControlada,false);
        await profiles.driver.screenshot('delivery4-preview-ready');
        console.log('Preview ready (trip deliberately NOT completed): '+baseUrl+'/Entregas/Navegar/'+deliveryId);
        console.log('Screenshots: '+JSON.stringify(Object.fromEntries(Object.entries(profiles).map(([role,client])=>[role,client.directory]))));
        return;
    }
    await profiles.public.navigate('/Rastreio/'+trackingCode);
    await until(profiles.public,`document.getElementById('trackingConnectionStatus').dataset.state==='connected'`,'Public tracking subscription');
    await requester.navigate('/Cliente/Pedido/'+orderId);
    await until(requester,`document.getElementById('clientOrderDetails').dataset.hasDelivery==='true'`,'Private customer shipment');
    const controlled=await profiles.driver.evaluate("!!document.getElementById('demoRouteControls')");
    if(process.env.MGL_ROAD_PROXY==='true') {
        stage='linked provider failure, no-route and recovery in actual cockpits';
        for(const [mode,state] of [['unavailable','ProviderUnavailable'],['no-route','NoRoute'],['ok','GenericUnverifiedTruck']]) {
            roadMode=mode;
            await profiles.admin.navigate('/Roteamento?entregaId='+deliveryId);
            await profiles.admin.evaluate("document.getElementById('deliveryRoadPlanForm').requestSubmit()");
            await until(profiles.admin,`document.readyState==='complete'&&document.getElementById('routeResult').dataset.routeState===${JSON.stringify(state)}`,'Linked '+mode);
            await profiles.driver.evaluate('window.mglControlledDemo.refresh()');
            if(mode!=='ok') {
                assert.equal(await profiles.admin.evaluate("document.getElementById('routeDistance').textContent"),'—');
                assert.equal(await profiles.driver.evaluate('window.mglControlledDemo.tracker.routeCoordinates.length'),0);
                assert.equal(await profiles.driver.evaluate("document.getElementById('distanceRemaining').textContent"),'—');
                assert.equal(await profiles.driver.evaluate("getComputedStyle(document.getElementById('btnToggleGps')).display!=='none'"),true);
            }else assert.ok(await profiles.driver.evaluate('window.mglControlledDemo.tracker.routeCoordinates.length>10'));
            await profiles.driver.screenshot('delivery4-linked-provider-'+mode);
        }
        await profiles.driver.navigate('/Entregas/Navegar/'+deliveryId);
        await until(profiles.driver,"!!window.mglControlledDemo?.tracker.routePolyline",'Recovered persisted road plan');
    }

    if(process.env.MGL_GEOGRAPHIC_CHECK==='true') {
        stage='private live geographic planning revision without reloading driver';
        const old=await profiles.driver.evaluate(`(()=>{window.__mglDelivery5Document=true;const t=window.mglControlledDemo.tracker;t.setFollowVehicle(false);return {id:document.getElementById('demoRouteControls').dataset.planId,revision:t.planRevision,coords:t.routeCoordinates,center:[t.map.getCenter().lat,t.map.getCenter().lng]};})()`);
        await profiles.driver.screenshot('delivery5-before-live-change');
        await confirmGeography(profiles.admin,deliveryId,{via:true});
        const saved=await profiles.admin.evaluate(`fetch('/Entregas/${deliveryId}/Planejamento').then(r=>r.json()).then(x=>x.plan)`);
        await profiles.admin.screenshot('delivery5-saved-v2-admin');
        assert.ok(saved.planRevision>old.revision);assert.equal(saved.confirmedStops.length,process.env.MGL_DRAFT_ROUTE_CHECK==='true'?4:3);assert.equal(saved.confirmedStops[1].source,'Map');assert.ok(Math.abs(saved.confirmedStops[1].latitude+23.5555)<0.00015&&Math.abs(saved.confirmedStops[1].longitude+46.6535)<0.00015,'Native map selection targets the intended public waypoint');assert.notDeepEqual(saved.trajetoCoordenadas.map(p=>[p.latitude,p.longitude]),old.coords,'A changed waypoint changes actual provider geometry');
        await until(profiles.driver,`document.getElementById('planningUpdateNotice').dataset.revision===${JSON.stringify(String(saved.planRevision))}&&document.getElementById('demoRouteControls').dataset.planId===${JSON.stringify(saved.planningId)}`,'Driver receives private revision and reloads geometry automatically');
        assert.equal(await profiles.driver.evaluate('window.__mglDelivery5Document'),true,'Document was not reloaded');
        const current=await profiles.driver.evaluate('(()=>{const t=window.mglControlledDemo.tracker;return {coords:t.routeCoordinates,center:[t.map.getCenter().lat,t.map.getCenter().lng],allowed:t.navigationAllowed};})()');
        assert.deepEqual(current.coords,saved.trajetoCoordenadas.map(p=>[p.latitude,p.longitude]));assert.deepEqual(current.center,old.center,'A manually controlled viewport is preserved');assert.equal(current.allowed,false);
        assert.match(await profiles.driver.evaluate("document.getElementById('planningUpdateText').textContent"),new RegExp('paradas intermediárias 0 → '+(process.env.MGL_DRAFT_ROUTE_CHECK==='true'?2:1)));
        assert.equal(await ownedQueueCount(),0);await profiles.driver.screenshot('delivery5-after-live-change');
        await profiles.driver.evaluate("document.getElementById('btnReviewRoute').click()");
        await profiles.driver.screenshot('delivery5-review-new-route');
        await profiles.admin.navigate('/Operacoes');
        await until(profiles.admin,"!!document.querySelector('#fleetTripList button')",'Operation lists linked trip before GPS');
        await profiles.admin.evaluate("document.querySelector('#fleetTripList button').click()");
        await until(profiles.admin,`document.getElementById('fleetMap').dataset.planId===${JSON.stringify(saved.planningId)}`,'Operation displays same new revision before GPS');
        await profiles.admin.screenshot('delivery5-same-revision-operation');
        await profiles.driver.evaluate("if(document.getElementById('btnFollowPosition').getAttribute('aria-pressed')==='false')document.getElementById('btnFollowPosition').click()");
        console.log('Geographic integration passed: Photon candidates, explicit operator confirmations, native map-selected stop, persisted revision and private automatic driver update without reload/recenter.');
    }
    stage='linked driver geometry, GPS and mobile regression';
    let roadResult;
    if(controlled) roadResult=await require('./driver-navigation-browser-check.cjs').checkDriverNavigation(profiles.driver,process.env.MGL_ROAD_PROXY==='true'?{
        setMode:mode=>{roadMode=mode;},recover:async()=>{
            await profiles.admin.navigate('/Roteamento?entregaId='+deliveryId);
            await profiles.admin.evaluate("document.getElementById('deliveryRoadPlanForm').requestSubmit()");
            await until(profiles.admin,"document.readyState==='complete'&&document.getElementById('routeResult').dataset.routeState==='GenericUnverifiedTruck'",'Recover failed recalculation');
        }}:{});
    else {
    await profiles.driver.command('Browser.grantPermissions',{origin:baseUrl,permissions:['geolocation']});
    await profiles.driver.command('Emulation.setGeolocationOverride',{latitude:-23.5505,longitude:-46.6333,accuracy:8});
    await profiles.driver.evaluate(`document.getElementById('btnToggleGps').click()`);
    await until(profiles.driver,`document.getElementById('positionSourceBadge').dataset.state==='received'`,'Server receives synthetic GPS');
    }
    await profiles.admin.navigate('/Operacoes');
    await until(profiles.admin,`document.getElementById('fleetTripList').textContent.includes(${JSON.stringify(trackingCode)}) && document.querySelectorAll('#fleetMap .leaflet-interactive').length===1`,'Actual map consumes persisted GPS');
    const position=await profiles.admin.evaluate(`fetch('/Operacoes/Posicoes').then(r=>r.json()).then(data=>data.items.find(item=>item.id===${deliveryId}))`);
    assert.equal(position.latitude,roadResult?roadResult.latest.latitude:controlled?-23.5500:-23.5505);assert.equal(position.longitude,roadResult?roadResult.latest.longitude:controlled?-46.6318:-46.6333);
    if(roadResult) {
        await profiles.admin.evaluate(`document.querySelector('#fleetTripList button').click()`);
        await until(profiles.admin,`document.getElementById('fleetMap').dataset.planId===${JSON.stringify(roadResult.plan.planningId)}`,'Operation displays same route version');
        const observed=await profiles.admin.evaluate(`fetch('/Entregas/${deliveryId}/Planejamento').then(r=>r.json()).then(x=>x.plan)`);
        assert.deepEqual(observed.trajetoCoordenadas,roadResult.plan.trajetoCoordenadas);
        assert.equal(await profiles.public.evaluate(`fetch('/Entregas/${deliveryId}/Planejamento',{redirect:'manual'}).then(r=>r.status)`),0);
        const history=await requester.evaluate(`fetch('/api/gps/historico/${deliveryId}').then(r=>r.json())`);
        assert.equal(history.total,0,'Synthetic fixes are absent from customer GPS projection');
    }
    if(controlled)assert.equal(position.isSimulated,true);
    await profiles.admin.screenshot('journey-map-observed-gps');
    await profiles.admin.navigate('/Entregas/Details/'+deliveryId);
    await until(profiles.admin,`document.getElementById('adminDeliveryDetails').dataset.deliveryStatus==='Em transporte'`,'Admin sees started shift');

    stage='POD commit with deliberately lost HTTP receipt';
    let lost=0;
    profiles.driver.onEvent('Fetch.requestPaused',async event=>{
        if(event.responseStatusCode===200 && lost===0) {lost++;await profiles.driver.command('Fetch.failRequest',{requestId:event.requestId,errorReason:'ConnectionClosed'});}
        else await profiles.driver.command('Fetch.continueRequest',{requestId:event.requestId});
    });
    await profiles.driver.command('Fetch.enable',{patterns:[{urlPattern:'*/api/entregas/'+deliveryId+'/pod',requestStage:'Response'}]});
    await drawAndSubmitPod(deliveryId,'Synthetic final receiver');
    // "Pending" is also rendered immediately on enqueue, before the first HTTP
    // request completes. Wait for the intercepted receipt before asserting it.
    for(let attempt=0;attempt<100 && lost===0;attempt++) await pause(100);
    await until(profiles.driver,`document.getElementById('btnSubmitPODNav').textContent==='Comprovante pendente'`,'Lost receipt stays pending');
    assert.equal(lost,1);assert.equal(await ownedQueueCount(),1);
    await profiles.driver.screenshot('journey-pod-pending-after-lost-receipt');
    await until(profiles.public,`document.getElementById('publicDeliveryStatus').textContent==='Entregue'`,'Public real event confirms completion');
    await until(requester,`!!document.getElementById('clientOrderPodHeading')`,'Private customer POD appears without page reload');
    await until(profiles.admin,`document.getElementById('adminDeliveryDetails').dataset.deliveryStatus==='Entregue'`,'Private administrator status/POD refreshes without page reload');

    stage='recover identical receipt and stop the completed cockpit';
    await profiles.driver.command('Fetch.disable');
    await profiles.driver.evaluate(`document.getElementById('retryOfflinePod').click()`);
    await until(profiles.driver,`document.getElementById('btnSubmitPODNav').textContent==='Entrega confirmada'`,'Matching idempotent server receipt');
    assert.equal(await ownedQueueCount(),0);
    assert.equal(await profiles.driver.evaluate(`document.getElementById('btnToggleGps').disabled`),true,'A confirmed delivery must stop its GPS cockpit.');
    assert.equal(await profiles.driver.evaluate(`document.getElementById('telemetryConnectionState').dataset.state`),'completed');
    assert.equal(await profiles.driver.evaluate(`document.getElementById('btnToggleGps').getAttribute('aria-pressed')`),'false');
    await profiles.driver.screenshot('journey-pod-confirmed-receipt');
    await profiles.driver.evaluate(`document.getElementById('modalPODNavegacao').querySelector('[data-bs-dismiss="modal"]').click()`);
    await until(profiles.driver,`!document.querySelector('.modal-backdrop')`,'POD dialog closes after confirmed receipt');
    await profiles.driver.screenshot('journey-completed-driver-cockpit');
    await requester.screenshot('journey-customer-completed-pod');
    await profiles.admin.screenshot('journey-admin-completed-pod');
    await profiles.public.screenshot('journey-public-completed');
    stage='desktop, tablet and mobile final states';
    for(const role of ['admin','client','driver','public']) await responsive(profiles[role],role);

    const pdf=await requester.evaluate(`(async()=>{const response=await fetch(document.querySelector('#clientOrderPodHeading').closest('header').querySelector('a').href);const bytes=new Uint8Array(await response.arrayBuffer());return{status:response.status,type:response.headers.get('Content-Type'),magic:String.fromCharCode(...bytes.slice(0,4))};})()`);
    assert.equal(pdf.status,200);assert.equal(pdf.type,'application/pdf');assert.equal(pdf.magic,'%PDF');
    assert.equal(await profiles.public.evaluate(`document.querySelector('.cc-tracking').textContent.includes('PRIVATE-BROWSER-DOC')`),false);
    await profiles.driver.navigate('/Entregas');
    assert.equal(await profiles.driver.evaluate(`!!document.querySelector('main a[href="/Entregas/Navegar/${deliveryId}"]')`),false);
    await profiles.admin.navigate('/Frota');
    assert.equal(await profiles.admin.evaluate(`document.getElementById('operationalPanel').textContent.includes(${JSON.stringify(trackingCode)})`),false);
    const active=await profiles.admin.evaluate(`fetch('/Operacoes/Posicoes').then(r=>r.json()).then(data=>data.items.some(item=>item.id===${deliveryId}))`);
    assert.equal(active,false);
    if(process.env.MGL_RECOVERY_CHECK==='1') {
        stage='archive the actual finished trip before a consistent live backup';
        const historyPath='/GpsHistorico?entregaId='+deliveryId;
        await profiles.admin.navigate(historyPath);
        async function submitArchive(action) {
            await profiles.admin.evaluate(`(async()=>{const f=[...document.forms].find(f=>new URL(f.action).pathname===${JSON.stringify(action)});if(!f)throw new Error('Missing recovery archive form');if(f.elements.confirmar)f.elements.confirmar.checked=true;const r=await fetch(f.action,{method:'POST',body:new FormData(f)});if(!r.ok)throw new Error('Archive action failed '+r.status);})()`);
            await profiles.admin.navigate(historyPath);
        }
        await submitArchive('/GpsHistorico/Arquivar');await submitArchive('/GpsHistorico/Compactar');
        const before=await profiles.admin.evaluate(`fetch('/GpsHistorico/Dados?entregaId=${deliveryId}').then(r=>r.json())`);
        assert.ok(before.total>0&&before.items.every(p=>p.arquivado));
        const {execFileSync}=require('node:child_process'),{existsSync}=require('node:fs');
        assert.ok(existsSync(join(directory,'database','.mgl-local-preview')));
        const dotnet=join(process.env.HOME,'.local/share/mglconnect-dotnet10/dotnet');
        const assembly=resolve(__dirname,'../src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll');
        const backup=join(directory,'verified-backup'),restored=join(directory,'restored');
        const recoveryTimes=[];
        const run=(...args)=>{const started=Date.now();const result=JSON.parse(execFileSync(dotnet,[assembly,'--operations',...args],{encoding:'utf8'}));recoveryTimes.push({operation:args[0],milliseconds:Date.now()-started});return result;};
        assert.equal(run('backup-sqlite',join(directory,'database','mglconnect.db'),backup,'--keys',join(directory,'database','keys')).success,true);
        assert.equal(run('verify',backup).success,true);assert.equal(run('restore',backup,restored).success,true);
        stage='unexpected application termination followed by isolated restored database and original key ring';
        const oldServer=server;const exited=new Promise(resolve=>oldServer.once('exit',resolve));oldServer.kill('SIGKILL');await exited;
        await startServer(restored,Number(new URL(baseUrl).port));
        await profiles.admin.navigate(historyPath);
        const after=await profiles.admin.evaluate(`fetch('/GpsHistorico/Dados?entregaId=${deliveryId}').then(async r=>{if(!r.ok)throw new Error('Restored history denied');return r.json()})`);
        assert.deepEqual(after.items.map(p=>p.eventoId),before.items.map(p=>p.eventoId));assert.equal(after.total,before.total);
        await requester.navigate('/Cliente/Pedido/'+orderId);
        const recoveredPdf=await requester.evaluate(`fetch('/Cliente/Pedido/${orderId}/Comprovante').then(async r=>({status:r.status,magic:new TextDecoder().decode((await r.arrayBuffer()).slice(0,4))}))`);
        assert.deepEqual(recoveredPdf,{status:200,magic:'%PDF'});
        await profiles.driver.navigate('/Entregas');
        assert.equal(await profiles.driver.evaluate(`!!document.querySelector('main a[href="/Entregas/Navegar/${deliveryId}"]')`),false);
        for(const role of ['admin','client'])await responsive(profiles[role],'restored-'+role);
        stage='localized operational error and visible correlation reference';
        await profiles.admin.navigate('/Home/Error');
        const errorReference=await profiles.admin.evaluate(`fetch('/Home/Error').then(async r=>({header:r.headers.get('X-Correlation-ID'),shown:new DOMParser().parseFromString(await r.text(),'text/html').querySelector('section[role="alert"] code')?.textContent}))`);
        assert.match(errorReference.header,/^[a-f0-9]{32}$/);assert.equal(errorReference.shown,errorReference.header);
        await responsive(profiles.admin,'operational-error');
        console.log('Recovery command measurements: '+JSON.stringify(recoveryTimes));
        console.log('Recovery Chromium passed: live WAL-aware backup + keys + gzip/UUID index, CLI verification, isolated restore after SIGKILL, original authenticated profiles, same archived event IDs, authorized PDF and closed driver trip; two restored screens plus localized error/reference x five widths x two themes; accessible names/language/keyboard verified. Backup: '+backup);
    }
    assert.deepEqual(errors,[],'Journey must have no unhandled browser JavaScript exception.');
    console.log('Complete journey passed: customer order, approval/dispatch, unchanged assignment retries preserving the issued code, driver shift, GPS via hub/database/map, signed POD, lost-receipt recovery, private/public convergence, authorized PDF, four profiles in five widths and two themes.');
    console.log('Owned database and server evidence: '+directory);
    console.log('Screenshots: '+JSON.stringify(Object.fromEntries(Object.entries(profiles).map(([role,client])=>[role,client.directory]))));
}
main().catch(async error=>{
    console.error('Failed stage: '+stage+'\n'+error.message);
    for(const[role,client]of Object.entries(profiles))try{await client.screenshot('journey-failed-'+role);console.error(role+': '+client.directory);}catch{}
    console.error('Owned evidence: '+directory);process.exitCode=1;
}).finally(()=>{for(const client of Object.values(profiles))client.closeBrowser();server?.kill('SIGTERM');roadProvider?.closeAllConnections();roadProvider?.close();photonProvider?.closeAllConnections();photonProvider?.close();});

// Complete browser journey on an exclusively owned temporary SQLite database.
// Four Chromium profiles keep administrator, customer, driver and public cookies separate.
const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {mkdtempSync,createWriteStream} = require('node:fs');
const {tmpdir} = require('node:os');
const {join,resolve} = require('node:path');
const net = require('node:net');
const {createBrowserClient} = require('./command-center-browser-check.cjs');
const directory = mkdtempSync(join(tmpdir(),'mgl-fleet-management-'));
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
async function startServer() {
    const port=await new Promise((resolve,reject)=>{
        const probe=net.createServer();probe.once('error',reject);
        probe.listen(0,'127.0.0.1',()=>{const port=probe.address().port;probe.close(()=>resolve(port));});
    });
    const log=createWriteStream(join(directory,'server.log'));
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
async function fill(client, fields, formId) {
    await client.evaluate(`(() => {
        const form=document.getElementById(${JSON.stringify(formId)});
        for(const [key,value] of Object.entries(${JSON.stringify(fields)})) form.elements[key].value=value;
        if(!form.reportValidity()) throw new Error('A real form rejected its data.');
        form.requestSubmit();
    })()`);
}
const vehicleFields={Modelo:'Veículo Chromium',Placa:'UIX-1A01',CapacidadeCargaKg:'5000.50',AlturaMetros:'4.20',LarguraMetros:'2.60',ComprimentoMetros:'18.60',PesoToneladas:'29.50',QtdEixos:'6',TipoCarga:'Carga Geral Seca'};
async function matrix(client, path, label) {
    await client.navigate(path);
    for(const theme of ['dark','light']) {
        if(await client.evaluate('document.documentElement.dataset.mglTheme')!==theme)
            await client.evaluate(`document.querySelector('[data-theme-toggle]').click()`);
        for(const width of [1440,1280,768,390,360]) {
            await client.viewport(width,width<500?844:1000);
            const result=await client.evaluate(`(() => ({
                overflow:document.documentElement.scrollWidth>innerWidth,
                missingLabels:[...document.querySelectorAll('main input:not([type=hidden])')].filter(e=>!e.labels?.length).map(e=>e.id),
                badTargets:[...document.querySelectorAll('main :is(input:not([type=hidden]),button,.btn)')].filter(e=>{const r=e.getBoundingClientRect();return r.width>0&&r.height<43.5;}).map(e=>e.id||e.textContent.trim()),
                body:document.querySelector('main').textContent
            }))()`);
            assert.equal(result.overflow,false,label+' overflow '+width);
            assert.deepEqual(result.missingLabels,[],label+' inputs must have labels');
            assert.deepEqual(result.badTargets,[],label+' touch targets');
            assert.ok(!result.body.includes('SenhaHash'));
            await client.screenshot(label+'-'+theme+'-'+width);
        }
    }
}
async function main() {
    const baseUrl=await startServer();
    profiles.admin=createBrowserClient({baseUrl});await profiles.admin.openBrowser();
    await login(profiles.admin,'admin','admin123','/Dashboard');
    const admin=profiles.admin;
    stage='create actual vehicle';
    await admin.navigate('/Frota/NovoVeiculo');
    await fill(admin,vehicleFields,'fleetVehicleForm');
    await until(admin,`location.pathname.startsWith('/Frota/Veiculo/') && document.readyState==='complete'`,'Vehicle created');
    const vehiclePath=await admin.evaluate('location.pathname');const vehicleId=vehiclePath.split('/').at(-1);
    assert.ok(await admin.evaluate(`document.querySelector('main').textContent.includes('UIX1A01')`));
    stage='edit vehicle and control activity';
    await admin.navigate('/Frota/EditarVeiculo/'+vehicleId);
    await fill(admin,{Modelo:'Veículo Chromium revisado'},'fleetVehicleForm');
    await until(admin,`location.pathname===${JSON.stringify(vehiclePath)} && document.readyState==='complete'`);
    for(const expected of ['Inativo','Ativo']) {
        await admin.evaluate(`document.getElementById('fleetStatusForm').requestSubmit()`);
        await until(admin,`document.querySelector('.cc-panel-header .cc-status')?.textContent===${JSON.stringify(expected)} && document.readyState==='complete'`,'Vehicle '+expected);
    }
    stage='create driver through actual account form';
    await admin.navigate('/Frota/NovoMotorista');
    await fill(admin,{Nome:'Motorista Chromium',Login:'chromium.driver',Telefone:'00000000000',SenhaInicial:'Synthetic-Chromium-only!'},'fleetDriverForm');
    await until(admin,`location.pathname.startsWith('/Frota/Motorista/') && document.readyState==='complete'`,'Driver created');
    const driverPath=await admin.evaluate('location.pathname');const driverId=driverPath.split('/').at(-1);
    await admin.navigate('/Frota/EditarMotorista/'+driverId);
    assert.equal(await admin.evaluate(`document.getElementById('Login').readOnly`),true);
    assert.equal(await admin.evaluate(`!!document.getElementById('SenhaInicial')`),false);
    await fill(admin,{Nome:'Motorista Chromium revisado'},'fleetDriverForm');
    await until(admin,`location.pathname===${JSON.stringify(driverPath)} && document.readyState==='complete'`);
    stage='new account signs in with the existing authentication';
    profiles.driver=createBrowserClient({baseUrl});await profiles.driver.openBrowser();
    const driver=profiles.driver;
    await driver.navigate('/Account/Login');
    await driver.evaluate(`document.getElementById('Login').value='chromium.driver';document.getElementById('Senha').value='Synthetic-Chromium-only!';document.querySelector('.cc-login-form').requestSubmit()`);
    await until(driver,`location.pathname==='/Entregas' && document.readyState==='complete'`);
    assert.equal(await driver.evaluate(`document.querySelectorAll('main a[href="/Frota"]').length`),0);
    stage='dispatch uses newly created real catalogue';
    await admin.navigate('/Entregas/Create?pedidoId=3');
    assert.ok(await admin.evaluate(`!!document.querySelector('#MotoristaId option[value="${driverId}"]') && !!document.querySelector('#selectVeiculoFrota option[value="${vehicleId}"]')`));
    await fill(admin,{MotoristaId:driverId,VeiculoId:vehicleId,DestinatarioNome:'Destinatário sintético',CepDestino:'00000-000',EnderecoDestino:'Endereço sintético local',NumeroDestino:'1',BairroDestino:'Centro',CidadeDestino:'Santos',EstadoDestino:'SP'},'dispatchOrderForm');
    await until(admin,`location.pathname.startsWith('/Entregas/Details/') && document.readyState==='complete'`,'Dispatch saved');
    const tripId=await admin.evaluate('location.pathname.split("/").at(-1)');
    stage='issue shift code, descriptive edits preserve its authorization';
    const code=await admin.evaluate(`(async()=>{
        const form=[...document.forms].find(f=>new URL(f.action).pathname==='/Entregas/EmitirCodigoTurno');
        const response=await fetch(form.action,{method:'POST',body:new FormData(form)});
        if(!response.ok) throw new Error('Shift code was not issued.');
        return new DOMParser().parseFromString(await response.text(),'text/html').querySelector('output[aria-label="Código temporário"]')?.textContent.trim();
    })()`);assert.ok(code);
    await admin.navigate('/Frota/EditarMotorista/'+driverId);
    await fill(admin,{Telefone:'00000000001'},'fleetDriverForm');
    await until(admin,`location.pathname===${JSON.stringify(driverPath)} && document.readyState==='complete'`);
    await admin.navigate('/Frota/EditarVeiculo/'+vehicleId);
    await fill(admin,{Modelo:'Veículo Chromium em operação'},'fleetVehicleForm');
    await until(admin,`location.pathname===${JSON.stringify(vehiclePath)} && document.readyState==='complete'`);
    await driver.navigate('/Entregas');
    await driver.evaluate(`(()=>{
        const form=[...document.forms].find(f=>f.elements.entregaId?.value==='${tripId}' && new URL(f.action).pathname==='/Entregas/IniciarTurno');
        if(!form) throw new Error('New driver did not receive the assigned trip.');
        form.elements.codigo.value=${JSON.stringify(code)};form.requestSubmit();
    })()`);
    await until(driver,`location.pathname==='/Entregas/Navegar/${tripId}' && document.readyState==='complete'`,'New driver starts original shift code');
    for(const path of [vehiclePath,driverPath]) {
        await admin.navigate(path);
        assert.ok(await admin.evaluate(`document.querySelector('#fleetStatusForm button').disabled`));
        assert.ok(await admin.evaluate(`!!document.querySelector('main a[href="/Entregas/Details/${tripId}"]')`));
    }
    stage='server pagination through the actual admin forms';
    await admin.navigate('/Frota/NovoVeiculo');
    const created=await admin.evaluate(`(async()=>{
        const template=document.getElementById('fleetVehicleForm');
        for(let n=0;n<21;n++) {
            const fields=${JSON.stringify(vehicleFields)};fields.Placa='UIX0A'+String(n).padStart(2,'0');fields.Modelo='PAGINA-UI-'+String(n).padStart(2,'0');
            const data=new FormData(template);for(const [key,value] of Object.entries(fields))data.set(key,value);
            const response=await fetch(template.action,{method:'POST',body:data});
            if(!response.ok || !new URL(response.url).pathname.startsWith('/Frota/Veiculo/')) throw new Error('Pagination fixture creation failed.');
        }
        return 21;
    })()`);assert.equal(created,21);
    await admin.navigate('/Frota?termo=PAGINA-UI');
    assert.ok(await admin.evaluate(`document.querySelector('main').textContent.includes('PAGINA-UI-19') && !document.querySelector('main').textContent.includes('PAGINA-UI-20')`));
    await admin.evaluate(`document.querySelector('nav[aria-label="Páginas dos veículos"] a').click()`);
    await until(admin,`location.search.includes('paginaVeiculos=2') && document.readyState==='complete'`);
    assert.ok(await admin.evaluate(`document.querySelector('main').textContent.includes('PAGINA-UI-20') && !document.querySelector('main').textContent.includes('PAGINA-UI-19')`));
    await admin.evaluate(`document.activeElement?.blur();window.dispatchEvent(new Event('online'))`);
    await until(admin,`document.getElementById('operationalRefreshStatus').textContent.includes('Dados atualizados')`);
    assert.ok(await admin.evaluate(`document.querySelector('main').textContent.includes('PAGINA-UI-20') && !document.querySelector('main').textContent.includes('PAGINA-UI-19')`));
    stage='desktop tablet mobile, both established themes';
    for(const [path,label] of [['/Frota','fleet-roster'],['/Frota/EditarVeiculo/'+vehicleId,'fleet-vehicle-form'],['/Frota/EditarMotorista/'+driverId,'fleet-driver-form'],[vehiclePath,'fleet-vehicle-details'],[driverPath,'fleet-driver-details'],['/Frota/NovoMotorista','fleet-driver-create']])
        await matrix(admin,path,label);
    console.log('Fleet management Chromium passed: real creation/edit/status/authentication/dispatch/shift, preserved code, busy-resource guard, pagination and refresh; six screens × five widths × two themes.');
    console.log('Evidence: '+directory+'; screenshots: '+admin.directory);
}
main().catch(async error=>{
    console.error('Stage: '+stage+'; '+error.stack);process.exitCode=1;
    try{await profiles.admin?.screenshot('fleet-failed');console.error('Screenshots: '+profiles.admin?.directory);}catch{}
}).finally(async()=>{
    for(const client of Object.values(profiles))client.closeBrowser();
    if(server && server.exitCode===null){server.kill('SIGTERM');await Promise.race([new Promise(resolve=>server.once('exit',resolve)),pause(5000)]);}
});

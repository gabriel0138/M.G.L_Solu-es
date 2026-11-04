// Runs only against a local synthetic demonstration. No production data.
const assert = require('node:assert/strict');
const browser = require('./command-center-browser-check.cjs');
const { openBrowser, closeBrowser, command, evaluate, navigate, viewport, screenshot, pause, onEvent } = browser;
let intercepted = 0;
async function until(expression) {
    for (let i = 0; i < 150; i++) { if (await evaluate(expression)) return; await pause(100); }
    throw new Error('Browser assertion timed out: ' + expression);
}
async function main() {
    await openBrowser();
    // This fixture varies transport responses through CDP. Keep registration
    // disabled in its isolated browser profile so requests stay on this target.
    await command('Page.addScriptToEvaluateOnNewDocument', { source: `if (navigator.serviceWorker) navigator.serviceWorker.register = async () => { throw new Error('CDP transport fixture'); };` });
    await viewport(1440, 1000);
    await navigate('/Account/Login');
    assert.ok(await evaluate(`!!document.querySelector('.cc-demo-access')`), 'Use a Development demonstration with synthetic data.');
    await evaluate(`document.getElementById('Login').value='admin'; document.getElementById('Senha').value='admin123'; document.querySelector('.cc-login-form').requestSubmit();`);
    await until(`location.pathname === '/Dashboard' && document.readyState === 'complete'`);
    let snapshot = await evaluate(`fetch('/Operacoes/Posicoes').then(r=>r.json())`);
    let mode = 'waiting';
    onEvent('Fetch.requestPaused', async event => {
        intercepted++;
        let status = 200;
        const body = structuredClone(snapshot);
        if (mode === 'waiting') for (const item of body.items) {
            item.latitude = null; item.longitude = null; item.recebidoEmUtc = null;
        }
        if (mode === 'stale') body.items[0].recebidoEmUtc = new Date(Date.now()-1800000).toISOString();
        if (mode === 'error') status = 500;
        if (mode === 'denied') status = 403;
        await command('Fetch.fulfillRequest', { requestId: event.requestId, responseCode: status,
            responseHeaders: [{name:'Content-Type',value:'application/json'},{name:'Cache-Control',value:'no-store'}],
            body: Buffer.from(JSON.stringify(status === 200 ? body : {erro:'Isolated browser fixture'})).toString('base64') });
    });
    await command('Network.setBypassServiceWorker', { bypass: true });
    await command('Fetch.enable', { patterns: [{ urlPattern: '*/Operacoes/Posicoes*', requestStage:'Request' }] });
    await navigate('/Operacoes');
    await until(`document.querySelectorAll('.cc-fleet-trip').length > 0`);
    assert.equal(await evaluate(`document.querySelectorAll('#fleetMap .leaflet-interactive').length`), 0);
    assert.ok(await evaluate(`document.getElementById('fleetTripList').textContent.includes('Aguardando GPS')`));
    await screenshot('map-waiting-gps-desktop');

    await command('Fetch.disable');
    // Exercise the production hub and database with a clearly synthetic demo fix.
    await evaluate(`(async () => {
        const response = await fetch('/Operacoes/Posicoes');
        const payload = await response.json();
        const code = payload.items[0].codigoRastreio;
        const negotiation = await (await fetch('/telemetryHub/negotiate?negotiateVersion=1', { method: 'POST' })).json();
        await new Promise((resolve,reject) => {
            const socket = new WebSocket('ws://' + location.host + '/telemetryHub?id=' + encodeURIComponent(negotiation.connectionToken));
            let handshake = false; let pending = '';
            const timer = setTimeout(() => { socket.close(); reject(new Error('Synthetic telemetry timeout')); }, 10000);
            socket.onopen = () => socket.send(JSON.stringify({protocol:'json',version:1})+'\\u001e');
            socket.onerror = () => { clearTimeout(timer); reject(new Error('Synthetic telemetry connection failed')); };
            socket.onmessage = event => {
                pending += event.data;
                const frames = pending.split('\\u001e'); pending = frames.pop();
                for (const frame of frames.filter(Boolean)) {
                    const item = JSON.parse(frame);
                    if (!handshake) {
                        handshake = true;
                        socket.send(JSON.stringify({type:1,invocationId:'fixture-gps',target:'SendVehicleTelemetry',arguments:[code,-23.5505,-46.6333,30,90,10,false]})+'\\u001e');
                    } else if (item.invocationId === 'fixture-gps') {
                        clearTimeout(timer); socket.close();
                        if (item.error) reject(new Error(item.error)); else resolve();
                    }
                }
            };
        });
        window.dispatchEvent(new Event('online'));
    })()`);
    await until(`document.querySelectorAll('#fleetMap .leaflet-interactive').length === 1`);
    snapshot = await evaluate(`fetch('/Operacoes/Posicoes').then(r=>r.json())`);
    assert.equal(snapshot.items[0].latitude, -23.5505);
    assert.equal(snapshot.items[0].longitude, -46.6333);
    assert.ok(snapshot.items[0].recebidoEmUtc.endsWith('Z'));
    for (const width of [1440,1280,768,390,360]) {
        await viewport(width, width < 500 ? 844 : 1000);
        await pause(220);
        assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Map page overflow at ' + width);
        await screenshot('map-gps-' + width);
    }
    await evaluate(`document.querySelector('.cc-fleet-trip').click(); document.getElementById('fleetFollow').checked=true;`);
    assert.ok(await evaluate(`document.querySelector('#fleetSelectionDetails a').href.includes('/Entregas/Navegar/')`));

    // Deterministically validate old-signal, outage and revocation presentation.
    mode = 'stale';
    await command('Fetch.enable', { patterns: [{ urlPattern: '*/Operacoes/Posicoes*', requestStage:'Request' }] });
    await evaluate(`document.activeElement?.blur(); window.dispatchEvent(new Event('online'));`);
    await until(`document.getElementById('fleetTripList').textContent.includes('desatualizado')`);
    await screenshot('map-stale-mobile');
    mode = 'error';
    await evaluate(`window.dispatchEvent(new Event('online'))`);
    await until(`document.getElementById('fleetMapStatus').textContent.includes('Não foi possível atualizar')`);
    assert.equal(await evaluate(`document.querySelectorAll('#fleetMap .leaflet-interactive').length`), 1);
    mode = 'denied';
    await evaluate(`window.dispatchEvent(new Event('online'))`);
    await until(`document.getElementById('fleetMapStatus').textContent.includes('acesso mudou')`);
    assert.equal(await evaluate(`document.querySelectorAll('#fleetMap .leaflet-interactive').length`), 0);
    assert.equal(await evaluate(`document.getElementById('fleetTripList').children.length`), 0);
    console.log('Map browser checks passed: persisted synthetic fix, no fake fallback, five widths, stale signal, outage and revocation.');
    console.log('Screenshots: ' + browser.directory);
}
main().catch(async error => {
    console.error(error.message, 'intercepted:', intercepted);
    try {
        console.error(await evaluate(`JSON.stringify({status:document.getElementById('fleetMapStatus')?.textContent,active:document.activeElement?.tagName,list:document.getElementById('fleetTripList')?.textContent})`));
        await screenshot('map-failed-state');
        console.error('Failure screenshot: ' + browser.directory);
    } catch { /* The browser may already be unavailable. */ }
    process.exitCode = 1;
}).finally(closeBrowser);

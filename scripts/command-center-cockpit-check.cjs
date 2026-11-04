// Real Chromium smoke checks against the local, synthetic Development demo only.
// Starts a demo shift through CSRF-protected forms and sends one CDP-emulated fix.
// POD/emergency dialogs are previewed and dismissed without submission.
const assert = require('node:assert/strict');
const base = new URL(process.env.MGL_UI_BASE_URL || 'http://localhost:5290');
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(base.hostname), 'Use a local demonstration host.');
assert.ok(['http:', 'https:'].includes(base.protocol), 'Use a local HTTP(S) demonstration.');
assert.equal(base.username + base.password, '', 'Do not supply credentials in the URL.');
process.env.MGL_UI_BASE_URL = base.origin;
const browser = require('./command-center-browser-check.cjs');
const { openBrowser, closeBrowser, command, evaluate, navigate, viewport, screenshot, pause, onEvent } = browser;
const widths = [1440, 1280, 768, 390, 360];
const javascriptErrors = [];
const prohibitedSubmissions = [];
const transportDiagnostics = [];
const savedGpsPattern = /const savedGps = (\{[^\n]+\});/;
let stage = 'setup';

async function until(expression, message = expression) {
    for (let attempt = 0; attempt < 150; attempt++) {
        try { if (await evaluate(expression)) return; }
        catch (error) {
            if (!/context.*destroyed|cannot find context/i.test(error.message)) throw error;
        }
        await pause(100);
    }
    throw new Error('Browser assertion timed out: ' + message);
}

async function noOverflow(context) {
    const bounds = await evaluate(`({ width: innerWidth, actual: Math.max(document.documentElement.scrollWidth, document.body?.scrollWidth || 0) })`);
    assert.ok(bounds.actual <= bounds.width, context + ': horizontal overflow ' + JSON.stringify(bounds));
}

async function login(name, password, landing) {
    await navigate('/Account/Login');
    const demo = await evaluate(`document.querySelector('.cc-demo-access')?.textContent || ''`);
    assert.ok(demo.includes('Acessos de demonstração local') && demo.includes(name) && demo.includes(password),
        'Credentials must be displayed by this local Development demonstration.');
    await evaluate(`document.getElementById('Login').value=${JSON.stringify(name)};
        document.getElementById('Senha').value=${JSON.stringify(password)};
        document.querySelector('.cc-login-form').requestSubmit();`);
    await until(`location.pathname === ${JSON.stringify(landing)} && document.readyState === 'complete'`, name + ' login');
}

async function logout() {
    const response = await evaluate(`(async () => {
        const form = [...document.forms].find(item => new URL(item.action).pathname === '/Account/Logout');
        if (!form?.querySelector('input[name="__RequestVerificationToken"]')) throw new Error('Missing logout CSRF form.');
        const result = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });
        return { ok: result.ok, path: new URL(result.url).pathname };
    })()`);
    assert.ok(response.ok && ['/', '/Home', '/Home/Index', '/Account/Login'].includes(response.path),
        'Demo logout must return to an anonymous entry point through its protected POST: ' + JSON.stringify(response));
}

async function prepareDriverShift() {
    await login('admin', 'admin123', '/Dashboard');
    await navigate('/Entregas/Details/1');
    const result = await evaluate(`(async () => {
        const form = [...document.forms].find(item => new URL(item.action).pathname === '/Entregas/EmitirCodigoTurno');
        if (!form?.querySelector('input[name="__RequestVerificationToken"]')) throw new Error('Missing shift-code CSRF form.');
        if (form.elements.entregaId?.value !== '1') throw new Error('Unexpected demo delivery.');
        const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });
        if (!response.ok) throw new Error('Shift-code request failed: ' + response.status);
        const page = new DOMParser().parseFromString(await response.text(), 'text/html');
        const code = page.querySelector('output[aria-label="Código temporário"]')?.textContent.trim();
        return { code: code || null, alreadyActive: page.body.textContent.includes('A entrega já possui turno ativo.') };
    })()`);
    assert.ok(result.code || result.alreadyActive, 'Issue a demo shift code or confirm the existing shift.');
    await logout();
    await login('carlos.motorista', 'motorista123', '/Entregas');
    const active = await evaluate(`!!document.querySelector('main a[href="/Entregas/Navegar/1"]')`);
    if (!active) {
        assert.ok(result.code, 'A new demo shift requires the server-issued code.');
        await evaluate(`(() => {
            const form = [...document.forms].find(item => new URL(item.action).pathname === '/Entregas/IniciarTurno'
                && item.elements.entregaId?.value === '1');
            if (!form?.querySelector('input[name="__RequestVerificationToken"]')) throw new Error('Missing driver shift CSRF form.');
            form.elements.codigo.value=${JSON.stringify(result.code)};
            form.requestSubmit();
        })()`);
        await until(`location.pathname === '/Entregas/Navegar/1' && document.readyState === 'complete'`, 'Driver starts its assigned shift');
    }
    await navigate('/Entregas/Navegar/1');
}

async function savedGps() {
    return evaluate(`(() => {
        const source = [...document.scripts].map(script => script.textContent).join('');
        const match = source.match(${savedGpsPattern.toString()});
        return match ? JSON.parse(match[1]) : null;
    })()`);
}

async function readyCockpit() {
    await until(`!!document.querySelector('#cockpitMap.leaflet-container')`, 'Leaflet cockpit renders');
    await until(`document.getElementById('telemetryConnectionState')?.dataset.state === 'connected'`, 'Authorized telemetry hub connection');
    assert.equal(await evaluate(`!!document.getElementById('btnSimular')`), false, 'Driver cannot simulate telemetry.');
    assert.equal(await evaluate(`!!document.querySelector('#btn3d, #btn3D, [data-map-mode="3d"], [data-view-mode="3d"]')`), false,
        'The Leaflet cockpit must not advertise an unsupported 3D mode.');
}

async function coherentGpsNotice() {
    const notice = await evaluate(`(() => {
        const map = document.getElementById('cockpitMap');
        const hud = document.getElementById('cockpitMapStatus');
        return { hasMapStatus: map.hasAttribute('data-map-status'),
            pseudoDisplay: getComputedStyle(map, '::after').display,
            status: map.dataset.mapStatus || '', hudText: hud?.textContent || '',
            hudHidden: hud?.hidden, hudRole: hud?.getAttribute('role'),
            inBottomHud: !!hud?.closest('.cc-cockpit-bottom') && !map.contains(hud),
            instruction: document.getElementById('maneuverInstruction')?.textContent || '' };
    })()`);
    if (notice.hasMapStatus) assert.equal(notice.pseudoDisplay, 'none',
        'Map status must not cover the center of the map or the GPS control.');
    assert.ok(notice.inBottomHud && notice.hudRole === 'status', 'Map notices belong in the accessible bottom HUD.');
    assert.equal(notice.hudText, notice.status, 'The HUD must mirror the actual map state.');
    assert.equal(notice.hudHidden, !notice.status, 'Empty map notices stay hidden.');
    assert.doesNotMatch(notice.status + ' ' + notice.hudText + ' ' + notice.instruction,
        /ative o gps|aguardando uma posição gps/i,
        'A valid persisted GPS fix must not be presented as waiting for device activation.');
}

async function previewModal(id, name) {
    await evaluate(`document.querySelector('[data-bs-target="#${id}"]').click()`);
    await until(`document.getElementById('${id}').classList.contains('show') && document.getElementById('${id}').getAttribute('aria-modal') === 'true'`);
    await until(`document.getElementById('${id}').contains(document.activeElement)`, name + ': modal receives keyboard focus after its transition');
    const result = await evaluate(`(() => {
        const modal = document.getElementById('${id}');
        const rect = modal.querySelector('.modal-dialog').getBoundingClientRect();
        return { left: rect.left, right: rect.right, width: innerWidth,
            named: !!document.getElementById(modal.getAttribute('aria-labelledby'))?.textContent.trim(),
            focused: modal.contains(document.activeElement),
            fields: [...modal.querySelectorAll('input:not([type="hidden"]), select, textarea')]
                .every(input => input.labels.length > 0 && input.name) };
    })()`);
    assert.ok(result.left >= 0 && result.right <= result.width && result.named && result.focused && result.fields,
        name + ': accessible responsive dialog ' + JSON.stringify(result));
    await noOverflow(name);
    await screenshot(name);
    await evaluate(`document.getElementById('${id}').querySelector('[data-bs-dismiss="modal"]').click()`);
    await until(`!document.getElementById('${id}').classList.contains('show')`);
    await until(`!document.querySelector('.modal-backdrop')`, name + ': modal backdrop is removed');
}

async function emulatedGpsPersistence(initial) {
    stage = 'synthetic GPS publication';
    // These coordinates originate in the synthetic demo, never from the user/device.
    const fix = {
        latitude: Number((initial.latitude + 0.00001).toFixed(6)),
        longitude: Number((initial.longitude + 0.00001).toFixed(6)),
        accuracy: 8
    };
    await command('Browser.grantPermissions', { origin: base.origin, permissions: ['geolocation'] });
    await command('Emulation.setGeolocationOverride', fix);
    const startedAt = Date.now();
    await evaluate(`document.getElementById('btnToggleGps').click()`);
    assert.equal(await evaluate(`document.getElementById('btnToggleGps').getAttribute('aria-pressed')`), 'true');
    let persisted;
    for (let attempt = 0; attempt < 30; attempt++) {
        persisted = await evaluate(`(async () => {
            const response = await fetch('/Entregas/Navegar/1', { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok || response.redirected) throw new Error('Could not read the authorized persisted GPS snapshot.');
            const match = (await response.text()).match(${savedGpsPattern.toString()});
            return match ? JSON.parse(match[1]) : null;
        })()`);
        if (persisted?.latitude === fix.latitude && persisted?.longitude === fix.longitude
            && Date.parse(persisted.timestamp) >= startedAt - 1000) break;
        await pause(500);
    }
    assert.equal(persisted?.latitude, fix.latitude, 'The actual GPS button must publish to the hub and persist latitude.');
    assert.equal(persisted?.longitude, fix.longitude, 'The actual GPS button must persist longitude.');
    assert.ok(Date.parse(persisted.timestamp) >= startedAt - 1000, 'Persisted GPS uses a fresh server timestamp.');
    await until(`document.getElementById('positionSourceBadge')?.dataset.state === 'received'`, 'Server acknowledges the synthetic device fix');
    stage = 'synthetic GPS stop';
    await evaluate(`document.getElementById('btnToggleGps').click()`);
    assert.equal(await evaluate(`document.getElementById('btnToggleGps').getAttribute('aria-pressed')`), 'false');
    await command('Emulation.clearGeolocationOverride');
    stage = 'GPS persisted reload';
    await navigate('/Entregas/Navegar/1');
    await readyCockpit();
    await coherentGpsNotice();
    const reloaded = await savedGps();
    assert.equal(reloaded.latitude, fix.latitude);
    assert.equal(reloaded.longitude, fix.longitude);
    assert.equal(reloaded.timestamp, persisted.timestamp, 'Reload must reuse the persisted timestamp instead of inventing a new fix.');
    assert.equal(await evaluate(`document.querySelectorAll('#cockpitVehicleMarker').length`), 1, 'One marker renders from the reloaded GPS.');
    assert.equal(await evaluate(`document.getElementById('positionSourceBadge').dataset.state`), 'received');
    await screenshot('cockpit-persisted-gps-after-reload');
}

async function staleSnapshotPresentation() {
    stage = 'aged snapshot through isolated browser clock';
    const actualSnapshot = await savedGps();
    // Age the actual persisted fix in this browser only. Document interception
    // triggers Chromium's local-network WebSocket checks; normal transport must
    // remain intact while verifying the production freshness calculation.
    const clockFixture = await command('Page.addScriptToEvaluateOnNewDocument', {
        source: `(() => { const actualNow = Date.now.bind(Date); Date.now = () => actualNow() + 600000; })();`
    });
    await navigate('/Entregas/Navegar/1');
    await readyCockpit();
    await coherentGpsNotice();
    await until(`document.getElementById('positionSourceBadge')?.dataset.state === 'stale'`, 'Old persisted GPS remains visibly stale');
    assert.deepEqual(await savedGps(), actualSnapshot, 'Aging presentation must retain the actual persisted position and timestamp.');
    assert.equal(await evaluate(`document.querySelectorAll('#cockpitVehicleMarker').length`), 1, 'A stale fix remains identified without faking new movement.');
    await screenshot('cockpit-stale-snapshot-mobile');
    await command('Page.removeScriptToEvaluateOnNewDocument', { identifier: clockFixture.identifier });
    stage = 'restore actual persisted snapshot';
    await navigate('/Entregas/Navegar/1');
    await readyCockpit();
    assert.deepEqual(await savedGps(), actualSnapshot);
    assert.equal(await evaluate(`document.getElementById('positionSourceBadge').dataset.state`), 'received',
        'Browser clock fixture must not modify the persisted server snapshot.');
}

async function main() {
    await openBrowser();
    onEvent('Runtime.exceptionThrown', event => javascriptErrors.push(event.exceptionDetails.exception?.description || event.exceptionDetails.text));
    onEvent('Runtime.consoleAPICalled', event => {
        if (event.type === 'error') javascriptErrors.push(stage + ': ' + event.args.map(arg => arg.value || arg.description || '').join(' '));
    });
    onEvent('Network.webSocketHandshakeResponseReceived', event => transportDiagnostics.push({ stage, handshake: event.response.status }));
    onEvent('Network.webSocketFrameError', event => transportDiagnostics.push({ stage, error: event.errorMessage }));
    onEvent('Network.requestWillBeSent', event => {
        if (event.request.method === 'POST' && /\/(FinalizarEntregaPOD|CriarAlertaEmergencia)(?:\?|$)/.test(event.request.url))
            prohibitedSubmissions.push(new URL(event.request.url).pathname);
    });
    await command('Runtime.enable');
    await command('Network.enable');
    await command('Network.setBypassServiceWorker', { bypass: true });
    // Isolated test profile only: keep the CDP transport fixture on this page target.
    await command('Page.addScriptToEvaluateOnNewDocument', {
        source: `if (navigator.serviceWorker) navigator.serviceWorker.register = async () => { throw new Error('CDP cockpit fixture'); };`
    });
    await viewport(1440, 1000);
    await prepareDriverShift();
    await readyCockpit();
    const initial = await savedGps();
    assert.ok(Number.isFinite(initial?.latitude) && Number.isFinite(initial?.longitude) && Number.isFinite(Date.parse(initial.timestamp)),
        'This local demonstration must have the explicitly synthetic GPS snapshot prepared by the map check.');
    assert.ok(['received', 'stale'].includes(await evaluate(`document.getElementById('positionSourceBadge').dataset.state`)));
    assert.equal(await evaluate(`document.querySelectorAll('#cockpitVehicleMarker').length`), 1);
    await coherentGpsNotice();
    await evaluate('document.fonts.ready');
    stage = 'responsive cockpit and modal matrix';
    for (const width of widths) {
        await viewport(width, width < 500 ? 844 : 1000);
        await pause(220);
        await noOverflow('cockpit-' + width);
        await coherentGpsNotice();
        await screenshot('cockpit-' + width);
        await previewModal('modalPODNavegacao', 'cockpit-pod-' + width);
        await previewModal('driverEmergencyModal', 'cockpit-emergency-' + width);
    }
    await emulatedGpsPersistence(initial);
    await staleSnapshotPresentation();
    assert.deepEqual(prohibitedSubmissions, [], 'Modal previews must never submit a POD or emergency.');
    assert.deepEqual(javascriptErrors, [], 'The cockpit must produce no JavaScript exceptions or console errors.');
    console.log('Cockpit checks passed: real CSRF shift flow, authorized hub, five widths, ten dialog previews, synthetic device GPS persistence/reload, browser clock aging and zero JavaScript errors.');
    console.log('Screenshots: ' + browser.directory);
}

if (require.main === module) main().catch(async error => {
    console.error(error.stack || error.message);
    if (javascriptErrors.length) console.error('JavaScript errors:', javascriptErrors);
    if (transportDiagnostics.length) console.error('Transport diagnostics:', transportDiagnostics);
    try {
        console.error(await evaluate(`JSON.stringify({ path: location.pathname, gps: document.getElementById('positionSourceBadge')?.textContent,
            connection: document.getElementById('telemetryConnectionState')?.textContent,
            map: document.getElementById('cockpitMap')?.dataset.mapStatus })`));
        await screenshot('cockpit-failure');
        console.error('Failure screenshot: ' + browser.directory);
    } catch { /* Browser may already have closed. */ }
    process.exitCode = 1;
}).finally(closeBrowser);
module.exports = { main };

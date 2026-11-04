// Real Chromium checks for the local, synthetic Development demonstration.
// Reuses the CDP harness; no npm packages and no valid operational submissions.
const assert = require('node:assert/strict');

const base = new URL(process.env.MGL_BROWSER_BASE_URL || 'http://localhost:5290');
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(base.hostname), 'Use a local demonstration host.');
assert.ok(['http:', 'https:'].includes(base.protocol), 'Use a local HTTP(S) demonstration.');
assert.equal(base.username + base.password, '', 'Do not put credentials in the demonstration URL.');
// The shared harness reads its own environment variable when first required.
process.env.MGL_UI_BASE_URL = base.origin;
const browser = require('./command-center-browser-check.cjs');
const { openBrowser, closeBrowser, command, evaluate, navigate, viewport, screenshot, key, pause } = browser;
const widths = [1440, 1280, 768, 390, 360];
const themes = ['dark', 'light'];
const checks = [];
const skips = [];

async function until(expression) {
    for (let attempt = 0; attempt < 150; attempt++) {
        try { if (await evaluate(expression)) return; }
        catch (error) {
            if (!/context.*destroyed|cannot find context/i.test(error.message)) throw error;
        }
        await pause(100);
    }
    throw new Error('Browser assertion timed out: ' + expression);
}

async function noOverflow(context) {
    const result = await evaluate(`(() => {
        const width = innerWidth;
        const actual = Math.max(document.documentElement.scrollWidth, document.body?.scrollWidth || 0);
        const offenders = actual > width ? [...document.querySelectorAll('body *')]
            .filter(element => {
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return rect.width && rect.right > width + 1 && style.position !== 'fixed'
                    && !element.closest('.table-responsive, .leaflet-pane, [inert]');
            }).slice(0, 5).map(element => ({ tag: element.tagName, className: element.className })) : [];
        return { width, actual, offenders };
    })()`);
    assert.ok(result.actual <= result.width, context + ': global horizontal overflow ' + JSON.stringify(result));
}

async function setTheme(theme) {
    const hasControl = await evaluate(`!!document.querySelector('[data-theme-toggle]')`);
    assert.ok(hasControl, 'The theme control must be available outside the navigation cockpit.');
    if (await evaluate('document.documentElement.dataset.mglTheme') !== theme) {
        await evaluate(`document.querySelector('[data-theme-toggle]').click()`);
    }
    assert.equal(await evaluate('document.documentElement.dataset.mglTheme'), theme);
    await pause(180);
}

async function login(login, password, landing) {
    await command('Network.clearBrowserCookies');
    await viewport(1440, 1000);
    await navigate('/Account/Login');
    const demonstration = await evaluate(`document.querySelector('.cc-demo-access')?.textContent || ''`);
    assert.ok(demonstration.includes('Acessos de demonstração local'), 'Use only the Development demonstration.');
    assert.ok(demonstration.includes(login) && demonstration.includes(password), 'The supplied account must be listed in the local demonstration.');
    await evaluate(`document.getElementById('Login').value=${JSON.stringify(login)};
        document.getElementById('Senha').value=${JSON.stringify(password)};
        document.querySelector('.cc-login-form').requestSubmit();`);
    await until(`location.pathname === ${JSON.stringify(landing)} && document.readyState === 'complete'`);
    assert.equal(await evaluate('location.origin'), base.origin);
}

async function accessibleFields(ids) {
    const controls = await evaluate(`(() => ${JSON.stringify(ids)}.map(id => {
        const element = document.getElementById(id);
        return { id, exists: !!element, labels: element?.labels?.length || 0,
            named: !!element?.name, enabled: !!element && !element.disabled };
    }))()`);
    for (const control of controls) {
        assert.ok(control.exists && control.labels > 0 && control.named && control.enabled,
            'A usable form control needs a label and name: ' + JSON.stringify(control));
    }
}

async function accessibleTables() {
    const tables = await evaluate(`(() => [...document.querySelectorAll('main table')].map(table => ({
        caption: !!table.caption?.textContent.trim(),
        headers: [...table.querySelectorAll('thead th')].every(cell => cell.scope === 'col'),
        region: table.closest('[role="region"]')?.getAttribute('tabindex') === '0'
    })))()`);
    for (const table of tables) assert.ok(table.caption && table.headers && table.region,
        'Tables need captions, scoped headings and keyboard-accessible scrolling.');
}

async function mobileMenu(context, capture) {
    const hasSidebar = await evaluate(`!!document.getElementById('commandSidebar')`);
    if (!hasSidebar) return;
    await evaluate(`document.querySelector('[data-sidebar-toggle]').click()`);
    assert.equal(await evaluate('document.body.dataset.sidebarOpen'), 'true', context + ': menu opens');
    assert.equal(await evaluate(`document.querySelector('.cc-workspace').inert`), true);
    assert.equal(await evaluate(`document.getElementById('commandSidebar').inert`), false);
    assert.equal(await evaluate(`document.querySelector('[data-sidebar-toggle]').getAttribute('aria-expanded')`), 'true');
    await noOverflow(context + ' menu open');
    if (capture) await screenshot(context + '-menu');
    await key('Escape');
    assert.equal(await evaluate('document.body.dataset.sidebarOpen'), 'false', context + ': Escape closes menu');
    assert.equal(await evaluate(`document.querySelector('.cc-workspace').inert`), false);
    assert.equal(await evaluate(`document.getElementById('commandSidebar').inert`), true);
    assert.equal(await evaluate(`document.activeElement === document.querySelector('[data-sidebar-toggle]')`), true);
}

async function pageMatrix(name, path, expected, fields = []) {
    await navigate(path);
    await until(`!!document.querySelector(${JSON.stringify(expected)})`);
    await evaluate('document.fonts.ready');
    await accessibleFields(fields);
    await accessibleTables();
    for (const theme of themes) {
        await setTheme(theme);
        for (const width of widths) {
            await viewport(width, width < 500 ? 844 : 1000);
            await pause(220);
            const context = name + '-' + theme + '-' + width;
            await noOverflow(context);
            await screenshot(context);
            if (width < 992) await mobileMenu(context, width === 360);
        }
    }
    checks.push(name + ': five widths, both themes and responsive navigation');
}

async function filterAndReset(path, searchId, statusId, selectedStatus, resetStatus = '') {
    await navigate(path);
    await accessibleFields([searchId, statusId]);
    const term = 'MGL_UI_NO_MATCH_7B49';
    await evaluate(`(() => {
        const search = document.getElementById(${JSON.stringify(searchId)});
        const status = document.getElementById(${JSON.stringify(statusId)});
        search.value = ${JSON.stringify(term)};
        status.value = ${JSON.stringify(selectedStatus)};
        search.form.requestSubmit();
    })()`);
    await until(`document.readyState === 'complete' && location.pathname === ${JSON.stringify(path)}
        && new URLSearchParams(location.search).get('termo') === ${JSON.stringify(term)}
        && new URLSearchParams(location.search).get('status') === ${JSON.stringify(selectedStatus)}`);
    assert.equal(await evaluate(`document.getElementById(${JSON.stringify(searchId)}).value`), term);
    assert.equal(await evaluate(`document.getElementById(${JSON.stringify(statusId)}).value`), selectedStatus);
    assert.ok(await evaluate(`!!document.querySelector('main .cc-empty-state')`), path + ': empty filtered result');
    await noOverflow(path + ' filtered');
    const reset = await evaluate(`(() => {
        const link = [...document.querySelectorAll('.cc-filter-bar a[href]')]
            .find(element => element.textContent.includes('Limpar filtros'));
        return link ? new URL(link.href).pathname + new URL(link.href).search : null;
    })()`);
    assert.equal(reset, path, path + ': reset removes the complete query');
    await navigate(reset);
    assert.equal(await evaluate(`document.getElementById(${JSON.stringify(searchId)}).value`), '');
    assert.equal(await evaluate(`document.getElementById(${JSON.stringify(statusId)}).value`), resetStatus);
    checks.push(path + ': combined GET filters, empty state and reset');
}

async function modalPreview(id, fieldIds, name) {
    const trigger = await evaluate(`!!document.querySelector('[data-bs-target="#${id}"]:not([disabled])')`);
    if (!trigger) {
        skips.push(name + ': action unavailable in this trip state');
        return;
    }
    await evaluate(`document.querySelector('[data-bs-target="#${id}"]').click()`);
    await until(`document.getElementById(${JSON.stringify(id)}).classList.contains('show')
        && document.getElementById(${JSON.stringify(id)}).getAttribute('aria-modal') === 'true'`);
    await until(`document.getElementById(${JSON.stringify(id)}).contains(document.activeElement)`);
    const modal = await evaluate(`(() => {
        const element = document.getElementById(${JSON.stringify(id)});
        const dialog = element.querySelector('.modal-dialog').getBoundingClientRect();
        const title = document.getElementById(element.getAttribute('aria-labelledby'));
        return { visible: dialog.width > 0, left: dialog.left, right: dialog.right,
            width: innerWidth, titled: !!title?.textContent.trim(), focused: element.contains(document.activeElement) };
    })()`);
    assert.ok(modal.visible && modal.left >= 0 && modal.right <= modal.width && modal.titled && modal.focused,
        name + ': responsive, labelled and focused dialog ' + JSON.stringify(modal));
    await accessibleFields(fieldIds);
    await noOverflow(name);
    await screenshot(name);
    // Preview and dismiss only: do not sign, save, sync or register an emergency.
    await evaluate(`document.getElementById(${JSON.stringify(id)}).querySelector('[data-bs-dismiss="modal"]').click()`);
    await until(`!document.getElementById(${JSON.stringify(id)}).classList.contains('show')`);
    await until(`!document.querySelector('.modal-backdrop')`);
}

async function optionalCockpit() {
    await navigate('/Entregas');
    const target = await evaluate(`document.querySelector('main a[href^="/Entregas/Navegar/"]')?.getAttribute('href') || null`);
    if (!target) {
        skips.push('Driver cockpit and modal previews: no active-shift navigation link; no shift was started.');
        return;
    }
    for (const theme of themes) {
        await navigate('/Entregas');
        await setTheme(theme);
        await navigate(target);
        await until(`!!document.querySelector('#cockpitMap.leaflet-container')`);
        assert.equal(await evaluate('document.documentElement.dataset.mglTheme'), theme);
        assert.equal(await evaluate(`!!document.getElementById('btnSimular')`), false, 'Driver cannot simulate telemetry.');
        for (const width of widths) {
            await viewport(width, width < 500 ? 844 : 1000);
            await pause(220);
            await noOverflow('driver-cockpit-' + theme + '-' + width);
            await screenshot('driver-cockpit-' + theme + '-' + width);
            if (width === 390 || width === 360) {
                await modalPreview('driverEmergencyModal', [], 'driver-emergency-' + theme + '-' + width);
                await modalPreview('modalPODNavegacao', ['nomeRecebedorNav', 'documentoRecebedorNav', 'localidadePodNav'],
                    'driver-pod-' + theme + '-' + width);
            }
        }
    }
    checks.push('Driver cockpit: five widths, both themes, available modal previews without submitting');
}

async function customerOrderForm() {
    const fields = ['Descricao', 'TipoCarga', 'PesoKg', 'ValorTotal',
        'AlturaCargaMetros', 'LarguraCargaMetros', 'ComprimentoCargaMetros',
        'OrigemEndereco', 'OrigemNumero', 'OrigemBairro', 'OrigemCidade', 'OrigemEstado', 'OrigemCep',
        'DestinoEndereco', 'DestinoNumero', 'DestinoBairro', 'DestinoCidade', 'DestinoEstado', 'DestinoCep'];
    await pageMatrix('customer-new-order', '/Cliente/Pedido/Novo', '#newClientOrderHeading', fields);
    const form = await evaluate(`(() => {
        const form = document.getElementById('clientOrderForm');
        return {
            method: form.method, action: new URL(form.action).pathname,
            csrf: !!form.querySelector('input[name="__RequestVerificationToken"]')?.value,
            submission: form.querySelector('input[name="ChaveEnvio"]')?.value,
            valid: form.checkValidity(),
            missing: ['Descricao', 'OrigemEndereco', 'OrigemNumero', 'OrigemCidade', 'OrigemEstado',
                'DestinoEndereco', 'DestinoNumero', 'DestinoCidade', 'DestinoEstado']
                .every(id => { const field = document.getElementById(id); return field.required && field.validity.valueMissing && !!field.validationMessage; })
        };
    })()`);
    assert.equal(form.method, 'post');
    assert.equal(form.action, '/Cliente/Pedido/Novo');
    assert.ok(form.csrf && /^[a-f0-9]{32}$/i.test(form.submission));
    assert.equal(form.valid, false, 'An incomplete order is invalid without submitting it.');
    assert.ok(form.missing, 'Required empty addresses and cargo need native validation messages.');
    checks.push('Customer order form: accessible fields, POST/CSRF, submission key and native invalid-state checks without sending an order');
}

async function customerOrderDetails() {
    await navigate('/Cliente/Pedidos');
    const targets = await evaluate(`(() => [...document.querySelectorAll('main tbody tr')].map(row => ({
        path: row.querySelector('a[href^="/Cliente/Pedido/"]')?.getAttribute('href'),
        dispatched: !!row.querySelector('[data-tracking-code]')
    })).filter(item => item.path))()`);
    const pending = targets.find(item => !item.dispatched);
    const dispatched = targets.find(item => item.dispatched);
    assert.ok(pending && dispatched, 'Use a synthetic demonstration with both an unassigned order and an assigned delivery.');
    await pageMatrix('customer-order-undispatched', pending.path, '#clientOrderDetailsHeading');
    assert.equal(await evaluate(`document.getElementById('clientOrderDetails').dataset.hasDelivery`), 'false');
    assert.ok(await evaluate(`document.querySelector('main').textContent.includes('Sua carga')
        && document.querySelector('main').textContent.includes('Aguardando despacho')`));
    assert.equal(await evaluate(`!!document.getElementById('clientOrderPodHeading')`), false);
    assert.equal(await evaluate(`!!document.querySelector('#clientOrderDetails a[href^="/Rastreio"]')`), false);
    await pageMatrix('customer-order-dispatched', dispatched.path, '#clientOrderDetailsHeading');
    assert.equal(await evaluate(`document.getElementById('clientOrderDetails').dataset.hasDelivery`), 'true');
    assert.ok(await evaluate(`!!document.getElementById('clientOrderDetails').dataset.trackingCode
        && !!document.querySelector('#clientOrderDetails a[href^="/Rastreio"]')
        && document.querySelectorAll('#timelineContainer li').length > 0`));
    assert.equal(await evaluate(`document.querySelector('main').textContent.includes('Validação Biométrica')
        || document.querySelector('main').textContent.includes('Bipado e Triado')`), false);
    await evaluate(`document.querySelector('#clientOrderDetails .cc-portal-copy').focus(); window.dispatchEvent(new Event('online'));`);
    await until(`document.getElementById('clientOrderConnectionStatus').textContent.includes('seleção atual')`);
    assert.ok(await evaluate(`document.activeElement.matches('#clientOrderDetails .cc-portal-copy')`), 'A refresh preserves the focused order action.');
    await evaluate(`document.activeElement.blur(); window.dispatchEvent(new Event('online'));`);
    await until(`document.getElementById('clientOrderConnectionStatus').textContent.includes('Dados atualizados na sua conta')`);
    checks.push('Customer details: both unassigned and assigned orders, actual history, focused-action preservation and authenticated refresh');
}

async function main() {
    await openBrowser();
    await login('admin', 'admin123', '/Dashboard');
    await pageMatrix('admin-orders', '/Pedidos', '#ordersHeading', ['orderSearch', 'orderStatus']);
    await filterAndReset('/Pedidos', 'orderSearch', 'orderStatus', 'Registrado');
    await pageMatrix('admin-deliveries', '/Entregas', '#deliveriesHeading', ['deliverySearch', 'deliveryStatus']);
    await filterAndReset('/Entregas', 'deliverySearch', 'deliveryStatus', 'Pendente');

    await pageMatrix('fleet-catalogue', '/Frota', '#fleetRosterTitle', ['fleetRosterSearch','fleetRosterStatus']);
    assert.ok(await evaluate(`document.getElementById('operationalPanel').dataset.refreshUrl.includes('/Frota/Painel')`));
    await evaluate(`document.activeElement?.blur(); window.dispatchEvent(new Event('online'))`);
    await until(`document.getElementById('operationalRefreshStatus').textContent.includes('Dados atualizados')`);
    await filterAndReset('/Frota', 'fleetRosterSearch', 'fleetRosterStatus', 'sem-atribuicao', 'todos');
    await viewport(1440,1000);
    await key('k','KeyK',2);
    assert.equal(await evaluate(`document.getElementById('commandPalette').open`),true);
    await evaluate(`const fleetSearch=document.getElementById('commandSearch'); fleetSearch.value='frota'; fleetSearch.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-command-options] a').getAttribute('href')`),'/Frota');
    await key('Escape');

    await pageMatrix('admin-new-order', '/Pedidos/Create', '#adminOrderForm', ['ClienteId','TipoCarga','Descricao','PesoKg','ValorTotal','OrigemEndereco','DestinoEndereco']);
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('#adminOrderForm .validation-summary-valid')).display`), 'none');
    assert.ok(await evaluate(`!!document.querySelector('#adminOrderForm input[name="__RequestVerificationToken"]')`));
    assert.equal(await evaluate(`document.getElementById('adminOrderForm').checkValidity()`), false);
    await evaluate(`document.getElementById('PesoKg').value='-1'`);
    assert.equal(await evaluate(`document.getElementById('PesoKg').checkValidity()`), false);
    // Intentionally invalid, synthetic data: exercise the server error response,
    // never create an order or bypass the application's cargo validation.
    await evaluate(`(() => {
        const form = document.getElementById('adminOrderForm');
        const client = document.getElementById('ClienteId');
        client.selectedIndex = [...client.options].findIndex(option => option.value);
        document.getElementById('Descricao').value = 'Invalid weight browser fixture';
        form.noValidate = true;
        form.requestSubmit();
    })()`);
    await until(`document.readyState === 'complete' && !!document.querySelector('#adminOrderForm .validation-summary-errors')`);
    assert.notEqual(await evaluate(`getComputedStyle(document.querySelector('#adminOrderForm .validation-summary-errors')).display`), 'none');
    assert.ok(await evaluate(`!!document.querySelector('[data-valmsg-for="PesoKg"].field-validation-error')`));
    await noOverflow('admin-order-server-validation');
    await screenshot('admin-order-server-validation');
    await navigate('/Pedidos');
    const adminDetails = await evaluate(`document.querySelector('main a[href^="/Pedidos/Details/"]')?.getAttribute('href')`);
    assert.ok(adminDetails, 'The demo must have an actual administrative order detail link.');
    await pageMatrix('admin-order-details', adminDetails, '#orderCargoTitle');
    await pageMatrix('admin-dispatch', '/Entregas/Create', '#dispatchOrderForm', ['selectPedidoDespacho','selectVeiculoFrota','MotoristaId','DestinatarioNome','CepDestino','EnderecoDestino']);
    const capacity = await evaluate(`(() => {
        const order = document.getElementById('selectPedidoDespacho');
        const vehicle = document.getElementById('selectVeiculoFrota');
        const orderIndex = [...order.options].findIndex(item => item.value && Number(item.dataset.peso) > 0);
        const vehicleIndex = [...vehicle.options].findIndex(item => item.value && Number(item.dataset.capacidade) > 0);
        if (orderIndex < 0 || vehicleIndex < 0) return null;
        order.selectedIndex = orderIndex;
        vehicle.selectedIndex = vehicleIndex;
        order.dispatchEvent(new Event('change'));
        vehicle.dispatchEvent(new Event('change'));
        return {weight:Number(order.options[orderIndex].dataset.peso),limit:Number(vehicle.options[vehicleIndex].dataset.capacidade),
            state:document.getElementById('badgeSugestaoDespacho').dataset.state,
            message:document.getElementById('textoSugestaoDespacho').textContent};
    })()`);
    assert.ok(capacity, 'Demo cargo and vehicle should expose actual catalogue values.');
    assert.equal(capacity.state, capacity.weight > capacity.limit ? 'warning' : 'within-capacity');
    assert.doesNotMatch(capacity.message, /isenta|liberada|obrigatória|autorizada/i);
    await noOverflow('admin-dispatch-capacity');
    await screenshot('admin-dispatch-capacity');
    checks.push('Admin order and dispatch: actual fields, native validation, eligible detail actions and capacity comparison without operational submissions');

    await login('cliente.demo', 'cliente123', '/Cliente');
    await pageMatrix('customer-overview', '/Cliente', '#operationalPanel');
    assert.ok(await evaluate(`!!document.getElementById('operationalPanel').dataset.refreshUrl`));
    assert.ok(await evaluate(`!!document.getElementById('operationalRefreshStatus')`));
    assert.equal(await evaluate(`!!document.querySelector('#commandSidebar a[href="/Dashboard"], #commandSidebar a[href="/Pedidos"], #commandSidebar a[href="/Entregas"], #commandSidebar a[href="/Frota"]')`), false);
    await pageMatrix('customer-orders', '/Cliente/Pedidos', '#portalOrdersHeading', ['portalOrderSearch', 'portalOrderStatus']);
    await filterAndReset('/Cliente/Pedidos', 'portalOrderSearch', 'portalOrderStatus', 'Cancelado');
    await customerOrderForm();
    await customerOrderDetails();

    await login('carlos.motorista', 'motorista123', '/Entregas');
    await pageMatrix('driver-trips', '/Entregas', '#driverHomeTitle');
    assert.equal(await evaluate(`!!document.getElementById('commandSidebar')`), false);
    assert.ok(await evaluate(`!!document.getElementById('driverOfflineQueueState')?.dataset.offlineUser
        && !!document.getElementById('driverOfflineQueueState')?.dataset.offlineTenant`));
    const shiftFields = await evaluate(`[].map.call(document.querySelectorAll('.cc-driver-shift-form input[name="codigo"]'), field => field.id)`);
    await accessibleFields(shiftFields);
    await pageMatrix('driver-profile', '/Entregas/PerfilMotorista', '#driverProfileTitle');
    assert.ok(await evaluate(`document.querySelector('main').textContent.includes('CPF protegido')`));
    if (process.env.MGL_BROWSER_SKIP_COCKPIT === '1') {
        skips.push('Driver cockpit and modal previews: omitted from this run by MGL_BROWSER_SKIP_COCKPIT.');
    } else {
        await optionalCockpit();
    }

    console.log('Area browser checks passed:\n- ' + checks.join('\n- '));
    if (skips.length) console.log('Not exercised:\n- ' + [...new Set(skips)].join('\n- '));
    console.log('Screenshots: ' + browser.directory);
}

main().catch(async error => {
    console.error(error.message);
    try {
        await screenshot('areas-failed-state');
        console.error('Failure screenshot: ' + browser.directory);
        console.error(await evaluate(`JSON.stringify({path:location.pathname,theme:document.documentElement.dataset.mglTheme,width:innerWidth})`));
    } catch { /* Chromium may already be unavailable. */ }
    process.exitCode = 1;
}).finally(closeBrowser);

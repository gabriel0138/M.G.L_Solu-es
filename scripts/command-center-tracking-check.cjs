// Real Chromium presentation checks for the local synthetic demonstration.
// Anonymous GET requests only; no database edits, fake callbacks or GPS samples.
const assert = require('node:assert/strict');

const base = new URL(process.env.MGL_BROWSER_BASE_URL || process.env.MGL_UI_BASE_URL || 'http://localhost:5290');
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(base.hostname), 'Use only a local demonstration host.');
assert.ok(['http:', 'https:'].includes(base.protocol), 'Use a local HTTP(S) demonstration.');
assert.equal(base.username + base.password, '', 'Do not put credentials in the URL.');
process.env.MGL_UI_BASE_URL = base.origin;
const browser = require('./command-center-browser-check.cjs');
const { openBrowser, closeBrowser, command, evaluate, navigate, viewport, screenshot, pause, onEvent } = browser;
const widths = [1440, 1280, 768, 390, 360];
const themes = ['dark', 'light'];
const validCode = 'MGL-BR100201';
const checks = [];
const limitations = [];
const exceptions = [];

async function until(expression) {
    for (let attempt = 0; attempt < 150; attempt++) {
        try { if (await evaluate(expression)) return; }
        catch (error) { if (!/context.*destroyed|cannot find context/i.test(error.message)) throw error; }
        await pause(100);
    }
    throw new Error('Browser assertion timed out: ' + expression);
}

async function noOverflow(context) {
    const size = await evaluate(`(() => ({ width: innerWidth,
        actual: Math.max(document.documentElement.scrollWidth, document.body?.scrollWidth || 0),
        offenders: [...document.querySelectorAll('.cc-tracking *')].filter(element => {
            const rect = element.getBoundingClientRect();
            return rect.width > 0 && (rect.left < -1 || rect.right > innerWidth + 1);
        }).slice(0, 5).map(element => ({ tag:element.tagName, className:element.className }))
    }))()`);
    assert.ok(size.actual <= size.width, context + ': horizontal overflow ' + JSON.stringify(size));
}

async function setTheme(theme) {
    assert.ok(await evaluate(`!!document.querySelector('[data-theme-toggle]')`), 'A theme control must exist on public pages.');
    if (await evaluate('document.documentElement.dataset.mglTheme') !== theme) {
        await evaluate(`document.querySelector('[data-theme-toggle]').click()`);
    }
    assert.equal(await evaluate('document.documentElement.dataset.mglTheme'), theme);
    await pause(180);
}

async function checkSearch() {
    const field = await evaluate(`(() => {
        const input = document.getElementById('trackingCode');
        const button = input?.form?.querySelector('button[type="submit"]');
        return { exists:!!input, labelled:input?.labels?.length > 0, name:input?.name,
            required:input?.required, method:input?.form?.method,
            action:input?.form ? new URL(input.form.action).pathname : '',
            touch:button?.getBoundingClientRect().height >= 44,
            mainCount:document.querySelectorAll('main').length };
    })()`);
    assert.ok(field.exists && field.labelled && field.required && field.touch, 'The tracking search must be labelled and touch accessible.');
    assert.equal(field.name, 'codigo');
    assert.equal(field.method, 'get');
    assert.equal(field.action, '/Rastreio');
    assert.equal(field.mainCount, 1);
    assert.equal(await evaluate('location.origin'), base.origin);
}

async function matrix(name, path, selector, verify) {
    await navigate(path);
    await until(`!!document.querySelector(${JSON.stringify(selector)})`);
    await evaluate('document.fonts.ready');
    await checkSearch();
    await verify();
    const surfaces = {};
    for (const theme of themes) {
        await setTheme(theme);
        surfaces[theme] = await evaluate(`getComputedStyle(document.querySelector('.cc-tracking .cc-panel')).backgroundColor`);
        for (const width of widths) {
            await viewport(width, width < 500 ? 844 : 1000);
            await pause(180);
            const context = name + '-' + theme + '-' + width;
            await noOverflow(context);
            await checkSearch();
            await screenshot(context);
        }
    }
    assert.notEqual(surfaces.dark, surfaces.light, name + ': selected theme must change the actual surface color.');
    checks.push(name + ': five widths, both themes, accessible search and no page overflow');
}

async function checkCopy() {
    let permissionGranted = false;
    try {
        await command('Browser.grantPermissions', { origin: base.origin,
            permissions: ['clipboardReadWrite', 'clipboardSanitizedWrite'] });
        permissionGranted = true;
    } catch { limitations.push('Chromium did not grant clipboard permission; only honest success/failure presentation was checked.'); }
    await viewport(360, 844);
    await evaluate(`document.getElementById('copyTrackingCode').scrollIntoView({block:'center',behavior:'instant'})`);
    await evaluate('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
    const point = await evaluate(`(() => {
        const button = document.getElementById('copyTrackingCode');
        const rect = button.getBoundingClientRect();
        return { x:rect.x+rect.width/2, y:rect.y+rect.height/2, width:rect.width, height:rect.height,
            named:!!button.getAttribute('aria-label') };
    })()`);
    assert.ok(point.width >= 44 && point.height >= 44 && point.named, 'The copy control must have an accessible name and touch target.');
    await command('Input.dispatchMouseEvent', { type:'mousePressed', x:point.x, y:point.y, button:'left', clickCount:1 });
    await command('Input.dispatchMouseEvent', { type:'mouseReleased', x:point.x, y:point.y, button:'left', clickCount:1 });
    await until(`['copied','failed'].includes(document.getElementById('copyTrackingCode').dataset.copyState)`);
    const state = await evaluate(`document.getElementById('copyTrackingCode').dataset.copyState`);
    if (permissionGranted) {
        assert.equal(state, 'copied', 'Copy may report success only after a successful clipboard write.');
        const read = await command('Runtime.evaluate', {
            expression:'navigator.clipboard.readText()', returnByValue:true, awaitPromise:true, userGesture:true
        });
        assert.ok(!read.exceptionDetails, 'The isolated browser clipboard must be readable after permission was granted.');
        assert.equal(read.result.value, validCode);
    } else {
        assert.ok(['copied','failed'].includes(state), 'Unavailable clipboard access must have an honest result.');
    }
    await noOverflow('tracking-copy-mobile-feedback');
    await screenshot('tracking-copy-mobile-feedback');
    checks.push('Copy: actual browser click and confirmed clipboard result' + (permissionGranted ? ' with clipboard content verified' : ' (read permission unavailable)'));
}

async function main() {
    await openBrowser();
    await command('Runtime.enable');
    onEvent('Runtime.exceptionThrown', event => exceptions.push(event.exceptionDetails?.exception?.description || event.exceptionDetails?.text || 'Unknown browser exception'));
    await command('Network.clearBrowserCookies');
    await viewport(1440, 1000);
    await navigate('/Account/Login');
    const demo = await evaluate(`document.querySelector('.cc-demo-access')?.textContent || ''`);
    assert.ok(demo.includes('Acessos de demonstração local'), 'Use only the Development demonstration with synthetic data.');
    assert.equal(await evaluate('location.origin'), base.origin);

    await matrix('tracking-empty', '/Rastreio', '#trackingEmptyTitle', async () => {
        assert.equal(await evaluate(`!!document.getElementById('publicTimeline')`), false);
        assert.equal(await evaluate(`document.getElementById('trackingCode').value`), '');
    });
    // Exercise the existing GET form, not a synthetic search callback.
    await evaluate(`document.getElementById('trackingCode').value=${JSON.stringify(validCode)};
        document.getElementById('trackingCode').form.requestSubmit()`);
    await until(`document.readyState === 'complete' && !!document.getElementById('publicDeliveryStatus')
        && new URLSearchParams(location.search).get('codigo') === ${JSON.stringify(validCode)}`);
    checks.push('Search: real GET submission resolves the known demonstration code');

    await matrix('tracking-found', '/Rastreio/' + validCode, '#publicTrackingHeadline', async () => {
        const details = await evaluate(`(() => {
            const badge = document.getElementById('publicDeliveryStatus');
            const stages = document.getElementById('publicTrackingStages');
            return { status:badge.textContent.trim(), hidden:stages.hidden,
                active:stages.querySelectorAll('[aria-current="step"]').length,
                timeline:!!document.getElementById('publicTimeline'),
                privateLink:new URL(document.querySelector('.cc-tracking-privacy a').href).pathname,
                hasMap:!!document.querySelector('.cc-tracking .leaflet-container'),
                fabricated:document.querySelector('.cc-tracking').textContent.includes('Bipado'),
                utc:[...document.querySelectorAll('#publicTimeline time')].every(time => time.dateTime.endsWith('Z') && time.textContent.includes('UTC')) };
        })()`);
        assert.ok(['Pendente','Em transporte','Entregue','Cancelada','Em atualização'].includes(details.status));
        assert.equal(details.hidden, ['Cancelada','Em atualização'].includes(details.status));
        if (!details.hidden) assert.equal(details.active, 1);
        assert.ok(details.timeline && details.utc);
        assert.equal(details.privateLink, '/Account/Login');
        assert.equal(details.hasMap, false, 'The minimal public DTO must not fabricate a GPS map.');
        assert.equal(details.fabricated, false);
        await until(`document.getElementById('trackingConnectionStatus').dataset.state !== 'connecting'`);
        const state = await evaluate(`document.getElementById('trackingConnectionStatus').dataset.state`);
        assert.ok(['connected','unavailable','reconnecting','closed'].includes(state), 'Connection state must describe the actual subscription.');
    });
    await checkCopy();

    await matrix('tracking-not-found', '/Rastreio/MGL-UI-NOT-FOUND-7B49', '#trackingErrorTitle', async () => {
        assert.equal(await evaluate(`!!document.getElementById('publicTimeline')`), false);
        assert.ok(await evaluate(`document.querySelector('.cc-tracking-error [role="status"]').textContent.trim().length > 0`));
        assert.equal(await evaluate(`new URL(document.querySelector('.cc-tracking-error a').href).pathname`), '/Rastreio');
    });
    await matrix('tracking-invalid', '/Rastreio?codigo=' + encodeURIComponent('INVALID_UI_CODE!'), '#trackingErrorTitle', async () => {
        assert.equal(await evaluate(`!!document.getElementById('publicDeliveryStatus')`), false);
        assert.ok(await evaluate(`document.querySelector('.cc-tracking-error [role="status"]').textContent.trim().length > 0`));
    });

    assert.deepEqual(exceptions, [], 'Public pages must not produce browser JavaScript exceptions.');
    console.log('Public tracking browser checks passed:\n- ' + checks.join('\n- '));
    if (limitations.length) console.log('Not exercised:\n- ' + limitations.join('\n- '));
    console.log('Screenshots: ' + browser.directory);
}

main().catch(async error => {
    console.error(error.message);
    try {
        await screenshot('tracking-failed-state');
        console.error('Failure screenshot: ' + browser.directory);
        console.error(await evaluate(`JSON.stringify({path:location.pathname,theme:document.documentElement.dataset.mglTheme,
            width:innerWidth,state:document.getElementById('trackingConnectionStatus')?.dataset.state})`));
    } catch { /* Chromium may already be unavailable. */ }
    process.exitCode = 1;
}).finally(closeBrowser);

// Local UI smoke checks using installed Chromium and Node's built-in CDP client.
// No npm dependencies, real browser profile, external accounts or production data.
const { spawn } = require('node:child_process');
const { mkdtempSync, readFileSync, writeFileSync, unlinkSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join } = require('node:path');
const assert = require('node:assert/strict');

function createBrowserClient(options = {}) {
const base = new URL(options.baseUrl || process.env.MGL_UI_BASE_URL || 'http://localhost:5290');
if (!['localhost', '127.0.0.1', '[::1]'].includes(base.hostname)) throw new Error('Use only a local demonstration host.');
const directory = mkdtempSync(join(tmpdir(), 'mgl-cc-browser-'));
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let browser;
let socket;
let sequence = 0;
const pending = new Map();
const eventHandlers = new Map();
async function command(method, params = {}) {
    const id = ++sequence;
    return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { pending.delete(id); reject(new Error('Browser command timed out: ' + method)); }, 15000);
        pending.set(id, { resolve: value => { clearTimeout(timeout); resolve(value); }, reject });
        socket.send(JSON.stringify({ id, method, params }));
    });
}
async function evaluate(expression) {
    const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || 'Browser evaluation failed');
    return result.result.value;
}
async function navigate(path) {
    const target = new URL(path, base).href;
    const previousDocument = await evaluate('performance.timeOrigin');
    await command('Page.navigate', { url: target });
    for (let i = 0; i < 150; i++) {
        if (await evaluate(`location.href === ${JSON.stringify(target)} && performance.timeOrigin !== ${JSON.stringify(previousDocument)} && document.readyState === 'complete'`)) return;
        await pause(100);
    }
    throw new Error('Navigation did not finish: ' + path);
}
async function viewport(width, height) {
    await command('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
    await evaluate('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
}
async function screenshot(name) {
    // Full-document captures otherwise place sticky/fixed elements at the
    // current scroll offset, creating apparent overlaps absent in the viewport.
    const scroll = await evaluate('({x:scrollX,y:scrollY})');
    await evaluate('window.scrollTo({left:0,top:0,behavior:"instant"});new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const metrics = await command('Page.getLayoutMetrics');
    const size = metrics.cssContentSize;
    const image = await command('Page.captureScreenshot', {
        format: 'png', captureBeyondViewport: true,
        clip: { x: 0, y: 0, width: size.width, height: size.height, scale: 1 }
    });
    writeFileSync(join(directory, name + '.png'), Buffer.from(image.data, 'base64'));
    await evaluate(`window.scrollTo({left:${scroll.x},top:${scroll.y},behavior:"instant"})`);
}
async function key(key, code = key, modifiers = 0) {
    // Chromium's native default actions need a virtual key code (keyCode 0 only
    // dispatched DOM events, so Enter never activated a focused button).
    const virtual = { Enter: 13, Tab: 9, Escape: 27, ' ': 32, Space: 32,
        ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40 }[key];
    const text = !modifiers && (key === 'Enter' ? '\r' : key === ' ' ? ' ' : undefined);
    await command('Input.dispatchKeyEvent', { type: 'keyDown', key, code, modifiers,
        windowsVirtualKeyCode: virtual, nativeVirtualKeyCode: virtual, text, unmodifiedText: text });
    await command('Input.dispatchKeyEvent', { type: 'keyUp', key, code, modifiers: 0,
        windowsVirtualKeyCode: virtual, nativeVirtualKeyCode: virtual });
}
async function openBrowser() {
    // This directory is created by this helper, never an existing user profile.
    try { unlinkSync(join(directory, 'DevToolsActivePort')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    const args = ['--headless', '--disable-dev-shm-usage', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', '--user-data-dir=' + directory, 'about:blank'];
    if (process.getuid?.() === 0) args.unshift('--no-sandbox');
    browser = spawn(process.env.MGL_CHROMIUM_PATH || '/usr/bin/chromium', args, { stdio: 'ignore' });
    let port;
    for (let i = 0; i < 150; i++) {
        try { port = Number(readFileSync(join(directory, 'DevToolsActivePort'), 'utf8').split('\n')[0]); break; }
        catch { if (browser.exitCode !== null) throw new Error('Chromium did not start.'); await pause(100); }
    }
    if (!port) throw new Error('Chromium debug endpoint unavailable.');
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', message => {
        const data = JSON.parse(message.data);
        if (data.method) {
            for (const handler of eventHandlers.get(data.method) || []) Promise.resolve(handler(data.params)).catch(error => console.error(error.message));
        }
        if (!pending.has(data.id)) return;
        const entry = pending.get(data.id);
        pending.delete(data.id);
        if (data.error) entry.reject(new Error(data.error.message)); else entry.resolve(data.result);
    });
    await command('Page.enable');
}
async function main() {
    await openBrowser();
    await viewport(1440, 1000);
    await navigate('/Account/Login');
    await evaluate('document.fonts.ready');
    await screenshot('login-dark-desktop');
    await viewport(390, 844);
    assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Login overflow on mobile');
    await screenshot('login-dark-mobile');
    await evaluate(`document.querySelector('[data-theme-toggle]').click()`);
    assert.equal(await evaluate('document.documentElement.dataset.mglTheme'), 'light');
    await screenshot('login-light-mobile');
    await navigate('/Account/Login');
    assert.equal(await evaluate('document.documentElement.dataset.mglTheme'), 'light');
    await evaluate(`document.getElementById('Login').value='admin'; document.getElementById('Senha').value='admin123'; document.querySelector('[data-toggle-password]').click();`);
    assert.equal(await evaluate(`document.getElementById('Senha').type`), 'text');
    await evaluate(`document.querySelector('[data-toggle-password]').click(); document.querySelector('.cc-login-form').requestSubmit();`);
    for (let i = 0; i < 150; i++) { if (await evaluate(`location.pathname === '/Dashboard' && document.readyState === 'complete'`)) break; await pause(100); }
    assert.equal(await evaluate('location.pathname'), '/Dashboard');
    await screenshot('dashboard-light-mobile');
    await evaluate(`document.querySelector('[data-sidebar-toggle]').click()`);
    assert.equal(await evaluate(`document.querySelector('.cc-workspace').inert`), true);
    assert.equal(await evaluate(`document.getElementById('commandSidebar').inert`), false);
    await key('Escape');
    assert.equal(await evaluate(`document.getElementById('commandSidebar').inert`), true);
    await viewport(1440, 1000);
    await evaluate(`document.querySelector('[data-theme-toggle]').click()`);
    await screenshot('dashboard-dark-desktop');
    assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Dashboard overflow on desktop');
    await key('k', 'KeyK', 2);
    assert.equal(await evaluate(`document.getElementById('commandPalette').open`), true);
    await evaluate(`const search = document.getElementById('commandSearch'); search.value='pedidos'; search.dispatchEvent(new Event('input',{bubbles:true}));`);
    assert.equal(await evaluate(`document.querySelector('[data-command-options] a').getAttribute('href')`), '/Pedidos');
    await key('Escape');
    console.log('Browser checks passed: login, password, persisted theme, mobile drawer/inert, desktop layout and Ctrl K.');
    console.log('Screenshots: ' + directory);
}
async function closeBrowser() {
    socket?.close();
    const previous=browser;
    if(previous && previous.exitCode===null && previous.signalCode===null) {
        const exited=new Promise(resolve=>previous.once('exit',resolve));
        previous.kill('SIGTERM');
        await exited;
    }
}
async function restartBrowser() { await closeBrowser(); await openBrowser(); }
function onEvent(method, handler) {
    const listeners = eventHandlers.get(method) || [];
    listeners.push(handler);
    eventHandlers.set(method, listeners);
}
return { openBrowser, closeBrowser, restartBrowser, command, evaluate, navigate, viewport, screenshot, key, pause, onEvent, directory, main };
}
const defaultBrowser = createBrowserClient();
module.exports = { ...defaultBrowser, createBrowserClient };
if (require.main === module) defaultBrowser.main().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(defaultBrowser.closeBrowser);

const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = name => fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/', name), 'utf8');

function setup({ savedTheme, blockedStorage = false, mobile = false } = {}) {
    const values = new Map(savedTheme ? [['mgl.ui.theme', savedTheme]] : []);
    const events = target => {
        target.listeners = {};
        target.addEventListener = (name, callback) => (target.listeners[name] ||= []).push(callback);
        target.emit = (name, event = {}) => { for (const callback of target.listeners[name] || []) callback(event); };
        return target;
    };
    let document;
    const element = (extra = {}) => events({ dataset: {}, attributes: {}, hidden: false,
        setAttribute(name, value) { this.attributes[name] = value; },
        getClientRects: () => [1], focus() { document.activeElement = this; }, querySelector: () => null, ...extra });
    const icon = element();
    const theme = element({ querySelector: () => icon });
    const password = element({ type: 'password', value: 'synthetic-secret' });
    const reveal = element({ dataset: { togglePassword: 'Senha' } });
    const toggle = element();
    const first = element();
    const last = element();
    const sidebar = element({ querySelectorAll: () => [first, last], contains: target => target === first || target === last });
    const backdrop = element();
    const workspace = element();
    const media = events({ matches: mobile });
    document = events({ readyState: 'complete', documentElement: { dataset: {}, style: {} }, body: { dataset: {} }, activeElement: null,
        getElementById: id => ({ Senha: password, commandSidebar: sidebar })[id] || null,
        querySelector: selector => ({ '[data-sidebar-toggle]': toggle, '.cc-sidebar-backdrop': backdrop, '.cc-workspace': workspace })[selector] || null,
        querySelectorAll: selector => ({ '[data-theme-toggle]': [theme], '[data-toggle-password]': [reveal], '[data-sidebar-close]': [backdrop] })[selector] || [] });
    const window = events({ matchMedia: () => media });
    const localStorage = { getItem(key) { if (blockedStorage) throw new Error('denied'); return values.get(key); },
        setItem(key, value) { if (blockedStorage) throw new Error('denied'); values.set(key, value); } };
    const context = { document, window, localStorage, getComputedStyle: () => ({ getPropertyValue: () => '#080F1D' }) };
    vm.runInNewContext(script('command-center-theme.js'), context);
    vm.runInNewContext(script('command-center.js'), context);
    return { document, window, theme, reveal, password, toggle, first, last, sidebar, backdrop, workspace, media, values };
}

test('saved theme loads before the shell and the toggle persists only its UI preference', () => {
    const app = setup({ savedTheme: 'light' });
    assert.equal(app.document.documentElement.dataset.mglTheme, 'light');
    assert.equal(app.theme.attributes['aria-label'], 'Ativar modo escuro');
    app.theme.emit('click');
    assert.equal(app.document.documentElement.dataset.mglTheme, 'dark');
    assert.equal(app.values.get('mgl.ui.theme'), 'dark');
    assert.equal(app.values.size, 1);
});

test('blocked storage does not prevent login or theme selection', () => {
    const app = setup({ blockedStorage: true });
    assert.equal(app.document.documentElement.dataset.mglTheme, 'dark');
    app.theme.emit('click');
    assert.equal(app.document.documentElement.dataset.mglTheme, 'light');
});

test('show password preserves its value and announces the visibility state', () => {
    const app = setup();
    app.reveal.emit('click');
    assert.equal(app.password.type, 'text');
    assert.equal(app.password.value, 'synthetic-secret');
    assert.equal(app.reveal.attributes['aria-pressed'], 'true');
    app.reveal.emit('click');
    assert.equal(app.password.type, 'password');
    assert.equal(app.reveal.attributes['aria-label'], 'Mostrar senha');
});

test('closed mobile navigation is inert and Escape restores the trigger focus', () => {
    const app = setup({ mobile: true });
    assert.equal(app.sidebar.inert, true);
    assert.equal(app.backdrop.hidden, true);
    app.toggle.emit('click');
    assert.equal(app.sidebar.inert, false);
    assert.equal(app.workspace.inert, true);
    assert.equal(app.document.body.dataset.sidebarOpen, 'true');
    assert.equal(app.document.activeElement, app.first);
    app.document.emit('keydown', { key: 'Escape', preventDefault() {} });
    assert.equal(app.sidebar.inert, true);
    assert.equal(app.workspace.inert, false);
    assert.equal(app.document.activeElement, app.toggle);
});

test('mobile drawer traps Tab and resizing restores access to desktop navigation', () => {
    const app = setup({ mobile: true });
    app.toggle.emit('click');
    app.last.focus();
    app.document.emit('keydown', { key: 'Tab', shiftKey: false, preventDefault() {} });
    assert.equal(app.document.activeElement, app.first);
    app.media.matches = false;
    app.media.emit('change');
    assert.equal(app.sidebar.inert, false);
    assert.equal(app.sidebar.attributes['aria-hidden'], 'false');
    assert.equal(app.backdrop.hidden, true);
});

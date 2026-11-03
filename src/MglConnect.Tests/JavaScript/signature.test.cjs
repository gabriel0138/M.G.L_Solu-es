const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

test('signature is prepared synchronously without a map and clear removes only the current drawing', () => {
    const callbacks = {};
    const target = name => ({ addEventListener: (event, handler) => { callbacks[name + ':' + event] = handler; } });
    const canvas = { ...target('canvas'), width: 560, height: 200,
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 280, height: 100 }),
        getContext: () => ({ beginPath() {}, moveTo() {}, lineTo() {}, stroke() {}, clearRect() {} }),
        setPointerCapture() {}, toDataURL: () => 'data:image/png;base64,TEST-SIGNATURE' };
    const field = { value: '' };
    const elements = { sigCanvasNav: canvas, formPODNavegacao: target('form'), assinaturaBase64Nav: field, btnClearSigNav: target('clear') };
    const document = { getElementById: id => elements[id], addEventListener: (_, handler) => handler() };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../MglConnect.Web/wwwroot/js/pod-signature.js'), 'utf8'), { document });
    callbacks['canvas:pointerdown']({ clientX: 10, clientY: 10, pointerId: 1, preventDefault() {} });
    callbacks['canvas:pointermove']({ clientX: 30, clientY: 30 });
    callbacks['canvas:pointerup']();
    callbacks['form:mgl:prepare-pod']();
    assert.equal(field.value, 'data:image/png;base64,TEST-SIGNATURE');
    callbacks['clear:click']();
    callbacks['form:mgl:prepare-pod']();
    assert.equal(field.value, '');
});

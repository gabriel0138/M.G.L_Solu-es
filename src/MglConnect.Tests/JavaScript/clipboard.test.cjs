const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
function fixture(writeText, fallback = false) {
    const timers = [];
    const attributes = new Map([['title', 'Copiar código'], ['aria-label', 'Copiar código']]);
    const button = {innerHTML:'<i>copy</i>', className:'btn btn-outline-secondary', dataset:{}, disabled:false,
        getAttribute: key => attributes.get(key) ?? null,
        setAttribute: (key,value) => attributes.set(key,value), removeAttribute: key => attributes.delete(key)};
    let removed = 0;
    const context = {navigator:{clipboard:writeText ? {writeText} : null},
        document:{getElementById:()=>null, createElement:()=>({style:{}, select(){}, remove(){removed++;}}),
            body:{appendChild(){}}, execCommand:()=>fallback, activeElement:{focus(){}}},
        setTimeout: callback => timers.push(callback)};
    vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../../MglConnect.Web/wwwroot/js/site.js'),'utf8'),context);
    return {copy:code=>context.copiarCodigoRastreio(code,button), button, timers, removed:()=>removed};
}
test('copy waits for Clipboard acknowledgement and restores the exact button presentation', async()=>{
    let resolve;
    const f=fixture(()=>new Promise(done=>{resolve=done;}));
    const pending=f.copy('MGL-TEST');
    assert.equal(f.button.dataset.copyState,'pending');
    assert.equal(f.button.innerHTML,'<i>copy</i>');
    assert.equal(f.button.disabled,true);
    resolve();
    assert.equal(await pending,true);
    assert.equal(f.button.dataset.copyState,'copied');
    f.timers[0]();
    assert.equal(f.button.className,'btn btn-outline-secondary');
    assert.equal(f.button.innerHTML,'<i>copy</i>');
    assert.equal(f.button.getAttribute('aria-label'),'Copiar código');
});
test('denied Clipboard does not announce success or leave an unhandled rejection', async()=>{
    const f=fixture(async()=>{throw new Error('Denied');});
    assert.equal(await f.copy('MGL-TEST'),false);
    assert.equal(f.button.dataset.copyState,'failed');
    assert.match(f.button.getAttribute('aria-label'),/Não foi possível/);
    assert.equal(f.button.disabled,false);
});
for(const success of [false,true]) test('legacy copy honours its real result: '+success,async()=>{
    const f=fixture(null,success);
    assert.equal(await f.copy('MGL-TEST'),success);
    assert.equal(f.removed(),1);
    assert.equal(f.button.dataset.copyState,success?'copied':'failed');
});

// Read-only checks for the catalogue on a local Development demonstration.
const assert = require('node:assert/strict');
const browser = require('./command-center-browser-check.cjs');
const {openBrowser,closeBrowser,evaluate,navigate,viewport,screenshot,pause}=browser;
async function until(expression) {
    for(let i=0;i<150;i++) {
        if(await evaluate(expression)) return;
        await pause(100);
    }
    throw new Error('Roster check timed out: '+expression);
}
async function main() {
    await openBrowser();
    await navigate('/Account/Login');
    assert.ok(await evaluate(`document.querySelector('.cc-demo-access')?.textContent.includes('admin123')`), 'Use only the local Development demo.');
    await evaluate(`document.getElementById('Login').value='admin';document.getElementById('Senha').value='admin123';document.querySelector('.cc-login-form').requestSubmit()`);
    await until(`location.pathname==='/Dashboard' && document.readyState==='complete'`);
    await navigate('/Frota');
    assert.ok(await evaluate(`!!document.querySelector('#commandSidebar a[href="/Frota"][aria-current="page"]')`));
    for(const theme of ['dark','light']) {
        if(await evaluate('document.documentElement.dataset.mglTheme')!==theme) await evaluate(`document.querySelector('[data-theme-toggle]').click()`);
        for(const width of [1440,1280,768,390,360]) {
            await viewport(width,width<500?844:1000);
            const metrics = await evaluate(`(() => ({
                overflow:document.documentElement.scrollWidth>innerWidth,
                numbers:[...document.querySelectorAll('.cc-roster-metrics > div')].map(card=>({row:Math.round(card.getBoundingClientRect().top),bottom:card.querySelector('dd').getBoundingClientRect().bottom})),
                fields:[...document.querySelectorAll('.cc-roster .cc-filter-bar :is(input,select,button)')].map(field=>field.getBoundingClientRect().height)
            }))()`);
            assert.equal(metrics.overflow,false,'Roster overflow at '+width);
            for(const item of metrics.numbers) for(const other of metrics.numbers.filter(candidate=>candidate.row===item.row))
                assert.ok(Math.abs(item.bottom-other.bottom)<1,'Roster numbers should align within each row.');
            assert.ok(metrics.fields.every(height=>height>=44),'Roster filters should have usable touch targets.');
            await screenshot('roster-'+theme+'-'+width);
        }
    }
    await evaluate(`document.activeElement?.blur();window.dispatchEvent(new Event('online'))`);
    await until(`document.getElementById('operationalRefreshStatus').textContent.includes('Dados atualizados')`);
    assert.equal(await evaluate(`document.querySelectorAll('main').length`),1);
    console.log('Roster browser checks passed: authorized catalogue, five widths, both themes, aligned metrics, touch targets and live private refresh.');
    console.log('Screenshots: '+browser.directory);
}
main().catch(async error=>{
    console.error(error.message);
    try{await screenshot('roster-failed-state');console.error('Failure screenshot: '+browser.directory);}catch{}
    process.exitCode=1;
}).finally(closeBrowser);

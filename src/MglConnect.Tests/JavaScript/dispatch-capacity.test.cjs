const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');
function fixture(weight, capacity) {
    const listeners={};
    const order={selectedIndex:0,options:[{value:'1',dataset:{peso:weight}}],addEventListener:(_,fn)=>listeners.order=fn};
    const vehicle={selectedIndex:0,options:[{value:'2',dataset:{capacidade:capacity}}],addEventListener:(_,fn)=>listeners.vehicle=fn};
    const box={dataset:{}};
    const message={textContent:''};
    const elements={selectPedidoDespacho:order,selectVeiculoFrota:vehicle,badgeSugestaoDespacho:box,textoSugestaoDespacho:message};
    vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../../MglConnect.Web/wwwroot/js/dispatch-capacity.js'),'utf8'),{document:{getElementById:id=>elements[id]}});
    return{order,vehicle,box,message,listeners};
}
test('fractional cargo is compared with the real vehicle capacity without discarding decimals',()=>{
    const f=fixture('1250.5','1250.4');
    assert.equal(f.box.dataset.state,'warning');
    assert.match(f.message.textContent,/1\.250,5/);
    f.vehicle.options[0].dataset.capacidade='2000';
    f.listeners.vehicle();
    assert.equal(f.box.dataset.state,'within-capacity');
    assert.match(f.message.textContent,/dimensões/);
});
test('missing or invalid values never imply that a route or a vehicle was approved',()=>{
    for(const [weight,capacity] of [[undefined,'2000'],['-1','2000'],['10',undefined],['10','0'],['10','NaN']]) {
        const f=fixture(weight,capacity);
        assert.equal(f.box.dataset.state,'unknown');
        assert.doesNotMatch(f.message.textContent,/autorizad|isent|obrigatóri|liberad/i);
    }
});
test('changing the order recalculates the capacity state',()=>{
    const f=fixture('10','100');
    f.order.options[0].dataset.peso='101';
    f.listeners.order();
    assert.equal(f.box.dataset.state,'warning');
});

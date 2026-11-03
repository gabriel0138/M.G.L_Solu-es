const {test}=require('node:test');
const assert=require('node:assert/strict');
const {MglDriverGpsQueue:Queue}=require('../../MglConnect.Web/wwwroot/js/driver-gps-queue.js');
const owner={userId:'4',tenantId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'};
const shift='11111111-1111-4111-8111-111111111111';
class Store {
    constructor(){this.rows=new Map();}
    async list(key){return [...this.rows.values()].filter(i=>i.owner===key).map(i=>structuredClone(i));}
    async enqueue(item,limit){if(this.rows.size>=limit)throw new Error('Fila GPS cheia');this.rows.set(item.id,structuredClone(item));return item;}
    async update(id,key,fn){const item=this.rows.get(id);if(item?.owner!==key)return null;const result=fn(structuredClone(item));if(result)this.rows.set(id,structuredClone(result));return result;}
    async remove(id,key){if(this.rows.get(id)?.owner===key)this.rows.delete(id);}
}
function setup(options={}){
    let clock=Date.parse('2026-10-29T12:00:00Z'),sequence=0;
    const store=options.store||new Store(),requests=[],states=[];
    const response=data=>({ok:true,status:200,redirected:false,json:async()=>data});
    const context={...owner,role:'Motorista',token:'EPHEMERAL-CREDENTIAL-NEVER-PERSIST'};
    const accepted=body=>({eventoId:body.eventoId,estado:'confirmado',sequencia:1,capturadoEmUtc:body.capturadoEmUtc,
        recebidoEmUtc:new Date(clock).toISOString(),posicao:{eventId:body.eventoId,latitude:body.latitude,longitude:body.longitude}});
    const fetch=async(url,input)=>{
        requests.push({url,input});
        if(url.includes('Contexto'))return options.context?options.context():response(context);
        assert.ok(store.rows.size>0,'Persistence must precede any GPS POST');
        return options.send?options.send(JSON.parse(input.body),accepted,response):response({receipts:JSON.parse(input.body).eventos.map(accepted)});
    };
    const config={store,fetch,now:()=>clock,uuid:()=>`aaaaaaaa-1111-4111-8111-${String(++sequence).padStart(12,'0')}`,onChange:s=>states.push(s)};
    const queue=new Queue(owner,config);
    return {queue,store,requests,states,config,now:()=>clock,advance:ms=>{clock+=ms;},position:extra=>({latitude:-23.5,longitude:-46.6,precisaoMetros:8,capturadoEmUtc:new Date(clock).toISOString(),velocidadeKmH:null,direcao:null,...extra}),response,accepted};
}
test('GPS is committed before transmission and confirmation identifies the original capture and actor scope',async()=>{
    const env=setup(),item=await env.queue.enqueue(3,shift,env.position());
    assert.equal(env.requests.length,0);assert.equal((await env.queue.snapshot()).pending,1);
    await env.queue.sync();
    const state=await env.queue.snapshot();assert.equal(state.pending,0);assert.equal(state.lastConfirmation.eventoId,item.id);
    const saved=env.store.rows.get(item.id);assert.equal(saved.body.capturadoEmUtc,item.body.capturadoEmUtc);assert.equal(saved.state,'confirmed');
    assert.equal(saved.body.velocidadeKmH,null);assert.equal(saved.body.direcao,null);
    assert.ok(!JSON.stringify(saved).includes('EPHEMERAL'));assert.ok(!JSON.stringify(saved).includes('token'));
});
test('a dropped response after commit replays the same id and capture safely after page reload',async()=>{
    const received=new Map();let drop=true;
    const env=setup({send:async(body,ack,response)=>{
        const receipts=body.eventos.map(item=>{const prior=received.get(item.eventoId);const result=prior?{...prior,estado:'duplicado'}:ack(item);received.set(item.eventoId,result);return result;});
        if(drop){drop=false;throw new Error('Response interrupted AFTER server commit');}return response({receipts});
    }});
    const item=await env.queue.enqueue(3,shift,env.position());await env.queue.sync();
    assert.equal((await env.queue.snapshot()).pending,1);assert.equal(received.size,1);
    env.advance(2100);const reloaded=new Queue(owner,env.config);await reloaded.sync();
    assert.equal((await reloaded.snapshot()).pending,0);assert.equal(received.size,1);assert.equal(env.store.rows.get(item.id).body.capturadoEmUtc,item.body.capturadoEmUtc);
});
for(const failure of ['network','401','403','500','redirect','unidentified-receipt','wrong-capture','wrong-coordinates'])test(`GPS stays pending without a matching receipt: ${failure}`,async()=>{
    const env=setup({send:async(body,ack,response)=>{
        if(failure==='network')throw new Error('offline');
        if(['401','403','500'].includes(failure))return {ok:false,status:Number(failure),redirected:false};
        if(failure==='redirect')return {ok:true,status:200,redirected:true};
        const receipt=ack(body.eventos[0]);
        if(failure==='unidentified-receipt')receipt.eventoId='another-id';
        if(failure==='wrong-capture')receipt.capturadoEmUtc='2020-01-01T00:00:00Z';
        if(failure==='wrong-coordinates')receipt.posicao.latitude=0;
        return response({receipts:[receipt]});
    }});
    await env.queue.enqueue(3,shift,env.position());await env.queue.sync();assert.equal((await env.queue.snapshot()).pending,1);
});
test('a cookie change cannot replay the previous account or organization queue',async()=>{
    const env=setup({context:()=>({ok:true,json:async()=>({...owner,userId:'999',role:'Motorista',token:'ephemeral'})})});
    await env.queue.enqueue(3,shift,env.position());await env.queue.sync();
    assert.equal(env.requests.filter(r=>r.url.includes('sincronizar')).length,0);assert.equal(env.states.at(-1).state,'account-mismatch');
    const other=new Queue({userId:'999',tenantId:owner.tenantId},env.config);await other.sync();assert.equal((await other.snapshot()).pending,0);assert.equal((await env.queue.snapshot()).pending,1);
});
test('partial batches distinguish confirmed rejected and unacknowledged events',async()=>{
    const env=setup({send:async(body,ack,response)=>response({receipts:[ack(body.eventos[0]),{eventoId:body.eventos[1].eventoId,estado:'rejeitado',motivo:'turno_indisponivel'}]})});
    for(let n=0;n<3;n++){await env.queue.enqueue(3,shift,env.position({latitude:-23.5+n*.001}));env.advance(5000);}
    await env.queue.sync();const state=await env.queue.snapshot();assert.equal(state.pending,1);assert.equal(state.rejected,1);assert.ok(state.lastConfirmation);
    const reloaded=await new Queue(owner,env.config).snapshot();assert.deepEqual(reloaded.rejectionReasons,['turno_indisponivel']);
});
test('exponential backoff and eight-attempt ceiling survive reload, while manual retry restores an opportunity',async()=>{
    let fail=true;const env=setup({send:async(body,ack,response)=>{if(fail)throw new Error('offline');return response({receipts:body.eventos.map(ack)});}});
    await env.queue.enqueue(3,shift,env.position());await env.queue.sync();
    await new Queue(owner,env.config).sync();assert.equal(env.requests.filter(r=>r.url.includes('sincronizar')).length,1);
    for(let n=1;n<8;n++){env.advance(61000);await new Queue(owner,env.config).sync();}
    env.advance(61000);await env.queue.sync();assert.equal(env.requests.filter(r=>r.url.includes('sincronizar')).length,8);
    assert.equal((await env.queue.snapshot()).paused,1);fail=false;await env.queue.retry();assert.equal((await env.queue.snapshot()).pending,0);
});
test('seven-day expiry is explicit, preserves events, and only an authenticated manual recovery can clear terminal copies',async()=>{
    const env=setup();const item=await env.queue.enqueue(3,shift,env.position());env.advance(Queue.MAX_AGE+1);
    await env.queue.sync();assert.equal(env.requests.length,0);assert.equal((await env.queue.snapshot()).expired,1);
    assert.equal(env.store.rows.get(item.id).body.capturadoEmUtc,item.body.capturadoEmUtc);
    env.advance(30*24*60*60*1000);await env.queue.maintain();assert.equal(env.store.rows.size,1);
    await assert.rejects(env.queue.clearTerminal(true),/cópia/);
    const backup=JSON.parse(await env.queue.recoveryFile());assert.equal(backup.events[0].id,item.id);
    await env.queue.clearTerminal(true);assert.equal(env.store.rows.size,0);
});
test('the storage cap spans owners and rejects growth without evicting unconfirmed samples',async()=>{
    const env=setup();for(let n=0;n<Queue.LIMIT;n++)env.store.rows.set('other-'+n,{id:'other-'+n,owner:'another-owner',state:'pending',body:{capturadoEmUtc:new Date(env.now()).toISOString()}});
    await assert.rejects(env.queue.enqueue(3,shift,env.position()),/cheia/);assert.equal(env.store.rows.size,Queue.LIMIT);assert.equal(env.requests.length,0);
});
test('only twenty compact confirmed receipts are retained and another owners samples remain untouched',async()=>{
    const env=setup();for(let n=0;n<30;n++){await env.queue.enqueue(3,shift,env.position({latitude:-23+n*.001}));await env.queue.sync();env.advance(5000);}
    assert.equal(env.store.rows.size,20);assert.equal((await env.queue.snapshot()).pending,0);
});
test('two tabs reserve the same pending event atomically and share a single delivery attempt',async()=>{
    const env=setup();await env.queue.enqueue(3,shift,env.position());await Promise.all([env.queue.sync(),new Queue(owner,env.config).sync()]);
    assert.equal(env.requests.filter(r=>r.url.includes('sincronizar')).length,1);assert.equal((await env.queue.snapshot()).pending,0);
});
test('a local storage failure cannot be mistaken for persistence or transmission',async()=>{
    const env=setup();env.store.enqueue=async()=>{throw new Error('quota denied');};
    await assert.rejects(env.queue.enqueue(3,shift,env.position()),/quota denied/);assert.equal(env.requests.length,0);assert.equal(env.store.rows.size,0);
});
test('invalid device coordinates accuracy and clocks are rejected before persistence',async()=>{
    const env=setup();for(const bad of [{latitude:NaN},{longitude:181},{precisaoMetros:10001},{velocidadeKmH:-1},{direcao:360},{capturadoEmUtc:'invalid'},{capturadoEmUtc:new Date(env.now()+60000).toISOString()}])
        await assert.rejects(env.queue.enqueue(3,shift,env.position(bad)),/inválida/);
    assert.equal(env.store.rows.size,0);
});
test('a repeated callback for the same device fix reuses its persisted event identifier',async()=>{
    const env=setup();const one=await env.queue.enqueue(3,shift,env.position());const two=await new Queue(owner,env.config).enqueue(3,shift,env.position());
    assert.equal(one.id,two.id);assert.equal(env.store.rows.size,1);await env.queue.sync();assert.equal((await env.queue.snapshot()).pending,0);
});
test('bounded draining commits more than one batch and stops promptly when transport makes no progress',async()=>{
    let sends=0;const env=setup({send:async(body,ack,response)=>{sends++;return response({receipts:body.eventos.map(ack)});}});
    for(let n=0;n<120;n++){await env.queue.enqueue(3,shift,env.position({latitude:-23.5+n*.001}));env.advance(1000);}
    await env.queue.drain();assert.equal(sends,3);assert.equal((await env.queue.snapshot()).pending,0);
    let failures=0;const broken=setup({send:async()=>{failures++;throw new Error('offline');}});
    await broken.queue.enqueue(3,shift,broken.position());await broken.queue.drain();assert.equal(failures,1);assert.equal((await broken.queue.snapshot()).pending,1);
});

test('low accuracy stays auditable and a five-day backlog retains original UUID and timestamp',async()=>{
    const env=setup();const item=await env.queue.enqueue(3,shift,env.position({precisaoMetros:700}));env.advance(5*24*60*60*1000);
    await new Queue(owner,env.config).sync();assert.equal((await env.queue.snapshot()).pending,0);
    assert.equal(env.store.rows.get(item.id).body.capturadoEmUtc,item.body.capturadoEmUtc);assert.equal(env.store.rows.get(item.id).body.precisaoMetros,700);
});
test('legacy local expiry is recoverable within seven days without changing its event identity',async()=>{
    const env=setup();const item=await env.queue.enqueue(3,shift,env.position());
    env.store.rows.get(item.id).state='rejected';env.store.rows.get(item.id).reason='expirado_local';env.advance(2*24*60*60*1000);
    await env.queue.maintain();assert.equal(env.store.rows.get(item.id).state,'pending');assert.equal(env.store.rows.get(item.id).id,item.id);
});
test('manual export and cleanup cannot cross an account change or discard pending events',async()=>{
    let account='4';const env=setup({context:()=>({ok:true,json:async()=>({...owner,userId:account,role:'Motorista',token:'ephemeral'})})});
    const item=await env.queue.enqueue(3,shift,env.position());await env.queue.recoveryFile();await env.queue.clearTerminal(true);assert.equal(env.store.rows.size,1);
    account='another';await assert.rejects(env.queue.recoveryFile(),/proprietário/);assert.equal(env.store.rows.get(item.id).state,'pending');
});

test('a moving server quota stays pending with its reason and can be confirmed later without replacing the event',async()=>{
    let quota=true;const env=setup({send:async(body,ack,response)=>response({receipts:body.eventos.map(item=>quota?{eventoId:item.eventoId,estado:'pendente',motivo:'limite_diario'}:ack(item))})});
    const item=await env.queue.enqueue(3,shift,env.position());await env.queue.sync();
    assert.equal((await env.queue.snapshot()).pending,1);assert.equal((await env.queue.snapshot()).rejected,0);
    assert.deepEqual((await new Queue(owner,env.config).snapshot()).pendingReasons,['limite_diario']);
    quota=false;env.advance(2100);await env.queue.sync();assert.equal(env.store.rows.get(item.id).state,'confirmed');assert.equal(env.store.rows.get(item.id).body.eventoId,item.id);
});

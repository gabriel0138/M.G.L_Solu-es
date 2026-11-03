/* Owned GPS outbox. Tokens never enter IndexedDB; only matching authenticated
   owners replay. This page cannot guarantee background geolocation. */
class MglIndexedGpsStore {
    async open() {
        if (this.database) return this.database;
        this.database = await new Promise((resolve, reject) => {
            const request = indexedDB.open('mgl-driver-gps-v1', 1);
            request.onupgradeneeded = () => request.result.createObjectStore('events', { keyPath: 'id' }).createIndex('owner', 'owner');
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error('Feche outras abas para abrir a fila GPS.'));
        });
        this.database.onversionchange = () => { this.database.close(); this.database = null; };
        return this.database;
    }
    async list(owner) {
        const db = await this.open();
        return new Promise((resolve,reject) => {
            const tx=db.transaction('events','readonly');const request=tx.objectStore('events').index('owner').getAll(owner);
            let rows=[];request.onsuccess=()=>{rows=request.result;};
            tx.oncomplete=()=>resolve(rows);tx.onerror=tx.onabort=()=>reject(tx.error || new Error('Leitura GPS interrompida.'));
        });
    }
    async enqueue(item,limit) {
        const db=await this.open();
        return new Promise((resolve,reject)=>{
            const tx=db.transaction('events','readwrite');const store=tx.objectStore('events');let failure;
            const count=store.count();count.onsuccess=()=>{
                if(count.result>=limit){failure=new Error('Fila GPS cheia. Nenhuma nova posição foi salva; sincronize os eventos pendentes.');tx.abort();}
                else store.add(item);
            };
            tx.oncomplete=()=>resolve(item);tx.onerror=tx.onabort=()=>reject(failure || tx.error || new Error('GPS não foi salvo no aparelho.'));
        });
    }
    async update(id,owner,change) {
        const db=await this.open();
        return new Promise((resolve,reject)=>{
            const tx=db.transaction('events','readwrite');const store=tx.objectStore('events');let result=null;
            const get=store.get(id);get.onsuccess=()=>{
                if(get.result?.owner!==owner)return;
                result=change(get.result);
                if(result)store.put(result);
            };
            tx.oncomplete=()=>resolve(result);tx.onerror=tx.onabort=()=>reject(tx.error || new Error('Estado GPS não foi salvo.'));
        });
    }
    async remove(id,owner) {
        return this.update(id,owner,item=>({...item, purge:true})).then(async result=>{
            if(!result)return;
            const db=await this.open();
            return new Promise((resolve,reject)=>{
                const tx=db.transaction('events','readwrite');const store=tx.objectStore('events');
                const get=store.get(id);get.onsuccess=()=>{if(get.result?.owner===owner && get.result.purge)store.delete(id);};
                tx.oncomplete=resolve;tx.onerror=tx.onabort=()=>reject(tx.error);
            });
        });
    }
}
class MglDriverGpsQueue {
    static LIMIT=2000;
    static MAX_AGE=7*24*60*60*1000;
    static TERMINAL_AGE=7*24*60*60*1000;
    constructor(scope,options={}) {
        if(!scope.userId || !scope.tenantId)throw new Error('Identidade GPS ausente.');
        this.scope={userId:String(scope.userId),tenantId:String(scope.tenantId).toLowerCase()};
        this.owner=this.scope.userId+':'+this.scope.tenantId;
        this.store=options.store || new MglIndexedGpsStore();
        this.fetch=options.fetch || globalThis.fetch.bind(globalThis);
        this.now=options.now || (()=>Date.now());this.uuid=options.uuid || (()=>crypto.randomUUID());
        this.onChange=options.onChange || (()=>{});this.inFlight=null;
    }
    async snapshot(state='ready',extra={}) {
        const items=await this.store.list(this.owner);
        const confirmed=items.filter(i=>i.state==='confirmed').sort((a,b)=>Date.parse(b.receipt.recebidoEmUtc)-Date.parse(a.receipt.recebidoEmUtc));
        const rejectionReasons=[...new Set(items.filter(i=>i.state==='rejected'||i.state==='expired').map(i=>i.reason))];
        const pendingReasons=[...new Set(items.filter(i=>i.state==='pending'&&i.reason).map(i=>i.reason))];
        const event={state,rejectionReasons,pendingReasons,expired:items.filter(i=>i.state==='expired').length,pending:items.filter(i=>i.state==='pending').length,rejected:items.filter(i=>i.state==='rejected').length,
            paused:items.filter(i=>i.state==='pending'&&i.attempts>=8).length,lastConfirmation:confirmed[0]?.receipt || null,...extra};
        this.onChange(event);return event;
    }
    async maintain() {
        const now=this.now();const items=await this.store.list(this.owner);
        const confirmed=items.filter(i=>i.state==='confirmed').sort((a,b)=>Date.parse(b.receipt.recebidoEmUtc)-Date.parse(a.receipt.recebidoEmUtc));
        for(const item of items) {
            const age=now-Date.parse(item.body.capturadoEmUtc);
            if((item.state==='pending'||item.state==='rejected'&&item.reason==='expirado_local')&&age>MglDriverGpsQueue.MAX_AGE)
                await this.store.update(item.id,this.owner,current=>current.state==='confirmed'?null:{...current,state:'expired',reason:'prazo_sincronizacao',terminalAt:now});
            else if(item.state==='rejected'&&item.reason==='expirado_local'&&age<=MglDriverGpsQueue.MAX_AGE)
                await this.store.update(item.id,this.owner,current=>current.state==='rejected'?{...current,state:'pending',attempts:0,nextAt:0,reason:null}:null);
            else if(item.state==='confirmed'&&confirmed.indexOf(item)>=20)
                await this.store.remove(item.id,this.owner);
        }
    }
    async enqueue(deliveryId,shiftId,position) {
        const captured=Date.parse(position.capturadoEmUtc);
        if(!Number.isSafeInteger(Number(deliveryId)) || Number(deliveryId)<=0 || typeof shiftId!=='string' || !/^[0-9a-f-]{36}$/i.test(shiftId)
            || !Number.isFinite(position.latitude) || position.latitude < -90 || position.latitude > 90
            || !Number.isFinite(position.longitude) || position.longitude < -180 || position.longitude > 180
            || (position.precisaoMetros!=null && (!Number.isFinite(position.precisaoMetros)||position.precisaoMetros<0||position.precisaoMetros>10000))
            || (Number.isFinite(position.velocidadeKmH) && (position.velocidadeKmH<0||position.velocidadeKmH>200))
            || (Number.isFinite(position.direcao) && (position.direcao<0||position.direcao>=360))
            || !Number.isFinite(captured) || captured>this.now()+30000 || this.now()-captured>MglDriverGpsQueue.MAX_AGE)
            throw new Error('Posição GPS inválida ou fora do prazo de sincronização.');
        const id=this.uuid();
        const body={eventoId:id,entregaId:Number(deliveryId),turnoId:shiftId,latitude:position.latitude,longitude:position.longitude,
            precisaoMetros:position.precisaoMetros ?? null,capturadoEmUtc:new Date(captured).toISOString(),
            velocidadeKmH:Number.isFinite(position.velocidadeKmH)?position.velocidadeKmH:null,direcao:Number.isFinite(position.direcao)?position.direcao:null,simulado:false};
        await this.maintain();
        const previous=(await this.store.list(this.owner)).find(item=>item.body.entregaId===body.entregaId
            && item.body.turnoId===body.turnoId && item.body.capturadoEmUtc===body.capturadoEmUtc
            && item.body.latitude===body.latitude && item.body.longitude===body.longitude && item.body.precisaoMetros===body.precisaoMetros);
        if(previous)return previous;
        const item={id,owner:this.owner,body,state:'pending',attempts:0,nextAt:0,createdAt:this.now()};
        await this.store.enqueue(item,MglDriverGpsQueue.LIMIT);
        await this.snapshot('pending');return item;
    }
    async request(url,options) {
        const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),15000);
        try{return await this.fetch(url,{...options,signal:controller.signal});}finally{clearTimeout(timer);}
    }
    sync() {
        if(!this.inFlight)this.inFlight=this.syncOwned().finally(()=>{this.inFlight=null;});
        return this.inFlight;
    }
    drain() {
        if(!this.drainFlight)this.drainFlight=(async()=>{
            const monotonic=()=>globalThis.performance?.now()??Date.now();
            const started=monotonic();
            for(let batch=0;batch<40 && monotonic()-started<10000;batch++){
                const before=await this.snapshot();if(!before.pending)return before;
                const after=await this.sync();if(after.pending>=before.pending)return after;
            }
            return this.snapshot();
        })().finally(()=>{this.drainFlight=null;});
        return this.drainFlight;
    }
    async retry() {
        for(const item of await this.store.list(this.owner))if(item.state==='pending')
            await this.store.update(item.id,this.owner,current=>current.state==='pending'?{...current,attempts:0,nextAt:0}:null);
        return this.sync();
    }
    async authenticateOwner() {
        const response=await this.request('/Motorista/Offline/Contexto',{credentials:'same-origin',cache:'no-store'});
        if(!response.ok||response.redirected)throw new Error('Entre novamente com o motorista proprietário para recuperar a fila.');
        const context=await response.json();
        if(String(context.userId)!==this.scope.userId||String(context.tenantId).toLowerCase()!==this.scope.tenantId||context.role!=='Motorista'||!context.token)
            throw new Error('A sessão não pertence ao proprietário desta fila.');
        return context;
    }
    async recoverRecent() {
        await this.authenticateOwner();
        for(const item of await this.store.list(this.owner))if(item.state==='rejected'&&['expirado_local','horario_invalido','turno_indisponivel','viagem_indisponivel'].includes(item.reason)
            &&this.now()-Date.parse(item.body.capturadoEmUtc)<=MglDriverGpsQueue.MAX_AGE)
            await this.store.update(item.id,this.owner,current=>current.state==='rejected'?{...current,state:'pending',attempts:0,nextAt:0,reason:null}:null);
        return this.retry();
    }
    async recoveryFile() {
        await this.authenticateOwner();await this.maintain();
        const events=(await this.store.list(this.owner)).filter(item=>item.state!=='confirmed');
        const content=JSON.stringify({format:'mgl-gps-recovery-v1',owner:this.owner,createdAt:new Date(this.now()).toISOString(),
            warning:'Cópia local não constitui confirmação do servidor. Não alterar horários, UUIDs ou associações.',events},null,2);
        this.recoveryPreparedIds=new Set(events.map(item=>item.id));return content;
    }
    async clearTerminal(confirmed) {
        if(!confirmed||!this.recoveryPreparedIds)throw new Error('Gere e guarde uma cópia antes de confirmar a limpeza.');
        await this.authenticateOwner();
        for(const item of await this.store.list(this.owner))if(this.recoveryPreparedIds.has(item.id)&&['expired','rejected'].includes(item.state))await this.store.remove(item.id,this.owner);
        this.recoveryPreparedIds=null;return this.snapshot();
    }
    async syncOwned() {
        await this.maintain();
        const now=this.now();const batch=[];
        for(const item of (await this.store.list(this.owner)).sort((a,b)=>Date.parse(a.body.capturadoEmUtc)-Date.parse(b.body.capturadoEmUtc))) {
            if(batch.length>=50)break;
            const reserved=await this.store.update(item.id,this.owner,current=>{
                if(current.state!=='pending'||current.attempts>=8||current.nextAt>now)return null;
                return {...current,attempts:current.attempts+1,nextAt:now+Math.min(60000,2000*2**current.attempts)};
            });
            if(reserved)batch.push(reserved);
        }
        if(!batch.length)return this.snapshot();
        let response;
        try {
            const auth=await this.request('/Motorista/Offline/Contexto',{credentials:'same-origin',cache:'no-store'});
            if(!auth.ok||auth.redirected)return this.snapshot('auth-required');
            const context=await auth.json();
            if(String(context.userId)!==this.scope.userId||String(context.tenantId).toLowerCase()!==this.scope.tenantId||context.role!=='Motorista'||!context.token)
                return this.snapshot('account-mismatch');
            response=await this.request('/api/gps/sincronizar',{method:'POST',credentials:'same-origin',cache:'no-store',
                headers:{'Content-Type':'application/json',Authorization:'Bearer '+context.token},body:JSON.stringify({eventos:batch.map(i=>i.body)})});
            if(!response.ok||response.redirected)return this.snapshot(response.status===401||response.status===403?'auth-required':'pending');
            const result=await response.json();
            if(!Array.isArray(result.receipts))return this.snapshot('pending');
            const accepted=[];const rejected=[];
            for(const item of batch) {
                const receipt=result.receipts.find(r=>r.eventoId===item.id);
                if(!receipt)continue;
                if(['confirmado','duplicado'].includes(receipt.estado) && Number.isSafeInteger(receipt.sequencia)&&receipt.sequencia>0
                    && Number.isFinite(Date.parse(receipt.recebidoEmUtc)) && Date.parse(receipt.capturadoEmUtc)===Date.parse(item.body.capturadoEmUtc)
                    && receipt.posicao?.eventId===item.id && receipt.posicao.latitude===item.body.latitude && receipt.posicao.longitude===item.body.longitude) {
                    await this.store.update(item.id,this.owner,current=>({...current,state:'confirmed',receipt,terminalAt:this.now()}));accepted.push(receipt);
                } else if(receipt.estado==='pendente' && receipt.motivo==='limite_diario') {
                    await this.store.update(item.id,this.owner,current=>current.state==='confirmed'?null:{...current,reason:'limite_diario'});
                } else if(receipt.estado==='rejeitado' && typeof receipt.motivo==='string') {
                    await this.store.update(item.id,this.owner,current=>current.state==='confirmed'?null:{...current,state:'rejected',reason:receipt.motivo,terminalAt:this.now()});rejected.push({...receipt,entregaId:item.body.entregaId,turnoId:item.body.turnoId});
                }
            }
            await this.maintain();return this.snapshot('updated',{accepted,rejectedReceipts:rejected,metrics:result.metrics});
        } catch {return this.snapshot('pending');}
    }
}
if(typeof module!=='undefined')module.exports={MglDriverGpsQueue,MglIndexedGpsStore};
if(typeof window!=='undefined'){window.MglDriverGpsQueue=MglDriverGpsQueue;window.MglIndexedGpsStore=MglIndexedGpsStore;}

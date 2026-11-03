(() => {
    const status=document.getElementById('offlineGpsStatus');let running=false,queue;
    async function resume(){
        if(running||!navigator.onLine||document.hidden)return;running=true;
        try{
            const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),10000);
            let response;try{response=await fetch('/Motorista/Offline/Contexto',{credentials:'same-origin',cache:'no-store',signal:controller.signal});}finally{clearTimeout(timeout);}
            if(!response.ok||response.redirected){status.textContent='Entre novamente como motorista para consultar e sincronizar sua fila GPS.';return;}
            const context=await response.json();if(context.role!=='Motorista'||!context.userId||!context.tenantId)return;
            const scope={userId:context.userId,tenantId:context.tenantId};
            // Rebuild from the freshly authenticated owner after a cookie change.
            const owner=String(scope.userId)+':'+String(scope.tenantId).toLowerCase();
            if(queue?.owner!==owner)queue=new MglDriverGpsQueue(scope,{onChange:event=>{
                status.textContent=event.state==='account-mismatch'||event.state==='auth-required'
                    ?'A sessão mudou. Entre com o motorista proprietário para sincronizar.'
                    :`${event.pending} posições GPS pendentes · ${event.rejected} rejeitadas ou expiradas. ${event.paused?'Tentativas pausadas; abra a viagem para tentar novamente.':''}`;
            }});
            await queue.drain();
        }catch{status.textContent='Não foi possível confirmar a sincronização. Os eventos continuam no aparelho conforme os limites da fila.';}
        finally{running=false;}
    }
    window.addEventListener('online',resume);document.addEventListener('visibilitychange',resume);
    setInterval(resume,15000);resume();
})();

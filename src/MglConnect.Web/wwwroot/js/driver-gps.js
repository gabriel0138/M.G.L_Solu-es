/* Page-only collection/replay lifecycle. A service worker may wake this page;
   it never captures GPS or holds authentication credentials. */
(() => {
    let shell=document.getElementById('driverGpsSync');
    const identity=shell||document.getElementById('driverOfflineQueueState');
    if(!identity||!window.MglDriverGpsQueue)return;
    const collectionScope=Boolean(shell?.dataset.delivery&&shell?.dataset.shift);
    if(!shell){
        shell=document.createElement('div');shell.id='driverGpsReplayState';shell.className='alert alert-info m-3';
        shell.setAttribute('role','status');shell.setAttribute('aria-live','polite');shell.hidden=true;
        shell.innerHTML='<div class="d-flex flex-wrap align-items-center gap-2"><span data-gps-sync-message>Consultando a fila GPS…</span><button type="button" class="btn btn-outline-secondary" style="min-height:44px" data-gps-sync-retry hidden>Tentar envio GPS</button></div><small data-gps-sync-last></small>';
        identity.after(shell);
    }
    const message=shell.querySelector('[data-gps-sync-message]');const last=shell.querySelector('[data-gps-sync-last]');
    const retry=shell.querySelector('[data-gps-sync-retry]');let stopped=false,storageFailure=null,lastPending=0;
    const reasons={viagem_indisponivel:'viagem encerrada ou não atribuída',turno_indisponivel:'turno encerrado ou incompatível',
        recurso_indisponivel:'veículo ou motorista indisponível',autorizacao_revogada:'autorização revogada',
        limite_historico:'limite do histórico atingido',horario_invalido:'horário fora do período permitido',
        dados_invalidos:'dados de localização inválidos',evento_conflitante:'identificador em conflito',
        expirado_local:'prazo anterior expirado',prazo_sincronizacao:'prazo de sete dias esgotado',limite_diario:'cota móvel de 24h temporariamente atingida',simulacao_nao_permitida:'simulação não autorizada'};
    const render=event=>{
        lastPending=event.pending+event.rejected+(event.expired||0);
        shell.dataset.expired=String(event.expired||0);
        shell.dataset.state=event.state;shell.dataset.pending=String(event.pending);shell.dataset.rejected=String(event.rejected);
        const auth=event.state==='account-mismatch'||event.state==='auth-required';
        shell.hidden=!collectionScope&&!event.pending&&!event.rejected&&!event.expired&&!storageFailure;
        message.textContent=storageFailure?'GPS não salvo no aparelho. '+storageFailure.message:auth?'GPS preservado. Entre com o mesmo motorista e organização para sincronizar.'
            :event.paused?`${event.pending} posições pendentes. Tentativas pausadas; confira a conexão e tente novamente.`
            :`${event.pending} posições GPS pendentes · ${event.rejected} rejeitadas · ${event.expired||0} expiradas (preservadas).`;
        if(event.pendingReasons?.includes('limite_diario'))message.textContent+=' Cota móvel de 24h atingida no servidor; posições preservadas para nova tentativa.';
        if(event.rejected||event.expired)message.textContent+=' Motivos: '+(event.rejectionReasons||[]).map(reason=>reasons[reason]||'confirmação não disponível').join(', ')+'. Confira a viagem ou procure a operação.';
        last.textContent=event.lastConfirmation?'Última confirmação do servidor: '+new Date(event.lastConfirmation.recebidoEmUtc).toLocaleTimeString('pt-BR'):'Nenhuma confirmação GPS recebida.';
        retry.hidden=event.pending===0;retry.disabled=false;
        for(const receipt of event.accepted||[])window.dispatchEvent(new CustomEvent('mgl:gps-confirmed',{detail:{receipt,metrics:event.metrics}}));
        if(event.state==='account-mismatch'||event.rejectedReceipts?.some(r=>collectionScope&&r.entregaId===Number(shell.dataset.delivery)&&r.turnoId===shell.dataset.shift&&['viagem_indisponivel','turno_indisponivel','autorizacao_revogada','recurso_indisponivel'].includes(r.motivo)))
            window.dispatchEvent(new Event('mgl:gps-scope-unavailable'));
    };
    const queue=new MglDriverGpsQueue({userId:identity.dataset.user||identity.dataset.offlineUser,tenantId:identity.dataset.tenant||identity.dataset.offlineTenant},{onChange:render});
    const failed=error=>{storageFailure=error;shell.hidden=false;shell.dataset.state='storage-error';message.textContent='GPS não confirmado. '+error.message;};
    const resume=()=>{if(!stopped&&navigator.onLine&&!document.hidden)queue.drain().catch(failed);};
    window.mglGpsOutbox={
        intervalMs(){return lastPending>=1000?600000:lastPending>=500?300000:navigator.onLine?5000:60000;},
        async capture(position){
            if(stopped)throw new Error('A coleta desta página foi encerrada.');
            if(!collectionScope)throw new Error('Abra uma viagem com turno ativo para coletar GPS.');
            try { await queue.enqueue(Number(shell.dataset.delivery),shell.dataset.shift,position); }
            catch(error){failed(error);throw error;}
            storageFailure=null;await queue.snapshot('pending');
            if(navigator.onLine)resume();
            else navigator.serviceWorker?.ready.then(reg=>reg.sync?.register('sync-gps')).catch(()=>{});
        },queue
    };
    // Recovery actions are authenticated again; a new cookie cannot export or clear a prior owner.
    let actions=shell.querySelector('[data-gps-recovery]');
    if(!actions){actions=document.createElement('details');actions.dataset.gpsRecovery='';actions.innerHTML='<summary>Recuperação e limites da fila</summary><p>Envio automático por até sete dias, com turno e permissões verificados. Eventos expirados e rejeitados não são apagados automaticamente. A cadência offline passa de 60 s para 5/10 min conforme a fila cresce; leituras intermediárias não são gravadas. Limite de 2.000 eventos.</p><div class="d-flex flex-wrap gap-2"><button type="button" class="btn btn-outline-secondary" data-gps-recover>Tentar rejeitados recentes</button><button type="button" class="btn btn-outline-secondary" data-gps-export>Gerar cópia local</button><button type="button" class="btn btn-outline-secondary" data-gps-clear>Limpar rejeitados/expirados da cópia gerada</button></div>';shell.append(actions);}
    shell.querySelector('[data-gps-recover]')?.addEventListener('click',()=>queue.recoverRecent().catch(failed));
    shell.querySelector('[data-gps-export]')?.addEventListener('click',async()=>{
        try{const content=await queue.recoveryFile(),url=URL.createObjectURL(new Blob([content],{type:'application/json'}));
            const link=document.createElement('a');link.href=url;link.download='mgl-gps-recuperacao.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),30000);
            message.textContent='Cópia local gerada para download. Confira se o arquivo foi guardado antes de qualquer limpeza.';
        }catch(error){failed(error);}
    });
    shell.querySelector('[data-gps-clear]')?.addEventListener('click',()=>{
        if(!confirm('Você guardou a cópia gerada? Serão removidos apenas os rejeitados/expirados incluídos nela, deste proprietário. Pendentes não serão removidos.'))return;
        queue.clearTerminal(true).catch(failed);
    });
    retry.addEventListener('click',()=>{retry.disabled=true;queue.retry().catch(failed).finally(()=>{retry.disabled=false;});});
    window.addEventListener('online',resume);document.addEventListener('visibilitychange',resume);
    navigator.serviceWorker?.addEventListener('message',event=>{if(event.data?.type==='MGL_SYNC_REQUIRED')resume();});
    window.addEventListener('mgl:delivery-confirmed',resume);
    window.addEventListener('pagehide',()=>{stopped=true;clearInterval(timer);});
    const timer=setInterval(resume,15000);
    queue.maintain().then(()=>queue.snapshot()).then(resume).catch(failed);
})();

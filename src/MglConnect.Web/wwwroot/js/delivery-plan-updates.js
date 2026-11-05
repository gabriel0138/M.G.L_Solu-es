// Notifications carry revision identifiers only. Geometry is fetched through scoped HTTP.
window.mglDeliveryPlanUpdates=(tracker,trip,initial)=>{
    const notice=document.getElementById('planningUpdateNotice'),text=document.getElementById('planningUpdateText');
    if(!notice||!tracker.hubConnection)return;
    const review=document.getElementById('btnReviewRoute');
    let revision=initial.revision||0,planId=initial.id||null,generation=0,stopped=false;
    let stopCount=initial.stopCount||0;
    let distance=initial.distance,expiry=initial.validUntil;
    tracker.planRevision=revision;
    async function refresh(notification=null){
        if(stopped||notification&&notification.entregaId!==trip||notification&&notification.revision<revision)return;
        const ticket=++generation;
        try {
            const response=await fetch(`/Entregas/${trip}/Planejamento`,{credentials:'same-origin',cache:'no-store'});
            if(stopped||ticket!==generation)return;
            if(!response.ok||response.redirected){
                if(response.status===404&&!planId&&!notification)return;
                if(response.status===401||response.status===403||response.status===404||response.redirected){tracker.clearPlannedRoute();if(review)review.disabled=true;notice.hidden=false;text.textContent='Planejamento indisponível ou acesso alterado. Consulte a operação.';}
                return;
            }
            const {plan}=await response.json();if(stopped||ticket!==generation||!plan||plan.planRevision<revision)return;
            const changed=plan.planningId!==planId||(plan.rotaDisponivel&&tracker.routeCoordinates.length<2);
            if(!changed&&!plan.rotaDisponivel&&!plan.isCalculating){tracker.clearPlannedRoute();if(review)review.disabled=true;notice.hidden=false;text.textContent='Planejamento expirado ou indisponível. Consulte a operação.';return;}
            if(!changed)return;
            window.mglControlledDemo?.pause();
            if(!tracker.applyPersistedPlan(plan,{preserveView:true}))return;
            if(review)review.disabled=!plan.rotaDisponivel;
            revision=plan.planRevision;planId=plan.planningId;expiry=plan.validUntilUtc;
            notice.dataset.revision=String(revision);notice.dataset.planId=planId;notice.hidden=false;
            const before=Number.isFinite(distance)?distance.toLocaleString('pt-BR',{maximumFractionDigits:2})+' km':'sem estimativa';
            const after=plan.rotaDisponivel?Number(plan.distanciaKm).toLocaleString('pt-BR',{maximumFractionDigits:2})+' km':'sem rota';
            const count=plan.requestedStops?.length||0;
            text.textContent=`Planejamento v${revision} carregado · ${before} → ${after} · paradas ${stopCount} → ${count}. Confira o trajeto; caminhão não validado.`;
            stopCount=count;
            distance=plan.rotaDisponivel?Number(plan.distanciaKm):null;
            if(tracker.demoMode)await window.mglControlledDemo?.refresh();
        } catch {if(!stopped){notice.hidden=false;text.textContent='Atualização de rota pendente. A última localização foi preservada.';}}
    }
    tracker.hubConnection.on('PlanningUpdated',refresh);
    tracker.hubConnection.onreconnected(()=>refresh());
    const online=()=>refresh();window.addEventListener('online',online);
    document.getElementById('btnReviewRoute')?.addEventListener('click',()=>tracker.fitPlannedRoute());
    const timer=setInterval(()=>{
        if(expiry&&Date.now()>=Date.parse(expiry)){tracker.clearPlannedRoute();if(review)review.disabled=true;notice.hidden=false;text.textContent='Planejamento expirado. Solicite novo cálculo à operação.';expiry=null;}
    },5000);
    window.addEventListener('pagehide',()=>{stopped=true;generation++;clearInterval(timer);window.removeEventListener('online',online);},{once:true});
    window.mglPlanUpdates={refresh,get revision(){return revision;}};refresh();
};

/* Server-owned synthetic circuit. Never enters the native GPS offline queue. */
window.mglDemoNavigation = (tracker, map, trip) => {
    const panel=document.getElementById('demoRouteControls');
    if(!panel)return;
    const button=document.getElementById('btnDemoGps'), detourButton=document.getElementById('btnDemoDetour');
    const roadRehearsal=panel.dataset.roadRehearsal==='true';
    const roadDescription=panel.dataset.routeSource==='HistoricalRoadCapture'?'Captura viária histórica':panel.dataset.routeSource==='LocalProviderFixture'?'Provedor local simulado':'Malha viária OSRM';
    const endpoint=`/Entregas/${trip}/Demonstracao`;
    let timer=null, next=0, busy=false, pending=null, stopped=false, routeKey='';
    let alternate=false, size=0;
    const stops=L.layerGroup().addTo(map);
    const token=panel.querySelector('[name="__RequestVerificationToken"]').value;
    function pause(){clearTimeout(timer);timer=null;button.setAttribute('aria-pressed','false');button.textContent='Iniciar GPS sintético';}
    function apply(data) {
        const plan=data.plan;
        panel.dataset.routeAvailable=String(plan?.rotaDisponivel===true);
        if(!plan?.rotaDisponivel || plan.trajetoCoordenadas.length<2) {
            pause();routeKey='';pending=null;tracker.suspendNavigation();stops.clearLayers();
            for(const key of ['routePolyline','routePolylineOutline','destinationMarker'])if(tracker[key]){map.removeLayer(tracker[key]);tracker[key]=null;}
            tracker.routeCoordinates=[];
            button.disabled=detourButton.disabled=true;
            document.getElementById('maneuverRoad').textContent='Rota indisponível';
            document.getElementById('maneuverInstruction').textContent='GPS disponível · sem orientação ou métricas de rota.';
            return;
        }
        document.getElementById('maneuverRoad').textContent=roadRehearsal?roadDescription+' · GPS sintético':'Circuito sintético de demonstração';
        const coords=plan.trajetoCoordenadas.map(c=>[c.latitude,c.longitude]);
        const key=JSON.stringify(coords);
        tracker.applyControlledDemoState(data);
        if(key!==routeKey){
            routeKey=key;
            stops.clearLayers();
            (roadRehearsal?coords.slice(0,1):coords.slice(0,-1)).forEach((point,index)=>{
                const label=document.createElement('span');label.textContent=index===0?(roadRehearsal?'Origem da rota viária':'Origem fictícia'):`Vértice do traçado ${index}`;
                L.circleMarker(point,{radius:index===0?7:3,color:'#00c9b7',fillOpacity:1}).bindTooltip(label).addTo(stops);
            });
            if(roadRehearsal)(plan.requestedStops||[]).slice(1,-1).forEach(point=>{
                const label=document.createElement('span');label.textContent='Parada declarada · não é GPS';
                L.circleMarker([point.latitude,point.longitude],{radius:6,color:'#ffb020',fillOpacity:1}).bindTooltip(label).addTo(stops);
            });
        }
        size=coords.length;button.disabled=detourButton.disabled=false;
        if(data.latest){

            const index=coords.findIndex(c=>c[0]===data.latest.latitude&&c[1]===data.latest.longitude);
            next=index<0?0:index+1;
        }
        panel.dataset.planId=plan.planningId||'';panel.dataset.sample=String(next);panel.dataset.recalculated=String(data.recalculated===true);
        document.getElementById('maneuverInstruction').textContent=roadRehearsal?(data.recalculated?'Novo cálculo OSRM · GPS de ensaio':roadDescription+' · GPS sintético · caminhão não validado'):data.recalculated?'Desvio do cenário · circuito alternativo carregado':'Circuito e GPS fictícios · sem homologação';
    }
    async function refresh(){
        if(stopped)return;
        try{
            const res=await fetch(endpoint,{cache:'no-store'});if(!res.ok)throw new Error('Demonstração não autorizada ou indisponível.');
            const data=await res.json();
            alternate=!roadRehearsal&&!!data.latest&&data.plan.trajetoCoordenadas[0]?.latitude!==-23.5505;
            apply(data);
        }catch(error){pause();document.getElementById('maneuverInstruction').textContent=error.message;}
    }
    async function sendSample(index,detour=false,input=null){
        if(stopped||busy)return;
        busy=true;
        pending=input||pending||{eventId:crypto.randomUUID(),sample:index,detour,planId:roadRehearsal?panel.dataset.planId:null,capturedAt:new Date().toISOString()};
        try {
            const response=await fetch(endpoint,{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify(pending)});
            if(!response.ok)throw new Error('Falha no envio do GPS sintético. Tente novamente.');
            const result=await response.json();
            if(result.receipt?.estado!=='confirmado'&&result.receipt?.estado!=='duplicado'){pending=null;throw new Error(result.mensagem||result.receipt?.motivo||'Demonstração indisponível.');}
            alternate=roadRehearsal?false:detour;apply(result);pending=null;
            return result;
        }catch(error){pause();document.getElementById('maneuverInstruction').textContent=error.message;throw error;}
        finally{busy=false;}
    }
    async function tick(){
        if(button.getAttribute('aria-pressed')!=='true'||stopped)return;
        try{await sendSample(next,alternate);}catch{return;}
        if(next>=size){pause();button.textContent='Circuito percorrido';return;}
        timer=setTimeout(tick,1200);
    }
    button.addEventListener('click',()=>{
        if(button.getAttribute('aria-pressed')==='true'){pause();return;}
        if(document.getElementById('btnToggleGps')?.getAttribute('aria-pressed')==='true')document.getElementById('btnToggleGps').click();
        if(next>=size){next=0;routeKey='';tracker.progressMeters=0;}
        button.setAttribute('aria-pressed','true');button.textContent='Pausar GPS sintético';tick();
    });
    detourButton.addEventListener('click',async()=>{pause();if(busy)return;try{await sendSample(roadRehearsal?Math.min(next,size-2):0,true);}catch{}});
    document.getElementById('btnToggleGps')?.addEventListener('click',pause);
    tracker.hubConnection?.onreconnected(refresh);
    window.addEventListener('online',refresh);
    function stop(){stopped=true;stops.clearLayers();pause();button.disabled=detourButton.disabled=true;window.removeEventListener('online',refresh);}
    window.addEventListener('pagehide',stop,{once:true});window.addEventListener('mgl:delivery-confirmed',stop,{once:true});
    window.mglControlledDemo={sendSample,refresh,pause,tracker,get busy(){return busy;}};
    refresh();
};

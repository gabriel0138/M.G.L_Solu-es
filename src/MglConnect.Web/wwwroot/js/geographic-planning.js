(() => {
    const form=document.getElementById('geographicPlanning');if(!form)return;
    const map=window.mglPlanningMap;if(!map)return;
    const context=JSON.parse(document.getElementById('geographyContext').textContent);
    const list=form.querySelector('[data-stops]'),message=form.querySelector('[data-message]');
    const save=document.getElementById('saveConfirmedRoute'),add=document.getElementById('addGeographicStop');
    const previewButton=document.getElementById('previewConfirmedRoute'),previewSummary=document.getElementById('geographicPreview');
    const previewLink=document.getElementById('viewDraftOnMap'),mapPreview=document.getElementById('geographicPreviewMap');
    const layer=L.layerGroup().addTo(map);let active=null,closed=false,previewLine=null,supersededLine=null,approvalId=null,approvalExpiry=null,dragKey=null,nextKey=0,renderEpoch=0,previewAbort=null,previewBusy=false,publishing=false,generation=0,previewFingerprint=null;
    const snapshot=()=>JSON.stringify(stops.map(({address,latitude,longitude,source,confirmed,candidateLabel})=>({address,latitude,longitude,source,confirmed,candidateLabel})));
    function resetPreview(){generation++;previewAbort?.abort();previewAbort=null;previewBusy=false;previewFingerprint=null;approvalId=null;approvalExpiry=null;save.textContent='Publicar planejamento confirmado';comparison.hidden=true;cancel.hidden=true;if(supersededLine){map.removeLayer(supersededLine);supersededLine=null;}previewLink.hidden=true;mapPreview.hidden=true;if(previewLine){map.removeLayer(previewLine);previewLine=null;}previewSummary.hidden=true;previewSummary.dataset.state='stale';}
    const orderStatus=document.getElementById('geographicOrderStatus'),comparison=document.getElementById('routeComparison'),cancel=document.getElementById('cancelRoutePreview');
    const initial=context.stops?.length>=2?context.stops:[{address:context.origin},{address:context.destination}];
    const stops=initial.map(s=>({...s,confirmed:false}));
    const valid=p=>Number.isFinite(p.latitude)&&Number.isFinite(p.longitude)&&Math.abs(p.latitude)<=90&&Math.abs(p.longitude)<=180;
    function node(tag,text){const el=document.createElement(tag);if(text)el.textContent=text;return el;}
    function label(index){return index===0?'Origem':index===stops.length-1?'Destino':`Parada ${index}`;}
    function update(invalidate=true){
        if(invalidate)resetPreview();
        layer.clearLayers();
        stops.forEach((s,i)=>{if(valid(s)){
            const marker=L.circleMarker([s.latitude,s.longitude],{radius:8,color:s.confirmed?'#00c9b7':'#ffb020',fillOpacity:1});
            marker.on('add',()=>{const element=marker.getElement();element?.setAttribute('role','img');element?.setAttribute('aria-label',`${i+1}. ${label(i)} · ${s.confirmed?'confirmado pelo operador':'aguardando confirmação'}`);});
            marker.bindTooltip(node('span',String(i+1)),{permanent:true,direction:'top',className:'cc-stop-order'}).addTo(layer);
        }});
        const draft=stops.filter(valid);if(!map._loaded&&draft.length)map.fitBounds(L.latLngBounds(draft.map(p=>[p.latitude,p.longitude])),{padding:[24,24],maxZoom:17,animate:false});
        const ready=stops.every(s=>s.confirmed&&valid(s)&&s.address?.trim().length>=3);
        previewButton.disabled=publishing||previewBusy||!ready;save.disabled=publishing||previewBusy||!ready||!approvalId||previewFingerprint!==snapshot();add.disabled=publishing||stops.length>=10;
    }
    function select(s,p,source,candidate){
        Object.assign(s,{latitude:p.latitude,longitude:p.longitude,source,candidateLabel:candidate,confirmed:false});
        s.checkbox.disabled=false;s.checkbox.checked=false;
        s.status.textContent=`${source==='Photon'?'Sugestão aproximada do Photon':'Ponto escolhido no mapa'} · ${p.latitude.toFixed(6)}, ${p.longitude.toFixed(6)}. Confirme visualmente.`;
        update();
    }
    function moveStop(key,target,direction){
        if(publishing)return;const from=stops.findIndex(s=>s.key===key);
        if(from<1||from>=stops.length-1||target<1||target>=stops.length-1||from===target)return;
        const moved=stops.splice(from,1)[0];stops.splice(target,0,moved);render();
        const field=[...list.children].find(f=>f.dataset.stopKey===key);
        const button=field?.querySelector(`[data-move="${direction}"]:not(:disabled)`);
        (button||field?.querySelector('legend'))?.focus();
        orderStatus.textContent=`Parada movida para a posição ${target+1}. Endereço e ponto preservados; gere nova prévia.`;
    }
    function render(){
        renderEpoch++;active=null;stops.forEach(s=>s.abort?.abort());list.replaceChildren();
        stops.forEach((s,i)=>{
            s.key??=String(++nextKey);const epoch=renderEpoch;
            const field=node('fieldset'),title=label(i);field.className='cc-geographic-stop';field.dataset.stopKey=s.key;const legend=node('legend',`${i+1}. ${title}${i===0||i===stops.length-1?' · fixo':''}`);legend.tabIndex=-1;field.append(legend);
            const input=node('input');input.type='text';input.className='form-control';input.value=s.address||'';input.maxLength=240;input.id='geographicAddress'+i;
            const inputLabel=node('label','Endereço de '+title.toLowerCase());inputLabel.htmlFor=input.id;field.append(inputLabel,input);
            const buttons=node('div');buttons.className='cc-geographic-actions';
            const search=node('button','Buscar no Photon');search.type='button';search.className='btn btn-outline-secondary';search.setAttribute('aria-label','Buscar '+title.toLowerCase()+' no Photon');
            const pick=node('button','Escolher no mapa');pick.type='button';pick.className='btn btn-outline-secondary';pick.setAttribute('aria-label','Escolher '+title.toLowerCase()+' no mapa');pick.setAttribute('aria-pressed','false');
            buttons.append(search,pick);field.append(buttons);
            const status=node('p','Selecione e confirme o ponto. Uma sugestão não garante um endereço exato.');status.className='small my-2';status.setAttribute('role','status');s.status=status;
            const results=node('div');results.className='cc-geographic-results';results.setAttribute('aria-label','Sugestões de '+title.toLowerCase());
            const confirm=node('label');confirm.className='cc-geographic-confirm';const checkbox=node('input');checkbox.type='checkbox';checkbox.disabled=!valid(s);checkbox.checked=s.confirmed;s.checkbox=checkbox;
            confirm.append(checkbox,node('span','Confirmo este ponto no mapa para '+title.toLowerCase()));
            checkbox.addEventListener('change',()=>{s.confirmed=checkbox.checked;update();});
            input.addEventListener('input',()=>{s.abort?.abort();s.address=input.value;s.confirmed=false;s.latitude=s.longitude=null;checkbox.checked=false;checkbox.disabled=true;results.replaceChildren();status.textContent='Endereço alterado: selecione e confirme novamente.';update();});
            pick.addEventListener('click',()=>{
                active=s;map.mglManualView=true;form.querySelectorAll('[aria-pressed]').forEach(b=>b.setAttribute('aria-pressed','false'));pick.setAttribute('aria-pressed','true');
                message.textContent='Clique no mapa para indicar '+title.toLowerCase()+'. O ponto não será confirmado automaticamente.';
                if(!map._loaded)map.setView([-23.555,-46.645],14);
                document.getElementById('truckMap').scrollIntoView({block:'center',behavior:'instant'});
            });
            search.addEventListener('click',async()=>{
                s.abort?.abort();s.abort=new AbortController();search.disabled=true;results.replaceChildren();status.textContent='Consultando Photon…';
                const query=input.value;
                const body=new FormData();body.set('entregaId',form.elements.entregaId.value);body.set('endereco',query);body.set('__RequestVerificationToken',form.elements.__RequestVerificationToken.value);
                try {
                    const response=await fetch('/Roteamento/Sugestoes',{method:'POST',body,credentials:'same-origin',signal:s.abort.signal});
                    if(!response.ok||response.redirected)throw new Error();const reply=await response.json();if(closed||epoch!==renderEpoch||input.value!==query)return;
                    status.textContent=reply.state==='Available'?'Sugestões aproximadas: selecione e confira no mapa.':'Busca indisponível ou sem resultados. Você pode escolher um ponto no mapa.';
                    for(const suggestion of reply.suggestions||[]){if(!valid(suggestion))continue;const choice=node('button',suggestion.label+' · aproximada');choice.type='button';choice.className='btn btn-outline-secondary';
                        choice.addEventListener('click',()=>{select(s,suggestion,'Photon',suggestion.label);active=null;map.mglManualView=true;map.stop();map.setView([s.latitude,s.longitude],17,{animate:false,reset:true});message.textContent='Confira o marcador e marque a confirmação do ponto.';});results.append(choice);}
                }catch(error){if(error.name!=='AbortError')status.textContent='Falha na busca. Escolha no mapa ou tente novamente.';}
                finally{search.disabled=false;}
            });
            field.append(results,status,confirm);
            if(i>0&&i<stops.length-1){
                const tools=node('div');tools.className='cc-geographic-actions';
                for(const [direction,title,delta] of [['up','↑ Subir',-1],['down','↓ Descer',1]]){
                    const button=node('button',title);button.type='button';button.className='btn btn-outline-secondary';button.dataset.move=direction;
                    button.setAttribute('aria-label',`Mover parada ${i} para ${delta<0?'cima':'baixo'}`);button.disabled=i+delta<1||i+delta>=stops.length-1;
                    button.addEventListener('click',()=>moveStop(s.key,stops.indexOf(s)+delta,direction));tools.append(button);
                }
                const handle=node('button','↕ Arrastar');handle.type='button';handle.className='btn btn-outline-secondary';handle.draggable=true;handle.setAttribute('aria-label',`Arrastar parada ${i}; também use Subir e Descer`);
                handle.addEventListener('dragstart',event=>{if(publishing){event.preventDefault();return;}dragKey=s.key;event.dataTransfer.setData('text/plain',s.key);event.dataTransfer.effectAllowed='move';});
                handle.addEventListener('dragend',()=>{dragKey=null;});tools.append(handle);
                field.addEventListener('dragover',event=>{if(dragKey&&!publishing){event.preventDefault();event.dataTransfer.dropEffect='move';}});
                field.addEventListener('drop',event=>{if(!dragKey||publishing)return;event.preventDefault();moveStop(dragKey,stops.indexOf(s),'down');dragKey=null;});
                const remove=node('button','Remover parada');remove.type='button';remove.className='btn btn-outline-secondary';remove.addEventListener('click',()=>{stops.splice(stops.indexOf(s),1);render();orderStatus.textContent='Parada removida; gere nova prévia.';});tools.append(remove);field.append(tools);
            }
            list.append(field);
            if(valid(s))status.textContent=s.confirmed?'Ponto confirmado pelo operador; sem garantia de precisão cadastral.':'Ponto do planejamento anterior: confira e confirme antes de salvar nova versão.';
        });update();
    }
    map.on('click',event=>{if(publishing||!active)return;if(map._animatingZoom||map._panAnim?._inProgress){message.textContent='Aguarde o mapa parar antes de escolher o ponto.';return;}const point=event.originalEvent?map.mouseEventToLatLng(event.originalEvent):event.latlng;select(active,{latitude:point.lat,longitude:point.lng},'Map','Seleção visual do operador');active=null;form.querySelectorAll('[aria-pressed]').forEach(b=>b.setAttribute('aria-pressed','false'));message.textContent='Ponto marcado. Confira e confirme para incluí-lo no cálculo.';});
    add.addEventListener('click',()=>{if(stops.length>=10)return;stops.splice(stops.length-1,0,{address:'',confirmed:false});render();});
    function renderDraft(draft,fingerprint,changed=false){
        const coords=(draft.coordinates||[]).map(p=>[p.latitude,p.longitude]);
            const available=draft.available&&coords.length>=2&&coords.every(c=>Number.isFinite(c[0])&&Math.abs(c[0])<=90&&Number.isFinite(c[1])&&Math.abs(c[1])<=180);
            previewSummary.hidden=false;previewSummary.dataset.state=available?(changed?'changed':'ready'):'unavailable';
            if(!available){previewFingerprint=null;approvalId=null;previewSummary.textContent=`${draft.label||'Prévia indisponível'}. Nenhum planejamento foi publicado; a rota salva permanece inalterada.`;return;}
        if(previewLine){if(changed){if(supersededLine)map.removeLayer(supersededLine);supersededLine=previewLine;supersededLine.setStyle({color:'#6655aa',weight:3,dashArray:'2 8'});}else map.removeLayer(previewLine);}
        previewLine=L.polyline(coords,{color:'#ffb020',weight:6,dashArray:'8 10',opacity:1}).addTo(map);map.mglManualView=true;map.fitBounds(previewLine.getBounds(),{padding:[28,28],maxZoom:17,animate:false});
            const distance=Number.isFinite(draft.distanceKm)?draft.distanceKm.toLocaleString('pt-BR',{maximumFractionDigits:2})+' km':'distância indisponível';
            const duration=Number.isFinite(draft.durationSeconds)?Math.ceil(draft.durationSeconds/60)+' min do provedor':'duração indisponível';
            const before=Number.isFinite(context.currentDistanceKm)?context.currentDistanceKm.toLocaleString('pt-BR',{maximumFractionDigits:2})+' km':'sem rota salva';
            previewSummary.textContent=`Prévia não publicada · ${before} → ${distance} · ${duration} · ${stops.length-2} parada(s) · ${draft.provider}/${draft.profile}. Caminhão não validado. Confira o traçado antes de publicar.`;
            previewFingerprint=fingerprint;approvalId=draft.approvalId;approvalExpiry=Date.parse(draft.expiresAtUtc);previewLink.hidden=false;mapPreview.hidden=false;cancel.hidden=false;
        comparison.replaceChildren();comparison.hidden=false;
        for(const [label,oldValue,newValue,format,unit] of [['Distância',context.currentDistanceKm,draft.distanceKm,v=>v.toLocaleString('pt-BR',{maximumFractionDigits:3})+' km','km'],['Duração do provedor',context.currentDurationSeconds,draft.durationSeconds,v=>v.toLocaleString('pt-BR',{maximumFractionDigits:1})+' s','s'],['Paradas intermediárias',context.currentStopCount,draft.stopCount,v=>String(v),'']]){
            const item=node('section');item.append(node('h3',label));const known=Number.isFinite;
            item.append(node('p','Publicada: '+(known(oldValue)?format(oldValue):'Indisponível')),node('p','Proposta: '+(known(newValue)?format(newValue):'Indisponível')));
            const delta=known(oldValue)&&known(newValue)?newValue-oldValue:null;item.append(node('p','Diferença: '+(delta===null?'Não comparável':delta===0?'Sem alteração':(delta>0?'+':'')+delta.toLocaleString('pt-BR',{maximumSignificantDigits:4})+' '+unit)));comparison.append(item);
        }
        previewSummary.textContent+=` Base v${draft.baseRevision} · válida até ${new Date(approvalExpiry).toLocaleTimeString('pt-BR')}.`;
        if(changed){save.textContent='Aprovar nova proposta e publicar';message.textContent='O recálculo mudou a geometria, as métricas ou o provedor. Nada foi publicado. Compare o pontilhado com a nova proposta e aprove novamente.';if(draft.previous)previewSummary.textContent+=' Prévia anterior: '+draft.previous.distanceKm+' km / '+(draft.previous.durationSeconds??'duração indisponível')+' s.';}
    }
    cancel.addEventListener('click',()=>{resetPreview();update(false);message.textContent='Prévia cancelada. Pontos preservados; planejamento publicado inalterado.';});
    const expiryTimer=setInterval(()=>{if(approvalId&&!publishing&&Date.now()>=approvalExpiry){resetPreview();update(false);message.textContent='Prévia expirada. Gere novamente antes de publicar.';}},1000);
    previewButton.addEventListener('click',async()=>{
        if(previewButton.disabled)return;resetPreview();const ticket=generation,fingerprint=snapshot(),controller=new AbortController();
        previewAbort=controller;previewBusy=true;update(false);previewSummary.hidden=false;previewSummary.dataset.state='calculating';previewSummary.textContent='Calculando prévia sem publicar…';
        const body=new FormData(form);body.set('confirmedStops',fingerprint);
        try {
            const response=await fetch('/Roteamento/Previa',{method:'POST',body,credentials:'same-origin',signal:controller.signal});
            if(closed||ticket!==generation||snapshot()!==fingerprint)return;
            if(!response.ok||response.redirected){previewSummary.dataset.state='error';previewSummary.textContent=response.redirected?'Acesso alterado. Entre novamente.':await response.text();return;}
            const draft=await response.json();if(closed||ticket!==generation||snapshot()!==fingerprint)return;
            renderDraft(draft,fingerprint);
        }catch(error){if(error.name!=='AbortError'&&ticket===generation){previewSummary.dataset.state='error';previewSummary.textContent='Falha na prévia. A rota salva não foi alterada. Tente novamente.';}}
        finally{if(previewAbort===controller){previewAbort=null;previewBusy=false;update(false);}}
    });
    form.addEventListener('submit',async event=>{
        event.preventDefault();if(publishing||save.disabled)return;let navigating=false;publishing=true;save.disabled=true;message.textContent='Calculando e salvando planejamento confirmado…';
        const body=new FormData(form);body.set('confirmedStops',snapshot());body.set('approvalId',approvalId);form.inert=true;
        try{const response=await fetch(form.action,{method:'POST',body,credentials:'same-origin'});if(!response.ok){const text=await response.text();let reply;try{reply=JSON.parse(text);}catch{}if(reply?.status==='Changed'){renderDraft(reply,snapshot(),true);}else if(reply?.status==='Unavailable'){resetPreview();renderDraft(reply,snapshot());message.textContent='Provedor indisponível; nada foi publicado. Gere nova prévia.';}else{message.textContent=text;resetPreview();}return;}if(response.redirected){navigating=true;location.assign(response.url);}else throw new Error();}
        catch{message.textContent='Não foi possível confirmar o resultado. Atualize antes de tentar novamente.';resetPreview();}
        finally{if(!navigating){publishing=false;form.inert=false;update(false);}}
    });
    window.addEventListener('pagehide',()=>{closed=true;clearInterval(expiryTimer);resetPreview();stops.forEach(s=>s.abort?.abort());},{once:true});render();
})();

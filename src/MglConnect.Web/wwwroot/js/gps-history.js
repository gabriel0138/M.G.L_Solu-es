(() => {
    const initialized=new WeakSet();
    const qualities={adequada:'Adequada',aproximada:'Aproximada · sem navegação',baixa_precisao:'Baixa precisão · auditoria',precisao_desconhecida:'Precisão não informada'};
    function init(box) {
        if(initialized.has(box))return;initialized.add(box);
        const status=box.querySelector('[data-gps-history-status]'),mapBox=box.querySelector('[data-gps-history-map]'),
            tableBox=box.querySelector('[data-gps-history-table]'),prev=box.querySelector('[data-gps-history-prev]'),next=box.querySelector('[data-gps-history-next]'),page=box.querySelector('[data-gps-history-page]');
        let map,layer,running=false,stopped=false,current=1,lastFitPage=null;
        const node=(tag,text)=>{const element=document.createElement(tag);if(text!==undefined)element.textContent=text;return element;};
        const date=value=>value?new Date(value).toLocaleString('pt-BR'):'Não informada';
        async function load() {
            if(running||stopped||document.hidden)return;running=true;
            const abort=new AbortController(),timeout=setTimeout(()=>abort.abort(),10000);
            try {
                const url=new URL(box.dataset.endpoint||`/api/gps/historico/${Number(box.dataset.delivery)}`,location.origin);
                url.searchParams.set('pagina',current);if(box.dataset.periodStart)url.searchParams.set('de',box.dataset.periodStart);if(box.dataset.periodEnd)url.searchParams.set('ate',box.dataset.periodEnd);
                const response=await fetch(url,{credentials:'same-origin',cache:'no-store',signal:abort.signal});
                if(response.redirected||[401,403,404].includes(response.status)){
                    stopped=true;map?.remove();map=null;mapBox.replaceChildren();mapBox.hidden=true;tableBox.replaceChildren();tableBox.hidden=true;
                    status.textContent='O acesso ao histórico GPS não está disponível. Entre novamente ou confira a viagem.';return;
                }
                if(response.status===409){
                    map?.remove();map=null;mapBox.replaceChildren();mapBox.hidden=true;tableBox.replaceChildren();tableBox.hidden=true;
                    prev.hidden=true;next.hidden=true;page.textContent='';
                    status.textContent='Integridade do histórico não confirmada. Consulta suspensa; solicite revisão do arquivo à operação.';return;
                }
                if(!response.ok)throw new Error();
                const data=await response.json();if(!box.isConnected)return;
                const items=data.items.filter(p=>Number.isFinite(p.latitude)&&p.latitude>=-90&&p.latitude<=90&&Number.isFinite(p.longitude)&&p.longitude>=-180&&p.longitude<=180&&Number.isFinite(Date.parse(p.capturadoEmUtc??p.recebidoEmUtc)));
                current=data.pagina;prev.hidden=current<=1;next.hidden=current>=data.paginas;page.textContent=`Página ${current} de ${data.paginas} · ${data.total} posições confirmadas`;
                tableBox.replaceChildren();tableBox.hidden=items.length===0;
                if(!items.length){map?.remove();map=null;mapBox.replaceChildren();mapBox.hidden=true;status.textContent='Sem posições confirmadas neste período. Nenhuma posição é inferida do trajeto.';return;}
                const latest=items.at(-1),stale=Date.now()-Date.parse(latest.capturadoEmUtc??latest.recebidoEmUtc)>120000;
                const state=latest.origem==='Simulacao'?'SIMULAÇÃO · não é GPS real':latest.qualidade==='baixa_precisao'?'Leitura imprecisa · apenas auditoria':!data.ultimaPosicao?'Viagem aguardando GPS; histórico preservado':!latest.capturaInformada?'Horário de captura não informado':stale?'Posição desatualizada':'Posição recente';
                status.textContent=`${state} · captura: ${date(latest.capturadoEmUtc)} · recebimento: ${date(latest.recebidoEmUtc)}. ${items.length} pontos nesta página. A fila do aparelho não é conhecida por esta consulta.`;
                const plotted=items.filter(p=>p.adequadaMapa!==false);
                if(window.L&&plotted.length){
                    mapBox.hidden=false;if(!map){map=L.map(mapBox,{scrollWheelZoom:false});L.tileLayer(box.dataset.tileUrl||'https://tile.openstreetmap.org/{z}/{x}/{y}.png',{attribution:box.dataset.attribution||'© OpenStreetMap contributors',maxZoom:19}).addTo(map);layer=L.layerGroup().addTo(map);lastFitPage=null;}
                    layer.clearLayers();
                    const token=name=>getComputedStyle(document.documentElement).getPropertyValue(name).trim();
                    for(const point of plotted){
                        const simulation=point.origem==='Simulacao';const color=token(simulation||point.qualidade!=='adequada'?'--mgl-warning':'--mgl-color-link');
                        const label=node('span',`${simulation?'SIMULAÇÃO · ':''}Captura ${date(point.capturadoEmUtc)} · ${qualities[point.qualidade]||'GPS'} · ${point.precisaoMetros==null?'Precisão não informada':point.precisaoMetros+' m'}`);
                        L.circleMarker([point.latitude,point.longitude],{radius:point.qualidade==='aproximada'?8:5,color,fillOpacity:simulation?.15:.6,dashArray:simulation?'4 4':null}).bindPopup(label).addTo(layer);
                    }
                    map.invalidateSize();if(lastFitPage!==current){map.fitBounds(L.latLngBounds(plotted.map(p=>[p.latitude,p.longitude])),{padding:[25,25],maxZoom:16});lastFitPage=current;}
                }else{map?.remove();map=null;mapBox.replaceChildren();mapBox.hidden=true;}
                const table=node('table');table.className='table mb-0';const caption=node('caption','Capturas confirmadas; horários exibidos no fuso deste aparelho. Pontos de baixa precisão não são usados no mapa.');caption.className='visually-hidden';table.append(caption);
                const head=node('thead'),tr=node('tr');const columns=['Captura','Recebimento','Precisão / qualidade','Origem','Sequência / lacunas','Armazenamento'];
                if(box.dataset.admin==='true')columns.push('Motorista / veículo (cadastro atual)');
                for(const title of columns){const th=node('th',title);th.scope='col';tr.append(th);}head.append(tr);table.append(head);
                const body=node('tbody');for(const point of items){const row=node('tr');
                    row.append(node('td',date(point.capturadoEmUtc)),node('td',date(point.recebidoEmUtc)),node('td',`${point.precisaoMetros==null?'Não informada':point.precisaoMetros+' m'} · ${qualities[point.qualidade]||'Não classificada'}`),
                        node('td',point.origem==='Simulacao'?'SIMULAÇÃO':point.capturaInformada?'GPS do aparelho':'Legado · captura não informada'),
                        node('td',(point.foraDeOrdem===true?'Recebida fora de ordem. ':point.foraDeOrdem==null?'Ordem não classificada. ':'Em ordem de captura. ')+(point.lacunaAnteriorSegundos?`Intervalo de ${Math.round(point.lacunaAnteriorSegundos)} s sem amostras confirmadas; causa e percurso desconhecidos.`:'')),
                        node('td',point.arquivado?'Arquivo verificado':'Cópia online'));
                    if(box.dataset.admin==='true')row.append(node('td',`${point.motorista||'Motorista #'+point.motoristaId} · ${point.veiculo||''} ${point.placa||''}`));
                    body.append(row);
                }table.append(body);tableBox.append(table);
            }catch{status.textContent='Não foi possível atualizar o histórico. Confira período, conexão e integridade dos arquivos; a exibição pode estar desatualizada.';}
            finally{clearTimeout(timeout);running=false;}
        }
        prev.addEventListener('click',()=>{current=Math.max(1,current-1);load();});next.addEventListener('click',()=>{current++;load();});box.querySelector('[data-gps-history-reload]').addEventListener('click',load);
        const timer=setInterval(()=>{if(!box.isConnected){clearInterval(timer);map?.remove();return;}load();},20000);load();
    }
    function discover(){document.querySelectorAll('[data-gps-history]').forEach(init);}discover();new MutationObserver(discover).observe(document.body,{childList:true,subtree:true});
})();

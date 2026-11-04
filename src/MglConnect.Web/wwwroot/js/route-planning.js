(() => {
    const result=document.getElementById('routeResult'), form=document.getElementById('rotaForm');
    form.addEventListener('submit',()=>{
        result.dataset.routeState='Calculating';result.setAttribute('aria-busy','true');
        document.getElementById('routeStateLabel').textContent='Calculando rota…';
        document.getElementById('calculateRoute').disabled=true;
    });
    const data=JSON.parse(document.getElementById('routePresentation').textContent);
    const parameters=document.getElementById('routeParameters'),compact=matchMedia('(max-width:767px)');
    const adaptParameters=()=>{parameters.open=!data.hasGeometry || !compact.matches;};
    adaptParameters();compact.addEventListener('change',adaptParameters);
    const element=document.getElementById('truckMap'),status=document.getElementById('routeMapStatus');
    if(!window.L){status.textContent='Biblioteca de mapa indisponível. Os resultados continuam acima.';return;}
    const map=L.map(element,{zoomControl:true});
    const tiles=L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:19,attribution:'© OpenStreetMap contributors'});
    let success=0,failed=0;
    tiles.on('loading',()=>{success=0;failed=0;status.textContent='Carregando mapa…';element.dataset.tileState='loading';});
    const tileResult=()=>{status.textContent=failed?'Mapa de fundo incompleto; a geometria disponível foi preservada.':success?'Mapa carregado.':'Carregando mapa…';element.dataset.tileState=failed?'error':success?'loaded':'loading';};
    tiles.on('tileload',()=>{success++;tileResult();});tiles.on('tileerror',()=>{failed++;tileResult();});tiles.addTo(map);
    let bounds=null;
    if(data.hasGeometry){
        const coords=data.coordinates.map(c=>[c.Latitude,c.Longitude]);
        L.polyline(coords,{color:'#00c9b7',weight:5}).addTo(map);bounds=L.latLngBounds(coords);
        const stops=[coords[0],coords[coords.length-1]];
        stops.forEach((point,index)=>{const label=document.createElement('span');label.textContent=index?'Destino planejado':'Origem planejada';L.circleMarker(point,{radius:7,color:index?'#ffb020':'#00c9b7',fillOpacity:1}).bindTooltip(label).addTo(map);});
        (data.stops||[]).slice(1,-1).forEach(point=>{
            const label=document.createElement('span');label.textContent='Parada solicitada · não é GPS';
            L.circleMarker([point.Latitude,point.Longitude],{radius:6,color:'#ffb020',fillOpacity:1}).bindTooltip(label).addTo(map);
        });
        map.fitBounds(bounds,{padding:[24,24]});
    }
    // No fictional center: without geometry or GPS there are no tiles to request.
    const observer=new ResizeObserver(()=>{map.invalidateSize({pan:false});if(bounds)map.fitBounds(bounds,{padding:[24,24],animate:false});});observer.observe(element);
    window.addEventListener('pagehide',()=>{observer.disconnect();compact.removeEventListener('change',adaptParameters);map.remove();},{once:true});
})();

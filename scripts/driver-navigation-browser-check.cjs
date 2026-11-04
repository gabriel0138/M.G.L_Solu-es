// Invoked by the owned SQLite journey after a real driver starts a shift.
// Exercises real Leaflet, responsive layout and pointer hit testing in Chromium.
const assert = require('node:assert/strict');

async function checkDriverNavigation(client) {
    if(await client.evaluate("!!document.getElementById('demoRouteControls')"))return checkControlledDemo(client);
    const navigationPath = await client.evaluate('location.pathname');
    console.log('No-route initial map: '+JSON.stringify(await client.evaluate("({tiles:document.querySelectorAll('#cockpitMap .leaflet-tile').length,position:document.getElementById('positionSourceBadge').dataset.state,...document.getElementById('cockpitMap').dataset})")));
    for (const theme of ['dark', 'light']) {
        await client.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)}`);
        for (const width of [320, 360, 390, 428, 768, 1024, 1280, 1440]) {
            await client.viewport(width, width < 500 ? 844 : 1000);
            const layout = await client.evaluate(`(() => {
                const gps = document.getElementById('btnToggleGps');
                const r = gps.getBoundingClientRect();
                const top = document.querySelector('.cc-cockpit-top').getBoundingClientRect();
                const bottom = document.querySelector('.cc-cockpit-bottom').getBoundingClientRect();
                return {width:innerWidth, scroll:document.documentElement.scrollWidth,
                    gpsHit:!!document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('#btnToggleGps'),
                    gap:bottom.top-top.bottom, height:document.documentElement.scrollHeight,
                    viewport:innerHeight};
            })()`);
            assert.ok(layout.scroll <= layout.width, 'Driver horizontal overflow: '+JSON.stringify(layout));
            assert.ok(layout.gpsHit, 'GPS must receive pointer input: '+JSON.stringify(layout));
            assert.ok(layout.gap >= 95, 'Panels must reserve map/control space');
            assert.ok(layout.height <= layout.viewport+1, 'Collapsed portrait/desktop panels must fit');
            await client.screenshot('driver-fixed-'+theme+'-'+width);
        }
    }
    for (const [width,height] of [[844,390], [390,500]]) {
        await client.viewport(width,height);
        await client.evaluate(`document.querySelectorAll('.cc-driver-diagnostics, #driverGpsSync details').forEach(d=>d.open=true)`);
        for (const selector of ['#btnToggleGps', '.driver-emergency-button', '.driver-pod-button']) {
            const hit = await client.evaluate(`(() => {
                const button=document.querySelector(${JSON.stringify(selector)});
                button.scrollIntoView({block:'center',behavior:'instant'});
                const r=button.getBoundingClientRect();
                return !!document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest(${JSON.stringify(selector)});
            })()`);
            assert.ok(hit, 'Short viewport must keep action reachable: '+selector);
        }
        await client.screenshot('driver-fixed-short-'+width+'-'+height);
    }
    await client.evaluate(`document.querySelectorAll('.cc-driver-diagnostics, #driverGpsSync details').forEach(d=>d.open=false);scrollTo(0,0)`);
    await client.viewport(390,844);
    await client.command('Browser.grantPermissions',{origin:await client.evaluate('location.origin'),permissions:['geolocation']});
    await client.command('Emulation.setGeolocationOverride',{latitude:-23.5505,longitude:-46.6333,accuracy:8});
    const rect=await client.evaluate(`(()=>{const r=document.getElementById('btnToggleGps').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await client.command('Input.dispatchMouseEvent',{type:'mousePressed',...rect,button:'left',buttons:1,clickCount:1});
    await client.command('Input.dispatchMouseEvent',{type:'mouseReleased',...rect,button:'left',clickCount:1});
    for(let i=0;i<100;i++) {
        if(await client.evaluate(`document.getElementById('positionSourceBadge').dataset.state==='received'`))break;
        await client.pause(100);
    }
    assert.equal(await client.evaluate(`document.getElementById('btnToggleGps').getAttribute('aria-pressed')`),'true');
    assert.equal(await client.evaluate(`document.getElementById('positionSourceBadge').dataset.state`),'received');
    assert.ok(await client.evaluate(`(() => {
        const marker=document.querySelector('#cockpitMap .vehicle-marker-wrapper');
        if(!marker)return false;
        const r=marker.getBoundingClientRect();
        return !!document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('.vehicle-marker-wrapper');
    })()`),'The observed vehicle must remain visible between the panels');
    await client.screenshot('driver-fixed-gps-pointer-confirmed');
    await client.evaluate(`document.getElementById('btnToggleGps').click()`);

    // Explicit synthetic bend in a separate map: never presented as operational routing.
    await client.evaluate(`(() => {
        const panel=document.createElement('section');panel.id='geometryFixture';
        panel.style='position:fixed;inset:0;z-index:3000;background:#122033;color:white';
        panel.innerHTML='<h3>ENSAIO SINTÉTICO · geometria em curva</h3><p>Marcador: amostras simuladas; linha: fixture, sem homologação.</p><div id="fixtureMap" style="height:600px"></div>';
        document.body.append(panel);
        const map=L.map('fixtureMap');
        class GeometryFixtureTracker extends MglCockpitTracker {
            setupSignalR() {} // No delivery or server subscription belongs to this display fixture.
            setupTrafficLayerMatch() {}
        }
        window.geometryProbe=new GeometryFixtureTracker({map,coordinates:[[-23.5,-46.6],[-23.5,-46.59],[-23.49,-46.59]],navigationAllowed:false});
        const fix=(latitude,longitude)=>({latitude,longitude,accuracyMeters:5,timestamp:new Date().toISOString(),isSimulated:true});
        geometryProbe.applyTelemetryPosition(fix(-23.5,-46.6));
        geometryProbe.applyTelemetryPosition(fix(-23.49,-46.59));
    })()`);
    await client.pause(450);
    assert.deepEqual(await client.evaluate(`[geometryProbe.currentLatLng.lat,geometryProbe.currentLatLng.lng]`),[-23.49,-46.59]);
    await client.screenshot('driver-fixed-synthetic-bend');
    // Resize just the container, without a window resize; Leaflet must track it.
    await client.evaluate(`document.getElementById('fixtureMap').style.width='280px'`);
    await client.pause(150);
    assert.equal(await client.evaluate(`geometryProbe.map.getSize().x`),280);
    await client.evaluate(`geometryProbe.destroy();geometryProbe.map.remove();document.getElementById('geometryFixture').remove();delete window.geometryProbe`);
    await client.navigate('/Entregas/PerfilMotorista');
    assert.ok(await client.evaluate(`!!document.getElementById('driverProfileTitle')`));
    for(const width of [360,390]) {
        await client.viewport(width,844);
        assert.ok(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'));
        await client.screenshot('driver-profile-'+width);
    }
    await client.navigate(navigationPath);
    console.log('Driver priority checks passed: eight widths x two themes, short viewports/expanded GPS details, pointer GPS capture, synthetic bend and Leaflet container resize.');
}
module.exports = {checkDriverNavigation};

async function checkControlledDemo(client) {
    if(await client.evaluate("document.getElementById('demoRouteControls').dataset.routeAvailable==='false'"))return checkUnavailableDemo(client);
    for(let i=0;i<100;i++){
        if(await client.evaluate("!!window.mglControlledDemo?.tracker.routePolyline"))break;
        await client.pause(100);
    }
    assert.ok(await client.evaluate("window.mglControlledDemo.tracker.demoMode && !window.mglControlledDemo.tracker.navigationAllowed"));
    assert.ok(await client.evaluate("window.mglControlledDemo.tracker.routeCoordinates.length>=6"));
    for(const theme of ['dark','light']) {
        await client.evaluate(`document.documentElement.dataset.mglTheme=${JSON.stringify(theme)}`);
        for(const [width,height] of [[320,844],[360,844],[390,844],[428,844],[768,1000],[1024,1000],[844,390],[390,500]]){
            await client.viewport(width,height);
            assert.ok(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'));
            const map=await client.evaluate("(()=>{const r=document.getElementById('cockpitMap').getBoundingClientRect();return{width:r.width,height:r.height}})()");
            assert.ok(map.height>=220 && map.width>=280, 'Useful map size: '+JSON.stringify(map));
            for(const selector of ['#btnDemoGps','#btnDemoDetour','#btnFollowPosition','.driver-pod-button','.driver-emergency-button']){
                assert.ok(await client.evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center',behavior:'instant'});const r=e.getBoundingClientRect();return r.height>=44 && !!document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest(${JSON.stringify(selector)});})()`), 'Demo action receives pointer: '+selector);
            }
            await client.screenshot('delivery2-demo-'+theme+'-'+width+'x'+height);
        }
    }
    await client.viewport(390,844);
    await client.evaluate("document.getElementById('btnDemoGps').click()");
    for(let i=0;i<100;i++){if(await client.evaluate("document.getElementById('positionSourceBadge').dataset.state==='simulated'"))break;await client.pause(100);}
    await client.evaluate('window.mglControlledDemo.pause()');
    assert.equal(await client.evaluate("document.getElementById('positionSourceBadge').dataset.state"),'simulated');
    const initial=await client.evaluate("[window.mglControlledDemo.tracker.currentLatLng.lat,window.mglControlledDemo.tracker.currentLatLng.lng]");
    assert.deepEqual(initial,[-23.5505,-46.6333]);
    const response=await client.evaluate('window.mglControlledDemo.sendSample(3)');
    assert.equal(response.receipt.estado,'confirmado');
    assert.ok(await client.evaluate("Number(document.getElementById('tripProgressBar').getAttribute('aria-valuenow'))>0"));
    assert.notEqual(await client.evaluate("document.getElementById('etaMinutes').textContent"),'0 min estimados');
    await client.pause(200);
    await client.screenshot('delivery2-demo-progress');
    // Duplicate UUID/body cannot create a second row or change the accepted fix.
    const duplicate=await client.evaluate(`window.mglControlledDemo.sendSample(3,false,{eventId:${JSON.stringify(response.receipt.eventoId)},sample:3,detour:false,capturedAt:${JSON.stringify(response.receipt.capturadoEmUtc)}})`);
    assert.equal(duplicate.receipt.estado,'duplicado');
    // Off-route projection is display-only and never snaps the raw GPS coordinate.
    await client.evaluate("window.mglControlledDemo.tracker.updateRouteProgress(-23.5520,-46.6333)");
    assert.equal(await client.evaluate('window.mglControlledDemo.tracker.routeProjectionOffRoute'),true);
    await client.evaluate("window.mglControlledDemo.tracker.updateRouteProgress(-23.5493,-46.6327)");
    assert.equal(await client.evaluate('window.mglControlledDemo.tracker.routeProjectionOffRoute'),false);
    const reroute=await client.evaluate('window.mglControlledDemo.sendSample(0,true)');
    assert.equal(reroute.recalculated,true);
    assert.equal(await client.evaluate('window.mglControlledDemo.tracker.routeProjectionOffRoute'),false);
    await client.screenshot('delivery2-demo-recalculated');
    // Block only background tiles: neither route geometry nor the observed marker may disappear.
    await client.command('Network.enable');
    await client.command('Network.setBlockedURLs',{urls:['https://tile.openstreetmap.org/*']});
    await client.evaluate('void window.mglControlledDemo.tracker.map.setZoom(window.mglControlledDemo.tracker.map.getZoom()+1)');
    for(let i=0;i<100;i++){if(await client.evaluate("Number(document.getElementById('cockpitMap').dataset.tilesFailed)>0"))break;await client.pause(100);}
    assert.ok(await client.evaluate("Number(document.getElementById('cockpitMap').dataset.tilesFailed)>0 && !!window.mglControlledDemo.tracker.vehicleMarker && !!window.mglControlledDemo.tracker.routePolyline"));
    assert.ok(await client.evaluate("!document.getElementById('cockpitTileState').hidden && ['error','partial'].includes(document.getElementById('cockpitMap').dataset.tileState)"));
    console.log('Blocked tile diagnostics: '+JSON.stringify(await client.evaluate("({...document.getElementById('cockpitMap').dataset})")));
    await client.screenshot('delivery2-tiles-blocked');
    const loadedBefore=await client.evaluate("Number(document.getElementById('cockpitMap').dataset.tilesLoaded)");
    await client.command('Network.setBlockedURLs',{urls:[]});
    await client.evaluate('void window.mglControlledDemo.tracker.map.eachLayer(layer=>{if(layer instanceof L.TileLayer)layer.redraw()})');
    for(let i=0;i<100;i++){
        if(await client.evaluate(`Number(document.getElementById('cockpitMap').dataset.tilesLoaded)>${loadedBefore} && document.getElementById('cockpitMap').dataset.tileState==='loaded'`))break;
        await client.pause(100);
    }
    assert.ok(await client.evaluate(`Number(document.getElementById('cockpitMap').dataset.tilesLoaded)>${loadedBefore} && document.getElementById('cockpitTileState').hidden`));
    const path=await client.evaluate('location.pathname');
    await client.navigate(path);
    for(let i=0;i<100;i++){if(await client.evaluate("document.getElementById('positionSourceBadge').dataset.state==='simulated'"))break;await client.pause(100);}
    assert.deepEqual(await client.evaluate('[window.mglControlledDemo.tracker.currentLatLng.lat,window.mglControlledDemo.tracker.currentLatLng.lng]'),[-23.5520,-46.6333]);
    // Close the actual native WebSocket, then require automatic reconnect/rejoin
    // and a persisted snapshot to restore intentionally cleared display state.
    const previousConnection=await client.evaluate(`(()=>{
        const tracker=window.mglControlledDemo.tracker;
        const id=tracker.hubConnection.connectionId;
        const socket=window.__mglTestSockets.find(s=>s.readyState===WebSocket.OPEN&&s.url.includes('/telemetryHub'));
        if(!socket)throw new Error('Missing live native telemetry socket');
        tracker.currentLatLng=null;tracker.lastPositionTimestamp=null;
        socket.close();return id;
    })()`);
    for(let i=0;i<200;i++){
        if(await client.evaluate(`window.mglControlledDemo.tracker.hubConnection.connectionId && window.mglControlledDemo.tracker.hubConnection.connectionId!==${JSON.stringify(previousConnection)} && !!window.mglControlledDemo.tracker.currentLatLng && document.getElementById('telemetryConnectionState').dataset.state==='connected'`))break;
        await client.pause(100);
    }
    assert.notEqual(await client.evaluate('window.mglControlledDemo.tracker.hubConnection.connectionId'),previousConnection);
    assert.deepEqual(await client.evaluate('[window.mglControlledDemo.tracker.currentLatLng.lat,window.mglControlledDemo.tracker.currentLatLng.lng]'),[-23.5520,-46.6333]);
    await client.screenshot('delivery2-demo-reconnected');
    // Container-only resize must update the actual Leaflet dimensions.
    await client.evaluate("document.getElementById('cockpitMap').style.setProperty('width','280px','important')");await client.pause(150);
    assert.equal(await client.evaluate('window.mglControlledDemo.tracker.map.getSize().x'),280);
    await client.evaluate("document.getElementById('cockpitMap').style.removeProperty('width')");
    await client.evaluate('window.mglControlledDemo.sendSample(2,true)');
    await client.evaluate('window.mglControlledDemo.sendSample(4,true)');
    assert.equal(await client.evaluate("document.getElementById('tripProgressBar').getAttribute('aria-valuenow')"),'100');
    await client.screenshot('delivery2-demo-circuit-complete');
    console.log('Controlled demo passed: controlled route, eight viewports x two themes, synthetic GPS, progress, duplicate, deviation/recalculation, reload, native WebSocket reconnect and container resize. Tile diagnostics: '+JSON.stringify(await client.evaluate("({...document.getElementById('cockpitMap').dataset})")));
}

async function checkUnavailableDemo(client) {
    await client.viewport(390,844);await client.pause(500);
    assert.ok(await client.evaluate("document.getElementById('btnDemoGps').disabled"));
    assert.equal(await client.evaluate('window.mglControlledDemo.tracker.routeCoordinates.length'),0);
    assert.equal(await client.evaluate("document.getElementById('etaValue').textContent"),'--:--');
    await client.command('Browser.grantPermissions',{origin:await client.evaluate('location.origin'),permissions:['geolocation']});
    await client.command('Emulation.setGeolocationOverride',{latitude:-23.5505,longitude:-46.6333,accuracy:8});
    await client.evaluate("document.getElementById('btnToggleGps').click()");
    for(let i=0;i<100;i++){if(await client.evaluate("document.getElementById('positionSourceBadge').dataset.state==='received'"))break;await client.pause(100);}
    assert.equal(await client.evaluate("document.getElementById('positionSourceBadge').dataset.state"),'received');
    assert.equal(await client.evaluate("document.getElementById('distanceRemaining').textContent"),'—');
    assert.equal(await client.evaluate("document.getElementById('etaValue').textContent"),'--:--');
    await client.screenshot('delivery2-unavailable-position-preserved');
    console.log('Unavailable route passed: no fabricated geometry, distance or ETA; device position still received.');
}

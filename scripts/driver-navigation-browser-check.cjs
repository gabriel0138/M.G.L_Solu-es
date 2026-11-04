// Invoked by the owned SQLite journey after a real driver starts a shift.
// Exercises real Leaflet, responsive layout and pointer hit testing in Chromium.
const assert = require('node:assert/strict');

async function checkDriverNavigation(client) {
    const navigationPath = await client.evaluate('location.pathname');
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
        await client.evaluate(`document.querySelectorAll('#driverGpsSync details').forEach(d=>d.open=true)`);
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
    await client.evaluate(`document.querySelectorAll('#driverGpsSync details').forEach(d=>d.open=false);scrollTo(0,0)`);
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

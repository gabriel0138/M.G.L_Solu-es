const {test}=require('node:test');const assert=require('node:assert/strict');const vm=require('node:vm');const fs=require('node:fs');const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../MglConnect.Web/wwwroot/js/delivery-plan-updates.js'),'utf8');
function setup(initial={id:'one',revision:1,distance:1}) {
 const nodes=Object.fromEntries(['planningUpdateNotice','planningUpdateText','btnReviewRoute'].map(id=>[id,{dataset:{},hidden:true,textContent:'',addEventListener(){}}]));
 const requests=[],handlers={},events={};let interval;const observed={lat:-23.55,lng:-46.63},pending=[{id:'unsent-physical-fix'}],applied=[];
 const tracker={routeCoordinates:[[1,2],[3,4]],currentLatLng:observed,pending,demoMode:false,hubConnection:{on:(name,cb)=>handlers[name]=cb,onreconnected:cb=>handlers.reconnect=cb},applyPersistedPlan(plan,options){applied.push({plan,options});return true;},clearPlannedRoute(){this.cleared=true;},fitPlannedRoute(){throw Error('No unsolicited fit');}};
 const window={addEventListener:(n,cb)=>events[n]=cb,removeEventListener:n=>delete events[n]};
 const context={window,document:{getElementById:id=>nodes[id]},fetch:()=>new Promise(resolve=>requests.push(resolve)),setInterval:cb=>{interval=cb;return 1;},clearInterval(){},Date,Number};vm.runInNewContext(source,context);window.mglDeliveryPlanUpdates(tracker,7,initial);
 return {window,tracker,requests,handlers,events,nodes,applied,observed,pending,interval:()=>interval()};
}
const response=plan=>({ok:true,json:async()=>({plan})});const flush=()=>new Promise(resolve=>setImmediate(resolve));
test('out of order HTTP snapshots cannot replace a newer notified revision or move GPS/pending queues',async()=>{
 const e=setup();e.handlers.PlanningUpdated({entregaId:8,revision:9});assert.equal(e.requests.length,1);
 const update=e.handlers.PlanningUpdated({entregaId:7,revision:2});e.requests[1](response({planningId:'two',planRevision:2,rotaDisponivel:true,distanciaKm:2}));await update;
 e.requests[0](response({planningId:'one',planRevision:1,rotaDisponivel:true,distanciaKm:1}));await flush();
 assert.equal(e.window.mglPlanUpdates.revision,2);assert.equal(e.applied.length,1);assert.equal(e.applied[0].options.preserveView,true);assert.equal(e.tracker.currentLatLng,e.observed);assert.equal(e.tracker.pending,e.pending);assert.match(e.nodes.planningUpdateText.textContent,/v2/);
});
test('a trip without a persisted plan keeps its separately identified circuit on initial 404',async()=>{
 const e=setup({id:null,revision:0});e.requests[0]({ok:false,status:404});await flush();assert.equal(e.tracker.cleared,undefined);
});
test('access revocation clears the private route, and pagehide ignores late replies',async()=>{
 const e=setup();e.requests[0]({ok:false,status:404});await flush();assert.equal(e.tracker.cleared,true);assert.equal(e.nodes.btnReviewRoute.disabled,true);
 const update=e.handlers.PlanningUpdated({entregaId:7,revision:2});e.events.pagehide();e.requests[1](response({planningId:'two',planRevision:2,rotaDisponivel:true,distanciaKm:2}));await update;assert.equal(e.applied.length,0);
});

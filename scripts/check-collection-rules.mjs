import assert from 'node:assert/strict';
import {Campaign} from '../src/workers/campaign.ts';
import {Sandbox,distance} from '../src/workers/sim.ts';
const tick=(s,t)=>{for(let i=0;i<t*60;i++)s.tick(1/60);};
const until=(s,p,t=180)=>{for(let i=0;i<t*60&&!p();i++)s.tick(1/60);assert.ok(p(),JSON.stringify(s.workers[0]));};
const total=s=>Object.values(s.inventory).reduce((a,b)=>a+b,0)+s.sources.reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.amount??0),0);
{
 const s=new Campaign(false);assert.equal(s.enemies.length,0);assert.equal(s.recruit(),false);assert.equal(s.guide.step,0);
 const sum=total(s);s.work([1],101);until(s,()=>s.workers[0].phase==='work');s.work([1],103,true);s.finishWork([1]);assert.equal(s.workers[0].queue.length,0);until(s,()=>s.collectionStats.finished===1&&s.workers[0].phase==='idle');assert.equal(s.inventory.wood,6);assert.equal(s.workers[0].source,null);assert.equal(s.guide.action,'resumeCollection');assert.deepEqual(s.resumeWork([1]),[1]);assert.equal(s.guide.step,4);until(s,()=>!!s.workers[0].cargo);s.stop([1]);const cargo=structuredClone(s.workers[0].cargo);tick(s,2);assert.deepEqual(s.workers[0].cargo,cargo);assert.equal(s.guide.action,'deliverCollection');s.work([1],103);until(s,()=>s.inventory.ore>=6);assert.equal(s.inventory.wood,12);assert.equal(total(s),sum);
}
{
 const s=new Sandbox();s.work([1],101);until(s,()=>s.workers[0].phase==='unload');const progress=s.workers[0].progress;s.finishWork([1]);assert.equal(s.workers[0].progress,progress);until(s,()=>s.workers[0].phase==='idle');assert.equal(s.inventory.wood,6);assert.equal(s.collectionStats.finished,1);
 s.paused=true;const before=JSON.stringify(s);s.finishWork([1]);s.resumeWork([1]);s.retryRoute([1]);assert.equal(JSON.stringify(s),before);
}
class Barrier extends Sandbox{wall=false;overrideUnused(){} blocked(p,padding=.32){return super.blocked(p,padding)||(this.wall&&Math.abs(p.x+2)<.4+padding);}}
{
 const s=new Barrier();s.work([1],101);until(s,()=>!!s.workers[0].cargo);const sum=total(s);s.wall=true;s.transport([1]);until(s,()=>s.workers[0].phase==='blocked',30);assert.ok(s.workers[0].cargo);until(s,()=>s.workers[0].phase==='blocked'&&s.workers[0].routeRetries===3,60);assert.match(s.status(s.workers[0]),/请重试/);tick(s,10);assert.equal(s.workers[0].routeRetries,3);assert.equal(total(s),sum);s.wall=false;s.retryRoute([1]);until(s,()=>s.inventory.wood===6&&s.workers[0].phase==='idle');assert.equal(total(s),sum);
}
{
 const s=new Barrier();s.wall=true;s.work([1],101);until(s,()=>s.workers[0].phase==='blocked',30);s.wall=false;until(s,()=>s.inventory.wood>=6,90);
}
{
 const s=new Sandbox();const w=s.workers[0];s.move([1],{x:0,z:0});const goal={...w.goal};const path=s.route.bind(s);s.walk=()=>{};tick(s,11);assert.equal(w.phase,'blocked');assert.deepEqual(w.goal,goal);s.stop([1]);assert.equal(w.phase,'idle');assert.equal(w.goal,null);
 const route=path({x:-9,z:-2},{x:-2,z:-2});assert.ok(route.length>1);for(const p of route)assert.ok(!s.blocked(p,.27));
}
{
 const s=new Sandbox();s.work(s.workers.map(w=>w.id),101);tick(s,2);assert.ok(s.workers.some(w=>w.phase==='waitWork'));assert.ok(s.workers.every(w=>w.phase!=='blocked'));s.sources[0].stock=0;s.stop([1]);assert.deepEqual(s.resumeWork([1]),[]);
}
console.log('PASS collection: finish current trip, immediate stop, resume, cargo reassignment, conservation, blocked delivery preserves cargo, capped auto retry, obstacle removal recovery, stall detection, static obstacle routing, normal work-slot queue');
{
 // A blocked worker must not reserve a work slot or unload bay forever.
 const s=new Sandbox();s.work([1],101);until(s,()=>s.workers[0].phase==='approach');const w=s.workers[0];assert.notEqual(w.slot,null);s.walk=()=>{};tick(s,11);assert.equal(w.phase,'blocked');assert.equal(w.slot,null);assert.equal(w.bay,null);assert.ok(w.source);assert.match(s.workerAdvice(w),/释放/);
 s.stop([1]);assert.equal(w.routeIssue,undefined);assert.equal(w.routeRetries,0);
}
{
 // An obstructed unload bay is skipped in favor of another reachable bay.
 const s=new Sandbox();const base=s.blocked.bind(s);s.blocked=(p,padding=.32)=>base(p,padding)||distance(p,{x:-6,z:-.15})<.25+padding;
 s.work([1],105);s.finishWork([1]);until(s,()=>s.workers[0].phase==='idle');assert.equal(s.inventory.wood,6);assert.equal(s.collectionStats.finished,1);
}
{
 // No work slot is reachable: do not leave the unit waiting forever or teleport it.
 const s=new Sandbox();const base=s.blocked.bind(s);s.blocked=(p,padding=.32)=>base(p,padding)||(distance(p,s.sources[0])>1.1&&distance(p,s.sources[0])<2.15+padding);s.work([1],101);tick(s,.1);assert.equal(s.workers[0].phase,'blocked');assert.equal(s.workers[0].slot,null);assert.equal(s.sources[0].stock,144);s.stop([1]);assert.equal(s.workers[0].phase,'idle');
}
{
 const s=new Campaign(false);s.paused=true;assert.equal(s.toggleRoadblock(),false);s.paused=false;s.workers[0].x=-2;assert.equal(s.toggleRoadblock(),false);s.workers[0].x=-8.7;assert.ok(s.toggleRoadblock());assert.ok(s.blocked({x:-2,z:0}));assert.ok(s.toggleRoadblock());assert.equal(s.roadblock,false);
}
console.log('PASS recovery: release reserved work slots and unload bays, skip obstructed bay, inaccessible resource feedback, roadblock placement and pause');
{
 // Ending a blocked trip keeps the cargo, overrides queued work and still permits recovery.
 const s=new Barrier();s.work([1],101);until(s,()=>!!s.workers[0].cargo);s.wall=true;s.transport([1]);until(s,()=>s.workers[0].phase==='blocked');s.work([1],103,true);s.finishWork([1]);assert.equal(s.workers[0].queue.length,0);assert.ok(s.workers[0].finishAfterDelivery);s.wall=false;until(s,()=>s.workers[0].phase==='idle');assert.equal(s.collectionStats.finished,1);assert.equal(s.inventory.wood,6);assert.equal(s.inventory.ore,0);
}
{
 const s=new Sandbox();s.work(s.workers.map(w=>w.id),101);tick(s,2.1);const waiting=s.workers.filter(w=>w.phase==='waitWork').sort((a,b)=>a.queuedAt-b.queuedAt);assert.ok(waiting.length>=2);assert.match(s.workerAdvice(waiting[0]),/第 1 位/);assert.match(s.workerAdvice(waiting[1]),/第 2 位/);assert.match(s.workerAdvice(waiting[0]),/已等待 2 秒/);
}
console.log('PASS interrupted trip finish and truthful queue rank/wait duration');

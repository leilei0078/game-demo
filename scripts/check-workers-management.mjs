import assert from 'node:assert/strict';
import {Sandbox,workerState} from '../src/workers/sim.ts';
const until=(s,p)=>{for(let i=0;i<180*60&&!p();i++)s.tick(1/60);assert.ok(p(),'worker must complete request');};
{
 const s=new Sandbox();const id=s.addWorker(101);assert.ok(id);assert.equal(s.workers.find(w=>w.id===id).source,101);assert.equal(s.workers.filter(w=>w.source!==null).length,1);
 const removed=s.removeWorker(101);assert.equal(removed,id);until(s,()=>s.workers.find(w=>w.id===id).phase==='idle');
 assert.equal(s.workers.find(w=>w.id===id).source,null);
}
{
 const s=new Sandbox();s.work([1],101);until(s,()=>!!s.workers[0].cargo);assert.equal(s.removeWorker(101),1);assert.ok(s.workers[0].cargo);until(s,()=>s.workers[0].phase==='idle');assert.equal(s.inventory.wood,6);assert.equal(s.workers[0].source,null);
}
{
 const s=new Sandbox();s.work(s.workers.map(w=>w.id),101);s.tick(1/60);const waiting=s.workers.filter(w=>w.phase==='waitWork');assert.ok(waiting.length);const id=s.removeWorker(101);assert.ok(waiting.some(w=>w.id===id));
 s.paused=true;const before=JSON.stringify(s);assert.equal(s.addWorker(103),null);assert.equal(s.removeWorker(101),null);assert.equal(JSON.stringify(s),before);
}
{
 const s=new Sandbox();s.workers.forEach(w=>s.work([w.id],103));assert.equal(s.addWorker(101),null);assert.equal(s.removeWorker(999),null);
 const w=s.workers[0];w.phase='waitDepot';w.cargo={kind:'ore',amount:6};w.path=[{x:0,z:0}];assert.equal(workerState(w),'haul');w.path=[];assert.equal(workerState(w),'wait');
}
console.log('PASS: add idle worker, withdraw waiting first, withdraw cargo deposits, pause isolation, truthful wait state');

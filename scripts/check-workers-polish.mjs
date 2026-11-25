import assert from 'node:assert/strict';
import {Sandbox} from '../src/workers/sim.ts';
const ids=Array.from({length:12},(_,i)=>i+1);
{
 const s=new Sandbox();const result=s.assign(ids,'chop');assert.equal(result.length,12);
 const a=s.workers.filter(w=>w.source===101).length,b=s.workers.filter(w=>w.source===102).length;
 assert.ok(Math.abs(a-b)<=2,`balanced assignments ${a}/${b}`);
 const untouched=s.workers.slice(4).map(w=>w.source);s.assign([1,2,3,4],'mine');assert.ok(s.workers.slice(0,4).every(w=>w.source===103));assert.deepEqual(s.workers.slice(4).map(w=>w.source),untouched);
 s.move([1],{x:1,z:0});s.assign([1],'gather',true);assert.equal(s.workers[0].phase,'move');assert.equal(s.workers[0].queue[0].source,104);s.clearQueue([1]);assert.equal(s.workers[0].queue.length,0);assert.equal(s.workers[0].phase,'move');
 s.sources[0].stock=0;s.assign(ids,'chop');assert.ok(s.workers.every(w=>w.source===102));s.sources[1].stock=0;const before=JSON.stringify(s.workers);assert.deepEqual(s.assign(ids,'chop'),[]);assert.equal(JSON.stringify(s.workers),before);
 s.paused=true;assert.deepEqual(s.assign(ids,'mine'),[]);assert.equal(JSON.stringify(s.workers),before);
}
{
 const s=new Sandbox(),initial=s.sources.reduce((n,v)=>n+v.stock,0);s.assign(ids,'chop');let complete=false;
 for(let i=0;i<360*60;i++){
  s.tick(1/60);
  if(s.inventory.wood===252&&!s.workers.some(w=>w.cargo)){complete=true;break;}
 }
 assert.ok(complete,'both tree jobs must finish without stalled transport');
 assert.equal(Object.values(s.inventory).reduce((a,b)=>a+b,0)+s.sources.reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.amount??0),0),initial);
}
console.log('PASS: balanced quick jobs, independent reassignment, queued work, clear queue, depletion, pause, 12 workers across both trees');

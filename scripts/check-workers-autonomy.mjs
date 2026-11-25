import assert from 'node:assert/strict';
import {Sandbox,distance,RADIUS} from '../src/workers/sim.ts';
const ids=Array.from({length:12},(_,i)=>i+1);
const total=s=>Object.values(s.inventory).reduce((a,b)=>a+b,0)+s.sources.reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.amount??0),0);
function run(s,p,seconds=240){const initial=total(s);for(let i=0;i<seconds*60;i++){s.tick(1/60);assert.equal(total(s),initial);for(const w of s.workers){assert.ok(!s.blocked(w,.27));for(const o of s.workers)if(w!==o)assert.ok(distance(w,o)>=RADIUS*2+.02);}if(p())return;}assert.ok(p(),JSON.stringify(s.workers.map(w=>({id:w.id,phase:w.phase,source:w.source,cargo:w.cargo}))));}
{
 const s=new Sandbox();s.distribute(ids);const byJob={};for(const w of s.workers){const job=s.sources.find(v=>v.id===w.source).activity;byJob[job]=(byJob[job]??0)+1;}assert.deepEqual(byJob,{chop:4,mine:3,gather:3,pickup:2});run(s,()=>s.inventory.wood>=18&&s.inventory.ore>=6&&s.inventory.food>=6);
}
{
 const s=new Sandbox();const untouched=JSON.stringify(s.workers.slice(4));s.distribute([1,2,3,4]);assert.equal(JSON.stringify(s.workers.slice(4)),untouched);s.paused=true;const before=JSON.stringify(s);s.distribute(ids);s.setContinuation(ids,true);assert.equal(JSON.stringify(s),before);
}
{
 const s=new Sandbox();s.sources[0].stock=6;s.sources[1].stock=12;s.setContinuation([1],true);s.work([1],101);run(s,()=>s.workers[0].source===102);assert.equal(s.inventory.wood,6);run(s,()=>s.inventory.wood===18&&s.workers[0].phase==='idle');
}
{
 const s=new Sandbox();s.sources[0].stock=6;s.work([1],101);run(s,()=>s.inventory.wood===6&&s.workers[0].phase==='idle');assert.equal(s.sources[1].stock,108);
}
{
 const s=new Sandbox();s.sources[0].stock=6;s.setContinuation([1],true);s.work([1],101);s.work([1],103,true);run(s,()=>s.workers[0].source===103);assert.equal(s.sources[1].stock,108);assert.equal(s.inventory.wood,6);
}
{
 const s=new Sandbox();s.setContinuation([1],true);s.work([1],101);run(s,()=>!!s.workers[0].cargo);s.stop([1]);const before=JSON.stringify(s.workers[0]);for(let i=0;i<600;i++)s.tick(1/60);assert.equal(JSON.stringify(s.workers[0]),before);s.work([1],103);run(s,()=>s.inventory.ore>=6);assert.equal(s.inventory.wood,6);
}
{
 const s=new Sandbox();s.sources.forEach(v=>v.stock=0);assert.deepEqual(s.distribute(ids),[]);assert.ok(s.workers.every(w=>w.phase==='idle'));
}
{
 const s=new Sandbox();s.setContinuation(ids,true);s.distribute(ids);
 run(s,()=>s.workers.every(w=>w.phase==='idle')&&Object.values(s.inventory).reduce((a,b)=>a+b,0)===558,480);
 assert.deepEqual(s.inventory,{wood:324,ore:144,food:90});assert.ok(s.sources.every(v=>v.stock===0));assert.ok(s.workers.every(w=>!w.cargo&&w.slot===null&&w.bay===null));
}
console.log('PASS: auto job balance, selection isolation, continuation on/off, explicit queue priority, stop override, cargo preservation, pause, resource conservation, crowd separation');

import assert from 'node:assert/strict';
import { Sandbox, distance } from '../src/workers/sim.ts';
const step=(s,seconds)=>{for(let i=0;i<seconds*60;i++)s.tick(1/60);};
const until=(s,p,seconds=120)=>{for(let i=0;i<seconds*60&&!p();i++)s.tick(1/60);assert.ok(p(),'condition must be reached');};
const total=s=>Object.values(s.inventory).reduce((a,b)=>a+b,0)+s.sources.reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.amount??0),0);
{
 const s=new Sandbox(),other={...s.workers[1]};s.move([1],{x:4,z:0});until(s,()=>s.workers[0].phase==='idle');assert.ok(distance(s.workers[0],{x:4,z:0})<.4);assert.deepEqual(s.workers[1],other);assert.equal(s.inventory.wood,0);
}
for(const [source,kind] of [[101,'wood'],[103,'ore'],[104,'food'],[105,'wood']]){
 const s=new Sandbox(),initial=total(s);s.work([1],source);until(s,()=>!!s.workers[0].cargo);assert.equal(s.inventory[kind],0);assert.equal(s.workers[0].cargo.kind,kind);until(s,()=>s.inventory[kind]>0);assert.equal(s.inventory[kind],6);assert.equal(total(s),initial);assert.ok(s.workers[0].source===source);
}
{
 const s=new Sandbox();s.work([1],101);until(s,()=>!!s.workers[0].cargo);s.stop([1]);const p={...s.workers[0]};step(s,3);assert.equal(s.workers[0].x,p.x);assert.deepEqual(s.workers[0].cargo,p.cargo);s.work([1],103);until(s,()=>s.inventory.wood===6);until(s,()=>s.inventory.ore===6);assert.equal(s.inventory.wood,6);
}
{
 const s=new Sandbox(),initial=total(s);s.work([1,2,3,4],105);until(s,()=>s.inventory.wood===24);step(s,30);assert.equal(s.sources.find(x=>x.id===105).stock,0);assert.ok(s.workers.every(w=>w.phase==='idle'&&!w.cargo));assert.equal(total(s),initial);
}
{
 const s=new Sandbox();s.work([1],104);step(s,3);s.paused=true;const state=JSON.stringify(s);step(s,20);assert.equal(JSON.stringify(s),state);
}
console.log('PASS: move, independent commands, four jobs, delivery accounting, stop/reroute cargo, depletion, conservation, pause');

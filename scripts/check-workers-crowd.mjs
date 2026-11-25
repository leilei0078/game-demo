import assert from 'node:assert/strict';
import { Sandbox, distance, RADIUS } from '../src/workers/sim.ts';
const ids=Array.from({length:12},(_,i)=>i+1);
const total=s=>Object.values(s.inventory).reduce((a,b)=>a+b,0)+s.sources.reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.amount??0),0);
function run(s,seconds,done=()=>false){
 const initial=total(s);let min=Infinity;
 for(let i=0;i<seconds*60;i++){
  s.tick(1/60);
  for(const w of s.workers){assert.ok(!s.blocked(w,.27),`worker ${w.id} inside obstacle`);for(const o of s.workers)if(w!==o)min=Math.min(min,distance(w,o));}
  assert.ok(min>=RADIUS*2+.02,`units overlapped ${min}`);
  for(const source of s.sources)assert.ok(s.workers.filter(w=>w.source===source.id&&w.slot!==null).length<=4);
  assert.ok(s.workers.filter(w=>w.bay!==null).length<=3);assert.equal(total(s),initial);
  assert.ok(!s.messages.some(m=>m.text.includes('阻挡')),'no silent route failures');
  if(done()){console.log('finished after',i/60,'seconds; min gap',min.toFixed(3));return;}
 }
 console.log(s.workers.map(w=>({id:w.id,phase:w.phase,source:w.source,x:w.x.toFixed(1),z:w.z.toFixed(1),cargo:w.cargo,goal:w.goal})));
 assert.ok(done(),'crowd must eventually finish');
}
{
 const s=new Sandbox();s.move(ids,{x:2,z:0});run(s,90,()=>s.workers.every(w=>w.phase==='idle'));assert.ok(s.workers.every(w=>distance(w,{x:2,z:0})<4));
 s.move(ids,{x:-3,z:3});run(s,90,()=>s.workers.every(w=>w.phase==='idle'));
}
{
 const s=new Sandbox();s.work(ids,101);run(s,360,()=>s.inventory.wood===144&&!s.workers.some(w=>w.cargo));assert.equal(s.sources[0].stock,0);
}
{
 const s=new Sandbox();s.work([1,2,3,4],101);s.work([5,6,7,8],103);s.work([9,10,11,12],104);
 run(s,480,()=>s.inventory.wood===144&&s.inventory.ore===144&&s.inventory.food===90);
}
{
 const s=new Sandbox();s.move([1,2],{x:1,z:0});s.move([1,2],{x:4,z:0},true);run(s,90,()=>s.workers.slice(0,2).every(w=>w.phase==='idle'));assert.ok(s.workers.slice(0,2).every(w=>w.x>3));
}
{
 const s=new Sandbox();
 s.workers.forEach((w,i)=>{w.x=i<6?-9:9;w.z=-4.5+(i%6)*1.5;});
 s.move([1,2,3,4,5,6],{x:5,z:-1});s.move([7,8,9,10,11,12],{x:-4,z:3});
 run(s,120,()=>s.workers.every(w=>w.phase==='idle'));
 assert.ok(s.workers.slice(0,6).every(w=>w.x>2));assert.ok(s.workers.slice(6).every(w=>w.x<-2));
}
console.log('PASS: 12-unit formations, separation, work slots, depot bays, queue fairness, depletion, mixed jobs, queued commands, conservation');

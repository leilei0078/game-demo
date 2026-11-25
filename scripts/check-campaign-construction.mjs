import assert from 'node:assert/strict';import {Campaign} from '../src/workers/campaign.ts';
const tick=(s,n)=>{for(let i=0;i<n*60;i++)s.tick(1/60);};
const until=(s,p)=>{for(let i=0;i<120*60&&!p();i++)s.tick(1/60);assert.ok(p(),s.notice);};
{
 const s=new Campaign();s.inventory.wood=60;assert.ok(s.placement({x:-6,z:-2}));assert.equal(s.place('depot',{x:-6,z:-2},[1]),null);assert.equal(s.inventory.wood,60);
 const b=s.place('depot',{x:2.2,z:.2},[1,2]);assert.ok(b);assert.equal(b.x,2);assert.equal(b.z,0);assert.equal(s.inventory.wood,36);
 until(s,()=>b.progress>.15);s.stop([1,2]);const progress=b.progress;tick(s,3);assert.equal(b.progress,progress);
 s.paused=true;assert.equal(s.cancel(b.id),false);tick(s,2);assert.equal(b.progress,progress);s.paused=false;
 assert.ok(s.build([1,2],b.id));until(s,()=>b.complete);assert.equal(s.cancel(b.id),false);
 const other=s.place('barracks',{x:-2,z:-1},[3]);assert.ok(other,s.notice);until(s,()=>other.progress>.1);const refund=Math.floor(other.paid*(1-other.progress)),before=s.inventory.wood;assert.ok(s.cancel(other.id));assert.equal(s.inventory.wood,before+refund);assert.ok(!s.buildings.includes(other));assert.ok(!s.builders.has(3));
}
{
 const s=new Campaign();s.inventory.wood=24;s.work([1],101);until(s,()=>!!s.workers[0].cargo);const b=s.place('depot',{x:2,z:0},[1]);assert.ok(b,s.notice);assert.ok(s.builders.has(1));until(s,()=>b.complete);assert.equal(s.inventory.wood,6);
}
{
 const s=new Campaign();s.inventory.wood=24;const b=s.place('depot',{x:2,z:0},[1]);until(s,()=>b.progress>.1);s.assign([1],'mine');const progress=b.progress;tick(s,3);assert.equal(b.progress,progress);assert.ok(!s.builders.has(1));assert.equal(s.workers[0].source,103);
}
console.log('PASS: grid placement, obstacle rejection, cost, stop/resume construction, cancel/refund, pause, carried cargo delivered before building, reassignment');
{
 const s=new Campaign();assert.equal(s.recruit(),false);s.inventory.wood=36;const b=s.place('barracks',{x:2,z:0},[1,2]);until(s,()=>b.complete);assert.equal(s.recruit(),false);s.inventory.ore=32;s.inventory.food=48;for(let i=0;i<4;i++)assert.ok(s.recruit());assert.equal(s.recruit(),false);s.paused=true;const before=JSON.stringify(b.training);tick(s,8);assert.equal(JSON.stringify(b.training),before);s.paused=false;until(s,()=>s.recruited===4);assert.equal(s.inventory.ore,0);assert.equal(s.inventory.food,0);
}
{
 const s=new Campaign();s.commandFighters([201],{x:8,z:-5},true);until(s,()=>s.lost);assert.ok(!s.won);const before=JSON.stringify(s.fighters);tick(s,2);assert.equal(JSON.stringify(s.fighters),before);
}
console.log('PASS: recruit prerequisite, resource spending, queue capacity, pause, lord death and loss freeze');
{
 const s=new Campaign();s.inventory.wood=24;const b=s.place('depot',{x:2,z:0},[1]);until(s,()=>b.progress>.1);s.assign([1],'mine',true);assert.ok(s.builders.has(1));until(s,()=>b.complete&&s.workers[0].source===103);assert.equal(s.workers[0].queue.length,0);
}
console.log('PASS: appended work executes after construction completes');

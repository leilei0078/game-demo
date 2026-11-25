import assert from 'node:assert/strict';import {Campaign,RECRUIT, CAMP} from '../src/workers/campaign.ts';
const tick=(s,n)=>{for(let i=0;i<n*60;i++)s.tick(1/60);};
const until=(s,p)=>{for(let i=0;i<180*60&&!p();i++)s.tick(1/60);assert.ok(p(),s.notice);};
{
 const s=new Campaign();assert.equal(s.guide.action,'gather');s.distribute(s.workers.map(w=>w.id));until(s,()=>s.inventory.wood>=24);assert.equal(s.guide.action,'placeDepot');
 const b=s.place('depot',{x:2,z:0},[1,2]);assert.ok(b);assert.equal(s.guide.action,'wait');s.stop([1,2]);assert.equal(s.guide.action,'resume');s.build([1,2],b.id);until(s,()=>b.complete);assert.equal(s.guide.step,1);
 until(s,()=>s.inventory.wood>=36);let site=null;for(let z=-4;z<=4&&!site;z++)for(let x=-8;x<=7;x++)if(!s.placement({x,z})){site={x,z};break;}assert.ok(site);const barracks=s.place('barracks',site,[3,4]);assert.ok(barracks,s.notice);until(s,()=>barracks.complete);assert.equal(s.guide.step,2);
 until(s,()=>s.inventory.ore>=32&&s.inventory.food>=48);
 for(let i=0;i<4;i++)assert.ok(s.recruit());assert.equal(s.guide.action,'wait');
 const oldOre=s.inventory.ore,oldFood=s.inventory.food,spent={...s.spent};tick(s,1);const head=barracks.training[0];assert.ok(s.cancelTraining(barracks.id));assert.equal(s.inventory.ore,oldOre+8);assert.equal(s.inventory.food,oldFood+12);assert.equal(s.spent.ore,spent.ore-8);assert.equal(barracks.training[0],head);assert.equal(s.guide.action,'train');assert.ok(s.recruit());
 until(s,()=>s.livingGuards===4);assert.equal(s.guide.action,'attack');s.fighters.find(f=>f.role==='guard').hp=0;assert.equal(s.guide.step,2);assert.match(s.guide.title,/3\/4/);assert.ok(s.recruit());until(s,()=>s.livingGuards===4);
 s.commandFighters(s.fighters.map(f=>f.id),CAMP,true);until(s,()=>s.won||s.lost);assert.ok(s.won);
 const before=JSON.stringify({buildings:s.buildings,inventory:s.inventory,workers:s.workers,fighters:s.fighters,spent:s.spent});
 s.assign([1],'mine');s.stop([2]);s.move([1],{x:0,z:0});s.cancelTraining(barracks.id);s.cancel(b.id);s.stopFighters([201]);s.setContinuation([1],true);s.clearQueue([1]);s.recruit();tick(s,2);
 assert.equal(JSON.stringify({buildings:s.buildings,inventory:s.inventory,workers:s.workers,fighters:s.fighters,spent:s.spent}),before);
}
{
 const s=new Campaign();s.fighters.push({...s.fighters[0],id:202,role:'guard',x:-8,z:.5,hp:40,maxHp:110,path:[]});s.fighters[0].hp=150;tick(s,3);assert.ok(s.fighters[0].hp>150&&s.fighters[1].hp>40);
 s.retreat([201,202]);assert.ok(s.fighters.every(f=>!f.attack));s.paused=true;const before=JSON.stringify(s);s.cancelTraining(999);s.retreat([201]);tick(s,2);assert.equal(JSON.stringify(s),before);
}
console.log('PASS: contextual guide, stopped construction recovery, refund training preserves head progress, living troop target after casualties, retreat healing, terminal state freeze');

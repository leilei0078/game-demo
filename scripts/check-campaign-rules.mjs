import assert from 'node:assert/strict';
import {Campaign,CAMP} from '../src/workers/campaign.ts';
const s=new Campaign();
const until=(p,seconds=240)=>{for(let i=0;i<seconds*60&&!p();i++)s.tick(1/60);assert.ok(p(),JSON.stringify({notice:s.notice,inventory:s.inventory,buildings:s.buildings,builders:[...s.builders],workers:s.workers.map(w=>({id:w.id,phase:w.phase,source:w.source,x:w.x,z:w.z,cargo:w.cargo}))}));};
const conservation=()=>{for(const kind of ['wood','ore','food'])assert.equal(s.inventory[kind]+s.spent[kind]+s.sources.filter(v=>v.kind===kind).reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.kind===kind?w.cargo.amount:0),0),s.sources.filter(v=>v.kind===kind).reduce((n,v)=>n+v.initial,0));};
assert.equal(s.place('depot',{x:1,z:0},[1]),null);assert.equal(s.inventory.wood,0);assert.ok(s.placement({x:-6,z:-2}));
s.distribute(s.workers.map(w=>w.id));until(()=>s.inventory.wood>=60&&s.inventory.ore>=32&&s.inventory.food>=48);conservation();
let depotSite=null;for(let z=0;z>=-4&&!depotSite;z--)for(let x=2;x>=-8;x--)if(!s.placement({x,z})){depotSite={x,z};break;}
assert.ok(depotSite);const depot=s.place('depot',depotSite,[1,2]);assert.ok(depot,s.notice);assert.equal(depot.x,depotSite.x);assert.ok(s.blocked(depot));until(()=>depot.complete);conservation();
let position=null;for(let z=-4;z<=4&&!position;z++)for(let x=-3;x<=5;x++)if(!s.placement({x,z})){position={x,z};break;}
assert.ok(position);console.log('barracks position',position);const barracks=s.place('barracks',position,[3,4]);assert.ok(barracks,s.notice);until(()=>barracks.complete);conservation();
for(let i=0;i<4;i++)assert.ok(s.recruit(),s.notice);until(()=>s.recruited===4);conservation();
s.commandFighters(s.fighters.map(f=>f.id),CAMP,true);until(()=>s.won||s.lost,120);assert.ok(s.won,'army must win');assert.ok(!s.lost);console.log('PASS campaign loop',s.time,s.inventory,'new depot deliveries',s.deliveredAtNewDepot);assert.ok(s.deliveredAtNewDepot>0,'new depot actually receives goods');

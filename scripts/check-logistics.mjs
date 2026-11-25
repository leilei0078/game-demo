import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import {Sandbox,distance} from '../src/workers/sim.ts';
import {DEFAULT_LOGISTICS,batchSize,movementSpeed,gatherSeconds,validateLogistics} from '../src/workers/logistics.ts';
const params=(patch={})=>({...structuredClone(DEFAULT_LOGISTICS),...patch,weights:{...DEFAULT_LOGISTICS.weights,...patch.weights}});
const tick=(s,t)=>{for(let i=0;i<t*60;i++)s.tick(1/60);};
const until=(s,p,t=300)=>{for(let i=0;i<t*60&&!p();i++)s.tick(1/60);assert.ok(p(),JSON.stringify(s.workers.map(w=>({phase:w.phase,stock:s.sources[0].stock,cargo:w.cargo}))));};
const sum=s=>s.sources.reduce((n,v)=>n+v.stock,0)+s.workers.reduce((n,w)=>n+(w.cargo?.amount??0),0)+Object.values(s.inventory).reduce((n,v)=>n+v,0);
function fixture(config=params(),{kind='wood',job='chop',stock=24,x=1,count=1}={}){
 const s=new Sandbox();assert.equal(s.configureLogistics(config),'');s.workers=s.workers.slice(0,count);s.sources=[{id:101,x,z:2.5,radius:.65,name:'对照资源',kind,activity:job,stock,initial:stock}];return s;
}
const rows=[];
function run(name,config=params(),options={}){
 const s=fixture(config,options),start=sum(s),stats={work:0,haul:0,wait:0},stock=s.sources[0].stock;let maxWeight=0,maxAmount=0,trips=0;
 s.work(s.workers.map(w=>w.id),101);
 for(let i=0;i<900*60;i++){
  const last=s.collectionStats.delivered;
  for(const w of s.workers){if(w.phase==='work')stats.work+=1/60;if(['deliver','waitDepot','unload'].includes(w.phase))stats.haul+=1/60;if(w.phase==='waitWork'||w.phase==='blocked')stats.wait+=1/60;}
  s.tick(1/60);assert.equal(sum(s),start,'inventory + cargo + source conserves count');
  for(const w of s.workers){if(w.cargo){maxWeight=Math.max(maxWeight,s.cargoWeight(w));maxAmount=Math.max(maxAmount,w.cargo.amount);assert.ok(s.cargoWeight(w)<=config.maxWeight+1e-8);assert.ok(w.cargo.amount<=config.maxItems);}}
  if(s.collectionStats.delivered>last)trips++;
  if(s.collectionStats.delivered===stock)break;
 }
 assert.equal(s.collectionStats.delivered,stock);assert.ok(s.workers.every(w=>!w.cargo));
 const row={name,workers:s.workers.length,items:stock,trips,seconds:+s.time.toFixed(2),itemsPerMinute:+(stock/s.time*60).toFixed(2),maxAmount,maxWeight:+maxWeight.toFixed(2),workSeconds:+stats.work.toFixed(2),haulSeconds:+stats.haul.toFixed(2),waitWorkerSeconds:+stats.wait.toFixed(2)};rows.push(row);return row;
}
// Physical relations, including caps, partial batches, invalid input, no silent overloading.
assert.equal(batchSize(params(),'wood',100),6);assert.equal(batchSize(params({weights:{ore:6}}),'ore',100),3);assert.equal(batchSize(params(),'food',2),2);
assert.equal(movementSpeed(params(),0),2.9);assert.ok(movementSpeed(params(),3)>movementSpeed(params(),12));assert.equal(movementSpeed(params(),100),2.9*.7);
assert.equal(gatherSeconds(params(),'chop',6),2.2);assert.equal(gatherSeconds(params({gatherRate:2}),'chop',6),1.1);
for(const patch of [{maxItems:0},{maxItems:1.5},{maxWeight:0},{walkSpeed:0},{gatherRate:NaN},{gatherRate:Infinity},{weights:{ore:20}},{weights:{ore:-1}},{unloadPerItem:-1}]){
 const s=fixture(),before=s.tuning;assert.ok(s.configureLogistics(params(patch)));assert.deepEqual(s.tuning,before);
}
{
 const s=fixture(),p=s.tuning;p.weights.wood=99;assert.equal(s.tuning.weights.wood,2);s.paused=true;const before=JSON.stringify(s);assert.ok(s.configureLogistics(params({maxItems:3})));assert.equal(JSON.stringify(s),before);
}
const light=run('轻货：0.5 kg/件',params({weights:{wood:.5}}));
const normal=run('木材：2 kg/件');
const heavy=run('重货：6 kg/件',params({weights:{wood:6}}));
assert.equal(light.maxAmount,6);assert.equal(normal.maxAmount,6);assert.equal(heavy.maxAmount,3);assert.ok(light.seconds<normal.seconds);assert.ok(normal.seconds<heavy.seconds);
const small=run('每趟3件',params({maxItems:3}));assert.ok(small.trips>normal.trips);assert.ok(small.seconds>normal.seconds);assert.ok(Math.abs(small.workSeconds-normal.workSeconds)<.2,'same total quantity requires same gathering work');
const fastGather=run('采集效率2倍',params({gatherRate:2}));assert.ok(fastGather.workSeconds<normal.workSeconds*.55);assert.ok(fastGather.seconds<normal.seconds);assert.ok(fastGather.itemsPerMinute<normal.itemsPerMinute*2,'transport limits benefit of faster gathering');
const fastWalk=run('空载速度4 m/s',params({walkSpeed:4}));assert.ok(fastWalk.seconds<normal.seconds);assert.ok(Math.abs(fastWalk.workSeconds-normal.workSeconds)<.2);
const slowUnload=run('卸货0.5秒/件',params({unloadPerItem:.5}));assert.ok(slowUnload.seconds>normal.seconds);
const far=run('更远资源 x=7',params(),{x:7});assert.ok(far.seconds>normal.seconds);
const ore=run('矿石：3 kg/件',params(),{kind:'ore',job:'mine'});assert.ok(ore.workSeconds>normal.workSeconds);
const food=run('果实：0.5 kg/件',params(),{kind:'food',job:'gather'});assert.ok(food.workSeconds<normal.workSeconds);
run('余量不足一趟：5件',params({maxItems:3}),{stock:5});
const four=run('4名工人',params(),{count:4,stock:72});const eight=run('8名工人',params(),{count:8,stock:72});assert.ok(eight.itemsPerMinute<four.itemsPerMinute*2,'work slots, approach and unload congestion limit scaling');
// Factorial: vary weight, batch cap, gathering efficiency, movement independently.
for(const weight of [.5,6])for(const maxItems of [3,6])for(const gatherRate of [.5,2])for(const walkSpeed of [1.5,4])run(`组合 w${weight} n${maxItems} g${gatherRate} v${walkSpeed}`,params({weights:{wood:weight},maxItems,gatherRate,walkSpeed}));
{
 const s=fixture();s.work([1],101);until(s,()=>!!s.workers[0].batch);const w=s.workers[0],batch=structuredClone(w.batch),progress=w.progress;
 assert.equal(s.configureLogistics(params({maxItems:2,maxWeight:6,gatherRate:2,weights:{wood:1}})),'');assert.deepEqual(w.batch,batch);assert.equal(w.progress,progress);until(s,()=>!!w.cargo);assert.equal(w.cargo.amount,6);assert.equal(w.cargo.unitWeight,2);assert.equal(s.cargoWeight(w),12);assert.equal(s.travelSpeed(w),2.9*.7);s.stop([1]);const carried=structuredClone(w.cargo);assert.equal(s.configureLogistics(params({weights:{wood:5}})),'');assert.deepEqual(w.cargo,carried);assert.equal(s.cargoWeight(w),12);s.work([1],101);until(s,()=>s.inventory.wood===6);until(s,()=>!!w.cargo);assert.equal(w.cargo.amount,3);assert.equal(w.cargo.unitWeight,5);assert.equal(sum(s),24);
}
{
 const s=fixture();s.work([1],101);until(s,()=>s.workers[0].phase==='work'&&s.workers[0].progress>.3);s.stop([1]);assert.equal(s.sources[0].stock,24);assert.equal(s.workers[0].batch,undefined);assert.equal(sum(s),24);
}
await fs.mkdir('output/workers',{recursive:true});await fs.writeFile('output/workers/logistics-comparison.json',JSON.stringify({pass:true,default:DEFAULT_LOGISTICS,rows},null,2));
const md=['# 采集与运输对照测试','', '同一地图、同一起点、固定数量采到全部入库；时间包括寻路、避让、采集与卸货。多人统计为工人秒。','', '|场景|人数|入库件数|完成秒数|件/分|最大单趟件数|最大负重kg|','|---|---:|---:|---:|---:|---:|---:|',...rows.map(r=>`|${r.name}|${r.workers}|${r.items}|${r.seconds}|${r.itemsPerMinute}|${r.maxAmount}|${r.maxWeight}|`)];await fs.writeFile('output/workers/logistics-comparison.md',md.join('\n')+'\n');
console.table(rows.slice(0,14));console.log(`PASS ${rows.length} measured scenarios: weight/count/capacity, gather/move/unload/distance, crowd scaling, factorial combinations, conservation, invalid tuning, mid-batch/cargo preservation, cancellation`);
// Measure actual straight-line displacement with identical starting conditions.
const measured=[];
for(const amount of [0,1,3,6]){
 const s=fixture(),w=s.workers[0];s.sources=[];w.x=-4;w.z=4;s.move([1],{x:7,z:4});if(amount)w.cargo={kind:'wood',amount,unitWeight:2};const start={x:w.x,z:w.z};tick(s,1);const speed=distance(w,start);assert.ok(Math.abs(speed-s.travelSpeed(w))<.03,`actual speed ${speed}, cap ${s.travelSpeed(w)}`);measured.push(speed);
}
assert.ok(measured.every((v,i)=>!i||v<measured[i-1]));
{
 const s=fixture(params(),{count:4,stock:5});s.work([1,2,3,4],101);until(s,()=>s.workers.some(w=>w.batch));const reserved=s.workers.find(w=>w.batch);assert.equal(s.workers.reduce((n,w)=>n+(w.batch?.amount??0),0),5);s.stop([reserved.id]);assert.equal(sum(s),5);until(s,()=>s.collectionStats.delivered===5);assert.ok(s.workers.every(w=>!w.cargo));assert.equal(sum(s),5);
}
console.log('PASS actual displacement decreases with load; small shared stock reservations released on stop');
{
 const s=fixture();s.work([1],101);until(s,()=>s.workers[0].phase==='unload');const w=s.workers[0],duration=w.unloadDuration,progress=w.progress;s.configureLogistics(params({unloadPerItem:1}));assert.equal(w.unloadDuration,duration);assert.equal(w.progress,progress);until(s,()=>s.inventory.wood===6);until(s,()=>w.phase==='unload');assert.equal(w.unloadDuration,6.3);
}
console.log('PASS active unload duration preserved; next unload uses updated item handling time');

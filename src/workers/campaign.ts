import {Sandbox,BAYS,DEPOT,LIMIT,distance,type Point,type Worker,type Job} from './sim.ts';
export type BuildingKind='depot'|'barracks';
export type Building=Point&{id:number;kind:BuildingKind;progress:number;complete:boolean;paid:number;delivered:number;training:number[]};
export type Fighter=Point&{id:number;role:'lord'|'guard';hp:number;maxHp:number;path:Point[];goal:Point|null;attack:boolean;cooldown:number;face:number};
export const COST={depot:24,barracks:36};
export const RECRUIT={ore:8,food:12,time:4};
export const CAMP={x:8.4,z:-5.1};
export const footprint={halfX:1,halfZ:1};
export type GuideAction='gather'|'placeDepot'|'placeBarracks'|'resume'|'train'|'wait'|'attack'|'restart'|'finishCollection'|'resumeCollection'|'retryRoute'|'deliverCollection';
export type Guide={step:number;title:string;detail:string;action:GuideAction;label:string;building?:number};
export class Campaign extends Sandbox{
 buildings:Building[]=[];
 builders=new Map<number,number>();
 private builderSlots=new Map<number,number>();
 spent={wood:0,ore:0,food:0};
 fighters:Fighter[]=[{id:201,role:'lord',x:-8.6,z:.2,hp:260,maxHp:260,path:[],goal:null,attack:false,cooldown:0,face:0}];
 enemies=[{id:301,x:7.1,z:-4.6,hp:70,maxHp:70,cooldown:0},{id:302,x:9.3,z:-3.8,hp:70,maxHp:70,cooldown:0}];
 campHp=220;campMaxHp=220;won=false;lost=false;
 recruited=0; deliveredAtNewDepot=0;
 notice='';private nextBuilding=401;private nextFighter=202;
 readonly combatEnabled:boolean;
 roadblock=false;
 toggleRoadblock(){
  if(this.paused)return false;
  if(!this.roadblock&&[...this.workers,...this.fighters].some(w=>Math.abs(w.x+2)<.65)){this.notice='通道中有人，先移开再放置路障';return false;}
  this.roadblock=!this.roadblock;this.notice=this.roadblock?'练习路障已关闭通道：观察受阻提示，再移除路障并重试':'练习路障已移除：工人可等待自动重试，或手动重试路线';return true;
 }
 constructor(combatEnabled=true){super();this.combatEnabled=combatEnabled;if(!combatEnabled)this.enemies=[];}
 get ended(){return this.won||this.lost;}
 get livingGuards(){return this.fighters.filter(f=>f.role==='guard'&&f.hp>0).length;}
 get trainingCount(){return this.buildings.reduce((n,b)=>n+b.training.length,0);}
 get casualties(){return this.fighters.filter(f=>f.role==='guard'&&f.hp<=0).length;}
 snap(p:Point):Point{return{x:Math.round(p.x),z:Math.round(p.z)};}
 override blocked(p:Point,padding=.32){return super.blocked(p,padding)||(this.roadblock&&Math.abs(p.x+2)<.25+padding)||this.buildings.some(b=>Math.abs(p.x-b.x)<1+padding&&Math.abs(p.z-b.z)<1+padding);}
 override deliveryBays(){return [...BAYS,...this.buildings.filter(b=>b.complete&&b.kind==='depot').flatMap(b=>[-.65,.65].map(dx=>({x:b.x+dx,z:b.z+1.8})))];}
 placement(p:Point){
  const q=this.snap(p);
  if(Math.abs(q.x)>LIMIT.x-2||Math.abs(q.z)>LIMIT.z-2.3)return '离场地边界太近，需留出通行空间';
  if(this.combatEnabled&&distance(q,CAMP)<4)return '这里太靠近敌方营地';
  if(this.buildings.length>=4)return '本次试玩最多建造四座建筑';
  if(Math.abs(q.x-DEPOT.x)<3.1&&Math.abs(q.z-DEPOT.z)<2.6)return '与主仓库或卸货通道重叠';
  if(this.buildings.some(b=>Math.abs(q.x-b.x)<3&&Math.abs(q.z-b.z)<3.1))return '与其他建筑或通道重叠';
  if(this.sources.some(s=>s.stock>0&&Math.hypot(Math.max(0,Math.abs(q.x-s.x)-1.5),Math.max(0,Math.abs(q.z-s.z)-1.5))<s.radius+.4))return '占用资源或工作位置';
  if([...this.workers,...this.fighters.filter(f=>f.hp>0)].some(w=>Math.abs(w.x-q.x)<1.5&&Math.abs(w.z-q.z)<1.5))return '这里有人员，先让他们移开';
  
  return '';
 }
 place(kind:BuildingKind,p:Point,ids:number[]){
  if(!this.combatEnabled&&kind==='barracks'){this.notice='当前专注采集、运输与仓库建设';return null;}
  if(this.paused||this.won||this.lost){this.notice='当前无法建造';return null;}
  const workers=this.workers.filter(w=>ids.includes(w.id));if(!workers.length){this.notice='先选择工人负责施工';return null;}
  const error=this.placement(p);if(error){this.notice=error;return null;}
  if(this.inventory.wood<COST[kind]){this.notice=`木材不足，需要 ${COST[kind]}`;return null;}
  this.inventory.wood-=COST[kind];this.spent.wood+=COST[kind];
  const b:Building={id:this.nextBuilding++,...this.snap(p),kind,progress:0,complete:false,paid:COST[kind],delivered:0,training:[]};this.buildings.push(b);
  // The construction footprint affects navigation immediately. Replan paths that cross it.
  for(const w of this.workers)if(w.goal&&w.path.length){w.goal=this.free(w.goal);w.path=w.goal?this.route(w,w.goal):[];}
  for(const f of this.fighters)if(f.goal)f.path=this.route(f,f.goal);
  this.build(ids,b.id);this.notice='工地已放置，工人前往施工';return b;
 }
 private buildPoint(b:Building,slot:number){return{x:b.x+(slot%2?1:-1)*1.65,z:b.z+(slot<2?-1:1)*.55};}
 build(ids:number[],building:number){
  if(this.paused||this.won||this.lost)return false;
  const b=this.buildings.find(b=>b.id===building&&!b.complete);if(!b)return false;
  const busy=[...this.builders].filter(([,id])=>id===building).map(([id])=>id);
  let count=busy.length;
  for(const w of this.workers.filter(w=>ids.includes(w.id)&&!busy.includes(w.id))){
   if(count>=4)break;this.stop([w.id]);
   const occupied=[...this.builders].filter(([,id])=>id===b.id).map(([id])=>this.builderSlots.get(id));
   const slot=[0,1,2,3].find(i=>!occupied.includes(i))!;
   this.builders.set(w.id,b.id);this.builderSlots.set(w.id,slot);count++;
   if(w.cargo)super.transport([w.id]);else this.startBuilder(w,b);
  }
  return count>0;
 }
 private startBuilder(w:Worker,b:Building){
  this.go(w,this.buildPoint(b,this.builderSlots.get(w.id)??0),'buildApproach');
 }
 cancel(building:number){
  if(this.paused||this.ended)return false;
  const b=this.buildings.find(b=>b.id===building&&!b.complete);if(!b)return false;
  const refund=Math.floor(b.paid*(1-b.progress));this.inventory.wood+=refund;this.spent.wood-=refund;
  this.buildings=this.buildings.filter(o=>o!==b);
  for(const [id,target]of this.builders)if(target===b.id){this.builders.delete(id);this.builderSlots.delete(id);super.stop([id]);}
  this.notice=`已取消施工，返还木材 ${refund}`;return true;
 }
 private unassignBuilders(ids:number[]){if(!this.paused)for(const id of ids){this.builders.delete(id);this.builderSlots.delete(id);}}
 override stop(ids:number[]){if(this.ended)return;this.unassignBuilders(ids);super.stop(ids);}
 override move(ids:number[],point:Point,append=false){if(this.ended)return;if(!append)this.unassignBuilders(ids);super.move(ids,point,append);}
 override work(ids:number[],source:number,append=false){if(this.ended)return false;if(!append&&this.sources.some(s=>s.id===source&&s.stock>0))this.unassignBuilders(ids);return super.work(ids,source,append);}
 override assign(ids:number[],job:Job,append=false){if(this.ended)return [];if(!append&&this.sources.some(s=>s.activity===job&&s.stock>0))this.unassignBuilders(ids);return super.assign(ids,job,append);}
 override distribute(ids:number[],append=false){if(this.ended)return [];if(!append)this.unassignBuilders(ids);return super.distribute(ids,append);}
 override transport(ids:number[],append=false){if(this.ended)return;if(!append)this.unassignBuilders(ids);super.transport(ids,append);}
 override clearQueue(ids:number[]){if(!this.ended)super.clearQueue(ids);}
 override setContinuation(ids:number[],enabled:boolean){if(!this.ended)super.setContinuation(ids,enabled);}
 override status(w:Worker){return w.phase==='buildApproach'?'前往施工':w.phase==='build'?'施工':super.status(w);}
 recruit(building?:number){
  if(!this.combatEnabled)return false;
  if(this.paused||this.won||this.lost)return false;
  const b=this.buildings.find(b=>b.complete&&b.kind==='barracks'&&(!building||b.id===building));
  if(!b){this.notice='先建成兵营';return false;}
  if(this.fighters.filter(f=>f.hp>0&&f.role==='guard').length+this.buildings.reduce((n,b)=>n+b.training.length,0)>=8){this.notice='士兵上限 8 人';return false;}
  if(b.training.length>=4){this.notice='兵营训练队列已满';return false;}
  if(this.inventory.ore<RECRUIT.ore||this.inventory.food<RECRUIT.food){this.notice='招募需要矿石 8、果实 12';return false;}
  this.inventory.ore-=RECRUIT.ore;this.inventory.food-=RECRUIT.food;this.spent.ore+=RECRUIT.ore;this.spent.food+=RECRUIT.food;b.training.push(RECRUIT.time);this.notice='士兵已加入训练队列';return true;
 }
 cancelTraining(building:number){
  if(this.paused||this.ended)return false;
  const b=this.buildings.find(b=>b.id===building&&b.training.length);if(!b)return false;
  // Remove the last paid order, preserving progress on the first when more than one is queued.
  b.training.pop();this.inventory.ore+=RECRUIT.ore;this.inventory.food+=RECRUIT.food;this.spent.ore-=RECRUIT.ore;this.spent.food-=RECRUIT.food;
  this.notice='已取消最后一名训练，返还矿石 8、果实 12';return true;
 }
 retreat(ids:number[]){
  this.commandFighters(ids,{x:-8.7,z:.2});
  if(!this.paused&&!this.ended)this.notice='所选部队撤回主仓库附近，脱离交战后恢复生命';
 }
 commandFighters(ids:number[],p:Point,attack=false){
  if(!this.combatEnabled)attack=false;
  if(this.paused||this.won||this.lost)return;
  const fs=this.fighters.filter(f=>f.hp>0&&ids.includes(f.id));
  fs.forEach((f,i)=>{const destination=this.free({x:p.x+(i%3-1)*.8,z:p.z+Math.floor(i/3)*.85});if(destination){f.goal=destination;f.path=this.route(f,destination);f.attack=attack;}});
 }
 stopFighters(ids:number[]){if(!this.paused&&!this.ended)for(const f of this.fighters.filter(f=>ids.includes(f.id))){f.path=[];f.goal=null;f.attack=false;}}
 private spawn(b:Building){
  let point:Point|null=null;
  for(let r=1.7;r<4&&!point;r+=.5)for(let i=0;i<24;i++){
   const p={x:b.x+Math.cos(i*Math.PI/12)*r,z:b.z+Math.sin(i*Math.PI/12)*r};
   if(!this.blocked(p,.4)&&[...this.workers,...this.fighters.filter(f=>f.hp>0)].every(w=>distance(w,p)>.8)){point=p;break;}
  }
  if(!point)return false;
  this.fighters.push({id:this.nextFighter++,role:'guard',...point,hp:110,maxHp:110,path:[],goal:null,attack:false,cooldown:0,face:0});this.recruited++;return true;
 }
 override tick(dt:number){
  if(this.paused||this.won||this.lost)return;
  const delivering=this.workers.filter(w=>w.phase==='unload'&&w.cargo&&w.bay!==null&&w.bay>=3).map(w=>({w,amount:w.cargo!.amount,bay:w.bay!}));
  super.tick(dt);
  for(const {w,amount,bay}of delivering)if(!w.cargo){const depots=this.buildings.filter(b=>b.complete&&b.kind==='depot');const b=depots[Math.floor((bay-3)/2)];if(b){b.delivered+=amount;this.deliveredAtNewDepot+=amount;}}
  for(const [id,target]of this.builders){
   const w=this.workers.find(w=>w.id===id)!,b=this.buildings.find(b=>b.id===target);
   if(!b||b.complete){this.builders.delete(id);this.builderSlots.delete(id);this.finish(w,true);continue;}
   if(w.cargo)continue;
   if(w.phase==='idle'||w.phase==='move'){this.startBuilder(w,b);continue;}
   if(w.phase==='buildApproach'){
    if(w.path.length)this.walk(w,dt);
    else if(w.goal&&distance(w,w.goal)<.24){w.phase='build';w.goal=null;w.vx=0;w.vz=0;}
   }else if(w.phase==='build'){
    w.face=Math.atan2(b.x-w.x,b.z-w.z);b.progress=Math.min(1,b.progress+dt/12);w.progress=b.progress;
   }else if(!['waitDepot','deliver','unload','move'].includes(w.phase)){this.builders.delete(id);this.builderSlots.delete(id);}
  }
  for(const b of this.buildings){
   if(!b.complete&&b.progress>=1){b.complete=true;this.notice=`${b.kind==='depot'?'仓库':'兵营'}已建成`;}
   if(b.complete&&b.training.length){b.training[0]=Math.max(0,b.training[0]-dt);if(b.training[0]===0&&this.spawn(b))b.training.shift();}
  }
  for(const f of this.fighters.filter(f=>f.hp>0)){
   f.cooldown=Math.max(0,f.cooldown-dt);
   if(!f.attack&&distance(f,DEPOT)<4&&!this.enemies.some(e=>e.hp>0&&distance(e,f)<4))f.hp=Math.min(f.maxHp,f.hp+dt*6);
   const enemy=f.attack?this.enemies.filter(e=>e.hp>0).sort((a,b)=>distance(f,a)-distance(f,b))[0]:null;
   const target:Point|undefined=enemy??(f.attack&&this.campHp>0?CAMP:undefined);
   if(target&&distance(f,target)<1.65){
    f.face=Math.atan2(target.x-f.x,target.z-f.z);
    if(f.cooldown===0){if(enemy)enemy.hp=Math.max(0,enemy.hp-(f.role==='lord'?22:16));else this.campHp=Math.max(0,this.campHp-(f.role==='lord'?22:16));f.cooldown=.85;}
   }else{
    if(target&&(!f.path.length||!f.goal||distance(f.goal,target)>1.8)){const p=this.free({x:target.x-1,z:target.z+.7});if(p){f.goal=p;f.path=this.route(f,p);}}
    if(f.path.length){const p=f.path[0],d=distance(f,p),step=Math.min(d,dt*2.8);if(d>.001){f.face=Math.atan2(p.x-f.x,p.z-f.z);f.x+=(p.x-f.x)/d*step;f.z+=(p.z-f.z)/d*step;}if(d<=step+.001)f.path.shift();}
   }
  }
  for(const e of this.enemies.filter(e=>e.hp>0)){
   e.cooldown=Math.max(0,e.cooldown-dt);const f=this.fighters.filter(f=>f.hp>0&&distance(f,e)<4).sort((a,b)=>distance(a,e)-distance(b,e))[0];
   if(f&&e.cooldown===0){f.hp=Math.max(0,f.hp-28);e.cooldown=1.1;}
  }
  if(this.combatEnabled){
   if(this.campHp===0&&this.enemies.every(e=>e.hp===0)){this.won=true;this.notice='营地已清除，本次经营与战斗流程完成';}
   if(this.fighters[0].hp===0){this.lost=true;this.notice='领主倒下，点击重置重新开始';}
  }
 }
 private get collectionGuide():Guide{
  const stats=this.collectionStats;
  if(this.sources.every(s=>!s.stock)&&this.workers.every(w=>!w.cargo))return{step:4,title:'本区域资源已采集完毕',detail:'所有货物已入库，可检查工人和仓库；点重置重新练习采集调度。',action:'wait',label:'采集完成'};
  const blocked=this.workers.filter(w=>w.phase==='blocked');
  if(blocked.length)return{step:1,title:`${blocked.length} 名工人路线受阻`,detail:'任务和携货已保留。工人会尝试重新寻路；也可手动重试、改派资源或停止。工作位与卸货位排队会单独显示。',action:'retryRoute',label:'选中受阻人员并重试'};
  if(this.workers.some(w=>w.phase==='idle'&&w.cargo))return{step:1,title:'有工人停下后仍携带货物',detail:'立即停止会保留货物。先送回仓库，或重新指定采集任务（会先卸货再出发）。',action:'deliverCollection',label:'让携货待命者送回仓库'};
  if(!stats.orders)return{step:0,title:'先安排一组工人伐木',detail:'选人 → 指定资源。点下方伐木，或直接点击树木；工人会自动采集、运送、卸货并返回。',action:'gather',label:'安排四人伐木'};
  if(!this.workers.some(w=>w.source||w.cargo)&&!stats.finished)return{step:0,title:'采集已停止，可以重新派工',detail:'可选择待命工人恢复原采集，或选其他资源开始；停止不会扣除尚未采得的资源。',action:'gather',label:'安排四人伐木'};
  if(!stats.delivered)return{step:1,title:'等待第一趟资源入库',detail:'采集完成后自动背货送仓库。背包里的货物尚未入库；可选中工人查看当前任务和携货。',action:'wait',label:'采集与运输中'};
  if(!stats.finished){const ending=this.workers.some(w=>w.finishAfterDelivery);return{step:2,title:ending?'工人正在结束本轮采集':'试着结束这一轮采集',detail:'“本轮后结束”会采完这一份并入库，再待命；“立即停止”原地停下，保留已携带的货物，并清空后续任务。',action:ending?'wait':'finishCollection',label:ending?'等待本轮货物入库':'让一名工人本轮后结束'};}
  if(!stats.resumed)return{step:3,title:'重新派出刚结束的工人',detail:'选中待命人员，点“恢复原采集”；也可选择其他资源改派。携货改派会先入库，货物不会丢失。',action:'resumeCollection',label:'恢复一名工人的原采集'};
  return{step:4,title:'现在可以自由调度采集',detail:'选人或框选 → 点击资源 → 自动往返。随时结束本轮、立即停止、恢复或改派；资源采空后默认待命，可开启同类续采。',action:'wait',label:'采集引导已完成'};
 }
 get guide():Guide{
  if(!this.combatEnabled)return this.collectionGuide;
  if(this.ended)return{step:4,title:this.won?'营地已清除':'领主倒下',detail:this.won?'经营与战斗流程完成，可以重新挑战。':'先积累四名士兵再出兵，领主负伤时及时撤回。',action:'restart',label:'重新开始'};
  for(const [index,kind]of (['depot','barracks'] as const).entries()){
   if(this.buildings.some(b=>b.kind===kind&&b.complete))continue;
   const b=this.buildings.find(b=>b.kind===kind&&!b.complete),name=kind==='depot'?'前哨仓库':'兵营';
   if(b){const count=[...this.builders.values()].filter(id=>id===b.id).length;
    return{step:index,title:`${name}施工 ${Math.floor(b.progress*100)}%`,detail:count?`${count} 名工人已接施工任务；携货者先卸货，再施工。`:'工地目前无人施工，派两名工人继续；已完成进度会保留。',action:count?'wait':'resume',label:count?'工人施工中':'派两人继续',building:b.id};}
   const shortage=Math.max(0,COST[kind]-this.inventory.wood);
   return{step:index,title:shortage?`为${name}准备木材`:`放置${name}`,detail:shortage?`已入库 ${this.inventory.wood}/${COST[kind]} 木材，还差 ${shortage}；背货入库后才可使用。`:`花费 ${COST[kind]} 木材。选择空地放置，自动安排两名工人施工。`,action:shortage?'gather':kind==='depot'?'placeDepot':'placeBarracks',label:shortage?'安排空闲工人采集':`选择${name}位置`};
  }
  const ready=this.livingGuards,queued=this.trainingCount;
  if(ready<4){
   if(ready+queued>=4)return{step:2,title:`等待部队集结 ${ready}/4`,detail:`训练中 ${queued} 人，每人 4 秒；可继续安排工人采集。`,action:'wait',label:'兵营训练中'};
   const ore=Math.max(0,RECRUIT.ore-this.inventory.ore),food=Math.max(0,RECRUIT.food-this.inventory.food);
   return{step:2,title:`补齐出征部队 ${ready}/4`,detail:ore||food?`下一名士兵还缺${ore?`矿石 ${ore}`:''}${ore&&food?'、':''}${food?`果实 ${food}`:''}。已训练 ${queued} 人。`:`每名消耗 8 矿石、12 果实；已有 ${queued} 人训练中。`,action:ore||food?'gather':'train',label:ore||food?'安排空闲工人采集':'训练一名士兵'};
  }
  const attacking=this.fighters.some(f=>f.hp>0&&f.attack);
  return{step:3,title:attacking?'部队正在进攻':'部队已就绪',detail:attacking?`守卫剩余 ${this.enemies.filter(e=>e.hp>0).length} 人 · 营地 ${this.campHp}/${this.campMaxHp}。负伤可撤回主仓库休整。`:`领主与 ${ready} 名士兵已待命。进攻前可检查血量，主仓库附近可休整。`,action:attacking?'wait':'attack',label:attacking?'交战中':'集结并进攻'};
 }
 get objective(){return this.guide.title;}
}

import './style.css';
import {DEFAULT_LOGISTICS} from './logistics';
import {Campaign,CAMP,COST,RECRUIT,type BuildingKind} from './campaign';
import * as THREE from 'three';
import { DEPOT, LIMIT, KIND, ACTIVITY, BAYS, workSpot, workerState, type Point, type Job, type Order } from './sim';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>('field');
const sim = new Campaign(false);
const selectedFighters=new Set<number>();
let buildMode:BuildingKind|null=null;
let buildPosition:Point|null=null;
const selected = new Set([1]);
const boxPreview=new Set<number>();
const stateNames={idle:'空闲',move:'移动',work:'工作',haul:'运输',wait:'等待'};
const stateColors={idle:'#8a9a9e',move:'#388ec2',work:'#5c9867',haul:'#be8b38',wait:'#b07757'};
const groupMaterials=['#3985b5','#65947a','#9575ab'].map(c=>new THREE.MeshStandardMaterial({color:c,roughness:.9}));
const groups = new Map<number,number[]>([[1,[1,2,3,4]],[2,[5,6,7,8]],[3,[9,10,11,12]]]);
$('roster').innerHTML=sim.workers.map(w=>`<button data-unit="${w.id}" aria-label="${w.id} 号"><b>${w.id}</b><span>待命</span></button>`).join('');
let mode: 'move' | 'work' | null = null;
let inspectedSource:number|null=null;
let inspectMode=false;
let renderer: THREE.WebGLRenderer;
try { renderer = new THREE.WebGLRenderer({ canvas, antialias: true }); }
catch { $('toast').textContent = '无法启动 WebGL，请使用支持 WebGL 2 的浏览器。'; $('toast').classList.add('visible'); throw new Error('WebGL unavailable'); }
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.setClearColor('#dce3e5');
const scene = new THREE.Scene();
const camera = new THREE.OrthographicCamera(-15, 15, 10, -10, .1, 100);
camera.position.set(0, 23, 18); camera.lookAt(0, 0, 0);
let zoom=1;
let panMode=false;
const viewCenter={x:0,z:0};
function updateCamera(){camera.position.set(viewCenter.x,23,18+viewCenter.z);camera.lookAt(viewCenter.x,0,viewCenter.z);camera.zoom=zoom;camera.updateProjectionMatrix();camera.updateMatrixWorld();}
scene.add(new THREE.HemisphereLight('#ffffff', '#a6b6ac', 2.4));
const sun = new THREE.DirectionalLight('#fff8e7', 3.1);
sun.position.set(-8, 16, 8); sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
Object.assign(sun.shadow.camera, { left: -15, right: 15, top: 12, bottom: -12, near: .1, far: 60 });
sun.shadow.normalBias = .035; scene.add(sun);
const mat = (c: string) => new THREE.MeshStandardMaterial({ color: c, roughness: .9 });
const blue = mat('#3985b5'), darkBlue = mat('#24587c'), skin = mat('#f1cea2'), brown = mat('#996a43'), green = mat('#72a878'), rock = mat('#869398'), crate = mat('#c89553'), white = mat('#e5ece5');
function mesh(geometry: THREE.BufferGeometry, material: THREE.Material, parent: THREE.Object3D, x = 0, y = 0, z = 0) {
  const m = new THREE.Mesh(geometry, material); m.position.set(x, y, z); m.castShadow = true; m.receiveShadow = true; parent.add(m); return m;
}
function box(parent: THREE.Object3D, material: THREE.Material, x: number, y: number, z: number, a: number, b: number, c: number) { return mesh(new THREE.BoxGeometry(a,b,c), material, parent,x,y,z); }
const ground = mesh(new THREE.BoxGeometry(22,.18,14), mat('#c7d4bb'), scene,0,-.1,0);
ground.castShadow = false;
const gridPoints: THREE.Vector3[]=[];
for(let x=-11;x<=11;x++)gridPoints.push(new THREE.Vector3(x,.004,-7),new THREE.Vector3(x,.004,7));
for(let z=-7;z<=7;z++)gridPoints.push(new THREE.Vector3(-11,.004,z),new THREE.Vector3(11,.004,z));
scene.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(gridPoints),new THREE.LineBasicMaterial({color:'#acbfa4'})));
const border = new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints([[-10.5,-6.5],[10.5,-6.5],[10.5,6.5],[-10.5,6.5]].map(([x,z])=>new THREE.Vector3(x,.025,z))), new THREE.LineBasicMaterial({ color:'#819b83' })); scene.add(border);
const depot = new THREE.Group(); depot.position.set(DEPOT.x,0,DEPOT.z); scene.add(depot);
box(depot,mat('#a5b8c0'),0,.12,0,3,.24,2.1);
box(depot,white,0,.45,-.9,3,.65,.14);
box(depot,white,-1.43,.45,0,.14,.65,1.8);
box(depot,white,1.43,.45,0,.14,.65,1.8);
for (let i=0;i<3;i++) box(depot,crate,-.8+i*.8,.48,-.3,.6,.55,.6);
function label(className: string) { const el=document.createElement('div'); el.className=`label ${className}`; $('labels').append(el); return el; }
const slotMaterial=new THREE.MeshBasicMaterial({color:'#a8b69d',side:THREE.DoubleSide,transparent:true,opacity:.6});
function slotRing(p:Point){const m=mesh(new THREE.RingGeometry(.31,.36,24),slotMaterial.clone(),scene,p.x,.02,p.z);m.rotation.x=-Math.PI/2;return m;}
const bayRings=BAYS.map(slotRing);
const slotRings=sim.sources.map(s=>Array.from({length:4},(_,i)=>slotRing(workSpot(s,i))));
const depotLabel=label('source-label'); depotLabel.innerHTML='仓库<small>卸货后入库</small>';
const sourceViews=sim.sources.map(s=>{
  const group=new THREE.Group(); group.position.set(s.x,0,s.z); scene.add(group);
  const parts: THREE.Object3D[]=[];
  if(s.activity==='chop') {
    mesh(new THREE.CylinderGeometry(.19,.26,1.3,8),brown,group,0,.65,0);
    parts.push(mesh(new THREE.ConeGeometry(.87,1.55,7),green,group,0,1.65,0));
    parts.push(mesh(new THREE.ConeGeometry(.66,1.25,7),green,group,0,2.35,0));
  } else if(s.activity==='mine') {
    for(let i=0;i<3;i++) { const m=mesh(new THREE.DodecahedronGeometry(.56,0),rock,group,(i-1)*.44,.4,i===1?-.2:.12); m.scale.set(1,1.15,1); parts.push(m); }
  } else if(s.activity==='gather') {
    parts.push(mesh(new THREE.IcosahedronGeometry(.65,1),green,group,0,.48,0));
    for(let i=0;i<5;i++) parts.push(mesh(new THREE.SphereGeometry(.10,6,4),mat('#bf6b57'),group,Math.cos(i*1.3)*.46,.65+Math.sin(i)*.17,Math.sin(i*1.3)*.4));
  } else {
    for(let i=0;i<4;i++) {const x=(i%2-.5)*.6,z=(Math.floor(i/2)-.5)*.6; const c=box(group,crate,x,.28,z,.5,.55,.5); parts.push(c); box(c,brown,0,0,0,.54,.10,.54);}
  }
  const hitHeight=s.activity==='chop'?2.9:s.activity==='mine'?1.2:1;
  const hit=mesh(new THREE.CylinderGeometry(s.radius+.12,s.radius+.12,hitHeight,10),new THREE.MeshBasicMaterial({visible:false}),group,0,hitHeight/2,0);
  hit.userData.source=s.id;
  const el=label('source-label');
  return {s,group,parts,hit,el};
});
const workerViews=sim.workers.map(w=>{
  const group=new THREE.Group(); scene.add(group);
  const body=new THREE.Group(); group.add(body);
  const torso=mesh(new THREE.CylinderGeometry(.22,.27,.58,8),groupMaterials[Math.floor((w.id-1)/4)],body,0,.65,0);
  const head=mesh(new THREE.BoxGeometry(.33,.33,.33),skin,body,0,1.13,0);
  box(head,darkBlue,0,.16,0,.39,.12,.39);
  box(head,darkBlue,0,.04,.169,.15,.045,.015);
  const legs=[box(body,darkBlue,-.12,.18,0,.14,.36,.18),box(body,darkBlue,.12,.18,0,.14,.36,.18)];
  const arm=new THREE.Group(); arm.position.set(.3,.84,0); body.add(arm);
  box(arm,blue,0,-.15,0,.12,.35,.14);
  const tool=new THREE.Group(); arm.add(tool);
  box(tool,brown,0,-.18,.24,.055,.055,.7);
  box(tool,rock,0,-.18,.53,.25,.09,.14);
  const cargo=box(body,crate,0,.69,-.32,.49,.48,.32); cargo.visible=false;
  const ring=mesh(new THREE.RingGeometry(.39,.45,32),new THREE.MeshBasicMaterial({color:'#268bc6',side:THREE.DoubleSide}),group,0,.018,0); ring.rotation.x=-Math.PI/2;
  const path=new THREE.Line(new THREE.BufferGeometry(),new THREE.LineDashedMaterial({color:'#4794b7',dashSize:.16,gapSize:.13,transparent:true,opacity:.55})); scene.add(path);
  const destination=slotRing(w);destination.visible=false;
  const queuePath=new THREE.Line(new THREE.BufferGeometry(),new THREE.LineDashedMaterial({color:'#aa8b51',dashSize:.12,gapSize:.16,transparent:true,opacity:.65}));scene.add(queuePath);
  const el=label('worker-label');
  return {w,group,body,torso,head,legs,arm,tool,cargo,ring,path,destination,queuePath,el};
});
const marker=mesh(new THREE.RingGeometry(.25,.33,24),new THREE.MeshBasicMaterial({color:'#247eae',side:THREE.DoubleSide,transparent:true}),scene); marker.rotation.x=-Math.PI/2; marker.visible=false;
let markerLife=0, toastLife=0;
function commandFeedback(p:Point,text:string,color='#247eae'){
  marker.position.set(p.x,.03,p.z);(marker.material as THREE.MeshBasicMaterial).color.set(color);markerLife=1;marker.visible=true;toast(text);
}
function toast(text:string) { $('toast').textContent=text; $('toast').classList.add('visible'); toastLife=3.5; }
// Feedback from issued commands stays visible while routine deliveries use a quiet receipt.
let lastReceipt='';
let viewport={top:130,bottom:170,width:innerWidth,height:innerHeight-300};
function resize(){
  const top=innerWidth<700?207:180, bottom=$('panel').offsetHeight+(innerWidth<700?24:38);
  const h=Math.max(120,innerHeight-top-bottom), aspect=innerWidth/h;
  const halfH=Math.max(6.6,11.7/aspect);
  camera.left=-halfH*aspect;camera.right=halfH*aspect;camera.top=halfH;camera.bottom=-halfH;camera.updateProjectionMatrix();
  renderer.setSize(innerWidth,innerHeight);viewport={top,bottom,width:innerWidth,height:h};updateCamera();
}
function changeZoom(factor:number){zoom=THREE.MathUtils.clamp(zoom*factor,1,2.8);if(zoom===1){viewCenter.x=0;viewCenter.z=0;}updateCamera();}
function pan(dx:number,dz:number){viewCenter.x=THREE.MathUtils.clamp(viewCenter.x+dx,-8,8);viewCenter.z=THREE.MathUtils.clamp(viewCenter.z+dz,-5,5);updateCamera();}
$('zoom-in').onclick=()=>changeZoom(1.25);
$('zoom-out').onclick=()=>changeZoom(.8);
$('pan-mode').onclick=()=>{panMode=!panMode;$('pan-mode').setAttribute('aria-pressed',String(panMode));updateUI();};
$('view-reset').onclick=()=>{zoom=1;viewCenter.x=0;viewCenter.z=0;panMode=false;$('pan-mode').setAttribute('aria-pressed','false');updateCamera();updateUI();};
canvas.addEventListener('wheel',e=>{e.preventDefault();changeZoom(Math.exp(-e.deltaY*.0015));},{passive:false});
function project(p:Point,y=0){const q=new THREE.Vector3(p.x,y,p.z).project(camera);return{x:(q.x+1)*viewport.width/2,y:viewport.top+(1-q.y)*viewport.height/2};}
const ray=new THREE.Raycaster(), plane=new THREE.Plane(new THREE.Vector3(0,1,0),0);
function pick(x:number,y:number){
  ray.setFromCamera(new THREE.Vector2(x/viewport.width*2-1,1-(y-viewport.top)/viewport.height*2),camera);
  // Screen-space hit radius keeps small units easy to pick.
  let worker:number|null=null,best=Infinity;
  for(const w of sim.workers){const p=project(w,.65),d=Math.hypot(x-p.x,y-p.y);if(d<(innerWidth<700?13:20)&&d<best){worker=w.id;best=d;}}
  const source=ray.intersectObjects(sourceViews.map(v=>v.hit),false)[0]?.object.userData.source as number|undefined;
  const hit=new THREE.Vector3();const ground=ray.ray.intersectPlane(plane,hit);
  const p=ground?{x:hit.x,z:hit.z}:null;
  const isDepot=p&&Math.abs(p.x-DEPOT.x)<DEPOT.halfX&&Math.abs(p.z-DEPOT.z)<DEPOT.halfZ;
  const building=p?sim.buildings.find(b=>Math.abs(p.x-b.x)<1&&Math.abs(p.z-b.z)<1):undefined;
  const fighter=sim.fighters.filter(f=>f.hp>0).map(f=>({f,p:project(f,.65)})).filter(v=>Math.hypot(x-v.p.x,y-v.p.y)<18).sort((a,b)=>Math.hypot(x-a.p.x,y-a.p.y)-Math.hypot(x-b.p.x,y-b.p.y))[0]?.f;
  return {worker,source,p,isDepot,building,fighter};
}
const distance2=(a:Point,b:Point)=>Math.hypot(a.x-b.x,a.z-b.z);
function orderTarget(o:Order):Point|null{return o.type==='move'?o.point:o.type==='transport'?DEPOT:sim.sources.find(s=>s.id===o.source)??null;}
function orderText(o:Order){return o.type==='move'?`移动到 ${o.point.x.toFixed(1)}, ${o.point.z.toFixed(1)}`:o.type==='transport'?'送回仓库':`工作：${sim.sources.find(s=>s.id===o.source)?.name??'资源点'}`;}
function returnToUnitControl(){
  inspectMode=false;
  panMode=false;panDrag=null;$('pan-mode').setAttribute('aria-pressed','false');
  $<HTMLDetailsElement>('logistics-panel').open=false;$('logistics-form').hidden=true;
}
function select(ids:number[],add=false){returnToUnitControl();if(!add){selected.clear();selectedFighters.clear();}for(const id of ids)selected.add(id); mode=null;updateUI();}
function issue(x:number,y:number,force: 'move'|'work'|null=null,append=false){
  if(sim.paused){toast('已暂停，先点击继续');return;}
  if(!selected.size&&!selectedFighters.size){toast('先点选工人或拖动框选');return;}
  const hit=pick(x,y),ids=[...selected];
  if(!hit.p||Math.abs(hit.p.x)>LIMIT.x||Math.abs(hit.p.z)>LIMIT.z){toast('目标超出场地，原任务保持');return;}
  if(force!=='move'&&hit.source&&!sim.sources.some(s=>s.id===hit.source&&s.stock>0)){commandFeedback(hit.p,'资源已采空，原任务保持','#b96850');return;}
  if(!hit.source&&!hit.isDepot&&!hit.building&&sim.blocked(hit.p,.1)){commandFeedback(hit.p,'目标被障碍占用，请点击可通行地面；原任务保持','#b96850');return;}
  if(hit.p&&selectedFighters.size)sim.commandFighters([...selectedFighters],hit.p,sim.combatEnabled&&distance2(hit.p,CAMP)<3);
  if(!ids.length){mode=null;updateUI();return;}
  if(force!=='move'&&hit.building&&!hit.building.complete){sim.build(ids,hit.building.id);toast('所选工人前往施工');updateUI();return;}
  if(force!=='move'&&hit.source){const s=sim.sources.find(s=>s.id===hit.source)!;if(sim.work(ids,s.id,append)){const assigned=sim.workers.filter(w=>w.source===s.id).length;commandFeedback(s,`${ids.length} 人 → ${ACTIVITY[s.activity]}${s.name}${assigned>4?' · 超出四个工作位的人员排队':''}${ids.some(id=>!!sim.workers[id-1].cargo)?' · 携货者先入库':''}`,'#5b9865');}else toast('这里的资源已经用完');}
  else if(force==='work'){toast('点击树木、矿石、果丛或木箱');return;}
  else if(hit.isDepot||(hit.building?.complete&&hit.building.kind==='depot')){const carrying=ids.filter(id=>!!sim.workers[id-1].cargo);if(!carrying.length){toast('所选工人没有货物，原任务保持');return;}sim.transport(carrying,append);commandFeedback(hit.building??DEPOT,`${carrying.length} 人送货，卸货后待命；未携货人员继续原任务`,'#ba8c3f');}
  else if(hit.p&&Math.abs(hit.p.x)<=LIMIT.x&&Math.abs(hit.p.z)<=LIMIT.z){sim.move(ids,hit.p,append);commandFeedback(hit.p,`${ids.length} 人移动到目标位置${ids.some(id=>!!sim.workers[id-1].cargo)?' · 携货保留':''}`);}
  else {toast('请点击场地边界以内');return;}
  if(append)toast('已追加到任务队列 · 采集任务在资源耗尽后结束');
  mode=null;updateUI();
}
let drag:{x:number;y:number;shift:boolean;id:number}|null=null;
let panDrag:{x:number;y:number}|null=null;
let hover:{x:number;y:number}|null=null;
canvas.addEventListener('contextmenu',e=>e.preventDefault());
canvas.addEventListener('pointerdown',e=>{
  if(e.button===1||(e.button===0&&panMode)){e.preventDefault();panDrag={x:e.clientX,y:e.clientY};canvas.setPointerCapture(e.pointerId);return;}
  if(e.button===2&&buildMode){cancelPlacement();return;}
  if(e.button===2){issue(e.clientX,e.clientY,null,e.shiftKey);return;}
  if(e.button!==0)return;
  canvas.focus();drag={x:e.clientX,y:e.clientY,shift:e.shiftKey,id:e.pointerId};canvas.setPointerCapture(e.pointerId);
});
canvas.addEventListener('pointermove',e=>{
  hover={x:e.clientX,y:e.clientY};
  if(buildMode){const p=pick(e.clientX,e.clientY).p;if(p){buildPosition=sim.snap(p);updateGhost();}}
  if(panDrag){const scale=(camera.right-camera.left)/viewport.width/zoom;pan((panDrag.x-e.clientX)*scale,(panDrag.y-e.clientY)*scale*1.27);panDrag={x:e.clientX,y:e.clientY};return;}
  if(!drag)return;
  if(Math.hypot(e.clientX-drag.x,e.clientY-drag.y)<7)return;
  boxPreview.clear();for(const w of sim.workers){const p=project(w,.65);if(p.x>=Math.min(e.clientX,drag.x)&&p.x<=Math.max(e.clientX,drag.x)&&p.y>=Math.min(e.clientY,drag.y)&&p.y<=Math.max(e.clientY,drag.y))boxPreview.add(w.id);}
  const box=$('selection-box');box.hidden=false;Object.assign(box.style,{left:`${Math.min(e.clientX,drag.x)}px`,top:`${Math.min(e.clientY,drag.y)}px`,width:`${Math.abs(e.clientX-drag.x)}px`,height:`${Math.abs(e.clientY-drag.y)}px`});
});
canvas.addEventListener('pointerup',e=>{
  if(panDrag){panDrag=null;return;}
  if(!drag||e.pointerId!==drag.id)return;
  const d=drag;drag=null;boxPreview.clear();$('selection-box').hidden=true;
  if(Math.hypot(e.clientX-d.x,e.clientY-d.y)>7){
    select(sim.workers.filter(w=>{const p=project(w,.65);return p.x>=Math.min(d.x,e.clientX)&&p.x<=Math.max(d.x,e.clientX)&&p.y>=Math.min(d.y,e.clientY)&&p.y<=Math.max(d.y,e.clientY);}).map(w=>w.id),d.shift);
    for(const f of sim.fighters.filter(f=>f.hp>0)){const p=project(f,.65);if(p.x>=Math.min(d.x,e.clientX)&&p.x<=Math.max(d.x,e.clientX)&&p.y>=Math.min(d.y,e.clientY)&&p.y<=Math.max(d.y,e.clientY))selectedFighters.add(f.id);}
    updateUI();
  }else if(buildMode){const p=pick(e.clientX,e.clientY).p;if(p){const b=sim.place(buildMode,p,[...selected]);toast(sim.notice);if(b)cancelPlacement();}updateUI();}
  else if(mode){issue(e.clientX,e.clientY,mode,e.shiftKey);}
  else {
    const hit=pick(e.clientX,e.clientY);
    if(inspectMode||e.altKey){
      if(hit.source){inspectedSource=hit.source;inspectMode=false;updateUI();}
      else toast('点击资源查看详情');
    }else if(hit.worker){
      if(d.shift&&selected.has(hit.worker)){selected.delete(hit.worker);updateUI();}else select([hit.worker],d.shift);
    }else if(hit.fighter){
      returnToUnitControl();if(!d.shift){selected.clear();selectedFighters.clear();}if(d.shift&&selectedFighters.has(hit.fighter.id))selectedFighters.delete(hit.fighter.id);else selectedFighters.add(hit.fighter.id);updateUI();
    }else if(selected.size||selectedFighters.size){
      issue(e.clientX,e.clientY,null,d.shift);
    }else if(hit.source){inspectedSource=hit.source;updateUI();}
  }
});
canvas.addEventListener('pointercancel',()=>{drag=null;panDrag=null;boxPreview.clear();$('selection-box').hidden=true;});
canvas.addEventListener('pointerleave',()=>{hover=null;$('hover-hint').hidden=true;for(const v of sourceViews)v.el.classList.remove('target-preview');});
document.querySelectorAll<HTMLButtonElement>('[data-unit]').forEach(b=>b.addEventListener('click',e=>select([Number(b.dataset.unit)],(e as MouseEvent).shiftKey)));
const logisticsForm=$<HTMLFormElement>('logistics-form');
document.body.append(logisticsForm);logisticsForm.hidden=true;
$<HTMLDetailsElement>('logistics-panel').ontoggle=()=>{logisticsForm.hidden=!$<HTMLDetailsElement>('logistics-panel').open;};
$('logistics-close').onclick=()=>{returnToUnitControl();canvas.focus();updateUI();};
function applyLogistics(){
 const fields=new FormData(logisticsForm),value=(key:string)=>Number(fields.get(key));
 const error=sim.configureLogistics({maxItems:value('maxItems'),maxWeight:value('maxWeight'),walkSpeed:value('walkSpeed'),gatherRate:value('gatherRate'),unloadPerItem:value('unloadPerItem'),weights:{wood:value('wood'),ore:value('ore'),food:value('food')}});
 $('logistics-message').textContent=error||'参数已应用；已开工批次与携货保留，后续按新参数执行。';updateUI();
}
logisticsForm.onsubmit=e=>{e.preventDefault();applyLogistics();};
$('logistics-defaults').onclick=()=>{for(const [key,value]of Object.entries({...DEFAULT_LOGISTICS,...DEFAULT_LOGISTICS.weights})){const input=logisticsForm.elements.namedItem(key);if(input instanceof HTMLInputElement)input.value=String(value);}applyLogistics();};
$('resource-close').onclick=()=>{inspectedSource=null;updateUI();};
$('inspect-resource').onclick=()=>{returnToUnitControl();mode=null;inspectMode=true;toast('点击资源查看详情，不改变工人任务');updateUI();};
$('resource-assign').onclick=e=>{if(!inspectedSource)return;const s=sim.sources.find(s=>s.id===inspectedSource)!;if(sim.work([...selected],s.id,(e as MouseEvent).shiftKey))toast(`${selected.size} 人已安排到${s.name}，携货者先入库再开始`);mode=null;updateUI();};
$('resource-workers').onclick=()=>select(sim.workers.filter(w=>w.source===inspectedSource).map(w=>w.id));
$('retry-route').onclick=()=>{sim.retryRoute([...selected]);toast('已重新计算路线；若仍不通，任务与携货继续保留');updateUI();};
$('all').onclick=()=>select(sim.workers.map(w=>w.id));
$('idle').onclick=()=>select(sim.workers.filter(w=>w.phase==='idle').map(w=>w.id));
$('auto-jobs').onclick=e=>{
  if(sim.paused){toast('已暂停，先点击继续');return;}
  if(!selected.size){toast('先选择小兵');return;}
  const assigned=sim.distribute([...selected],(e as MouseEvent).shiftKey);
  toast(assigned.length?`${assigned.length} 人已自动分工 · 木材 / 矿石 / 果实 / 搬货`:'可采资源已经用完');mode=null;updateUI();
};
$('continue-job').onclick=()=>{
  const ws=sim.workers.filter(w=>selected.has(w.id));
  if(!ws.length){toast('先选择小兵');return;}
  if(sim.paused){toast('已暂停，先点击继续');return;}
  const enabled=!ws.every(w=>w.continueJob);sim.setContinuation([...selected],enabled);
  toast(enabled?'已开启：采空后寻找同类资源；后续命令优先':'已关闭：采空后待命或执行后续命令');updateUI();
};
$('select-waiting').onclick=()=>select(sim.workers.filter(w=>workerState(w)==='wait'&&w.phase!=='blocked').map(w=>w.id));
$('select-blocked').onclick=()=>{select(sim.workers.filter(w=>w.phase==='blocked').map(w=>w.id));if(selected.size)window.dispatchEvent(new KeyboardEvent('keydown',{key:'f'}));};
$('roadblock').onclick=()=>{sim.toggleRoadblock();toast(sim.notice);updateUI();};
$('workboard-toggle').onclick=()=>{const open=$('workboard').hidden;$('workboard').hidden=!open;$('workboard-toggle').setAttribute('aria-expanded',String(open));};
$('workboard-rows').innerHTML=sim.sources.map(s=>`<div class="board-row" data-source="${s.id}"><button class="board-source" data-select-source="${s.id}" aria-label="选择${s.name}人员">${s.name}</button><span class="board-info"></span><button data-remove-source="${s.id}" aria-label="${s.name}撤回一人">−</button><b class="board-count">0</b><button data-add-source="${s.id}" aria-label="${s.name}补一人">＋</button></div>`).join('');
for(const b of document.querySelectorAll<HTMLButtonElement>('[data-select-source]'))b.onclick=()=>{const source=Number(b.dataset.selectSource);select(sim.workers.filter(w=>w.source===source).map(w=>w.id));};
for(const b of document.querySelectorAll<HTMLButtonElement>('[data-add-source]'))b.onclick=()=>{
 const source=Number(b.dataset.addSource),id=sim.addWorker(source);
 toast(id?`${id} 号已补到${sim.sources.find(s=>s.id===source)!.name}`:sim.paused?'已暂停，先点击继续':'没有空闲且未携货的人员可补充');updateUI();
};
for(const b of document.querySelectorAll<HTMLButtonElement>('[data-remove-source]'))b.onclick=()=>{
 const id=sim.removeWorker(Number(b.dataset.removeSource));toast(id?`${id} 号已撤回 · 有货先入库，然后待命`:sim.paused?'已暂停，先点击继续':'这里没有已分配的人员');updateUI();
};

for(const b of document.querySelectorAll<HTMLButtonElement>('[data-job]'))b.onclick=e=>{
  if(sim.paused){toast('已暂停，先点击继续');return;}
  if(!selected.size){toast('先选择小兵');return;}
  const assigned=sim.assign([...selected],b.dataset.job as Job,(e as MouseEvent).shiftKey);
  toast(assigned.length?`${assigned.length} 人已${(e as MouseEvent).shiftKey?'追加':'分配'}${ACTIVITY[b.dataset.job as Job]}任务${b.dataset.job==='chop'?' · 按各树木人数分配':''}`:'这类资源已经采完');mode=null;updateUI();
};
for(const b of document.querySelectorAll<HTMLButtonElement>('[data-group]'))b.onclick=e=>{
 const n=Number(b.dataset.group);
 if((e as MouseEvent).ctrlKey||(e as MouseEvent).metaKey){groups.set(n,[...selected]);toast(`已保存第 ${n} 组`);}
 else select(groups.get(n)??[]);
};
document.querySelectorAll<HTMLButtonElement>('[data-order]').forEach(b=>b.onclick=()=>{returnToUnitControl();mode=b.dataset.order as 'move'|'work';toast(mode==='move'?'点击地面指定移动位置':'点击资源指定工作');canvas.focus();updateUI();});
$('deposit').onclick=e=>{sim.transport(sim.workers.filter(w=>selected.has(w.id)&&w.cargo).map(w=>w.id),(e as MouseEvent).shiftKey);mode=null;toast('携货者送完后待命，其余人员继续原任务');updateUI();};
$('deposit').title='立即送回现有货物，然后待命；不会返回资源点';
$('finish-work').onclick=()=>{sim.finishWork([...selected]);mode=null;toast('完成这一份采集并卸货后待命，后续任务已取消');updateUI();};
$('resume-work').onclick=()=>{const ids=sim.resumeWork([...selected]);toast(ids.length?`${ids.length} 人恢复原采集`:'所选人员无可恢复任务，或原资源已采空');updateUI();};
$('stop').onclick=()=>{if(sim.paused){toast('已暂停，先点击继续');return;}sim.stopFighters([...selectedFighters]);sim.stop([...selected]);mode=null;toast('已原地停止 · 携货保留 · 后续任务已清空');updateUI();};
function pause(){sim.paused=!sim.paused;$('pause').textContent=sim.paused?'继续':'暂停';$('pause').setAttribute('aria-pressed',String(sim.paused));updateUI();}
$('pause').onclick=pause;
$('reset').onclick=()=>location.reload();
window.addEventListener('keydown',e=>{
  if(e.key==='Escape'){const cancelOnly=!!mode||inspectMode||panMode||!!buildMode||$<HTMLDetailsElement>('logistics-panel').open;returnToUnitControl();cancelPlacement();mode=null;if(!cancelOnly){selected.clear();selectedFighters.clear();inspectedSource=null;}canvas.focus();updateUI();return;}
  if(e.repeat||e.target instanceof HTMLInputElement||e.target instanceof HTMLSelectElement)return;
  if(e.key.toLowerCase()==='f'){const ws=[...sim.workers.filter(w=>selected.has(w.id)),...sim.fighters.filter(f=>selectedFighters.has(f.id)&&f.hp>0)];if(ws.length){viewCenter.x=ws.reduce((n,w)=>n+w.x,0)/ws.length;viewCenter.z=ws.reduce((n,w)=>n+w.z,0)/ws.length;zoom=Math.max(zoom,1.4);updateCamera();}return;}
  if(e.key.toLowerCase()==='i'){$('idle').click();return;}
  if(e.key.toLowerCase()==='m'){document.querySelector<HTMLButtonElement>('[data-order=move]')!.click();return;}
  if(e.key.toLowerCase()==='g'){document.querySelector<HTMLButtonElement>('[data-order=work]')!.click();return;}
  if(/^[1-3]$/.test(e.key)){
    const n=Number(e.key);
    if(e.ctrlKey||e.metaKey){e.preventDefault();groups.set(n,[...selected]);toast(`已保存第 ${n} 组`);}
    else select(groups.get(n)??[],e.shiftKey);
  }
  else if(e.code==='Space'){e.preventDefault();pause();}
  else if(e.key.toLowerCase()==='s'){$('stop').click();}
  else if(e.key==='Home'){$('view-reset').click();}
  else if(e.key==='ArrowLeft'){e.preventDefault();pan(-.65/zoom,0);}
  else if(e.key==='ArrowRight'){e.preventDefault();pan(.65/zoom,0);}
  else if(e.key==='ArrowUp'){e.preventDefault();pan(0,-.65/zoom);}
  else if(e.key==='ArrowDown'){e.preventDefault();pan(0,.65/zoom);}
});
function updateUI(){
  for(const id of selectedFighters)if(!sim.fighters.some(f=>f.id===id&&f.hp>0))selectedFighters.delete(id);
  const ids=[...selected].sort((a,b)=>a-b);$('selected-title').textContent=ids.length?(ids.length>4?`已选中 ${ids.length} 人`:`已选中 ${ids.join('、')} 号`):'未选中小兵';
  if(selectedFighters.size)$('selected-title').textContent=`已选 ${selected.size} 工人 · ${selectedFighters.size} 领主`;
  $('detail').textContent=ids.length===1?sim.status(sim.workers[ids[0]-1]):ids.length?`${ids.length} 个单位 · 指令仅影响选中单位`:'点击小兵，或拖动框选';
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-unit]')){
    const w=sim.workers[Number(b.dataset.unit)-1];b.setAttribute('aria-pressed',String(selected.has(w.id)));
    b.querySelector('span')!.textContent=w.phase==='blocked'?'受阻':stateNames[workerState(w)];
    b.dataset.phase=w.phase;b.dataset.state=workerState(w);b.title=`${w.id} 号：${sim.status(w)}`;
  }
  if(selectedFighters.size&&!selected.size)$('detail').textContent='点击地面移动 · S 停止';
  const counts={idle:0,move:0,work:0,haul:0,wait:0};for(const w of sim.workers)counts[workerState(w)]++;
  $('activity-summary').innerHTML=Object.entries(counts).map(([k,n])=>`<span style="--state:${stateColors[k as keyof typeof stateColors]}">${stateNames[k as keyof typeof stateNames]} <b>${n}</b></span>`).join('');
  const assignments=new Map<string,number>();for(const w of sim.workers.filter(w=>selected.has(w.id))){const name=w.source?sim.sources.find(s=>s.id===w.source)!.name:w.cargo?'送货':w.phase==='idle'?'空闲':'移动';assignments.set(name,(assignments.get(name)??0)+1);}
  $('job-detail').textContent=[...assignments].map(([name,n])=>`${name} ${n}人`).join(' · ')||'选人后点击即可分配';
  const ws=sim.workers.filter(w=>selected.has(w.id)),continues=ws.filter(w=>w.continueJob).length;
  $('continue-job').textContent=`采空后续采：${!continues?'关':continues===ws.length?'开':'部分'}`;
  $('continue-job').setAttribute('aria-pressed',continues&&continues<ws.length?'mixed':String(!!continues));
  $('continuation-hint').textContent=ws.length?`${continues}/${ws.length} 人开启 · 后续命令优先`:'仅影响所选人员 · 后续命令优先';
  if(!$('workboard').hidden)for(const row of document.querySelectorAll<HTMLElement>('.board-row')){
    const source=Number(row.dataset.source),s=sim.sources.find(s=>s.id===source)!,workers=sim.workers.filter(w=>w.source===source);
    row.querySelector('.board-info')!.textContent=`余 ${s.stock} · 工作 ${workers.filter(w=>w.phase==='work').length} · 排队 ${workers.filter(w=>w.phase==='waitWork'||w.phase==='waitDepot').length} · 受阻 ${workers.filter(w=>w.phase==='blocked').length}`;
    row.querySelector('.board-count')!.textContent=String(workers.length);
    row.querySelector<HTMLButtonElement>('[data-add-source]')!.disabled=sim.paused||!s.stock||!sim.workers.some(w=>w.phase==='idle'&&!w.cargo&&!w.queue.length);
    row.querySelector<HTMLButtonElement>('[data-remove-source]')!.disabled=sim.paused||!workers.length;
  }
  const tuning=sim.tuning;
  $('logistics-summary').textContent=`每趟 ≤${tuning.maxItems} 件 / ${tuning.maxWeight} kg`;
  if($<HTMLDetailsElement>('logistics-panel').open){
    $('logistics-preview').innerHTML=sim.sources.filter((s,i)=>i!==1).map(s=>{const p=sim.batchPlan(s);return `<span>${s.name}：${p.amount} 件 / ${p.weight.toFixed(1)} kg · 采 ${p.seconds.toFixed(2)} 秒 · 运 ${p.speed.toFixed(2)} m/s</span>`;}).join('');
    $('logistics-measured').textContent=`实际累计入库 ${sim.collectionStats.delivered} 件 · 全场平均 ${sim.deliveredPerMinute.toFixed(1)} 件/分（含起步、等待与停工）`;
  }
  const blockedCount=sim.workers.filter(w=>w.phase==='blocked').length;
  $('select-blocked').textContent=`选受阻${blockedCount?' '+blockedCount:''}`;$('select-blocked').toggleAttribute('disabled',!blockedCount);
  $('roadblock').textContent=sim.roadblock?'移除练习路障':'放置练习路障';$('roadblock').setAttribute('aria-pressed',String(sim.roadblock));$('roadblock').toggleAttribute('disabled',sim.paused);
  const carrying=ws.reduce((n,w)=>n+(w.cargo?.amount??0),0),pending=ws.filter(w=>w.finishAfterDelivery).length;
  $('collection-feedback').textContent=ws.length?`${ws.length} 人已选 · 携货 ${carrying} 件 / ${ws.reduce((n,w)=>n+sim.cargoWeight(w),0).toFixed(1)} kg（尚未入库）${pending?` · ${pending} 人本轮后结束`:''}${ws.some(w=>w.phase==='idle'&&w.cargo)?' · 停止后货物仍在，点送回仓库或改派':''}`:'先选人，再点击资源或地面';
  $('retry-route').toggleAttribute('disabled',sim.paused||!ws.some(w=>w.phase==='blocked'));
  $('finish-work').toggleAttribute('disabled',sim.paused||!ws.some(w=>(w.source||w.cargo)&&!w.finishAfterDelivery));
  $('resume-work').toggleAttribute('disabled',sim.paused||!ws.some(w=>w.phase==='idle'&&sim.sources.some(s=>s.id===w.lastSource&&s.stock>0)));
  $('stop').toggleAttribute('disabled',sim.paused||(!ws.some(w=>w.phase!=='idle'||w.queue.length)&&!selectedFighters.size));
  $('deposit').toggleAttribute('disabled',sim.paused||!ws.some(w=>w.cargo));
  const resource=sim.sources.find(s=>s.id===inspectedSource);$('resource-card').hidden=!resource;
  if(resource){
    const staff=sim.workers.filter(w=>w.source===resource.id),waiting=staff.filter(w=>w.phase==='waitWork').length;
    $('resource-name').textContent=resource.name;
    $('resource-detail').textContent=`剩余 ${resource.stock} · 已分配 ${staff.length} 人 · 工作位 4 个${waiting?` · ${waiting} 人等待工作位`:''}${resource.stock?' · '+tuning.weights[resource.kind]+' kg/件 · 每趟 '+sim.batchPlan(resource).amount+' 件 · 采 '+sim.batchPlan(resource).seconds.toFixed(2)+' 秒':' · 已采空，请改派其他资源'}`;
    $('resource-assign').toggleAttribute('disabled',sim.paused||!selected.size||!resource.stock);
    $('resource-workers').toggleAttribute('disabled',!staff.length);
  }
  const single=ids.length===1?sim.workers[ids[0]-1]:null;
  $('worker-advice').hidden=!single;$('worker-advice').textContent=single?`${sim.workerAdvice(single)} 负重 ${sim.cargoWeight(single).toFixed(1)}/${tuning.maxWeight} kg · 移动上限 ${sim.travelSpeed(single).toFixed(2)} m/s${single.batch?' · 本份 '+single.batch.amount+' 件 / '+single.batch.seconds.toFixed(2)+' 秒':''}`:'';
  const orderHTML=single&&single.queue.length?`<span>待执行</span>${single.queue.map((o,i)=>`<span class="order-chip">${i+1}. ${orderText(o)}</span>`).join('')}<button id="clear-queue">清空后续</button>`:'';
  $('orders').hidden=!orderHTML;
  if($('orders').innerHTML!==orderHTML){$('orders').innerHTML=orderHTML;const clear=document.getElementById('clear-queue');if(clear)clear.onclick=()=>{sim.clearQueue([...selected]);updateUI();};}
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-job]'))b.disabled=sim.paused||!selected.size||!sim.sources.some(s=>s.activity===b.dataset.job&&s.stock>0);
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-group]')){const ids=groups.get(Number(b.dataset.group))??[];b.classList.toggle('active',ids.length===selected.size&&ids.every(id=>selected.has(id)));b.title=`${ids.join('、')} 号 · Ctrl + 点击保存当前选区`;}

  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-order]'))b.classList.toggle('active',b.dataset.order===mode);
  $('inspect-resource').classList.toggle('active',inspectMode);
  $('hint').textContent=inspectMode?'点击资源只查看，不派工 · Esc 取消':mode==='move'?'点击地面 → 移动到该位置 · Esc 取消':mode==='work'?'点击树木 / 矿石 / 果丛 / 待运木箱 → 自动工作、运送':'选人后点击：地面移动 / 资源采集 / 仓库送货 · Shift 追加 · Esc 取消选择';
  canvas.style.cursor=buildMode||inspectMode?'crosshair':panMode?'grab':mode?'crosshair':'default';
  updateCampaignUI();
}
function placeLabel(el:HTMLElement,p:Point,y:number){const q=project(p,y);el.style.left=`${q.x}px`;el.style.top=`${q.y}px`;}
let prev=performance.now(),acc=0,uiTime=0;
function updateGeometry(line:THREE.Line,points:Point[]){const old=line.geometry;line.geometry=new THREE.BufferGeometry().setFromPoints(points.map(p=>new THREE.Vector3(p.x,.045,p.z)));line.computeLineDistances();old.dispose();}
function frame(now:number){
  const dt=Math.min((now-prev)/1000,.1);prev=now;acc+=dt;
  while(acc>=1/60){sim.tick(1/60);acc-=1/60;}
  renderCampaign(dt);
  uiTime+=dt;
  for(const v of workerViews){
    const {w}=v;v.group.position.set(w.x,0,w.z);v.body.rotation.y+=Math.atan2(Math.sin(w.face-v.body.rotation.y),Math.cos(w.face-v.body.rotation.y))*Math.min(1,dt*14);
    v.body.position.y=w.moving?Math.sin(sim.time*15+w.id)*.035:0;
    v.legs.forEach((leg,i)=>leg.rotation.x=w.moving?Math.sin(sim.time*15+i*Math.PI)*.42:0);
    v.arm.rotation.x=(w.phase==='work'||w.phase==='build')?Math.sin(sim.time*10)*.8-.4:0;
    const source=sim.sources.find(s=>s.id===w.source);
    v.tool.visible=(w.phase==='build')||w.phase==='work'&&(source?.activity==='chop'||source?.activity==='mine');
    v.cargo.visible=!!w.cargo;v.cargo.material=w.cargo?.kind==='ore'?rock:w.cargo?.kind==='food'?green:crate;
    v.ring.visible=selected.has(w.id)||boxPreview.has(w.id);v.path.visible=selected.has(w.id)&&w.path.length>0;
    if(v.path.visible&&uiTime>.12)updateGeometry(v.path,[w,...w.path]);
    v.destination.visible=selected.has(w.id)&&!!w.goal&&w.path.length>0&&(w.phase==='move'||selected.size<=4);
    (v.path.material as THREE.LineDashedMaterial).opacity=selected.size>4?.2:.55;
    if(w.goal)v.destination.position.set(w.goal.x,.025,w.goal.z);
    v.queuePath.visible=selected.has(w.id)&&selected.size===1&&w.queue.length>0;
    if(v.queuePath.visible&&uiTime>.12)updateGeometry(v.queuePath,[w.goal??w,...w.queue.map(orderTarget).filter((p):p is Point=>!!p)]);
    const progress=w.phase==='work'||w.phase==='unload'||w.phase==='build';
    if(uiTime>.12){v.el.innerHTML=`<strong>${w.id} 号${w.continueJob?' ↻':''}</strong><span class="unit-state"> · ${sim.status(w)}</span>${w.cargo?`<span class="cargo-text">${KIND[w.cargo.kind]} ×${w.cargo.amount} · ${sim.cargoWeight(w).toFixed(1)} kg</span>`:''}${progress?`<div class="taskbar"><i style="width:${w.progress*100}%"></i></div>`:''}`;v.el.style.setProperty('--state',stateColors[workerState(w)]);}
    v.el.classList.toggle('selected',selected.has(w.id)||boxPreview.has(w.id));v.el.classList.toggle('compact',!selected.has(w.id)||selected.size>1);placeLabel(v.el,w,1.48);
  }
  for(const [si,v] of sourceViews.entries()){
    const working=sim.workers.filter(w=>w.source===v.s.id&&w.slot!==null).length;
    const waiting=sim.workers.filter(w=>w.source===v.s.id&&w.phase==='waitWork'&&!w.path.length).length;
    slotRings[si].forEach((ring,i)=>{ring.visible=v.s.stock>0;(ring.material as THREE.MeshBasicMaterial).color.set(sim.workers.some(w=>w.source===v.s.id&&w.slot===i)?'#c39849':'#91a48b');});
    v.el.classList.toggle('assigned',sim.workers.some(w=>selected.has(w.id)&&w.source===v.s.id));
    v.el.innerHTML=`${v.s.name}<small>${v.s.stock>0?`剩余 ${v.s.stock}<span class="resource-slots"> · 占位 ${working}/4${waiting?` · 等待 ${waiting}`:''}</span>`:'已采空'}</small>`;placeLabel(v.el,v.s,v.s.activity==='chop'?3.15:1.15);if(v.s.stock===0)v.parts.forEach(p=>p.visible=false);}
  const unloading=sim.workers.filter(w=>w.phase==='unload').length, waiting=sim.workers.filter(w=>w.phase==='waitDepot'&&!w.path.length).length;
  depotLabel.innerHTML=`仓库<small>卸货 ${unloading}/3${waiting?` · 等待 ${waiting}`:''}</small>`;
  bayRings.forEach((ring,i)=>(ring.material as THREE.MeshBasicMaterial).color.set(sim.workers.some(w=>w.bay===i)?'#c39849':'#91a48b'));
  placeLabel(depotLabel,DEPOT,1.12);
  for(const kind of ['wood','ore','food'] as const)$(kind).textContent=String(sim.inventory[kind]);
  if(uiTime>.12){updateUI();uiTime=0;}
  const messages=sim.messages.splice(0);const message=messages.at(-1);if(message){lastReceipt=`${message.worker} 号：${message.text}`;$('receipt').textContent=lastReceipt;}
  if(hover&&!drag&&!panDrag&&uiTime<.02){
    const hit=pick(hover.x,hover.y),source=sim.sources.find(s=>s.id===hit.source),hint=$('hover-hint');
    const count=selected.size+selectedFighters.size;
    let text='';
    if(sim.paused)text='已暂停：可以选择人员，继续后再下令';
    else if(hit.worker)text=`点击选择 ${hit.worker} 号 · Shift 加选`;
    else if(inspectMode||!count)text=source?`${source.name} · 点击查看详情`:'先点击工人或框选人员';
    else if(source)text=source.stock?`${source.name} · 点击安排 ${selected.size} 名工人${sim.workers.filter(w=>w.source===source.id&&!selected.has(w.id)).length+selected.size>4?' · 多余人员排队':''} · Alt 查看`:`${source.name}已采空 · 点击不会改变原任务`;
    else if(hit.isDepot)text='点击让携货工人送货 · 其余工人继续原任务';
    else if(hit.p&&Math.abs(hit.p.x)<=LIMIT.x&&Math.abs(hit.p.z)<=LIMIT.z)text=sim.blocked(hit.p,.1)?'障碍物占用 · 请点击可通行地面':`点击移动 ${count} 人到这里 · Shift 追加`;
    else text='场地以外 · 不可下令';
    hint.hidden=!text;hint.textContent=text;hint.style.left=`${Math.min(innerWidth-250,Math.max(8,hover.x+14))}px`;hint.style.top=`${Math.min(innerHeight-50,hover.y+18)}px`;
    for(const v of sourceViews)v.el.classList.toggle('target-preview',v.s.id===source?.id);
  }
  toastLife-=dt;if(toastLife<=0)$('toast').classList.remove('visible');
  markerLife-=dt;marker.visible=markerLife>0;if(marker.visible)marker.scale.setScalar(1+(1-markerLife)*.7);
  renderer.setViewport(0,0,innerWidth,innerHeight);renderer.clear();
  renderer.setViewport(0,viewport.bottom,viewport.width,viewport.height);renderer.render(scene,camera);
  requestAnimationFrame(frame);
}

const roadblockView=new THREE.Group();scene.add(roadblockView);for(let z=-6.2;z<=6.2;z+=.8)box(roadblockView,mat('#b67b48'),-2,.3,z,.5,.6,.65);roadblockView.visible=false;
const ghost=mesh(new THREE.BoxGeometry(2,.2,2),new THREE.MeshBasicMaterial({color:'#65a66d',transparent:true,opacity:.5}),scene,0,.15,0);ghost.visible=false;
function updateGhost(){ghost.visible=!!buildMode&&!!buildPosition;if(buildPosition){const error=sim.placement(buildPosition);ghost.position.set(buildPosition.x,.15,buildPosition.z);(ghost.material as THREE.MeshBasicMaterial).color.set(error?'#c16b60':'#65a66d');$('placement-note').textContent=error?`无法放置：${error}`:`可放置 · ${buildMode==='depot'?'仓库':'兵营'} · 2×2 格 · 点击确认，Esc / 右键取消`;}}
function cancelPlacement(){buildMode=null;buildPosition=null;ghost.visible=false;$('placement-note').hidden=true;}
for(const b of document.querySelectorAll<HTMLButtonElement>('[data-build]'))b.onclick=()=>{
 if(!selected.size){toast('先选择工人施工');return;}
 if(sim.paused||sim.ended){toast('当前无法建造');return;}
 const kind=b.dataset.build as BuildingKind;if(sim.inventory.wood<COST[kind]){toast(`木材还差 ${COST[kind]-sim.inventory.wood}，需要先运回仓库`);return;}
 buildMode=b.dataset.build as BuildingKind;mode=null;panMode=false;$('pan-mode').setAttribute('aria-pressed','false');
 $('placement-note').hidden=false;$('placement-note').textContent=`放置${buildMode==='depot'?'前哨仓库':'兵营'} · ${COST[buildMode]} 木材 · 点击绿地确认，Esc / 右键取消`;
 toast('点击空地放置，红色表示占地受阻');updateUI();
};
$('select-army').onclick=()=>{selected.clear();selectedFighters.clear();for(const f of sim.fighters.filter(f=>f.hp>0))selectedFighters.add(f.id);updateUI();};
$('select-lord').onclick=()=>{selected.clear();selectedFighters.clear();if(sim.fighters[0].hp>0)selectedFighters.add(201);updateUI();};
$('recruit').onclick=()=>{sim.recruit();toast(sim.notice);updateUI();};
$('attack-camp').onclick=()=>{cancelPlacement();if(!selectedFighters.size){toast('先选择领主或士兵');return;}sim.commandFighters([...selectedFighters],CAMP,true);toast('部队向营地进攻');};
$('retreat').onclick=()=>{if(!selectedFighters.size){toast('先选择要撤回的部队');return;}sim.retreat([...selectedFighters]);toast(sim.notice);updateUI();};
$('play-again').onclick=()=>location.reload();
function chooseBuilders(){
 const ws=sim.workers.filter(w=>!sim.builders.has(w.id)).sort((a,b)=>Number(a.phase!=='idle')-Number(b.phase!=='idle')||Number(!!a.cargo)-Number(!!b.cargo)||a.id-b.id).slice(0,2);
 select(ws.map(w=>w.id));return ws.map(w=>w.id);
}
$('guide-action').onclick=()=>{
 if(sim.paused){toast('已暂停，先点击继续');return;}
 const guide=sim.guide;
 if(guide.action==='restart'){location.reload();return;}
 if(guide.action==='gather'){
  const idle=sim.workers.filter(w=>w.phase==='idle'&&!w.cargo&&!sim.builders.has(w.id)).map(w=>w.id);
  if(!idle.length){toast('工人已在工作，等待货物入库；也可手动调整分工');return;}
  const ids=idle.slice(0,4);select(ids);const assigned=sim.sources.some(s=>s.activity==='chop'&&s.stock>0)?sim.assign(ids,'chop'):sim.distribute(ids);toast(assigned.length?`已安排 ${assigned.length} 名工人采集，采得后自动入库`:'可采资源已经用完');
 }else if(guide.action==='retryRoute'){select(sim.workers.filter(w=>w.phase==='blocked').map(w=>w.id));$('retry-route').click();
 }else if(guide.action==='deliverCollection'){select(sim.workers.filter(w=>w.phase==='idle'&&w.cargo).map(w=>w.id));$('deposit').click();
 }else if(guide.action==='finishCollection'){
  const w=sim.workers.find(w=>w.source&&!w.finishAfterDelivery);if(w){select([w.id]);$('finish-work').click();}else toast('没有正在采集的人员，请先下达采集任务');
 }else if(guide.action==='resumeCollection'){
  const w=sim.workers.find(w=>w.phase==='idle'&&sim.sources.some(s=>s.id===w.lastSource&&s.stock>0));if(w){select([w.id]);$('resume-work').click();}else toast('正在结束本轮，等待工人送货并待命');
 }else if(guide.action==='placeDepot' ||guide.action==='placeBarracks'){
  chooseBuilders();document.querySelector<HTMLButtonElement>(`[data-build=${guide.action==='placeDepot'?'depot':'barracks'}]`)!.click();
 }else if(guide.action==='resume'&&guide.building){sim.build(chooseBuilders(),guide.building);toast('已安排两名工人继续施工');}
 else if(guide.action==='train'){$('recruit').click();}
 else if(guide.action==='attack'){$('select-army').click();$('attack-camp').click();}
 updateUI();
};
const buildingViews=new Map<number,{group:THREE.Group;body:THREE.Mesh;el:HTMLElement}>();
const fighterViews=new Map<number,{group:THREE.Group;body:THREE.Group;ring:THREE.Mesh;el:HTMLElement}>();
const enemyViews=sim.enemies.map(e=>{const group=new THREE.Group();scene.add(group);box(group,mat('#b66153'),0,.6,0,.5,.8,.45);box(group,skin,0,1.15,0,.32,.3,.32);group.position.set(e.x,0,e.z);return{e,group,el:label('worker-label enemy-label')};});
const campGroup=new THREE.Group();campGroup.position.set(CAMP.x,0,CAMP.z);if(sim.combatEnabled)scene.add(campGroup);box(campGroup,mat('#925d50'),0,.5,0,1.3,1,1.1);box(campGroup,mat('#b75e4f'),0,1.15,0,1.5,.25,1.3);const campLabel=label('source-label enemy-label');campLabel.hidden=!sim.combatEnabled;
let lastNotice='';
function updateCampaignUI(){
 const guide=sim.guide,ended=sim.ended;
 const track=['选人与派工','运送入库','结束采集','恢复与改派'];
 $('step-track').innerHTML=track.map((name,i)=>`<span class="${i<guide.step?'done':i===guide.step?'current':''}">${i<guide.step?'✓':i+1} ${name}</span>`).join('');
 $('guide-title').textContent=guide.title;$('guide-detail').textContent=guide.detail;$('guide-action').textContent=guide.label;
 $('guide-action').toggleAttribute('disabled',guide.action==='wait'||sim.paused);
 $('objective').classList.toggle('placing',!!buildMode);
 $('mission-result').hidden=!ended;
 if(ended){
  cancelPlacement();$('result-title').textContent=sim.won?'营地已清除 · 挑战完成':'领主倒下 · 本局结束';
  const minutes=Math.floor(sim.time/60),seconds=Math.floor(sim.time%60).toString().padStart(2,'0');
  $('result-stats').textContent=`用时 ${minutes}:${seconds} · 建成 ${sim.buildings.filter(b=>b.complete).length} 座建筑 · 招募 ${sim.recruited} 人 · 阵亡 ${sim.casualties} 人`;
  $('result-tip').textContent=sim.won?`投入木材 ${sim.spent.wood}、矿石 ${sim.spent.ore}、果实 ${sim.spent.food} · 前哨仓库收货 ${sim.deliveredAtNewDepot}`:'建议先训练四名士兵，再带领主出征；负伤时撤回主仓库休整。';
 }
 const ready=sim.buildings.find(b=>b.complete&&b.kind==='barracks'),queue=sim.trainingCount;
 const trainingReason=!ready?'需要建成兵营':sim.livingGuards+queue>=8?'士兵已达上限':(ready.training.length>=4?'训练队列已满':sim.inventory.ore<RECRUIT.ore||sim.inventory.food<RECRUIT.food?`还缺${sim.inventory.ore<8?'矿石 '+(8-sim.inventory.ore):''}${sim.inventory.ore<8&&sim.inventory.food<12?'、':''}${sim.inventory.food<12?'果实 '+(12-sim.inventory.food):''}`:'可训练');
 $('training').textContent=`士兵 ${sim.livingGuards}/8 · 训练 ${queue}${trainingReason==='可训练'?'':' · '+trainingReason}`;
 $('recruit').toggleAttribute('disabled',!ready||sim.paused||ended||trainingReason!=='可训练');$('recruit').title=trainingReason;
 for(const button of document.querySelectorAll<HTMLButtonElement>('[data-build]')){const kind=button.dataset.build as BuildingKind;button.disabled=ended||sim.paused;button.title=`占地 2×2 格 · ${Math.max(0,COST[kind]-sim.inventory.wood)?'木材还差 '+Math.max(0,COST[kind]-sim.inventory.wood):'资源充足'}`;}
 const html=sim.buildings.map(b=>{
  const builders=[...sim.builders].filter(([,id])=>id===b.id).length;
  const refund=Math.floor(b.paid*(1-b.progress));
  return `<div class="construction-row"><span>${b.kind==='depot'?'前哨仓库':'兵营'} ${b.complete?'已完成':Math.floor(b.progress*100)+'% · '+(builders?builders+'人施工':'无人施工')}${b.kind==='depot'&&b.complete?' · 收货 '+b.delivered:''}${b.training.length?' · '+(b.training[0]===0?'出兵口受阻':Math.ceil(b.training[0])+'秒后出兵')+' · 排队 '+b.training.length:''}</span>${!b.complete?`<button data-resume="${b.id}" ${ended||sim.paused?'disabled':''}>派人施工</button><button data-cancel="${b.id}" ${ended||sim.paused?'disabled':''} title="返还木材 ${refund}">取消 · 返木${refund}</button>`:''}${b.training.length?`<button data-cancel-training="${b.id}" ${ended||sim.paused?'disabled':''}>取消末位 · 返矿8果12</button>`:''}</div>`;
 }).join('');
 if($('construction-list').innerHTML!==html){$('construction-list').innerHTML=html;
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-resume]'))b.onclick=()=>{const ids=selected.size?[...selected]:chooseBuilders();toast(sim.build(ids,Number(b.dataset.resume))?'已安排施工':'选择工人后再施工');updateUI();};
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-cancel]'))b.onclick=()=>{sim.cancel(Number(b.dataset.cancel));toast(sim.notice);updateUI();};
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-cancel-training]'))b.onclick=()=>{sim.cancelTraining(Number(b.dataset.cancelTraining));toast(sim.notice);updateUI();};
 }
 $('attack-camp').toggleAttribute('disabled',ended||sim.paused);$('retreat').toggleAttribute('disabled',ended||sim.paused);
 if(sim.notice&&sim.notice!==lastNotice){lastNotice=sim.notice;toast(sim.notice);}
}
function renderCampaign(dt:number){
 roadblockView.visible=sim.roadblock;
 if(buildMode&&buildPosition)updateGhost();
 if(sim.won||sim.lost)updateCampaignUI();
 for(const [id,v] of buildingViews)if(!sim.buildings.some(b=>b.id===id)){scene.remove(v.group);v.el.remove();buildingViews.delete(id);}
 for(const b of sim.buildings){
  let v=buildingViews.get(b.id);if(!v){const group=new THREE.Group();scene.add(group);group.position.set(b.x,0,b.z);box(group,mat('#9cae9c'),0,.08,0,2,.16,2);const body=box(group,mat(b.kind==='depot'?'#a8bfc1':'#798dab'),0,.6,0,1.7,1.1,1.7);v={group,body,el:label('source-label')};buildingViews.set(b.id,v);}
  v.body.scale.y=b.complete?1:Math.max(.1,b.progress);v.body.position.y=.1+.55*v.body.scale.y;
  v.el.innerHTML=`${b.kind==='depot'?'前哨仓库':'兵营'}<small>${b.complete?b.kind==='depot'?'已收货 '+b.delivered:'训练队列 '+b.training.length:'施工 '+Math.floor(b.progress*100)+'%'}</small>`;placeLabel(v.el,b,1.5);
 }
 for(const f of sim.fighters){
  let v=fighterViews.get(f.id);if(!v){const group=new THREE.Group(),body=new THREE.Group();group.add(body);scene.add(group);box(body,mat(f.role==='lord'?'#c49743':'#536eac'),0,.6,0,.45,.7,.4);box(body,skin,0,1.14,0,.32,.32,.32);if(sim.combatEnabled)box(body,rock,.34,.7,.05,.12,.65,.12);const ring=slotRing(f);v={group,body,ring,el:label('worker-label fighter-label')};fighterViews.set(f.id,v);}
  v.group.visible=f.hp>0;v.ring.visible=f.hp>0&&selectedFighters.has(f.id);v.ring.position.set(f.x,.025,f.z);v.el.hidden=f.hp<=0;v.group.position.set(f.x,0,f.z);v.body.rotation.y=f.face;
  v.el.classList.toggle('selected',selectedFighters.has(f.id));
  const resting=!f.attack&&distance2(f,DEPOT)<4&&f.hp<f.maxHp;
  v.el.innerHTML=`${f.role==='lord'?'领主':'兵 '+(f.id-201)}${sim.combatEnabled&&selectedFighters.has(f.id)?`<small>${Math.ceil(f.hp)}/${f.maxHp}${resting?' · 休整':f.attack?' · 进攻':''}</small>`:''}${sim.combatEnabled?`<div class="taskbar"><i style="width:${f.hp/f.maxHp*100}%;background:#70a376"></i></div>`:''}`;placeLabel(v.el,f,1.5);
 }
 for(const v of enemyViews){v.group.visible=v.e.hp>0;v.el.hidden=v.e.hp<=0;v.el.innerHTML=`守卫 ${Math.ceil(v.e.hp)}/${v.e.maxHp}`;placeLabel(v.el,v.e,1.5);}
 campGroup.visible=sim.combatEnabled&&sim.campHp>0;campLabel.innerHTML=`敌方营地<small>${sim.campHp}/${sim.campMaxHp} · 右键进攻</small>`;placeLabel(campLabel,CAMP,1.6);
}
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();sim.paused=true;toast('画面连接中断，请重置恢复');});
canvas.addEventListener('webglcontextrestored',()=>location.reload());
window.addEventListener('resize',resize);new ResizeObserver(resize).observe($('panel'));resize();updateUI();camera.updateMatrixWorld();
if(import.meta.env.DEV)(window as unknown as {__workers:unknown}).__workers={sim,selected,selectedFighters,project,scene,camera,renderer};
canvas.dataset.ready='true';requestAnimationFrame(frame);

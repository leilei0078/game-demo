import './style.css';
import * as THREE from 'three';
import { Sandbox, DEPOT, LIMIT, KIND, ACTIVITY, BAYS, workSpot, workerState, type Point, type Job, type Order } from './sim';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>('field');
const sim = new Sandbox();
const selected = new Set([1]);
const boxPreview=new Set<number>();
const stateNames={idle:'空闲',move:'移动',work:'工作',haul:'运输',wait:'等待'};
const stateColors={idle:'#8a9a9e',move:'#388ec2',work:'#5c9867',haul:'#be8b38',wait:'#b07757'};
const groupMaterials=['#3985b5','#65947a','#9575ab'].map(c=>new THREE.MeshStandardMaterial({color:c,roughness:.9}));
const groups = new Map<number,number[]>([[1,[1,2,3,4]],[2,[5,6,7,8]],[3,[9,10,11,12]]]);
$('roster').innerHTML=sim.workers.map(w=>`<button data-unit="${w.id}" aria-label="${w.id} 号"><b>${w.id}</b><span>待命</span></button>`).join('');
let mode: 'move' | 'work' | null = null;
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
function toast(text:string) { $('toast').textContent=text; $('toast').classList.add('visible'); toastLife=3.5; }
// Feedback from issued commands stays visible while routine deliveries use a quiet receipt.
let lastReceipt='';
let viewport={top:130,bottom:170,width:innerWidth,height:innerHeight-300};
function resize(){
  const top=innerWidth<700?123:115, bottom=$('panel').offsetHeight+(innerWidth<700?24:38);
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
  return {worker,source,p,isDepot};
}
function orderTarget(o:Order):Point|null{return o.type==='move'?o.point:o.type==='transport'?DEPOT:sim.sources.find(s=>s.id===o.source)??null;}
function orderText(o:Order){return o.type==='move'?`移动到 ${o.point.x.toFixed(1)}, ${o.point.z.toFixed(1)}`:o.type==='transport'?'送回仓库':`工作：${sim.sources.find(s=>s.id===o.source)?.name??'资源点'}`;}
function select(ids:number[],add=false){if(!add)selected.clear();for(const id of ids)selected.add(id); mode=null;updateUI();}
function issue(x:number,y:number,force: 'move'|'work'|null=null,append=false){
  if(sim.paused){toast('已暂停，先点击继续');return;}
  if(!selected.size){toast('先选择一个小兵');return;}
  const hit=pick(x,y),ids=[...selected];
  if(force!=='move'&&hit.source){const s=sim.sources.find(s=>s.id===hit.source)!;if(sim.work(ids,s.id,append))toast(`${ids.join('、')} 号 → ${ACTIVITY[s.activity]}${s.name}`);else toast('这里的资源已经用完');}
  else if(force==='work'){toast('点击树木、矿石、果丛或木箱');return;}
  else if(hit.isDepot){sim.transport(ids,append);toast('运送到仓库');}
  else if(hit.p&&Math.abs(hit.p.x)<=LIMIT.x&&Math.abs(hit.p.z)<=LIMIT.z){sim.move(ids,hit.p,append);marker.position.set(hit.p.x,.03,hit.p.z);marker.visible=true;markerLife=1;}
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
  if(e.button===2){issue(e.clientX,e.clientY,null,e.shiftKey);return;}
  if(e.button!==0)return;
  canvas.focus();drag={x:e.clientX,y:e.clientY,shift:e.shiftKey,id:e.pointerId};canvas.setPointerCapture(e.pointerId);
});
canvas.addEventListener('pointermove',e=>{
  hover={x:e.clientX,y:e.clientY};
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
  }else if(mode){issue(e.clientX,e.clientY,mode,e.shiftKey);}
  else {const hit=pick(e.clientX,e.clientY);if(hit.worker){if(d.shift&&selected.has(hit.worker)){selected.delete(hit.worker);updateUI();}else select([hit.worker],d.shift);}else if(hit.source||hit.isDepot){issue(e.clientX,e.clientY,null,e.shiftKey);}else select([],d.shift);}
});
canvas.addEventListener('pointercancel',()=>{drag=null;panDrag=null;boxPreview.clear();$('selection-box').hidden=true;});
canvas.addEventListener('pointerleave',()=>{hover=null;$('hover-hint').hidden=true;});
document.querySelectorAll<HTMLButtonElement>('[data-unit]').forEach(b=>b.addEventListener('click',e=>select([Number(b.dataset.unit)],(e as MouseEvent).shiftKey)));
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
$('select-waiting').onclick=()=>select(sim.workers.filter(w=>workerState(w)==='wait').map(w=>w.id));
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
document.querySelectorAll<HTMLButtonElement>('[data-order]').forEach(b=>b.onclick=()=>{panMode=false;$('pan-mode').setAttribute('aria-pressed','false');mode=b.dataset.order as 'move'|'work';toast(mode==='move'?'点击地面指定移动位置':'点击资源指定工作');updateUI();});
$('deposit').onclick=e=>{sim.transport([...selected],(e as MouseEvent).shiftKey);mode=null;updateUI();};
$('stop').onclick=()=>{sim.stop([...selected]);mode=null;updateUI();};
function pause(){sim.paused=!sim.paused;$('pause').textContent=sim.paused?'继续':'暂停';$('pause').setAttribute('aria-pressed',String(sim.paused));}
$('pause').onclick=pause;
$('reset').onclick=()=>location.reload();
window.addEventListener('keydown',e=>{
  if(e.repeat)return;
  if(e.key.toLowerCase()==='f'){const ws=sim.workers.filter(w=>selected.has(w.id));if(ws.length){viewCenter.x=ws.reduce((n,w)=>n+w.x,0)/ws.length;viewCenter.z=ws.reduce((n,w)=>n+w.z,0)/ws.length;zoom=Math.max(zoom,1.4);updateCamera();}return;}
  if(e.key.toLowerCase()==='i'){$('idle').click();return;}
  if(e.key.toLowerCase()==='m'){document.querySelector<HTMLButtonElement>('[data-order=move]')!.click();return;}
  if(e.key.toLowerCase()==='g'){document.querySelector<HTMLButtonElement>('[data-order=work]')!.click();return;}
  if(/^[1-3]$/.test(e.key)){
    const n=Number(e.key);
    if(e.ctrlKey||e.metaKey){e.preventDefault();groups.set(n,[...selected]);toast(`已保存第 ${n} 组`);}
    else select(groups.get(n)??[],e.shiftKey);
  }
  else if(e.code==='Space'){e.preventDefault();pause();}
  else if(e.key==='Escape'){mode=null;panMode=false;$('pan-mode').setAttribute('aria-pressed','false');updateUI();}
  else if(e.key.toLowerCase()==='s'){sim.stop([...selected]);mode=null;updateUI();}
  else if(e.key==='Home'){$('view-reset').click();}
  else if(e.key==='ArrowLeft'){e.preventDefault();pan(-.65/zoom,0);}
  else if(e.key==='ArrowRight'){e.preventDefault();pan(.65/zoom,0);}
  else if(e.key==='ArrowUp'){e.preventDefault();pan(0,-.65/zoom);}
  else if(e.key==='ArrowDown'){e.preventDefault();pan(0,.65/zoom);}
});
function updateUI(){
  const ids=[...selected].sort((a,b)=>a-b);$('selected-title').textContent=ids.length?(ids.length>4?`已选中 ${ids.length} 人`:`已选中 ${ids.join('、')} 号`):'未选中小兵';
  $('detail').textContent=ids.length===1?sim.status(sim.workers[ids[0]-1]):ids.length?`${ids.length} 个单位 · 指令仅影响选中单位`:'点击小兵，或拖动框选';
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-unit]')){
    const w=sim.workers[Number(b.dataset.unit)-1];b.setAttribute('aria-pressed',String(selected.has(w.id)));
    b.querySelector('span')!.textContent=stateNames[workerState(w)];
    b.dataset.phase=w.phase;b.dataset.state=workerState(w);b.title=`${w.id} 号：${sim.status(w)}`;
  }
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
    const waiting=workers.filter(w=>workerState(w)==='wait').length;
    row.querySelector('.board-info')!.textContent=`余 ${s.stock} · 工作 ${workers.filter(w=>w.phase==='work').length} · 排队 ${waiting}`;
    row.querySelector('.board-count')!.textContent=String(workers.length);
    row.querySelector<HTMLButtonElement>('[data-add-source]')!.disabled=sim.paused||!s.stock||!sim.workers.some(w=>w.phase==='idle'&&!w.cargo&&!w.queue.length);
    row.querySelector<HTMLButtonElement>('[data-remove-source]')!.disabled=sim.paused||!workers.length;
  }
  const single=ids.length===1?sim.workers[ids[0]-1]:null;
  const orderHTML=single&&single.queue.length?`<span>待执行</span>${single.queue.map((o,i)=>`<span class="order-chip">${i+1}. ${orderText(o)}</span>`).join('')}<button id="clear-queue">清空后续</button>`:'';
  $('orders').hidden=!orderHTML;
  if($('orders').innerHTML!==orderHTML){$('orders').innerHTML=orderHTML;const clear=document.getElementById('clear-queue');if(clear)clear.onclick=()=>{sim.clearQueue([...selected]);updateUI();};}
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-job]'))b.disabled=!sim.sources.some(s=>s.activity===b.dataset.job&&s.stock>0);
  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-group]')){const ids=groups.get(Number(b.dataset.group))??[];b.classList.toggle('active',ids.length===selected.size&&ids.every(id=>selected.has(id)));b.title=`${ids.join('、')} 号 · Ctrl + 点击保存当前选区`;}

  for(const b of document.querySelectorAll<HTMLButtonElement>('[data-order]'))b.classList.toggle('active',b.dataset.order===mode);
  $('hint').textContent=mode==='move'?'点击地面 → 移动到该位置 · Esc 取消':mode==='work'?'点击树木 / 矿石 / 果丛 / 待运木箱 → 自动工作、运送':'左键点选 / 框选 · 右键移动或工作 · Shift + 右键追加任务';
  canvas.style.cursor=panMode?'grab':mode?'crosshair':'default';
}
function placeLabel(el:HTMLElement,p:Point,y:number){const q=project(p,y);el.style.left=`${q.x}px`;el.style.top=`${q.y}px`;}
let prev=performance.now(),acc=0,uiTime=0;
function updateGeometry(line:THREE.Line,points:Point[]){const old=line.geometry;line.geometry=new THREE.BufferGeometry().setFromPoints(points.map(p=>new THREE.Vector3(p.x,.045,p.z)));line.computeLineDistances();old.dispose();}
function frame(now:number){
  const dt=Math.min((now-prev)/1000,.1);prev=now;acc+=dt;
  while(acc>=1/60){sim.tick(1/60);acc-=1/60;}
  uiTime+=dt;
  for(const v of workerViews){
    const {w}=v;v.group.position.set(w.x,0,w.z);v.body.rotation.y+=Math.atan2(Math.sin(w.face-v.body.rotation.y),Math.cos(w.face-v.body.rotation.y))*Math.min(1,dt*14);
    v.body.position.y=w.moving?Math.sin(sim.time*15+w.id)*.035:0;
    v.legs.forEach((leg,i)=>leg.rotation.x=w.moving?Math.sin(sim.time*15+i*Math.PI)*.42:0);
    v.arm.rotation.x=w.phase==='work'?Math.sin(sim.time*10)*.8-.4:0;
    const source=sim.sources.find(s=>s.id===w.source);
    v.tool.visible=w.phase==='work'&&(source?.activity==='chop'||source?.activity==='mine');
    v.cargo.visible=!!w.cargo;v.cargo.material=w.cargo?.kind==='ore'?rock:w.cargo?.kind==='food'?green:crate;
    v.ring.visible=selected.has(w.id)||boxPreview.has(w.id);v.path.visible=selected.has(w.id)&&w.path.length>0;
    if(v.path.visible&&uiTime>.12)updateGeometry(v.path,[w,...w.path]);
    v.destination.visible=selected.has(w.id)&&!!w.goal&&w.path.length>0&&(w.phase==='move'||selected.size<=4);
    (v.path.material as THREE.LineDashedMaterial).opacity=selected.size>4?.2:.55;
    if(w.goal)v.destination.position.set(w.goal.x,.025,w.goal.z);
    v.queuePath.visible=selected.has(w.id)&&selected.size===1&&w.queue.length>0;
    if(v.queuePath.visible&&uiTime>.12)updateGeometry(v.queuePath,[w.goal??w,...w.queue.map(orderTarget).filter((p):p is Point=>!!p)]);
    const progress=w.phase==='work'||w.phase==='unload';
    if(uiTime>.12){v.el.innerHTML=`<strong>${w.id} 号${w.continueJob?' ↻':''}</strong><span class="unit-state"> · ${sim.status(w)}</span>${w.cargo?`<span class="cargo-text">${KIND[w.cargo.kind]} ×${w.cargo.amount}</span>`:''}${progress?`<div class="taskbar"><i style="width:${w.progress*100}%"></i></div>`:''}`;v.el.style.setProperty('--state',stateColors[workerState(w)]);}
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
  if(hover&&!drag&&!panDrag&&uiTime<.02){const hit=pick(hover.x,hover.y),s=sim.sources.find(s=>s.id===hit.source);const hint=$('hover-hint');hint.hidden=!(s||hit.isDepot);if(!hint.hidden){hint.textContent=s?`${s.name} · ${s.stock>0?`右键${ACTIVITY[s.activity]} · 已分配 ${sim.workers.filter(w=>w.source===s.id).length} 人`:'已采空'}`:'仓库 · 右键送货';hint.style.left=`${Math.min(innerWidth-230,Math.max(8,hover.x+14))}px`;hint.style.top=`${hover.y+18}px`;}}
  toastLife-=dt;if(toastLife<=0)$('toast').classList.remove('visible');
  markerLife-=dt;marker.visible=markerLife>0;if(marker.visible)marker.scale.setScalar(1+(1-markerLife)*.7);
  renderer.setViewport(0,0,innerWidth,innerHeight);renderer.clear();
  renderer.setViewport(0,viewport.bottom,viewport.width,viewport.height);renderer.render(scene,camera);
  requestAnimationFrame(frame);
}
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();sim.paused=true;toast('画面连接中断，请重置恢复');});
canvas.addEventListener('webglcontextrestored',()=>location.reload());
window.addEventListener('resize',resize);new ResizeObserver(resize).observe($('panel'));resize();updateUI();camera.updateMatrixWorld();
if(import.meta.env.DEV)(window as unknown as {__workers:unknown}).__workers={sim,selected,project,scene,camera,renderer};
canvas.dataset.ready='true';requestAnimationFrame(frame);

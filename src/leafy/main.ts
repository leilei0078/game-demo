import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import '../trees/style.css';
import './style.css';

type Level={level:number;triangles:number;vertices:number;dimensions:number[];bytes:number;url:string};
type Style={id:string;title:string;alphaClip:boolean;lods:Level[]};
type Mode='compare'|'single'|'forest';
type TreeAsset={lods:THREE.Group[];height:number};
type ForestTree={position:THREE.Vector3;matrix:THREE.Matrix4;scale:number;cell:number};
type Batch={cell:number;level:number;meshes:THREE.InstancedMesh[];trees:ForestTree[]};
const $=<T extends HTMLElement=HTMLElement>(id:string)=>document.getElementById(id) as T;
const canvas=$<HTMLCanvasElement>('scene');
const viewport=$('viewport');
const renderer=new THREE.WebGLRenderer({canvas,antialias:true,alpha:false,powerPreference:'high-performance'});
renderer.setPixelRatio(Math.min(devicePixelRatio,1.6));
renderer.outputColorSpace=THREE.SRGBColorSpace;
renderer.toneMapping=THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure=1.0;
renderer.shadowMap.enabled=true;
renderer.shadowMap.type=THREE.PCFSoftShadowMap;
const scene=new THREE.Scene();
scene.background=new THREE.Color('#1e302b');
scene.fog=new THREE.Fog('#1e302b',45,130);
const camera=new THREE.PerspectiveCamera(32,1,.1,300);
const controls=new OrbitControls(camera,canvas);
controls.enableDamping=true;controls.dampingFactor=.09;
controls.minDistance=5;controls.maxDistance=180;controls.maxPolarAngle=Math.PI*.49;
controls.screenSpacePanning=true;
const hemi=new THREE.HemisphereLight('#e5f0ff','#536037',1.5);scene.add(hemi);
const sun=new THREE.DirectionalLight('#fff2ce',2.6);sun.position.set(-8,18,10);
sun.castShadow=true;sun.shadow.mapSize.set(2048,2048);
Object.assign(sun.shadow.camera,{left:-22,right:22,top:18,bottom:-18,near:1,far:80});
sun.shadow.bias=-.00025;sun.shadow.normalBias=.025;sun.shadow.radius=3;scene.add(sun);
const fill=new THREE.DirectionalLight('#bddeef',.65);fill.position.set(8,10,-8);scene.add(fill);
const floorMat=new THREE.MeshStandardMaterial({color:'#1e302b',roughness:1});
const floor=new THREE.Mesh(new THREE.PlaneGeometry(2000,2000),floorMat);
floor.rotation.x=-Math.PI/2;floor.position.y=-.15;floor.receiveShadow=true;scene.add(floor);
const display=new THREE.Group();scene.add(display);
const forest=new THREE.Group();forest.name='Instanced forest';scene.add(forest);forest.visible=false;
const platforms:THREE.Mesh[]=[];
const platformMaterial=new THREE.MeshStandardMaterial({color:'#63744f',roughness:1});
const rimMaterial=new THREE.MeshStandardMaterial({color:'#97a56b',roughness:1});
const assets:TreeAsset[]=[];let styles:Style[]=[];
let mode:Mode='single',selected=0,lodMode='auto',wireframe=false;
let trees:ForestTree[]=[],batches:Batch[]=[];
let lastStat=0,frames=0,frameRate=0,counts=[0,0,0],culled=0;
const relativeHeights=[.36,.14,.025];
const frustum=new THREE.Frustum(),projectionView=new THREE.Matrix4();
const work=new THREE.Vector3(),sphere=new THREE.Sphere();
const commonMaterials=new Map<string,THREE.MeshStandardMaterial>();
const wireMaterials=new Map<string,THREE.MeshBasicMaterial>();
const descriptions=[
  ['厚叶团簇 · 丰满圆冠','图三方向 / 标准树','不透明','分叉支撑树冠，近景实体小叶，远景合并叶簇。'],
  ['横向舒展 · 疏密错落','林缘 / 场景主树','不透明','展开的枝冠保留通透空隙，适合路边与开阔区域。'],
  ['小冠细干 · 生长变化','林间 / 幼树','不透明','统一叶形与色板，缩小树冠与树干，丰富森林层次。'],
];

function setupCards(){
  $('styles').innerHTML=styles.map((s,i)=>`<button class="style-card" data-style="${i}" aria-pressed="${i===selected}" title="${descriptions[i][3]}"><div class="card-top"><span class="card-index">0${i+1} / ${['VERDANT OAK','SPREADING OAK','YOUNG OAK'][i]}</span><span class="badge">新版</span></div><h2>${s.title}</h2><p class="card-description">${descriptions[i][0]}</p><div class="lod-table">${s.lods.map(l=>`<div class="lod-cell"><span>LOD ${l.level}</span><strong>${l.triangles.toLocaleString()}</strong></div>`).join('')}</div><div class="card-bottom"><span>三角面 / ${descriptions[i][2]}</span><span>查看 ↗</span></div></button>`).join('');
  document.querySelectorAll<HTMLButtonElement>('[data-style]').forEach(b=>b.onclick=()=>{
    selected=Number(b.dataset.style);
    if(mode==='compare')setMode('single');else if(mode==='single')layout();else{makeForest();resetCamera();}
    updateCards();
  });
}

function updateCards(){
  document.querySelectorAll<HTMLButtonElement>('[data-style]').forEach(b=>b.setAttribute('aria-pressed',String(Number(b.dataset.style)===selected)));
}

function layout(){
  assets.forEach((a,i)=>a.lods.forEach(g=>{
    g.position.set(mode==='compare'?(i-(assets.length-1)/2)*9.3:0,0,0);
    g.visible=mode==='compare'||i===selected;
  }));
  platforms.forEach((p,i)=>{
    p.position.set(mode==='compare'?(i-(assets.length-1)/2)*9.3:0,-.075,0);
    p.visible=mode==='compare'||i===selected;
  });
  resetCamera();
}

function resetCamera(){
  const aspect=viewport.clientWidth/viewport.clientHeight;
  camera.aspect=aspect;camera.updateProjectionMatrix();
  if(mode==='forest'){
    controls.target.set(0,3,0);camera.position.set(35,29,46);controls.minDistance=8;
    sun.position.set(-15,38,22);Object.assign(sun.shadow.camera,{left:-44,right:44,top:44,bottom:-44,far:110});
  }else{
    const rows=mode==='compare'&&aspect<1.15;
    if(rows){
      assets.forEach((a,i)=>a.lods.forEach(g=>g.position.set((i%2-.5)*9.3,0,(Math.floor(i/2)-(Math.ceil(assets.length/2)-1)/2)*12.5)));
      platforms.forEach((p,i)=>p.position.set((i%2-.5)*9.3,-.075,(Math.floor(i/2)-(Math.ceil(assets.length/2)-1)/2)*12.5));
      controls.target.set(0,3.4,0);camera.position.set(0,48,32);
      const dist=Math.max(18/aspect,43)/(2*Math.tan(THREE.MathUtils.degToRad(16)));
      camera.position.sub(controls.target).setLength(dist).add(controls.target);
    }else{
      const viewHeight=Math.max(11.5,(mode==='compare'?assets.length*9.3+2.6:9.5)/aspect);
      const distance=viewHeight/(2*Math.tan(THREE.MathUtils.degToRad(16)));
      const targetHeight=mode==='single'?assets[selected].height*.48:3.8;
      controls.target.set(0,targetHeight,0);
      camera.position.set(0,targetHeight+distance*.16,distance);
    }
    controls.minDistance=5;
    sun.position.set(-8,18,10);Object.assign(sun.shadow.camera,{left:-22,right:22,top:18,bottom:-18,near:1,far:80});
  }
  sun.shadow.camera.updateProjectionMatrix();sun.shadow.needsUpdate=true;
  controls.update();
}

function setMode(next:Mode){
  mode=next;display.visible=mode!=='forest';forest.visible=mode==='forest';
  $('forest-options').hidden=mode!=='forest';
  $('stage-note').textContent=mode==='compare'?`01 — 0${styles.length} / 统一比例 · 统一光照`:mode==='single'?`${styles[selected].title} / 单树细看`:`${styles[selected].title} / 实例化森林`;
  document.querySelectorAll<HTMLButtonElement>('[data-mode]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.mode===mode)));
  if(mode==='forest'){makeForest();resetCamera();}else layout();
  updateCards();
}

function chooseLOD(height:number,position:THREE.Vector3,scale=1){
  if(lodMode!=='auto')return Number(lodMode);
  const distance=camera.position.distanceTo(work.copy(position).addScaledVector(THREE.Object3D.DEFAULT_UP,height*scale*.5));
  const relative=height*scale/(2*Math.tan(THREE.MathUtils.degToRad(camera.fov/2))*Math.max(distance,.01));
  return relative>relativeHeights[0]?0:relative>relativeHeights[1]?1:relative>relativeHeights[2]?2:-1;
}

function makeForest(){
  for(const b of batches)for(const mesh of b.meshes){forest.remove(mesh);mesh.dispose();}
  batches=[];trees=[];
  let seed=45;const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
  const count=Number($<HTMLSelectElement>('tree-count').value),grid=Math.ceil(Math.sqrt(count));
  const dummy=new THREE.Object3D();
  for(let i=0;i<count;i++){
    const x=(i%grid-(grid-1)/2)*5.9+(random()-.5)*2.4;
    const z=(Math.floor(i/grid)-(grid-1)/2)*5.9+(random()-.5)*2.4;
    dummy.position.set(x,0,z);dummy.rotation.set(0,random()*Math.PI*2,0);const s=.78+random()*.35;dummy.scale.setScalar(s);dummy.updateMatrix();
    const cell=Math.floor((i%grid)/4)+Math.floor(Math.floor(i/grid)/4)*Math.ceil(grid/4);
    trees.push({position:dummy.position.clone(),matrix:dummy.matrix.clone(),scale:s,cell});
  }
  const cells=[...new Set(trees.map(t=>t.cell))];
  for(const cell of cells)for(let level=0;level<3;level++){
    const parts:THREE.Mesh[]=[];assets[selected].lods[level].traverse(o=>{if(o instanceof THREE.Mesh)parts.push(o);});
    const cellTrees=trees.filter(t=>t.cell===cell);
    const meshes=parts.map(p=>{
      const mesh=new THREE.InstancedMesh(p.geometry,p.material,cellTrees.length);mesh.name=`${styles[selected].id} cell ${cell} LOD ${level} ${p.name}`;
      mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);mesh.castShadow=level===0;mesh.receiveShadow=true;mesh.count=0;forest.add(mesh);return mesh;
    });
    batches.push({cell,level,meshes,trees:cellTrees});
  }
  setWire(wireframe);
}

function setWire(value:boolean){
  wireframe=value;$('wire').setAttribute('aria-pressed',String(value));
  scene.traverse(o=>{
    if(!(o instanceof THREE.Mesh)||o===floor||platforms.includes(o))return;
    const mat=(Array.isArray(o.material)?o.material[0]:o.material) as THREE.Material;
    const name=mat.userData.originalName||mat.name;
    if(!commonMaterials.has(name))return;
    if(value){
      let wm=wireMaterials.get(name);
      if(!wm){wm=new THREE.MeshBasicMaterial({color:name.includes('Bark')?'#8d7253':'#497149',wireframe:true});wm.name=`Wire / ${name}`;wm.userData.originalName=name;wireMaterials.set(name,wm);}
      o.material=wm;
    }else o.material=commonMaterials.get(name)!;
  });
}

function updateLOD(){
  counts=[0,0,0];culled=0;
  if(mode==='forest'){
    camera.updateMatrixWorld();projectionView.multiplyMatrices(camera.projectionMatrix,camera.matrixWorldInverse);frustum.setFromProjectionMatrix(projectionView);
    const height=assets[selected].height;
    const assignments=new Map<ForestTree,number>();
    for(const tree of trees){
      sphere.center.copy(tree.position).y=height*tree.scale*.5;sphere.radius=height*tree.scale*.68;
      const level=frustum.intersectsSphere(sphere)?chooseLOD(height,tree.position,tree.scale):-1;
      assignments.set(tree,level);if(level>=0)counts[level]++;else culled++;
    }
    for(const batch of batches){
      let n=0;
      for(const t of batch.trees)if(assignments.get(t)===batch.level){for(const mesh of batch.meshes)mesh.setMatrixAt(n,t.matrix);n++;}
      for(const mesh of batch.meshes){mesh.count=n;mesh.visible=n>0;mesh.instanceMatrix.needsUpdate=true;if(n){mesh.computeBoundingSphere();}}
    }
  }else{
    assets.forEach((a,i)=>{
      const active=mode==='compare'||i===selected;
      const wanted=chooseLOD(a.height,a.lods[0].position);
      const level=active?(wanted<0?2:wanted):-1;
      a.lods.forEach((g,l)=>g.visible=active&&level===l);
      if(active)counts[level]++;
    });
  }
}

function resize(){
  renderer.setSize(viewport.clientWidth,viewport.clientHeight,false);
  resetCamera();
}

async function start(){
  const manifest=await (await fetch('/models/leafy-trees/manifest.json')).json();
  styles=manifest.styles;
  const loader=new GLTFLoader();
  const bundle=await loader.loadAsync(manifest.bundle.url);
  bundle.scene.updateMatrixWorld(true);
  const loaded=await Promise.all(styles.map(async s=>{
    const levels=await Promise.all(s.lods.map(async l=>{
      const root=bundle.scene.getObjectByName(`${s.id}_LOD${l.level}`);
      if(!root)throw new Error(`Missing asset ${s.id} LOD ${l.level}`);
      // Bake GLB transforms into local geometry once; instances then use grounded roots.
      const group=new THREE.Group();group.name=`${s.id} LOD ${l.level}`;
      root.traverse(o=>{
        if(!(o instanceof THREE.Mesh))return;
        const mat=o.material as THREE.MeshStandardMaterial;
        let shared=commonMaterials.get(mat.name);
        if(!shared){shared=mat;commonMaterials.set(mat.name,shared);if(shared.map)shared.map.anisotropy=Math.min(8,renderer.capabilities.getMaxAnisotropy());}
        const geometry=o.geometry.clone().applyMatrix4(o.matrixWorld);
        const mesh=new THREE.Mesh(geometry,shared);mesh.name=o.name;mesh.castShadow=true;mesh.receiveShadow=true;group.add(mesh);
      });
      return group;
    }));
    return {lods:levels,height:s.lods[0].dimensions[2]};
  }));
  assets.push(...loaded);
  assets.forEach((a,i)=>{
    a.lods.forEach(g=>display.add(g));
    const radius=i===2?2.7:3.8;
    const platform=new THREE.Mesh(new THREE.CylinderGeometry(radius,radius+.04,.15,64),platformMaterial);
    platform.name=`Platform ${i}`;platform.position.y=-.075;platform.receiveShadow=true;display.add(platform);platforms.push(platform);
    const rim=new THREE.Mesh(new THREE.TorusGeometry(radius+.14,.007,4,96),rimMaterial);rim.rotation.x=Math.PI/2;rim.position.y=.078;platform.add(rim);
  });
  setupCards();
  document.querySelectorAll<HTMLButtonElement>('[data-mode]').forEach(b=>b.onclick=()=>setMode(b.dataset.mode as Mode));
  $<HTMLSelectElement>('lod').onchange=e=>{lodMode=(e.target as HTMLSelectElement).value;};
  $('wire').onclick=()=>setWire(!wireframe);
  $('reset').onclick=resetCamera;
  $<HTMLSelectElement>('tree-count').onchange=()=>{makeForest();resetCamera();};
  new ResizeObserver(resize).observe(viewport);
  resize();setMode('single');updateLOD();renderer.render(scene,camera);
  $('loading').hidden=true;canvas.dataset.ready='true';
  // Expose read-only scene handles plus explicit controls for QA of real exported assets.
  (window as unknown as {__trees:unknown}).__trees={renderer,scene,camera,controls,assets,styles,get mode(){return mode;},get selected(){return selected;},get counts(){return counts;},get culled(){return culled;}};
  renderer.setAnimationLoop((time:number)=>{
    controls.update();updateLOD();renderer.render(scene,camera);frames++;
    if(time-lastStat>600){
      frameRate=Math.round(frames*1000/Math.max(time-lastStat,1));frames=0;lastStat=time;
      $('live-stats').textContent=`${renderer.info.render.triangles.toLocaleString()} tris · ${renderer.info.render.calls} draws · ${frameRate} FPS`;
      $('lod-distribution').textContent=`LOD 0 / 1 / 2：${counts.join(' / ')} · 剔除 ${culled}`;
      if(mode==='single')$('stage-note').textContent=`${styles[selected].title} / ${lodMode==='auto'?'自动 · ':''}LOD ${counts.findIndex(n=>n>0)}`;
    }
  });
}

start().catch(error=>{
  console.error(error);$('loading').hidden=true;$('load-error').hidden=false;
  $('load-error').textContent='模型加载失败，请刷新页面重试。';
});

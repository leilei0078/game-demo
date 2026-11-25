import '../style.css';
import './style.css';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { mergeGeometries, mergeVertices } from 'three/addons/utils/BufferGeometryUtils.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { ForestOcclusionPass as SSAOPass } from './occlusion';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { SMAAPass } from 'three/addons/postprocessing/SMAAPass.js';
import { createMistwoodLandscape as createLandscape } from './landscape';
import { createMistwoodMist as createMist } from './atmosphere';
import { addMistwoodForest as addForest } from './forest';
import { addLeanTo } from './structures';
import { weatherMaterial, addGrainUV } from './materials';
import { finishWindowGlass } from '../windows';
import { roadDistance } from '../roads';
import { createCampfire } from '../fire';
import { scenePalette } from './palette';
import { createColorOutput } from './color-output';

const $ = <T extends HTMLElement = HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const canvas = $<HTMLCanvasElement>('#scene');
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const state = { ready: false, night: false, nightAmount: 0, orbit: false, selected: '', view: 'wide' };
const scene = new THREE.Scene();
scene.background = new THREE.Color(scenePalette.background.day);
scene.fog = new THREE.Fog(scenePalette.background.day, 35, 85);
const camera = new THREE.OrthographicCamera(-10, 10, 8, -8, .1, 100);
camera.position.set(11.5, 10.5, 19.1);
const pickable: THREE.Object3D[] = [];
const windowMaterials = new Set<THREE.MeshStandardMaterial>();
let renderer: THREE.WebGLRenderer;

function fail(message: string) {
  $('#error-message').textContent = message;
  $('#error').hidden = false;
  $('#loading').hidden = true;
}
$('#retry').addEventListener('click', () => location.reload());

try {
  renderer = new THREE.WebGLRenderer({ canvas, antialias: false, powerPreference: 'high-performance' });
} catch {
  fail('请使用支持 WebGL 2 的浏览器，并确认浏览器的硬件加速已开启。');
  throw new Error('WebGL 2 unavailable');
}
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.6));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.AgXToneMapping;
renderer.toneMappingExposure = scenePalette.exposure;
renderer.info.autoReset = false;
const landscape = createLandscape(renderer);
const controls = new OrbitControls(camera, canvas);
controls.target.set(-1.5, 1.7, 1.1);
controls.enableDamping = true;
controls.dampingFactor = .065;
controls.minZoom = .70;
controls.maxZoom = 3.4;
controls.minPolarAngle = .22;
controls.maxPolarAngle = Math.PI / 2 - .12;
controls.rotateSpeed = .6;
controls.zoomSpeed = .8;
controls.panSpeed = .65;
controls.autoRotateSpeed = .5;
controls.update();

const hemi = new THREE.HemisphereLight(scenePalette.sky.color, scenePalette.sky.ground, scenePalette.sky.dayIntensity);
scene.add(hemi);
const key = new THREE.DirectionalLight(scenePalette.sunlight.day, scenePalette.sunlight.dayIntensity);
key.position.set(-8, 18, -9);
key.castShadow = true;
key.shadow.mapSize.set(2048, 2048);
Object.assign(key.shadow.camera, { left: -10, right: 10, top: 10, bottom: -10, near: .5, far: 65 });
key.shadow.normalBias = .018;
key.shadow.bias = -.00015;
key.shadow.radius = 4;
key.shadow.blurSamples = 8;
scene.add(key);
const fill = new THREE.DirectionalLight(scenePalette.fill.color, scenePalette.fill.dayIntensity);
fill.position.set(8, 6, -4);
scene.add(fill);
const fireLight = new THREE.PointLight('#ff8d37', 15, 6, 2);
fireLight.position.set(1, 1.05, .74);
scene.add(fireLight);
const porchLight = new THREE.PointLight('#ffba65', 0, 5, 2);
porchLight.position.set(-2.65, 1.95, .45);
scene.add(porchLight);
const forestAudit=addForest(scene,landscape);
addLeanTo(scene);

const composer = new EffectComposer(renderer);
composer.addPass(new RenderPass(scene, camera));
const ao = new SSAOPass(scene, camera, 1, 1);
// SSAOPass defaults to perspective depth even when given an orthographic camera.
ao.ssaoMaterial.defines.PERSPECTIVE_CAMERA = 0;
ao.depthRenderMaterial.defines.PERSPECTIVE_CAMERA = 0;
ao.kernelRadius = .32;
ao.minDistance = .001;
ao.maxDistance = .12;
composer.addPass(ao);
const campfire = createCampfire(camera, ao.normalRenderTarget.depthTexture!);
scene.add(campfire.sparks);
campfire.pass.material.fragmentShader=campfire.pass.material.fragmentShader.replace('vec3 q=p;','vec3 q=p; q.y*=1.22;');
composer.addPass(campfire.pass);
const mist=createMist(camera,ao.normalRenderTarget.depthTexture!);
composer.addPass(mist.pass);
const bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), .28, .52, 1.2);
composer.addPass(bloom);
composer.addPass(new SMAAPass());
const colorOutput = createColorOutput();
composer.addPass(colorOutput.pass);
if (import.meta.env.DEV) Object.assign(window, { __wayfarer: { ao, composer, camera, controls, renderer, scene, forestAudit } });

const presets: Record<string, { target: THREE.Vector3; offset: THREE.Vector3; zoom: number }> = {
  wide: { target: new THREE.Vector3(-1.5, 1.7, 1.1), offset: new THREE.Vector3(13, 8.8, 18), zoom: 1 },
  cottage: { target: new THREE.Vector3(-2.35, 2, -1.1), offset: new THREE.Vector3(12, 9, 18), zoom: 1.65 },
  camp: { target: new THREE.Vector3(.75, .8, 1.35), offset: new THREE.Vector3(10, 12, 18), zoom: 1.9 },
};
let transition: { start: number; from: THREE.Vector3; to: THREE.Vector3; fromTarget: THREE.Vector3; target: THREE.Vector3; zoom: number; fromZoom: number } | null = null;

function setOrbit(value: boolean) {
  state.orbit = value;
  controls.autoRotate = value;
  $('#orbit').setAttribute('aria-pressed', String(value));
}
function setView(name: string) {
  if (!state.ready) return;
  const p = presets[name];
  if (!p) return;
  setOrbit(false);
  state.view = name;
  document.querySelectorAll<HTMLButtonElement>('[data-view]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.view === name)));
  if (reducedMotion.matches) {
    controls.target.copy(p.target);
    camera.position.copy(p.target).add(p.offset);
    camera.zoom = p.zoom;
    camera.updateProjectionMatrix();
    controls.update();
    return;
  }
  transition = { start: performance.now(), from: camera.position.clone(), to: p.target.clone().add(p.offset),
    fromTarget: controls.target.clone(), target: p.target.clone(), zoom: p.zoom, fromZoom: camera.zoom };
}
function setNight(value: boolean) {
  state.night = value;
  $('#daylight').setAttribute('aria-pressed', String(value));
  $('#daylight span').textContent = value ? '日间' : '入夜';
  $('#time-label').textContent = value ? '密林 · 夜色' : '密林 · 晨雾';
}
document.querySelectorAll<HTMLButtonElement>('[data-view]').forEach(b => b.addEventListener('click', () => setView(b.dataset.view!)));
$('#reset').addEventListener('click', () => { closeDetail(); setView('wide'); });
$('#orbit').addEventListener('click', () => { if (state.ready) setOrbit(!state.orbit); });
$('#daylight').addEventListener('click', () => setNight(!state.night));
$('#help-toggle').addEventListener('click', () => {
  $('#help').hidden = !$('#help').hidden;
  $('#help-toggle').setAttribute('aria-expanded', String(!$('#help').hidden));
});

const descriptions: Record<string, { label: string; title: string; description: string }> = {
  cottage: { label: '01 / THE WAYFARER’S REST', title: '旅人的落脚处', description: '雨水浸进旧木梁与瓦片，苔藓爬上墙脚。高树和冷雾围住小屋，窗边的一盏暖灯仍为旅人留着。' },
  camp: { label: '02 / A MOMENT BY THE FIRE', title: '篝火旁的片刻', description: '火光照亮潮湿的石头和落叶。坐下来听柴火与溪水的声音，等待林间的雾慢慢散去。' },
  bridge: { label: '03 / ACROSS THE CREEK', title: '溪上的小木桥', description: '几块旧木板跨过碧绿的溪水。沿着林间小路走，远处的风景还在等着。' },
  woodland: { label: '04 / THE QUIET WOODLAND', title: '松林与野花', description: '松树围出一小片安静的空地。石缝间长着苔藓，路边的野花也有自己的季节。' },
  supplies: { label: '05 / READY FOR THE ROAD', title: '出发前的准备', description: '木桶、货箱，还有新劈好的柴。驿站里的日常，藏在这些不起眼的小东西里。' },
};
function closeDetail() { state.selected = ''; $('#detail').hidden = true; }
$('#detail-close').addEventListener('click', closeDetail);
function showDetail(region: string) {
  const info = descriptions[region];
  if (!info) { closeDetail(); return; }
  state.selected = region;
  $('#detail-label').textContent = info.label;
  $('#detail-title').textContent = info.title;
  $('#detail-description').textContent = info.description;
  $('#detail').hidden = false;
}
window.addEventListener('keydown', e => {
  if (e.repeat || e.ctrlKey || e.metaKey || e.altKey || /INPUT|TEXTAREA|SELECT/.test((e.target as HTMLElement)?.tagName)) return;
  if (e.key === '1') setView('wide');
  if (e.key === '2') setView('cottage');
  if (e.key === '3') setView('camp');
  if (e.key.toLowerCase() === 'r') { closeDetail(); setView('wide'); }
  if (e.key.toLowerCase() === 'n') setNight(!state.night);
  if (e.key === 'Escape') { closeDetail(); $('#help').hidden = true; $('#help-toggle').setAttribute('aria-expanded','false'); }
});

// Batch static meshes by material and semantic region. Keep moving parts separate.
function prepareModel(root: THREE.Group) {
  root.updateMatrixWorld(true);
  const batches = new Map<string, { geometries: THREE.BufferGeometry[]; material: THREE.Material; region: string }>();
  const model = new THREE.Group();
  model.name = 'Wayfarers Rest';
  const sourceGeometries = new Set<THREE.BufferGeometry>();
  root.traverse(obj => {
    if (!(obj instanceof THREE.Mesh)) return;
    let owner: THREE.Object3D | null = obj;
    while (owner && !owner.userData.region) owner = owner.parent;
    const region = owner?.userData.region || 'terrain';
    const type = obj.userData.animated || owner?.userData.animated;
    let material = obj.material as THREE.MeshStandardMaterial;
    sourceGeometries.add(obj.geometry);
    // Remove the old rigid white bars; the water shader supplies moving highlights.
    if (type === 'water' || type === 'flame' || type === 'ember') return;
    // The bridge-tagged legacy road missed the previous terrain-only material override.
    // Both ribbons now become part of the continuous ground surface.
    if (material.name === 'path') return;
    if (material.name==='soil' && region==='camp') material=weatherMaterial(material);
    const cleanName=obj.name.replaceAll('_',' ');
    if (/Angular pine trunk|Layered faceted pine crown|Meadow grass|Wildflower|Flat moss cap|Low wild shrub|Exposed earthen|Island underside/i.test(cleanName)) return;
    if (region==='camp' && /Adventurer|Traveller|Tunic|Helmet|Helmed|Shield|Sword|Leather belt|Leather glove|Belt buckle|Chunky shoulder|Dark helmet|Rolled travel|Bedroll/.test(cleanName)) return;
    if (region==='woodland' && ['bush','flower','flower_white','grass','grass_light','grass_dark'].includes(material.name)) return;
    if (!material.name.startsWith('M_Window_Glass_') && material.name!=='M_Window_Linen' &&
        !['water','soil','grass','grass_light','grass_dark'].includes(material.name)) material=weatherMaterial(material);
    const objectName=obj.name.replaceAll('_',' ');
    if(objectName.startsWith('Meadow grass blade') || objectName.startsWith('Wildflower center')) {
      obj.geometry.computeBoundingBox();
      const center=obj.geometry.boundingBox!.getCenter(new THREE.Vector3()).applyMatrix4(obj.matrixWorld);
      if(roadDistance(center.x,center.z)<.07) return;
    }
    if (obj.name.startsWith('Triangulated mossy clearing') ||
        (region === 'terrain' && ['grass','grass_light','grass_dark'].includes(material.name))) material = landscape.grass;
    if (material.name === 'water') material = landscape.water;
    if (region === 'terrain' && material.name === 'soil') material = landscape.bank;
    if (material.name.startsWith('M_Window_Glass_') || material.name === 'M_Window_Linen') {
      const pane = material.name.startsWith('M_Window_Glass_');
      windowMaterials.add(material);
      material.emissive.setRGB(.83,.37,.085);
      material.userData.windowNightIntensity = pane ? .90 + Number(material.name.slice(-1))*.09 : .23;
      material.emissiveIntensity = pane ? .48 : .08;
      if (pane) {
        material.envMap = landscape.water.envMap;
        material.envMapIntensity = .65;
        finishWindowGlass(material);
      }
    }
    const roundRock=/Mossy granite boulder|Ring of hearth stones/.test(cleanName);
    let geometry:THREE.BufferGeometry;
    if(roundRock) {
      obj.geometry.computeBoundingBox();
      const box=obj.geometry.boundingBox!.clone().applyMatrix4(obj.matrixWorld);
      const size=box.getSize(new THREE.Vector3()).multiplyScalar(.5),center=box.getCenter(new THREE.Vector3());
      geometry=new THREE.IcosahedronGeometry(1,2);
      const p=geometry.attributes.position;
      for(let i=0;i<p.count;i++) {
        const x=p.getX(i),y=p.getY(i),z=p.getZ(i);
        const r=1+.055*Math.sin(x*9+z*4)*Math.sin(y*8-z*3);
        p.setXYZ(i,x*r*size.x+center.x,y*r*size.y+center.y,z*r*size.z+center.z);
      }
      geometry.deleteAttribute('uv');
      const welded=mergeVertices(geometry,.00001);
      geometry.dispose();welded.computeVertexNormals();
      geometry=welded.toNonIndexed();welded.dispose();
    } else geometry=obj.geometry.index ? obj.geometry.toNonIndexed() : obj.geometry.clone();
    if (!material.name.startsWith('M_Window_Glass_')) addGrainUV(geometry,roundRock?new THREE.Matrix4():obj.matrixWorld);
    const kept = ['position','normal','uv'];
    for (const attribute of Object.keys(geometry.attributes)) if (!kept.includes(attribute)) geometry.deleteAttribute(attribute);
    if(!roundRock) geometry.applyMatrix4(obj.matrixWorld);
    if (!geometry.getAttribute('normal')) geometry.computeVertexNormals();
    const key = region + material.uuid;
    if (!batches.has(key)) batches.set(key, { geometries: [], material, region });
    batches.get(key)!.geometries.push(geometry);
  });
  for (const { geometries, material, region } of batches.values()) {
    const merged = mergeGeometries(geometries, false);
    if (!merged) throw new Error('Could not batch scene geometry');
    merged.computeBoundingSphere();
    const mesh = new THREE.Mesh(merged, material);
    mesh.name = region + ' / ' + material.name;
    mesh.userData.region = region;
    mesh.castShadow = material !== landscape.water && !material.name.startsWith('M_Window_Glass_');
    mesh.receiveShadow = true;
    model.add(mesh);
    pickable.push(mesh);
    geometries.forEach(g => g.dispose());
  }
  sourceGeometries.forEach(g => g.dispose());
  scene.add(model);
}

new GLTFLoader().load(import.meta.env.BASE_URL + 'models/wayfarers-rest.glb?v=inset-windows-5', gltf => {
  try {
    prepareModel(gltf.scene);
    state.ready = true;
    $('#loading-bar').style.width = '100%';
    $('#loading-status').textContent = '欢迎来到林间驿站';
    $('#loading').classList.add('done');
    setTimeout(() => { $('#loading').hidden = true; }, 700);
    canvas.dataset.ready = 'true';
  } catch (error) {
    console.error(error);
    fail('场景没有完整加载，请刷新页面再试一次。');
  }
}, event => {
  if (event.total > 0) {
    const progress = Math.round(event.loaded / event.total * 90);
    $('#loading-bar').style.width = `${progress}%`;
    $('#loading-status').textContent = `走进森林 ${progress}%`;
  }
}, error => {
  console.error(error);
  fail('场景资源加载失败。请检查连接后重新加载。');
});

const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
function regionAt(x: number, y: number) {
  const rect = canvas.getBoundingClientRect();
  pointer.set((x - rect.left) / rect.width * 2 - 1, -(y - rect.top) / rect.height * 2 + 1);
  raycaster.setFromCamera(pointer, camera);
  return raycaster.intersectObjects(pickable, false)[0]?.object.userData.region || '';
}
let down: { x: number; y: number; id: number; multi: boolean } | null = null;
const pointers = new Set<number>();
canvas.addEventListener('pointerdown', e => {
  pointers.add(e.pointerId);
  if (pointers.size === 1) down = { x: e.clientX, y: e.clientY, id: e.pointerId, multi: false };
  else if (down) down.multi = true;
  $('#hover-label').hidden = true;
});
window.addEventListener('pointerup', e => {
  pointers.delete(e.pointerId);
  if (down?.id === e.pointerId && !down.multi && e.button === 0 && Math.hypot(e.clientX-down.x,e.clientY-down.y) < 6 && e.target === canvas && state.ready) showDetail(regionAt(e.clientX,e.clientY));
  if (pointers.size === 0) { down = null; canvas.classList.remove('dragging'); }
});
canvas.addEventListener('pointercancel', () => { down = null; pointers.clear(); canvas.classList.remove('dragging'); });
canvas.addEventListener('pointermove', e => {
  if (down) {
    if (Math.hypot(e.clientX-down.x,e.clientY-down.y)>6) canvas.classList.add('dragging');
    return;
  }
  if (e.pointerType === 'touch' || !state.ready) return;
  const info = descriptions[regionAt(e.clientX, e.clientY)];
  canvas.style.cursor = info ? 'pointer' : 'grab';
  const label = $('#hover-label');
  label.hidden = !info;
  if (info) {
    label.textContent = info.title;
    label.style.left = `${Math.min(e.clientX + 16, window.innerWidth - 150)}px`;
    label.style.top = `${Math.min(e.clientY + 18, window.innerHeight - 45)}px`;
  }
});
canvas.addEventListener('pointerleave', () => { $('#hover-label').hidden = true; });
controls.addEventListener('start', () => { transition = null; setOrbit(false); $('#hover-label').hidden = true; });

function resize() {
  const width = canvas.clientWidth, height = canvas.clientHeight;
  const aspect = width / height;
  const frustumHeight = aspect < 1 ? 20.5 : 16.8;
  camera.left = -frustumHeight * aspect / 2;
  camera.right = frustumHeight * aspect / 2;
  camera.top = frustumHeight / 2;
  camera.bottom = -frustumHeight / 2;
  camera.updateProjectionMatrix();
  renderer.setSize(width, height, false);
  composer.setSize(width, height);
}
window.addEventListener('resize', resize);
resize();

const clock = new THREE.Clock();
const dayBackground = new THREE.Color(scenePalette.background.day), nightBackground = new THREE.Color(scenePalette.background.night);
const dayKey = new THREE.Color(scenePalette.sunlight.day), nightKey = new THREE.Color(scenePalette.sunlight.night);
let contextLost = false;
canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); contextLost = true; fail('图形连接暂时中断，请重新加载场景。'); });
canvas.addEventListener('webglcontextrestored', () => location.reload());

renderer.setAnimationLoop(() => {
  if (contextLost || document.hidden) { clock.getDelta(); return; }
  const dt = Math.min(clock.getDelta(), .05);
  const t = clock.elapsedTime;
  if (transition) {
    const p = Math.min((performance.now() - transition.start) / 1050, 1);
    const ease = p * p * (3 - 2 * p);
    camera.position.lerpVectors(transition.from, transition.to, ease);
    controls.target.lerpVectors(transition.fromTarget, transition.target, ease);
    camera.zoom = THREE.MathUtils.lerp(transition.fromZoom, transition.zoom, ease);
    camera.updateProjectionMatrix();
    if (p === 1) transition = null;
  }
  controls.update(dt);
  // Keep panning near the island, while preserving camera-to-target direction.
  const old = controls.target.clone();
  controls.target.x = THREE.MathUtils.clamp(controls.target.x, -5, 5);
  controls.target.z = THREE.MathUtils.clamp(controls.target.z, -4, 4);
  controls.target.y = THREE.MathUtils.clamp(controls.target.y, .1, 3);
  camera.position.add(controls.target.clone().sub(old));
  state.nightAmount = THREE.MathUtils.damp(state.nightAmount, state.night ? 1 : 0, reducedMotion.matches ? 100 : 3, dt);
  const n = state.nightAmount;
  landscape.update(reducedMotion.matches ? 0 : t,n);
  mist.update(reducedMotion.matches ? 0 : t,n);
  campfire.update(reducedMotion.matches ? 0 : t);
  colorOutput.update(n);
  (scene.background as THREE.Color).lerpColors(dayBackground, nightBackground, n);
  (scene.fog as THREE.Fog).color.copy(scene.background as THREE.Color);
  key.color.lerpColors(dayKey, nightKey, n);
  key.intensity = THREE.MathUtils.lerp(scenePalette.sunlight.dayIntensity, scenePalette.sunlight.nightIntensity, n);
  hemi.intensity = THREE.MathUtils.lerp(scenePalette.sky.dayIntensity, scenePalette.sky.nightIntensity, n);
  fill.intensity = THREE.MathUtils.lerp(scenePalette.fill.dayIntensity, scenePalette.fill.nightIntensity, n);
  windowMaterials.forEach(m => {
    const pane = m.name.startsWith('M_Window_Glass_');
    m.emissiveIntensity = THREE.MathUtils.lerp(pane ? .48 : .08, m.userData.windowNightIntensity, n);
    if (pane) m.envMapIntensity = THREE.MathUtils.lerp(.65,.18,n);
  });
  porchLight.intensity = 7 + n * 6;
  fireLight.intensity = (reducedMotion.matches ? 6.5 : 6.5 + Math.sin(t*7)*.55 + Math.sin(t*11)*.35) * (1+n*.45);
  // Orbit zoom changes projection without resizing the postprocessing targets.
  ao.ssaoMaterial.uniforms.cameraProjectionMatrix.value.copy(camera.projectionMatrix);
  ao.ssaoMaterial.uniforms.cameraInverseProjectionMatrix.value.copy(camera.projectionMatrixInverse);
  renderer.info.reset();
  composer.render(dt);
});

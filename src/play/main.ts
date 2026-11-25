import './style.css';
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { SSAOPass } from 'three/addons/postprocessing/SSAOPass.js';
import { SMAAPass } from 'three/addons/postprocessing/SMAAPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { createLandscape } from '../landscape';
import { applyScenePalette } from '../palette';
import { newState, suppliesReady, findPath, passable, riverX, distance, CAMP, GOAL, type Point, type Resource, type Boar } from './simulation';
import { createPhysics } from './physics';

const $ = <T extends HTMLElement = HTMLElement>(s: string) => document.querySelector<T>(s)!;
const canvas = $<HTMLCanvasElement>('#game');
const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
const mobile = matchMedia('(max-width: 600px)').matches;
const scene = new THREE.Scene();
scene.background = new THREE.Color('#a2b7a3');
scene.fog = new THREE.Fog('#a2b7a3', 28, 66);
const camera = new THREE.OrthographicCamera(-13, 13, 9, -9, .1, 110);
const cameraOffset = new THREE.Vector3(15, 21, 23);
const focus = new THREE.Vector3(0, .7, 1);
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, mobile ? 1.4 : 1.65));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.08;
renderer.outputColorSpace = THREE.SRGBColorSpace;
const composer = new EffectComposer(renderer);
composer.addPass(new RenderPass(scene, camera));
const ao = new SSAOPass(scene, camera, 1, 1, 12);
ao.ssaoMaterial.defines.PERSPECTIVE_CAMERA = 0;
ao.depthRenderMaterial.defines.PERSPECTIVE_CAMERA = 0;
ao.kernelRadius = .38; ao.minDistance = .001; ao.maxDistance = .15;
if (!mobile) composer.addPass(ao);
composer.addPass(new SMAAPass());
composer.addPass(new OutputPass());
scene.add(new THREE.HemisphereLight('#dce9cb', '#59644a', 2.0));
const sun = new THREE.DirectionalLight('#ffdfaa', 3.25);
sun.position.set(-10, 20, 12);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
Object.assign(sun.shadow.camera, { left: -22, right: 22, top: 22, bottom: -22, near: .5, far: 70 });
sun.shadow.normalBias = .035; sun.shadow.bias = -.0001; sun.shadow.radius = 2;
scene.add(sun, sun.target);
const landscape = createLandscape(renderer);
const ground = new THREE.Mesh(new THREE.PlaneGeometry(130, 130), landscape.grass);
ground.rotation.x = -Math.PI / 2; ground.position.y = .264; ground.receiveShadow = true; scene.add(ground);
const fireLight = new THREE.PointLight('#ffa24d', 9, 5, 2);
fireLight.position.set(1, 1.05, .74); scene.add(fireLight);
const loader = new GLTFLoader();
let state = newState();
let physics: Awaited<ReturnType<typeof createPhysics>>;
let player: THREE.Group;
let ready = false;
let path: Point[] = [];
let selected: string | null = null;
let queued: string | null = null;
let attackTime = 0, attackDuration = .45, dashTime = 0;
let toastTimer = 0, respawnTimer = 0, busyTime = 0;
let simulationTime = 0;
const keys = new Set<string>();
const actorViews = new Map<string, THREE.Group>();
const parts = new Map<string, THREE.Object3D>();
const resources = new Map<string, THREE.Group>();
const targets: THREE.Object3D[] = [];
const effects: { mesh: THREE.Object3D; life: number; duration: number; update: (t: number) => void }[] = [];
const texts: { el: HTMLElement; p: THREE.Vector3; life: number; duration: number }[] = [];
const temp = new THREE.Vector3();
const ringMaterial = new THREE.MeshBasicMaterial({ color: '#e6c783', transparent: true, opacity: .65, side: THREE.DoubleSide, depthWrite: false });
function ring(radius: number, mat = ringMaterial) {
  const mesh = new THREE.Mesh(new THREE.RingGeometry(radius - .035, radius, 48), mat);
  mesh.rotation.x = -Math.PI / 2; mesh.position.y = .30; mesh.renderOrder = 3;
  return mesh;
}
const selectionRing = ring(.82); selectionRing.visible = false; scene.add(selectionRing);
const playerRing = ring(.47, new THREE.MeshBasicMaterial({ color: '#ead49c', transparent: true, opacity: .31, depthWrite: false })); scene.add(playerRing);
const moveRing = ring(.25); moveRing.visible = false; scene.add(moveRing);
const campRing = ring(1.1, new THREE.MeshBasicMaterial({ color: '#d5bf80', transparent: true, opacity: .22, depthWrite: false })); campRing.position.set(CAMP.x, .292, CAMP.z); scene.add(campRing);
const campMarker = new THREE.Object3D(); campMarker.position.set(.3, 3.25, .7); scene.add(campMarker);

function notify(message: string, seconds = 3.5) {
  $('#toast').textContent = message; $('#toast').classList.add('visible'); toastTimer = seconds;
}
function floatText(message: string, point: Point, color = '#f4de93') {
  const el = document.createElement('div'); el.className = 'float-text'; el.textContent = message; el.style.color = color;
  document.body.append(el); texts.push({ el, p: new THREE.Vector3(point.x, 1.8, point.z), life: 1.25, duration: 1.25 });
}
function effect(mesh: THREE.Object3D, duration: number, update: (t: number) => void) {
  scene.add(mesh); effects.push({ mesh, life: duration, duration, update });
}
function pulse(point: Point, color = '#e9d08a', radius = 1) {
  const mat = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: .6, depthWrite: false });
  const mesh = ring(radius, mat); mesh.position.set(point.x, .31, point.z);
  effect(mesh, .42, t => { mesh.scale.setScalar(.6 + t); mat.opacity = .6 * (1 - t); });
}

function batchEnvironment(root: THREE.Group) {
  root.updateMatrixWorld(true);
  const batches = new Map<string, { mat: THREE.MeshStandardMaterial; geometries: THREE.BufferGeometry[] }>();
  root.traverse(obj => {
    if (!(obj instanceof THREE.Mesh) || Array.isArray(obj.material)) return;
    let owner: THREE.Object3D | null = obj;
    while (owner && !owner.userData.region) owner = owner.parent;
    const region = owner?.userData.region || 'terrain', mat = obj.material as THREE.MeshStandardMaterial;
    const name = obj.name;
    if (region === 'terrain' || region === 'woodland') return;
    if (obj.userData.animated || owner?.userData.animated || mat.name === 'path') return;
    if (region === 'camp' && /adventurer|greave|helm|visor|armor|belt|glove|shield|sword|traveller|tunic/i.test(owner?.name || name)) return;
    applyScenePalette(mat);
    if (mat.name.startsWith('M_Window_Glass')) { mat.emissive.set('#bd8a35'); mat.emissiveIntensity = .15; }
    const geo = obj.geometry.index ? obj.geometry.toNonIndexed() : obj.geometry.clone();
    for (const attr of Object.keys(geo.attributes)) if (!['position', 'normal'].includes(attr)) geo.deleteAttribute(attr);
    geo.applyMatrix4(obj.matrixWorld);
    if (!batches.has(mat.uuid)) batches.set(mat.uuid, { mat, geometries: [] });
    batches.get(mat.uuid)!.geometries.push(geo);
  });
  for (const b of batches.values()) {
    const mesh = new THREE.Mesh(mergeGeometries(b.geometries)!, b.mat);
    mesh.castShadow = true; mesh.receiveShadow = true; mesh.name = 'Camp / ' + b.mat.name; scene.add(mesh);
    for (const g of b.geometries) g.dispose();
  }
}

function addRiver() {
  function ribbon(width: number, y: number, material: THREE.Material) {
    const p: number[] = [], indices: number[] = [];
    for (let i = 0; i <= 160; i++) {
      const z = -40 + i * .5, x = riverX(z);
      p.push(x - width / 2, y, z, x + width / 2, y, z);
      if (i < 160) indices.push(i * 2, i * 2 + 2, i * 2 + 1, i * 2 + 1, i * 2 + 2, i * 2 + 3);
    }
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(p, 3)); g.setIndex(indices); g.computeVertexNormals();
    const m = new THREE.Mesh(g, material); m.receiveShadow = true; scene.add(m);
  }
  ribbon(2.25, .272, landscape.bank); ribbon(1.75, .281, landscape.water);
}

function scatterForest(source: THREE.Group) {
  const spots: { x: number; z: number; scale: number; angle: number }[] = [];
  let seed = 1420;
  const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
  for (let z = -31; z < 32; z += 2.6) for (let x = -31; x < 32; x += 2.6) {
    const xx = x + (rnd() - .5), zz = z + (rnd() - .5), d = Math.hypot(xx / 15, zz / 14);
    if (d < 1 || Math.abs(xx - riverX(zz)) < 2.2 || rnd() < .16) continue;
    spots.push({ x: xx, z: zz, scale: 1.05 + rnd() * .8, angle: rnd() * Math.PI * 2 });
  }
  const dummy = new THREE.Object3D(), matrix = new THREE.Matrix4();
  source.updateMatrixWorld(true);
  source.traverse(o => {
    if (!(o instanceof THREE.Mesh)) return;
    const geo = o.geometry.clone(); geo.applyMatrix4(o.matrixWorld);
    const im = new THREE.InstancedMesh(geo, o.material, spots.length); im.castShadow = true; im.receiveShadow = true;
    spots.forEach((s, i) => {
      dummy.position.set(s.x, .264, s.z); dummy.rotation.y = s.angle; dummy.scale.setScalar(s.scale); dummy.updateMatrix();
      matrix.copy(dummy.matrix); im.setMatrixAt(i, matrix); im.setColorAt(i, new THREE.Color().setScalar(.78 + rnd() * .36));
    });
    im.computeBoundingSphere(); scene.add(im);
  });
  // Sparse flowers and grass stay below the player's silhouette.
  const grass = new THREE.InstancedMesh(new THREE.ConeGeometry(.08, .24, 3), new THREE.MeshStandardMaterial({ color: '#8b9c4e', roughness: 1 }), 850);
  for (let i = 0; i < 850; i++) {
    let x = (rnd() - .5) * 32, z = (rnd() - .5) * 32;
    if (Math.abs(x - riverX(z)) < 1.3 || !passable(x, z, 0)) x = -18 - rnd() * 3;
    dummy.position.set(x, .38, z); dummy.scale.set(.6 + rnd(), .5 + rnd() * .7, .6 + rnd()); dummy.rotation.y = rnd() * 6; dummy.updateMatrix(); grass.setMatrixAt(i, dummy.matrix);
  }
  grass.computeBoundingSphere(); scene.add(grass);
}

const flames: THREE.Mesh[] = [];
function addFire() {
  const material = new THREE.MeshBasicMaterial({ color: '#ffc46b', transparent: true, opacity: .88, depthWrite: false });
  for (let i = 0; i < 5; i++) {
    const flame = new THREE.Mesh(new THREE.IcosahedronGeometry(.20, 1), i % 2 ? material : material.clone());
    flame.position.set(1 + Math.sin(i * 2) * .13, .68 + i * .065, .74 + Math.cos(i * 2) * .12);
    flame.scale.set(.6, 1.7 + i * .12, .65); scene.add(flame); flames.push(flame);
  }
}

async function boot() {
  const urls = ['wayfarers-rest', 'play/traveller', 'play/boar', 'play/copper', 'play/cedar', 'play/stump'];
  let loaded = 0;
  const [models, p] = await Promise.all([
    Promise.all(urls.map(async url => {
      const model = await loader.loadAsync('/models/' + url + '.glb');
      loaded++; $('#load-fill').style.width = `${loaded / urls.length * 93}%`; return model.scene;
    })), createPhysics(),
  ]);
  physics = p;
  batchEnvironment(models[0]); addRiver(); scatterForest(models[4]); addFire();
  player = models[1]; player.scale.setScalar(1.15); scene.add(player);
  player.traverse(o => { o.castShadow = true; o.receiveShadow = true; parts.set(o.name, o); });
  player.updateMatrixWorld(true);
  for (const [parent, children] of [['ArmR', ['Blade', 'Crossguard', 'Grip']], ['ArmL', ['Shield', 'ShieldBoss']], ['LegL', ['BootL']], ['LegR', ['BootR']]] as const) {
    for (const name of children) if (parts.get(name) && parts.get(parent)) parts.get(parent)!.attach(parts.get(name)!);
  }
  for (const r of state.resources) {
    const model = models[r.kind === 'wood' ? 4 : 3].clone(true);
    model.position.set(r.x, .264, r.z); model.userData.id = r.id;
    model.traverse(o => { o.castShadow = true; o.receiveShadow = true; o.userData.target = r.id; if (o instanceof THREE.Mesh) targets.push(o); });
    if (r.kind === 'wood') {
      const stump = models[5].clone(true); stump.visible = false; model.add(stump); stump.name = 'depleted';
    }
    resources.set(r.id, model); scene.add(model);
  }
  for (const b of state.boars) {
    const model = models[2].clone(true); model.scale.setScalar(1.18); model.position.set(b.x, .264, b.z);
    model.traverse(o => { o.castShadow = true; o.receiveShadow = true; o.userData.target = b.id; if (o instanceof THREE.Mesh) targets.push(o); });
    actorViews.set(b.id, model); scene.add(model);
  }
  ready = true;
  canvas.dataset.ready = 'true'; $('#loading').hidden = true;
  focus.set(state.player.x - .6, .7, state.player.z - 1.1);
  notify('WASD 移动 · 点击目标行动 · F 采集 / 交付', 7);
  resize(); updateUI();
}

function targetById(id: string | null): Resource | Boar | undefined {
  return state.resources.find(r => r.id === id && !r.depleted) || state.boars.find(b => b.id === id && b.hp > 0);
}
function harvest(r: Resource) {
  if (state.cooldowns.harvest > 0 || distance(state.player, r) > 1.65 || state.complete) return;
  state.cooldowns.harvest = .65; busyTime = .45; attackTime = .34; attackDuration = .34;
  state.player.facing = Math.atan2(r.x - state.player.x, r.z - state.player.z) + Math.PI;
  r.hp--; pulse(r, '#c8cb8b', .55);
  floatText(r.kind === 'wood' ? '砍伐' : '采矿', r, '#ddcfaa');
  if (r.hp <= 0) {
    r.depleted = true; state.inventory[r.kind]++;
    const view = resources.get(r.id)!;
    if (r.kind === 'wood') for (const c of view.children) c.visible = c.name === 'depleted';
    else { view.scale.setScalar(.4); view.position.y = .16; }
    notify(`${r.kind === 'wood' ? '雪松木' : '铜矿石'} +1`);
    if (selected === r.id) selected = null;
    queued = null;
  }
  updateUI();
}
function attack(special = false) {
  if (!ready || state.paused || respawnTimer > 0 || state.cooldowns.attack > 0) return;
  if (special && (state.cooldowns.cleave > 0 || state.player.energy < 30)) return;
  attackTime = special ? .65 : .42; attackDuration = attackTime;
  state.cooldowns.attack = special ? .75 : .53;
  if (special) { state.cooldowns.cleave = 6; state.player.energy -= 30; }
  const radius = special ? 2.8 : 1.95;
  const target = state.boars.filter(b => b.hp > 0 && distance(state.player, b) <= radius).sort((a, b) => distance(state.player, a) - distance(state.player, b));
  if (target[0]) state.player.facing = Math.atan2(target[0].x - state.player.x, target[0].z - state.player.z) + Math.PI;
  for (const b of special ? target : target.slice(0, 1)) {
    b.hp = Math.max(0, b.hp - (special ? 55 : 32));
    floatText(String(special ? 55 : 32), b, special ? '#ffe2a0' : '#f5f0d8');
    if (b.hp === 0) {
      b.phase = 'dead'; state.inventory.boars++; queued = null; if (selected === b.id) selected = null;
      notify('林道更安全了 · 野猪已击退');
    }
  }
  const mat = new THREE.MeshBasicMaterial({ color: special ? '#ffe2a1' : '#eee5c1', transparent: true, opacity: .7, side: THREE.DoubleSide, depthWrite: false });
  const geo = new THREE.RingGeometry(radius - .16, radius, 40, 1, special ? 0 : .25, special ? Math.PI * 2 : Math.PI * .95);
  const arc = new THREE.Mesh(geo, mat); arc.rotation.x = -Math.PI / 2; arc.rotation.z = -state.player.facing;
  arc.position.set(state.player.x, .73, state.player.z);
  effect(arc, special ? .45 : .26, t => { arc.scale.setScalar(.6 + t * .5); mat.opacity = .7 * (1 - t); arc.rotation.z += .03; });
  updateUI();
}
function dash() {
  if (!ready || state.paused || state.cooldowns.dash > 0 || state.player.energy < 20) return;
  state.cooldowns.dash = 4; state.player.energy -= 20; dashTime = .38;
  pulse(state.player, '#a8cfb6', .5); updateUI();
}
function potion() {
  if (!ready || state.paused || state.player.potions <= 0 || state.player.hp >= 100) return;
  state.player.potions--; state.player.hp = Math.min(100, state.player.hp + 55);
  pulse(state.player, '#b8e0a5', .7); floatText('+55', state.player, '#b8e0a5'); updateUI();
}
function interact() {
  if (!ready || state.paused || state.complete || respawnTimer > 0) return;
  const r = state.resources.filter(r => !r.depleted && distance(state.player, r) < 1.65).sort((a, b) => distance(state.player, a) - distance(state.player, b))[0];
  if (r) { selected = r.id; harvest(r); return; }
  if (distance(state.player, CAMP) < 2.2) {
    if (suppliesReady(state)) {
      state.complete = true; state.paused = true; keys.clear(); state.inventory.silver = 120;
      $('#dialog-eyebrow').textContent = '委托完成 · 暮松林地';
      $('#dialog-title').textContent = '一路辛苦，旅人。';
      $('#dialog-copy').innerHTML = `驿站收到了新的补给，林道也恢复了宁静。<br>雪松木 3 · 铜矿石 2 · 野猪 2<br>获得报酬：120 银币<br>本次旅程 ${Math.floor(state.elapsed / 60)} 分 ${Math.floor(state.elapsed % 60)} 秒`;
      $('#resume').textContent = '留在林间'; $('#overlay').hidden = false; updateUI();
    } else {
      state.player.hp = 100; state.player.energy = 100;
      notify('篝火让你恢复了体力 · 继续为驿站寻找补给'); pulse(state.player, '#badca3'); updateUI();
    }
  } else notify('靠近树木或矿石按 F 采集');
}

function setPause(value: boolean) {
  if (!ready) return;
  state.paused = value; keys.clear(); $('#overlay').hidden = !value;
  $('#pause').textContent = value ? '▷' : 'Ⅱ';
}
function restart() { location.reload(); }
$('#attack').onclick = () => attack(); $('#cleave').onclick = () => attack(true);
$('#dash').onclick = dash; $('#potion').onclick = potion; $('#interact').onclick = interact;
$('#pause').onclick = () => setPause(!state.paused);
$('#help-button').onclick = () => setPause(true);
$('#resume').onclick = () => setPause(false);
$('#restart').onclick = restart;
window.addEventListener('keydown', e => {
  if (['Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.code)) e.preventDefault();
  if (e.code === 'Escape') { setPause(!state.paused); return; }
  if (e.code === 'Slash') { setPause(true); return; }
  if (!ready || state.paused) return;
  keys.add(e.code);
  if (e.repeat) return;
  if (e.code === 'Space') attack(); if (e.code === 'KeyQ') attack(true);
  if (e.code === 'KeyE') dash(); if (e.code === 'KeyR') potion(); if (e.code === 'KeyF') interact();
});
window.addEventListener('keyup', e => keys.delete(e.code));
window.addEventListener('blur', () => { keys.clear(); if (ready) setPause(true); });
document.addEventListener('visibilitychange', () => { if (document.hidden && ready) setPause(true); });
document.querySelectorAll<HTMLElement>('[data-dir]').forEach(b => {
  b.onpointerdown = e => { e.preventDefault(); b.setPointerCapture(e.pointerId); keys.add(b.dataset.dir!); };
  b.onpointerup = b.onpointercancel = () => keys.delete(b.dataset.dir!);
});
const ray = new THREE.Raycaster(), mouse = new THREE.Vector2();
const groundPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -.28);
function aim(e: PointerEvent) {
  mouse.set(e.clientX / innerWidth * 2 - 1, 1 - e.clientY / innerHeight * 2); ray.setFromCamera(mouse, camera);
}
canvas.addEventListener('contextmenu', e => e.preventDefault());
canvas.addEventListener('pointerdown', e => {
  if (!ready || state.paused || respawnTimer > 0) return;
  aim(e);
  const hits = ray.intersectObjects(targets, false);
  const hit = hits.find(h => h.object.visible && targetById(h.object.userData.target));
  if (hit) {
    selected = hit.object.userData.target; queued = selected;
    const t = targetById(selected)!;
    if (distance(state.player, t) < 1.55) { if ('kind' in t) harvest(t); else attack(); path = []; }
    else path = findPath(state.player, t);
  } else if (ray.ray.intersectPlane(groundPlane, temp)) {
    selected = null; queued = null; path = findPath(state.player, { x: temp.x, z: temp.z });
    moveRing.position.set(temp.x, .30, temp.z); moveRing.visible = true;
  }
});
canvas.addEventListener('pointermove', e => {
  if (!ready) return;
  aim(e); const hit = ray.intersectObjects(targets, false).some(h => !!targetById(h.object.userData.target));
  canvas.style.cursor = hit ? 'pointer' : 'crosshair';
});
canvas.addEventListener('wheel', e => {
  e.preventDefault(); camera.zoom = THREE.MathUtils.clamp(camera.zoom * Math.exp(-e.deltaY * .00065), .65, 1.6); camera.updateProjectionMatrix();
}, { passive: false });
canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); setPause(true); notify('画面连接中断，正在恢复…', 30); });
canvas.addEventListener('webglcontextrestored', () => location.reload());

function updateUI() {
  $('#health-fill').style.width = `${state.player.hp}%`; $('#health-text').textContent = `${Math.ceil(state.player.hp)} / 100`;
  $('#energy-fill').style.width = `${state.player.energy}%`; $('#potion-count').textContent = String(state.player.potions);
  for (const [name, key, total] of [['wood', 'wood', GOAL.wood], ['ore', 'ore', GOAL.ore], ['boar', 'boars', GOAL.boars]] as const) {
    const n = state.inventory[key]; $(`#${name}-count`).textContent = `${n} / ${total}`;
    $(`#${name}-goal`).classList.toggle('complete', n >= total); $(`#${name}-goal .check`).textContent = n >= total ? '✓' : '◇';
  }
  $('#return-hint').hidden = !suppliesReady(state) || state.complete;
  if (state.complete) $('.quest h1').textContent = '委托已完成';
  $('#cleave i').style.transform = `scaleY(${state.cooldowns.cleave / 6})`;
  $('#dash i').style.transform = `scaleY(${state.cooldowns.dash / 4})`;
}
function hurt(amount: number) {
  if (respawnTimer > 0 || dashTime > 0 || state.complete) return;
  state.player.hp = Math.max(0, state.player.hp - amount); floatText(`−${amount}`, state.player, '#f0a28c');
  pulse(state.player, '#d3806c', .48);
  if (state.player.hp === 0) { respawnTimer = 2; state.deaths++; path = []; queued = null; notify('你负伤倒下了，驿站守卫正赶来救援…', 4); }
  updateUI();
}
function tick(dt: number) {
  if (!ready || state.paused) return;
  if (!state.complete) state.elapsed += dt;
  simulationTime += dt;
  for (const k of Object.keys(state.cooldowns) as (keyof typeof state.cooldowns)[]) state.cooldowns[k] = Math.max(0, state.cooldowns[k] - dt);
  state.player.energy = Math.min(100, state.player.energy + dt * 7);
  attackTime = Math.max(0, attackTime - dt); busyTime = Math.max(0, busyTime - dt); dashTime = Math.max(0, dashTime - dt);
  if (respawnTimer > 0) {
    respawnTimer -= dt;
    if (respawnTimer <= 0) {
      physics.teleport(.55, 3.8); state.player.x = .55; state.player.z = 3.8; state.player.hp = 100; state.player.energy = 100;
      for (const b of state.boars) if (b.hp > 0) { b.x = b.home.x; b.z = b.home.z; b.phase = 'idle'; }
      notify('已在驿站获救 · 已收集的补给仍在背包中'); updateUI();
    }
    return;
  }
  let dx = Number(keys.has('KeyD') || keys.has('ArrowRight')) - Number(keys.has('KeyA') || keys.has('ArrowLeft'));
  let dz = Number(keys.has('KeyS') || keys.has('ArrowDown')) - Number(keys.has('KeyW') || keys.has('ArrowUp'));
  if (dx || dz) {
    path = []; queued = null;
    const right = new THREE.Vector3().setFromMatrixColumn(camera.matrixWorld, 0); right.y = 0; right.normalize();
    const forward = new THREE.Vector3(-right.z, 0, right.x);
    const motion = right.multiplyScalar(dx).addScaledVector(forward, dz).normalize(); dx = motion.x; dz = motion.z;
  } else if (path.length) {
    const dest = path[0], d = distance(state.player, dest);
    if (d < .16) path.shift();
    else { dx = (dest.x - state.player.x) / d; dz = (dest.z - state.player.z) / d; }
  }
  if (!path.length) moveRing.visible = false;
  if (queued) {
    const target = targetById(queued);
    if (!target) queued = null;
    else if (distance(state.player, target) < 1.52) {
      dx = 0; dz = 0; path = [];
      if ('kind' in target) harvest(target); else attack();
    } else if (!path.length || ('phase' in target && simulationTime % .5 < dt)) path = findPath(state.player, target);
  }
  const speed = busyTime > 0 ? 0 : dashTime > 0 ? 8.5 : attackTime > .18 ? 1.25 : 3.25;
  if (dx || dz) state.player.facing = Math.atan2(dx, dz) + Math.PI;
  const pos = physics.move(dx * speed * dt, dz * speed * dt);
  state.player.x = pos.x; state.player.z = pos.z;
  if (keys.has('Space')) attack(); if (keys.has('KeyF')) interact();
  for (const b of state.boars) {
    if (b.hp <= 0) continue;
    b.timer = Math.max(0, b.timer - dt);
    const d = distance(state.player, b), homeDistance = distance(b, b.home);
    if (b.phase === 'idle' && d < 5.7) b.phase = 'chase';
    if (b.phase === 'chase') {
      if (d > 9 || homeDistance > 8) { b.phase = 'idle'; b.x = b.home.x; b.z = b.home.z; b.hp = b.maxHp; continue; }
      b.facing = Math.atan2(state.player.x - b.x, state.player.z - b.z) + Math.PI;
      if (d < 1.45) { b.phase = 'windup'; b.timer = .75; b.strike = { x: state.player.x, z: state.player.z }; pulse(b, '#d79969', 1.35); }
      else {
        const x = b.x + (state.player.x - b.x) / d * dt * 1.95, z = b.z + (state.player.z - b.z) / d * dt * 1.95;
        if (passable(x, z, .35)) { b.x = x; b.z = z; }
      }
    } else if (b.phase === 'windup' && b.timer <= 0) {
      if (d < 1.8 && distance(state.player, b.strike) < 1.25) hurt(17);
      b.phase = 'recover'; b.timer = 1.15;
    } else if (b.phase === 'recover' && b.timer <= 0) b.phase = 'chase';
  }
  updateUI();
}

function placeLabel(el: HTMLElement, p: THREE.Vector3) {
  const v = p.clone().project(camera);
  el.style.left = `${(v.x * .5 + .5) * innerWidth}px`; el.style.top = `${(-v.y * .5 + .5) * innerHeight}px`;
  return v.z > -1 && v.z < 1 && Math.abs(v.x) < 1.15 && Math.abs(v.y) < 1.2;
}
function renderState(dt: number, time: number) {
  if (!ready) return;
  const p = state.player;
  const moving = keys.size > 0 || path.length > 0;
  const footY = physics.body.translation().y - .792;
  player.position.set(p.x, footY + (moving && !state.paused && !reduced ? Math.abs(Math.sin(simulationTime * 9)) * .045 : 0), p.z);
  player.rotation.y = p.facing;
  if (respawnTimer > 0) player.rotation.z = -1.1; else player.rotation.z = 0;
  const stride = moving && !state.paused && !reduced ? Math.sin(simulationTime * 10) * .36 : 0;
  for (const [name, sign] of [['LegL', 1], ['LegR', -1]] as const) {
    const leg = parts.get(name);
    if (leg) leg.rotation.x = stride * sign;
  }
  const swing = attackTime > 0 ? Math.sin((1 - attackTime / attackDuration) * Math.PI) : 0;
  const arm = parts.get('ArmR'); if (arm) arm.rotation.x = -swing * 1.6 - stride * .5;
  playerRing.position.set(p.x, .295, p.z);
  const desired = new THREE.Vector3(p.x - .5, .8, p.z - .9);
  focus.lerp(desired, reduced ? 1 : 1 - Math.exp(-dt * 3.6));
  camera.position.copy(focus).add(cameraOffset); camera.lookAt(focus); camera.updateMatrixWorld();
  sun.position.set(p.x - 10, 20, p.z + 12); sun.target.position.set(p.x, 0, p.z); sun.target.updateMatrixWorld();
  for (const b of state.boars) {
    const v = actorViews.get(b.id)!;
    v.position.set(b.x, .264, b.z); v.rotation.y = b.facing;
    v.rotation.z = b.hp <= 0 ? 1.1 : b.phase === 'windup' ? Math.sin(simulationTime * 30) * .035 : 0;
    if (b.phase === 'chase' && !reduced) v.position.y += Math.abs(Math.sin(simulationTime * 13)) * .04;
    v.scale.setScalar(b.hp <= 0 ? .85 : 1.18);
  }
  const target = targetById(selected);
  $('#target-label').hidden = !target;
  selectionRing.visible = !!target;
  if (target) {
    selectionRing.position.set(target.x, .30, target.z);
    $('#target-label strong').textContent = 'kind' in target ? target.name : '林地野猪';
    $('#target-label span').textContent = 'kind' in target ? 'Ⅰ · 可采集' : 'Ⅰ · 野生生物';
    $('#target-label i').style.width = `${target.hp / target.maxHp * 100}%`;
    placeLabel($('#target-label'), new THREE.Vector3(target.x, 'kind' in target && target.kind === 'wood' ? 3.7 : 1.8, target.z));
  }
  $('#camp-label').hidden = !placeLabel($('#camp-label'), campMarker.position);
  const nearby = state.resources.find(r => !r.depleted && distance(p, r) < 1.65);
  const prompt = nearby ? `F · ${nearby.kind === 'wood' ? '砍伐雪松' : '开采铜矿'}${queued === nearby.id ? ' · 正在采集' : ''}` : distance(p, CAMP) < 2.2 ? suppliesReady(state) ? 'F · 交付补给，领取 120 银币' : 'F · 篝火休整' : '';
  $('#prompt').textContent = prompt; $('#prompt').hidden = !prompt || state.complete || state.paused;
  if (!state.paused) {
    toastTimer -= dt; if (toastTimer <= 0) $('#toast').classList.remove('visible');
    for (const f of [...effects]) {
      f.life -= dt; f.update(1 - Math.max(0, f.life) / f.duration);
      if (f.life <= 0) { scene.remove(f.mesh); if (f.mesh instanceof THREE.Mesh) { f.mesh.geometry.dispose(); (f.mesh.material as THREE.Material).dispose(); } effects.splice(effects.indexOf(f), 1); }
    }
    for (const t of [...texts]) {
      t.life -= dt; placeLabel(t.el, t.p.clone().add(new THREE.Vector3(0, (1 - t.life / t.duration) * .8, 0))); t.el.style.opacity = String(Math.min(1, t.life * 2));
      if (t.life <= 0) { t.el.remove(); texts.splice(texts.indexOf(t), 1); }
    }
  }
  landscape.update(reduced || state.paused ? simulationTime : time, 0);
  for (let i = 0; i < flames.length; i++) flames[i].scale.y = reduced ? 1.6 : 1.7 + Math.sin(simulationTime * 5 + i * 2) * .45;
}
function resize() {
  const aspect = innerWidth / innerHeight, h = mobile ? 10.8 : 8.9;
  camera.left = -h * aspect; camera.right = h * aspect; camera.top = h; camera.bottom = -h; camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight); composer.setSize(innerWidth, innerHeight);
}
window.addEventListener('resize', resize); resize();
let previous = performance.now(), accumulator = 0;
renderer.setAnimationLoop(now => {
  const dt = Math.min((now - previous) / 1000, .08); previous = now;
  accumulator += dt;
  while (accumulator >= 1 / 60) { tick(1 / 60); accumulator -= 1 / 60; }
  renderState(dt, now / 1000); composer.render();
});
if (import.meta.env.DEV) Object.assign(window, { __game: {
  get state() { return state; }, get ready() { return ready; }, camera, renderer, scene,
  get path() { return path; }, get selected() { return selected; },
  get physics() { return physics; },
  project(point: Point) { const v = new THREE.Vector3(point.x, .6, point.z).project(camera); return { x: (v.x * .5 + .5) * innerWidth, y: (-v.y * .5 + .5) * innerHeight }; },
  teleport(point: Point) { physics.teleport(point.x, point.z); state.player.x = point.x; state.player.z = point.z; path = []; },
} });
boot().catch(e => {
  console.error(e); $('#load-status').textContent = '林地未能加载，请刷新重试。';
});

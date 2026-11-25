import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';

if (process.argv.includes('--leafy-trees')) {
  await import('./check-leafy-trees.mjs');
  process.exit(0);
}

if (process.argv.includes('--hero-trees')) {
  await import('./check-hero-trees.mjs');
  process.exit(0);
}

if (process.argv.includes('--trees')) {
  await import('./check-trees.mjs');
  process.exit(0);
}

if (process.argv.includes('--mistwood')) {
  await import('./check-mistwood.mjs');
  process.exit(0);
}

// Check the actual optimized shipping asset, including world transforms and axis conversion.
const document = await new NodeIO().registerExtensions(ALL_EXTENSIONS).read(new URL('../public/models/wayfarers-rest.glb',import.meta.url).pathname);
const boxes = new Map();
const signParts = new Map();
const windowParts = [];
for (const node of document.getRoot().listNodes()) {
  const group = node.getExtras().asset_group;
  if (!group || !node.getMesh()) continue;
  const partBox = [[Infinity,Infinity,Infinity],[-Infinity,-Infinity,-Infinity]];
  if (!boxes.has(group)) boxes.set(group,[[Infinity,Infinity,Infinity],[-Infinity,-Infinity,-Infinity]]);
  const box = boxes.get(group), matrix = node.getWorldMatrix();
  for (const primitive of node.getMesh().listPrimitives()) {
    const positions = primitive.getAttribute('POSITION');
    for (let i=0;i<positions.getCount();i++) {
      const p=positions.getElement(i,[]);
      const x=matrix[0]*p[0]+matrix[4]*p[1]+matrix[8]*p[2]+matrix[12];
      const y=matrix[1]*p[0]+matrix[5]*p[1]+matrix[9]*p[2]+matrix[13];
      const z=matrix[2]*p[0]+matrix[6]*p[1]+matrix[10]*p[2]+matrix[14];
      [x,-z,y].forEach((value,axis)=>{
        box[0][axis]=Math.min(box[0][axis],value);
        box[1][axis]=Math.max(box[1][axis],value);
        partBox[0][axis]=Math.min(partBox[0][axis],value);
        partBox[1][axis]=Math.max(partBox[1][axis],value);
      });
    }
  }
  if(group==='sign_support') signParts.set(node.getName(),{box:partBox,role:node.getExtras().support_role});
  if(group.startsWith('window_')) windowParts.push({group,role:node.getExtras().window_role});
}
assert.equal(windowParts.filter(p=>p.role==='glass').length,14,'Fourteen individually fitted window panes must ship');
for(const group of ['window_front_left','window_front_right','window_side']) {
  for(const role of ['jamb','sash','interior','sill']) {
    assert.equal(windowParts.filter(p=>p.group===group && p.role===role).length,1,`Missing ${group} ${role}`);
  }
}
assert.equal(signParts.size,16,'The complete sign support must be present in the shipping model');
for(const role of ['wall_anchor','cantilever','brace','crossbar','sign_board']) {
  assert.equal([...signParts.values()].filter(p=>p.role===role).length,1,`Missing sign support role: ${role}`);
}
for(const role of ['suspension_ring','hanger','sign_socket']) {
  assert.equal([...signParts.values()].filter(p=>p.role===role).length,2,`Missing paired sign support role: ${role}`);
}
for(const index of [0,1]) {
  const ring=signParts.get(`SM_Sign_suspension_ring_${index}`).box;
  const rod=signParts.get(`SM_Sign_hanger_${index}`).box;
  const socket=signParts.get(`SM_Sign_top_socket_${index}`).box;
  const crossbar=[...signParts.values()].find(p=>p.role==='crossbar').box;
  assert.ok(Math.abs((ring[0][0]+ring[1][0]-rod[0][0]-rod[1][0])/2)<.002,'Hanger must align with its collar');
  assert.ok(rod[1][2]>=ring[0][2] && rod[0][2]<=socket[1][2],'Hanger must reach both collar and panel socket');
  assert.ok(ring[0][0]>=crossbar[0][0] && ring[1][0]<=crossbar[1][0],'Collars must sit on the crossbar');
}
const supplies=['barrel_0','barrel_1','crate_stack','firewood'];
for(const name of supplies) {
  assert.ok(boxes.has(name),`Missing authored assembly: ${name}`);
  assert.ok(boxes.get(name)[0][0] >= -.8+.16,`${name} must clear the cottage foundation`);
  assert.ok(Math.abs(boxes.get(name)[0][2]-.27)<.002,`${name} must rest on the meadow`);
}
for(let i=0;i<supplies.length;i++) for(let j=i+1;j<supplies.length;j++) {
  const a=boxes.get(supplies[i]),b=boxes.get(supplies[j]);
  const gap=Math.max(...[0,1,2].map(k=>Math.max(a[0][k]-b[1][k],b[0][k]-a[1][k])));
  assert.ok(gap>=.06,`${supplies[i]} and ${supplies[j]} need a visible gap`);
}

const out = new URL('../output/web/', import.meta.url);
const signOnly = process.argv.includes('--sign-only');
const windowsOnly = process.argv.includes('--windows-only');
const roadsOnly = process.argv.includes('--roads-only');
const fireOnly = process.argv.includes('--fire-only');
await mkdir(out, { recursive: true });
const browser = await chromium.launch({
  executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  headless: true,
  args: ['--use-angle=metal', '--disable-background-timer-throttling'],
});
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1 });
const errors = [];
let clearanceAssetLoaded = false;
page.on('response',response=>{
  if(response.url().includes('wayfarers-rest.glb?v=inset-windows-5') && response.ok()) clearanceAssetLoaded=true;
});
page.on('pageerror', error => errors.push(error.message));
page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text()); });
await page.goto('http://localhost:5173');
await page.locator('#scene[data-ready="true"]').waitFor({ timeout: 30000 });
await page.locator('#loading').waitFor({ state: 'hidden' });
if (!signOnly && !windowsOnly && !roadsOnly && !fireOnly) {
await page.screenshot({ path: new URL('desktop.png', out).pathname });
await page.getByRole('button', { name: '营地', exact: true }).click();
await page.waitForTimeout(1300);
assert.equal(await page.getByRole('button', { name: '营地', exact: true }).getAttribute('aria-pressed'), 'true');
assert.ok(Math.abs(await page.evaluate(() => window.__wayfarer.camera.zoom) - 1.9) < .01, 'Camera must actually zoom into camp');
await page.screenshot({ path: new URL('camp.png', out).pathname });
await page.getByRole('button', { name: '切换昼夜' }).click();
await page.waitForTimeout(1800);
assert.equal(await page.locator('#time-label').textContent(), '林间 · 静夜');
await page.screenshot({ path: new URL('night.png', out).pathname });
await page.getByRole('button', { name: '重置视角' }).click();
await page.keyboard.press('n');
await page.waitForTimeout(1300);
const beforeDrag = await page.evaluate(() => window.__wayfarer.camera.position.toArray());
await page.mouse.move(750, 500);
await page.mouse.down();
await page.mouse.move(1000, 540, { steps: 15 });
await page.mouse.up();
assert.notDeepEqual(await page.evaluate(() => window.__wayfarer.camera.position.toArray()), beforeDrag, 'Dragging must change camera position');
const beforeZoom = await page.evaluate(() => window.__wayfarer.camera.zoom);
await page.mouse.wheel(0, -250);
await page.waitForTimeout(500);
assert.ok(await page.evaluate(() => window.__wayfarer.camera.zoom) > beforeZoom, 'Wheel must zoom camera');
await page.screenshot({ path: new URL('rotated.png', out).pathname });
await page.keyboard.press('r');
await page.waitForTimeout(1300);
// Locate a pickable visible part of the scene using the actual hover feedback.
let picked = false;
for (const [x,y] of [[620,430],[650,500],[720,650],[850,700]]) {
  await page.mouse.move(x,y);
  if (await page.locator('#hover-label').isVisible()) {
    await page.mouse.click(x,y);
    if (await page.locator('#detail').isVisible()) { picked = true; break; }
  }
}
assert.ok(picked, 'Clicking a visible model should open its description');
await page.screenshot({ path: new URL('detail.png', out).pathname });
await page.keyboard.press('Escape');
assert.equal(await page.locator('#detail').isVisible(), false);
await page.getByRole('button', { name: '自动环绕', exact: true }).click();
assert.equal(await page.locator('#orbit').getAttribute('aria-pressed'), 'true');
await page.getByRole('button', { name: '重置视角' }).click();
assert.equal(await page.locator('#orbit').getAttribute('aria-pressed'), 'false');
await page.getByRole('button', { name: '操作帮助' }).click();
assert.equal(await page.locator('#help').isVisible(), true);
await page.keyboard.press('Escape');
await page.setViewportSize({ width: 390, height: 844 });
await page.waitForTimeout(600);
await page.mouse.click(20,150);
await page.screenshot({ path: new URL('mobile.png', out).pathname });
assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
const limestoneCount = await page.evaluate(() => {
  const materials=new Set();
  window.__wayfarer.scene.traverse(o=>{
    if(o.isMesh && o.material?.name.startsWith('M_Limestone_')) materials.add(o.material.name);
  });
  return materials.size;
});
assert.equal(limestoneCount,6,'The updated limestone courses must be loaded');
await page.setViewportSize({width:1440,height:900});
await page.evaluate(()=>{
  const {camera,controls}=window.__wayfarer;
  controls.target.set(-2.65,.85,0);
  camera.position.set(-2.65,4.5,10);
  camera.zoom=3.5;
  camera.updateProjectionMatrix();
  controls.update();
});
await page.waitForTimeout(350);
await page.screenshot({path:new URL('masonry-closeup.png',out).pathname});
for(const [name,position] of [['supplies-front',[3.8,3.6,6]],['supplies-side',[3.5,5.4,-.8]]]) {
  await page.evaluate(position=>{
    const {camera,controls}=window.__wayfarer;
    controls.target.set(-.75,.95,-1.45);
    camera.position.set(...position);
    camera.zoom=3.3;
    camera.updateProjectionMatrix();
    controls.update();
  },position);
  await page.waitForTimeout(400);
  await page.screenshot({path:new URL(`${name}.png`,out).pathname});
}
assert.ok(clearanceAssetLoaded,'The browser must load the updated clearance model');
}
for(const [name,position] of (windowsOnly || roadsOnly || fireOnly ? [] : [['sign-front',[0,3.05,5.8]],['sign-side',[2,3.2,3.8]]])) {
  await page.evaluate(position=>{
    const {camera,controls}=window.__wayfarer;
    controls.maxZoom=9; // Inspect joints closer than the normal user camera limit.
    controls.target.set(-1,2.4,.55);
    camera.position.set(...position);
    camera.zoom=7;
    camera.updateProjectionMatrix();
    controls.update();
  },position);
  await page.waitForTimeout(400);
  await page.screenshot({path:new URL(`${name}.png`,out).pathname});
}
if (!signOnly && !roadsOnly && !fireOnly) {
  const styles=await page.evaluate(()=>{
    const materials=new Map();
    window.__wayfarer.scene.traverse(o=>{
      if(o.isMesh && o.material.name.startsWith('M_Window_Glass_')) materials.set(o.material.name,{
        transmission:o.material.transmission,emissiveIntensity:o.material.emissiveIntensity,reflection:!!o.material.envMap,
      });
    });
    return [...materials.values()];
  });
  assert.equal(styles.length,4,'Four subtle glass variants must load');
  assert.ok(styles.every(m=>m.transmission>.3 && m.reflection && m.emissiveIntensity<.025),'Daylight glass must reflect and transmit, with minimal emission');
  for(const [name,target,position] of [
    ['windows-front-day',[-3.83,2.12,-.025],[-2.5,3.05,5]],
    ['windows-side-day',[-.85,2.12,-1.9],[2.5,3.1,-.8]],
  ]) {
    await page.evaluate(({target,position})=>{
      const {camera,controls}=window.__wayfarer;
      controls.maxZoom=9;
      controls.target.set(...target);
      camera.position.set(...position);
      camera.zoom=6.8;
      camera.updateProjectionMatrix();
      controls.update();
    },{target,position});
    await page.waitForTimeout(400);
    await page.screenshot({path:new URL(`${name}.png`,out).pathname});
  }
  await page.keyboard.press('n');
  await page.waitForTimeout(1800);
  await page.evaluate(()=>{
    const {camera,controls}=window.__wayfarer;
    controls.target.set(-3.83,2.12,-.025);
    camera.position.set(-2.5,3.05,5);
    camera.zoom=6.8;
    camera.updateProjectionMatrix();
    controls.update();
  });
  await page.waitForTimeout(400);
  await page.screenshot({path:new URL('windows-front-night.png',out).pathname});
  assert.ok(await page.evaluate(()=>{
    const values=[];
    window.__wayfarer.scene.traverse(o=>{
      if(o.isMesh && o.material.name.startsWith('M_Window_Glass_')) values.push(o.material.emissiveIntensity);
    });
    return values.length>0 && values.every(v=>v>.85 && v<1.2);
  }),'At night the separated panes must emit a restrained warm light');
}
if (!signOnly && !windowsOnly && !fireOnly) {
  const roadAudit=await page.evaluate(async()=>{
    const {roadDistance}=await import('/src/roads.ts');
    const {scene}=window.__wayfarer;
    const ramp=scene.getObjectByName('Road / earth approach to bridge');
    const ground=scene.getObjectByName('Forest / continuous meadow');
    const stale=[];
    scene.traverse(o=>{if(o.isMesh && ['path','Path / worn earth'].includes(o.material?.name)) stale.push(o.name);});
    // Trace the corrected branch from the junction to the bridge entrance.
    const samples=[[-2.15,2.65],[-1.35,2.63],[-.15,2.42],[.95,1.99],[1.95,1.35],[2.48,1.10],[2.82,1.095]];
    const onRoad=samples.map(([x,z])=>roadDistance(x,z)<-.30);
    const excluded=roadDistance(1,.74)>.20; // The hearth must retain its grass/soil clearance.
    const normals=ramp.geometry.attributes.normal;
    const upward=Array.from({length:normals.count},(_,i)=>normals.getY(i)).every(v=>v>0);
    const position=ramp.geometry.attributes.position;
    let deckHeight=0;
    for(let i=0;i<position.count;i++) if(position.getX(i)>2.86) deckHeight=Math.max(deckHeight,position.getY(i));
    return {stale,onRoad,excluded,upward,deckHeight,sharedMaterial:ramp.material===ground.material,
      rampTriangles:ramp.geometry.index.count/3};
  });
  assert.deepEqual(roadAudit.stale,[],'No legacy road strips should remain visible');
  assert.ok(roadAudit.onRoad.every(Boolean),'The branch must continuously reach the bridge');
  assert.ok(roadAudit.excluded,'The road must avoid the hearth');
  assert.ok(roadAudit.upward && roadAudit.sharedMaterial,'The approach must face up and share the ground material');
  assert.ok(roadAudit.deckHeight>.58 && roadAudit.deckHeight<.60,'The bank must rise to the first bridge plank');
  if((await page.locator('#time-label').textContent()).includes('静夜')) {
    await page.keyboard.press('n');
    await page.waitForTimeout(1800);
  }
  for(const [name,target,position,zoom] of [
    ['road-junction',[.25,.35,2.35],[4.8,8,9],3.0],
    ['road-overhead',[.2,.3,1.4],[.2,14,7],1.7],
    ['road-bridge',[2.35,.45,1.1],[5.5,2.7,5.4],4.0],
  ]) {
    await page.evaluate(({target,position,zoom})=>{
      const {camera,controls}=window.__wayfarer;
      controls.maxZoom=9;
      controls.target.set(...target);camera.position.set(...position);camera.zoom=zoom;
      camera.updateProjectionMatrix();controls.update();
    },{target,position,zoom});
    await page.waitForTimeout(400);
    await page.screenshot({path:new URL(`${name}.png`,out).pathname});
  }
  console.log('ROAD_AUDIT',JSON.stringify(roadAudit));
  await writeFile(new URL('road-audit.json',out),JSON.stringify({passed:true,...roadAudit},null,2));
}
if (!signOnly && !windowsOnly && !roadsOnly) {
  if ((await page.locator('#time-label').textContent()).includes('静夜')) {
    await page.keyboard.press('n');
    await page.waitForTimeout(1800);
  }
  const fireAudit = await page.evaluate(() => {
    const {scene,composer} = window.__wayfarer;
    const stale=[];
    scene.traverse(o=>{
      if(o.isMesh && ['flame','flame_core'].includes(o.material?.name)) stale.push(o.name);
    });
    const pass=composer.passes.find(p=>p.material?.name==='Campfire / flowing volume');
    return {stale,volume:!!pass,depthClipping:!!pass?.uniforms.uDepth.value,
      sparks:!!scene.getObjectByName('Campfire / rising embers'),
      fuel:!!scene.getObjectByName('camp / wood')};
  });
  assert.deepEqual(fireAudit.stale,[],'Old crystal-shaped flame meshes must be removed');
  assert.ok(fireAudit.volume && fireAudit.depthClipping && fireAudit.sparks && fireAudit.fuel,'Depth-clipped fire and soft embers must retain the fuel');
  for(const [name,position,zoom] of [
    ['fire-day',[3.5,2.5,4.5],6.0],
    ['fire-side',[3.8,2.3,-2.4],6.0],
    ['fire-overhead',[1,8,2],5.0],
  ]) {
    await page.evaluate(({position,zoom})=>{
      const {camera,controls}=window.__wayfarer;
      controls.maxZoom=9;
      controls.target.set(1,.85,.74);
      camera.position.set(...position); camera.zoom=zoom;
      camera.updateProjectionMatrix(); controls.update();
    },{position,zoom});
    await page.waitForTimeout(400);
    await page.screenshot({path:new URL(`${name}.png`,out).pathname});
  }
  await page.evaluate(()=>{
    const {camera,controls}=window.__wayfarer;
    controls.target.set(1,.85,.74); camera.position.set(3.5,2.5,4.5);
    camera.zoom=6; camera.updateProjectionMatrix(); controls.update();
  });
  await page.keyboard.press('n');
  await page.waitForTimeout(1800);
  await page.screenshot({path:new URL('fire-night.png',out).pathname});
  await page.emulateMedia({reducedMotion:'reduce'});
  await page.waitForTimeout(900);
  // Check actual rendered pixels: a valid shader can still silently sample stale depth.
  const fireClip={x:560,y:285,width:340,height:390};
  const visibleFire=await page.screenshot({clip:fireClip});
  await page.evaluate(()=>{
    window.__wayfarer.composer.passes.find(p=>p.material?.name==='Campfire / flowing volume').enabled=false;
  });
  await page.waitForTimeout(150);
  const noFire=await page.screenshot({clip:fireClip});
  assert.ok(!visibleFire.equals(noFire),'Fire must visibly contribute pixels above the fuel');
  await page.evaluate(()=>{
    const pass=window.__wayfarer.composer.passes.find(p=>p.material?.name==='Campfire / flowing volume');
    pass.enabled=true;
    pass.uniforms.uRoot.value.y=-2;
  });
  await page.waitForTimeout(150);
  const occludedFire=await page.screenshot({clip:fireClip});
  assert.ok(occludedFire.equals(noFire),'A fire hidden below opaque ground must contribute no pixels');
  await page.evaluate(()=>{
    window.__wayfarer.composer.passes.find(p=>p.material?.name==='Campfire / flowing volume').uniforms.uRoot.value.y=.48;
  });
  await page.waitForTimeout(150);
  assert.ok((await page.screenshot({clip:fireClip})).equals(visibleFire),'Restoring the fire must restore its rendered pixels');
  const freezeState=()=>page.evaluate(()=>{
    const {scene,composer}=window.__wayfarer;
    return {time:composer.passes.find(p=>p.material?.name==='Campfire / flowing volume').uniforms.uTime.value,
      sparks:Array.from(scene.getObjectByName('Campfire / rising embers').geometry.attributes.position.array)};
  });
  const frozen=await freezeState();
  await page.waitForTimeout(300);
  assert.deepEqual(await freezeState(),frozen,'Reduced motion must stop both fire and embers');
  await page.emulateMedia({reducedMotion:'no-preference'});
  await page.waitForTimeout(400);
  assert.notEqual((await freezeState()).time,frozen.time,'Fire animation must resume');
  console.log('FIRE_AUDIT',JSON.stringify(fireAudit));
  await writeFile(new URL('fire-audit.json',out),JSON.stringify({passed:true,...fireAudit,renderedPixels:true,opaqueOcclusion:true,reducedMotion:true},null,2));
}
assert.ok(clearanceAssetLoaded,'The browser must load the updated sign model');
assert.deepEqual(errors, [], 'No browser runtime or console errors');
console.log(JSON.stringify({ passed: true, checked: fireOnly ? ['flame replacement','depth clipping','day/night and three camera angles','soft embers','reduced motion'] : roadsOnly ? ['road continuity','legacy road removal','shared ground material','hearth clearance','bridge approach normals and elevation','junction/overhead/bridge views'] : windowsOnly ? ['fourteen fitted glass panes','window parts','daylight transmission/reflections','night emission','window closeups'] : signOnly ? ['exported supply clearances','exported sign support and alignment','updated GLB load','sign closeups from two angles'] : ['exported supply clearances','ground contact','exported sign support parts and alignment','updated GLB load','camera presets','day/night','orbit drag','zoom','object picking','close detail','auto orbit/reset','help','mobile layout','supply closeups from two angles','sign closeups from two angles','fitted windows and day/night glass','window closeups','road network and bridge approach'], browserErrors: errors, screenshots: out.pathname }, null, 2));
await browser.close();

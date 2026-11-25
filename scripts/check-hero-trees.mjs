import { chromium } from '@playwright/test';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { mkdir,readFile,writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
const out=new URL('../output/hero-trees/web/',import.meta.url);await mkdir(out,{recursive:true});
const manifest=JSON.parse(await readFile(new URL('../public/models/hero-trees/manifest.json',import.meta.url),'utf8'));
const qa=JSON.parse(await readFile(new URL('../output/hero-trees/qa.json',import.meta.url),'utf8'));
assert.equal(Object.keys(qa).length,manifest.styles.length*3);assert.ok(Object.values(qa).every(q=>q.pass),'Blender asset QA must pass for all LODs');
const original=JSON.parse(await readFile(new URL('../output/versions/classic-manifest.json',import.meta.url),'utf8'));
for(const [name,hash] of Object.entries(original))assert.equal(createHash('sha256').update(await readFile(new URL('../'+name,import.meta.url))).digest('hex'),hash,`${name} must remain unchanged`);
const io=new NodeIO().registerExtensions(ALL_EXTENSIONS);
for(const style of manifest.styles){
  assert.equal(style.lods.length,3);
  for(const lod of style.lods){
    const doc=await io.read(new URL('../public'+lod.url,import.meta.url).pathname);
    let triangles=0,minY=Infinity,maxY=-Infinity;
    for(const node of doc.getRoot().listNodes())if(node.getMesh()){
      const m=node.getWorldMatrix();
      for(const primitive of node.getMesh().listPrimitives()){
        triangles+=(primitive.getIndices()?.getCount()??primitive.getAttribute('POSITION').getCount())/3;
        const p=primitive.getAttribute('POSITION');
        for(let i=0;i<p.getCount();i++){
          const [x,y,z]=p.getElement(i,[]);assert.ok(Number.isFinite(x+y+z));
          const wy=m[1]*x+m[5]*y+m[9]*z+m[13];minY=Math.min(minY,wy);maxY=Math.max(maxY,wy);
        }
      }
    }
    assert.equal(triangles,lod.triangles);assert.equal(doc.getRoot().listMeshes().length,2);
    assert.ok(Math.abs(minY)<.002 && maxY>7 && maxY<10,`Grounded meter scale: ${style.id}`);
    for(const mat of doc.getRoot().listMaterials())if(mat.getName().includes('Cutout')){
      assert.equal(mat.getAlphaMode(),'MASK');assert.ok(mat.getDoubleSided());
    }
  }
  assert.ok(style.lods[0].triangles>style.lods[1].triangles&&style.lods[1].triangles>style.lods[2].triangles);
}
const browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,args:['--use-angle=metal','--disable-background-timer-throttling']});
try{
  const page=await browser.newPage({viewport:{width:1600,height:1100},deviceScaleFactor:1});
  const errors=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
  await page.goto('http://localhost:5173/grove.html');
  await page.locator('#scene[data-ready="true"]').waitFor({timeout:60000});await page.waitForTimeout(900);
  assert.equal(await page.evaluate(()=>window.__trees.selected),0);
  await page.screenshot({path:new URL('hero-oak.png',out).pathname,fullPage:true});
  await page.locator('[data-mode="compare"]').click();await page.waitForTimeout(250);
  await page.screenshot({path:new URL('comparison.png',out).pathname,fullPage:true});
  assert.deepEqual(errors,[]);
  if(process.argv.includes('--quick')){console.log('Tree assets + comparison smoke check passed');process.exitCode=0;}
  else{
    const mode=()=>page.evaluate(()=>window.__trees.mode);
    const camera=()=>page.evaluate(()=>window.__trees.camera.position.toArray());
    await page.locator('[data-style="1"]').click();await page.waitForTimeout(500);assert.equal(await mode(),'single');
    await page.screenshot({path:new URL('painted-fir.png',out).pathname});
    const before=await camera();await page.mouse.move(760,550);await page.mouse.down();await page.mouse.move(1050,570,{steps:15});await page.mouse.up();await page.waitForTimeout(400);assert.notDeepEqual(await camera(),before);
    for(const level of ['0','1','2']){
      await page.selectOption('#lod',level);await page.waitForTimeout(150);
      const visible=await page.evaluate(()=>window.__trees.assets[window.__trees.selected].lods.map(l=>l.visible));
      assert.deepEqual(visible,[0,1,2].map(i=>i===Number(level)));
    }
    await page.locator('#wire').click();assert.equal(await page.locator('#wire').getAttribute('aria-pressed'),'true');
    await page.screenshot({path:new URL('wire.png',out).pathname});await page.locator('#wire').click();
    await page.selectOption('#lod','auto');
    for(const i of [0,2,3]){
      await page.locator(`[data-style="${i}"]`).click();await page.waitForTimeout(400);
      await page.screenshot({path:new URL(`${manifest.styles[i].id}.png`,out).pathname});
    }
    for(const level of ['0','1','2']){
      await page.selectOption('#lod',level);await page.waitForTimeout(180);
      assert.deepEqual(await page.evaluate(()=>window.__trees.assets[3].lods.map(l=>l.visible)),[0,1,2].map(i=>i===Number(level)));
      await page.screenshot({path:new URL(`hero-oak-lod${level}.png`,out).pathname});
    }
    await page.selectOption('#lod','auto');
    await page.locator('[data-mode="forest"]').click();await page.waitForTimeout(700);
    assert.equal(await page.evaluate(()=>window.__trees.counts.reduce((a,b)=>a+b,0)+window.__trees.culled),100);
    await page.screenshot({path:new URL('hero-oak-forest.png',out).pathname});
    await page.locator('[data-style="1"]').click();
    await page.locator('[data-mode="forest"]').click();await page.waitForTimeout(1000);
    assert.equal(await mode(),'forest');
    await page.screenshot({path:new URL('forest-100.png',out).pathname});
    const auto=await page.evaluate(()=>({counts:window.__trees.counts,culled:window.__trees.culled,calls:window.__trees.renderer.info.render.calls,triangles:window.__trees.renderer.info.render.triangles}));
    assert.equal(auto.counts.reduce((a,b)=>a+b,0)+auto.culled,100);
    assert.ok(auto.counts[1]+auto.counts[2]>0,'Forest must actually use reduced LODs');
    await page.selectOption('#lod','0');await page.waitForTimeout(250);
    const allHigh=await page.evaluate(()=>window.__trees.renderer.info.render.triangles);
    assert.ok(allHigh>auto.triangles,'Automatic LOD should reduce rendered triangles');
    await page.selectOption('#lod','auto');await page.selectOption('#tree-count','300');await page.waitForTimeout(800);
    assert.equal(await page.evaluate(()=>window.__trees.counts.reduce((a,b)=>a+b,0)+window.__trees.culled),300);
    await page.screenshot({path:new URL('forest-300.png',out).pathname});
    await page.locator('[data-mode="compare"]').click();
    await page.setViewportSize({width:390,height:844});await page.waitForTimeout(600);
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    await page.screenshot({path:new URL('mobile.png',out).pathname,fullPage:true});
    await page.locator('[data-style="2"]').click();await page.waitForTimeout(500);
    await page.screenshot({path:new URL('mobile-single.png',out).pathname,fullPage:true});
    await page.locator('[data-style="3"]').click();await page.waitForTimeout(500);
    await page.screenshot({path:new URL('hero-oak-mobile.png',out).pathname,fullPage:true});
    assert.deepEqual(errors,[],'All scene controls should remain free of shader/runtime errors');
    const report={passed:true,styles:manifest.styles.map(s=>({id:s.id,triangles:s.lods.map(l=>l.triangles)})),blenderQA:manifest.styles.length*3,originalHashesPreserved:true,forest100:{auto,forcedLOD0Triangles:allHigh},browserErrors:errors};
    await writeFile(new URL('audit.json',out),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
  }
}finally{await browser.close();}

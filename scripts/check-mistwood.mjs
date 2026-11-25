import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import { mkdir,writeFile,readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const out=new URL('../output/mistwood/',import.meta.url);
await mkdir(out,{recursive:true});
const manifest=JSON.parse(await readFile(new URL('../output/versions/classic-manifest.json',import.meta.url),'utf8'));
for(const [name,hash] of Object.entries(manifest)) {
  assert.equal(createHash('sha256').update(await readFile(new URL('../'+name,import.meta.url))).digest('hex'),hash,`Original version changed: ${name}`);
}
const browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true,
  args:['--use-angle=metal','--disable-background-timer-throttling']});
try {
  const page=await browser.newPage({viewport:{width:1600,height:1000},deviceScaleFactor:1});
  const errors=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
  await page.goto('http://localhost:5173/mistwood.html');
  await page.locator('#scene[data-ready="true"]').waitFor({timeout:60000});
  await page.locator('#loading').waitFor({state:'hidden'});
  await page.waitForTimeout(900);
  await page.screenshot({path:new URL('forest.png',out).pathname});
  assert.deepEqual(errors,[],'New scene must compile and render without WebGL errors');
  const geometry=await page.evaluate(()=>{
    const {scene,forestAudit,renderer}=window.__wayfarer;
    const invalid=[];
    scene.traverse(o=>{
      if(o.isMesh) {
        const p=o.geometry.attributes.position;
        if(!p) return;
        for(let i=0;i<p.count;i++) if(!Number.isFinite(p.getX(i)+p.getY(i)+p.getZ(i))) {invalid.push(o.name);break;}
      }
    });
    return {...forestAudit,invalid,drawCalls:renderer.info.render.calls,triangles:renderer.info.render.triangles,
      shelter:!!scene.getObjectByName('Mistwood / attached supplies shelter'),
      ferns:scene.getObjectByName('Mistwood / rooted ferns')?.count,
      leaves:scene.getObjectByName('Mistwood / fallen leaves')?.count};
  });
  assert.deepEqual(geometry.invalid,[],'Generated geometry must be finite');
  assert.ok(geometry.trees>60 && geometry.needleSprays>6000 && geometry.ferns>150 && geometry.leaves>1800 && geometry.shelter);
  if(!process.argv.includes('--quick')) {
    await page.getByRole('button',{name:'小屋',exact:true}).click();await page.waitForTimeout(1350);
    assert.equal(await page.getByRole('button',{name:'小屋',exact:true}).getAttribute('aria-pressed'),'true');
    await page.screenshot({path:new URL('cottage.png',out).pathname});
    await page.getByRole('button',{name:'营地',exact:true}).click();await page.waitForTimeout(1350);
    await page.screenshot({path:new URL('camp.png',out).pathname});
    await page.keyboard.press('n');await page.waitForTimeout(2000);
    assert.equal(await page.locator('#time-label').textContent(),'密林 · 夜色');
    await page.screenshot({path:new URL('night.png',out).pathname});
    await page.keyboard.press('n');await page.waitForTimeout(1500);
    await page.keyboard.press('r');await page.waitForTimeout(1350);
    const before=await page.evaluate(()=>window.__wayfarer.camera.position.toArray());
    await page.mouse.move(800,500);await page.mouse.down();await page.mouse.move(1030,530,{steps:16});await page.mouse.up();
    assert.notDeepEqual(await page.evaluate(()=>window.__wayfarer.camera.position.toArray()),before);
    await page.waitForTimeout(500);await page.screenshot({path:new URL('rotated.png',out).pathname});
    await page.keyboard.press('r');await page.waitForTimeout(1350);
    let picked=false;
    for(const [x,y] of [[800,450],[800,550],[950,620],[650,600],[870,650]]) {
      await page.mouse.move(x,y);
      if(await page.locator('#hover-label').isVisible()) {
        await page.mouse.click(x,y);
        if(await page.locator('#detail').isVisible()){picked=true;break;}
      }
    }
    assert.ok(picked,'New scene must retain picking and descriptions');await page.keyboard.press('Escape');
    await page.emulateMedia({reducedMotion:'reduce'});await page.waitForTimeout(200);
    const time=await page.evaluate(()=>window.__wayfarer.composer.passes.find(p=>p.material?.name==='Campfire / flowing volume').uniforms.uTime.value);
    await page.waitForTimeout(200);
    assert.equal(await page.evaluate(()=>window.__wayfarer.composer.passes.find(p=>p.material?.name==='Campfire / flowing volume').uniforms.uTime.value),time);
    await page.setViewportSize({width:390,height:844});await page.waitForTimeout(400);
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    await page.screenshot({path:new URL('mobile.png',out).pathname});
    assert.deepEqual(errors,[],'All new scene interactions must be free of runtime errors');
    await page.goto('http://localhost:5173/versions/classic/');
    await page.locator('#scene[data-ready="true"]').waitFor({timeout:30000});
    assert.equal(await page.locator('h1').textContent(),"林间驿站Wayfarer's Rest");
    await page.locator('#loading').waitFor({state:'hidden'});
    await page.screenshot({path:new URL('preserved-classic.png',out).pathname});
    assert.deepEqual(errors,[],'The frozen original version must still load');
  }
  const audit={passed:true,originalHashesPreserved:true,geometry,browserErrors:errors};
  await writeFile(new URL('audit.json',out),JSON.stringify(audit,null,2));
  console.log(JSON.stringify(audit,null,2));
} finally { await browser.close(); }

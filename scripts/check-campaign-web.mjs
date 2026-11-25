import {chromium} from '@playwright/test';import assert from 'node:assert/strict';import fs from 'node:fs/promises';
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-angle=metal']});const page=await browser.newPage({viewport:{width:1440,height:960}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
const clickWorld=async p=>{await page.waitForTimeout(180);const q=await page.evaluate(p=>window.__workers.project(p),p);await page.mouse.move(q.x,q.y);await page.mouse.click(q.x,q.y);};
try{
 await page.goto('http://localhost:5173/play.html');await page.waitForSelector('#field[data-ready=true]');
 await page.getByRole('button',{name:'全选 12 人',exact:true}).click();await page.getByRole('button',{name:'自动分工',exact:true}).click();
 await page.waitForFunction(()=>{const s=window.__workers.sim;return s.inventory.wood>=60&&s.inventory.ore>=32&&s.inventory.food>=48;},null,{timeout:90000});
 await page.getByRole('button',{name:'1 号',exact:true}).click();await page.getByRole('button',{name:'2 号',exact:true}).click({modifiers:['Shift']});
 await page.getByRole('button',{name:'建仓库 · 木24',exact:true}).click();await clickWorld({x:2,z:0});
 await page.waitForFunction(()=>window.__workers.sim.buildings.length===1);await page.screenshot({path:'output/workers/campaign-building.png'});
 await page.waitForFunction(()=>window.__workers.sim.buildings[0].complete,null,{timeout:45000});
 await page.getByRole('button',{name:'3 号',exact:true}).click();await page.getByRole('button',{name:'4 号',exact:true}).click({modifiers:['Shift']});
 await page.getByRole('button',{name:'建兵营 · 木36',exact:true}).click();
 for(let attempt=0;attempt<12;attempt++){
  const p=await page.evaluate(()=>{const s=window.__workers.sim;for(let z=-4;z<=4;z++)for(let x=-8;x<=7;x++)if(!s.placement({x,z}))return{x,z};return null;});
  if(p)await clickWorld(p);if(await page.evaluate(()=>window.__workers.sim.buildings.length===2))break;
  await page.waitForTimeout(300);
 }
 assert.equal(await page.evaluate(()=>window.__workers.sim.buildings.length),2,await page.evaluate(()=>window.__workers.sim.notice));
 await page.waitForFunction(()=>window.__workers.sim.buildings[1].complete,null,{timeout:45000});
 for(let i=0;i<4;i++)await page.getByRole('button',{name:'招兵 · 矿8 果12',exact:true}).click();
 await page.waitForFunction(()=>window.__workers.sim.recruited===4,null,{timeout:30000});await page.waitForTimeout(200);await page.screenshot({path:'output/workers/campaign-army.png'});
 assert.ok(await page.evaluate(()=>window.__workers.sim.deliveredAtNewDepot>0));
 await page.getByRole('button',{name:'选部队',exact:true}).click();await page.getByRole('button',{name:'进攻营地',exact:true}).click();
 await page.waitForFunction(()=>window.__workers.sim.won||window.__workers.sim.lost,null,{timeout:45000});assert.ok(await page.evaluate(()=>window.__workers.sim.won));
 await page.waitForSelector('#mission-result:not([hidden])');await page.waitForTimeout(200);await page.screenshot({path:'output/workers/campaign-victory.png'});
 await page.getByRole('button',{name:'重置',exact:true}).click();await page.waitForSelector('#field[data-ready=true]');await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth),390);await page.screenshot({path:'output/workers/campaign-mobile.png'});
 assert.deepEqual(errors,[]);await fs.writeFile('output/workers/campaign-check.json',JSON.stringify({pass:true,checks:['zero resource start','actual collection','grid placement','worker construction','new depot receives goods','barracks','paid recruit queue','army command','enemy retaliation','victory','reset','mobile layout'],errors},null,2));console.log('PASS browser complete campaign: collect → construct → recruit → combat → victory');
}catch(error){await page.screenshot({path:'output/workers/campaign-failure.png'});console.log(await page.evaluate(()=>({notice:window.__workers?.sim.notice,inventory:window.__workers?.sim.inventory,buildings:window.__workers?.sim.buildings})));throw error;}finally{await browser.close();}

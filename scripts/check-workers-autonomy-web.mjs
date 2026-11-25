import {chromium} from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-angle=metal']});const page=await browser.newPage({viewport:{width:1440,height:960}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
try{
 await page.goto('http://localhost:5173/workers.html');await page.waitForSelector('#field[data-ready=true]');await page.waitForTimeout(250);
 await page.getByRole('button',{name:'全选 12 人',exact:true}).click();await page.getByRole('button',{name:'自动分工',exact:true}).click();
 const counts=await page.evaluate(()=>{const {sim}=window.__workers;const counts={};for(const w of sim.workers){const job=sim.sources.find(s=>s.id===w.source).activity;counts[job]=(counts[job]??0)+1;}return counts;});assert.deepEqual(counts,{chop:4,mine:3,gather:3,pickup:2});
 await page.getByRole('button',{name:'采空后续采：关',exact:true}).click();assert.ok(await page.evaluate(()=>window.__workers.sim.workers.every(w=>w.continueJob)));
 await page.getByRole('button',{name:'1 号',exact:true}).click();await page.getByRole('button',{name:'采空后续采：开',exact:true}).click();
 assert.ok(await page.evaluate(()=>!window.__workers.sim.workers[0].continueJob&&window.__workers.sim.workers.slice(1).every(w=>w.continueJob)));
 await page.getByRole('button',{name:'全选 12 人',exact:true}).click();assert.match(await page.locator('#continue-job').innerText(),/部分/);
 await page.waitForFunction(()=>{const s=window.__workers.sim;return s.inventory.wood>=12&&s.inventory.ore>=6&&s.inventory.food>=6;},null,{timeout:60000});
 await page.screenshot({path:'output/workers/auto-working.png'});
 await page.getByRole('button',{name:'选等待',exact:true}).click();assert.ok(await page.evaluate(()=>[...window.__workers.selected].every(id=>['waitWork','waitDepot'].includes(window.__workers.sim.workers[id-1].phase))));
 await page.getByRole('button',{name:'重置',exact:true}).click();await page.waitForSelector('#field[data-ready=true]');await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth),390);await page.screenshot({path:'output/workers/auto-mobile.png'});
 await page.getByRole('button',{name:'第 1 组',exact:true}).click();await page.getByRole('button',{name:'自动分工',exact:true}).click();
 assert.ok(await page.evaluate(()=>window.__workers.sim.workers.slice(0,4).every(w=>w.source)&&window.__workers.sim.workers.slice(4).every(w=>w.phase==='idle')));
 await page.getByRole('button',{name:'采空后续采：关',exact:true}).click();assert.ok(await page.evaluate(()=>window.__workers.sim.workers.slice(0,4).every(w=>w.continueJob)));
 assert.deepEqual(errors,[]);await fs.writeFile('output/workers/autonomy-check.json',JSON.stringify({pass:true,checks:['automatic job balance','continuation on/off','mixed selection indication','all three inventories receive actual deliveries','select waiting','mobile automatic jobs','unselected units stay idle'],errors},null,2));console.log('PASS: automatic work UI, continuation on/off, mixed state, real deliveries, select waiting, mobile');
}finally{await browser.close();}

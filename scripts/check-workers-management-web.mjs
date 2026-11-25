import {chromium} from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-angle=metal']});const page=await browser.newPage({viewport:{width:1440,height:960}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
try{
 await page.goto('http://localhost:5173/workers.html');await page.waitForSelector('#field[data-ready=true]');await page.getByRole('button',{name:'工作概览',exact:true}).click();await page.getByRole('button',{name:'树木 A补一人',exact:true}).click();
 let assigned=await page.evaluate(()=>window.__workers.sim.workers.filter(w=>w.source===101).map(w=>w.id));assert.equal(assigned.length,1);
 await page.getByRole('button',{name:'选择树木 A人员',exact:true}).click();assert.deepEqual(await page.evaluate(()=>[...window.__workers.selected]),assigned);
 await page.waitForFunction(()=>window.__workers.sim.workers.some(w=>w.source===101&&w.cargo),null,{timeout:30000});
 await page.getByRole('button',{name:'树木 A撤回一人',exact:true}).click();
 await page.waitForFunction(()=>window.__workers.sim.inventory.wood===6&&window.__workers.sim.workers.every(w=>w.phase==='idle'),null,{timeout:30000});
 assert.ok(await page.evaluate(()=>window.__workers.sim.workers.every(w=>w.source===null&&!w.cargo)));
 await page.getByRole('button',{name:'矿石补一人',exact:true}).click();await page.getByRole('button',{name:'选择矿石人员',exact:true}).click();await page.screenshot({path:'output/workers/management-working.png'});
 await page.keyboard.press('f');assert.ok(await page.evaluate(()=>window.__workers.camera.zoom>=1.4));await page.keyboard.press('Home');
 await page.getByRole('button',{name:'平移',exact:true}).click();await page.keyboard.press('m');assert.equal(await page.locator('#pan-mode').getAttribute('aria-pressed'),'false');await page.waitForFunction(()=>document.querySelector('[data-order=move]').classList.contains('active'));await page.keyboard.press('Escape');
 await page.keyboard.press('i');assert.ok(await page.evaluate(()=>[...window.__workers.selected].every(id=>window.__workers.sim.workers[id-1].phase==='idle')));
 await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth),390);await page.screenshot({path:'output/workers/management-mobile.png'});
 await page.getByRole('button',{name:'果丛补一人',exact:true}).click();assert.ok(await page.evaluate(()=>window.__workers.sim.workers.some(w=>w.source===104)));
 assert.deepEqual(errors,[]);await fs.writeFile('output/workers/management-check.json',JSON.stringify({pass:true,checks:['board add idle worker','select source staff','withdraw loaded worker preserves inventory','keyboard orders','focus selection','mobile board'],errors},null,2));console.log('PASS: management board, add/remove workers, loaded withdrawal, keyboard focus/orders, mobile');
}finally{await browser.close();}

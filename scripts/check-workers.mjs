import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-angle=metal']});
const page=await browser.newPage({viewport:{width:1440,height:960}});
const errors=[];page.on('pageerror',e=>errors.push(e.message));
const snapshot=()=>page.evaluate(()=>{const {sim,selected}=window.__workers;return {workers:sim.workers,inventory:sim.inventory,selected:[...selected],paused:sim.paused};});
const project=(p,y=0)=>page.evaluate(({p,y})=>window.__workers.project(p,y),{p,y});
const clickAt=async(p,button='left',y=0)=>{const q=await project(p,y);await page.mouse.click(q.x,q.y,{button});};
const waitState=async(fn,timeout=30000)=>page.waitForFunction(fn,null,{timeout});
try{
 await page.goto('http://localhost:5173/workers.html');await page.waitForSelector('#field[data-ready=true]');await page.waitForTimeout(200);
 assert.ok((await snapshot()).workers.every(w=>w.phase==='idle'));
 await page.screenshot({path:'output/workers/initial.png'});
 // Actual click selection and right-click movement. Unselected workers stay put.
 await clickAt({x:-7.4,z:2.5},'left',.65);assert.deepEqual((await snapshot()).selected,[2]);
 await clickAt({x:-1.8,z:.7},'right');
 await waitState(()=>window.__workers.sim.workers[1].phase==='idle');
 let state=await snapshot();assert.ok(Math.hypot(state.workers[1].x+1.8,state.workers[1].z-.7)<.5);assert.equal(state.workers[0].x,-8.7);
 // Real rectangle selection of two units.
 const p1=await project(state.workers[0],.65),p3=await project(state.workers[2],.65);
 await page.mouse.move(Math.min(p1.x,p3.x)-24,Math.min(p1.y,p3.y)-24);await page.mouse.down();await page.mouse.move(Math.max(p1.x,p3.x)+24,Math.max(p1.y,p3.y)+24,{steps:12});await page.mouse.up();
 assert.deepEqual((await snapshot()).selected.sort(),[1,3]);
 await page.getByRole('button',{name:'1 号',exact:true}).click();await page.getByRole('button',{name:'采集 / 砍伐',exact:true}).click();
 await clickAt({x:3.2,z:-3},'left',.8);
 await waitState(()=>window.__workers.sim.workers[0].phase==='work');await page.screenshot({path:'output/workers/chopping.png'});
 await waitState(()=>window.__workers.sim.workers[0].cargo?.kind==='wood');assert.equal((await snapshot()).inventory.wood,0);
 await page.screenshot({path:'output/workers/hauling.png'});
 // Stop while loaded, then resume delivery through UI.
 await page.getByRole('button',{name:'停止',exact:true}).click();const stopped=(await snapshot()).workers[0];await page.waitForTimeout(300);state=await snapshot();assert.equal(state.workers[0].x,stopped.x);assert.equal(state.workers[0].cargo.amount,6);
 await page.getByRole('button',{name:'送回仓库',exact:true}).click();await waitState(()=>window.__workers.sim.inventory.wood>=6);await waitState(()=>window.__workers.sim.workers[0].phase==='idle');
 assert.equal((await snapshot()).workers[0].source,null);
 // Two independent concurrent jobs via right-click.
 await page.getByRole('button',{name:'1 号',exact:true}).click();await clickAt({x:-.8,z:-3.8},'right',.4);
 await page.getByRole('button',{name:'2 号',exact:true}).click();await clickAt({x:6.2,z:3.7},'right',.4);
 state=await snapshot();assert.equal(state.workers[0].source,104);assert.equal(state.workers[1].source,103);
 await waitState(()=>window.__workers.sim.inventory.food>=6&&window.__workers.sim.inventory.ore>=6);
 await page.screenshot({path:'output/workers/working.png'});
 await page.getByRole('button',{name:'暂停',exact:true}).click();const before=JSON.stringify((await snapshot()).workers);await page.waitForTimeout(500);assert.equal(JSON.stringify((await snapshot()).workers),before);
 await page.getByRole('button',{name:'继续',exact:true}).click();
 await page.getByRole('button',{name:'重置',exact:true}).click();await page.waitForSelector('#field[data-ready=true]');assert.equal((await snapshot()).inventory.wood,0);
 // Select default groups, save custom selection and recall it using keyboard.
 await page.getByRole('button',{name:'第 2 组',exact:true}).click();assert.deepEqual((await snapshot()).selected,[5,6,7,8]);
 await page.getByRole('button',{name:'1 号',exact:true}).click();await page.keyboard.press('Control+3');
 await page.getByRole('button',{name:'全选 12 人',exact:true}).click();assert.equal((await snapshot()).selected.length,12);
 await page.keyboard.press('3');assert.deepEqual((await snapshot()).selected,[1]);
 // All twelve move in formation using actual mouse command.
 await page.getByRole('button',{name:'全选 12 人',exact:true}).click();await clickAt({x:2,z:0},'right');
 await waitState(()=>window.__workers.sim.workers.every(w=>w.phase==='idle'),45000);
 await page.screenshot({path:'output/workers/formation.png'});
 // Shift appends a movement order and executes it after the first destination.
 await page.getByRole('button',{name:'1 号',exact:true}).click();await clickAt({x:-1.8,z:.7},'right');
 await page.keyboard.down('Shift');await clickAt({x:3,z:0},'right');await page.keyboard.up('Shift');
 assert.equal((await snapshot()).workers[0].queue.length,1);
 await waitState(()=>window.__workers.sim.workers[0].phase==='idle',30000);
 assert.ok(Math.abs((await snapshot()).workers[0].x-3)<.4);
 // Mixed jobs controlled entirely through group buttons.
 await page.getByRole('button',{name:'第 1 组',exact:true}).click();await clickAt({x:3.2,z:-3},'right',.8);
 await page.getByRole('button',{name:'第 2 组',exact:true}).click();await clickAt({x:6.2,z:3.7},'right',.4);
 await page.getByRole('button',{name:'9 号',exact:true}).click();await page.keyboard.press('Shift');await page.getByRole('button',{name:'10 号',exact:true}).click({modifiers:['Shift']});await page.keyboard.up('Shift');
 await clickAt({x:-.8,z:-3.8},'right',.4);
 await waitState(()=>window.__workers.sim.inventory.wood>=6&&window.__workers.sim.inventory.ore>=6&&window.__workers.sim.inventory.food>=6,60000);
 await page.screenshot({path:'output/workers/multiplayer-jobs.png'});
 await page.getByRole('button',{name:'重置',exact:true}).click();await page.waitForSelector('#field[data-ready=true]');
 // Narrow screen plus button-armed interaction, same as touch controls.
 await page.setViewportSize({width:390,height:844});await page.waitForTimeout(300);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth),390);
 await page.getByRole('button',{name:'4 号',exact:true}).click();await page.getByRole('button',{name:'移动',exact:true}).click();await clickAt({x:1.6,z:0});
 await waitState(()=>window.__workers.sim.workers[3].phase==='idle');assert.ok(Math.hypot((await snapshot()).workers[3].x-1.6,(await snapshot()).workers[3].z)<.5);
 await page.screenshot({path:'output/workers/mobile.png'});
 assert.deepEqual(errors,[]);
 await fs.writeFile('output/workers/browser-check.json',JSON.stringify({pass:true,checks:['boot','single selection','right-click move','independent command','rectangle select','button-armed work','chopping','visible cargo','stop preserves load','deposit','concurrent jobs','pause','reset','mobile movement','no page errors','12-unit formation','default groups','custom group save/recall','Shift queued movement','three simultaneous jobs'],errors},null,2));
 console.log('PASS: browser selection, move, jobs, cargo, deposit, pause, reset, mobile');
} finally {await browser.close();}

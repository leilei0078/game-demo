import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
const out = new URL('../output/play/web/', import.meta.url);
await mkdir(out, { recursive: true });
const qa = JSON.parse(await readFile(new URL('../output/play/qa.json', import.meta.url), 'utf8'));
for (const [name, report] of Object.entries(qa)) {
  assert.equal(report.assembly.pass, true, name + ' assembly QA');
  assert.deepEqual(report.assembly.warnings, [], name + ' assembly warnings');
  for (const component of Object.values(report.components)) assert.equal(component.pass, true);
}
const browser = await chromium.launch({ executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', headless: true, args: ['--use-angle=metal', '--disable-background-timer-throttling'] });
const errors = [], results = [];
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1 });
page.on('pageerror', e => errors.push(e.message));
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
const record = message => { results.push(message); console.log('PASS', message); };
const snapshot = name => page.screenshot({ path: new URL(name + '.png', out).pathname });
const clickWorld = async (point, target = true) => {
  // Project the actual game coordinates, then use normal pointer input.
  const p = await page.evaluate(point => window.__game.project(point), point);
  assert.ok(p.x > 0 && p.x < 1440 && p.y > 100 && p.y < 850, `Target on playfield: ${JSON.stringify(p)}`);
  await page.mouse.click(p.x, p.y);
  if (target) await page.waitForTimeout(100);
};
try {
  await page.goto('http://localhost:5173/adventure.html');
  await page.locator('#game[data-ready="true"]').waitFor({ timeout: 60000 });
  await page.waitForTimeout(1500);
  await snapshot('01-start'); record('GLB assets and Rapier loaded; first playable view');
  const start = await page.evaluate(() => ({ ...window.__game.state.player }));
  await page.keyboard.down('KeyS'); await page.waitForTimeout(900); await page.keyboard.up('KeyS');
  const after = await page.evaluate(() => ({ ...window.__game.state.player }));
  assert.ok(Math.hypot(start.x - after.x, start.z - after.z) > 1.6); record('WASD moves player in screen direction');
  await page.keyboard.press('Escape');
  const pausedAt = await page.evaluate(() => window.__game.state.elapsed);
  await page.waitForTimeout(450); assert.equal(await page.evaluate(() => window.__game.state.elapsed), pausedAt);
  await page.getByRole('button', { name: '继续旅程' }).click(); record('Pause freezes simulation and resumes');
  await page.keyboard.press('KeyE');
  assert.ok(await page.evaluate(() => window.__game.state.cooldowns.dash > 3)); record('Dash consumes energy and starts cooldown');
  for (const id of ['cedar-1', 'cedar-2', 'cedar-3']) {
    const r = await page.evaluate(id => window.__game.state.resources.find(r => r.id === id), id);
    await clickWorld(r);
    await page.waitForFunction(id => window.__game.state.resources.find(r => r.id === id).depleted, id, { timeout: 24000 });
    record('Click to approach and harvest ' + id);
  }
  assert.equal(await page.locator('#wood-count').textContent(), '3 / 3'); await snapshot('02-timber');
  // The two shores are connected through the bridge. No teleport for the quest run.
  await clickWorld({ x: 6.2, z: 1.1 }, false);
  await page.waitForFunction(() => window.__game.state.player.x > 5.8, null, { timeout: 30000 });
  record('A* route crosses bridge with Rapier river colliders'); await snapshot('03-bridge');
  for (const id of ['copper-1', 'copper-2']) {
    const r = await page.evaluate(id => window.__game.state.resources.find(r => r.id === id), id);
    await clickWorld(r);
    await page.waitForFunction(id => window.__game.state.resources.find(r => r.id === id).depleted, id, { timeout: 20000 });
    record('Click to approach and mine ' + id);
  }
  await snapshot('04-mining');
  for (const id of ['boar-0', 'boar-1']) {
    const b = await page.evaluate(id => window.__game.state.boars.find(b => b.id === id), id);
    await clickWorld(b);
    await page.waitForFunction(id => window.__game.state.boars.find(b => b.id === id).hp <= 0, id, { timeout: 26000 });
    record('Click to approach, attack and defeat ' + id);
  }
  assert.equal(await page.locator('#boar-count').textContent(), '2 / 2'); await snapshot('05-combat');
  assert.equal(await page.locator('#return-hint').isVisible(), true);
  // Walk to the bridge's east approach, then the camp.
  await clickWorld({ x: 5.5, z: 1.1 }, false);
  await page.waitForFunction(() => Math.hypot(window.__game.state.player.x - 5.5, window.__game.state.player.z - 1.1) < .6, null, { timeout: 30000 });
  await clickWorld({ x: .7, z: 2.4 }, false);
  await page.waitForFunction(() => Math.hypot(window.__game.state.player.x - .7, window.__game.state.player.z - 2.4) < 1.3, null, { timeout: 30000 });
  await page.keyboard.press('KeyF');
  await page.waitForFunction(() => window.__game.state.complete);
  assert.equal(await page.evaluate(() => window.__game.state.inventory.silver), 120);
  await snapshot('06-complete'); record('Full quest completed via normal input, reward 120 silver');
  await page.locator('#resume').click();
  const end = await page.evaluate(() => ({ ...window.__game.state.player }));
  await page.keyboard.down('KeyS'); await page.waitForTimeout(700); await page.keyboard.up('KeyS');
  assert.ok(await page.evaluate(end => Math.hypot(window.__game.state.player.x - end.x, window.__game.state.player.z - end.z) > 1, end));
  assert.equal(await page.evaluate(() => window.__game.state.inventory.silver), 120); record('Completed quest permits exploration without duplicate rewards');

  // Isolated collision / recovery checks use an explicit development-only fixture.
  await page.reload(); await page.locator('#game[data-ready="true"]').waitFor();
  await page.evaluate(() => window.__game.teleport({ x: -2.65, z: 1.3 }));
  await page.waitForTimeout(600);
  await page.keyboard.down('KeyW'); await page.waitForTimeout(1400); await page.keyboard.up('KeyW');
  const collision = await page.evaluate(() => ({ ...window.__game.state.player }));
  assert.ok(collision.z > .65 || collision.x < -5 || collision.x > -.1, 'Cannot enter cottage'); record('Cottage blocks movement');
  await page.evaluate(() => window.__game.teleport({ x: 8.2, z: -4.6 }));
  await page.waitForFunction(() => window.__game.state.player.hp < 100, null, { timeout: 7000 });
  const hp = await page.evaluate(() => window.__game.state.player.hp);
  await page.keyboard.press('KeyR');
  assert.ok(await page.evaluate(hp => window.__game.state.player.hp > hp && window.__game.state.player.potions === 2, hp)); record('Enemy damage and healing potion');
  await page.keyboard.press('KeyQ');
  assert.ok(await page.evaluate(() => window.__game.state.cooldowns.cleave > 5)); record('Cleave damages enemies and starts cooldown');
  await snapshot('07-action');
  await page.waitForFunction(() => window.__game.state.deaths > 0, null, { timeout: 24000 });
  await page.waitForFunction(() => window.__game.state.player.hp === 100 && window.__game.state.player.x < 1, null, { timeout: 5000 });
  record('Defeat returns player safely to camp');
  await page.getByRole('button', { name: '操作指南' }).click(); await snapshot('08-help');
  await page.locator('#resume').click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload(); await page.locator('#game[data-ready="true"]').waitFor();
  await page.waitForTimeout(800); await snapshot('09-mobile');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  const mobileStart = await page.evaluate(() => ({ ...window.__game.state.player }));
  const btn = page.locator('[data-dir="KeyD"]');
  const box = await btn.boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await page.mouse.down(); await page.waitForTimeout(800); await page.mouse.up();
  const mobileEnd = await page.evaluate(() => ({ ...window.__game.state.player }));
  assert.ok(Math.hypot(mobileEnd.x - mobileStart.x, mobileEnd.z - mobileStart.z) > 1.3); record('Mobile layout and on-screen movement pad');
  await page.goto('http://localhost:5173/'); await page.locator('#scene[data-ready="true"]').waitFor({ timeout: 60000 });
  assert.equal(await page.getByRole('link', { name: '进入游戏' }).isVisible(), true); record('Existing scene still loads and links to playable demo');
  assert.deepEqual(errors, [], 'No runtime / shader / network errors');
} finally {
  await writeFile(new URL('report.json', out), JSON.stringify({ results, errors, state: await page.evaluate(() => window.__game?.state).catch(() => null) }, null, 2));
  await snapshot('last-state'); await browser.close();
}

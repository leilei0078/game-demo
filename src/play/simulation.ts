export type Point = { x: number; z: number };
export type Resource = Point & { id: string; kind: 'wood' | 'ore'; name: string; hp: number; maxHp: number; depleted: boolean };
export type Boar = Point & { id: string; home: Point; hp: number; maxHp: number; phase: 'idle' | 'chase' | 'windup' | 'recover' | 'dead'; timer: number; facing: number; strike: Point };
export type Obstacle = Point & { hx: number; hz: number };
export const CAMP: Point = { x: .7, z: 2.4 };
export const GOAL = { wood: 3, ore: 2, boars: 2 };
export const obstacles: Obstacle[] = [
  { x: -2.65, z: -1.55, hx: 2.12, hz: 1.91 },
  { x: 1, z: .74, hx: .80, hz: .8 },
  { x: -.1, z: -1.9, hx: .7, hz: 1.3 },
];
export const resourceLayout = [
  { id: 'cedar-1', kind: 'wood', name: '雪松', x: -6.8, z: 3.4 },
  { id: 'cedar-2', kind: 'wood', name: '雪松', x: -7.6, z: -.8 },
  { id: 'cedar-3', kind: 'wood', name: '雪松', x: -5.9, z: 6.6 },
  { id: 'copper-1', kind: 'ore', name: '铜矿脉', x: 7.1, z: 3.9 },
  { id: 'copper-2', kind: 'ore', name: '铜矿脉', x: 9.5, z: .3 },
] as const;
export const distance = (a: Point, b: Point) => Math.hypot(a.x - b.x, a.z - b.z);
export const riverX = (z: number) => 3.85 + Math.sin((z - 1.1) * .22) * .24;
export function passable(x: number, z: number, margin = .32) {
  if (Math.abs(x) > 15.6 || Math.abs(z) > 15.6) return false;
  if (Math.abs(x - riverX(z)) < 1.54 && Math.abs(z - 1.1) > .20) return false;
  if (resourceLayout.some(r => Math.hypot(x - r.x, z - r.z) < (r.kind === 'wood' ? .23 : .53) + margin)) return false;
  return !obstacles.some(o => Math.abs(x - o.x) < o.hx + margin && Math.abs(z - o.z) < o.hz + margin);
}

/** A* on a quarter-meter navigation grid, including the narrow bridge approach. */
export function findPath(from: Point, to: Point): Point[] {
  const step = .25, N = 129, origin = -16;
  const cell = (p: Point) => ({ x: Math.round((p.x - origin) / step), z: Math.round((p.z - origin) / step) });
  const point = (i: number) => ({ x: origin + (i % N) * step, z: origin + Math.floor(i / N) * step });
  const start = cell(from), end = cell(to);
  const id = (x: number, z: number) => z * N + x;
  const si = id(start.x, start.z);
  // Snap an obstructed target to the closest navigable cell.
  let target = -1, best = Infinity;
  for (let dz = -5; dz <= 5; dz++) for (let dx = -5; dx <= 5; dx++) {
    const x = end.x + dx, z = end.z + dz;
    if (x < 0 || x >= N || z < 0 || z >= N) continue;
    const i = id(x, z), p = point(i), d = distance(p, to);
    // The bridge center lies at z=1.1; allow the z=1 grid row through it.
    if (passable(p.x, p.z) && d < best) { target = i; best = d; }
  }
  if (target < 0) return [];
  const open = new Set([si]), closed = new Set<number>();
  const g = new Map([[si, 0]]), parent = new Map<number, number>();
  let iterations = 0;
  while (open.size && iterations++ < 17000) {
    let current = -1, score = Infinity;
    for (const i of open) {
      const f = g.get(i)! + distance(point(i), point(target));
      if (f < score) { current = i; score = f; }
    }
    if (current === target) {
      const result: Point[] = [];
      while (current !== si) { result.unshift(point(current)); current = parent.get(current)!; }
      return result;
    }
    open.delete(current); closed.add(current);
    const cx = current % N, cz = Math.floor(current / N);
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      if (!dx && !dz) continue;
      const x = cx + dx, z = cz + dz;
      if (x < 0 || x >= N || z < 0 || z >= N) continue;
      const i = id(x, z), p = point(i);
      if (closed.has(i) || !passable(p.x, p.z)) continue;
      if (dx && dz && (!passable(point(id(cx + dx, cz)).x, point(id(cx + dx, cz)).z) || !passable(point(id(cx, cz + dz)).x, point(id(cx, cz + dz)).z))) continue;
      const cost = g.get(current)! + step * Math.hypot(dx, dz);
      if (cost < (g.get(i) ?? Infinity)) { g.set(i, cost); parent.set(i, current); open.add(i); }
    }
  }
  return [];
}

export function newState() {
  return {
    player: { x: .55, z: 3.8, hp: 100, facing: 0, energy: 100, potions: 3 },
    resources: resourceLayout.map(r => ({ ...r, hp: 3, maxHp: 3, depleted: false })) as Resource[],
    boars: [{ x: 8.2, z: -4.6 }, { x: 11.2, z: 6.7 }].map((p, i) => ({ ...p, id: 'boar-' + i, home: { ...p }, hp: 90, maxHp: 90, phase: 'idle', timer: 0, facing: 0, strike: { ...p } })) as Boar[],
    inventory: { wood: 0, ore: 0, boars: 0, silver: 0 },
    cooldowns: { attack: 0, cleave: 0, dash: 0, harvest: 0 },
    elapsed: 0, paused: false, complete: false, deaths: 0,
  };
}
export type GameState = ReturnType<typeof newState>;
export const suppliesReady = (s: GameState) => s.inventory.wood >= GOAL.wood && s.inventory.ore >= GOAL.ore && s.inventory.boars >= GOAL.boars;

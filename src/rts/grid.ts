// Grid boundaries are integer world coordinates. A cell is one meter wide.
export const CELL_SIZE = 1;
export type GridPoint = { x: number; z: number };
export type FootprintKind = 'keep' | 'house' | 'barracks' | 'tower' | 'forge' | 'lumber' | 'enemy';
export const FOOTPRINTS: Record<FootprintKind, readonly [number, number]> = {
  keep: [8, 8], house: [4, 4], barracks: [6, 5], tower: [4, 4],
  forge: [5, 5], lumber: [5, 4], enemy: [6, 5],
};
export function footprint(kind: FootprintKind, point: GridPoint) {
  const [width, depth] = FOOTPRINTS[kind];
  const minX = Math.floor(point.x / CELL_SIZE - width / 2 + .5);
  const minZ = Math.floor(point.z / CELL_SIZE - depth / 2 + .5);
  return {
    minX, minZ, maxX: minX + width, maxZ: minZ + depth, width, depth,
    x: (minX + width / 2) * CELL_SIZE, z: (minZ + depth / 2) * CELL_SIZE,
  };
}
export const snapBuilding = (kind: FootprintKind, point: GridPoint): GridPoint => {
  const f = footprint(kind, point); return { x: f.x, z: f.z };
};
export function distanceToFootprint(kind: FootprintKind, center: GridPoint, p: GridPoint) {
  const f = footprint(kind, center);
  return Math.hypot(Math.max(f.minX - p.x, 0, p.x - f.maxX), Math.max(f.minZ - p.z, 0, p.z - f.maxZ));
}

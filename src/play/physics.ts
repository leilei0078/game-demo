import RAPIER from '@dimforge/rapier3d-compat';
import { obstacles, resourceLayout, riverX } from './simulation';

export async function createPhysics() {
  await RAPIER.init();
  const world = new RAPIER.World({ x: 0, y: -9.81, z: 0 });
  world.timestep = 1 / 60;
  const ground = world.createCollider(RAPIER.ColliderDesc.cuboid(60, .15, 60).setTranslation(0, .11, 0));
  const obstacle = (x: number, z: number, hx: number, hz: number) => world.createCollider(
    RAPIER.ColliderDesc.cuboid(hx, 1.8, hz).setTranslation(x, 1.8, z));
  for (const o of obstacles) obstacle(o.x, o.z, o.hx, o.hz);
  for (const r of resourceLayout) {
    world.createCollider(RAPIER.ColliderDesc.cylinder(.9, r.kind === 'wood' ? .23 : .53).setTranslation(r.x, .9, r.z));
  }
  for (let z = -17; z < 17; z += .25) {
    if (z >= .55 && z <= 1.6) continue;
    obstacle(riverX(z), z, .91, .126);
  }
  const bridgeVertices: number[] = [];
  const bridgeIndices: number[] = [];
  for (const [x, height] of [[2.12, .264], [2.92, .59], [4.82, .59], [5.48, .264]]) {
    bridgeVertices.push(x, height, .62, x, height, 1.58);
  }
  for (let i = 0; i < 3; i++) {
    const a = i * 2;
    bridgeIndices.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
  }
  world.createCollider(RAPIER.ColliderDesc.trimesh(new Float32Array(bridgeVertices), new Uint32Array(bridgeIndices)));
  obstacle(-16.5, 0, .5, 18); obstacle(16.5, 0, .5, 18);
  obstacle(0, -16.5, 18, .5); obstacle(0, 16.5, 18, .5);
  const body = world.createRigidBody(RAPIER.RigidBodyDesc.kinematicPositionBased().setTranslation(.55, 1.12, 3.8));
  const collider = world.createCollider(RAPIER.ColliderDesc.capsule(.5, .28), body);
  const controller = world.createCharacterController(.012);
  controller.setSlideEnabled(true);
  controller.enableSnapToGround(.3);
  controller.enableAutostep(.45, .20, true);
  controller.setApplyImpulsesToDynamicBodies(false);
  world.step();
  return {
    world, body, ground,
    move(dx: number, dz: number) {
      controller.computeColliderMovement(collider, { x: dx, y: -.02, z: dz });
      const d = controller.computedMovement(), p = body.translation();
      body.setNextKinematicTranslation({ x: p.x + d.x, y: p.y + d.y, z: p.z + d.z });
      world.step();
      return body.translation();
    },
    teleport(x: number, z: number) {
      body.setTranslation({ x, y: 1.12, z }, true);
      body.setNextKinematicTranslation({ x, y: 1.12, z });
      world.step();
    },
  };
}

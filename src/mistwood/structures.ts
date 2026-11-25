import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { makeSurface, addGrainUV } from './materials';

/** A lean-to over the existing supplies; every beam reaches a post or the cottage. */
export function addLeanTo(scene:THREE.Scene) {
  const group=new THREE.Group();group.name='Mistwood / attached supplies shelter';
  const timber=makeSurface('wood','#67503a'),boards=makeSurface('wood','#6e6857');
  function beam(name:string,a:THREE.Vector3,b:THREE.Vector3,width:number,material=timber,depth=width) {
    const length=a.distanceTo(b),geometry=new THREE.BoxGeometry(width,length,depth);
    addGrainUV(geometry,new THREE.Matrix4());
    const mesh=new THREE.Mesh(geometry,material);
    mesh.name=name;mesh.position.copy(a).add(b).multiplyScalar(.5);
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0,1,0),b.clone().sub(a).normalize());
    mesh.castShadow=true;mesh.receiveShadow=true;group.add(mesh);
    return mesh;
  }
  const v=(x:number,y:number,z:number)=>new THREE.Vector3(x,y,z);
  for(const z of [-3.02,-.20]) {
    beam('Shelter / outer post',v(1.12,.265,z),v(1.12,2.16,z),.135);
    beam('Shelter / wall ledger',v(-.83,2.72,z),v(-.83,2.72,z+.15),.13);
    beam('Shelter / sloping rafter',v(-.88,2.76,z),v(1.26,2.14,z),.125);
    beam('Shelter / knee brace',v(1.12,1.65,z),v(.60,2.32,z),.09);
  }
  beam('Shelter / outer header',v(1.12,2.12,-3.12),v(1.12,2.12,-.10),.16);
  beam('Shelter / wall header',v(-.83,2.73,-3.12),v(-.83,2.73,-.10),.15);
  // Roof boards rest directly on the sloping rafters and both headers.
  for(let i=0;i<22;i++) {
    const z=-3.18+i*.146;
    beam('Shelter / weathered roof board',v(-.96,2.825,z),v(1.36,2.15,z),.055,boards,.142);
  }
  group.updateMatrixWorld(true);
  for(const material of [timber,boards]) {
    const sources=group.children.filter((child):child is THREE.Mesh=>child instanceof THREE.Mesh && child.material===material);
    const geometries=sources.map(mesh=>{
      mesh.visible=false;
      return mesh.geometry.clone().applyMatrix4(mesh.matrixWorld);
    });
    const mesh=new THREE.Mesh(mergeGeometries(geometries)!,material);
    mesh.name=material===timber?'Shelter / assembled frame':'Shelter / assembled roof';
    mesh.castShadow=true;mesh.receiveShadow=true;group.add(mesh);
    geometries.forEach(geometry=>geometry.dispose());
  }
  scene.add(group);
  return group;
}

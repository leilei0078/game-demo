import * as THREE from 'three';
import { createBridgeApproach, roadDistance } from './roads';
import { scenePalette } from './palette';

/** Surround the clearing with continuous terrain and several layers of woodland. */
export function addForest(scene: THREE.Scene, materials: {
  grass: THREE.MeshStandardMaterial; bank: THREE.MeshStandardMaterial;
  water: THREE.MeshStandardMaterial;
}) {
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(180,180),materials.grass);
  ground.name='Forest / continuous meadow';
  ground.rotation.x=-Math.PI/2;
  ground.position.y=.264;
  ground.receiveShadow=true;
  scene.add(ground);

  function track(name: string,coords: number[][],width: number | number[],y: number,material: THREE.Material) {
    const curve=new THREE.CatmullRomCurve3(coords.map(p=>new THREE.Vector3(p[0],y,p[1])));
    const points=curve.getPoints(100),positions:number[]=[],indices:number[]=[];
    for(let i=0;i<points.length;i++) {
      const tangent=points[Math.min(i+1,points.length-1)].clone().sub(points[Math.max(i-1,0)]).normalize();
      const widthT=i/(points.length-1)*(Array.isArray(width)?width.length-1:0);
      const wi=Math.floor(widthT);
      const localWidth=Array.isArray(width)?THREE.MathUtils.lerp(width[wi],width[Math.min(wi+1,width.length-1)],widthT-wi):width;
      const side=new THREE.Vector3(-tangent.z,0,tangent.x).multiplyScalar(localWidth/2);
      positions.push(...points[i].clone().add(side).toArray(),...points[i].clone().sub(side).toArray());
      if(i<points.length-1) indices.push(i*2,i*2+2,i*2+1,i*2+1,i*2+2,i*2+3);
    }
    const geometry=new THREE.BufferGeometry();
    geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
    geometry.setIndex(indices);geometry.computeVertexNormals();
    const mesh=new THREE.Mesh(geometry,material);
    mesh.name=name;mesh.receiveShadow=true;scene.add(mesh);
  }
  for(const [name,points] of [
    ['Creek into the forest',[[3,-4.7],[2.9,-6],[2.8,-9],[2.1,-13],[2.8,-22],[1.8,-35]]],
    ['Creek past the clearing',[[3.6,4.25],[3.5,6],[3.8,8],[3.5,12],[4.0,22],[3.8,35]]],
  ] as [string,number[][]][]) {
    track(name+' / bank',points,1.48,.278,materials.bank);
    track(name+' / water',points,1.02,.282,materials.water);
  }
  scene.add(createBridgeApproach(materials.grass));

  let seed=928;
  const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
  const trees:{x:number;z:number;height:number;angle:number}[]=[];
  for(let z=-29;z<26;z+=2.8) for(let x=-29;x<29;x+=2.8) {
    const xx=x+(random()-.5)*1.7,zz=z+(random()-.5)*1.7;
    const radius=Math.hypot(xx/6.85,zz/5.35);
    if(radius<1.34 || (zz>2 && Math.abs(xx)<6.4)) continue;
    if(roadDistance(xx,zz)<1.5) continue;
    if(Math.abs(xx-3)<1.65 || random()<.29) continue;
    trees.push({x:xx,z:zz,height:3.1+random()*2.6,angle:random()*Math.PI});
  }
  const cone=new THREE.ConeGeometry(1,1,7);
  const trunkGeometry=new THREE.CylinderGeometry(.065,.10,1,7);
  const bark=new THREE.MeshStandardMaterial({color:'#51412d',roughness:1});
  const trunk=new THREE.InstancedMesh(trunkGeometry,bark,trees.length);
  trunk.name='Forest / trunks';
  const palettes=scenePalette.forest;
  const dummy=new THREE.Object3D();
  for(let level=0;level<3;level++) {
    const foliage=new THREE.MeshStandardMaterial({color:palettes[level],roughness:1});
    const crowns=new THREE.InstancedMesh(cone,foliage,trees.length);
    crowns.name=`Forest / canopy layer ${level+1}`;
    trees.forEach((t,i)=>{
      dummy.position.set(t.x,.264+t.height*(.36+level*.22),t.z);
      dummy.rotation.set(0,t.angle+level*.4,0);
      const radius=t.height*(.29-level*.065);
      dummy.scale.set(radius,t.height*.49,radius);
      dummy.updateMatrix();crowns.setMatrixAt(i,dummy.matrix);
      crowns.setColorAt(i,new THREE.Color().setScalar(.78+random()*.37));
      if(level===0) {
        dummy.position.set(t.x,.264+t.height*.28,t.z);
        dummy.scale.set(t.height,t.height*.56,t.height);
        dummy.updateMatrix();trunk.setMatrixAt(i,dummy.matrix);
      }
    });
    crowns.castShadow=true;crowns.receiveShadow=true;
    crowns.computeBoundingSphere();scene.add(crowns);
  }
  trunk.castShadow=true;trunk.receiveShadow=true;
  trunk.computeBoundingSphere();scene.add(trunk);
}

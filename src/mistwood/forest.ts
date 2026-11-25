import * as THREE from 'three';
import { createBridgeApproach, roadDistance } from '../roads';
import { makeSurface } from './materials';
import { needleTexture, needleClusterGeometry, seededRandom, addUndergrowth } from './plants';

type Tree = { x:number; z:number; h:number; r:number; hero:boolean };

export function addMistwoodForest(scene:THREE.Scene, materials:{grass:THREE.MeshStandardMaterial;bank:THREE.MeshStandardMaterial;water:THREE.MeshStandardMaterial}) {
  const ground=new THREE.Mesh(new THREE.PlaneGeometry(180,180),materials.grass);
  ground.name='Mistwood / continuous forest floor';ground.rotation.x=-Math.PI/2;
  ground.position.y=.264;ground.receiveShadow=true;scene.add(ground);
  scene.add(createBridgeApproach(materials.grass));
  function creek(coords:number[][],width:number,y:number,material:THREE.Material) {
    const curve=new THREE.CatmullRomCurve3(coords.map(([x,z])=>new THREE.Vector3(x,y,z)));
    const points=curve.getPoints(100),v:number[]=[],indices:number[]=[];
    points.forEach((p,i)=>{
      const t=points[Math.min(i+1,100)].clone().sub(points[Math.max(i-1,0)]).normalize();
      const s=new THREE.Vector3(-t.z,0,t.x).multiplyScalar(width/2);
      v.push(...p.clone().add(s).toArray(),...p.clone().sub(s).toArray());
      if(i<100) indices.push(i*2,i*2+2,i*2+1,i*2+1,i*2+2,i*2+3);
    });
    const geo=new THREE.BufferGeometry();geo.setAttribute('position',new THREE.Float32BufferAttribute(v,3));
    geo.setIndex(indices);geo.computeVertexNormals();
    const mesh=new THREE.Mesh(geo,material);mesh.receiveShadow=true;scene.add(mesh);
  }
  for(const path of [
    [[3,-4.7],[2.8,-8],[2.1,-13],[2.8,-22],[1.8,-40]],
    [[3.6,4.25],[3.5,6],[3.8,8],[3.5,12],[4.0,22],[3.8,40]],
  ]) {
    creek(path,1.48,.278,materials.bank);creek(path,1.02,.282,materials.water);
  }
  const random=seededRandom(741);
  const trees:Tree[]=[
    {x:-7.2,z:3.8,h:18,r:.72,hero:true},
    {x:7.8,z:-1.6,h:20,r:.78,hero:true},
    {x:-6.7,z:-5.5,h:17,r:.57,hero:true},
    {x:.2,z:-8.0,h:19,r:.48,hero:true},
    {x:-6.7,z:9.0,h:17,r:.67,hero:true},
    {x:10.3,z:6.5,h:21,r:.83,hero:true},
  ];
  for(let z=-29;z<28;z+=3.5) for(let x=-29;x<29;x+=3.5) {
    const xx=x+(random()-.5)*2.2,zz=z+(random()-.5)*2.2;
    if(Math.hypot(xx/6.3,zz/5.3)<1.45) continue;
    // A sightline into the clearing, framed by the deliberately placed foreground giants.
    if(zz>1 && Math.abs((xx+1.5)*.81-(zz-1.1)*.585)<6.7) continue;
    if(Math.abs(xx-3.3)<1.7 || roadDistance(xx,zz)<1.1 || random()<.28) continue;
    if(trees.some(t=>Math.hypot(t.x-xx,t.z-zz)<2.8)) continue;
    trees.push({x:xx,z:zz,h:12+random()*10,r:.25+random()*.26,hero:false});
  }
  const bark=makeSurface('bark','#544b3c');
  bark.name='Mistwood / fissured bark and basal moss';
  const trunkGeo=new THREE.CylinderGeometry(.46,1,1,18,14);
  const tp=trunkGeo.attributes.position;
  for(let i=0;i<tp.count;i++) {
    const y=tp.getY(i),a=Math.atan2(tp.getZ(i),tp.getX(i));
    const swell=1+(Math.sin(a*7+y*9)*.045+Math.cos(a*11-y*4)*.032)+(1-THREE.MathUtils.smoothstep(y,-.5,-.30))*.25;
    tp.setX(i,tp.getX(i)*swell);tp.setZ(i,tp.getZ(i)*swell);
  }
  trunkGeo.computeVertexNormals();
  const trunks=new THREE.InstancedMesh(trunkGeo,bark,trees.length);
  trunks.name='Mistwood / mature conifer trunks';
  const branchMatrices:THREE.Matrix4[]=[],rootMatrices:THREE.Matrix4[]=[],needles:THREE.Matrix4[]=[];
  const dummy=new THREE.Object3D(),up=new THREE.Vector3(0,1,0);
  function segment(list:THREE.Matrix4[],a:THREE.Vector3,b:THREE.Vector3,r1:number,r2?:number) {
    const d=b.clone().sub(a);
    dummy.position.copy(a).add(b).multiplyScalar(.5);
    dummy.quaternion.setFromUnitVectors(up,d.clone().normalize());
    dummy.scale.set(r1,d.length(),r2??r1);dummy.updateMatrix();list.push(dummy.matrix.clone());
  }
  trees.forEach((tree,i)=>{
    dummy.position.set(tree.x,.264+tree.h/2,tree.z);dummy.rotation.set(0,random()*6.28,0);
    dummy.scale.set(tree.r,tree.h,tree.r);dummy.updateMatrix();trunks.setMatrixAt(i,dummy.matrix);
    trunks.setColorAt(i,new THREE.Color().setScalar(.75+random()*.45));
    for(let j=0;j<(tree.hero?9:5);j++) {
      const a=j*2.399+random()*.5,len=tree.r*(2.3+random()*1.2);
      const p0=new THREE.Vector3(tree.x,.35+tree.r*.52,tree.z);
      const p1=new THREE.Vector3(tree.x+Math.cos(a)*len*.5,.34,tree.z+Math.sin(a)*len*.5);
      const p2=new THREE.Vector3(tree.x+Math.cos(a)*len,.24,tree.z+Math.sin(a)*len);
      segment(rootMatrices,p0,p1,tree.r*.26);segment(rootMatrices,p1,p2,tree.r*.12);
    }
    const levels=tree.hero?12:9;
    for(let level=0;level<levels;level++) {
      const f=level/(levels-1),y=tree.h*(.43+f*.51);
      const reach=(1-f*.83)*tree.h*.16;
      for(let branch=0;branch<5;branch++) {
        const a=branch*1.256+level*2.399+random()*.35;
        const dir=new THREE.Vector3(Math.cos(a),0,Math.sin(a));
        const start=new THREE.Vector3(tree.x,y,tree.z);
        const bend=start.clone().addScaledVector(dir,reach*.55).add(new THREE.Vector3(0,-.28,0));
        const end=start.clone().addScaledVector(dir,reach).add(new THREE.Vector3(0,-.48+f*.3,0));
        segment(branchMatrices,start,bend,tree.r*(.16-f*.10));
        segment(branchMatrices,bend,end,tree.r*(.08-f*.05));
        for(let spray=0;spray<3;spray++) {
          const t=.38+spray*.24;
          dummy.position.copy(start).lerp(end,t);
          dummy.rotation.set((random()-.5)*.6,a+random()*.6,(random()-.5)*.35);
          const s=(.68+random()*.5)*(1-f*.58);
          dummy.scale.set(s*1.5,s*.9,s);dummy.updateMatrix();needles.push(dummy.matrix.clone());
        }
      }
    }
    if(tree.hero) for(let j=0;j<4;j++) {
      const a=j*2.4+1,y=3.8+j*1.2;
      const end=new THREE.Vector3(tree.x+Math.cos(a)*1.8,y+.15,tree.z+Math.sin(a)*1.8);
      segment(branchMatrices,new THREE.Vector3(tree.x,y,tree.z),end,tree.r*.13);
      segment(branchMatrices,end.clone().lerp(new THREE.Vector3(tree.x,y,tree.z),.28),end.clone().add(new THREE.Vector3(.35,.45,.1)),tree.r*.055);
    }
  });
  const segmentGeo=new THREE.CylinderGeometry(.42,1,1,7);
  const roots=new THREE.InstancedMesh(segmentGeo,bark,rootMatrices.length);
  const branches=new THREE.InstancedMesh(segmentGeo,bark,branchMatrices.length);
  roots.name='Mistwood / connected buttress roots';branches.name='Mistwood / connected branch structure';
  rootMatrices.forEach((m,i)=>roots.setMatrixAt(i,m));branchMatrices.forEach((m,i)=>branches.setMatrixAt(i,m));
  const texture=needleTexture();
  const crownMaterial=new THREE.MeshStandardMaterial({map:texture,color:'#b2b79e',roughness:.91,
    alphaTest:.32,side:THREE.DoubleSide,shadowSide:THREE.DoubleSide});
  crownMaterial.name='Mistwood / alpha-cut conifer needles';
  const crowns=new THREE.InstancedMesh(needleClusterGeometry(),crownMaterial,needles.length);
  crowns.name='Mistwood / individual needle sprays';needles.forEach((m,i)=>{
    crowns.setMatrixAt(i,m);crowns.setColorAt(i,new THREE.Color().setScalar(.7+random()*.55));
  });
  for(const mesh of [trunks,roots,branches,crowns]) {
    mesh.castShadow=mesh===trunks || mesh===roots;
    mesh.receiveShadow=true;mesh.computeBoundingSphere();scene.add(mesh);
  }
  addUndergrowth(scene);
  return {trees:trees.length,needleSprays:needles.length,branches:branchMatrices.length,roots:rootMatrices.length};
}

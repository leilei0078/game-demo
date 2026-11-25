import * as THREE from 'three';
import { roadDistance } from '../roads';

export function seededRandom(seed: number) {
  return () => { seed=(seed*1664525+1013904223)>>>0; return seed/4294967296; };
}

/** A photographed-style needle spray painted into a local alpha texture. */
export function needleTexture() {
  const canvas=document.createElement('canvas'); canvas.width=512; canvas.height=256;
  const c=canvas.getContext('2d')!,random=seededRandom(717);
  function twig(x:number,y:number,length:number,angle:number,depth:number) {
    const ex=x+Math.cos(angle)*length,ey=y+Math.sin(angle)*length;
    c.strokeStyle=depth===0?'#4f5035':'#455331';c.lineWidth=depth===0?2.8:1.5;
    c.beginPath();c.moveTo(x,y);c.lineTo(ex,ey);c.stroke();
    const count=Math.floor(length/3);
    for(let i=0;i<count;i++) {
      const t=i/count,px=x+(ex-x)*t,py=y+(ey-y)*t;
      for(const side of [-1,1]) {
        const a=angle+side*(.45+random()*.5),l=7+random()*11;
        c.strokeStyle=['#617b45','#748756','#475e37','#8c985c'][Math.floor(random()*4)];
        c.lineWidth=1.8+random()*1.4;c.beginPath();c.moveTo(px,py);c.lineTo(px+Math.cos(a)*l,py+Math.sin(a)*l);c.stroke();
      }
    }
    if(depth===0) for(let i=1;i<9;i++) {
      const t=i/10;
      for(const side of [-1,1]) twig(x+(ex-x)*t,y+(ey-y)*t,(1-t)*75+20,angle+side*(.4+random()*.4),1);
    }
  }
  twig(17,118,448,.035,0);
  const texture=new THREE.CanvasTexture(canvas);texture.colorSpace=THREE.SRGBColorSpace;
  texture.anisotropy=8;
  return texture;
}

export function needleClusterGeometry() {
  const positions:number[]=[],uv:number[]=[],indices:number[]=[];
  for(let i=0;i<3;i++) {
    const a=i*Math.PI/3,dx=Math.cos(a),dz=Math.sin(a),start=positions.length/3;
    positions.push(-dx*.55,0,-dz*.55,dx*.55,0,dz*.55,dx*.55,.52,dz*.55,-dx*.55,.52,-dz*.55);
    uv.push(0,0,1,0,1,1,0,1);indices.push(start,start+1,start+2,start,start+2,start+3);
  }
  const geometry=new THREE.BufferGeometry();
  geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
  geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));
  geometry.setIndex(indices);geometry.computeVertexNormals();
  return geometry;
}

function fernGeometry() {
  const v:number[]=[],colors:number[]=[],indices:number[]=[];
  const random=seededRandom(926);
  function triangle(a:THREE.Vector3,b:THREE.Vector3,c:THREE.Vector3,color:number[]) {
    const n=v.length/3;v.push(...a.toArray(),...b.toArray(),...c.toArray());
    colors.push(...color,...color,...color);indices.push(n,n+1,n+2);
  }
  for(let frond=0;frond<9;frond++) {
    const a=frond*2.399,len=.55+random()*.37;
    const forward=new THREE.Vector3(Math.cos(a),0,Math.sin(a));
    const side=new THREE.Vector3(-Math.sin(a),0,Math.cos(a));
    const curve=(t:number)=>forward.clone().multiplyScalar(t*len).add(new THREE.Vector3(0,Math.sin(t*Math.PI*.8)*len*.48,0));
    for(let j=0;j<12;j++) {
      const t=(j+1)/14,center=curve(t),next=curve(t+.07);
      triangle(center.clone().addScaledVector(side,.005),center.clone().addScaledVector(side,-.005),next,[.14,.23,.052]);
      for(const sign of [-1,1]) {
        const l=Math.sin(t*Math.PI)*len*.20;
        const tip=center.clone().addScaledVector(side,l*sign).addScaledVector(forward,.045);
        const base=center.clone().addScaledVector(forward,.037);
        const col=[.045+random()*.035,.10+random()*.09,.023+random()*.02];
        triangle(center,tip.clone().add(new THREE.Vector3(0,.018,0)),base,col);
        triangle(base,tip,center.clone().addScaledVector(forward,.07),col);
      }
    }
  }
  const geo=new THREE.BufferGeometry();
  geo.setAttribute('position',new THREE.Float32BufferAttribute(v,3));
  geo.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));
  geo.setIndex(indices);geo.computeVertexNormals();return geo;
}

export function clearForPlant(x:number,z:number) {
  if(x>-5.15 && x<1.45 && z>-3.7 && z<.5) return false;
  if(x>2.6 && x<4.85) return false;
  if(Math.hypot(x-1,z-.74)<1.2) return false;
  if(roadDistance(x,z)<.32) return false;
  return true;
}

export function addUndergrowth(scene:THREE.Scene) {
  const random=seededRandom(941),dummy=new THREE.Object3D();
  const ferns=new THREE.InstancedMesh(fernGeometry(),new THREE.MeshStandardMaterial({
    color:0xffffff,vertexColors:true,roughness:.84,side:THREE.DoubleSide,
  }),280);
  ferns.name='Mistwood / rooted ferns';
  let count=0;
  for(let i=0;i<4000 && count<280;i++) {
    const x=(random()-.5)*44,z=(random()-.5)*42;
    if(!clearForPlant(x,z)) continue;
    dummy.position.set(x,.27,z);dummy.rotation.set(0,random()*Math.PI*2,0);
    dummy.scale.setScalar(.5+random()*.8);dummy.updateMatrix();
    ferns.setMatrixAt(count++,dummy.matrix);
  }
  ferns.count=count;ferns.receiveShadow=true;ferns.computeBoundingSphere();scene.add(ferns);

  const geo=new THREE.BufferGeometry();
  geo.setAttribute('position',new THREE.Float32BufferAttribute([-.5,0,0,0,.10,-.5,.5,0,0,0,.10,.5],3));
  geo.setIndex([0,1,2,0,2,3]);geo.computeVertexNormals();
  const leaves=new THREE.InstancedMesh(geo,new THREE.MeshStandardMaterial({color:0xffffff,side:THREE.DoubleSide,roughness:.95}),2600);
  leaves.name='Mistwood / fallen leaves';
  count=0;
  for(let i=0;i<9000 && count<2600;i++) {
    const x=(random()-.5)*29,z=(random()-.5)*27;
    if(x>-4.8 && x<1.25 && z>-3.6 && z<.1) continue;
    if(x>2.7 && x<4.8) continue;
    if(Math.hypot(x-1,z-.74)<.8) continue;
    if(roadDistance(x,z)<-.42 && random()<.85) continue;
    dummy.position.set(x,.272+random()*.008,z);dummy.rotation.set(0,random()*Math.PI,0);
    const s=.035+random()*.07;dummy.scale.set(s,.07,s*1.6);dummy.updateMatrix();
    leaves.setMatrixAt(count,dummy.matrix);
    leaves.setColorAt(count,new THREE.Color().setRGB(.075+random()*.07,.045+random()*.035,.014+random()*.015));
    count++;
  }
  leaves.count=count;leaves.receiveShadow=true;leaves.computeBoundingSphere();scene.add(leaves);
}

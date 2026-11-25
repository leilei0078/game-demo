import * as THREE from 'three';

type RoadSample = { point: THREE.Vector3; halfWidth: number };

// Coverage, vegetation exclusion and the bridge approach share these centre lines.
const definitions = [
  { points: [[-2.6,.15],[-2.6,2],[-1.9,2.9],[-1.4,3.7],[-.6,4.5],[-.25,5.1],
      [.7,6.9],[.2,10],[-2,15],[-1,25]],
    widths: [.96,.94,1.0,1.16,1.3,1.45,1.45,1.45,1.45,1.45], samples: 260 },
  { points: [[-2.15,2.65],[-1.35,2.63],[-.15,2.42],[.95,1.99],[1.95,1.35],[2.48,1.10],[2.91,1.095]],
    widths: [.98,.94,.88,.84,.87,.89,.89], samples: 100 },
];
const roads: RoadSample[][] = definitions.map(({points,widths,samples})=>{
  const curve=new THREE.CatmullRomCurve3(points.map(p=>new THREE.Vector3(p[0],0,p[1])));
  return curve.getPoints(samples).map((point,i)=>{
    const t=i/samples*(widths.length-1), index=Math.floor(t);
    return {point,halfWidth:THREE.MathUtils.lerp(widths[index],widths[Math.min(index+1,widths.length-1)],t-index)/2};
  });
});

function segmentDistance(x:number,z:number,a:RoadSample,b:RoadSample) {
  const dx=b.point.x-a.point.x,dz=b.point.z-a.point.z;
  const t=THREE.MathUtils.clamp(((x-a.point.x)*dx+(z-a.point.z)*dz)/(dx*dx+dz*dz),0,1);
  return Math.hypot(x-a.point.x-dx*t,z-a.point.z-dz*t)-THREE.MathUtils.lerp(a.halfWidth,b.halfWidth,t);
}
function union(a:number,b:number) {
  const k=.24, h=Math.max(k-Math.abs(a-b),0)/k;
  return Math.min(a,b)-h*h*k*.25;
}
export function roadDistance(x:number,z:number) {
  const distances=roads.map(road=>{
    let distance=100;
    for(let i=1;i<road.length;i++) distance=Math.min(distance,segmentDistance(x,z,road[i-1],road[i]));
    return distance;
  });
  return union(distances[0],distances[1]);
}

/** Merge the junction once, as a signed distance field read by the ground shader. */
export function createRoadMask() {
  const width=768,height=1536,rect=new THREE.Vector4(-8,-6,16,32);
  const fields=roads.map(road=>{
    const field=new Float32Array(width*height).fill(10);
    for(let i=1;i<road.length;i++) {
      const a=road[i-1],b=road[i],margin=Math.max(a.halfWidth,b.halfWidth)+.30;
      const x0=Math.max(0,Math.floor((Math.min(a.point.x,b.point.x)-margin-rect.x)/rect.z*width));
      const x1=Math.min(width-1,Math.ceil((Math.max(a.point.x,b.point.x)+margin-rect.x)/rect.z*width));
      const z0=Math.max(0,Math.floor((Math.min(a.point.z,b.point.z)-margin-rect.y)/rect.w*height));
      const z1=Math.min(height-1,Math.ceil((Math.max(a.point.z,b.point.z)+margin-rect.y)/rect.w*height));
      for(let z=z0;z<=z1;z++) for(let x=x0;x<=x1;x++) {
        const index=z*width+x;
        field[index]=Math.min(field[index],segmentDistance(
          rect.x+(x+.5)/width*rect.z,rect.y+(z+.5)/height*rect.w,a,b));
      }
    }
    return field;
  });
  const pixels=new Uint8Array(width*height);
  for(let i=0;i<pixels.length;i++) pixels[i]=Math.round(THREE.MathUtils.clamp(.5-union(fields[0][i],fields[1][i])/.48,0,1)*255);
  const texture=new THREE.DataTexture(pixels,width,height,THREE.RedFormat,THREE.UnsignedByteType);
  texture.name='Road network / unified signed distance';
  texture.minFilter=THREE.LinearFilter;
  texture.magFilter=THREE.LinearFilter;
  texture.generateMipmaps=false;
  texture.needsUpdate=true;
  return {texture,rect};
}

/** A compacted bank rises gradually to the wooden deck, with sloping shoulders. */
export function createBridgeApproach(material:THREE.Material) {
  const vertices:number[]=[],indices:number[]=[],along=28,across=16;
  for(let i=0;i<=along;i++) {
    const t=i/along,x=2.05+t*.815,rise=THREE.MathUtils.smoothstep(t,0,1)*.315;
    for(let j=0;j<=across;j++) {
      const v=j/across*2-1,shoulder=1-THREE.MathUtils.smoothstep(Math.abs(v),.57,1);
      vertices.push(x,.271+rise*shoulder,1.095+(1-t)*.16+v*.68);
      if(i<along && j<across) {
        const a=i*(across+1)+j,b=a+across+1;
        indices.push(a,a+1,b,a+1,b+1,b);
      }
    }
  }
  const geometry=new THREE.BufferGeometry();
  geometry.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();
  const ramp=new THREE.Mesh(geometry,material);
  ramp.name='Road / earth approach to bridge';
  ramp.receiveShadow=true;
  return ramp;
}

import * as THREE from 'three';

type Surface = 'wood' | 'bark' | 'stone' | 'plaster' | 'roof' | 'metal';
const detailGLSL = /* glsl */`
varying vec3 vDetailWorld;
varying vec2 vDetailUV;
float grainHash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float grainNoise(vec2 p) {
  vec2 i=floor(p),f=fract(p); f=f*f*(3.0-2.0*f);
  float value=mix(mix(grainHash(i),grainHash(i+vec2(1,0)),f.x),
             mix(grainHash(i+vec2(0,1)),grainHash(i+vec2(1,1)),f.x),f.y);
  vec2 footprint=max(abs(dFdx(p)),abs(dFdy(p)));
  return mix(value,.5,smoothstep(.25,1.0,max(footprint.x,footprint.y)));
}
float grainFbm(vec2 p) {
  return grainNoise(p)*.56+grainNoise(p*2.03+5.4)*.28+grainNoise(p*4.11-7.2)*.16;
}
vec3 detailBump(vec3 surface,vec3 surfaceNormal,float height) {
  vec3 dx=dFdx(surface),dy=dFdy(surface);
  vec3 r1=cross(dy,surfaceNormal),r2=cross(surfaceNormal,dx);
  float det=dot(dx,r1);
  return normalize(abs(det)*surfaceNormal-sign(det)*(dFdx(height)*r1+dFdy(height)*r2));
}
`;

export function finishSurface(material: THREE.MeshStandardMaterial, kind: Surface) {
  material.customProgramCacheKey = () => `mistwood-detail-3-${kind}`;
  material.onBeforeCompile = shader => {
    shader.vertexShader = 'varying vec3 vDetailWorld;\nvarying vec2 vDetailUV;\n' + shader.vertexShader;
    shader.vertexShader = shader.vertexShader.replace('#include <begin_vertex>', /* glsl */`
      #include <begin_vertex>
      vec4 detailPosition=vec4(transformed,1.0);
      #ifdef USE_INSTANCING
        detailPosition=instanceMatrix*detailPosition;
      #endif
      vDetailWorld=(modelMatrix*detailPosition).xyz;
      vDetailUV=uv;
    `);
    // The procedural grain UV exists even on materials without image textures.
    shader.vertexShader = '#define USE_UV\n' + shader.vertexShader;
    shader.fragmentShader = detailGLSL + shader.fragmentShader;
    const colors = kind === 'wood' ? /* glsl */`
      vec2 g=vDetailUV;
      float warp=grainFbm(vec2(g.x*8.0,g.y*.8));
      float grain=grainFbm(vec2(g.x*110.0+warp*5.0,g.y*3.0));
      float fissure=pow(1.0-grainNoise(vec2(g.x*48.0+warp*2.0,g.y*1.7)),7.0);
      float stain=grainFbm(vDetailWorld.xz*2.4+vDetailWorld.y*.2);
      diffuseColor.rgb*=mix(.55,1.12,grain)*(1.0-fissure*.55);
      diffuseColor.rgb=mix(diffuseColor.rgb,diffuseColor.rgb*vec3(.52,.67,.65),stain*.32);
      float detailHeight=(grain-fissure*.7)*.003;
    ` : kind === 'bark' ? /* glsl */`
      vec3 p=vDetailWorld;
      vec2 g=vec2(p.x+p.z*.73,p.y);
      float warp=grainFbm(g*vec2(3.0,.6));
      float plate=grainFbm(g*vec2(37.0,4.0)+vec2(warp*3.0,0));
      float crack=pow(1.0-grainNoise(g*vec2(22.0,1.8)+vec2(warp*2.0,0)),5.0);
      float fine=grainNoise(g*vec2(150.0,23.0));
      diffuseColor.rgb*=mix(.28,1.45,plate)*(1.0-crack*.8)*(.8+fine*.4);
      float moss=(1.0-smoothstep(.6,3.6,p.y))*smoothstep(.37,.70,grainFbm(p.xz*5.2+p.y*.5));
      diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.045,.075,.019)*(.6+plate),moss*.86);
      float detailHeight=(plate-crack)*.035+fine*.005;
    ` : kind === 'roof' ? /* glsl */`
      vec3 p=vDetailWorld;
      float grain=grainFbm(p.xz*28.0+p.y*11.0);
      float streak=grainNoise(vec2(p.z*43.0,p.x*3.2));
      float weather=grainFbm(p.xz*3.0);
      diffuseColor.rgb*=.4+grain*.85;
      diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.035,.057,.035),smoothstep(.52,.75,weather)*.8);
      float detailHeight=grain*.003+streak*.001;
    ` : kind === 'plaster' ? /* glsl */`
      vec3 p=vDetailWorld;
      float broad=grainFbm(p.xy*2.5+p.z*3.0);
      float fine=grainNoise(p.xy*110.0+p.z*70.0);
      float damp=(1.0-smoothstep(.4,1.9,p.y))*smoothstep(.28,.7,broad);
      diffuseColor.rgb*=.62+broad*.5+fine*.08;
      diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.055,.075,.059),damp*.7);
      float detailHeight=broad*.008+fine*.002;
    ` : kind === 'metal' ? /* glsl */`
      vec3 p=vDetailWorld;
      float grain=grainFbm(p.xy*30.0+p.z*21.0);
      diffuseColor.rgb*=.55+grain*.6;
      float detailHeight=grain*.002;
    ` : /* glsl */`
      vec3 p=vDetailWorld;
      float broad=grainFbm(p.xz*6.0+p.y*4.0);
      float grain=grainNoise(p.xy*87.0+p.z*72.0);
      diffuseColor.rgb*=.47+broad*.75+grain*.16;
      float upward=clamp(inverseTransformDirection(normalize(vNormal),viewMatrix).y,0.0,1.0);
      float moss=smoothstep(.43,.68,grainFbm(p.xz*5.5+p.y*.4))*upward;
      diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.046,.085,.025)*(.7+broad*.5),moss*.75);
      float detailHeight=broad*.006+grain*.001;
    `;
    shader.fragmentShader = shader.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n' + colors);
    shader.fragmentShader = shader.fragmentShader.replace('#include <normal_fragment_maps>', /* glsl */`
      #include <normal_fragment_maps>
      normal=detailBump(-vViewPosition,normal,detailHeight);
    `);
  };
  return material;
}

export function makeSurface(kind: Surface, color: THREE.ColorRepresentation) {
  return finishSurface(new THREE.MeshStandardMaterial({ color, roughness: kind === 'metal' ? .65 : .92,
    metalness: kind === 'metal' ? .55 : 0 }), kind);
}

const materialCache = new Map<THREE.Material, THREE.MeshStandardMaterial>();
export function weatherMaterial(source: THREE.MeshStandardMaterial) {
  if (materialCache.has(source)) return materialCache.get(source)!;
  const material = source.clone();
  materialCache.set(source,material);
  const name = source.name;
  let kind: Surface = 'wood';
  if (/stone|limestone|mortar/i.test(name)) {
    kind='stone'; material.color.setRGB(.21,.24,.24);
    const variant=Number(name.slice(-2));
    if(Number.isFinite(variant)) material.color.multiplyScalar(.9+variant*.035);
  } else if(name.startsWith('roof')) {
    kind='roof'; material.color.setRGB(.26,.11,.07);
    if(name==='roof_light') material.color.multiplyScalar(1.18);
    if(name==='roof_dark') material.color.multiplyScalar(.67);
  } else if(name.startsWith('plaster')) {
    kind='plaster'; material.color.setRGB(.40,.39,.32);
  } else if(/iron|steel/i.test(name)) {
    kind='metal'; material.color.setRGB(.065,.085,.09);
  } else if(name==='soil') {
    kind='stone'; material.color.setRGB(.045,.035,.021);
  } else if(/coal/.test(name)) {
    kind='stone'; material.color.setRGB(.012,.010,.009);
  } else {
    material.color.multiplyScalar(.54);
    if(name==='timber') material.color.setRGB(.074,.050,.033);
  }
  return finishSurface(material,kind);
}

/** Put longitudinal wood grain along each authored part before batching it. */
export function addGrainUV(geometry: THREE.BufferGeometry, matrix: THREE.Matrix4) {
  geometry.computeBoundingBox();
  const size=geometry.boundingBox!.getSize(new THREE.Vector3());
  const scale=new THREE.Vector3().setFromMatrixScale(matrix);
  const extent=[size.x*scale.x,size.y*scale.y,size.z*scale.z];
  const axis=extent.indexOf(Math.max(...extent));
  const s=scale.toArray();
  const p=geometry.attributes.position,uv=new Float32Array(p.count*2);
  for(let i=0;i<p.count;i++) {
    const v=[p.getX(i),p.getY(i),p.getZ(i)];
    uv[i*2]=v[(axis+1)%3]*s[(axis+1)%3]+v[(axis+2)%3]*s[(axis+2)%3]*.73;
    uv[i*2+1]=v[axis]*s[axis];
  }
  geometry.setAttribute('uv',new THREE.BufferAttribute(uv,2));
}

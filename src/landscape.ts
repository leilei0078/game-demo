import * as THREE from 'three';
import { createRoadMask, roadDistance } from './roads';

// World-space patterns stay continuous across the exported material boundaries.
const surfaceGLSL = /* glsl */`
varying vec3 vSurfaceWorld;
uniform float uLandscapeTime;
uniform float uLandscapeNight;
uniform sampler2D uRoadMask;
uniform vec4 uRoadRect;
float fieldHash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float fieldNoise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  f = f*f*(3.0-2.0*f);
  return mix(mix(fieldHash(i),fieldHash(i+vec2(1,0)),f.x),
             mix(fieldHash(i+vec2(0,1)),fieldHash(i+vec2(1,1)),f.x),f.y);
}
float fieldFbm(vec2 p) {
  return fieldNoise(p)*0.58 + fieldNoise(p*2.07+4.3)*0.28 + fieldNoise(p*4.13-2.1)*0.14;
}
// Match the authored creek centerline (Blender Y becomes negative Web Z).
float creekCenter(float z) {
  return 3.0 + .4*smoothstep(-4.7,-3.7,z) + .5*smoothstep(-3.7,-2.7,z)
    -.2*smoothstep(-2.7,-1.7,z) + .45*smoothstep(-1.7,-.5,z)
    -.35*smoothstep(-.5,.8,z) + .3*smoothstep(.8,2.0,z)
    -.4*smoothstep(2.0,3.2,z) -.1*smoothstep(3.2,4.25,z);
}
`;

type LandscapeKind = 'grass' | 'bank' | 'water';

export function createLandscape(renderer: THREE.WebGLRenderer) {
  const roadMask=createRoadMask();
  const uniforms = { uLandscapeTime: { value: 0 }, uLandscapeNight: { value: 0 },
    uRoadMask: { value: roadMask.texture }, uRoadRect: { value: roadMask.rect } };
  const water = new THREE.MeshPhysicalMaterial({
    color: 'white', roughness: .29, metalness: 0, ior: 1.333,
    clearcoat: .25, clearcoatRoughness: .31, specularIntensity: .7,
  });
  water.name = 'Creek / depth, flow & sky';
  const grass = new THREE.MeshStandardMaterial({ color: 'white', roughness: 1 });
  grass.name = 'Meadow / continuous moss & grass';
  const bank = new THREE.MeshStandardMaterial({ color: 'white', roughness: .95 });
  bank.name = 'Bank / damp silt & sand';

  function shade(material: THREE.MeshStandardMaterial, kind: LandscapeKind) {
    material.customProgramCacheKey = () => `landscape-roads-v2-${kind}`;
    material.onBeforeCompile = shader => {
      Object.assign(shader.uniforms, uniforms);
      shader.vertexShader = 'varying vec3 vSurfaceWorld;\n' + shader.vertexShader;
      shader.vertexShader = shader.vertexShader.replace('#include <begin_vertex>', `
        #include <begin_vertex>
        vSurfaceWorld = (modelMatrix * vec4(position, 1.0)).xyz;
      `);
      shader.fragmentShader = surfaceGLSL + shader.fragmentShader;
      const colors = kind === 'grass' ? /* glsl */`
        vec2 p = vSurfaceWorld.xz;
        float broad = fieldFbm(p*.67);
        float clumps = fieldFbm(p*3.4 + broad*1.5);
        vec3 moss = vec3(.048,.145,.080);
        vec3 meadow = vec3(.105,.245,.120);
        vec3 sunGrass = vec3(.19,.325,.145);
        vec3 groundColor = mix(moss,meadow,smoothstep(.22,.74,broad));
        groundColor = mix(groundColor,sunGrass,smoothstep(.50,.78,clumps)*.55);
        float fine = fieldNoise(p*31.0);
        groundColor *= .95 + fine*.10;
        // Moist, darker vegetation beside the creek, with an irregular edge.
        float bankDistance = abs(p.x-creekCenter(p.y));
        float damp = (1.0-smoothstep(.65,1.18,bankDistance+fieldNoise(p*5.0)*.13))
          * step(-4.7,p.y)*step(p.y,4.3);
        groundColor = mix(groundColor,groundColor*vec3(.70,.83,.76),damp*.50);
        diffuseColor.rgb = groundColor;
        // A single ground surface carries both roads and their rounded junction.
        vec2 roadUV=(p-uRoadRect.xy)/uRoadRect.zw;
        float roadSample=texture2D(uRoadMask,clamp(roadUV,0.0,1.0)).r;
        float roadDistance=(.5-roadSample)*.48;
        float edgeNoise=(fieldNoise(p*9.0)-.5)*.065+(fieldNoise(p*24.0)-.5)*.025;
        float onRoad=1.0-smoothstep(-.028,.060,roadDistance+edgeNoise);
        onRoad*=step(0.0,roadUV.x)*step(roadUV.x,1.0)*step(0.0,roadUV.y)*step(roadUV.y,1.0);
        float wear=fieldFbm(p*2.2), grain=fieldNoise(p*37.0);
        vec3 roadColor=mix(vec3(.23,.205,.155),vec3(.33,.285,.215),wear);
        roadColor*=.94+grain*.12;
        roadColor+=pow(fieldNoise(p*48.0),15.0)*.06*vec3(.58,.52,.38);
        float verge=1.0-smoothstep(-.16,-.015,roadDistance);
        roadColor=mix(roadColor,roadColor*.84,verge*.42);
        diffuseColor.rgb=mix(groundColor,roadColor,onRoad);
      ` : kind === 'bank' ? /* glsl */`
        vec2 p = vSurfaceWorld.xz;
        float grains = fieldFbm(p*10.0);
        float wet = 1.0-smoothstep(.48,.77,abs(p.x-creekCenter(p.y)) + (grains-.5)*.05);
        vec3 drySand = mix(vec3(.23,.245,.20),vec3(.32,.33,.265),grains);
        vec3 wetSilt = vec3(.07,.135,.12)*( .85 + grains*.3 );
        vec3 bankColor = mix(drySand,wetSilt,wet*.87);
        float fringe = smoothstep(.60,.85,abs(p.x-creekCenter(p.y))+(grains-.5)*.16);
        diffuseColor.rgb = mix(bankColor,vec3(.09,.21,.10),fringe*.8);
      ` : /* glsl */`
        vec2 p = vSurfaceWorld.xz;
        float edge = clamp(abs(p.x-creekCenter(p.y))/.54,0.0,1.0);
        float bed = fieldFbm(vec2(p.x*8.0,p.y*4.0));
        float depth = (1.0-smoothstep(.18,1.0,edge))*(.79+bed*.21);
        vec3 shallows = vec3(.105,.285,.26);
        vec3 deepWater = vec3(.018,.125,.155);
        vec3 waterColor = mix(shallows,deepWater,depth);
        // Soft bed pebbles remain visible in the shallows and disappear at depth.
        vec2 bedUV=p*vec2(8.0,6.0), cell=floor(bedUV), local=fract(bedUV);
        float nearest=2.0;
        for(int iy=-1;iy<=1;iy++) for(int ix=-1;ix<=1;ix++) {
          vec2 offset=vec2(float(ix),float(iy));
          vec2 seed=cell+offset;
          vec2 stone=offset+vec2(fieldHash(seed),fieldHash(seed+13.7))-local;
          nearest=min(nearest,length(stone*vec2(.8,1.0)));
        }
        float pebbles=1.0-smoothstep(.13,.30,nearest);
        waterColor=mix(waterColor,shallows*.74,pebbles*pow(1.0-depth,1.4)*.34);
        // Broad, low-contrast streaks suggest a bed seen through flowing water.
        float flow = fieldFbm(vec2(p.x*7.0,p.y*2.0-uLandscapeTime*.12));
        waterColor *= .92+flow*.16;
        float ripple = sin(p.y*16.0+p.x*5.0-uLandscapeTime*1.0+fieldNoise(p*2.0)*3.0);
        float glint = pow(max(0.0,ripple),18.0)*.018*(.4+depth*.6);
        diffuseColor.rgb = waterColor+vec3(.65,.81,.70)*glint;
      `;
      shader.fragmentShader = shader.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n' + colors);
      if (kind === 'water') shader.fragmentShader = shader.fragmentShader.replace('#include <normal_fragment_maps>', /* glsl */`
        #include <normal_fragment_maps>
        vec2 q = vSurfaceWorld.xz;
        float slow = uLandscapeTime*.65;
        float nx = .075*cos(q.x*14.0+q.y*4.0-slow)+.025*sin(q.y*21.0+slow*.7);
        float nz = .055*sin(q.y*17.0-q.x*3.0-slow*.8)+.02*cos(q.x*25.0+slow);
        normal = normalize(mat3(viewMatrix)*vec3(nx,1.0,nz));
        nonPerturbedNormal = normal;
      `);
    };
  }
  shade(grass,'grass'); shade(bank,'bank'); shade(water,'water');

  // A soft sky reflection, prefiltered once; no remote HDRI or extra downloads.
  const skyScene = new THREE.Scene();
  const skyGeometry = new THREE.SphereGeometry(20,32,16);
  const skyMaterial = new THREE.ShaderMaterial({
    side: THREE.BackSide,
    vertexShader: 'varying vec3 vSky; void main(){vSky=position; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',
    fragmentShader: `varying vec3 vSky; void main(){
      vec3 d=normalize(vSky);
      vec3 sky=mix(vec3(.34,.45,.49),vec3(.10,.23,.36),smoothstep(0.0,.9,d.y));
      float cloud=pow(max(0.0,dot(d,normalize(vec3(-.4,.85,.3)))),10.0);
      gl_FragColor=vec4(sky+vec3(.68,.76,.84)*cloud,1.0);
    }`,
  });
  skyScene.add(new THREE.Mesh(skyGeometry,skyMaterial));
  const pmrem = new THREE.PMREMGenerator(renderer);
  const sky = pmrem.fromScene(skyScene,.10,.1,100);
  water.envMap = sky.texture;
  water.envMapIntensity = .32;
  skyGeometry.dispose(); skyMaterial.dispose(); pmrem.dispose();

  return {
    grass, bank, water,
    update(time: number, night: number) {
      uniforms.uLandscapeTime.value = time;
      uniforms.uLandscapeNight.value = night;
      water.envMapIntensity = THREE.MathUtils.lerp(.32,.09,night);
    },
  };
}

export function addMeadowTufts(parent: THREE.Group) {
  const vertices: number[] = [];
  for (let i=0;i<3;i++) {
    const a=i*Math.PI/3, dx=Math.cos(a), dz=Math.sin(a);
    vertices.push(-dx*.028,0,-dz*.028, dx*.028,0,dz*.028, dx*.032,.20,dz*.032);
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position',new THREE.Float32BufferAttribute(vertices,3));
  geometry.computeVertexNormals();
  const material = new THREE.MeshStandardMaterial({ color:'white',roughness:1,side:THREE.DoubleSide });
  const instances = new THREE.InstancedMesh(geometry,material,800);
  instances.name = 'Meadow / clustered grass tufts';
  instances.userData.region = 'woodland';
  const dummy = new THREE.Object3D();
  let seed=417;
  const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
  let count=0;
  for (let attempt=0;attempt<6000 && count<800;attempt++) {
    const x=(random()-.5)*13.0,z=(random()-.5)*10.0;
    if ((x/6.55)**2+(z/5.0)**2>.96) continue;
    if (x>-4.65 && x<-.65 && z> -3.6 && z<.55) continue;
    if (x>2.7 && x<4.85) continue;
    if (Math.hypot(x-1,z-.74)<.97) continue;
    if (Math.hypot(x-.55,z-2.65)<.6) continue;
    if (roadDistance(x,z)<.13) continue;
    const cluster=Math.sin(x*2.2+Math.cos(z*1.7))*Math.sin(z*2.5-x*.3);
    if (random()>.48+cluster*.4) continue;
    const s=.38+random()*.48;
    dummy.position.set(x,.272,z);
    dummy.rotation.set(0,random()*Math.PI,0);
    dummy.scale.set(s,s*(.65+random()*.5),s);
    dummy.updateMatrix();
    instances.setMatrixAt(count,dummy.matrix);
    const color=new THREE.Color().setRGB(.065+random()*.06,.20+random()*.085,.075+random()*.035);
    instances.setColorAt(count,color);
    count++;
  }
  instances.count=count;
  instances.instanceMatrix.needsUpdate=true;
  instances.receiveShadow=true;
  instances.castShadow=false;
  instances.computeBoundingSphere();
  parent.add(instances);
}

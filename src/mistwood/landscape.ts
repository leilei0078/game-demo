import * as THREE from 'three';
import { createRoadMask, roadDistance } from '../roads';

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

export function createMistwoodLandscape(renderer: THREE.WebGLRenderer) {
  const roadMask=createRoadMask();
  const uniforms = { uLandscapeTime: { value: 0 }, uLandscapeNight: { value: 0 },
    uRoadMask: { value: roadMask.texture }, uRoadRect: { value: roadMask.rect } };
  const water = new THREE.MeshPhysicalMaterial({
    color: 'white', roughness: .19, metalness: 0, ior: 1.333,
    clearcoat: .6, clearcoatRoughness: .31, specularIntensity: .7,
  });
  water.name = 'Creek / depth, flow & sky';
  const grass = new THREE.MeshStandardMaterial({ color: 'white', roughness: 1 });
  grass.name = 'Meadow / continuous moss & grass';
  const bank = new THREE.MeshStandardMaterial({ color: 'white', roughness: .95 });
  bank.name = 'Bank / damp silt & sand';

  function shade(material: THREE.MeshStandardMaterial, kind: LandscapeKind) {
    material.customProgramCacheKey = () => `mistwood-floor-v1-${kind}`;
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
        vec3 moss = vec3(.020,.040,.022);
        vec3 meadow = vec3(.048,.069,.031);
        vec3 sunGrass = vec3(.080,.100,.043);
        vec3 groundColor = mix(moss,meadow,smoothstep(.22,.74,broad));
        groundColor = mix(groundColor,sunGrass,smoothstep(.50,.78,clumps)*.55);
        float fine = fieldNoise(p*31.0);
        float mulch=fieldFbm(p*12.0);
        groundColor*=.6+fine*.28+mulch*.35;
        vec2 leafUV=p*vec2(17.0,19.0);
        vec2 leafCell=floor(leafUV),leafLocal=fract(leafUV)-.5;
        float leafAngle=fieldHash(leafCell)*6.283;
        leafLocal=mat2(cos(leafAngle),sin(leafAngle),-sin(leafAngle),cos(leafAngle))*leafLocal;
        float leaf=1.0-smoothstep(.33,.46,length(leafLocal/vec2(.32,.75)));
        leaf*=step(.80,fieldHash(leafCell+8.0));
        groundColor=mix(groundColor,vec3(.080,.043,.017),leaf*.65);
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
        vec3 roadColor=mix(vec3(.075,.058,.038),vec3(.135,.105,.068),wear);
        roadColor*=.94+grain*.12;
        roadColor+=pow(fieldNoise(p*48.0),15.0)*.06*vec3(.58,.52,.38);
        float verge=1.0-smoothstep(-.16,-.015,roadDistance);
        roadColor=mix(roadColor,roadColor*.84,verge*.42);
        diffuseColor.rgb=mix(groundColor,roadColor,onRoad);
      ` : kind === 'bank' ? /* glsl */`
        vec2 p = vSurfaceWorld.xz;
        float grains = fieldFbm(p*10.0);
        float wet = 1.0-smoothstep(.48,.77,abs(p.x-creekCenter(p.y)) + (grains-.5)*.05);
        vec3 drySand = mix(vec3(.035,.044,.028),vec3(.080,.075,.048),grains);
        vec3 wetSilt = vec3(.026,.036,.027)*( .85 + grains*.3 );
        vec3 bankColor = mix(drySand,wetSilt,wet*.87);
        float fringe = smoothstep(.60,.85,abs(p.x-creekCenter(p.y))+(grains-.5)*.16);
        diffuseColor.rgb = mix(bankColor,vec3(.038,.059,.028),fringe*.8);
      ` : /* glsl */`
        vec2 p = vSurfaceWorld.xz;
        float edge = clamp(abs(p.x-creekCenter(p.y))/.54,0.0,1.0);
        float bed = fieldFbm(vec2(p.x*8.0,p.y*4.0));
        float depth = (1.0-smoothstep(.18,1.0,edge))*(.79+bed*.21);
        vec3 shallows = vec3(.070,.11,.105);
        vec3 deepWater = vec3(.009,.034,.040);
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
      if (kind !== 'water') shader.fragmentShader = shader.fragmentShader.replace('#include <normal_fragment_maps>', `
        #include <normal_fragment_maps>
        float a=fieldNoise(vSurfaceWorld.xz*95.0);
        float b=fieldNoise(vSurfaceWorld.xz*95.0+vec2(.18,0));
        float c=fieldNoise(vSurfaceWorld.xz*95.0+vec2(0,.18));
        normal=normalize(normal+mat3(viewMatrix)*vec3((b-a)*.22,0.0,(c-a)*.22));
      `);
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
  water.envMapIntensity = .38;
  skyGeometry.dispose(); skyMaterial.dispose(); pmrem.dispose();

  return {
    grass, bank, water,
    update(time: number, night: number) {
      uniforms.uLandscapeTime.value = time;
      uniforms.uLandscapeNight.value = night;
      water.envMapIntensity = THREE.MathUtils.lerp(.38,.18,night);
    },
  };
}


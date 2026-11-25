import * as THREE from 'three';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';

/** Small, depth-clipped fire volume; the hearth and logs remain authored meshes. */
export function createCampfire(camera: THREE.Camera, depth: THREE.DepthTexture) {
  const pass = new ShaderPass({
    uniforms: {
      tDiffuse: { value: null }, uDepth: { value: depth }, uTime: { value: 0 },
      uInverseProjection: { value: camera.projectionMatrixInverse },
      uCameraWorld: { value: camera.matrixWorld },
      uRoot: { value: new THREE.Vector3(1, .48, .74) },
    },
    vertexShader: /* glsl */`
      varying vec2 vUv;
      void main() { vUv=uv; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0); }
    `,
    fragmentShader: /* glsl */`
      uniform sampler2D tDiffuse, uDepth;
      uniform mat4 uInverseProjection, uCameraWorld;
      uniform float uTime;
      uniform vec3 uRoot;
      varying vec2 vUv;
      vec3 unproject(float depth) {
        vec4 p=uInverseProjection*vec4(vUv*2.0-1.0,depth*2.0-1.0,1.0);
        return (uCameraWorld*vec4(p.xyz/p.w,1.0)).xyz-uRoot;
      }
      float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
      float noise(vec2 p) {
        vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
        return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),
                   mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);
      }
      float tongue(vec3 p,vec2 base,float height,float radius,float phase) {
        float h=height*(1.0+.085*sin(uTime*3.8+phase));
        float v=p.y/h;
        if(v<0.0 || v>1.0) return 0.0;
        // Roots stay seated in the fuel; only the upper flame curls and stretches.
        vec2 curl=vec2(sin(uTime*2.6-v*5.0+phase),cos(uTime*2.1-v*4.0+phase));
        vec2 center=base+curl*.13*v*v;
        float width=radius*pow(1.0-v,.80)*(.74+.64*sin(v*3.14159));
        float d=length((p.xz-center)/vec2(1.0,.88));
        return (1.0-smoothstep(width*.24,width,d))*smoothstep(0.0,.07,p.y);
      }
      void main() {
        vec4 original=texture2D(tDiffuse,vUv);
        vec3 origin=unproject(0.0), surface=unproject(texture2D(uDepth,vUv).r);
        vec3 direction=normalize(unproject(1.0)-origin);
        vec3 inverse=sign(direction+vec3(.00001))/max(abs(direction),vec3(.00001));
        vec3 a=(vec3(-.55,0,-.55)-origin)*inverse;
        vec3 b=(vec3(.55,1.3,.55)-origin)*inverse;
        vec3 low=min(a,b), high=max(a,b);
        float start=max(0.0,max(low.x,max(low.y,low.z)));
        // Stop at the actual opaque scene surface, including the foreground logs.
        float end=min(length(surface-origin),min(high.x,min(high.y,high.z)));
        if(end<=start) { gl_FragColor=original; return; }
        float stepSize=(end-start)/48.0;
        vec3 light=vec3(0.0);
        float transmission=1.0;
        for(int i=0;i<48;i++) {
          vec3 p=origin+direction*(start+(float(i)+.5)*stepSize);
          float n=noise(vec2(p.x*9.0+p.z*7.0,p.y*8.0-uTime*2.6));
          vec3 q=p;
          q.xz+=(vec2(n,noise(vec2(p.z*8.0-p.x*6.0,p.y*7.0-uTime*2.1)))-.5)*.12*p.y;
          float density=tongue(q,vec2(0,0),.90,.22,0.0);
          density=max(density,tongue(q,vec2(-.17,.04),.63,.14,1.9));
          density=max(density,tongue(q,vec2(.16,-.05),.72,.15,4.1));
          density=max(density,tongue(q,vec2(.02,.16),.53,.14,2.7));
          density=max(density,tongue(q,vec2(-.03,-.16),.48,.13,5.2));
          density*=.45+.75*n;
          float alpha=1.0-exp(-density*stepSize*14.0);
          float heat=clamp(pow(density,1.35)*.9+(1.0-p.y)*.17,0.0,1.0);
          float core=(1.0-smoothstep(.03,.16,length(p.xz)))*(1.0-smoothstep(.14,.52,p.y));
          heat=max(heat,core);
          vec3 color=mix(vec3(1.8,.10,.003),vec3(2.6,.82,.018),smoothstep(.30,.90,heat));
          color=mix(color,vec3(2.9,1.4,.13),smoothstep(.82,1.05,heat)*.55);
          light+=transmission*alpha*color;
          transmission*=1.0-alpha;
        }
        gl_FragColor=vec4(original.rgb*transmission+light,original.a);
      }
    `,
  });
  // ShaderPass clones constructor uniforms; keep the live depth target and camera matrices.
  pass.uniforms.uDepth.value = depth;
  pass.uniforms.uInverseProjection.value = camera.projectionMatrixInverse;
  pass.uniforms.uCameraWorld.value = camera.matrixWorld;
  pass.material.name = 'Campfire / flowing volume';
  pass.material.depthTest = false;
  pass.material.depthWrite = false;

  const count = 11;
  const positions = new Float32Array(count * 3);
  const brightness = new Float32Array(count);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('brightness', new THREE.BufferAttribute(brightness, 1));
  const sparks = new THREE.Points(geometry, new THREE.ShaderMaterial({
    name: 'Campfire / soft embers', transparent: true, depthWrite: false,
    uniforms: { uSize: { value: 3 } },
    vertexShader: /* glsl */`
      attribute float brightness;
      uniform float uSize;
      varying float vBrightness;
      void main() {
        vBrightness=brightness;
        gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);
        gl_PointSize=uSize;
      }
    `,
    fragmentShader: /* glsl */`
      varying float vBrightness;
      void main() {
        float d=length(gl_PointCoord-.5)*2.0;
        float alpha=(1.0-smoothstep(.12,1.0,d))*vBrightness;
        gl_FragColor=vec4(1.8,.62,.06,alpha);
      }
    `,
  }));
  sparks.name = 'Campfire / rising embers';
  sparks.frustumCulled = false;
  return {
    pass, sparks,
    update(time: number) {
      pass.uniforms.uTime.value = time;
      for (let i = 0; i < count; i++) {
        const phase = (time * (.17 + (i % 3) * .025) + i * .618) % 1;
        const angle = i * 2.4;
        positions[i * 3] = 1 + Math.cos(angle) * .13 + Math.sin(time * .7 + i) * .10 * phase;
        positions[i * 3 + 1] = .7 + phase * 1.20;
        positions[i * 3 + 2] = .74 + Math.sin(angle) * .14 + .12 * phase;
        brightness[i] = Math.sin(phase * Math.PI) * .7;
      }
      geometry.attributes.position.needsUpdate = true;
      geometry.attributes.brightness.needsUpdate = true;
    },
    dispose() { pass.dispose(); geometry.dispose(); sparks.material.dispose(); },
  };
}

import * as THREE from 'three';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { scenePalette } from './palette';

/** World-space aerial perspective and low drifting mist across a continuous forest. */
export function createMistwoodMist(camera: THREE.Camera, depth: THREE.DepthTexture) {
  const pass = new ShaderPass({
    uniforms: {
      tDiffuse: { value: null }, uDepth: { value: null },
      uInverseProjection: { value: new THREE.Matrix4() },
      uCameraWorld: { value: new THREE.Matrix4() },
      uTime: { value: 0 }, uNight: { value: 0 },
      uDayColor: { value: new THREE.Color(scenePalette.mist.day) },
      uNightColor: { value: new THREE.Color(scenePalette.mist.night) },
    },
    vertexShader: /* glsl */`
      varying vec2 vUv;
      void main() { vUv=uv; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0); }
    `,
    fragmentShader: /* glsl */`
      uniform sampler2D tDiffuse, uDepth;
      uniform mat4 uInverseProjection, uCameraWorld;
      uniform float uTime, uNight;
      uniform vec3 uDayColor, uNightColor;
      varying vec2 vUv;

      float mistHash(vec2 p) {
        vec3 q=fract(vec3(p.xyx)*.1031);
        q+=dot(q,q.yzx+33.33);
        return fract((q.x+q.y)*q.z);
      }
      float mistNoise(vec2 p) {
        vec2 cell=floor(p), f=fract(p);
        f=f*f*(3.0-2.0*f);
        return mix(mix(mistHash(cell),mistHash(cell+vec2(1,0)),f.x),
                   mix(mistHash(cell+vec2(0,1)),mistHash(cell+vec2(1,1)),f.x),f.y);
      }
      vec3 unproject(float depth) {
        vec4 v=uInverseProjection*vec4(vUv*2.0-1.0,depth*2.0-1.0,1.0);
        return (uCameraWorld*vec4(v.xyz/v.w,1.0)).xyz;
      }
      float density(vec3 p) {
        float r=length(p.xz/vec2(6.85,5.35));
        float eddy=mistNoise(p.xz*.62+vec2(uTime*.018,-uTime*.011)+p.y*.24);
        float wisps=mistNoise(p.xz*1.45+vec2(-uTime*.024,uTime*.009)-p.y*.35);
        float distanceVeil=smoothstep(.80,2.8,r+(eddy-.5)*.22);
        float aerial=exp(-max(p.y-.25,0.0)*.20)*distanceVeil*.13;
        float low=exp(-max(p.y-.15,0.0)*2.0);
        float streak=pow(mistNoise(p.xz*vec2(.19,.75)+vec2(uTime*.012,-uTime*.019)),2.0);
        float groundMist=low*(.018+distanceVeil*.23)*streak;
        return (aerial+groundMist)*smoothstep(-1.2,-.5,p.y);
      }
      void main() {
        vec4 original=texture2D(tDiffuse,vUv);
        vec3 origin=unproject(0.0), surface=unproject(texture2D(uDepth,vUv).r);
        vec3 direction=normalize(unproject(1.0)-origin);
        vec3 inverse=sign(direction+vec3(.00001))/max(abs(direction),vec3(.00001));
        vec3 t0=(vec3(-70,-1.3,-70)-origin)*inverse;
        vec3 t1=(vec3(70,16.0,70)-origin)*inverse;
        vec3 low=min(t0,t1), high=max(t0,t1);
        float start=max(0.0,max(low.x,max(low.y,low.z)));
        float end=min(length(surface-origin),min(high.x,min(high.y,high.z)));
        if (end<=start) { gl_FragColor=original; return; }
        float stepLength=(end-start)/24.0;
        float transmittance=1.0;
        vec3 scatter=vec3(0.0);
        vec3 fogColor=mix(uDayColor,uNightColor,uNight);
        for (int i=0;i<24;i++) {
          vec3 p=origin+direction*(start+(float(i)+.5)*stepLength);
          float cameraDistance=length(p-uCameraWorld[3].xyz);
          float depthWeight=smoothstep(10.0,34.0,cameraDistance);
          float extinction=1.0-exp(-density(p)*depthWeight*stepLength);
          float shaft=pow(mistNoise((p.xz+p.y*vec2(.40,.48))*.28),5.0);
          float light=.76+shaft*2.0*(1.0-uNight)*smoothstep(0.0,3.0,p.y);
          scatter+=transmittance*extinction*fogColor*light;
          transmittance*=1.0-extinction;
        }
        float surfaceRadius=length(surface.xz/vec2(6.85,5.35));
        float centerVeil=mix(.13,.20,uNight);
        float veilStart=mix(.85,.70,uNight);
        float aerialWeight=mix(centerVeil,1.0,smoothstep(veilStart,1.85,surfaceRadius));
        gl_FragColor=vec4(mix(original.rgb,original.rgb*transmittance+scatter,aerialWeight),original.a);
      }
    `,
  });
  pass.uniforms.uDepth.value=depth;
  pass.uniforms.uInverseProjection.value=camera.projectionMatrixInverse;
  pass.uniforms.uCameraWorld.value=camera.matrixWorld;
  pass.material.depthTest=false;
  pass.material.depthWrite=false;
  return {
    pass,
    update(time: number,night: number) {
      pass.uniforms.uTime.value=time;
      pass.uniforms.uNight.value=night;
    },
  };
}

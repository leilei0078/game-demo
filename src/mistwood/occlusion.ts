import * as THREE from 'three';
import { SSAOPass } from 'three/addons/postprocessing/SSAOPass.js';

/** The normal/depth buffer must honor foliage alpha, just like the beauty pass. */
export class ForestOcclusionPass extends SSAOPass {
  private cutouts=new Map<THREE.Material,THREE.MeshNormalMaterial>();
  private doubleNormal=new THREE.MeshNormalMaterial({side:THREE.DoubleSide});
  _renderOverride(renderer:THREE.WebGLRenderer,override:THREE.Material,target:THREE.WebGLRenderTarget,clearColor:THREE.ColorRepresentation,clearAlpha:number) {
    const originalClear=renderer.getClearColor(new THREE.Color()),alpha=renderer.getClearAlpha();
    const originalAutoClear=renderer.autoClear,shadowAuto=renderer.shadowMap.autoUpdate;
    const replaced:{mesh:THREE.Mesh;material:THREE.Material|THREE.Material[]}[]=[];
    this.scene.traverse(object=>{
      if(!(object instanceof THREE.Mesh)) return;
      const source=object.material as THREE.MeshStandardMaterial;
      replaced.push({mesh:object,material:object.material});
      if(source.map && source.alphaTest>0) {
        let normal=this.cutouts.get(source);
        if(!normal) {
          normal=new THREE.MeshNormalMaterial({side:source.side});
          const texture=source.map,threshold=source.alphaTest;
          normal.onBeforeCompile=shader=>{
            shader.uniforms.uNeedles={value:texture};
            shader.vertexShader='varying vec2 vCutout;\n'+shader.vertexShader;
            shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nvCutout=uv;');
            shader.fragmentShader='varying vec2 vCutout; uniform sampler2D uNeedles;\n'+shader.fragmentShader;
            shader.fragmentShader=shader.fragmentShader.replace('#include <clipping_planes_fragment>',`if(texture2D(uNeedles,vCutout).a<${threshold.toFixed(3)}) discard;\n#include <clipping_planes_fragment>`);
          };
          normal.customProgramCacheKey=()=>`needle-depth-${texture.uuid}`;
          this.cutouts.set(source,normal);
        }
        object.material=normal;
      } else object.material=source.side===THREE.DoubleSide?this.doubleNormal:override;
    });
    try {
      renderer.autoClear=false;renderer.shadowMap.autoUpdate=false;
      renderer.setRenderTarget(target);renderer.setClearColor(clearColor,clearAlpha);renderer.clear();
      renderer.render(this.scene,this.camera);
    } finally {
      replaced.forEach(({mesh,material})=>{mesh.material=material;});
      renderer.autoClear=originalAutoClear;renderer.shadowMap.autoUpdate=shadowAuto;
      renderer.setClearColor(originalClear,alpha);
    }
  }
  dispose() {
    this.cutouts.forEach(material=>material.dispose());this.doubleNormal.dispose();
    // The published type currently misspells this method; the runtime implements dispose.
    (SSAOPass.prototype as unknown as {dispose:()=>void}).dispose.call(this);
  }
}

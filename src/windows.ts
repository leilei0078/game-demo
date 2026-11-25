import * as THREE from 'three';

/** Small variations in old glass; pane coordinates keep the edge dirt in its frame. */
export function finishWindowGlass(material: THREE.MeshStandardMaterial) {
  material.customProgramCacheKey = () => 'old-cottage-glass-v1';
  material.onBeforeCompile = shader => {
    shader.vertexShader = 'varying vec2 vPaneUV;\n' + shader.vertexShader;
    shader.vertexShader = shader.vertexShader.replace('#include <begin_vertex>', `
      #include <begin_vertex>
      vPaneUV = uv;
    `);
    shader.fragmentShader = /* glsl */`
      varying vec2 vPaneUV;
      float glassHash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
      float glassNoise(vec2 p) {
        vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
        return mix(mix(glassHash(i),glassHash(i+vec2(1,0)),f.x),
          mix(glassHash(i+vec2(0,1)),glassHash(i+vec2(1,1)),f.x),f.y);
      }
    ` + shader.fragmentShader;
    shader.fragmentShader = shader.fragmentShader.replace('#include <color_fragment>', /* glsl */`
      #include <color_fragment>
      vec2 paneUV=clamp(vPaneUV,0.0,1.0);
      float edgeDistance=min(min(paneUV.x,1.0-paneUV.x),min(paneUV.y,1.0-paneUV.y));
      float edgeDirt=1.0-smoothstep(.015,.12,edgeDistance);
      float glassMottle=glassNoise(paneUV*vec2(7.0,10.0));
      diffuseColor.rgb *= (1.0-edgeDirt*.24)*(.96+glassMottle*.08);
    `);
    shader.fragmentShader = shader.fragmentShader.replace('#include <roughnessmap_fragment>', /* glsl */`
      #include <roughnessmap_fragment>
      roughnessFactor=clamp(roughnessFactor+edgeDirt*.12+(glassMottle-.5)*.07,.15,.65);
    `);
    shader.fragmentShader = shader.fragmentShader.replace('#include <emissivemap_fragment>', /* glsl */`
      #include <emissivemap_fragment>
      float lightFalloff=1.0-smoothstep(.08,.62,length((paneUV-vec2(.48,.30))*vec2(1.1,.7)));
      totalEmissiveRadiance *= mix(.56,1.04,lightFalloff)*(1.0-edgeDirt*.23);
    `);
  };
}

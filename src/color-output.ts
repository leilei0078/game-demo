import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { scenePalette } from './palette';

/** Grade display colors after AgX, inside the existing output draw. */
export function createColorOutput() {
  const pass = new OutputPass();
  const daylight = { value: 1 };
  pass.material.name = 'Output / daylight color';
  Object.assign(pass.uniforms, {
    uDaylight: daylight,
    uDaySaturation: { value: scenePalette.daylight.saturation },
    uDayContrast: { value: scenePalette.daylight.contrast },
  });
  pass.material.fragmentShader = pass.material.fragmentShader
    .replace('uniform sampler2D tDiffuse;', `
      uniform sampler2D tDiffuse;
      uniform float uDaylight, uDaySaturation, uDayContrast;
    `)
    .replace('gl_FragColor = sRGBTransferOETF( gl_FragColor );', /* glsl */`
      gl_FragColor = sRGBTransferOETF( gl_FragColor );
      vec3 source=gl_FragColor.rgb;
      float luma=dot(source,vec3(.2126,.7152,.0722));
      // Keep bright fire and reflections soft while restoring muted midtone colors.
      float saturation=mix(uDaySaturation,1.10,smoothstep(.62,.94,luma));
      vec3 graded=mix(vec3(luma),source,saturation);
      graded=(graded-.5)*uDayContrast+.5;
      gl_FragColor.rgb=mix(source,clamp(graded,0.0,1.0),uDaylight);
    `);
  return {
    pass,
    update(night: number) { daylight.value = 1 - night; },
  };
}

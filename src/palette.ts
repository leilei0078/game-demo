import * as THREE from 'three';

// Web look development: linear material colors, with sRGB hex values for lights and fog.
export const scenePalette = {
  exposure: 1.02,
  background: { day: '#243e48', night: '#101b30' },
  mist: { day: '#6d96a1', night: '#304a6e' },
  sunlight: { day: '#f2f5ff', night: '#a9c6ff', dayIntensity: 3.35, nightIntensity: .66 },
  sky: { color: '#bdd7f4', ground: '#3c535b', dayIntensity: 1.2, nightIntensity: .42 },
  fill: { color: '#8fb7d8', dayIntensity: .58, nightIntensity: .52 },
  daylight: { saturation: 1.38, contrast: 1.08 },
  forest: ['#24564c', '#316c58', '#4a8060'],
};

const colors: Record<string, [number, number, number]> = {
  pine: [.060, .225, .155], pine_light: [.12, .32, .20], pine_dark: [.025, .13, .105],
  bush: [.16, .29, .09],
  grass: [.12, .28, .10], grass_light: [.19, .35, .115], grass_dark: [.065, .20, .075],
  timber: [.115, .070, .045], wood: [.285, .17, .095],
  wood_light: [.43, .28, .145], wood_end: [.53, .36, .19], door: [.19, .11, .062],
  plaster: [.68, .69, .59], plaster_light: [.79, .80, .69],
  roof: [.40, .115, .067], roof_light: [.54, .18, .088], roof_dark: [.29, .065, .047],
  stone: [.30, .37, .405], stone_light: [.43, .50, .52], stone_dark: [.19, .255, .29],
  cloth: [.095, .23, .33], steel: [.37, .49, .56], iron: [.07, .11, .15],
};
const finished = new WeakSet<THREE.Material>();

export function applyScenePalette(material: THREE.MeshStandardMaterial) {
  if (finished.has(material)) return;
  finished.add(material);
  const color = colors[material.name];
  if (color) material.color.setRGB(...color);
  if (material.name.startsWith('M_Limestone_') ||
      ['M_Door_threshold_limestone', 'M_Window_Sill_limestone', 'M_Recessed_lime_mortar'].includes(material.name)) {
    // Preserve the authored variations between individual masonry courses.
    material.color.multiply(new THREE.Color().setRGB(.78, .88, 1.06));
  }
}

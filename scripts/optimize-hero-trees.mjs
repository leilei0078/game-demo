import { NodeIO, Document } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { dedup, prune, weld, mergeDocuments, unpartition } from '@gltf-transform/functions';
import { readFile, writeFile, stat } from 'node:fs/promises';

const base=new URL('../',import.meta.url);
const io=new NodeIO().registerExtensions(ALL_EXTENSIONS);
const manifest=JSON.parse(await readFile(new URL('public/models/hero-trees/manifest.json',base),'utf8'));
const bundle=new Document();const bundleScene=bundle.createScene('Tree Studies / all LODs');
for(const style of manifest.styles) for(const lod of style.lods) {
  const doc=await io.read(new URL(`output/hero-trees/${style.id}_LOD${lod.level}.glb`,base).pathname);
  for(const m of doc.getRoot().listMaterials()) {
    if(m.getName().includes('Cutout'))m.setAlphaMode('MASK').setAlphaCutoff(.48).setDoubleSided(true);
  }
  await doc.transform(dedup(),weld(),prune());
  const path=new URL(`public${lod.url}`,base);
  await io.write(path.pathname,doc);
  lod.bytes=(await stat(path)).size;
  const readback=await io.read(path.pathname);
  const meshes=readback.getRoot().listMeshes();
  lod.triangles=meshes.reduce((sum,m)=>sum+m.listPrimitives().reduce((n,p)=>n+(p.getIndices()?.getCount()??p.getAttribute('POSITION').getCount())/3,0),0);
  lod.vertices=meshes.reduce((sum,m)=>sum+m.listPrimitives().reduce((n,p)=>n+p.getAttribute('POSITION').getCount(),0),0);
  lod.primitives=meshes.reduce((n,m)=>n+m.listPrimitives().length,0);
  const map=mergeDocuments(bundle,readback);
  for(const sourceScene of readback.getRoot().listScenes()){
    const merged=map.get(sourceScene);
    for(const node of merged.listChildren())bundleScene.addChild(node);
    merged.dispose();
  }
}
await bundle.transform(dedup(),prune(),unpartition());
await io.write(new URL('public/models/hero-trees/HeroTrees.glb',base).pathname,bundle);
manifest.bundle={url:'/models/hero-trees/HeroTrees.glb',bytes:(await stat(new URL('public/models/hero-trees/HeroTrees.glb',base))).size};
await writeFile(new URL('public/models/hero-trees/manifest.json',base),JSON.stringify(manifest,null,2));
await writeFile(new URL('UnityTreeStudies/Assets/HeroTrees/manifest.json',base),JSON.stringify(manifest,null,2));
console.log({bundleKB:Math.round(manifest.bundle.bytes/1024),styles:manifest.styles.map(s=>({style:s.id,tris:s.lods.map(l=>l.triangles),kb:s.lods.map(l=>Math.round(l.bytes/1024))}))});

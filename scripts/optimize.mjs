import { NodeIO } from '@gltf-transform/core';
import { dedup, prune, weld } from '@gltf-transform/functions';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { stat, mkdir } from 'node:fs/promises';

const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const input = new URL('../output/web_source.glb', import.meta.url);
const output = new URL('../public/models/wayfarers-rest.glb', import.meta.url);
await mkdir(new URL('../public/models/', import.meta.url), { recursive: true });
const doc = await io.read(input.pathname);
await doc.transform(dedup(), weld(), prune());
await io.write(output.pathname, doc);
console.log(`GLB: ${(await stat(input)).size} → ${(await stat(output)).size} bytes`);

"""Reimport exported GLB/FBX in isolated scenes, verify bounds and actual textures."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/wood-fence'
FILES = ['WoodFence_1m', 'WoodFence_PanelA', 'WoodFence_PanelB', 'WoodFence_Post', 'WoodFence_CornerDemo']
expected = json.loads((OUT / 'metrics.json').read_text())
expected_tris = {
    'WoodFence_1m': expected['complete_single_tris'],
    'WoodFence_PanelA': expected['SM_WoodFence_PanelA']['tris'],
    'WoodFence_PanelB': expected['SM_WoodFence_PanelB']['tris'],
    'WoodFence_Post': expected['SM_WoodFence_Post']['tris'],
    'WoodFence_CornerDemo': 5 * expected['SM_WoodFence_PanelA']['tris'] + 6 * expected['SM_WoodFence_Post']['tris'],
}
reports = []
for stem in FILES:
    pair = []
    for ext in ['glb', 'fbx']:
        scene = bpy.data.scenes.new('Verify_' + stem + '_' + ext)
        bpy.context.window.scene = scene
        scene.unit_settings.system = 'METRIC'
        scene.unit_settings.scale_length = 1
        path = OUT / 'exports' / (stem + '.' + ext)
        if ext == 'glb':
            bpy.ops.import_scene.gltf(filepath=str(path))
        else:
            bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
        bpy.context.view_layer.update()
        obs = [o for o in scene.objects if o.type == 'MESH']
        points = [o.matrix_world @ v.co for o in obs for v in o.data.vertices]
        low = [min(v[i] for v in points) for i in range(3)]
        high = [max(v[i] for v in points) for i in range(3)]
        tris = sum(sum(len(f.vertices) - 2 for f in o.data.polygons) for o in obs)
        mats = {slot.material for o in obs for slot in o.material_slots if slot.material}
        textures = []
        for mat in mats:
            for node in mat.node_tree.nodes:
                if node.type == 'TEX_IMAGE' and node.image:
                    im = node.image
                    textures.append({'name': im.name, 'size': list(im.size),
                                     'colorspace': im.colorspace_settings.name,
                                     'available': bool(im.packed_file) or Path(bpy.path.abspath(im.filepath)).exists()})
        report = {'file': path.name, 'bytes': path.stat().st_size,
                  'objects': len(obs), 'tris': tris, 'bounds_min': low, 'bounds_max': high,
                  'dimensions_m': [high[i] - low[i] for i in range(3)],
                  'materials': len(mats), 'textures': textures}
        assert tris == expected_tris[stem], report
        assert len(mats) == 1, report
        assert path.stat().st_size > 1000
        assert all(o.data.uv_layers for o in obs)
        assert textures and all(t['available'] and t['size'] == [1024, 1024] for t in textures), report
        if ext == 'glb':
            assert len(textures) == 3, report
        if stem == 'WoodFence_1m':
            assert 1.28 < report['dimensions_m'][2] < 1.32, report
            assert 1.17 < report['dimensions_m'][0] < 1.22, report
            assert abs(low[2]) < .001, report
        pair.append(report)
    assert all(abs(a - b) < 1e-4 for a, b in zip(pair[0]['dimensions_m'], pair[1]['dimensions_m'])), pair
    reports.extend(pair)

result = {'pass': True, 'exports': reports,
          'scope': 'Blender export/reimport verification; Unity runtime integration not performed.'}
(OUT / 'qa/export-roundtrip.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
print('ROUNDTRIP_PASS', len(reports), 'files', flush=True)

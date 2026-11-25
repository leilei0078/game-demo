"""Export the saved diorama without changing its .blend source."""
import bpy
from pathlib import Path

root = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'output/wayfarers_rest.blend'))
bpy.ops.object.select_all(action='DESELECT')
count = 0
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH' or obj.name == 'Matte studio ground':
        continue
    collection = obj.users_collection[0].name
    obj['region'] = ('cottage' if '02' in collection else
                     'woodland' if '03' in collection else
                     'supplies' if '04' in collection else
                     'camp' if '05' in collection else 'terrain')
    if 'bridge' in obj.name.lower(): obj['region']='bridge'
    if 'flame' in obj.name.lower(): obj['animated']='flame'
    if 'ember' in obj.name.lower(): obj['animated']='ember'
    if obj.name.startswith('Small glint'): obj['animated']='water'
    obj.select_set(True)
    count += 1

out = root/'output'
out.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out/'web_source.glb'), export_format='GLB',
    use_selection=True, export_apply=True, export_extras=True,
    export_cameras=False, export_lights=False, export_animations=False,
    export_yup=True)
print('WEB_EXPORT_COMPLETE', count, 'meshes')

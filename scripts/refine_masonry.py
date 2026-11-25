"""Update just the cottage foundation, then audit and save the editable scene."""
import json
import sys
import shutil
from pathlib import Path
import bpy

root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(Path(__file__).parent))
from masonry import build_foundation
source=root/'output/wayfarers_rest.blend'
backup=root/'output/wayfarers_rest_before_masonry.blend'
if not backup.exists(): shutil.copy2(source,backup)
bpy.ops.wm.open_mainfile(filepath=str(source))
collection=next(c for c in bpy.data.collections if c.name.startswith('02'))
old=[o.name for o in collection.objects if o.name.startswith((
    'Raised fieldstone foundation','Hand cut foundation block','Side foundation block',
    'SM_Foundation_','SM_Door_threshold',
))]
for name in old:
    bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
objects=build_foundation(collection)
for obj in collection.objects:
    if obj.name.startswith('Worn doorstep'):
        for mod in obj.modifiers:
            if mod.type=='BEVEL': mod.width=.012
        objects.append(obj)

qa_dir=Path('/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
sys.path.insert(0,str(qa_dir))
import qa_check
names=[o.name for o in objects]
report=qa_check.run(objects=names,profile='game',uv=False)
report['scope']='Rebuilt masonry and the three refined stair slabs only.'
report['uv_note']='UV checks omitted: these parts use solid-color PBR materials and no baked or image textures.'
report['accepted_warnings']={w:'The scene ground is z=0.27 m. The bottom stair extends to z=0.25 m intentionally so it is seated in the ground, not floating.' for w in report['warnings'] if 'lowest point' in w}
out=root/'output/masonry-qa'
out.mkdir(exist_ok=True)
(out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('MASONRY_QA',report['pass'],'ERRORS',report['errors'],'WARNINGS',report['warnings'])
if not report['pass']: raise RuntimeError('Masonry QA failed; source scene has not been overwritten')
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(source))
print('MASONRY_STATS',len(objects),'objects',sum(m['tris'] for m in report['objects'].values()),
      'tris',len({slot.material.name for o in objects for slot in o.material_slots}),'materials')
print('MASONRY_DIMENSIONS',[round(v,3) for v in bpy.data.objects['SM_Foundation_mortar_core'].dimensions])

import review_shots
for mode in ('material','wire'):
    result=review_shots.run(objects=names,out=str(out/f'{mode}.png'),
      views=['front','right','back','three_quarter'],mode=mode,res=400)
    print('MASONRY_REVIEW',result['sheet'])

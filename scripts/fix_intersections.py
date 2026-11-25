"""Re-space existing assets without rebuilding the scene or changing its masonry."""
import json
import shutil
import sys
from pathlib import Path
import bpy

root = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(Path(__file__).parent))
from clearance import refine_layout, check_clearance
source = root/'output/wayfarers_rest.blend'
backup = root/'output/wayfarers_rest_before_clearance.blend'
if not backup.exists():
    shutil.copy2(source,backup)
bpy.ops.wm.open_mainfile(filepath=str(source))
assets = refine_layout()
out = root/'output/clearance-qa'
out.mkdir(exist_ok=True)
report = check_clearance(assets)
(out/'clearance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('CLEARANCE_CHECK',report['pass'],json.dumps([c for c in report['checks'] if not c['pass']]))

sys.path.insert(0,'/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check
reports = {name:qa_check.run(objects=[o.name for o in objects],profile='game',uv=False)
           for name,objects in assets.items()}
for result in reports.values():
    result['uv_note'] = 'Solid-color procedural materials; no image textures or baked UVs.'
    result['accepted_warnings'] = {}
    for warning in result['warnings']:
        if 'lowest point' in warning:
            reason = 'The scene ground is z=0.27 m; supplies and trunks rest on it, and rocks/shrubs are deliberately rooted into it.'
        elif 'back-to-back' in warning:
            reason = 'Hidden contact surfaces between the two supported crates; retained so each crate remains an independently closed mesh.'
        else:
            raise RuntimeError('Unreviewed QA warning: ' + warning)
        result['accepted_warnings'][warning] = reason
(out/'geometry.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2))
for name,result in reports.items():
    print('ASSET_QA',name,result['pass'],'ERRORS',result['errors'],'WARNINGS',result['warnings'])
if not report['pass'] or not all(r['pass'] for r in reports.values()):
    raise RuntimeError('Clearance or asset QA failed; saved source has not been overwritten.')
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(source))
import review_shots
names = [o.name for key in ['barrel_0','barrel_1','crate_stack','firewood'] for o in assets[key]]
for mode in ('material','wire'):
    result=review_shots.run(objects=names,out=str(out/f'{mode}.png'),
      views=['front','right','back','three_quarter'],mode=mode,res=500)
    print('CLEARANCE_REVIEW',result['sheet'])
context = [o.name for objects in assets.values() for o in objects]
context += [o.name for o in bpy.context.scene.objects if o.type == 'MESH' and
            (any(c.name.startswith('02') for c in o.users_collection) or o.name.startswith(
             ('Split oak fence','Pointed fence cap','Fence crossrail','Fence diagonal')))]
for mode in ('material','wire'):
    result = review_shots.run(objects=context,out=str(out/f'context-{mode}.png'),
        views=['front','right','back','three_quarter'],mode=mode,res=600)
    print('CONTEXT_REVIEW',result['sheet'])

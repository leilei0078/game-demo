"""Back up the scene, rebuild the windows, audit the result, and save."""
import json
import shutil
import sys
from pathlib import Path
import bpy
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(Path(__file__).parent))
from windows import refine_windows
from clearance import bounds,bvh
source=root/'output/wayfarers_rest.blend'
backup=root/'output/wayfarers_rest_before_windows.blend'
if not backup.exists():
    shutil.copy2(source,backup)
bpy.ops.wm.open_mainfile(filepath=str(source))
assemblies,wall=refine_windows()
out=root/'output/windows-qa'
out.mkdir(exist_ok=True)
sys.path.insert(0,'/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check
reports={key:qa_check.run(objects=[o.name for o in objects],profile='game',uv=False) for key,objects in assemblies.items()}
reports['modified_wall']=qa_check.run(objects=[wall.name],profile='game',uv=False)
timbers=[o for o in bpy.data.objects if o.name.startswith(('Side upright','Side diagonal timber'))]
for obj in timbers:
    reports[obj.name]=qa_check.run(objects=[obj.name],profile='game',uv=False)
for key,report in reports.items():
    report['uv_note']='Solid PBR materials, no image textures or baked UVs. Glass has planar pane coordinates for procedural edge dirt; thin edge faces do not need texture coverage.'
    report['accepted_warnings']={}
    for warning in report['warnings']:
        if 'lowest point' in warning:
            reason='Windows are wall-mounted; the main wall is seated above the foundation.'
        elif 'back-to-back' in warning:
            reason='Hidden, supported join between the timber frame and its fitted components.'
        elif 'n-gons' in warning and key=='modified_wall':
            reason='Planar static wall faces after Exact boolean pockets; triangulated on export.'
        else:
            raise RuntimeError('Unreviewed warning: '+warning)
        report['accepted_warnings'][warning]=reason
    print('WINDOW_QA',key,report['pass'],report['errors'],report['warnings'])
(out/'geometry.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2))
checks=[]
wall_tree=bvh([wall])
for key,objects in assemblies.items():
    inside=Vector((-1,0,0)) if key=='side' else Vector((0,1,0))
    for pane in [o for o in objects if o.get('window_role')=='glass']:
        lo,hi=bounds([pane])
        center=Vector([(a+b)/2 for a,b in zip(lo,hi)])
        hit=wall_tree.ray_cast(center-inside*.5,inside,1)
        clear=hit[0] is not None and hit[3] > .60
        checks.append({'pane':pane.name,'wall_recess_behind_glass':clear,'wall_distance_behind_pane_m':round(hit[3]-.5,4) if hit[0] else None,'pass':clear})
    jamb=next(o for o in objects if o.get('window_role')=='jamb')
    attached=bool(bvh([jamb]).overlap(wall_tree))
    checks.append({'window':key,'frame_connected_to_wall':attached,'pass':attached})
glass_tree=bvh([o for o in assemblies['side'] if o.get('window_role')=='glass'])
crossings=len(bvh(timbers).overlap(glass_tree))
checks.append({'side_timber_intersections_with_glass':crossings,'pass':crossings==0})
audit={'pass':all(c['pass'] for c in checks),'checks':checks}
(out/'recess-and-support.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2))
print('WINDOW_RECESS_AND_SUPPORT',audit['pass'])
if not all(r['pass'] for r in reports.values()) or not audit['pass']:
    raise RuntimeError('Window QA failed; saved source has not been overwritten.')
metrics={key:{'objects':len(objects),'triangles':sum(m['tris'] for m in reports[key]['objects'].values()),
    'materials':len({s.material.name for o in objects for s in o.material_slots}),
    'dimensions_m':[round(b-a,4) for a,b in zip(*bounds(objects))]} for key,objects in assemblies.items()}
(out/'metrics.json').write_text(json.dumps(metrics,ensure_ascii=False,indent=2))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(source))
import review_shots
for key,objects in assemblies.items():
    for mode in ('material','wire'):
        result=review_shots.run(objects=[o.name for o in objects],out=str(out/f'{key}-{mode}.png'),
            views=['front','right','three_quarter'],mode=mode,res=450)
        print('WINDOW_REVIEW',result['sheet'])
context=[wall.name]+[o.name for objects in assemblies.values() for o in objects]+[o.name for o in timbers]
for mode in ('material','wire'):
    result=review_shots.run(objects=context,out=str(out/f'wall-context-{mode}.png'),
        views=['front','right','three_quarter'],mode=mode,res=550)
    print('WINDOW_CONTEXT_REVIEW',result['sheet'])
print('WINDOW_METRICS',json.dumps(metrics))

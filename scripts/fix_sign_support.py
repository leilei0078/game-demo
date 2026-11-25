"""Repair and audit the sign's complete wall-to-panel support path."""
import json
import shutil
import sys
from pathlib import Path
import bpy
from mathutils import Vector

root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(Path(__file__).parent))
from sign_support import refine_sign
from clearance import bounds,bvh
source=root/'output/wayfarers_rest.blend'
backup=root/'output/wayfarers_rest_before_sign_support.blend'
if not backup.exists():
    shutil.copy2(source,backup)
bpy.ops.wm.open_mainfile(filepath=str(source))
objects=refine_sign()
out=root/'output/sign-support-qa'
out.mkdir(exist_ok=True)

sys.path.insert(0,'/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check
report=qa_check.run(objects=[o.name for o in objects],profile='game',uv=False,eps=.001,tol=.002)
report['scope']='Complete wall bracket, collars, hangers, panel sockets and existing sign panel/decorations.'
report['uv_note']='Solid-color PBR materials and painted geometry; no image textures.'
report['accepted_warnings']={}
for warning in report['warnings']:
    if 'lowest point' in warning:
        report['accepted_warnings'][warning]='Wall-mounted sign is intentionally elevated; wall plate and brace carry it.'
    elif 'back-to-back' in warning:
        report['accepted_warnings'][warning]='The rear of the decorative rim seats against the sign panel; these hidden faces are retained for independent editing.'
    else:
        raise RuntimeError('Unreviewed warning: '+warning)
(out/'geometry.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('SIGN_QA',report['pass'],report['errors'],report['warnings'])

# Require a connected component all the way from a real wall contact to the sign.
trees={o.name:bvh([o]) for o in objects}
def touches(first,second):
    if trees[first.name].overlap(trees[second.name]):
        return True
    for a,b in ((first,second),(second,first)):
        evaluated=a.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh=evaluated.to_mesh()
        touching=any((nearest:=trees[b.name].find_nearest(evaluated.matrix_world@v.co)) and
                     nearest[0] is not None and nearest[3]<=.002 for v in mesh.vertices)
        evaluated.to_mesh_clear()
        if touching:
            return True
    return False
edges=[]
adjacency={o.name:set() for o in objects}
for i,first in enumerate(objects):
    for second in objects[i+1:]:
        if touches(first,second):
            edges.append([first.name,second.name])
            adjacency[first.name].add(second.name)
            adjacency[second.name].add(first.name)
anchor=next(o for o in objects if o.get('support_role')=='wall_anchor')
wall=next(o for o in bpy.data.objects if o.name.startswith('Heavy corner oak post') and o.location.x> -2 and o.location.y<.1)
trees[wall.name]=bvh([wall])
anchored=touches(anchor,wall)
connected={anchor.name}
pending=[anchor.name]
while pending:
    for name in adjacency[pending.pop()]-connected:
        connected.add(name)
        pending.append(name)
missing=[o.name for o in objects if o.name not in connected]
connections={'pass':anchored and not missing,'anchored_to_wall':anchored,'wall_object':wall.name,
    'disconnected_parts':missing,'contact_edges':edges,'contact_tolerance_m':.002,
    'note':'Geometric mechanical support for the static asset; no rigid-body simulation is added.'}
(out/'connections.json').write_text(json.dumps(connections,ensure_ascii=False,indent=2))
print('SUPPORT_GRAPH',connections['pass'],'ANCHORED',anchored,'DISCONNECTED',missing)
if not report['pass'] or not connections['pass']:
    raise RuntimeError('Sign support QA failed; original scene has not been overwritten.')
report['metrics']={'objects':len(objects),'triangles':sum(v['tris'] for v in report['objects'].values()),
    'materials':len({slot.material.name for o in objects for slot in o.material_slots}),
    'dimensions_m':[round(b-a,4) for a,b in zip(*bounds(objects))]}
(out/'geometry.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(source))
import review_shots
for mode in ('material','wire'):
    result=review_shots.run(objects=[o.name for o in objects],out=str(out/f'{mode}.png'),
        views=['front','right','back','three_quarter'],mode=mode,res=500)
    print('SIGN_REVIEW',result['sheet'])
print('SIGN_METRICS',report['metrics'])

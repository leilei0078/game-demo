"""Resolve nearly coplanar foliage cards by rotating each affected card about its stem."""
from pathlib import Path
import bpy,sys,json,math
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/hero-trees';UNITY=ROOT/'UnityTreeStudies/Assets/HeroTrees'
sys.path.insert(0,'/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check,review_shots
bpy.ops.wm.open_mainfile(filepath=str(OUT/'HeroTrees.blend'))
for o in bpy.data.objects:
 if o.type=='EMPTY' and '_LOD' in o.name:o.location=(0,0,0)
bpy.context.view_layer.update()

def repair(ob):
    me=ob.data
    for attempt in range(20):
        verts=[v.co.copy() for v in me.vertices];tris=[list(p.vertices) for p in me.polygons]
        bvh=BVHTree.FromPolygons(verts,tris,epsilon=0.0)
        normals=[p.normal.copy() for p in me.polygons];sets=list(map(set,tris));cards=set()
        for ti,t in enumerate(tris):
            a,b,c=(verts[i] for i in t);n=normals[ti];cen=(a+b+c)/3
            for p in (cen,cen.lerp(a,.6),cen.lerp(b,.6),cen.lerp(c,.6)):
                for _,_,tj,_ in bvh.find_nearest_range(p,.0005):
                    if tj is None or ti==tj or sets[ti]&sets[tj]:continue
                    n2=normals[tj]
                    if n.dot(n2)<.999:continue
                    a2,b2,c2=(verts[i] for i in tris[tj]);q=p-n2*n2.dot(p-a2)
                    if qa_check._point_in_tri(q,a2,b2,c2,n2):cards.add(max(ti,tj)//4)
        if not cards:break
        print('Rotating cards',ob.name,attempt,len(cards),flush=True)
        for card in cards:
            start=card*5;base=me.vertices[start].co.copy();axis=(me.vertices[start+4].co-base).normalized()
            rot=Matrix.Rotation(.11+attempt*.023,3,axis)
            for i in range(start+1,start+5):me.vertices[i].co=base+rot@(me.vertices[i].co-base)
        me.update()
    # Preserve soft canopy normals after geometric correction.
    me.normals_split_custom_set([Vector((me.vertices[l.vertex_index].co.x*.15,me.vertices[l.vertex_index].co.y*.15,.9)).normalized() for l in me.loops])

reports=json.loads((OUT/'qa.json').read_text())
for key,qa in reports.items():
 if qa['pass']:continue
 parent=bpy.data.objects[key];obs=list(parent.children)
 repair(next(o for o in obs if 'Foliage' in o.name))
 qa=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002);reports[key]=qa
 print('REPAIRED',key,qa['pass'],qa['errors'],flush=True)
 if not qa['pass']:raise RuntimeError(key)
 bpy.ops.object.select_all(action='DESELECT')
 for o in [parent]+obs:o.select_set(True)
 bpy.ops.export_scene.gltf(filepath=str(OUT/f'{key}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
 if key.endswith('LOD0'):
  for mode in ['material','wire']:review_shots.run(objects=[o.name for o in obs],out=str(OUT/f'{key[:-5]}_{mode}.png'),views=['front','right','top','three_quarter'],mode=mode,res=400)
for name in ['LayeredFir','AncientOak']:
 bpy.ops.object.select_all(action='DESELECT')
 for lod in range(3):
  p=bpy.data.objects[f'{name}_LOD{lod}']
  for o in [p]+list(p.children):o.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
(OUT/'qa.json').write_text(json.dumps(reports,indent=2))
for si,name in enumerate(['SculptedSpruce','LayeredFir','WindsweptPine','AncientOak']):
 for lod in range(3):bpy.data.objects[f'{name}_LOD{lod}'].location=(si*9,lod*11,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HeroTrees.blend'))

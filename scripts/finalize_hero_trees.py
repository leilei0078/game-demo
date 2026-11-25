"""Ground tree root geometry, revalidate and export all final LODs consistently."""
from pathlib import Path
import bpy,sys,json
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/hero-trees';UNITY=ROOT/'UnityTreeStudies/Assets/HeroTrees';WEB=ROOT/'public/models/hero-trees'
sys.path.insert(0,'/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check
bpy.ops.wm.open_mainfile(filepath=str(OUT/'HeroTrees.blend'))
manifest=json.loads((WEB/'manifest.json').read_text());reports={}
for style in manifest['styles']:
 for lod in style['lods']:
  key=f"{style['id']}_LOD{lod['level']}";parent=bpy.data.objects[key];parent.location=(0,0,0)
  for ob in parent.children:
   if 'Bark' in ob.name:
    for v in ob.data.vertices:
     if v.co.z<0:v.co.z=0
    ob.data.update()
bpy.context.view_layer.update()
for style in manifest['styles']:
 hierarchy=[]
 for lod in style['lods']:
  key=f"{style['id']}_LOD{lod['level']}";parent=bpy.data.objects[key];obs=list(parent.children);hierarchy+=[parent]+obs
  qa=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002);reports[key]=qa
  print('FINAL_QA',key,qa['pass'],qa['errors'],flush=True)
  if not qa['pass']:raise RuntimeError(key)
  points=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
  lod['dimensions']=[round(max(p[k] for p in points)-min(p[k] for p in points),3) for k in range(3)]
  bpy.ops.object.select_all(action='DESELECT')
  for o in [parent]+obs:o.select_set(True)
  bpy.ops.export_scene.gltf(filepath=str(OUT/f'{key}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
 bpy.ops.object.select_all(action='DESELECT')
 for o in hierarchy:o.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f"{style['id']}.fbx"),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
(OUT/'qa.json').write_text(json.dumps(reports,indent=2));(WEB/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False))
for i,style in enumerate(manifest['styles']):
 for lod in range(3):bpy.data.objects[f"{style['id']}_LOD{lod}"].location=(i*9.3,lod*11,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HeroTrees.blend'))

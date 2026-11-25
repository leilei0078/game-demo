"""Update foliage albedo only; preserve geometry and UVs."""
from pathlib import Path
import bpy,numpy as np
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/leafy-trees';WEB=ROOT/'public/models/leafy-trees';UNITY=ROOT/'UnityTreeStudies/Assets/LeafyTrees'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'LeafyTrees.blend'))
im=bpy.data.images['T_LeafyPalette'];a=np.ones((256,256,4),dtype=np.float32);a[:,:,:3]=[.48,.68,.085]
im.pixels.foreach_set(a.ravel());im.update()
for path in [UNITY/'Textures/T_LeafyPalette.png',WEB/'T_LeafyPalette.png']:
 im.filepath_raw=str(path);im.file_format='PNG';im.save()
im.unpack(method='REMOVE');im.pack()
mat=bpy.data.materials['M_LeafyCanopy'];mat.diffuse_color=(.48,.68,.085,1)
principled=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
for link in list(principled.inputs['Base Color'].links):mat.node_tree.links.remove(link)
principled.inputs['Base Color'].default_value=(.48,.68,.085,1)
for node in list(mat.node_tree.nodes):
 if node.type=='TEX_IMAGE':mat.node_tree.nodes.remove(node)
names=['VerdantOak','SpreadingOak','YoungOak']
for name in names:
 for lod in range(3):bpy.data.objects[f'{name}_LOD{lod}'].location=(0,0,0)
bpy.context.view_layer.update()
for name in names:
 hierarchy=[]
 for lod in range(3):
  parent=bpy.data.objects[f'{name}_LOD{lod}'];obs=[parent]+list(parent.children);hierarchy+=obs
  bpy.ops.object.select_all(action='DESELECT')
  for o in obs:o.select_set(True)
  bpy.ops.export_scene.gltf(filepath=str(OUT/f'{name}_LOD{lod}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
 bpy.ops.object.select_all(action='DESELECT')
 for o in hierarchy:o.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
for i,name in enumerate(names):
 for lod in range(3):bpy.data.objects[f'{name}_LOD{lod}'].location=(i*9.3,lod*11,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'LeafyTrees.blend'))
print('UNIFORM_FOLIAGE_COLOR_COMPLETE')

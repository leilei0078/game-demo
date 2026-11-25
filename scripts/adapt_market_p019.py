"""Blender: open the existing ore pannier, then -- <P019 OBJ> <Unity asset dir> <output dir>."""
from pathlib import Path
import bpy, bmesh, math, sys, json
from mathutils import Matrix
source,dest,review=map(lambda v:Path(v).resolve(),sys.argv[-3:]);bpy.context.preferences.filepaths.save_version=0
straps=bpy.data.objects['Woven carrier'];bm=bmesh.new();bm.from_mesh(straps.data)
hemp=straps.data.materials.find('Faded hemp');assert hemp>=0
bmesh.ops.delete(bm,geom=[v for v in bm.verts if all(f.material_index!=hemp for f in v.link_faces)],context='VERTS');bm.to_mesh(straps.data);bm.free();straps.name='Hemp shoulder straps';straps.data.update();
bpy.ops.wm.obj_import(filepath=str(source),use_split_groups=False);body=bpy.context.selected_objects[0];body.data.transform(body.matrix_world);body.matrix_world=Matrix.Identity(4)
bm=bmesh.new();bm.from_mesh(body.data);left=set(bm.verts);islands=[]
while left:
 a=left.pop();q=[a];group=[a]
 while q:
  for edge in q.pop().link_edges:
   for v in edge.verts:
    if v in left:left.remove(v);group.append(v);q.append(v)
 islands.append(group)
assert len(islands)==2;handle=max(islands,key=lambda g:max(v.co.z for v in g));shell=next(g for g in islands if g is not handle);assert min(v.co.z for v in handle)>min(v.co.z for v in shell)+20
bmesh.ops.delete(bm,geom=handle,context='VERTS');lo=[min(v.co[i] for v in bm.verts) for i in range(3)];hi=[max(v.co[i] for v in bm.verts) for i in range(3)]
for v in bm.verts:
 x,y,z=v.co;t=(z-lo[2])/(hi[2]-lo[2]);v.co=((x-(lo[0]+hi[0])*.5)/(hi[0]-lo[0])*.477*(.77+.23*t),(y-(lo[1]+hi[1])*.5)/(hi[1]-lo[1])*.34*(.77+.23*t)+.009*t,t*.452)
bm.to_mesh(body.data);bm.free();body.data.update();body.name='P019 wicker shell';body.data.name='Basket_B original shell UV';m=bpy.data.materials.new('P019_Wicker');body.data.materials.clear();body.data.materials.append(m);bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Roughness'].default_value=.9
for typ in ('diffuse','normal'):
 img=bpy.data.images.load(str(dest/f'P019_Wicker_{typ}.jpg'));node=m.node_tree.nodes.new('ShaderNodeTexImage');node.image=img
 if typ=='normal':
  img.colorspace_settings.name='Non-Color';normal=m.node_tree.nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.65;m.node_tree.links.new(node.outputs[0],normal.inputs['Color']);m.node_tree.links.new(normal.outputs[0],bs.inputs['Normal'])
 else:m.node_tree.links.new(node.outputs[0],bs.inputs['Base Color'])
 img.pack()
# Seat the lower strap ends in the narrower curved P019 shell; avoid a loose tab.
for v in straps.data.vertices:
 x,y,z=v.co; sign=1 if x>0 else -1
 if z<.16:
  t=max(0,min(1,(.15-z)/.095));v.co.x-=sign*.043*t;v.co.z+=.037*t;v.co.y-=.005*t
 elif .32<z<.35 and y>-.16:v.co.y+=.015
straps.data.update()
# Remove OBJ-imported source materials and external image references which are no longer used.
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.object.select_all(action='SELECT');bpy.context.view_layer.objects.active=body;bpy.ops.wm.save_as_mainfile(filepath=str(review/'P019_BackBasket.blend'));bpy.ops.export_scene.fbx(filepath=str(dest/'P019_BackBasket.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,path_mode='STRIP',mesh_smooth_type='FACE')
sys.path.insert(0,str(Path.home()/'.codex/skills/blender-asset-qa/scripts'));import qa_check
report=qa_check.run(objects=[body.name,straps.name],profile='game',eps=.00005,tol=.003)
(review/'geometry.json').write_text(json.dumps(report,indent=2));print('RESULT',report['errors'],report['warnings']);assert report['pass']

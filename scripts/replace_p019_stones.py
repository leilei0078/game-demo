"""Blender -b P019_BackBasket.blend -P this.py -- <Heathland> <FBX destination> <review folder>."""
import bpy,bmesh,math,struct,sys,json
from pathlib import Path
from mathutils import Vector,Euler
heath,dest,out=[Path(x).resolve() for x in sys.argv[-3:]]
bpy.context.preferences.filepaths.save_version=0
raw=(heath/'Boulder.bytes').read_bytes();nv,ni=struct.unpack_from('<ii',raw);rows=list(struct.iter_unpack('<8f',raw[8:8+nv*32]));faces=list(struct.iter_unpack('<3H',raw[8+nv*32:]));assert len(faces)*3==ni
mesh=bpy.data.meshes.new('Quarry scan');mesh.from_pydata([(r[0],r[2],r[1]) for r in rows],[],faces);mesh.update();uv=mesh.uv_layers.new(name='Scan UV');
for loop in mesh.loops:uv.data[loop.index].uv=rows[loop.vertex_index][6:8]
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=.000002);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
old=bpy.data.objects['Stone load'];bpy.data.objects.remove(old,do_unlink=True);src=bpy.data.objects.new('Reduced scan master',mesh);bpy.context.collection.objects.link(src);bpy.context.view_layer.objects.active=src;dec=src.modifiers.new('Load scale reduction','DECIMATE');dec.ratio=.052;bpy.ops.object.modifier_apply(modifier=dec.name)
mat=bpy.data.materials.new('P019_QuarryStone');bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Roughness'].default_value=.91;img=bpy.data.images.load(str(dest/'P019_StoneColor.jpg'));img.pack();n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=img;mat.node_tree.links.new(n.outputs['Color'],bs.inputs['Base Color']);src.data.materials.append(mat)
# Settled lower bed, longer fractured slabs and smaller chips. Different exposed scan faces.
layout=[(0,0,.354,.32,.21,.104,11,8,-12),(-.108,.047,.420,.179,.114,.101,-13,21,31),(.040,.092,.433,.171,.092,.075,17,-8,-13),(.134,.008,.419,.136,.108,.086,13,-17,72),(-.100,-.066,.414,.154,.098,.090,16,14,-31),(.045,-.065,.424,.166,.096,.076,-21,-12,20),(-.029,.002,.463,.173,.108,.096,11,-19,47),(.091,.046,.468,.102,.072,.071,-19,21,-33),(-.126,-.012,.465,.090,.066,.059,16,-16,71),(.012,-.086,.463,.087,.067,.053,-19,-23,5)]
chunks=[]
for i,(x,y,z,sx,sy,sz,rx,ry,rz) in enumerate(layout):
 obj=src.copy();obj.data=src.data.copy();bpy.context.collection.objects.link(obj);chunks.append(obj)
 base=Euler((math.radians((i%3)*72),math.radians((i%4)*64),math.radians(i*133))).to_matrix();coords=[base@v.co for v in obj.data.vertices];lo=Vector(map(min,zip(*coords)));hi=Vector(map(max,zip(*coords)));mid=(hi+lo)*.5;size=hi-lo;rot=Euler(tuple(map(math.radians,(rx,ry,rz)))).to_matrix()
 for v,c in zip(obj.data.vertices,coords):
  a=c-mid;v.co=Vector((x,y,z))+rot@Vector((a.x/size.x*sx,a.y/size.y*sy,a.z/size.z*sz))
 for f in obj.data.polygons:f.use_smooth=True
bpy.data.objects.remove(src,do_unlink=True);bpy.ops.object.select_all(action='DESELECT');[o.select_set(True) for o in chunks];bpy.context.view_layer.objects.active=chunks[0];bpy.ops.object.join();rock=bpy.context.object;rock.name='Stone load';rock.data.name='Heath quarry fragments';bpy.data.orphans_purge(do_recursive=True)
# Keep all existing pivots, basket surface, straps and the cargo node name.
bpy.ops.object.select_all(action='SELECT');bpy.ops.wm.save_as_mainfile(filepath=str(out/'P019_BackBasket.blend'));bpy.ops.export_scene.fbx(filepath=str(dest/'P019_BackBasket.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,path_mode='STRIP',mesh_smooth_type='FACE')
sys.path.insert(0,str(Path.home()/'.codex/skills/blender-asset-qa/scripts'));import qa_check
report=qa_check.run(objects=[rock.name],profile='game',eps=.00005,tol=.003);(out/'stone-qa.json').write_text(json.dumps(report,indent=2));print('STONE_QA',report['errors'],report['warnings'])

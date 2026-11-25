"""Run in Blender on Survivor_LightOlive.blend; pass the Unity asset directory after --."""
import bpy, sys
from pathlib import Path
out=Path(sys.argv[-1]).resolve(); out.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene;scene.frame_set(1);rig=bpy.data.objects['RIG_Survivor_Mixamo'];rig.hide_viewport=False;rig.hide_set(False);rig.hide_select=False;rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis.identity(); b.location=(0,0,0); b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0);b.scale=(1,1,1)
bpy.context.view_layer.update();parts=[o for cname in ('01_Survivor_Original','02_Starter_Outfit') for o in bpy.data.collections[cname].objects if o.type=='MESH'];body=bpy.data.objects['SK_Survivor_Body']
for o in parts:
 if o.parent_type=='BONE':
  head=o.parent_bone;world=o.matrix_world.copy();o.parent_type='OBJECT';o.matrix_world=world;g=o.vertex_groups.new(name=head);g.add(list(range(len(o.data.vertices))),1,'REPLACE')
# Preserve the UV data of each mesh on the same active UV channel when joining.
for o in parts:o.data.uv_layers.active.name='UVMap'
bpy.ops.object.select_all(action='DESELECT');[o.select_set(True) for o in parts];bpy.context.view_layer.objects.active=body;bpy.ops.object.join();body.name='Survivor_Worker_Skin'
# Use the semantic joint names consumed by FarmerPose on this export copy only.
names={'Hips':'Pelvis','Spine':'Spine','Spine1':'Spine2','Spine2':'Spine3','Neck':'Neck','Head':'Head'}
for side,prefix in [('Left','L '),('Right','R ')]:
 for src,dst in [('Shoulder','Clavicle'),('Arm','UpperArm'),('ForeArm','Forearm'),('Hand','Hand'),('UpLeg','Thigh'),('Leg','Calf'),('Foot','Foot'),('ToeBase','Toe0'),('HandIndex1','Finger1'),('HandMiddle1','Finger2'),('HandPinky1','Finger4')]: names[side+src]=prefix+dst
for src,dst in names.items():rig.data.bones['mixamorig:'+src].name='Char_Farmer_001 '+dst
for i,m in enumerate(body.data.materials):
 sh=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');img=sh.inputs['Base Color'].links[0].from_node.image;name='SurvivorColor%02d'%i;path=out/(name+'.png');path.write_bytes(img.packed_file.data)
 mat=bpy.data.materials.new('SurvivorSurface%02d'%i);node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(path));mat.node_tree.links.new(tex.outputs['Color'],node.inputs['Base Color']);node.inputs['Roughness'].default_value=.88;body.data.materials[i]=mat
rig.select_set(True);body.select_set(True);assert rig in bpy.context.selected_objects and body.parent==rig and any(m.type=='ARMATURE' for m in body.modifiers);bpy.ops.export_scene.fbx(filepath=str(out/'Survivor_LightOlive.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE',path_mode='STRIP');body.data.calc_loop_triangles();print('SURVIVOR_EXPORT',len(body.data.loop_triangles),len(body.data.materials),len(rig.data.bones))

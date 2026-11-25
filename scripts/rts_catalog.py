"""Render transparent in-game command portraits and arrange the editable asset catalog."""
import bpy, math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]; out=root/'public/rts'; out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(root/'output/rts/FrontierRTS.blend'))
s=bpy.context.scene; cols=[c for c in bpy.data.collections if any(o.type=='MESH' for o in c.objects)]
cam=bpy.data.objects.new('Catalog camera',bpy.data.cameras.new('Catalog camera'));s.collection.objects.link(cam);s.camera=cam;cam.data.type='ORTHO'
s.render.engine='BLENDER_WORKBENCH';s.display.shading.color_type='MATERIAL';s.display.shading.light='STUDIO';s.display.shading.show_shadows=True;s.display.shading.show_cavity=True;s.display.shading.cavity_type='WORLD';s.display.shading.show_specular_highlight=True;s.render.film_transparent=True;s.render.image_settings.color_mode='RGBA';s.render.resolution_x=s.render.resolution_y=160;s.render.resolution_percentage=100
for c in cols:
 for other in cols:other.hide_render=other!=c
 pts=[o.matrix_world@Vector(corner) for o in c.objects if o.type=='MESH' for corner in o.bound_box];lo=Vector([min(p[k] for p in pts) for k in range(3)]);hi=Vector([max(p[k] for p in pts) for k in range(3)]);center=(lo+hi)/2
 if c.name in ['hero','worker','soldier','archer']:center.z=1.18;size=2.44
 else:size=max((hi-lo)) *1.38
 cam.location=center+Vector((5,-8,4.8));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=size;s.render.filepath=str(out/(c.name+'.png'));bpy.ops.render.render(write_still=True)
for c in cols:c.hide_render=False
bpy.data.objects.remove(cam,do_unlink=True)
for i,c in enumerate(sorted(cols,key=lambda c:c.name)):
 for o in c.objects:o.location+=Vector((i%4*10,i//4*10,0))
for ar in bpy.context.screen.areas:
 if ar.type=='VIEW_3D':ar.spaces.active.region_3d.view_distance=45;ar.spaces.active.region_3d.view_location=(15,10,1.2);ar.spaces.active.shading.color_type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(root/'output/rts/FrontierRTS.blend'))

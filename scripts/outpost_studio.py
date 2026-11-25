import math
from pathlib import Path

import bpy
from mathutils import Vector


def render_tiles(folder):
    scene=bpy.context.scene; camera=scene.camera; world=bpy.data.collections['SET_GroundAndInstances']; world.hide_render=True
    bpy.ops.mesh.primitive_plane_add(size=80,location=(0,0,-.015)); floor=bpy.context.object; floor.name='StudioSweep'
    material=bpy.data.materials.new('M_StudioSweep'); shader=next(node for node in material.node_tree.nodes if node.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(.144,.159,.153,1); shader.inputs['Roughness'].default_value=.88; floor.data.materials.append(material)
    scene.render.resolution_x=960; scene.render.resolution_y=484; scene.cycles.samples=16
    direction=Vector((1,-1,math.sqrt(2/3))).normalized()
    for key,title,center,scale,yaw in (('01_Shelter','shelter',(0,-.18,2.10),14.0,0),('02_LogCrib','crib',(0,0,.58),7.0,math.pi/2),('04_Fence','fence',(0,0,.64),3.65,0),('03_Standard','standard',(.37,0,1.62),8.35,0)):
        obj=bpy.data.objects.new('StudioSample',None); scene.collection.objects.link(obj); obj.instance_type='COLLECTION'; obj.instance_collection=bpy.data.collections['KIT_'+key]; obj.rotation_euler.z=yaw
        camera.data.ortho_scale=scale; camera.location=Vector(center)+direction*35; scene.render.filepath=str(folder/(title+'.png')); bpy.ops.render.render(write_still=True); bpy.data.objects.remove(obj,do_unlink=True)
    bpy.data.objects.remove(floor,do_unlink=True); world.hide_render=False


if __name__=='__main__': render_tiles(Path(bpy.data.filepath).parent)

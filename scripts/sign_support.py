"""Build a wall-mounted cantilever and two connected sign hangers, in metres."""
import math
import bpy
import bmesh
from mathutils import Vector


def build_sign_support(collection, cx=-2.65, cy=1.55):
    center_x, wall_y, sign_y = cx+1.65, cy-1.53-.1, cy-1.53-.9
    created = []
    material = bpy.data.materials['iron']

    def finish(obj, name, role):
        obj.name = name
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
        obj.data.materials.append(material)
        obj['asset_group'] = 'sign_support'
        obj['support_role'] = role
        created.append(obj)
        return obj

    def block(name, position, size, role, edge=.005):
        sx,sy,sz=size
        vertices=[(x*sx/2,y*sy/2,z*sz/2) for x,y,z in [
            (-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
            (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        data=bpy.data.meshes.new(name)
        data.from_pydata(vertices,[],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
        obj=bpy.data.objects.new(name,data)
        collection.objects.link(obj)
        obj.location=position
        finish(obj,name,role)
        if edge:
            bevel=obj.modifiers.new('Forged edge bevel','BEVEL')
            bevel.width=edge
            bevel.segments=1
            bevel.use_clamp_overlap=True
        return obj

    def bar(name, start, end, width, role):
        a,b=Vector(start),Vector(end)
        obj=block(name,(a+b)/2,(width,width,(b-a).length),role)
        obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
        return obj

    def cylinder(name, start, end, radius, role):
        a,b=Vector(start),Vector(end)
        bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=radius,depth=(b-a).length,location=(a+b)/2)
        obj=finish(bpy.context.object,name,role)
        obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
        return obj

    plate=block('SM_Sign_wall_mount',(center_x,wall_y-.005,2.71),(.16,.075,.40),'wall_anchor')
    bar('SM_Sign_cantilever',(center_x,wall_y,2.84),(center_x,sign_y-.025,2.84),.075,'cantilever')
    bar('SM_Sign_diagonal_brace',(center_x,wall_y-.01,2.57),(center_x,sign_y+.07,2.84),.038,'brace')
    cylinder('SM_Sign_crossbar',(center_x-.35,sign_y,2.84),(center_x+.35,sign_y,2.84),.030,'crossbar')
    for index,z in enumerate((2.57,2.86)):
        cylinder(f'SM_Sign_wall_bolt_{index}',(center_x,wall_y-.050,z),(center_x,wall_y-.025,z),.023,'wall_bolt')
    for index,x in enumerate((center_x-.255,center_x+.255)):
        # A closed collar surrounds the crossbar. Its inside top rests on the bar.
        bpy.ops.mesh.primitive_torus_add(major_radius=.047,minor_radius=.009,
            major_segments=20,minor_segments=8,location=(x,sign_y,2.832),rotation=(0,math.pi/2,0))
        finish(bpy.context.object,f'SM_Sign_suspension_ring_{index}','suspension_ring')
        cylinder(f'SM_Sign_hanger_{index}',(x,sign_y,2.50),(x,sign_y,2.785),.013,'hanger')
        block(f'SM_Sign_top_socket_{index}',(x,sign_y,2.508),(.070,.075,.034),'sign_socket',.004)
    for obj in created:
        bm=bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>4])
        bm.to_mesh(obj.data)
        bm.free()
    bpy.context.view_layer.update()
    return created


def refine_sign():
    collection=next(c for c in bpy.data.collections if c.name.startswith('02'))
    for obj in list(collection.objects):
        if obj.name.startswith(('Sign bracket post','Sign iron hanger','SM_Sign_')):
            bpy.data.objects.remove(obj,do_unlink=True)
    objects=build_sign_support(collection)
    for obj in collection.objects:
        if obj.name.startswith(('Hanging wayfarer sign','Sign gilded rim','Eight point wayfarer emblem')):
            if obj.name.startswith('Sign gilded rim'):
                # Keep the trim on the full-thickness face, inside the board's bevel.
                obj.location.z = 1.955 if obj.location.z < 2.2 else 2.445
                obj.location.y = -.947
            if obj.name.startswith('Eight point'):
                # Seat the painted emblem 1.5 mm above the panel, within contact tolerance.
                for vertex in obj.data.vertices:
                    vertex.co.y=-.9415
                bm=bmesh.new()
                bm.from_mesh(obj.data)
                bmesh.ops.triangulate(bm,faces=bm.faces[:])
                bm.to_mesh(obj.data)
                bm.free()
            obj['asset_group']='sign_support'
            obj['support_role']='sign_board' if obj.name.startswith('Hanging') else 'decoration'
            objects.append(obj)
    bpy.context.view_layer.update()
    return objects

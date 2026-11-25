"""Fit a modular worn outfit to the original downloaded Mixamo survivor."""
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

OUT = Path(__file__).resolve().parents[1] / 'output/survivor-outfit'
TEXTURES = OUT / 'source/textures'
QA = OUT / 'qa'
QA.mkdir(exist_ok=True)
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
import review_shots

assert not bpy.data.filepath
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=str(OUT / 'source/source/survivor.fbx'))
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
rig = next(obj for obj in scene.objects if obj.type == 'ARMATURE')
body = next(obj for obj in scene.objects if obj.type == 'MESH' and obj.modifiers)
eyes = [obj for obj in scene.objects if obj.type == 'MESH' and obj != body]
rig.name, body.name = 'RIG_Survivor_Mixamo', 'SK_Survivor_Body'
scene.render.image_settings.file_format = 'PNG'
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
bpy.context.preferences.filepaths.save_version = 0


def collection(name):
    result = bpy.data.collections.new(name)
    scene.collection.children.link(result)
    return result


character = collection('01_Survivor_Original')
wardrobe = collection('02_Starter_Outfit')
studio = collection('03_Review_Studio')
for obj in [rig, body, *eyes]:
    for owner in list(obj.users_collection):
        owner.objects.unlink(obj)
    character.objects.link(obj)


def reconnect(mat, filename, roughness):
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = next(node for node in nodes if node.type == 'BSDF_PRINCIPLED')
    for node in list(nodes):
        if node.type in {'TEX_IMAGE','NORMAL_MAP'}:
            nodes.remove(node)
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(str(TEXTURES / filename))
    tex.image.pack()
    links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    shader.inputs['Roughness'].default_value = roughness


reconnect(body.data.materials[0], 'Personagem_BaseColor.png', .65)
reconnect(eyes[0].data.materials[0], 'Olho_BaseColor.png', .29)
bpy.context.view_layer.update()
body_coords = [body.matrix_world @ vert.co for vert in body.data.vertices]
body_faces = [tuple(face.vertices) for face in body.data.polygons]
skin = BVHTree.FromPolygons(body_coords, body_faces)
influences = [{body.vertex_groups[entry.group].name: entry.weight for entry in vert.groups if entry.weight > 0}
              for vert in body.data.vertices]


def fabric(name, color, seed, rubber=False):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1)
    nodes, links = material.node_tree.nodes, material.node_tree.links
    shader = next(node for node in nodes if node.type == 'BSDF_PRINCIPLED')
    shader.inputs['Roughness'].default_value = .86 if not rubber else .76
    shader.inputs['Specular IOR Level'].default_value = .24
    if not rubber:
        shader.inputs['Sheen Weight'].default_value = .16
    size = 1024
    yy, xx = np.mgrid[:size, :size] / size
    rng = np.random.default_rng(seed)
    def field(nx, ny):
        grid = rng.uniform(-1, 1, (ny, nx))
        sx, sy = xx*nx, yy*ny
        ix, iy = sx.astype(int), sy.astype(int)
        tx, ty = sx-ix, sy-iy
        tx, ty = tx*tx*(3-2*tx), ty*ty*(3-2*ty)
        return ((1-tx)*(1-ty)*grid[iy,ix] + tx*(1-ty)*grid[iy,(ix+1)%nx] +
                (1-tx)*ty*grid[(iy+1)%ny,ix] + tx*ty*grid[(iy+1)%ny,(ix+1)%nx])
    coarse, marks, lint = field(9, 12), field(36, 42), field(119,143)
    warp = np.cos(xx*math.tau*191 + .38*lint)
    weft = np.cos(yy*math.tau*179 + .30*lint)
    weave = (.53*warp + .47*weft) * (.70 + .30*warp*weft)
    if rubber:
        weave *= .20
    fade = np.clip(.67*coarse+.28*marks+.16*lint-.10,0,.77)
    value = np.clip(.91+.22*coarse+.14*marks+.08*lint+.060*weave, .54, 1.55)
    rgba = np.ones((size,size,4),dtype=np.float32)
    dust = [.067,.059,.050] if name.startswith('M_Shorts') else [.042,.036,.025]
    rgba[:,:,:3] = np.clip(np.array(color)*value[:,:,None]+fade[:,:,None]*dust,0,1)
    tex = bpy.data.images.new(name+'_BaseColor',width=size,height=size,alpha=False)
    tex.pixels.foreach_set(rgba.ravel()); tex.pack()
    tex_node = nodes.new('ShaderNodeTexImage'); tex_node.image=tex; tex_node.location=(-560,160)
    links.new(tex_node.outputs['Color'], shader.inputs['Base Color'])
    shader.inputs['Base Color'].default_value=(*color,1)
    tex_node.select=True; nodes.active=tex_node
    bump = nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.19
    bump.inputs['Distance'].default_value=.0018 if not rubber else .001
    bump.location=(-170,-90)
    grain=nodes.new('ShaderNodeTexNoise'); grain.inputs['Scale'].default_value=285 if not rubber else 150
    grain.inputs['Roughness'].default_value=.74; grain.location=(-390,-120)
    links.new(grain.outputs['Fac'],bump.inputs['Height']); links.new(bump.outputs[0],shader.inputs['Normal'])
    return material


cloth = fabric('M_Shirt_Faded_Olive', (.214,.211,.149), 18)
seam = fabric('M_Shirt_Seam', (.145,.144,.102), 19)
thread = fabric('M_Shirt_Frayed_Thread', (.309,.280,.186), 21)
black = fabric('M_Shorts_Faded_Black', (.032,.034,.036), 25)
black_edge = fabric('M_Shorts_Worn_Hem', (.056,.057,.055), 26)
canvas = fabric('M_Shoes_Dusty_Canvas', (.073,.091,.108), 28)
rubber = fabric('M_Shoes_Old_Rubber', (.31,.277,.216), 29, True)
outsole = fabric('M_Shoes_Outsole', (.045,.044,.036), 30, True)
laces = fabric('M_Shoes_Laces', (.31,.272,.199), 31)
shoe_trim = fabric('M_Shoes_Faded_Binding', (.049,.059,.064), 33)


class Pattern:
    def __init__(self, materials):
        self.vertices, self.faces, self.uvs, self.mats = [], [], [], []
        self.materials = materials

    def point(self, coord, uv):
        self.vertices.append(Vector(coord)); self.uvs.append(uv)
        return len(self.vertices)-1

    def face(self, indices, material=0):
        self.faces.append(tuple(indices)); self.mats.append(material)

    def strip(self, lower, upper, material=0, omit=()):
        for i in range(len(lower)):
            if i not in omit:
                j=(i+1)%len(lower)
                self.face((lower[i],lower[j],upper[j],upper[i]),material)

    def hem(self, ring, center, shift, material):
        inner=[]
        for i in ring:
            coord=self.vertices[i]
            radial=(Vector(center)-coord)*.09
            inner.append(self.point(coord+radial+Vector(shift),(self.uvs[i][0],self.uvs[i][1]+.014)))
        self.strip(ring,inner,material)

    def finish(self, name, force_bone=None):
        used=sorted({i for face in self.faces for i in face})
        remap={value:index for index,value in enumerate(used)}
        data=bpy.data.meshes.new(name+'_Mesh')
        data.from_pydata([self.vertices[i] for i in used],[],[tuple(remap[i] for i in face) for face in self.faces])
        for mat in self.materials: data.materials.append(mat)
        uv=data.uv_layers.new(name='Fabric')
        for face, material in zip(data.polygons,self.mats):
            face.material_index=material; face.use_smooth=True
            for loop in face.loop_indices:
                uv.data[loop].uv=self.uvs[used[data.loops[loop].vertex_index]]
        bm=bmesh.new(); bm.from_mesh(data)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free(); data.update()
        obj=bpy.data.objects.new(name,data); wardrobe.objects.link(obj)
        for vert in data.vertices:
            if force_bone:
                weights={force_bone:1.}
            else:
                near, _, face_index, _ = skin.find_nearest(vert.co)
                weights={}
                for index in body_faces[face_index]:
                    factor=1/max(.0006,(body_coords[index]-near).length)**2
                    for bone, weight in influences[index].items():
                        weights[bone]=weights.get(bone,0)+factor*weight
                weights={bone:weight for bone,weight in sorted(weights.items(),key=lambda x:-x[1])[:4] if weight>0}
            total=sum(weights.values()); assert total>0
            for bone, weight in weights.items():
                group=obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                group.add([vert.index],weight/total,'REPLACE')
        modifier=obj.modifiers.new('Mixamo Skin','ARMATURE'); modifier.object=rig
        obj.parent=rig; obj.matrix_parent_inverse=rig.matrix_world.inverted()
        return obj


def clearance(coord, threshold, padding):
    hit, normal, _, distance=skin.find_nearest(coord)
    offset=(coord-hit).dot(normal)
    if distance<threshold and offset<padding:
        return coord+normal*(padding-offset)
    return coord


def shirt():
    pat=Pattern([cloth,seam,thread]); rings=[]; count=48
    profiles=[(1.105,.226,.028,.194),(1.154,.222,.022,.172),(1.223,.205,.017,.149),
              (1.280,.196,.020,.148),(1.343,.205,.024,.152),(1.402,.228,.028,.159),
              (1.449,.254,.037,.181),(1.501,.264,.048,.183),(1.553,.254,.056,.164),
              (1.607,.228,.056,.144),(1.651,.172,.053,.117),(1.690,.103,.052,.092)]
    for row,(z,width,cy,depth) in enumerate(profiles):
        ring=[]
        for index in range(count):
            theta=index*math.tau/count
            wrinkle=(.005*math.sin(9*theta+z*41)+.004*math.sin(14*theta-z*38))*min(1,(11-row)*.35)
            position=Vector(((width+wrinkle)*math.sin(theta),cy-(depth+wrinkle)*math.cos(theta),z))
            if row==0:
                position.z+=.016*math.sin(theta*3+.6)+.008*math.sin(theta*10)+(.027 if index in (5,37) else 0)
            if index==44 and row in (3,4):
                position.x+=.012 if row==4 else -.003
                position.z+=.013 if row==4 else .002
            if index==45 and row==4:
                position.z-=.006
            if row>=9:
                position.z-=max(0,math.cos(theta))**6 * (row-8)*.015
                position.z+=.004*math.sin(7*theta)
            position=clearance(position,.055,.012)
            ring.append(pat.point(position,(.045+.89*index/count,.075+row*.068)))
        rings.append(ring)
    openings=[(8,16,1),(32,40,-1)]
    for row in range(len(rings)-1):
        omit=set()
        if 6<=row<9:
            for start,end,_ in openings: omit.update(range(start,end))
        if row==3: omit.update([44])
        pat.strip(rings[row],rings[row+1],omit=omit)
    for start,end,side in openings:
        rim=[rings[6][i] for i in range(start,end+1)]+[rings[j][end] for j in range(7,10)]+\
            [rings[9][i] for i in range(end-1,start-1,-1)]+[rings[j][start] for j in range(8,6,-1)]
        origin=Vector((side*.302,.049,1.426)); direction=Vector((side*.48,-.08,-.873)).normalized()
        axis=Vector((0,1,0)); up=direction.cross(axis).normalized()*side
        cuff=[]
        for step in range(1,4):
            current=[]
            for i,index in enumerate(rim):
                point=pat.vertices[index]
                phi=(-2.40 + math.tau*i/len(rim)) if side>0 else (-.62 - math.tau*i/len(rim))
                end=origin+axis*(math.cos(phi)*.101)+up*(math.sin(phi)*.092)
                end+=direction*(.008*math.sin(i*2.1)+(.016 if i in (4,11) else 0))
                coord=point.lerp(end,step/3)
                coord.y+=.014*max(0,math.cos(phi))*math.sin(math.pi*step/4)
                coord=clearance(coord,.085,.032)
                current.append(pat.point(coord,(.075+.83*i/len(rim),.66+step*.080)))
            pat.strip(rim if step==1 else cuff,current,1 if step==3 else 0); cuff=current
        pat.hem(cuff,origin,-direction*.003,1)
    pat.hem(rings[0],(0,.019,1.12),(0,0,.004),2)
    pat.hem(rings[-1],(0,.052,1.675),(0,0,-.005),1)
    return pat.finish('SK_Worn_ShortShirt')


def shorts():
    pat=Pattern([black,black_edge]); count=48; rows=[]
    for row,(z,width,center,depth) in enumerate([( .908,.239,.025,.163),(.957,.232,.029,.171),
                                                (1.015,.223,.025,.189),(1.077,.205,.032,.169),
                                                (1.121,.193,.026,.155),(1.136,.194,.027,.157)]):
        ring=[]
        for i in range(count):
            theta=i*math.tau/count
            wave=.004*math.sin(13*theta+z*31)+.005*math.sin(7*theta-z*17)
            position=Vector(((width+wave)*math.sin(theta),center-(depth+wave)*math.cos(theta),z+.004*math.sin(theta*3)))
            ring.append(pat.point(clearance(position,.047,.010),(.045+.9*i/count,.61+row*.062)))
        rows.append(ring)
    for i in range(len(rows)-1): pat.strip(rows[i],rows[i+1],int(i==len(rows)-2))
    pat.hem(rows[-1],(0,.026,1.131),(0,0,-.005),1)
    bridge=[pat.point((0,.026+.163*math.cos(k*math.pi/12),.908-.060*math.sin(k*math.pi/12)),
                      (.14+.065*k,.565)) for k in range(1,12)]
    arcs=[rows[0][:25]+bridge, rows[0][24:]+[rows[0][0]]+list(reversed(bridge))]
    for side, base in zip((1,-1),arcs):
        last=base
        for step,(z,cx,cy,rx,ry) in enumerate([(.854,.129,.014,.133,.134),(.802,.140,.019,.125,.126),
                                               (.735,.150,.026,.111,.112),(.671,.153,.034,.106,.106),
                                               (.650,.155,.035,.105,.102)]):
            ring=[]; shift=(-.013 if side<0 else 0)
            for i,index in enumerate(base):
                angle=math.tau*i/len(base) + (-math.pi/2 if side==1 else math.pi/2)
                x,y=math.cos(angle),math.sin(angle)
                x+=.05*math.sin(angle*8+step); y+=.055*math.sin(angle*7-step*.6)
                zz=z+shift+(.006*math.sin(angle*9)+(.018 if i in (11,27) else 0))*(step/4)**3
                coord=Vector((side*cx+rx*x,cy+ry*y,zz))
                if step==0:
                    previous=pat.vertices[index]; coord.x=.65*coord.x+.35*previous.x; coord.y=.65*coord.y+.35*previous.y
                    coord.z=.72*coord.z+.28*previous.z
                coord.y-=max(0,-y)*(.026 if step<2 else .014 if step==2 else .006)
                coord=clearance(coord,.047,.016)
                ring.append(pat.point(coord,(.035+.91*i/len(base),.55-step*.105)))
            pat.strip(last,ring,1 if step==4 else 0); last=ring
        pat.hem(last,(side*.155,.035,.658+shift),(0,0,.006),1)
    return pat.finish('SK_Worn_BlackShorts')


def shoe(side):
    pat=Pattern([canvas,rubber,outsole,laces,shoe_trim]); rings=[]; count=64
    profiles=[(-.008,-.021,.075,.178),(.003,-.021,.077,.181),(.016,-.021,.078,.182),
              (.025,-.021,.077,.179),(.039,-.020,.075,.176),(.056,-.017,.072,.172),
              (.075,.005,.070,.134),(.099,.034,.060,.102),(.117,.061,.054,.079)]
    hole_side = 1 if side > 0 else -1
    torn = {(4,8),(4,9),(5,8)} if hole_side == 1 else set()
    for row,(z,cy,radx,rady) in enumerate(profiles):
        ring=[]
        for i in range(count):
            theta=math.tau*i/count
            fore=math.cos(theta)
            x=side*(.211+.026*max(0,fore)); y=cy-rady*fore
            wide=(1+.16*max(0,fore)-.20*max(0,-fore))*radx
            x+=side*wide*math.sin(theta)*(1+.035*math.cos(5*theta))
            height=z+.006*max(0,fore)**5
            if row>=7:
                height+=.020*max(0,-fore)**3-.010*math.sin(theta)**6
            if row in (4,5,6) and i in (8,9,10):
                height+=.0035*math.sin(i*3+row*1.4)
            position=Vector((x,y,height))
            if row<=7:
                probe=Vector((side*.217,.042,max(.006,position.z)))
                radial=Vector((position.x-probe.x,position.y-probe.y,0))
                heading=radial.normalized()
                hit,_,_,_=skin.ray_cast(probe+heading*.40,-heading,.415)
                if hit is not None and hit.x*side>0:
                    reach=(hit-probe).dot(heading)+.0085
                    position+=heading*max(0,reach-radial.length)
            ring.append(pat.point(position,(.055+.90*i/count,.050+row*.102)))
        rings.append(ring)
    for row in range(len(rings)-1):
        pat.strip(rings[row],rings[row+1],2 if row==0 else 1 if row<3 else 0,
                  omit={index for it,index in torn if it==row})
    bottom_center=(side*.224,-.019,-.008)
    bottom=pat.point(bottom_center,(.52,.195))
    for i,index in enumerate(rings[0]): pat.face((bottom,rings[0][(i+1)%count],index),2)
    pat.hem(rings[-1],(side*.217,.064,.119),(0,0,-.005),4)
    if torn:
        rim=[(4,8),(4,9),(4,10),(5,10),(5,9),(6,9),(6,8),(5,8)]
        outer=[rings[level][index] for level,index in rim]
        inner=[]
        for index in outer:
            original=pat.vertices[index]
            delta=Vector((side*.228,-.016,.053))-original
            inner.append(pat.point(original+delta.normalized()*.0027,
                                   (pat.uvs[index][0]+.006,pat.uvs[index][1]+.008)))
        pat.strip(outer,inner,3)
    mesh_bvh=BVHTree.FromPolygons(pat.vertices,pat.faces)
    def ribbon(start, end, width, material, arc=.001):
        left,right=[],[]
        along=Vector((end[0]-start[0],end[1]-start[1],0)).normalized()
        normal=Vector((-along.y,along.x,0))
        for step in range(9):
            t=step/8
            center=Vector((start[0]*(1-t)+end[0]*t,start[1]*(1-t)+end[1]*t,.25))
            for edge, sign in ((left,-1),(right,1)):
                hit,*_=mesh_bvh.ray_cast(center+normal*sign*width*.5,Vector((0,0,-1)))
                assert hit is not None
                hit.z+=.001+arc*math.sin(math.pi*t)
                edge.append(pat.point(hit,(.10+t*.78,.25+sign*.036)))
        for step in range(8): pat.face((left[step],left[step+1],right[step+1],right[step]),material)
    center=side*.222
    for row in range(4):
        y=-.092+row*.024
        rise=(.009 if row%2 else -.009)
        ribbon((center-.028,y),(center+.028,y+rise),.0046,3,.0030)
    suffix='Left' if side>0 else 'Right'
    return pat.finish('SK_Worn_CanvasShoe_'+suffix,'mixamorig:'+suffix+'Foot')


garments=[shirt(),shorts(),shoe(1),shoe(-1)]


def studio_material(name,color):
    mat=bpy.data.materials.new(name)
    shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(*color,1); shader.inputs['Roughness'].default_value=.90
    return mat


floor_data=bpy.data.meshes.new('Studio_Ground_Mesh')
floor_data.from_pydata([(-200,-200,-.010),(200,-200,-.010),(200,200,-.010),(-200,200,-.010)],[],[(0,1,2,3)])
floor=bpy.data.objects.new('Studio_Ground',floor_data); studio.objects.link(floor)
floor.data.materials.append(studio_material('M_Studio_Slate',(.054,.064,.069)))
world=bpy.data.worlds.new('W_Studio'); scene.world=world
world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.67,.72,.80,1)
world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.25
for name,location,power,size,color in [('Key',(-3,-4.5,5),560,3.4,(1,.92,.79)),
                                       ('Fill',(3,-3,2.7),250,3.5,(.78,.89,1)),
                                       ('Rim',(2.2,3.5,4.2),670,3.2,(1,.92,.83))]:
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size; data.color=color
    light=bpy.data.objects.new('Studio_'+name,data); studio.objects.link(light); light.location=location
    light.rotation_euler=(Vector((0,0,1.0))-light.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.objects.new('Studio_Camera',bpy.data.cameras.new('Studio_Lens')); studio.objects.link(camera)
camera.data.type='ORTHO'; scene.camera=camera
scene.render.engine='CYCLES'; scene.cycles.samples=30; scene.cycles.use_denoising=True
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type='METAL'; prefs.refresh_devices()
    for device in prefs.devices: device.use=device.type=='METAL'
    scene.cycles.device='GPU'
except (TypeError,RuntimeError): pass


def render(filename, direction, target=(0,0,1.01), scale=2.25, size=(760,920)):
    camera.location=Vector(target)+Vector(direction)
    camera.rotation_euler=(-Vector(direction)).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=size
    scene.render.filepath=str(OUT/filename)
    bpy.ops.render.render(write_still=True)


def summary():
    bpy.context.view_layer.update()
    results={}
    for obj in garments:
        report=qa_check.run(objects=[obj.name],profile='game',eps=.00035,tol=.004)
        results[obj.name]=report
        print('QA',obj.name,report['errors'],report['warnings'],flush=True)
    (QA/'geometry.json').write_text(json.dumps(results,indent=2))


render('front.png',(1.4,-6,1.0))
render('back.png',(-1.5,6,.95))
render('shoes.png',(1.0,-1.7,.80),target=(0,-.029,.115),scale=.76,size=(1000,640))
summary()
for mode in ('random','wire'):
    review_shots.run(objects=[obj.name for obj in garments],out=str(QA/(mode+'.png')),mode=mode,
                     views=['front','back','right','three_quarter'],res=380)

def bend(name,axis,angle):
    bone=rig.pose.bones['mixamorig:'+name]
    basis=(rig.matrix_world@bone.bone.matrix_local).to_quaternion()
    bone.rotation_mode='QUATERNION'
    bone.rotation_quaternion=Quaternion(basis.inverted()@Vector(axis),math.radians(angle))
    return bone


posed=[bend('LeftArm',(0,1,0),-28),bend('LeftForeArm',(1,0,0),-27),
       bend('RightArm',(1,0,0),-16),bend('RightForeArm',(1,0,0),-31),
       bend('LeftUpLeg',(1,0,0),-32),bend('LeftLeg',(1,0,0),45),
       bend('RightUpLeg',(1,0,0),13),bend('RightLeg',(1,0,0),13)]
scene.frame_end=24
scene.timeline_markers.new('01_Neutral',frame=1)
scene.timeline_markers.new('24_Clothing_Fit_Check',frame=24)
for bone in posed:
    bone.keyframe_insert('rotation_quaternion',frame=24,group=bone.name)
    bone.rotation_quaternion=(1,0,0,0)
    bone.keyframe_insert('rotation_quaternion',frame=1,group=bone.name)
rig.animation_data.action.name='QA_Clothing_Fit_Pose'
scene.frame_set(24)
floor.hide_render=True
render('fit-pose.png',(2.7,-6,1))
render('fit-pose-back.png',(-2.5,6,.82))
floor.hide_render=False
scene.frame_set(1)
camera.location=Vector((1.4,-6,2.01))
camera.rotation_euler=Vector((-1.4,6,-1)).to_track_quat('-Z','Y').to_euler()
scene.render.filepath='//front.png'

for obj in studio.objects: obj.hide_set(True)
rig.hide_set(True)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active=garments[0]; garments[0].select_set(True)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active; space.region_3d.view_perspective='ORTHO'
            space.region_3d.view_rotation=Vector((.8,-5,.5)).to_track_quat('Z','Y')
            space.region_3d.view_distance=3.6; space.region_3d.view_location=(0,0,1.02)
            space.shading.type='SOLID'; space.shading.color_type='TEXTURE'
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Survivor_StarterOutfit.blend'))

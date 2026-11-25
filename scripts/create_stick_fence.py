"""Build an original starter fence from crooked poles; use a fresh Blender process."""
import json
import math
import random
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/wood-fence/starter-sticks'
QA = OUT / 'qa'
QA.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
import review_shots

assert not bpy.data.filepath
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
bpy.context.preferences.filepaths.save_version = 0


def noise(u, v, nx, ny, seed):
    rng = np.random.default_rng(seed)
    grid = rng.uniform(-1, 1, (ny, nx))
    x, y = u*nx, v*ny
    ix, iy = np.floor(x).astype(int), np.floor(y).astype(int)
    dx, dy = x-ix, y-iy
    dx, dy = dx*dx*(3-2*dx), dy*dy*(3-2*dy)
    return ((1-dy)*((1-dx)*grid[iy%ny,ix%nx]+dx*grid[iy%ny,(ix+1)%nx]) +
            dy*((1-dx)*grid[(iy+1)%ny,ix%nx]+dx*grid[(iy+1)%ny,(ix+1)%nx]))


def make_material():
    size, strip_h = 1024, 960
    v, u = np.mgrid[:size,:size] / size
    v = v*size/strip_h
    coarse = noise(u,v,18,9,3)
    bend = noise(u,v,11,8,84)*.006 + noise(u,v,22,21,177)*.0018
    ribs = noise(u+bend,v,105,12,228)
    mid = noise(u+bend,v,165,45,683)
    fleck = noise(u,v,380,300,494)
    tooth = noise(u,v,58,44,56)
    flow = u + bend + .00085 * noise(u,v,80,96,270)
    furrow = (1-np.abs(np.sin(flow*math.tau*42 + .5*noise(u,v,20,60,147))))**6
    deep = furrow * np.clip(.62 + .9*noise(u,v,51,25,152), .04, 1)
    lichen = np.clip((noise(u,v,30,23,711)*.68 + tooth*.27 + fleck*.20 - .43)*3.5,0,1)
    scales = noise(flow,v,82,55,574)
    flake = np.clip((np.abs(scales+.31*tooth)-.40)*5,0,1)
    ridge = .13*coarse+.20*ribs+.13*mid+.14*fleck+.13*tooth-.77*deep-.19*flake
    color = np.array([.269,.222,.175])[None,None,:]*np.clip(1+ridge[:,:,None],.23,1.76)
    color += lichen[:,:,None]*np.array([.070,.081,.063])
    value = .34*ribs+.18*mid+.18*tooth-.84*deep+.10*fleck-.29*flake
    normal_y, normal_x = np.gradient(value)
    normal = np.stack([-normal_x*2.1,-normal_y*2.1,np.ones_like(value)],axis=-1)
    normal /= np.linalg.norm(normal,axis=-1,keepdims=True)
    normal = normal*.5+.5
    rough = np.clip(.84+.055*coarse+.04*mid,.65,.97)
    for tile in range(16):
        y,x = np.mgrid[:64,:64]/63
        radius = np.hypot((x-.43)*1.08,(y-.51))
        rings = np.sin(radius*68 + .5*np.sin(x*15+tile))
        cut = .08*rings+.06*noise(x,y,15,20,tile+133)
        split = (np.abs((y-.51)-.5*(x-.43)) < .008)*(x>.56)
        color[960:,tile*64:(tile+1)*64] = np.array([.51,.433,.344])*(1+cut-.31*split)[:,:,None]
    normal[960:] = [.5,.5,1]
    rough[960:] = .85
    def save(name, data, space):
        im = bpy.data.images.new(name,width=size,height=size,alpha=False)
        im.colorspace_settings.name=space
        rgba = np.ones((size,size,4),dtype=np.float32)
        rgba[:,:,:3] = data
        im.pixels.foreach_set(rgba.ravel())
        im.filepath_raw = str(OUT/(name+'.png'))
        im.file_format='PNG'
        im.save()
        im.pack()
        return im
    images = [save('StickBark_Color',color,'sRGB'),save('StickBark_Normal',normal,'Non-Color'),
              save('StickBark_Rough',rough[:,:,None],'Non-Color')]
    mat = bpy.data.materials.new('M_Starter_Bark')
    nodes, links = mat.node_tree.nodes,mat.node_tree.links
    bsdf = next(node for node in nodes if node.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Specular IOR Level'].default_value=.24
    for index,im in enumerate(images):
        node=nodes.new('ShaderNodeTexImage'); node.image=im; node.location=(-580,260-index*260)
        if index==1:
            nm=nodes.new('ShaderNodeNormalMap'); nm.location=(-300,-30)
            links.new(node.outputs['Color'],nm.inputs['Color']); links.new(nm.outputs[0],bsdf.inputs['Normal'])
        else:
            links.new(node.outputs['Color'],bsdf.inputs['Base Color' if index==0 else 'Roughness'])
        if index==0: active=node
    nodes.active=active
    return mat


material=make_material()


class Builder:
    def __init__(self):
        self.vertices=[]; self.polys=[]; self.uvs=[]; self.smooth=[]

    def face(self, indices, uv, smooth=False):
        self.polys.append(indices); self.uvs.append(uv); self.smooth.append(smooth)

    def stick(self, centers, radii, seed, sides=10, ground=False, slant=.16, roll=0):
        rng=random.Random(seed); path=[Vector(p) for p in centers]
        axis=(path[-1]-path[0]).normalized()
        ref=Vector((0,1,0)) if abs(axis.y)<.8 else Vector((0,0,1))
        side=axis.cross(ref).normalized(); around=axis.cross(side).normalized()
        if ground:
            side=Vector((1,0,0)); around=Vector((0,1,0))
        phase=rng.random()*math.tau; contour=[rng.uniform(.87,1.12) for _ in range(sides)]
        angles=[phase+(i+rng.uniform(-.15,.15))*math.tau/sides for i in range(sides)]
        shape=[(math.cos(a)*r,math.sin(a)*r*.92) for a,r in zip(angles,contour)]
        heights=[rng.uniform(-.07,.07)+math.cos(a)*slant for a in angles]
        dist=[0]
        for a,b in zip(path,path[1:]): dist.append(dist[-1]+(b-a).length)
        start_u=rng.uniform(.015,.62); width_u=rng.uniform(.19,.34)
        vstart=rng.uniform(.018,.16); vend=rng.uniform(.76,.921)
        rings=[]
        for j,(p,radius) in enumerate(zip(path,radii)):
            ring=[]
            for i,(cs,sn) in enumerate(shape):
                r=radius*(1+.023*math.sin(i*2.3+j*1.35))
                point=p+side*(cs*r)+around*(sn*r)
                if j==len(path)-1: point+=axis*(heights[i]*r)
                if ground and j==0: point.z=0
                ring.append(len(self.vertices)); self.vertices.append(tuple(point))
            rings.append(ring)
        for j,(a,b) in enumerate(zip(rings,rings[1:])):
            v0,v1=[vstart+(vend-vstart)*dist[k]/dist[-1] for k in (j,j+1)]
            for i in range(sides):
                nxt=(i+1)%sides
                u0=start_u+width_u*i/sides; u1=start_u+width_u*(i+1)/sides
                self.face([a[i],a[nxt],b[nxt],b[i]],[(u0,v0),(u1,v0),(u1,v1),(u0,v1)],True)
        for j in (0,len(rings)-1):
            ring=rings[j]; center=sum((Vector(self.vertices[k]) for k in ring),Vector())/sides
            idx=len(self.vertices); self.vertices.append(tuple(center))
            cx=(seed%16+.5)/16; cy=.96875
            cap=[(cx+.023*cs,cy+.023*sn) for cs,sn in shape]
            for i in range(sides):
                k=(i+1)%sides
                self.face([ring[i],ring[k],idx],[cap[i],cap[k],(cx,cy)])

    def finish(self,name,col):
        mesh=bpy.data.meshes.new(name+'_Mesh'); mesh.from_pydata(self.vertices,[],self.polys)
        mesh.materials.append(material); layer=mesh.uv_layers.new(name='BarkAtlas')
        for poly,coords,smooth in zip(mesh.polygons,self.uvs,self.smooth):
            poly.use_smooth=smooth
            for n,uv in zip(poly.loop_indices,coords): layer.data[n].uv=uv
        bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bmesh.ops.triangulate(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free(); mesh.update()
        obj=bpy.data.objects.new(name,mesh); col.objects.link(obj); obj.asset_mark()
        obj['grid_step_m']=1.; obj.asset_data.description='初始树枝栅栏 · 单块 1 米，可直连或直角拼接'
        return obj


def collection(name):
    col=bpy.data.collections.new(name); scene.collection.children.link(col); return col


assets=collection('01 · 独立栅栏块'); line=collection('02 · 直线拼接'); corner=collection('03 · 直角拼接')
stage=collection('04 · 预览布景')


def variant(label, upright, rail_heights, seed):
    build=Builder(); paths=[]
    for i,(foot,lean,height,radius) in enumerate(upright):
        rng=random.Random(seed+i); kink=rng.uniform(-.023,.023)
        pts=[]; radii=[]
        for t,scale in [(0,1.08),(.09,1.06),(.31,1),(.49,.97),(.66,1.01),(.83,.94),(.977,.89),(1,.85)]:
            x=foot+lean*t+kink*math.sin(math.pi*t)+.009*math.sin(t*10+i)*math.sin(math.pi*t)
            y=-.039 + .012*math.sin(t*5.3+i) + (i-1)*.004
            pts.append((x,y,t*height)); radii.append(radius*scale)
        build.stick(pts,radii,seed+i,12,True,.33 if i==1 else .19); paths.append(pts)
        if i in (0,2):
            p=Vector(pts[3 if i==0 else 5]); direction=Vector((-.8 if i==0 else .62,-.35,.6)).normalized()
            build.stick([p+direction*radius*.64,p+direction*radius*1.13,p+direction*radius*1.67],
                        [radius*.34,radius*.30,radius*.22],seed+62+i,7,False,.37)
    for j,z in enumerate((.34,.88)):
        dz=rail_heights[j]; rng=random.Random(seed+43+j); rr=.033 if j==0 else .0295
        # Terminals overlap around the grid node at complementary heights.
        xs=[-.527,-.50,-.455,-.402,-.30,-.15,.03,.22,.36,.422,.456,.5,.527]
        zs=[-.013,-.013,-.014,-.008,dz*.48,dz,dz*.71,dz*.15,.006,.013,.014,.014,.014]
        radii=[.016,.019,.021,rr*.92,rr,rr*1.09,rr*1.02,rr*.96,rr*.95,rr*.87,.019,.017,.0145]
        ys=[0,0,.003,.006,.006,.009,.010,.006,.005,.003,0,0,0]
        points=[(x,y,z+off) for x,y,off in zip(xs,ys,zs)]
        build.stick(points,radii,seed+43+j,11,False,.25)
        # A small broken spur on the lower cross branch is visible from the side.
        if j==0:
            p=Vector(points[6]); build.stick([p,p+Vector((.014,.032,.017)),p+Vector((.033,.057,.026))],
                                            [.016,.013,.008],seed+77,7,False,.35)
    return build.finish('SM_StarterFence_'+label,assets)


A=variant('A',[(-.498,.014,1.301,.048),(-.193,.122,1.090,.030),(.236,-.081,1.207,.033)],[-.032,.041],410)
B=variant('B',[(-.492,-.010,1.153,.040),(-.128,-.072,1.231,.033),(.227,.064,1.252,.037)],[.038,-.035],782)


def inst(src,col,name,x,y,angle=0):
    ob=bpy.data.objects.new(name,src.data); col.objects.link(ob); ob.location=(x,y,0)
    ob.rotation_euler.z=math.radians(angle); return ob


row=[inst(src,line,'Line_'+str(i+1),i-1,0) for i,src in enumerate([A,B,A])]
turn=[inst(A,corner,'Turn_1',-.5,2),inst(B,corner,'Turn_2',.5,2),inst(A,corner,'Turn_3',1,2.5,90)]
A.location=(-.75,-1.8,0); B.location=(.75,-1.8,0)
bpy.context.view_layer.update()
reports={}
for key, group in [('A',[A]),('B',[B]),('straight',row),('corner',turn)]:
    if len(group)>1:
        for a,b in zip(group,group[1:]):
            assert ((a.matrix_world@Vector((.5,0,0)))-(b.matrix_world@Vector((-.5,0,0)))).length<1e-5
    report=qa_check.run(objects=[o.name for o in group],profile='game')
    reports[key]=report; print('GEOMETRY',key,report['errors'],report['warnings'],flush=True)
(QA/'geometry.json').write_text(json.dumps(reports,indent=2))
assert all(r['pass'] for r in reports.values())
for mode in ('random','wire'):
    review_shots.run(objects=[A.name],out=str(QA/(mode+'.png')),views=['front','back','top','three_quarter'],mode=mode,res=320)


def select(ob):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob


for obj in (A,B):
    home=obj.location.copy(); obj.location=(0,0,0); select(obj)
    bpy.ops.export_scene.gltf(filepath=str(OUT/(obj.name+'.glb')),export_format='GLB',use_selection=True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(obj.name+'.fbx')),use_selection=True,object_types={'MESH'},
                             axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,
                             mesh_smooth_type='FACE',bake_anim=False,path_mode='COPY',embed_textures=True)
    obj.location=home


floor_mesh=bpy.data.meshes.new('StudioFloor'); floor_mesh.from_pydata([(-200,-200,-.003),(200,-200,-.003),
                                                                    (200,200,-.003),(-200,200,-.003)],[],[(0,1,2,3)])
floor=bpy.data.objects.new('Studio_Floor',floor_mesh); stage.objects.link(floor)
floor_mat=bpy.data.materials.new('M_Slate'); floor_mesh.materials.append(floor_mat)
fshader=next(n for n in floor_mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
fshader.inputs['Base Color'].default_value=(.014,.019,.025,1); fshader.inputs['Roughness'].default_value=.94
world=bpy.data.worlds.new('W_Studio'); scene.world=world; world.use_nodes=True
back=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND'); back.inputs[0].default_value=(.67,.72,.80,1); back.inputs[1].default_value=.21
for name,pos,power,size,color in [('Key',(-3.8,-3.2,5.5),560,2.7,(1,.86,.72)),('Fill',(2,-4,2.8),119,3.5,(.79,.86,1)),
                                  ('Rim',(2.4,3,4),390,3.0,(1,.92,.80))]:
    light=bpy.data.lights.new(name,'AREA'); light.energy=power; light.shape='DISK'; light.size=size; light.color=color
    ob=bpy.data.objects.new(name,light); stage.objects.link(ob); ob.location=pos
    ob.rotation_euler=(Vector((0,0,.6))-ob.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('Starter_Camera',bpy.data.cameras.new('StarterCamera')); stage.objects.link(cam); scene.camera=cam; cam.data.type='ORTHO'
scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type='METAL'; prefs.refresh_devices()
    for device in prefs.devices: device.use=device.type=='METAL'
    scene.cycles.device='GPU'
except (TypeError,RuntimeError): pass
scene.render.resolution_percentage=100; scene.view_settings.view_transform='AgX'
all_units=[A,B,*row,*turn]
def frame(target,delta,width):
    cam.location=Vector(target)+Vector(delta); cam.rotation_euler=(-Vector(delta)).to_track_quat('-Z','Y').to_euler(); cam.data.ortho_scale=width
def render(name,group,target,delta,width,w,h):
    for obj in all_units: obj.hide_render=obj not in group
    frame(target,delta,width); scene.render.resolution_x=w; scene.render.resolution_y=h
    scene.render.filepath=str(OUT/(name+'.png')); bpy.ops.render.render(write_still=True)
render('starter-fence',row,(0,0,.655),(1.25,-7,1.65),3.65,1560,840)
render('single', [A],(-.75,-1.8,.64),(1.6,-6,1.9),1.9,1040,880)
render('corner',turn,(.12,2.20,.61),(3.3,-6,3.1),3.02,1200,828)
for ob in all_units: ob.hide_render=False
frame((0,.25,.57),(3,-9,4.9),6.4)
for helper in stage.objects: helper.hide_set(True)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active; space.region_3d.view_perspective='ORTHO'; space.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
            space.region_3d.view_distance=7; space.region_3d.view_location=(0,.25,.57)
            space.shading.type='SOLID'; space.shading.color_type='TEXTURE'
select(A); bpy.context.view_layer.update()
for image in bpy.data.images:
    if image.packed_file: image.filepath='//'+Path(image.filepath).name
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'StarterStickFence.blend'),relative_remap=False)
print('DELIVER',[(o.name,len(o.data.polygons),list(o.dimensions)) for o in (A,B)],flush=True)

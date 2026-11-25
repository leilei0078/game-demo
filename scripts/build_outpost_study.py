import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector, noise

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/frontier-study-01'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
import review_shots

random.seed(617)
rand = random.uniform
scene = bpy.context.scene
for obj in list(scene.objects): bpy.data.objects.remove(obj, do_unlink=True)
scene.unit_settings.system = 'METRIC'
bpy.context.preferences.filepaths.save_version = 0


def group(name, visible=False):
    result = bpy.data.collections.new(name)
    if visible: scene.collection.children.link(result)
    return result


def material(name, color, shade=None, stretch=(1,1,1), bump=.03, glow=0):
    result = bpy.data.materials.new('M_' + name); result.diffuse_color = (*color,1)
    tree = result.node_tree; shader = next(node for node in tree.nodes if node.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(*color,1); shader.inputs['Roughness'].default_value=.84
    if glow:
        shader.inputs['Emission Color'].default_value=(*color,1); shader.inputs['Emission Strength'].default_value=glow
    if shade:
        coord=tree.nodes.new('ShaderNodeTexCoord'); mult=tree.nodes.new('ShaderNodeVectorMath'); mult.operation='MULTIPLY'
        tree.links.new(coord.outputs['Object'],mult.inputs[0]); mult.inputs[1].default_value=stretch
        tex=tree.nodes.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value=3.8; tex.inputs['Detail'].default_value=3.5; tex.inputs['Distortion'].default_value=.23
        tree.links.new(mult.outputs[0],tex.inputs[0]); ramp=tree.nodes.new('ShaderNodeValToRGB')
        for element,pos,rgb in zip(ramp.color_ramp.elements,(.27,.75),(shade,color)):
            element.position=pos; element.color=(*rgb,1)
        tree.links.new(tex.outputs['Fac'],ramp.inputs[0]); tree.links.new(ramp.outputs[0],shader.inputs['Base Color'])
        normal=tree.nodes.new('ShaderNodeBump'); normal.inputs['Strength'].default_value=.24; normal.inputs['Distance'].default_value=bump
        tree.links.new(tex.outputs['Fac'],normal.inputs['Height']); tree.links.new(normal.outputs[0],shader.inputs['Normal'])
    return result


def wood(name,color,shade): return material(name,color,shade,(11,14,.64),.018)


posts=wood('AdzedOak',(.195,.132,.078),(.052,.033,.023))
planks=[wood('GreyShake'+str(idx),tuple(val*gain for val in (.236,.191,.14)),tuple(val*gain for val in (.081,.068,.052))) for idx,gain in enumerate((.76,.94,1.1,1.28))]
bark=wood('CleftBark',(.16,.103,.062),(.045,.032,.021)); endgrain=material('CutPine',(.52,.347,.168),(.27,.155,.070),(4,4,1),.015)
stone=material('Fieldstone',(.225,.226,.199),(.088,.094,.087),(1.7,1.5,2),.08)
iron=material('BlackenedIron',(.056,.052,.044)); rope=material('Cord',(.32,.248,.149))
cloth=material('MadderCanvas',(.25,.048,.031),(.113,.021,.016),(4,4,4),.012)
gold=material('AmberGlass',(.97,.37,.07),glow=3.8); shadow=material('Interior',(.025,.023,.019))
grass_mats=[material('Sedge'+str(idx),col) for idx,col in enumerate(((.16,.145,.076),(.255,.207,.107),(.113,.122,.073),(.31,.255,.158)))]
house,yard,flag=[group(name) for name in ('KIT_01_Shelter','KIT_02_LogCrib','KIT_03_Standard')]
world=group('SET_GroundAndInstances',True); rig=group('SET_CameraAndLights',True); current=house


def mesh(name,vertices,faces,mats):
    data=bpy.data.meshes.new(name); data.from_pydata(vertices,[],faces); data.update()
    obj=bpy.data.objects.new(name,data); current.objects.link(obj)
    for mat in mats: data.materials.append(mat)
    return obj


def beam(name,start,finish,width,depth,mat,radial=False,cut=False,across=(1,0,0)):
    delta=Vector(finish)-Vector(start); length=delta.length
    if radial: section=[(math.cos(idx*math.tau/12)*width,math.sin(idx*math.tau/12)*depth) for idx in range(12)]
    else:
        half,side=width/2,depth/2; edge=min(half,side)*.18
        section=[(half-edge,side),(-half+edge,side),(-half,side-edge),(-half,-side+edge),(-half+edge,-side),(half-edge,-side),(half,-side+edge),(half,side-edge)]
    count=len(section); vertices=[(wide,thick,level*length/2) for level in (-1,1) for wide,thick in section]
    faces=[(idx,(idx+1)%count,(idx+1)%count+count,idx+count) for idx in range(count)]+[tuple(reversed(range(count))),tuple(range(count,2*count))]
    obj=mesh(name,vertices,faces,[mat,endgrain] if cut else [mat]); obj.location=(Vector(finish)+Vector(start))/2
    up=delta.normalized(); side=Vector(across)
    if abs(side.dot(up))>.999: side=Vector((0,1,0))
    side=(side-up*up.dot(side)).normalized(); obj.rotation_euler=Matrix((side,up.cross(side),up)).transposed().to_euler()
    if cut:
        for poly in obj.data.polygons[-2:]: poly.material_index=1
    return obj


def block(name,center,size,mat):
    wide,deep,high=size; east,north,level=center
    return beam(name,(east,north,level-high/2),(east,north,level+high/2),wide,deep,mat)


def rock(name,center,size):
    vertices=[]; count=7; phase=rand(0,6)
    for level,radius in ((-.5,.72),(-.24,1),(.27,.92),(.5,.66)):
        for idx in range(count):
            angle=math.tau*idx/count+phase+rand(-.07,.07)
            vertices.append((math.cos(angle)*size[0]*radius*rand(.87,1.04),math.sin(angle)*size[1]*radius*rand(.89,1.03),size[2]*(level+rand(-.032,.032))))
    faces=[tuple(reversed(range(count))),tuple(range(3*count,4*count))]+[(row*count+idx,row*count+(idx+1)%count,(row+1)*count+(idx+1)%count,(row+1)*count+idx) for row in range(3) for idx in range(count)]
    obj=mesh(name,vertices,faces,[stone]); obj.location=center; return obj


for wall in (-1,1):
    for idx in range(8): rock('Footing',(-1.85+idx*.525,wall*1.51,.17),(.34,.31,rand(.33,.43)))
    for idx in range(5): rock('SideFooting',(wall*1.95,-1.02+idx*.51,.16),(.3,.34,rand(.32,.41)))
for idx in range(17):
    east=(idx-8)*.24; top=2.51+1.41*(1-abs(east)/2)
    for front in (-1,1):
        bottom=2.14 if abs(east)<.49 and front==-1 else .25
        beam('GableBoard',(east,front*1.51,bottom),(east+rand(-.009,.009),front*1.51,top),rand(.232,.24),.13,random.choice(planks))
for wall in (-1,1):
    for idx in range(13):
        north=(idx-6)*.244
        spans=((.25,1.16),(1.86,2.49)) if wall==1 and abs(north)<.55 else ((.25,2.49),)
        for low,high in spans: block('SideBoard',(wall*1.98,north,(low+high+.019)/2),(.15,.241,high-low-.019),random.choice(planks))
    for north in (-1.52,1.52): beam('CornerPost',(wall*1.96,north,.23),(wall*1.96,north,2.57),.23,.24,posts)
    beam('EavesPlate',(wall*1.985,-1.7,2.48),(wall*1.985,1.7,2.48),.21,.2,posts)
for east in (-.59,.59): block('DoorJamb',(east,-1.61,1.23),(.17,.20,2.04),posts)
block('DoorLintel',(.01,-1.64,2.18),(1.40,.22,.22),posts); block('DoorInShadow',(.04,-1.28,1.08),(1.09,.09,1.95),shadow)
for idx in range(4): block('DoorPlank',(-.43+idx*.179,-1.38,1.13),(.176,.082,1.92),planks[0])
for level in (.63,1.46): block('DoorStrap',(-.16,-1.439,level),(.66,.052,.075),iron)
for idx in range(3): rock('Step',(.02-idx*.021,-1.74-idx*.365,.16-idx*.047),(.84-idx*.04,.34,.34-idx*.065))
block('SideWindow',(1.915,.0,1.514),(.065,1.18,.846),gold)
for north in (-.55,0,.55): block('WindowMullion',(2.011,north,1.50),(.121,.09,.79),posts)
for level in (1.14,1.86): block('WindowSill',(2.041,.018,level),(.19,1.255,.105),posts)
beam('GableTie',(-1.91,-1.605,2.54),(1.92,-1.602,2.55),.12,.15,posts)
for side in (-1,1): beam('GableBrace',(side*1.57,-1.62,2.54),(side*.42,-1.62,3.42),.105,.12,posts)
canvas=material('WeatheredFlax',(.385,.297,.187),(.20,.159,.106),(4,4,4),.015)
for east in (-.79,.79):
    beam('PorchBracket',(east,-1.56,1.96),(east,-2.27,2.30),.095,.091,posts)
    beam('CanopyRib',(east,-1.56,2.53),(east,-2.30,2.29),.084,.088,posts)
beam('PorchLip',(-.89,-2.26,2.30),(.89,-2.26,2.30),.052,.052,posts,True,True)
verts=[]
for row in range(4):
    for idx in range(15):
        over=idx/14; down=row/3; verts.append(((over-.5)*1.84,-1.54-down*.79,2.58-.243*down+.022*math.cos(over*math.pi*8)-.038*math.sin(over*math.pi)))
awning=mesh('LinenDoorHood',verts,[(row*15+idx,row*15+idx+1,(row+1)*15+idx+1,(row+1)*15+idx) for row in range(3) for idx in range(14)],[canvas]); awning.modifiers.new('DoubleHem','SOLIDIFY').thickness=.013
for north in (-1.73,1.72):
    for side in (-1,1): beam('Bargeboard',(side*2.31,north,2.40),(side*.02,north,4.04),.19,.22,posts)
for side in (-1,1):
    for tier,outer in enumerate((2.34,1.62,.90)):
        inner=max(0,outer-.97)
        for idx in range(13):
            north=-1.93+idx*.319+(tier%2)*.035; tip=outer+rand(-.045,.05)
            beam('SplitShake',(side*inner,north,4.04-inner*.69+tier*.066),(side*tip,north,4.04-tip*.69+tier*.066),rand(.308,.318),.087,random.choice(planks),across=(0,1,0))
beam('Ridge',(0,-2.16,4.19),(0,2.18,4.17),.129,.12,posts,True,True)
for idx in range(7):
    for row in range(2): rock('DryStackFlue',(-1.15+(row-.5)*.35,.95+.025*(idx%2),3.12+idx*.178),(.24,.34,.24))
block('ChimneyCap',(-1.15,.96,4.33),(.85,.74,.15),stone); block('Soot',(-1.135,.962,4.411),(.46,.37,.018),shadow)
beam('LanternPin',(.89,-1.53,2.15),(.89,-1.92,2.18),.078,.068,iron); beam('Hanger',(.89,-1.89,2.16),(.89,-1.89,1.95),.018,.018,iron,True)
block('Lantern',(.89,-1.89,1.80),(.205,.185,.28),gold)
for level in (1.655,1.945): block('LanternRim',(.89,-1.89,level),(.253,.222,.051),iron)
for dx in (-.1,.1):
    for dy in (-.09,.09): block('LanternBar',(.89+dx,-1.89+dy,1.80),(.021,.021,.29),iron)

current=yard
for east in (-1.23,1.23):
    beam('Sleeper',(east,-.99,.105),(east,1.09,.105),.23,.21,posts)
    for north in (-.95,.97): beam('Bollard',(east,north,.025),(east+rand(-.045,.045),north,1.25+rand(-.14,.14)),.11,.12,bark,True,True)
for level,count in enumerate((5,4,3)):
    for idx in range(count):
        north=(idx-(count-1)/2)*.345; height=.376+level*.29
        log=beam('Sawlog',(-1.56+rand(-.18,.15),north,height),(1.58+rand(-.11,.18),north+rand(-.011,.011),height+rand(-.02,.02)),.18,rand(.174,.185),bark,True,True)
        for poly in list(log.data.polygons)[:-2]: poly.use_smooth=True

current=flag
beam('SignalPole',(0,0,0),(.035,0,3.24),.088,.085,posts,True,True); beam('Yardarm',(-.17,0,3.02),(1.12,.015,3.09),.048,.046,posts,True,True)
coords=[]
for row in range(10):
    down=row/9
    for idx in range(8):
        over=idx/7; taper=down**3*.19
        coords.append((.135+.86*over+(1-2*over)*taper+down*.19,.015+down*(.13*math.sin(over*8+down*3)+.095*down),3.045+over*.034-down*(1.23+.19*math.sin(over*math.pi)-.06*math.sin(over*9))))
banner=mesh('MadderGuidon',coords,[(row*8+idx,row*8+idx+1,(row+1)*8+idx+1,(row+1)*8+idx) for row in range(9) for idx in range(7)],[cloth]); banner.modifiers.new('ClothThickness','SOLIDIFY').thickness=.009
for east in (.16,.51,.92): beam('CanvasTie',(east,0,3.0),(east,0,3.115),.018,.018,rope,True)


def imported(name,path):
    before=set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=str(ROOT/path)); result=group(name)
    for obj in set(bpy.data.objects)-before:
        for collection in list(obj.users_collection): collection.objects.unlink(obj)
        result.objects.link(obj)
    return result


fence=imported('KIT_04_Fence','output/wood-fence/tiles-v2/WoodFenceTile_1m.glb'); pine=imported('KIT_05_NaturalPine','output/tree-studies/NaturalPine_LOD1.glb'); worker=imported('KIT_06_Resident','public/models/rts/worker.glb')


def instance(collection,pos,yaw=0,scale=1):
    obj=bpy.data.objects.new(collection.name.removeprefix('KIT_'),None); world.objects.link(obj)
    obj.instance_type='COLLECTION'; obj.instance_collection=collection; obj.location=pos; obj.rotation_euler.z=yaw; obj.scale=(scale,)*3; obj.empty_display_size=.25; return obj


instance(house,(-3.65,.35,0)); instance(yard,(3.10,1.75,0),math.pi/2); instance(flag,(4.65,2.86,0),.25); instance(worker,(.52,-2.82,0),-.32,.98)
for idx in range(13): instance(fence,(-6+idx,4.05,-.015))
for idx in range(4): instance(fence,(-6.50,.55+idx,-.01),math.pi/2)
for east,north,size in ((-8.4,2.25,.83),(-7.9,5.9,.81),(-4.8,8.1,.91),(-2.6,10.5,.76),(.7,10.2,.88),(3.1,10.4,.69),(7.1,9.8,.95),(8,6.4,.64),(-10,-1.9,.78),(-10,-7.8,.53),(12,5.2,.61)):
    instance(pine,(east,north,-.06),rand(0,6),size)

current=world
routes=[[(8,-13),(3,-6),(1.15,-3.1),(-1.3,-2.65),(-3.55,-2.65),(-3.65,-1.25)],[(1.15,-3.1),(3.1,-1.65),(3.25,.54)],[(1.15,-3.1),(-1.5,-5.2),(-5.6,-6.5),(-13,-8.9)]]
segments=[(Vector(start),Vector(end)) for route in routes for start,end in zip(route,route[1:])]


def distance(east,north):
    point=Vector((east,north)); best=100
    for start,end in segments:
        along=end-start; near=start+along*max(0,min(1,(point-start).dot(along)/along.length_squared)); best=min(best,(point-near).length)
    return best


def jitter(east,north,scale): return noise.noise_vector(Vector((east*scale,north*scale,.843)))[0]


earth=material('EarthAndTracks',(.22,.184,.12),(.08,.086,.059),(2,2,2),.08)
tree=earth.node_tree; shader=next(node for node in tree.nodes if node.type=='BSDF_PRINCIPLED'); attr=tree.nodes.new('ShaderNodeVertexColor'); attr.layer_name='GroundTint'
mix=tree.nodes.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=.86
tex=next(node for node in tree.nodes if node.type=='TEX_NOISE'); tree.links.new(tex.outputs['Fac'],mix.inputs[2]); tree.links.new(attr.outputs[0],mix.inputs[1]); tree.links.new(mix.outputs[0],shader.inputs['Base Color'])
grid=141; vertices=[]; colors=[]
for row in range(grid):
    for idx in range(grid):
        east=(idx-grid//2)*.32; north=(row-grid//2)*.32; track=distance(east,north)
        wild=max(0,min(1,(track-1+jitter(east,north,.8)*.52)*2.6)); variation=jitter(east,north,.8)*.24+jitter(east,north,.16)*.27
        clr=Vector((.39,.272,.164)).lerp(Vector((.151,.147,.085)),wild); clr*=1+variation
        if north>5: clr=clr.lerp(Vector((.123,.165,.158)),min(.75,(north-5)*.085))
        vertices.append((east,north,-.031+(jitter(east,north,1.12)*.035+jitter(east,north,.31)*.085)*wild)); colors.append((*clr,1))
faces=[(row*grid+idx,row*grid+idx+1,(row+1)*grid+idx+1,(row+1)*grid+idx) for row in range(grid-1) for idx in range(grid-1)]
terrain=mesh('WornForkedLane',vertices,faces,[earth]); layer=terrain.data.color_attributes.new(name='GroundTint',type='FLOAT_COLOR',domain='POINT')
for elem,color in zip(layer.data,colors): elem.color=color
for poly in terrain.data.polygons: poly.use_smooth=True
vertices=[]; faces=[]; choices=[]
for clump in range(16900):
    east=rand(-15,15); north=rand(-14,15); track=distance(east,north)
    if track<rand(1,1.75) or (-5.9<east<-1.45 and -1.45<north<2.3) or (2<east<4.3 and -.1<north<3.6): continue
    if jitter(east,north,.55)+rand(-.6,.85)<.09: continue
    base=-.06+jitter(east,north,1.12)*.035+jitter(east,north,.31)*.085; tall=rand(.09,.37); color=random.randrange(4)
    for blade in range(9):
        angle=rand(0,math.tau); along=Vector((math.cos(angle),math.sin(angle),0)); wide=Vector((-along.y,along.x,0))*rand(.007,.016); foot=Vector((east+rand(-.07,.07),north+rand(-.07,.07),base)); tip=foot+along*rand(.06,.24)+Vector((.035,0,tall*rand(.65,1.25))); mid=foot.lerp(tip,.55)+Vector((0,0,.034)); first=len(vertices)
        vertices.extend((foot-wide,foot+wide,mid+wide*.55,mid-wide*.55,tip)); faces.extend(((first,first+1,first+2,first+3),(first+3,first+2,first+4))); choices.extend((color,color))
grass=mesh('SedgeBanks',vertices,faces,grass_mats)
for poly,color in zip(grass.data.polygons,choices): poly.material_index=color
for idx in range(172):
    east=rand(-10,10); north=rand(-8,9); scale=rand(.055,.22) if distance(east,north)>1.35 else rand(.025,.068)
    rock('Scree',(east,north,-.006),(scale*1.3,scale,scale*.8))

for east,north,size in ((-7.4,4,.49),(-5.8,6.1,.57),(-.4,7.8,.41),(3.4,6.9,.47),(6.75,4.5,.52)):
    instance(pine,(east,north,-.04),rand(0,6),size)

fogmat=bpy.data.materials.new('M_LowMist'); nodes=fogmat.node_tree.nodes; links=fogmat.node_tree.links
out=next(node for node in nodes if node.type=='OUTPUT_MATERIAL'); links.remove(out.inputs['Surface'].links[0]); volume=nodes.new('ShaderNodeVolumeScatter'); volume.inputs['Color'].default_value=(.67,.77,.91,1); volume.inputs['Anisotropy'].default_value=.18
links.new(volume.outputs[0],out.inputs['Volume']); density=volume.inputs['Density']; density.default_value=.052
block('NorthernFogBank',(0,13.6,3.5),(56,15,11),fogmat)


def light(name,kind,pos,power,color,size,target=(0,0,0)):
    data=bpy.data.lights.new(name,kind); obj=bpy.data.objects.new(name,data); rig.objects.link(obj); obj.location=pos; data.energy=power; data.color=color
    if kind=='SUN': data.angle=size
    elif kind=='AREA': data.shape='DISK'; data.size=size
    else: data.shadow_soft_size=size
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler(); return obj


sun=light('LateLight','SUN',(-8,-6,10),2.7,(1,.855,.70),.19); sky=light('CoolSky','AREA',(4,5,11),1500,(.65,.78,1),10)
light('DoorLamp','POINT',(-2.76,-1.78,1.81),52,(1,.43,.135),.19)
scene.world.color=(.24,.285,.35)
camera_data=bpy.data.cameras.new('Management30'); camera=bpy.data.objects.new('Management30',camera_data); rig.objects.link(camera); scene.camera=camera; camera_data.type='ORTHO'


def frame(target,scale):
    camera_data.ortho_scale=scale; direction=Vector((1,-1,math.sqrt(2/3))).normalized(); camera.location=Vector(target)+direction*35; camera.rotation_euler=(-direction).to_track_quat('-Z','Y').to_euler()


scene.render.engine='CYCLES'; scene.cycles.samples=18; scene.cycles.use_denoising=True; scene.cycles.max_bounces=5; scene.cycles.transparent_max_bounces=8
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type='METAL'; prefs.refresh_devices()
    for device in prefs.devices: device.use=device.type=='METAL'
    if any(device.use for device in prefs.devices): scene.cycles.device='GPU'
except (TypeError,RuntimeError): pass
scene.render.resolution_percentage=100; scene.render.resolution_x=1500; scene.render.resolution_y=940
frame((-.45,.13,1.46),16.8); scene.render.filepath=str(OUT/'camp.png'); scene.render.image_settings.file_format='PNG'
bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'FrontierOutpost.blend'),compress=True)
bpy.ops.render.render(write_still=True)
sun.data.energy=.65; sun.data.color=(.62,.74,1); sky.data.energy=1600; sky.data.color=(.48,.64,1); density.default_value=.115
scene.world.color=(.19,.24,.33); scene.render.filepath=str(OUT/'camp-blue-hour.png'); bpy.ops.render.render(write_still=True)
sun.data.energy=2.7; sun.data.color=(1,.855,.7); sky.data.energy=1500; sky.data.color=(.65,.78,1); scene.world.color=(.24,.285,.35)

sys.path.insert(0,str(ROOT/'scripts'))
from outpost_studio import render_tiles
render_tiles(OUT)
for collection in (house,yard,flag): scene.collection.children.link(collection)
bpy.context.view_layer.update(); reports={}
for collection in (house,yard,flag):
    names=[obj.name for obj in collection.objects]; report=qa_check.run(objects=names,profile='render',eps=.00025,tol=.006,uv=False); reports[collection.name]=report
    print('QA',collection.name,report['errors'],flush=True)
    for mode in ('random','wire'):
        review_shots.run(objects=names,out=str(OUT/'qa'/(collection.name+'-'+mode+'.png')),views=['front','three_quarter','back'],mode=mode,res=220)
(OUT/'qa'/'geometry.json').write_text(json.dumps(reports,indent=2))

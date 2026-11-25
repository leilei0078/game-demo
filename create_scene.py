"""Build a self-contained, stylized medieval diorama in Blender 4.5+.
Run: blender -b --factory-startup --python create_scene.py
"""
import bpy
import math
import random
import sys
from mathutils import Vector
from pathlib import Path

random.seed(23)
OUT = Path(__file__).resolve().parent / 'output'
OUT.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for data in list(bpy.data.materials):
    bpy.data.materials.remove(data)

def mat(name, color, rough=0.85, metal=0, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metal
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

M = {}
for name, col in {
    'grass':(0.24,.34,.15), 'grass_light':(.31,.40,.18), 'grass_dark':(.19,.28,.13),
    'earth':(.24,.19,.12), 'earth_edge':(.34,.27,.16), 'soil':(.43,.36,.23),
    'path':(.56,.45,.28), 'path_light':(.64,.53,.34),
    'plaster':(.79,.69,.47), 'plaster_light':(.89,.79,.56),
    'timber':(.19,.105,.054), 'wood':(.36,.21,.095), 'wood_light':(.52,.34,.16),
    'wood_end':(.64,.45,.23), 'door':(.25,.15,.075),
    'roof':(.53,.19,.085), 'roof_light':(.68,.27,.11), 'roof_dark':(.40,.12,.06),
    'stone':(.40,.44,.39), 'stone_light':(.54,.56,.48), 'stone_dark':(.28,.32,.29),
    'pine':(.12,.26,.17), 'pine_light':(.21,.36,.20), 'pine_dark':(.075,.18,.13),
    'bush':(.37,.44,.16), 'iron':(.12,.16,.16), 'steel':(.48,.60,.60),
    'leather':(.29,.15,.075), 'cloth':(.19,.34,.40), 'skin':(.67,.43,.25),
    'water':(.12,.46,.46), 'water_light':(.37,.70,.62), 'window':(1,.53,.14),
    'flame':(1,.26,.025), 'flame_core':(1,.72,.15), 'coal':(.09,.07,.05),
    'flower':(.88,.68,.22), 'flower_white':(.85,.83,.62),
}.items():
    M[name] = mat(name, col, rough=.3 if name == 'water' else .85,
                  metal=.65 if name in ('iron','steel') else 0,
                  emission=3 if name in ('window','flame') else 5 if name == 'flame_core' else 0)

collection = None
def group(name):
    global collection
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)

def finish(obj, name, material):
    obj.name = name
    if collection:
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        collection.objects.link(obj)
    if material:
        obj.data.materials.append(M[material] if isinstance(material,str) else material)
    return obj

def bevel(obj, amount=.035):
    mod = obj.modifiers.new('Soft worn edges', 'BEVEL')
    mod.width = amount
    mod.segments = 1
    mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True
    return obj

def cube(name, pos, scale, material, edge=.025, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rot: obj.rotation_euler = rot
    finish(obj,name,material)
    if edge: bevel(obj,edge)
    return obj

def mesh(name, verts, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name,data)
    (collection or bpy.context.scene.collection).objects.link(obj)
    if material: data.materials.append(M[material] if isinstance(material,str) else material)
    return obj

def cylinder(name, pos, radius, depth, material, vertices=8, r2=None):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius,
        radius2=radius if r2 is None else r2, depth=depth, location=pos)
    return finish(bpy.context.object,name,material)

def beam(name,a,b,width,material,depth=None):
    a,b = Vector(a),Vector(b)
    obj = cube(name,(a+b)/2,(width,depth or width,(b-a).length),material,.015)
    obj.rotation_euler = (b-a).to_track_quat('Z','Y').to_euler()
    return obj

def log(name,a,b,r,material='wood',vertices=9):
    a,b = Vector(a),Vector(b)
    obj = cylinder(name,(a+b)/2,r,(b-a).length,material,vertices)
    obj.rotation_euler = (b-a).to_track_quat('Z','Y').to_euler()
    for p in (a,b):
        end = cylinder(name+' end grain',p,r*.86,.014,'wood_end',vertices)
        end.rotation_euler = obj.rotation_euler
    return obj

def ico(name,pos,scale,material,sub=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub,radius=1,location=pos)
    obj=bpy.context.object
    obj.scale=scale
    obj.rotation_euler=(random.random()*.3,random.random()*.4,random.random()*6.28)
    return finish(obj,name,material)

def ribbon(name, points, widths, material, z=.31):
    # Catmull-Rom interpolation gives the tracks and creek flowing bends.
    source=points
    source_widths=widths if isinstance(widths,list) else [widths]*len(points)
    points=[]; widths=[]
    for i in range(len(source)-1):
        p0=Vector(source[max(0,i-1)]); p1=Vector(source[i])
        p2=Vector(source[i+1]); p3=Vector(source[min(len(source)-1,i+2)])
        for j in range(7):
            t=j/7
            p=.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)
            points.append(tuple(p))
            widths.append(source_widths[i]*(1-t)+source_widths[i+1]*t)
    points.append(source[-1]); widths.append(source_widths[-1])
    verts=[]
    for i,p in enumerate(points):
        prev=Vector(points[max(0,i-1)]); nxt=Vector(points[min(len(points)-1,i+1)])
        delta=(nxt-prev).normalized(); side=Vector((-delta.y,delta.x))
        w=widths[i] if isinstance(widths,list) else widths
        verts.extend([(p[0]+side.x*w/2,p[1]+side.y*w/2,z),(p[0]-side.x*w/2,p[1]-side.y*w/2,z)])
    return mesh(name,verts,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(len(points)-1)],material)

# A sculpted polygonal island, with a quiet studio floor beneath it.
group('01 • Ground & creek')
N=54
outline=[]
for i in range(N):
    a=2*math.pi*i/N
    r=1+random.uniform(-.025,.025)
    outline.append((6.85*math.cos(a)*r,5.35*math.sin(a)*r))
verts=[(0,0,.27)]+[(x,y,.27) for x,y in outline]
terrain=mesh('Triangulated mossy clearing',verts,[(0,i+1,(i+1)%N+1) for i in range(N)],'grass')
terrain.data.materials.append(M['grass_light']); terrain.data.materials.append(M['grass_dark'])
for p in terrain.data.polygons:
    p.material_index=random.choices([0,1,2],[.77,.15,.08])[0]
verts=[(x,y,.25) for x,y in outline]+[(x*.98,y*.98,-.57+random.uniform(-.1,.08)) for x,y in outline]
side=mesh('Exposed earthen diorama edge',verts,[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)],'earth')
side.data.materials.append(M['earth_edge'])
for p in side.data.polygons: p.material_index=random.randrange(2)
mesh('Island underside',[(x*.98,y*.98,-.58) for x,y in outline],[tuple(range(N-1,-1,-1))],'earth')

path=[(-.25,-5.1),(-.6,-4.5),(-1.4,-3.7),(-1.9,-2.9),(-2.6,-2.0),(-2.6,-.15)]
ribbon('Worn ochre approach',path,[1.45,1.30,1.16,1.0,.94,.96],'path',.274)
ribbon('Track toward bridge',[(-1.75,-3),(0,-2.5),(1.3,-1.9),(2.7,-1.3),(4.65,-.9)],.85,'path',.276)
creek=[(3.0,4.7),(3.4,3.7),(3.9,2.7),(3.7,1.7),(4.15,.5),(3.8,-.8),(4.1,-2.0),(3.7,-3.2),(3.6,-4.25)]
ribbon('Pale creek banks',creek,[1.5,1.5,1.45,1.55,1.5,1.55,1.65,1.5,1.45],'soil',.278)
ribbon('Jade creek',creek,[1.05,1.02,.96,1.04,1.02,1.0,1.14,1.0,1.02],'water',.282)
for x,y in [(3.45,3.5),(3.85,2.25),(4.08,.7),(3.86,-2.2),(3.65,-3.7)]:
    for j in range(3):
        cube('Small glint on water',(x+random.uniform(-.25,.25),y+j*.14,.288),(random.uniform(.16,.42),.018,.006),'water_light',.005)

group('02 • Wayfarer cottage')
cx,cy=-2.65,1.55
front=cy-1.53
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent/'scripts'))
from masonry import build_foundation
build_foundation(collection,cx,cy)
# Keep the pre-existing random sequence for the rest of this scene.
for _ in range(26): random.choice(['stone','stone_light'])
cube('Warm limewashed walls',(cx,cy,2.01),(3.42,3.05,2.48),'plaster',.035)
eave=3.25; ridge=4.80
for y in [front,cy+1.525]:
    mesh('Limewash gable',[(cx-1.71,y,eave),(cx+1.71,y,eave),(cx,y,ridge)],[(0,1,2)],'plaster_light')
for x in [cx-1.72,cx+1.72]:
    for y in [front-.025,cy+1.54]:
        cube('Heavy corner oak post',(x,y,2.02),(.20,.20,2.69),'timber')
for z in [.88,3.18]:
    cube('Front horizontal oak',(cx,front-.075,z),(3.65,.18,.18),'timber')
    cube('Side horizontal oak',(cx+1.76,cy,z),(.18,3.1,.18),'timber')
for x in [cx-.65,cx+.65]:
    cube('Facade upright',(x,front-.08,2.0),(.12,.13,2.3),'timber')
for y in [cy-.55,cy+.55]:
    cube('Side upright',(cx+1.76,y,2.01),(.14,.14,2.4),'timber')
beam('Side diagonal timber',(cx+1.80,cy-.48,1.02),(cx+1.80,cy+.45,2.96),.12,'timber')
for s in [-1,1]:
    beam('Gable oak verge',(cx+s*1.85,front-.12,3.25),(cx,front-.12,4.91),.19,'timber')
    beam('Gable brace',(cx+s*1.25,front-.13,3.25),(cx,front-.13,4.40),.115,'timber')
cube('Gable king post',(cx,front-.14,3.96),(.14,.15,1.65),'timber')

# Individually modelled, overlapping terracotta shingles.
roof_w=2.12; roof_rise=1.72; roof_top=4.95; y0=front-.42; roof_len=3.92
for sign in [-1,1]:
    mesh('Thick roof underlay',[(cx,y0,roof_top-.05),(cx+sign*roof_w,y0,roof_top-roof_rise-.05),
         (cx+sign*roof_w,y0+roof_len,roof_top-roof_rise-.05),(cx,y0+roof_len,roof_top-.05)],[(0,1,2,3)],'roof_dark')
    for row in range(7):
        u0=row/7; u1=min(1,(row+1.22)/7)
        for col in range(10):
            ya=y0+col*roof_len/10+random.uniform(-.025,.025)
            yb=ya+roof_len/10-.012
            h=random.uniform(.008,.032)+(7-row)*.008
            v=[(cx+sign*roof_w*u0,ya,roof_top-roof_rise*u0+h),
               (cx+sign*roof_w*u1,ya,roof_top-roof_rise*u1+h),
               (cx+sign*roof_w*u1,yb,roof_top-roof_rise*u1+h),
               (cx+sign*roof_w*u0,yb,roof_top-roof_rise*u0+h)]
            tile=mesh('Overlapping clay shingle',v,[(0,1,2,3)],random.choices(['roof','roof_light','roof_dark'],[6,3,1])[0])
            solid=tile.modifiers.new('Tile thickness','SOLIDIFY'); solid.thickness=.055
            bevel(tile,.015)
    beam('Oak eave fascia',(cx+sign*roof_w,y0-.04,roof_top-roof_rise),
         (cx+sign*roof_w,y0+roof_len+.02,roof_top-roof_rise),.16,'timber')
for i in range(12):
    obj=cylinder('Terracotta ridge cap',(cx,y0+.17+i*.32,4.99),.115,.34,'roof_light',8)
    obj.rotation_euler[0]=math.pi/2

# A chimney with uneven masonry and a dark mouth.
chimx,chimy=cx+.95,cy+.78
cube('Stone chimney',(chimx,chimy,4.72),(.58,.65,1.48),'stone',.04)
for z in [4.2,4.54,4.88,5.22]:
    cube('Chimney mortar course',(chimx,chimy-.334,z),(.59,.015,.026),'stone_dark',0)
    cube('Chimney accent stone',(chimx+random.uniform(-.16,.12),chimy-.35,z+.13),(.21,.055,.15),'stone_light',.012)
cube('Chimney overhanging crown',(chimx,chimy,5.48),(.78,.83,.17),'stone_light',.04)
cube('Soot opening',(chimx,chimy,5.576),(.46,.50,.025),'coal',.015)

# Door, ironwork, and warmly lit cottage windows.
cube('Recess behind plank door',(cx,front-.105,1.64),(1.01,.11,1.66),'timber',.04)
for i in range(5):
    cube('Door vertical plank',(cx-.37+i*.185,front-.178,1.62),(.174,.09,1.48),'door',.015)
for x in [cx-.52,cx+.52]:
    cube('Door jamb',(x,front-.20,1.67),(.13,.18,1.85),'wood_light')
cube('Door lintel',(cx,front-.20,2.56),(1.17,.19,.17),'wood_light')
for z in [1.20,2.04]:
    cube('Forged door strap',(cx-.13,front-.236,z),(.66,.035,.065),'iron',.01)
for z in [.33,.47,.61]:
    cube('Worn doorstep',(cx,front-.46-(.61-z)*1.3,z),(1.28,.55,.16),'stone_light',.012)
bpy.ops.mesh.primitive_torus_add(major_radius=.066,minor_radius=.013,major_segments=12,minor_segments=5,location=(cx+.27,front-.25,1.65),rotation=(math.pi/2,0,0))
finish(bpy.context.object,'Iron ring door pull','iron')

def front_window(x,z,w=.62):
    y=front-.12
    cube('Dark window recess',(x,y,z),(w+.17,.12,.86),'timber')
    cube('Amber panes',(x,y-.075,z),(w,.04,.67),'window',.01)
    cube('Window mullion',(x,y-.11,z),(.055,.08,.71),'wood_light',.01)
    cube('Window crossbar',(x,y-.11,z),(w,.08,.055),'wood_light',.01)
    cube('Window sill',(x,y-.16,z-.42),(w+.28,.31,.10),'wood_light')
front_window(cx-1.18,2.14,.48)
front_window(cx+1.18,2.14,.48)
cube('Side window frame',(cx+1.80,cy+.35,2.12),(.13,.95,1.05),'timber')
cube('Side amber glass',(cx+1.88,cy+.35,2.12),(.025,.76,.86),'window',.008)
for y in [cy+.09,cy+.60]:
    cube('Side window mullion',(cx+1.91,y,2.12),(.07,.055,.90),'wood_light',.01)
cube('Side window crossbar',(cx+1.91,cy+.35,2.12),(.07,.82,.06),'wood_light',.01)
cube('Side window sill',(cx+1.91,cy+.35,1.56),(.32,1.10,.12),'wood_light')

# A small hanging tavern sign, painted with a gold compass star.
beam('Sign bracket post',(cx+1.65,front-.1,2.77),(cx+1.65,front-.97,2.77),.10,'iron')
for x in [cx+1.40,cx+1.91]:
    beam('Sign iron hanger',(x,front-.90,2.77),(x,front-.90,2.53),.024,'iron')
cube('Hanging wayfarer sign',(cx+1.65,front-.9,2.20),(.78,.12,.62),'wood',.055)
for z in [1.93,2.47]:
    cube('Sign gilded rim',(cx+1.65,front-.972,z),(.70,.023,.028),'wood_end',.008)
star=[]
for i in range(16):
    a=2*math.pi*i/16
    r=.22 if i%4==0 else .12 if i%2==0 else .05
    star.append((cx+1.65+math.sin(a)*r,front-.975,2.20+math.cos(a)*r))
mesh('Eight point wayfarer emblem',star,[tuple(range(16))],'flower')

group('03 • Pines, stones & meadow')
def pine(x,y,h):
    z=.29
    log('Angular pine trunk',(x,y,z),(x+.05,y,z+h*.72),h*.055,'wood',7)
    for i in range(4):
        size=h*(.30-i*.047)
        base=z+h*(.22+i*.18)
        obj=cylinder('Layered faceted pine crown',(x,y,base+h*.20),size,h*.47,
            ['pine_dark','pine','pine_light','pine'][i],7,r2=.025)
        obj.rotation_euler[2]=i*.46+random.uniform(-.2,.2)

for args in [(-5.15,2.75,4.3),(-4.5,3.9,3.8),(-.4,4.1,4.5),(1.3,3.8,3.6),
             (5.10,1.70,3.5),(5.35,2.70,3.0),(-5.75,-.7,2.6),(-5.1,-2.0,3.35)]:
    pine(*args)

for x,y,s in [(-5.3,1.3,.7),(-4.8,3.4,.65),(.8,3.0,.7),(2.3,3.4,.75),
              (4.7,-3.0,.48),(4.7,-2.5,.7),(-4.8,-3,.7),(-3.8,-3.5,.4),
              (2.3,-3.7,.63),(2.0,-4.1,.34),(5.4,.0,.65)]:
    ico('Mossy granite boulder',(x,y,.25+s*.38),(s,s*.72,s*.67),random.choice(['stone','stone_light','stone_dark']))
    if s>.5:
        ico('Flat moss cap',(x-.04,y,.28+s*.75),(s*.65,s*.50,.10),'grass_dark')
for x,y in [(-4.7,-1.7),(-4.4,-2.8),(-.8,3.5),(1.7,3.3),(5.2,.25),(2.3,-4.1),(-5.1,1.8)]:
    for j in range(3):
        ico('Low woodland shrub',(x+random.uniform(-.25,.25),y+random.uniform(-.2,.2),.50),(.42,.32,.30),'bush')

# Small patches keep the meadow readable from the isometric camera.
for i in range(240):
    x=random.uniform(-6.4,6.4); y=random.uniform(-4.8,4.8)
    if (x/6.4)**2+(y/4.9)**2>1: continue
    if -4.6<x<-.7 and -.45<y<3.35: continue
    if 2.7<x<4.8: continue
    if abs(x+.8)<.9 and y<-3: continue
    if -3.1<x<-1.8 and -3.4<y<0: continue
    if -.8<x<2.4 and -2.8<y<1.0: continue
    h=random.uniform(.09,.22)
    for j in range(3):
        a=j*2.09+random.random()
        dx,dy=math.cos(a),math.sin(a)
        mesh('Meadow grass blade',[(x-dy*.025,y+dx*.025,.29),(x+dy*.025,y-dx*.025,.29),
             (x+dx*h*.35,y+dy*h*.35,.29+h)],[(0,1,2)],random.choice(['grass_light','bush','grass_dark']))
    if i%7==0:
        cylinder('Wildflower center',(x,y,.31+h),.042,.04,random.choice(['flower','flower_white']),5)

# Rounded stones and reeds trace the stream edge.
for i in range(28):
    y=random.uniform(-3.9,4.2); x=3.65+random.choice([-1,1])*random.uniform(.62,.82)
    ico('Creek pebble',(x,y,.36),(random.uniform(.09,.22),random.uniform(.08,.18),random.uniform(.09,.18)),'stone_light')
for x,y in [(3.1,2.8),(4.65,1.3),(3.2,-2.4),(4.3,-3.1)]:
    for j in range(5):
        xx=x+random.uniform(-.13,.13); yy=y+random.uniform(-.13,.13); h=random.uniform(.35,.65)
        beam('Reed stalk',(xx,yy,.3),(xx+.06,yy,.3+h),.017,'bush')
        cylinder('Reed seed head',(xx+.06,yy,.31+h),.032,.14,'wood',6)

group('04 • Bridge, fence & supplies')
# The bridge spans the creek along X.
for y in [-1.54,-.65]:
    beam('Bridge supporting stringer',(2.9,y,.42),(4.90,y,.42),.17,'timber')
for i in range(11):
    x=2.91+i*.19
    cube('Uneven footbridge plank',(x,-1.095,.53+random.uniform(-.018,.018)),(.177,1.11,.13),
         random.choice(['wood','wood_light','wood_end']),.025,rot=(0,0,random.uniform(-.018,.018)))
for x in [2.98,4.79]:
    for y in [-1.56,-.64]:
        cube('Footbridge rail post',(x,y,.95),(.13,.13,1.13),'wood_light')
        cylinder('Post cap',(x,y,1.54),.105,.12,'wood_end',4,r2=.02)
for y in [-1.56,-.64]:
    beam('Bridge handrail',(2.94,y,1.39),(4.84,y,1.39),.10,'wood_light')

def fence(a,b,segments):
    a,b=Vector(a),Vector(b)
    for i in range(segments+1):
        p=a+(b-a)*i/segments
        cube('Split oak fence post',(p.x,p.y,.86),(.17,.18,1.20),'wood',.025)
        cylinder('Pointed fence cap',(p.x,p.y,1.50),.13,.18,'wood_light',4,r2=0)
    for z in [.73,1.15]:
        beam('Fence crossrail',(a.x,a.y,z),(b.x,b.y,z),.12,'wood_light')
    for i in range(segments):
        p=a+(b-a)*i/segments; q=a+(b-a)*(i+1)/segments
        beam('Fence diagonal brace',(p.x,p.y,.67),(q.x,q.y,1.21),.07,'wood')
fence((-.3,2.9,0),(2.4,2.9,0),3)
fence((-4.8,-3.4,0),(-3.15,-4.05,0),2)

def barrel(x,y,z=.28,s=1):
    # Staves share a bulging silhouette but retain visible seams.
    levels=[(0,.29),(.12,.33),(.42,.37),(.73,.33),(.84,.29)]
    for i in range(12):
        a=2*math.pi*i/12+.018; b=2*math.pi*(i+1)/12-.018
        verts=[]
        for zz,r in levels:
            verts.extend([(x+math.cos(a)*r*s,y+math.sin(a)*r*s,z+zz*s),
                          (x+math.cos(b)*r*s,y+math.sin(b)*r*s,z+zz*s)])
        mesh('Curved oak barrel stave',verts,[(j*2,j*2+1,j*2+3,j*2+2) for j in range(4)],
             'wood_light' if i%3 else 'wood')
    for zz,r in [(.15,.34),(.68,.345)]:
        bpy.ops.mesh.primitive_torus_add(major_radius=r*s,minor_radius=.028*s,
             major_segments=12,minor_segments=4,location=(x,y,z+zz*s))
        finish(bpy.context.object,'Barrel iron hoop','iron')
    cylinder('Barrel lid',(x,y,z+.835*s),.283*s,.035*s,'wood_end',12)
    for dx in [-.13,0,.13]:
        cube('Barrel lid seam',(x+dx*s,y,z+.855*s),(.009,.43*s,.006),'wood',0)
barrel(-.54,.65)
barrel(-.38,1.41,s=.83)

def crate(x,y,z,s=.68):
    cube('Supply crate body',(x,y,z+s/2),(s,s,s),'wood',.025)
    for zz in [z+.07,z+s-.07]:
        for yy in [y-s/2-.015,y+s/2+.015]:
            cube('Crate cross slat',(x,yy,zz),(s+.04,.07,.10),'wood_light')
    for xx in [x-s/2+.06,x+s/2-.06]:
        cube('Crate upright',(xx,y-s/2-.02,z+s/2),(.10,.07,s),'wood_light')
    beam('Crate front diagonal',(x-s*.37,y-s/2-.07,z+.12),(x+s*.37,y-s/2-.07,z+s-.12),.09,'wood_end')
crate(-.15,2.10,.28,.68)
crate(-.12,2.08,.96,.46)

# Chopped firewood piled neatly along the cottage.
for row in range(3):
    for col in range(4-row):
        x=cx+1.98+row*.04; y=cy+1.0+col*.21+row*.09; z=.43+row*.18
        log('Stacked split firewood',(x-.28,y,z),(x+.34,y,z),.11)

group('05 • Campfire & adventurer')
fx,fy=1.0,-.74
cylinder('Ash and scorched earth',(fx,fy,.327),.69,.028,'soil',14)
cylinder('Black charcoal bed',(fx,fy,.349),.46,.035,'coal',12)
for i in range(10):
    a=2*math.pi*i/10
    rock=ico('Ring of hearth stones',(fx+.57*math.cos(a),fy+.57*math.sin(a),.44),(.20,.15,.14),
        random.choice(['stone','stone_light']))
    rock.rotation_euler[2]=a
for a in [.3,2.3,4.3]:
    log('Charred campfire log',(fx+.41*math.cos(a),fy+.41*math.sin(a),.42),
        (fx-.33*math.cos(a),fy-.33*math.sin(a),.51),.10,'wood')
for dx,dy,h,r in [(0,0,.78,.19),(-.19,.03,.49,.13),(.14,.11,.62,.14),(.06,-.14,.40,.13)]:
    obj=cylinder('Faceted amber flame',(fx+dx,fy+dy,.51+h/2),r,h,'flame',5,r2=0)
    obj.rotation_euler=(.10,-.19,.4)
    cylinder('Golden flame core',(fx+dx,fy+dy,.50+h*.24),r*.57,h*.48,'flame_core',5,r2=0)
for i in range(7):
    ico('Rising ember',(fx+random.uniform(-.22,.22),fy+random.uniform(-.17,.17),random.uniform(1.1,1.75)),(.018,.018,.035),'flame_core')
for x,y in [(.0,-1.7),(1.84,-.05)]:
    for dx in [-.36,.36]:
        log('Stool leg',(x+dx,y,.29),(x+dx,y,.59),.08)
    cube('Camp bench seat',(x,y,.65),(1.08,.40,.16),'wood_light',.04)

# An original little travelling knight establishes the scale.
px,py=.55,-2.65
for dx in [-.145,.145]:
    cube('Adventurer boot',(px+dx,py-.04,.45),(.24,.38,.30),'leather',.055)
    beam('Adventurer greave',(px+dx,py,.59),(px+dx,py,.91),.19,'steel')
cylinder('Tunic skirt',(px,py,1.01),.29,.34,'cloth',7,r2=.24)
cube('Traveller breastplate',(px,py,1.32),(.50,.31,.48),'steel',.07)
cube('Leather belt',(px,py,1.10),(.54,.35,.095),'leather',.015)
cube('Belt buckle',(px,py-.195,1.11),(.105,.03,.085),'wood_end',.01)
ico('Helmed head',(px,py,1.80),(.25,.23,.28),'steel',2)
cube('Dark helmet visor',(px,py-.218,1.80),(.30,.034,.055),'iron',.012)
beam('Helmet nose guard',(px,py-.25,1.91),(px,py-.25,1.67),.045,'steel')
cylinder('Helmet crown rivet',(px,py,2.09),.045,.09,'steel',6,r2=.01)
for dx in [-.34,.34]:
    ico('Chunky shoulder armor',(px+dx,py,1.48),(.21,.21,.18),'steel')
    beam('Traveller sleeve',(px+dx,py,1.42),(px+dx*1.28,py-.04,1.15),.15,'cloth')
    ico('Leather glove',(px+dx*1.28,py-.04,1.09),(.10,.10,.12),'leather')
# Cape with an asymmetric, visible silhouette behind the right shoulder.
cape=mesh('Traveller teal cape',[(px-.27,py+.15,1.56),(px+.27,py+.15,1.56),
    (px+.42,py+.37,.74),(px+.12,py+.46,.80),(px-.33,py+.37,.71)],[(0,1,2),(0,2,3),(0,3,4)],'cloth')
solid=cape.modifiers.new('Wool thickness','SOLIDIFY'); solid.thickness=.045
# Round shield on the left arm, and a sword planted beside the right hand.
shield=cylinder('Traveller round shield',(px-.49,py-.17,1.18),.34,.09,'wood',10)
shield.rotation_euler[0]=math.pi/2
bpy.ops.mesh.primitive_torus_add(major_radius=.319,minor_radius=.035,major_segments=10,minor_segments=4,
    location=(px-.49,py-.225,1.18),rotation=(math.pi/2,0,0))
finish(bpy.context.object,'Shield iron rim','iron')
boss=ico('Shield central steel boss',(px-.49,py-.255,1.18),(.12,.065,.12),'steel',2)
sx,sy=px+.50,py-.12
cube('Sword steel blade',(sx,sy,.72),(.105,.045,.73),'steel',.012)
cube('Sword crossguard',(sx,sy,1.09),(.32,.09,.07),'iron',.025)
cube('Sword grip',(sx,sy,1.22),(.075,.075,.22),'leather',.012)
ico('Sword pommel',(sx,sy,1.36),(.072,.065,.065),'steel')

# A quiet bedroll and bag beside the bench.
roll=cylinder('Rolled travel blanket',(2.0,-2.20,.48),.21,.75,'cloth',10)
roll.rotation_euler[1]=math.pi/2
for x in [1.76,2.24]:
    bpy.ops.mesh.primitive_torus_add(major_radius=.215,minor_radius=.024,major_segments=10,minor_segments=4,
        location=(x,-2.20,.48),rotation=(0,math.pi/2,0))
    finish(bpy.context.object,'Bedroll leather strap','leather')
ico('Traveller satchel',(2.27,-1.90,.49),(.25,.18,.24),'leather',2)

group('06 • Lighting & cameras')
floor_mat=mat('Backdrop • deep desaturated blue',(.075,.115,.125))
cube('Matte studio ground',(0,0,-.86),(200,200,.20),floor_mat,0)
world=bpy.context.scene.world
world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.42,.48,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.32

def area(name,pos,power,color,size,target=(0,0,0)):
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.color=color; data.shape='DISK'; data.size=size
    obj=bpy.data.objects.new(name,data); collection.objects.link(obj); obj.location=pos
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj
area('Warm late afternoon key',(-6,-7,13),1800,(1,.79,.53),7)
area('Soft sky fill',(8,-1,9),1100,(.61,.79,1),8)
area('Rim over the pines',(1,7,11),2100,(1,.88,.62),6,(0,1,2))
data=bpy.data.lights.new('Campfire warm pool','POINT'); data.energy=45; data.color=(1,.26,.045); data.shadow_soft_size=.8
obj=bpy.data.objects.new('Campfire warm pool',data); collection.objects.link(obj); obj.location=(fx,fy,.98)

def camera(name,pos,target,ortho):
    data=bpy.data.cameras.new(name); obj=bpy.data.objects.new(name,data); collection.objects.link(obj)
    obj.location=pos; obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO'; data.ortho_scale=ortho; data.lens=45
    return obj
cam=camera('01 • Hero isometric',(13,-18,14),(0,0,1.15),16.4)
camera('02 • Camp detail',(8,-11,8),(-.35,-.2,1.2),9.5)
scene=bpy.context.scene
scene.camera=cam
scene.render.engine='CYCLES'
scene.cycles.samples=48
scene.cycles.use_denoising=True
scene.cycles.max_bounces=6
scene.render.resolution_x=1600; scene.render.resolution_y=1440
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGB'
scene.render.image_settings.color_depth='8'
scene.render.filepath=str(OUT/'wayfarers_rest.png')
scene.view_settings.view_transform='AgX'
scene.view_settings.look='AgX - Medium High Contrast'
scene.render.film_transparent=False
# A restrained glow catches the lit windows and the little campfire.
if bpy.app.version >= (5,0,0):
    tree=bpy.data.node_groups.new('Wayfarer • final grade','CompositorNodeTree')
    scene.compositing_node_group=tree
    tree.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
else:
    scene.use_nodes=True
    tree=scene.node_tree
tree.nodes.clear()
rl=tree.nodes.new('CompositorNodeRLayers')
glare=tree.nodes.new('CompositorNodeGlare')
if hasattr(glare,'glare_type'):
    glare.glare_type='FOG_GLOW'; glare.quality='HIGH'; glare.threshold=2.0
else:
    glare.inputs['Type'].default_value='Fog Glow'
    glare.inputs['Quality'].default_value='High'
    glare.inputs['Threshold'].default_value=2.0
output=tree.nodes.new('NodeGroupOutput' if bpy.app.version >= (5,0,0) else 'CompositorNodeComposite')
tree.links.new(rl.outputs['Image'],glare.inputs['Image']); tree.links.new(glare.outputs['Image'],output.inputs['Image'])

# Open the .blend in the same composed view, with clean material colors.
for screen in bpy.data.screens:
    for area_ in screen.areas:
        if area_.type=='VIEW_3D':
            area_.spaces.active.region_3d.view_perspective='CAMERA'
            area_.spaces.active.shading.type='MATERIAL'
            area_.spaces.active.overlay.show_overlays=False
bpy.ops.object.select_all(action='DESELECT')
scene['Art direction']='Original stylized medieval woodland diorama, inspired by the readable forms and earthy palette of Albion Online.'
scene['Rebuild']='blender -b --factory-startup --python create_scene.py'
scene['Contents']='Everything is modeled with native meshes. No external textures, add-ons or linked assets.'
sys.path.insert(0,str(Path(__file__).resolve().parent/'scripts'))
from clearance import refine_layout
refine_layout()
from sign_support import refine_sign
refine_sign()
from windows import refine_windows
refine_windows()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'wayfarers_rest.blend'))
bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['02 • Camp detail']
scene.render.resolution_x=1400; scene.render.resolution_y=1200
scene.cycles.samples=40
scene.render.filepath=str(OUT/'camp_detail.png')
bpy.ops.render.render(write_still=True)
print('SCENE_COMPLETE',len(bpy.data.objects),'objects',OUT)

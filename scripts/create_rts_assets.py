"""Original frontier RTS kit, authored in Blender meters; no live-session mutation."""
import bpy, bmesh, math, json, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/rts'; OUT.mkdir(exist_ok=True,parents=True)
DEST=ROOT/'public/models/rts'; DEST.mkdir(exist_ok=True,parents=True)
sys.path.insert(0,'/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check,review_shots
for ob in list(bpy.data.objects): bpy.data.objects.remove(ob,do_unlink=True)
bpy.context.scene.unit_settings.system='METRIC'
bpy.context.scene.unit_settings.scale_length=1
assets={}; current=None
def mat(name,c,metal=0,rough=.8):
 m=bpy.data.materials.new(name);m.diffuse_color=(*c,1)
 n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 n.inputs['Base Color'].default_value=(*c,1);n.inputs['Roughness'].default_value=rough;n.inputs['Metallic'].default_value=metal
 return m
stone=mat('M_Limestone',(.32,.34,.30));plaster=mat('M_Warm_plaster',(.60,.55,.40))
wood=mat('M_Oak_beam',(.17,.095,.044));end=mat('M_Oak_end',(.43,.27,.11))
roof=mat('M_Copper_slate',(.05,.16,.19));tile=mat('M_Slate_tile',(.08,.22,.26));ridge=mat('M_Ridge',(.13,.27,.28))
iron=mat('M_Iron',(.20,.25,.27),1,.5);steel=mat('M_Steel',(.49,.57,.58),1,.4)
gold=mat('M_Brass',(.63,.40,.14),1,.45);blue=mat('M_Azure_banner',(.025,.17,.29))
dark=mat('M_Shadow',(.027,.033,.03));skin=mat('M_Skin',(.52,.32,.19))
leather=mat('M_Leather',(.13,.065,.028));linen=mat('M_Linen',(.44,.42,.27))
red=mat('M_Crimson_banner',(.31,.035,.026));wheat=mat('M_Wheat',(.50,.39,.13))
assets={}
def start(name):
 global current
 current=bpy.data.collections.new(name);bpy.context.scene.collection.children.link(current);assets[name]=[]
def finish(o,name,m):
 o.name=name
 for c in list(o.users_collection):c.objects.unlink(o)
 current.objects.link(o);o.data.materials.clear();o.data.materials.append(m)
 bpy.context.view_layer.objects.active=o;o.select_set(True)
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 assets[current.name].append(o);o.select_set(False);return o
def box(n,p,s,m,bev=.025,rot=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.dimensions=s
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bev:
  md=o.modifiers.new('Worn bevel','BEVEL');md.width=min(bev,min(s)*.28);md.segments=1;bpy.ops.object.modifier_apply(modifier=md.name)
 if rot:o.rotation_euler=rot
 return finish(o,n,m)
def ico(n,p,s,m,sub=1):
 bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub,radius=1,location=p);o=bpy.context.object;o.scale=s;return finish(o,n,m)
def cylinder(n,p,r,h,m,top=None,verts=12):
 bpy.ops.mesh.primitive_cone_add(vertices=verts,radius1=r,radius2=r if top is None else top,depth=h,location=p)
 return finish(bpy.context.object,n,m)
def beam(n,a,b,width,m):
 a,b=Vector(a),Vector(b);o=box(n,(a+b)*.5,(width,width,(b-a).length),m,.01)
 o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
 bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.transform_apply(location=False,rotation=True,scale=True);o.select_set(False)
 return o
def roofshape(n,cx,cy,z,w,d,h,m):
 vs=[(cx-w/2,cy-d/2,z),(cx+w/2,cy-d/2,z),(cx,cy-d/2,z+h),(cx-w/2,cy+d/2,z),(cx+w/2,cy+d/2,z),(cx,cy+d/2,z+h)]
 faces=[(0,2,1),(3,4,5),(0,1,4,3),(0,3,5,2),(1,2,5,4)]
 me=bpy.data.meshes.new(n);me.from_pydata(vs,[],faces);me.update();o=bpy.data.objects.new(n,me);current.objects.link(o)
 return finish(o,n,m)
def house(w=4,d=3,h=2.5,cx=0,cy=0,tiles=True):
 box('Foundation',(cx,cy,.19),(w+.5,d+.5,.38),stone,.05)
 box('Plaster walls',(cx,cy, .35+h*.5),(w,d,h),plaster,.03)
 for x in [-w/2+.08,w/2-.08]:
  for y in [-d/2+.07,d/2-.07]:box('Corner timber',(cx+x,cy+y,.35+h*.5),(.21,.21,h+.1),wood,.018)
 for z in [.58,h+.25]:box('Front crossbeam',(cx,cy-d/2-.035,z),(w,.13,.20),wood,.018)
 roofshape('Solid gabled roof',cx,cy,h+.31,w+.75,d+.8,1.55,roof)
 if tiles:
  for side in [-1,1]:
   for row in range(4):
    x=(row+.55)/4*(w/2+.30)*side
    z=h+.31+1.55*(1-abs(x)/(w/2+.375))+.035
    angle=side*math.atan(1.55/(w/2+.375))
    for j in range(6):
     y=cy-(d+.64)/2+(j+.5)*(d+.64)/6
     box('Overlapping slate', (cx+x,y,z),((w/2+.35)/4*1.18,(d+.64)/6*.96,.09),tile if (j+row)%3 else ridge,.009,(0,angle,0))
 box('Ridge cap',(cx,cy,h+1.91),(.18,d+.9,.17),ridge,.025)
 # Front arch-like entry and amber inset panels.
 box('Door',(cx,cy-d/2-.08,1.10),(.94,.12,1.48),wood,.06)
 for x in [-.57,.57]:box('Door post',(cx+x,cy-d/2-.13,1.10),(.18,.23,1.72),stone,.025)
 box('Lintel',(cx,cy-d/2-.13,1.94),(1.3,.23,.22),stone,.025)
 for x in [-w*.33,w*.33]:
  box('Window dark recess',(cx+x,cy-d/2-.07,1.58),(.48,.12,.67),dark,.02)
  box('Window shutter',(cx+x,cy-d/2-.15,1.58),(.10,.11,.68),end,.012)
def banner(x,y,z,m=blue):
 cylinder('Banner pole',(x,y,(z+.17)/2),.045,z-.17,iron,verts=8)
 box('Hanging pennant',(x+.35,y,z-.49),(.70,.07,.9),m,.015)
 box('Banner brass device',(x+.35,y-.05,z-.45),(.11,.035,.46),gold,.01)

start('keep');house(5.6,4.3,3.1)
box('Keep stone tower',(-2.1,1.22,2.6),(1.5,1.7,4.8),stone,.07)
roofshape('Tower roof',-2.1,1.3,4.95,2.1,2.2,1.6,tile)
banner(-2.1,1.3,7.4)
box('Town steps',(0,-2.5,.22),(2.0,.8,.4),stone,.035)

start('barracks');house(4.7,3.7,2.4)
banner(-2,-1.7,4.1)
for x in [-1.3,1.3]:
 box('Shield mounting',(x,-1.92,2.15),(.65,.18,.79),blue,.1)
 box('Shield emblem',(x,-2.026,2.15),(.10,.05,.45),gold,.014)

start('house');house(2.9,2.7,1.9)
box('Chimney',(.85,.6,3.0),(.53,.57,2.8),stone,.04)

start('lumber');house(3.4,2.7,1.9,tiles=False)
for i in range(5):
 y=-1.3+i*.55
 cylinder('Lumber stacked end',(-1.3,y,.55),.23,2.7,end,verts=10).rotation_euler.y=math.pi/2
box('Saw bench',(0,-1.9,.65),(3.4,.8,1.3),wood,.04)

start('tower')
box('Tower foundation',(0,0,.2),(2.25,2.25,.4),stone,.05)
box('Stone tower core',(0,0,2.0),(1.55,1.55,3.9),stone,.05)
box('Wooden platform',(0,0,4.0),(2.55,2.55,.3),wood,.035)
for x in [-1.15,1.15]:
 for y in [-1.15,1.15]:box('Watch post',(x,y,4.65),(.18,.18,1.4),wood,.018)
roofshape('Watchtower roof',0,0,5.15,3.0,2.9,1.25,tile)
for y in [-1.18,1.18]:box('Watch parapet',(0,y,4.4),(2.6,.15,.55),blue,.025)
banner(0,0,7.0)

start('forge');house(3.8,3.0,2.1)
box('Forge chimney',(1.3,.5,3.05),(.9,.95,4.9),stone,.06)
box('Forge mouth',(1.31,-.01,1.25),(.67,.08,.8),dark,.025)
box('Forge embers',(1.31,-.07,.92),(.48,.08,.13),gold,.02)
box('Anvil base',(-.5,-1.9,.47),(.9,.7,.9),wood,.06)
box('Anvil',(-.5,-1.9,1.05),(1.25,.7,.35),iron,.05)

start('enemy');house(5.0,4.0,2.5,tiles=False)
for o in assets['enemy']:
 if o.data.materials[0] in [roof,tile,ridge,blue]:o.data.materials[0]=red
banner(1.6,-1.85,5.7,red)

def character(kind):
 start(kind)
 hero=kind=='hero';worker=kind=='worker';archer=kind=='archer'
 chest=blue if not worker else linen
 ico('Torso',(0,0,1.03),(.33,.22,.41),chest,2)
 box('Belt',(0,0,.84),(.57,.4,.09),leather,.02)
 ico('Head',(0,-.01,1.57),(.19,.18,.23),skin,2)
 if not worker:
  ico('Helmet',(0,.015,1.68),(.23,.21,.19),steel,2)
  box('Nasal guard',(0,-.195,1.61),(.045,.06,.26),gold,.01)
  for x in [-.32,.32]:ico('Pauldron',(x,0,1.29),(.20,.22,.17),steel,1)
 else:
  cylinder('Peasant cap',(0,0,1.75),.22,.13,linen,top=.16)
 for side,x in [('L',-.17),('R',.17)]:
  ico('Leg'+side,(x,0,.48),(.13,.15,.34),leather,1)
  box('Boot'+side,(x,-.08,.15),(.25,.38,.3),iron if not worker else leather,.04)
 for side,x in [('L',-.38),('R',.38)]:
  ico('Arm'+side,(x,-.02,1.02),(.14,.16,.32),chest,1)
  ico('Hand'+side,(x,-.08,.82),(.11,.11,.12),skin,1)
 if hero:
  box('Cape',(0,.22,1.04),(.59,.12,.68),blue,.03)
  ico('Shield',(-.43,-.2,1.05),(.15,.30,.36),blue,2)
  ico('Shield boss',(-.55,-.25,1.05),(.075,.10,.13),gold,1)
  box('Blade',(.40,-.08,.46),(.12,.07,.68),steel,.014)
  box('Crossguard',(.4,-.08,.83),(.33,.1,.08),gold,.015)
 elif worker:
  box('Axe handle',(.40,-.08,.82),(.06,.08,.9),end,.008)
  box('Axe iron',(.48,-.08,1.20),(.31,.12,.23),iron,.02)
 elif archer:
  beam('Bow upper',(-.39,-.12,.85),(-.5,-.32,1.45),.065,end)
  beam('Bow lower',(-.39,-.12,.85),(-.50,-.32,.48),.065,end)
  beam('Bow string',(-.50,-.32,.48),(-.50,-.32,1.45),.012,linen)
 else:
  cylinder('Spear shaft',(.40,-.08,1.28),.035,2.3,end,verts=8)
  cylinder('Spearhead',(.40,-.08,2.52),.10,.30,steel,top=0,verts=6)
 if hero:
  box('Crest mount',(0,.025,1.84),(.09,.28,.15),gold,.018)
  box('Blue helmet crest',(0,.025,1.95),(.07,.26,.20),blue,.02)
for name in ['hero','worker','soldier','archer']:character(name)

start('horse')
ico('Chestnut body',(0,.04,1.12),(.40,.83,.43),wood,2)
ico('Muscular hindquarters',(0,.49,1.12),(.37,.46,.42),wood,2)
beam('Raised neck',(0,-.43,1.2),(0,-.72,1.85),.34,wood)
ico('Head and muzzle',(0,-.82,1.85),(.21,.37,.20),end,2)
for x in [-.11,.11]:ico('Pointed ear',(x,-.68,2.04),(.074,.068,.155),wood)
for x in [-.24,.24]:
 for y in [-.39,.48]:
  beam('Leg',(x,y,.97),(x,y+.06,.12),.16,wood)
  box('Hoof',(x,y+.045,.09),(.20,.24,.18),dark,.026)
ico('Tail',(0,.82,.93),(.12,.17,.41),leather,2)
ico('Blue saddlecloth',(0,.04,1.43),(.39,.40,.12),blue,2)
ico('Saddle',(0,.04,1.51),(.28,.31,.11),leather,2)

reports={}
for name,obs in assets.items():
 bpy.context.view_layer.update()
 # Recalculate after final rotations, and apply scale on all props.
 for o in obs:
  bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.transform_apply(location=False,rotation=True,scale=True);o.select_set(False)
  bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 individual={o.name:qa_check.run(objects=[o.name],uv=False) for o in obs}
 assembly=qa_check.run(objects=[o.name for o in obs],uv=False,eps=.00015,tol=.005)
 reports[name]={'components':individual,'assembly':assembly,'triangles':sum(len(o.data.polygons) for o in obs),'objects':len(obs),'materials':len(set(o.data.materials[0].name for o in obs))}
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.ops.export_scene.gltf(filepath=str(DEST/(name+'.glb')),use_selection=True,export_format='GLB',export_yup=True,export_apply=True,export_animations=False)
bpy.context.scene.render.engine='BLENDER_WORKBENCH'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'FrontierRTS.blend'))
(OUT/'asset-qa.json').write_text(json.dumps(reports,indent=2))
for name in assets:
 for mode in ['material','wire']:
  review_shots.run(objects=[o.name for o in assets[name]],out=str(OUT/(name+'-'+mode+'.png')),views=['front','right','top','three_quarter'],mode=mode,res=240)
print('RTS_ASSETS',json.dumps({n:{'tris':r['triangles'],'errors':r['assembly']['errors'],'warnings':r['assembly']['warnings']} for n,r in reports.items()}))

"""Blender background build: -- <Unity WorkerGear directory> <review directory>."""
import bpy, bmesh, math, sys, random, json
from mathutils import Vector
from pathlib import Path
out,review=map(lambda x:Path(x).resolve(),sys.argv[-2:]);out.mkdir(exist_ok=True,parents=True);review.mkdir(exist_ok=True,parents=True)
bpy.ops.wm.read_factory_settings(use_empty=True); rng=random.Random(181);tau=math.tau
palette=[('Split cane',(.32,.165,.065)),('Weathered willow',(.235,.111,.042)),('Sunlit cane',(.39,.217,.09)),('Bindings',(.135,.072,.035)),('Faded hemp',(.30,.228,.135)),('Slate',(.10,.119,.132)),('Granite',(.19,.195,.18)),('Basalt',(.074,.080,.082))];mats=[]
for name,col in palette:
 m=bpy.data.materials.new(name);m.diffuse_color=(*col,1);s=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');s.inputs['Base Color'].default_value=(*col,1);s.inputs['Roughness'].default_value=.92;mats.append(m)
V=[];F=[];M=[]
def face(v,m):F.append(v);M.append(m)
def tube(points,axes,width,thick,mat,closed=False,n=4):
 off=len(V);P=[Vector(p) for p in points];length=len(P)
 for i,p in enumerate(P):
  tangent=(P[(i+1)%length if closed else min(i+1,length-1)]-P[(i-1)%length if closed else max(0,i-1)]).normalized();a=Vector(axes[i]).normalized();b=tangent.cross(a).normalized();a=b.cross(tangent).normalized()
  for j in range(n):
   # bevelled edges on the wider, flat splints
   angle=tau*(j+.5)/n;V.append(tuple(p+a*math.cos(angle)*width+b*math.sin(angle)*thick))
 for i in range(length if closed else length-1):
  for j in range(n):face(tuple(off+k*n+l for k,l in ((i,j),(i,(j+1)%n),((i+1)%length,(j+1)%n),((i+1)%length,j))),mat)
 if not closed:face(tuple(off+j for j in reversed(range(n))),mat);face(tuple(off+(length-1)*n+j for j in range(n)),mat)
def section(t,h,offset=0):
 q=h/.46;return ((.167+.073*q+offset)*math.cos(t),(.122+.047*q+offset)*math.sin(t)+q*.012,h)
up=(0,0,1);N=48
for row in range(13):
 h=.021+.034*row;pts=[section(tau*i/N,h,.004*math.cos(tau*i/N*12+row*math.pi)) for i in range(N)];tube(pts,[up]*N,.023,.0048,rng.choice([0,0,1,2]),True)
for rib in range(24):
 t=tau*rib/24;pts=[section(t,.007+i*.034, -.004*math.cos(i*math.pi-math.pi*.41+rib*math.pi)) for i in range(14)];tube(pts,[(-math.sin(t),math.cos(t),0)]*14,.012,.0046,rng.choice([0,1,1]),False)
for h,w in ((.008,.009),(.435,.007),(.452,.012)):
 pts=[section(tau*i/N,h,.002) for i in range(N)];tube(pts,[up]*N,w,w,3 if h==.435 else 0,True,8)
# Wrapped lashings pin the rolled lip to the uprights.
for k in range(18):
 t=tau*k/18;pts=[]
 for j in range(12):
  u=tau*j/12;pts.append(section(t+.016*math.sin(u),.446+.014*math.cos(u),.002+.014*math.sin(u)))
 tube(pts,[(-math.sin(t),math.cos(t),0)]*12,.0038,.0038,3,True)
# Interleaved bottom slats behind a solid floor, and the two flat sling straps.
for i in range(-6,7):
 x=i*.025;y=.122*math.sqrt(max(.015,1-(x/.167)**2));tube([(x,-y,.009),(x,y,.009)],[(1,0,0)]*2,.016,.007,i%3)
for sign in (-1,1):
 pts=[(.127,-.087,.055),(.158,-.129,.098),(.18,-.202,.178),(.175,-.305,.264),(.14,-.36,.36),(.117,-.36,.449),(.11,-.321,.494),(.106,-.241,.511),(.115,-.17,.493),(.129,-.112,.414),(.136,-.116,.335)];tube([(sign*x,y,z) for x,y,z in pts],[(1,0,0)]*len(pts),.024,.006,4,False,8)
def finish(name):
 used=sorted(set(M));me=bpy.data.meshes.new(name);me.from_pydata(V,[],F);me.update();ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob)
 for i in used:me.materials.append(mats[i]);
 for f,m in zip(me.polygons,M):f.material_index=used.index(m)
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>4]);bm.to_mesh(me);bm.free();me.update();bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob;bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.008);bpy.ops.object.mode_set(mode='OBJECT');V.clear();F.clear();M.clear();return ob
basket=finish('Woven carrier');
# A tight lower layer with several broken, flatter stones showing above the rim.
for x,y,z,rx,ry,rz in [(0,0,.334,.158,.119,.095),(-.087,-.032,.413,.104,.079,.076),(.083,-.046,.421,.091,.073,.085),(.095,.068,.431,.104,.074,.063),(-.075,.069,.44,.082,.075,.084),(-.008,.003,.482,.09,.067,.059)]:
 off=len(V);turn=rng.random()*tau;mat=rng.choice([5,5,6,7]);n=7
 for ring,(a,h) in enumerate(((.60,-.9),(1,-.13),(.82,.66),(.46,.93))):
  for j in range(n):
   t=tau*j/n+turn+ring*.12;r=a*rng.uniform(.85,1.09);V.append((x+rx*r*math.cos(t),y+ry*r*math.sin(t),z+rz*(h+rng.uniform(-.15,.13))))
 face(tuple(off+j for j in reversed(range(n))),mat)
 for k in range(3):
  for j in range(n):
   a=off+k*n+j;b=off+k*n+(j+1)%n;face((a,b,b+n),mat);face((a,b+n,a+n),mat)
 face(tuple(off+3*n+j for j in range(n)),mat)
rocks=finish('Stone load');bpy.ops.object.select_all(action='SELECT');bpy.context.view_layer.objects.active=basket
# Use the base of the basket as the origin; export forward in the same convention as the worker.
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(review/'OrePannier.blend'));bpy.ops.export_scene.fbx(filepath=str(out/'OrePannier.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y',mesh_smooth_type='FACE');
sys.path.insert(0,str(Path.home()/'.codex/skills/blender-asset-qa/scripts'));import qa_check
report={ob.name:qa_check.run(objects=[ob.name],profile='game',eps=.00018,tol=.005) for ob in [basket,rocks]};(review/'geometry.json').write_text(json.dumps(report,indent=2));print('QA',[(k,v['errors']) for k,v in report.items()]);

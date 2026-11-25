"""Reference-inspired stylized trees: layered spruce, full fir, bonsai pine and old broadleaf."""
from pathlib import Path
import bpy,bmesh,math,random,json,sys
import numpy as np
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
exec(compile((ROOT/'scripts/create_tree_studies.py').read_text().split('\nbark,foliage,needles=textures()')[0],str(ROOT/'scripts/create_tree_studies.py'),'exec'))
OUT=ROOT/'output/reference-trees';WEB=ROOT/'public/models/reference-trees';UNITY=ROOT/'UnityTreeStudies/Assets/ReferenceTrees'
for p in [OUT,WEB,UNITY/'Models',UNITY/'Textures',UNITY/'Editor',UNITY/'Materials']:p.mkdir(parents=True,exist_ok=True)
# flat albedos; lighting creates all face variation
n=256;a=np.ones((n,n,4),np.float32);a[:,:,:3]=[.35,.56,.08]
LEAF=material('M_ReferenceLeaf',save_texture('T_ReferenceLeaf',a),(.35,.56,.08))
for link in list(next(n for n in LEAF.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].links):LEAF.node_tree.links.remove(link)
next(n for n in LEAF.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(.35,.56,.08,1)
n=256;a=np.ones((n,n,4),np.float32);a[:,:,:3]=[.40,.22,.10]
BARK=material('M_ReferenceBark',save_texture('T_ReferenceBark',a),(.40,.22,.10))

def frond(mesh,base,direction,length,width,seed,fold=.18):
    rr=random.Random(seed);base=Vector(base);d=Vector(direction).normalized();up=Vector((0,0,1))
    side=d.cross(up)
    if side.length<.1:side=d.cross(Vector((0,1,0)))
    side.normalize();up=side.cross(d).normalized()
    # broad five-segment drooping leaf spray, with a raised central vein
    pts=[]
    for j in range(6):
        t=j/5;center=base+d*length*t+up*(math.sin(t*math.pi)*length*.12-t*t*length*.16)
        w=width*math.sin(math.pi*(t*.90+.05))**.66*(.9+rr.random()*.2)
        pts.append((center, w))
    for j in range(5):
        c0,w0=pts[j];c1,w1=pts[j+1];
        v0=c0+side*w0;v1=c1+side*w1;v2=c1-side*w1;v3=c0-side*w0
        ridge=c0.lerp(c1,.5)+up*width*fold
        ids=[mesh.vert(v) for v in [v3,v0,v1,v2,ridge]]
        uv=[(.08,.1+j*.17),(.92,.1+j*.17),(.92,.27+j*.17),(.08,.27+j*.17),(.5,.18+j*.17)]
        for face in [[0,1,4],[1,2,4],[2,3,4],[3,0,4]]:mesh.face([ids[k] for k in face],[uv[k] for k in face])

def broadleaf(mesh,center,axis,size,seed):
    rr=random.Random(seed);c=Vector(center);d=Vector(axis).normalized();side=d.cross(Vector((0,0,1)))
    if side.length<.1:side=d.cross(Vector((0,1,0)))
    side.normalize();up=side.cross(d).normalized();w=size;length=size*1.45
    # rounded lobed leaf silhouette, slightly folded around the center vein
    pts=[c-d*length*.52, c+side*w*.66-d*length*.10, c+side*w*.54+d*length*.35, c+d*length*.52, c-side*w*.54+d*length*.35, c-side*w*.66-d*length*.10, c+up*w*.18]
    ids=[mesh.vert(v) for v in pts];uv=[(.08,.45),(.28,.1),(.72,.1),(.94,.45),(.72,.9),(.28,.9),(.5,.5)]
    for k in range(6):mesh.face([ids[k],ids[(k+1)%6],ids[6]],[uv[k],uv[(k+1)%6],uv[6]])

def cluster(mesh,center,scale,kind,lod,seed):
    rr=random.Random(seed);c=Vector(center);s=Vector(scale)
    # low-poly closed crown volume
    before=len(mesh.uv);mass(mesh,c,s,[12,9,7][lod],[6,5,4][lod],seed,0)
    for i in range(before,len(mesh.uv)):mesh.uv[i]=[(u,.2+v*.25) for u,v in mesh.uv[i]]
    if lod==2:return
    count=18 if lod==0 else 8
    for j in range(count):
        a=j*2.399+rr.uniform(-.2,.2);z=rr.uniform(-.8,.8);rad=math.sqrt(max(.01,1-z*z));p=c+Vector((math.cos(a)*rad*s.x*.82,math.sin(a)*rad*s.y*.82,z*s.z*.82))
        d=(p-c).normalized(); broadleaf(mesh,p,d*-.35+Vector((0,0,.35)),rr.uniform(.16,.28)*s.x,seed+j) if kind=='leaf' else frond(mesh,p,d+Vector((0,0,-.28)),rr.uniform(.55,.85)*s.x,rr.uniform(.12,.22)*s.x,seed+j,.12)

NAMES=['LayeredSpruce','FullFir','BonsaiPine','AncientBroadleaf'];TITLES=['层叠云杉','丰冠冷杉','风雕盆景松','古树阔叶']
records=[];reports={};roots=[]
for style,name in enumerate(NAMES):
 rec={'id':name,'title':TITLES[style],'alphaClip':False,'lods':[]}
 for lod in range(3):
  parent=bpy.data.objects.new(f'{name}_LOD{lod}',None);sc.collection.objects.link(parent);wood=Mesh();fol=Mesh();sides=[12,8,6][lod]
  if style==0: trunk=[(0,0,0),(.05,0,.8),(-.08,.02,2.1),(.10,0,3.6),(-.03,.02,5.1),(.04,0,6.5),(.02,0,7.7)];height=7.8
  elif style==1: trunk=[(0,0,0),(.08,0,.8),(-.12,.03,2.0),(.18,.02,3.4),(.04,.04,4.8),(.16,0,6.2),(.02,0,7.4)];height=7.5
  elif style==2: trunk=[(0,0,0),(.14,0,.65),(-.32,.06,1.7),(.35,.08,2.9),(.72,.08,4.0),(.48,.04,5.1),(.90,.02,6.0)];height=6.1
  else: trunk=[(0,0,0),(.12,0,.7),(-.28,-.03,1.8),(-.12,.08,3.0),(.44,.12,4.0),(.74,.10,5.1),(.54,.02,6.4),(.92,.06,7.3)];height=7.4
  tube(wood,trunk,[.58,.43,.34,.28,.21,.13,.06,.02][:len(trunk)],sides,.2)
  # roots
  for k in range([6,5,4][lod]):
   a=k*2.399;d=Vector((math.cos(a),math.sin(a),0));tube(wood,[(0,0,.18),d*.5+Vector((0,0,.05)),d*1.0],[.20,.08,.012],max(4,sides-3),a)
  branches=[]
  if style in (0,1):
   for k in range(9 if lod==0 else 6 if lod==1 else 4):
    z=1.25+k*.66;rad=(2.0*(1-k/11)**.65)*(1.0 if style==0 else .82);count=7 if lod==0 else 5 if lod==1 else 3
    for j in range(count):
     a=j*math.tau/count+k*.65;d=Vector((math.cos(a),math.sin(a),0));start=Vector((0,0,z))+d*.38;end=start+d*rad+Vector((0,0,-.18 if style==0 else -.05));tube(wood,[start,start.lerp(end,.55)+Vector((0,0,.06)),end],[.12,.06,.012],max(4,sides-3),j+k*9);branches.append((end,d,rad*.45, .16 if style==0 else .22))
  else:
   for k in range(14 if style==2 else 16):
    idx=2+k%5;start=Vector(trunk[idx]);a=k*2.399+(0.3 if style==3 else 1.1);d=Vector((math.cos(a),math.sin(a),.25 if style==2 else .45));length=random.Random(31+k*11+style).uniform(1.0,2.1);start=start+d*.32;end=start+d*length;tube(wood,[start,start.lerp(end,.55)+Vector((0,0,.12)),end],[.11 if style==2 else .15,.05,.012],max(5,sides-2),k);branches.append((end,d,.75 if style==2 else 1.0,.15 if style==2 else .33))
  # top leader/foliage clusters
  if style==0:
   for k in range(9):
    z=1.55+k*.70;rad=2.0*(1-k/10)**.78
    for j in range(5 if lod==0 else 3 if lod==1 else 2):
     a=j*math.tau/(5 if lod==0 else 3 if lod==1 else 2)+k*.77;d=Vector((math.cos(a),math.sin(a),-.18));frond(fol,Vector((0,0,z))+d*rad*.25,d,rad*.9,rad*.22,100+k*13+j,.2)
  elif style==1:
   for k in range(10):
    z=1.15+k*.64;rad=2.0*(1-k/11)**.73
    for j in range(6 if lod==0 else 4 if lod==1 else 2):
     a=j*math.tau/(6 if lod==0 else 4 if lod==1 else 2)+k*.41;d=Vector((math.cos(a),math.sin(a),-.08));frond(fol,Vector((0,0,z))+d*rad*.12,d,rad*.85,rad*.20,200+k*17+j,.17)
  elif style==2:
   for i,(p,d,rad,w) in enumerate(branches):cluster(fol,p+Vector((0,0,.18)),(rad*1.0,rad*.75,rad*.38),'pine',lod,300+i)
  else:
   for i,(p,d,rad,w) in enumerate(branches):cluster(fol,p+Vector((0,0,.18)),(rad*1.15,rad*.95,rad*.75),'leaf',lod,400+i)
  if style==0: cluster(fol,(0,0,7.15),(1.0,.92,.55),'pine',lod,999)
  if style==1: cluster(fol,(0,0,6.9),(1.1,1.0,.65),'pine',lod,998)
  if style==2: cluster(fol,(.9,.02,5.75),(1.05,.8,.48),'pine',lod,997)
  if style==3: cluster(fol,(.65,.05,6.7),(1.25,1.1,.85),'leaf',lod,996)
  wo=wood.finish(f'SM_{name}_Bark_LOD{lod}',BARK,parent);fo=fol.finish(f'SM_{name}_Foliage_LOD{lod}',LEAF,parent)
  for p in wo.data.polygons:p.use_smooth=True
  for p in fo.data.polygons:p.use_smooth=True
  for v in wo.data.vertices:v.co.z=max(0,v.co.z)
  obs=[wo,fo];bpy.context.view_layer.update();qa=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002);reports[f'{name}_LOD{lod}']=qa;print('QA',name,lod,qa['pass'],qa['errors'],flush=True)
  pts=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box];dims=[round(max(p[k] for p in pts)-min(p[k] for p in pts),3) for k in range(3)];tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs)
  rec['lods'].append({'level':lod,'triangles':tris,'dimensions':dims,'objects':2,'materials':2,'url':f'/models/reference-trees/{name}_LOD{lod}.glb'})
  select([parent]+obs);bpy.ops.export_scene.gltf(filepath=str(OUT/f'{name}_LOD{lod}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
  if lod==0:
   for mode in ['material','wire']:review_shots.run(objects=[o.name for o in obs],out=str(OUT/f'{name}_{mode}.png'),views=['front','right','top','three_quarter'],mode=mode,res=450)
  roots.append(parent)
 select([parent]+obs);bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
 records.append(rec)
(OUT/'qa.json').write_text(json.dumps(reports,indent=2));(WEB/'manifest.json').write_text(json.dumps({'version':1,'units':'meters','styles':records},indent=2,ensure_ascii=False))
for i,p in enumerate(roots):p.location=((i%4)*9.5,(i//4)*11,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ReferenceTrees.blend'))
if any(not q['pass'] for q in reports.values()):raise RuntimeError('Fix QA before delivery')
print('REFERENCE_TREES_COMPLETE',json.dumps(records),flush=True)

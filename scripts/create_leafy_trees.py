"""Opaque, clustered storybook broadleaf trees with explicit mobile-oriented LODs."""
from pathlib import Path
import bpy,bmesh,math,random,json,sys
import numpy as np
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
exec(compile((ROOT/'scripts/create_tree_studies.py').read_text().split('\nbark,foliage,needles=textures()')[0],str(ROOT/'scripts/create_tree_studies.py'),'exec'))
OUT=ROOT/'output/leafy-trees';WEB=ROOT/'public/models/leafy-trees';UNITY=ROOT/'UnityTreeStudies/Assets/LeafyTrees'
for p in [OUT,WEB,UNITY/'Models',UNITY/'Textures',UNITY/'Editor',UNITY/'Materials']:p.mkdir(parents=True,exist_ok=True)
n=256;y,x=np.mgrid[0:n,0:n]/(n-1)
a=np.ones((n,n,4),dtype=np.float32)
# Uniform albedo: all leaf shading comes from geometry and lighting.
a[:,:,:3]=[.48,.68,.085]
LEAF=material('M_LeafyCanopy',save_texture('T_LeafyPalette',a),(.48,.68,.085))
leaf_shader=next(n for n in LEAF.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
for link in list(leaf_shader.inputs['Base Color'].links):LEAF.node_tree.links.remove(link)
leaf_shader.inputs['Base Color'].default_value=(.48,.68,.085,1)
n=512;y,x=np.mgrid[0:n,0:n]/(n-1);a=np.ones((n,n,4),dtype=np.float32)
tone=.90+.10*np.sin(x*math.tau*14+np.sin(y*13)*.9)-.18*np.clip((np.cos(x*math.tau*27+np.sin(y*9))-.65)*2,0,1)
a[:,:,:3]=np.array([.53,.30,.12])*tone[:,:,None]
BARK=material('M_LeafyBark',save_texture('T_LeafyBark',a),(.53,.30,.12))

def cluster(mesh,center,scale,lod,seed):
    rr=random.Random(seed);c=Vector(center);s=Vector(scale)
    # A closed inner crown carries volume at distance and supports every sculpted leaf.
    before=len(mesh.uv);mass(mesh,c,s*(.76 if lod<2 else 1),[10,8,7][lod],[5,4,3][lod],seed,0)
    # UVs remain for export compatibility; the foliage texture is uniformly colored.
    for i in range(before,len(mesh.uv)):
        mesh.uv[i]=[(u,.20+v*.26) for u,v in mesh.uv[i]]
    count=[72,22,0][lod]
    for j in range(count):
        z=1-2*(j+.5)/count;a=j*2.399963+rr.uniform(-.18,.18)
        radial=Vector((math.sqrt(1-z*z)*math.cos(a),math.sqrt(1-z*z)*math.sin(a),z))
        normal=Vector((radial.x/s.x,radial.y/s.y,radial.z/s.z)).normalized()
        p=c+Vector((radial.x*s.x,radial.y*s.y,radial.z*s.z))*.48
        down=Vector((0,0,-1));tangent=down-normal*down.dot(normal)
        if tangent.length<.1:tangent=Vector((1,0,0))
        tangent.normalize()
        axis=(normal*.65+tangent*.76).normalized();side=axis.cross(normal)
        if side.length<.1:side=axis.cross(Vector((0,1,0)))
        side.normalize();up=side.cross(axis).normalized()
        length=rr.uniform(.40,.60)*s.x;wid=rr.uniform(.25,.33)*s.x
        surface=c+Vector((radial.x*s.x,radial.y*s.y,radial.z*s.z))
        mid=c+(surface-c)*.96+tangent*.04;tip=c+(surface-c)*1.18+tangent*length*.58
        # Six-sided oval outline with a raised vein, twelve triangles per closed leaf.
        leftA=p.lerp(mid,.80)+side*wid*.83
        leftB=mid.lerp(tip,.33)+side*wid*.78
        rightA=p.lerp(mid,.80)-side*wid*.83
        rightB=mid.lerp(tip,.33)-side*wid*.78
        pts=[p,leftA,leftB,tip,rightB,rightA,mid+up*wid*.38,mid-up*wid*.24]
        ids=[mesh.vert(v) for v in pts]
        shade=rr.uniform(.49,.77)+max(0,z)*.16
        for k in range(6):
            for face in [[k,(k+1)%6,6],[(k+1)%6,k,7]]:
                mesh.face([ids[t] for t in face],[(.1,shade-.10),(.9,shade-.03),(.4,min(.98,shade+.07))])

NAMES=['VerdantOak','SpreadingOak','YoungOak'];TITLES=['丰叶橡树','舒冠橡树','林间幼树']
records=[];reports={};allroots=[]
for style,name in enumerate(NAMES):
    record={'id':name,'title':TITLES[style],'alphaClip':False,'lods':[]};hierarchy=[]
    for lod in range(3):
        rr=random.Random(120+style);parent=bpy.data.objects.new(f'{name}_LOD{lod}',None);sc.collection.objects.link(parent);allroots.append((style,lod,parent))
        wood=Mesh();leaves=Mesh();sides=[10,7,5][lod]
        height=7.6 if style!=2 else 5.5
        trunk=[(0,0,0),(.10,-.03,.5),(-.08,0,1.7),(.18,.05,3),(-.10,.08,4.3),(.18,.03,5.5),(.04,0,6.9)]
        if style==2:trunk=[(x*.7,y*.7,z*.73) for x,y,z in trunk]
        tube(wood,trunk,[.61,.43,.35,.28,.20,.12,.035] if style!=2 else [.35,.28,.22,.17,.12,.08,.018],sides,.31)
        for k in range([6,5,4][lod]):
            a=k*2.399+.4;d=Vector((math.cos(a),math.sin(a),0));scale=.95 if style!=2 else .62
            tube(wood,[(.02,0,.5*scale),d*.42*scale+Vector((0,0,.16)),d*1.1*scale+Vector((0,0,.025))],[.23*scale,.14*scale,.009],max(4,sides-3),a)
        # Cluster positions are identical between LODs, so the silhouette stays stable.
        specs=[]
        count=11 if style!=2 else 8
        for k in range(count):
            layer=k//4;az=k*2.399+style*.6
            radius=(1.75 if style==0 else 2.25 if style==1 else 1.20)*(1-layer*.20)*rr.uniform(.90,1.10)
            z=(3.45+layer*1.35+rr.uniform(-.18,.18))*(.76 if style==2 else 1)
            c=Vector((math.cos(az)*radius,math.sin(az)*radius,z))
            scale=(rr.uniform(1.04,1.28),rr.uniform(.96,1.20),rr.uniform(.78,.96))
            if style==1:scale=(scale[0]*1.1,scale[1]*1.1,scale[2]*.95)
            if style==2:scale=tuple(v*.80 for v in scale)
            specs.append((c,scale))
        specs.append((Vector((.10,0,6.85 if style!=2 else 5.05)),(1.10,1.04,.88) if style!=2 else (.83,.80,.66)))
        for k,(c,scale) in enumerate(specs):
            z=c.z;idx=min(len(trunk)-2,max(1,int(z/1.3)-1));start=Vector(trunk[idx]);elbow=start.lerp(c,.51)+Vector((0,0,-.30));end=c+Vector((0,0,-.10))
            tube(wood,[start,elbow,end],[.17 if style!=2 else .10,.105 if style!=2 else .07,.025],max(4,sides-2),k*.72)
            cluster(leaves,c,scale,lod,482+style*131+k*17)
        wo=wood.finish(f'SM_{name}_Bark_LOD{lod}',BARK,parent);fo=leaves.finish(f'SM_{name}_Foliage_LOD{lod}',LEAF,parent)
        for v in wo.data.vertices:
            if v.co.z<0:v.co.z=0
        for p in wo.data.polygons:p.use_smooth=True
        for p in fo.data.polygons:p.use_smooth=True
        obs=[wo,fo];hierarchy +=[parent]+obs;bpy.context.view_layer.update()
        qa=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002);reports[f'{name}_LOD{lod}']=qa
        print('QA',name,lod,qa['pass'],qa['errors'],flush=True)
        points=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
        dims=[round(max(p[k] for p in points)-min(p[k] for p in points),3) for k in range(3)]
        tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs)
        record['lods'].append({'level':lod,'triangles':tris,'dimensions':dims,'objects':2,'materials':2,'url':f'/models/leafy-trees/{name}_LOD{lod}.glb'})
        select([parent]+obs);bpy.ops.export_scene.gltf(filepath=str(OUT/f'{name}_LOD{lod}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
        if lod==0:
            for mode in ['material','wire']:review_shots.run(objects=[o.name for o in obs],out=str(OUT/f'{name}_{mode}.png'),views=['front','right','top','three_quarter'],mode=mode,res=450)
    select(hierarchy)
    bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
    records.append(record)
(OUT/'qa.json').write_text(json.dumps(reports,indent=2))
(WEB/'manifest.json').write_text(json.dumps({'version':1,'units':'meters','styles':records},indent=2,ensure_ascii=False))
for im in bpy.data.images:
    if im.name.startswith('T_Leafy'):im.pack()
for style,lod,p in allroots:p.location=(style*9.3,lod*11,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'LeafyTrees.blend'))
if any(not q['pass'] for q in reports.values()):raise RuntimeError('Fix QA before delivery')
print('LEAFY_TREES_COMPLETE',json.dumps(records),flush=True)

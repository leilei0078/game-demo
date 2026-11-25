"""Four richer game trees, isolated from the original five studies."""
from pathlib import Path
import bpy, bmesh, math, random, json, sys
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
exec(compile((ROOT/'scripts/create_tree_studies.py').read_text().split('\nbark,foliage,needles=textures()')[0],str(ROOT/'scripts/create_tree_studies.py'),'exec'))
OUT=ROOT/'output/hero-trees';WEB=ROOT/'public/models/hero-trees';UNITY=ROOT/'UnityTreeStudies/Assets/HeroTrees'
for p in [OUT,WEB,UNITY/'Textures',UNITY/'Models',UNITY/'Editor',UNITY/'Materials']:p.mkdir(parents=True,exist_ok=True)


class Painter:
    def __init__(self,n=512):
        self.n=n;self.arr=np.ones((n,n,4),dtype=np.float32)
        self.arr[:,:,:3]=[.32,.46,.18];self.arr[:,:,3]=0
    def stroke(self,a,b,w,col):
        a=np.array(a)*self.n;b=np.array(b)*self.n
        lo=np.maximum(np.floor(np.minimum(a,b)-w-2).astype(int),0)
        hi=np.minimum(np.ceil(np.maximum(a,b)+w+2).astype(int),self.n)
        if np.any(hi<=lo):return
        yy,xx=np.mgrid[lo[1]:hi[1],lo[0]:hi[0]];p=np.stack([xx+.5,yy+.5],axis=-1)
        d=b-a;t=np.clip(np.sum((p-a)*d,axis=-1)/(np.dot(d,d)+1e-8),0,1)
        alpha=np.clip(w+.6-np.linalg.norm(p-a-t[:,:,None]*d,axis=-1),0,1)
        tile=self.arr[lo[1]:hi[1],lo[0]:hi[0]]
        tile[:,:,:3]=tile[:,:,:3]*(1-alpha[:,:,None])+np.array(col)*alpha[:,:,None]
        tile[:,:,3]=np.maximum(tile[:,:,3],alpha)
    def leaf(self,center,angle,length,width,col,lobes=False):
        c=np.array(center)*self.n;d=np.array([math.cos(angle),math.sin(angle)]);s=np.array([-d[1],d[0]])
        r=length*self.n;w=width*self.n
        lo=np.maximum(np.floor(c-r-w-2).astype(int),0);hi=np.minimum(np.ceil(c+r+w+2).astype(int),self.n)
        if np.any(hi<=lo):return
        yy,xx=np.mgrid[lo[1]:hi[1],lo[0]:hi[0]];p=np.stack([xx,yy],axis=-1)-c
        u=np.sum(p*d,axis=-1)/r;v=np.sum(p*s,axis=-1)/w
        shape=np.maximum(0,1-u*u)**.65
        if lobes:shape*=.73+.27*np.cos(u*math.pi*3.4)**2
        alpha=np.clip((shape-np.abs(v))*w+0.5,0,1)*(np.abs(u)<1)
        tone=.93+.12*u+.10*np.abs(v)-.09*np.exp(-np.abs(v)*15)
        tile=self.arr[lo[1]:hi[1],lo[0]:hi[0]]
        tile[:,:,:3]=tile[:,:,:3]*(1-alpha[:,:,None])+np.clip(np.array(col)*tone[:,:,None],0,1)*alpha[:,:,None]
        tile[:,:,3]=np.maximum(tile[:,:,3],alpha)


def atlas():
    tiles=[];rng=random.Random(729)
    for kind in range(4):
        p=Painter()
        if kind<2:
            p.stroke((.50,.025),(.50,.94),2.0,[.43,.32,.15])
            for k in range(11):
                y=.12+k*.068;size=.39*(1-y)**.65
                for sign in [-1,1]:
                    a=np.array([.5,y]);b=np.array([.5+sign*size,y+.16])
                    p.stroke(a,b,1.3,[.33,.40,.16])
                    d=b-a;ang=math.atan2(d[1],d[0])
                    for j in range(12 if kind==0 else 18):
                        t=.08+j*(.073 if kind==0 else .049)
                        base=a+d*t
                        for side in [-1,1]:
                            angle=ang+side*.68
                            length=(.042 if kind==0 else .045)*rng.uniform(.8,1.16)
                            col=np.array([.42,.59,.25] if kind==0 else [.40,.54,.22])*rng.uniform(.72,1.24)
                            c=base+np.array([math.cos(angle),math.sin(angle)])*length*.48
                            p.leaf(c,angle,length,.010 if kind==0 else .0055,col)
        elif kind==2:
            for j in range(19):
                angle=j*2.399;rad=.12+.22*rng.random();c=np.array([.5,.5])+np.array([math.cos(angle),math.sin(angle)])*rad
                p.stroke((.5,.07),c,1.5,[.39,.31,.15])
                for i in range(7):
                    a=angle+(i-3)*.21
                    start=c-np.array([math.cos(a),math.sin(a)])*.02
                    end=c+np.array([math.cos(a),math.sin(a)])*rng.uniform(.08,.14)
                    p.stroke(start,end,1.5,np.array([.45,.59,.24])*rng.uniform(.7,1.22))
        else:
            for j in range(39):
                angle=j*2.399;c=np.array([.5,.53])+np.array([math.cos(angle),math.sin(angle)])*rng.uniform(.06,.31)
                p.stroke((.5,.06),c,.85,[.45,.34,.15])
                p.leaf(c,angle,rng.uniform(.055,.09),rng.uniform(.026,.041),np.array([.48,.63,.23])*rng.uniform(.73,1.23),True)
        tiles.append(p.arr)
    a=np.ones((1024,1024,4),dtype=np.float32)
    a[:512,:512]=tiles[0];a[:512,512:]=tiles[1];a[512:,:512]=tiles[2];a[512:,512:]=tiles[3]
    # Dilate RGB beneath transparent pixels; preserve original alpha to avoid black fringes.
    rgb=a[:,:,:3].copy();mask=a[:,:,3]>.05
    for _ in range(8):
        total=np.zeros_like(rgb);weight=np.zeros(mask.shape,dtype=np.float32)
        for dy,dx in [(1,0),(-1,0),(0,1),(0,-1)]:
            m=np.roll(mask,(dy,dx),(0,1));total+=np.roll(rgb,(dy,dx),(0,1))*m[:,:,None];weight+=m
        fill=(~mask)&(weight>0);rgb[fill]=total[fill]/weight[fill,None];mask|=fill
    a[:,:,:3]=rgb
    return save_texture('T_HeroFoliage_Atlas',a)


def bark_texture():
    n=1024;y,x=np.mgrid[0:n,0:n]/n
    ridge=np.sin(x*math.tau*17+np.sin(y*14)*.9+np.sin(y*47+x*9)*.15)
    creases=np.clip((np.cos(x*math.tau*29+np.sin(y*21+x*12)*.6)-.65)*2.5,0,1)
    plates=.05*np.sin(y*120+np.sin(x*46)*2)
    tone=.86+.15*ridge-.32*creases+plates
    a=np.ones((n,n,4),dtype=np.float32);a[:,:,:3]=np.clip(np.array([.57,.34,.16])*tone[:,:,None],0,1)
    return save_texture('T_HeroBark',a)


def palette_tex():
    n=256;y,x=np.mgrid[0:n,0:n]/(n-1)
    a=np.ones((n,n,4),dtype=np.float32)
    a[:,:,:3]=np.array([.23,.37,.12])+(np.array([.62,.70,.28])-[.23,.37,.12])*y[:,:,None]
    return save_texture('T_HeroOpaque',a)


def barktube(mesh,points,radii,sides,seed=0):
    """Rooted, irregular longitudinal ridges encoded in real geometry."""
    points=[Vector(p) for p in points];rr=random.Random(seed);phase=rr.random()*6.28;rows=[]
    for j,(p,r) in enumerate(zip(points,radii)):
        d=(points[min(j+1,len(points)-1)]-points[max(j-1,0)]).normalized()
        u=d.cross(Vector((0,1,0))).normalized();v=d.cross(u)
        row=[]
        for i in range(sides):
            a=i*math.tau/sides+phase
            ridge=1+.095*math.sin(i*2.2+seed)+.065*math.sin(i*4.1+j*.57)
            q=p+(math.cos(a)*u+math.sin(a)*v)*r*ridge
            if j==0 and p.z==0:q.z=0
            if j==len(points)-1 and p.z<.06:q.z=max(.01,q.z)
            row.append(mesh.vert(q))
        rows.append(row)
    for j in range(len(rows)-1):
        for i in range(sides):
            h=(i+1)%sides
            mesh.face([rows[j][i],rows[j][h],rows[j+1][h],rows[j+1][i]],[(i/sides,j/(len(rows)-1)),((i+1)/sides,j/(len(rows)-1)),((i+1)/sides,(j+1)/(len(rows)-1)),(i/sides,(j+1)/(len(rows)-1))])
    for j in [0,len(rows)-1]:
        c=mesh.vert(points[j])
        for i in range(sides):
            uv=lambda k:(.5+.44*math.cos(k*math.tau/sides),.5+.44*math.sin(k*math.tau/sides))
            mesh.face([c,rows[j][i],rows[j][(i+1)%sides]],[(.5,.5),uv(i),uv(i+1)])


def spray(mesh,base,direction,length,width,tile,roll,seed):
    base=Vector(base);d=Vector(direction).normalized()
    u=d.cross(Vector((0,0,1))).normalized();v=d.cross(u)
    side=(math.cos(roll)*u+math.sin(roll)*v)*width
    fold=(math.cos(roll)*v-math.sin(roll)*u)*width*.14
    # Four triangles in a mildly folded card. Stems start exactly on their twig.
    a=mesh.vert(base);b=mesh.vert(base+d*length*.48-side+fold)
    c=mesh.vert(base+d*length*.48+fold*.5)
    e=mesh.vert(base+d*length*.48+side+fold);f=mesh.vert(base+d*length)
    def uv(x,y):return ((tile%2+x)/2,(tile//2+y)/2)
    for ids,coords in [([a,b,c],[(.5,.02),(.025,.5),(.5,.5)]),([a,c,e],[(.5,.02),(.5,.5),(.975,.5)]),([b,f,c],[(.025,.5),(.5,.98),(.5,.5)]),([c,f,e],[(.5,.5),(.5,.98),(.975,.5)])]:
        mesh.face(ids,[uv(*p) for p in coords])


def blade(mesh,base,direction,length,width,seed):
    """Opaque broad needles: two folded spear leaves per attached blade."""
    rr=random.Random(seed);base=Vector(base);d=Vector(direction).normalized()
    u=d.cross(Vector((0,0,1))).normalized();v=d.cross(u)
    for j in range(3):
        c=base+d*length*(j*.22)
        for sign in [-1,1]:
            tip=c+d*length*.51+u*width*sign+Vector((0,0,-length*.20))
            mid=c.lerp(tip,.54);side=u*width*.34
            vs=[mesh.vert(c),mesh.vert(mid+side),mesh.vert(tip),mesh.vert(mid-side),mesh.vert(mid+v*.085)]
            shade=rr.uniform(.28,.90)
            coords=[(.15,shade),(.4,min(.97,shade+.04)),(.9,min(.97,shade+.08)),(.7,max(.05,shade-.05)),(.5,shade)]
            for f in [[0,1,4],[1,2,4],[2,3,4],[3,0,4]]:mesh.face([vs[k] for k in f],[coords[k] for k in f])


def tree(style,lod):
    name=NAMES[style];rr=random.Random(1801+style*70)
    parent=bpy.data.objects.new(name+f'_LOD{lod}',None);sc.collection.objects.link(parent)
    wood=Mesh();leaves=Mesh();sides=[14,9,6][lod]
    if style<2:
        pts=[(0,0,0),(.05,-.02,.5),(-.10,.03,1.6),(.06,.01,3.1),(.04,-.08,4.7),(-.08,.02,6.3),(.04,.03,7.6),(.02,0,8.65)]
        radii=[.57,.42,.34,.27,.20,.14,.075,.018]
    elif style==2:
        pts=[(0,0,0),(.05,0,.5),(-.15,.04,1.6),(.12,.05,2.7),(.40,0,3.9),(.17,.10,5.1),(.43,.12,6.2),(.34,0,7.3),(.6,.05,8.0)]
        radii=[.61,.44,.38,.31,.26,.20,.15,.10,.025]
    else:
        pts=[(0,0,0),(.09,0,.5),(-.20,-.06,1.4),(-.64,.02,2.4),(-.34,0,3.4),(.43,.04,4.35),(.70,.04,5.2),(.38,.10,6.1),(.98,.10,7.1)]
        radii=[.74,.56,.48,.41,.35,.29,.22,.14,.045]
    barktube(wood,pts,radii,sides,30+style)
    for j in range([7,5,4][lod]):
        a=j*2.399+.23;d=Vector((math.cos(a),math.sin(a),0))
        barktube(wood,[(0,0,.38),d*.45+Vector((0,0,.20)),d*1.0+Vector((0,0,.085)),d*1.46+Vector((0,0,.02))],[.26,.20,.10,.012],max(5,sides-3),j+23)
    tips=[]
    if style<2:
        for k in range(9):
            z=1.50+k*.74;radius=(2.25*(1-k/10)**.88)*rr.uniform(.92,1.10)
            count=[6,5,4][lod] if k<6 else [5,4,3][lod]
            for j in range(count):
                a=j*math.tau/count+k*.83+rr.uniform(-.17,.17)
                d=Vector((math.cos(a),math.sin(a),0));start=Vector((.02,0,z+.29))
                mid=start+d*radius*.53+Vector((0,0,-.23));end=start+d*radius+Vector((0,0,-.31))
                barktube(wood,[start,mid,end],[.11*(1-k*.065),.064,.015],max(4,sides-5),k*31+j)
                for t in ([.32,.57,.80] if lod==0 else [.40,.77] if lod==1 else [.60]):
                    origin=start.lerp(end,t)
                    for sign in [-1,1]:
                        dd=(d+Vector((-d.y,d.x,0))*sign*.65+Vector((0,0,-.24))).normalized()
                        tip=origin+dd*(radius*.38+.23)
                        barktube(wood,[origin,tip],[.033,.006],4,300+k*20+j)
                        tips.append((tip-dd*.08,dd, .90+radius*.22, .29+radius*.075,k*100+j*10+sign))
        for j in range(6):
            a=j*2.399;d=Vector((math.cos(a)*.35,math.sin(a)*.35,.8)).normalized()
            tips.append((Vector((.02,0,7.95)),d,.85,.24,900+j))
    else:
        for k in range(15 if style==2 else 17):
            a=k*2.399+rr.uniform(-.2,.2)
            idx=2+k%6;start=Vector(pts[idx])
            radius=rr.uniform(1.35,2.50)*(1-.035*max(0,idx-4))
            d=Vector((math.cos(a),math.sin(a),0))
            mid=start+d*radius*.52+Vector((0,0,rr.uniform(.0,.37)))
            end=start+d*radius+Vector((0,0,rr.uniform(.36,.95)))
            barktube(wood,[start,mid,end],[.16,.09,.025],max(5,sides-3),k*13)
            for j in range([5,4,2][lod]):
                t=.32+j*.145 if lod<2 else .5+j*.32
                origin=mid.lerp(end,t)
                dd=(d+Vector((-d.y,d.x,0))*(1 if j%2 else -1)*.85+Vector((0,0,.6))).normalized()
                tip=origin+dd*rr.uniform(.46,.82)
                barktube(wood,[origin,origin.lerp(tip,.5)+Vector((0,0,.08)),tip],[.045,.027,.007],max(4,sides-7),k*20+j)
                tips.append((tip-dd*.07,dd,1.25 if style==2 else 1.5,.65 if style==2 else .73,k*20+j))
        # Crown leader stays connected to the central trunk.
        for j in range(6):
            a=j*2.399;d=Vector((math.cos(a),math.sin(a),.4)).normalized();start=Vector(pts[-1])
            end=start+d*.65
            barktube(wood,[start,end],[.05,.008],5,j+100)
            tips.append((end-d*.05,d,1.20,.65,j+800))
        # Exposed old broken branches make the pine trunk read as a real branching tree.
        for k in range(4):
            a=k*2.399+1.1;start=Vector(pts[2+k]);d=Vector((math.cos(a),math.sin(a),.34))
            end=start+d*.70;elbow=start.lerp(end,.62)+Vector((0,0,.12))
            barktube(wood,[start,elbow,end],[.12,.062,.026],max(5,sides-3),k+700)
    for base,d,length,width,seed in tips:
        r=random.Random(seed+500)
        if style==0:
            blade(leaves,base,d,length,width,seed)
            if lod==0:blade(leaves,base+d*.10+Vector((0,0,.07)),d+Vector((0,0,.25)),length*.88,width*.86,seed+1)
        else:
            tile=1 if style==1 else 2 if style==2 else 3
            for j in range([3,2,2][lod]):
                spray(leaves,base+d*(j*.016),d,length*(1.0 if lod<2 else 1.20),width*(1.0 if lod<2 else 1.17),tile,r.uniform(-.20,.20)+j*math.pi/[3,2,2][lod],seed+j)
    wo=wood.finish(f'SM_{name}_Bark_LOD{lod}',BARK,parent)
    fo=leaves.finish(f'SM_{name}_Foliage_LOD{lod}',OPAQUE if style==0 else FOLIAGE,parent)
    for p in wo.data.polygons:p.use_smooth=True
    # Point leaf normals outward/up as a soft canopy, preserving real geometric cutouts.
    if style!=0:
        fo.data.set_sharp_from_angle(angle=math.pi)
        normals=[]
        for loop in fo.data.loops:
            p=fo.data.vertices[loop.vertex_index].co
            normal=Vector((p.x*.15,p.y*.15,.9)).normalized()
            normals.append(normal)
        fo.data.normals_split_custom_set(normals)
    bpy.context.view_layer.update()
    return parent,[wo,fo]


BARK=material('M_HeroBark',bark_texture(),(.55,.33,.17))
FOLIAGE=material('M_HeroFoliage_Cutout',atlas(),(.42,.58,.23),True)
OPAQUE=material('M_HeroOpaque',palette_tex(),(.43,.56,.22));OPAQUE.use_backface_culling=False
NAMES=['SculptedSpruce','LayeredFir','WindsweptPine','AncientOak']
TITLES=['雕叶云杉','密簇冷杉','疏冠老松','曲干古橡']
records=[];reports={};alltrees=[]
for style,name in enumerate(NAMES):
    record={'id':name,'title':TITLES[style],'alphaClip':style>0,'lods':[]}
    hierarchy=[]
    for lod in range(3):
        parent,obs=tree(style,lod);hierarchy +=[parent]+obs;alltrees.append((style,lod,parent,obs))
        qa=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002)
        reports[f'{name}_LOD{lod}']=qa
        print('QA',name,lod,qa['pass'],qa['errors'][:8],flush=True)
        points=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
        dims=[round(max(p[k] for p in points)-min(p[k] for p in points),3) for k in range(3)]
        tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs)
        record['lods'].append({'level':lod,'triangles':tris,'dimensions':dims,'objects':2,'materials':2,'url':f'/models/hero-trees/{name}_LOD{lod}.glb'})
        select([parent]+obs)
        bpy.ops.export_scene.gltf(filepath=str(OUT/f'{name}_LOD{lod}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
        if lod==0:
            for mode in ['material','wire']:
                review_shots.run(objects=[o.name for o in obs],out=str(OUT/f'{name}_{mode}.png'),views=['front','right','top','three_quarter'],mode=mode,res=400)
    select(hierarchy)
    bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
    records.append(record)
(OUT/'qa.json').write_text(json.dumps(reports,indent=2))
(WEB/'manifest.json').write_text(json.dumps({'version':2,'units':'meters','styles':records},indent=2,ensure_ascii=False))
for im in bpy.data.images:
    if im.name.startswith('T_Hero'):im.pack()
for style,lod,parent,obs in alltrees:parent.location=(style*8,lod*11,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HeroTrees.blend'))
if any(not q['pass'] for q in reports.values()):raise RuntimeError('Fix QA errors before delivery')
print('HERO_TREES_COMPLETE',json.dumps(records),flush=True)

"""Deterministic, engine-neutral tree studies. Run with Blender 5.2 in background.

All form/color is authored as mesh + UV + PNG; no runtime procedural shader needed.
"""
import bpy, bmesh, math, random, json, sys, shutil
from pathlib import Path
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/tree-studies'
WEB = ROOT / 'public/models/trees'
UNITY = ROOT / 'UnityTreeStudies/Assets/TreeStudies'
for p in [OUT, WEB, UNITY/'Models', UNITY/'Textures', UNITY/'Editor']:
    p.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, '/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check, review_shots

for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
sc = bpy.context.scene
sc.unit_settings.system = 'METRIC'
sc.unit_settings.scale_length = 1
sc.world.color = (.18, .22, .28)


def save_texture(name, arr):
    h,w,_ = arr.shape
    im = bpy.data.images.new(name, width=w, height=h, alpha=True)
    im.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    im.filepath_raw = str(UNITY/'Textures'/f'{name}.png')
    im.file_format = 'PNG'
    im.save()
    shutil.copy(im.filepath_raw, WEB/f'{name}.png')
    return im


def textures():
    rng = np.random.default_rng(120)
    n=512
    y,x=np.mgrid[0:n,0:n]/n
    ridge = np.sin(x*math.tau*15 + np.sin(y*math.tau*2)*.65 + np.sin(y*23+x*12)*.23)
    detail=np.sin(x*math.tau*48+np.sin(y*27)*1.4)
    cracks=np.clip((np.cos(x*math.tau*23+np.sin(y*38)*.32)-.76)*3,0,1)
    shade=.79+.16*ridge+.045*detail-.24*cracks+rng.normal(0,.018,(n,n))
    arr=np.ones((n,n,4),dtype=np.float32)
    arr[:,:,:3]=np.clip(shade[:,:,None]*np.array([.48,.286,.151]),0,1)
    bark=save_texture('T_Bark_BaseColor',arr)
    n=512
    y,x=np.mgrid[0:n,0:n]/(n-1)
    grain=rng.normal(0,.015,(n,n))
    scales=np.sin(x*55+np.sin(y*40))*np.cos(y*85+x*12)*.026
    # Four color lanes, with a soft vertical dark-to-light gradient in each lane.
    palette=np.array([[.27,.46,.225],[.28,.47,.30],[.36,.49,.20],[.39,.53,.22]])
    lane=np.minimum((x*4).astype(int),3)
    grad=.69+.36*y+grain+scales
    arr=np.ones((n,n,4),dtype=np.float32)
    arr[:,:,:3]=np.clip(palette[lane]*grad[:,:,None],0,1)
    # Small painted leaf marks on the broadleaf lane, shared by every crown cluster.
    for _ in range(240):
        cx=rng.uniform(.754,.993);cy=rng.uniform(.02,.98)
        dx=(x-cx)/rng.uniform(.006,.013);dy=(y-cy)/rng.uniform(.014,.029)
        mask=np.clip((1-np.abs(dx+dy*.12)-np.abs(dy))/.22,0,1)
        col=np.array([.42,.56,.24])*rng.uniform(.75,1.14)
        arr[:,:,:3]=arr[:,:,:3]*(1-mask[:,:,None]*.54)+col*mask[:,:,None]*.54
    foliage=save_texture('T_Canopy_BaseColor',arr)
    # A hand-painted style needle spray. Alpha clipped in both engines, RGB dilated.
    n=512
    arr=np.ones((n,n,4),dtype=np.float32)
    arr[:,:,:3]=[.19,.34,.22]
    arr[:,:,3]=0
    def stroke(a,b,width,color):
        a=np.array(a)*n; b=np.array(b)*n
        low=np.maximum(np.floor(np.minimum(a,b)-width-2).astype(int),0)
        high=np.minimum(np.ceil(np.maximum(a,b)+width+2).astype(int),n)
        if np.any(high<=low): return
        yy,xx=np.mgrid[low[1]:high[1],low[0]:high[0]]
        p=np.stack([xx+.5,yy+.5],axis=-1)
        d=b-a
        t=np.clip(np.sum((p-a)*d,axis=-1)/(np.dot(d,d)+1e-8),0,1)
        distance=np.linalg.norm(p-a-t[:,:,None]*d,axis=-1)
        alpha=np.clip(width+.65-distance,0,1)
        tile=arr[low[1]:high[1],low[0]:high[0]]
        tile[:,:,:3]=tile[:,:,:3]*(1-alpha[:,:,None])+np.array(color)*alpha[:,:,None]
        tile[:,:,3]=np.maximum(tile[:,:,3],alpha)
    rr=random.Random(44)
    stroke((.50,.025),(.51,.96),2.3,(.38,.32,.17))
    for side in [-1,1]:
        for k in range(13):
            z=.10+k*.06
            length=.40*(1-z)**.60
            a=Vector((.50,z)); b=Vector((.50+side*length,z+.20*(1-z)))
            stroke(a,b,1.45,(.27,.35,.16))
            d=(b-a).normalized(); normal=Vector((-d.y,d.x))
            for j in range(18):
                t=.08+j*.05
                base=a.lerp(b,t)
                for s in [-1,1]:
                    tip=base+d*rr.uniform(.015,.042)+normal*s*rr.uniform(.035,.088)*(1-t*.5)
                    col=np.array([.30,.48,.25])*rr.uniform(.76,1.24)
                    stroke(base,tip,rr.uniform(1.0,1.8),col)
    for j in range(25):
        z=.72+j*.009
        stroke((.50,z),(.50+rr.uniform(-.055,.055),min(.99,z+.065)),1.0,(.40,.56,.29))
    needles=save_texture('T_Needles_BaseColor',arr)
    return bark,foliage,needles


def material(name, tex, color, cutout=False):
    m=bpy.data.materials.new(name)
    m.diffuse_color=(*color,1)
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Roughness'].default_value=.88
    p.inputs['Specular IOR Level'].default_value=.22
    t=m.node_tree.nodes.new('ShaderNodeTexImage'); t.image=tex
    m.node_tree.links.new(t.outputs['Color'],p.inputs['Base Color'])
    if cutout:
        m.node_tree.links.new(t.outputs['Alpha'],p.inputs['Alpha'])
        # glTF exporter detects this thresholded alpha as MASK.
        m.surface_render_method='DITHERED'
        m.alpha_threshold=.48
    m.use_backface_culling=not cutout
    return m


class Mesh:
    def __init__(self): self.v=[]; self.f=[]; self.uv=[]
    def vert(self,p): self.v.append(tuple(p)); return len(self.v)-1
    def face(self,ids,uv): self.f.append(ids); self.uv.append(uv)
    def finish(self,name,mat,parent):
        me=bpy.data.meshes.new(name)
        me.from_pydata(self.v,[],self.f); me.update()
        ob=bpy.data.objects.new(name,me); sc.collection.objects.link(ob)
        ob.parent=parent
        me.materials.append(mat)
        layer=me.uv_layers.new(name='UVMap')
        for poly,coords in zip(me.polygons,self.uv):
            for li,uv in zip(poly.loop_indices,coords): layer.data[li].uv=uv
        bm=bmesh.new(); bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
        bm.to_mesh(me); bm.free(); me.update()
        return ob


def tube(mesh, points, radii, sides=7, phase=0):
    points=[Vector(p) for p in points]
    rings=[]
    for k,(c,r) in enumerate(zip(points,radii)):
        d=(points[min(k+1,len(points)-1)]-points[max(k-1,0)]).normalized()
        q=Vector((0,1,0))
        if abs(q.dot(d))>.95: q=Vector((1,0,0))
        u=d.cross(q).normalized(); v=d.cross(u).normalized()
        row=[]
        for i in range(sides):
            p=c+r*(math.cos(phase+i*math.tau/sides)*u+math.sin(phase+i*math.tau/sides)*v)
            if k==0 and abs(c.z)<1e-7:p.z=0
            row.append(mesh.vert(p))
        rings.append(row)
    for j in range(len(rings)-1):
        for i in range(sides):
            h=(i+1)%sides
            mesh.face([rings[j][i],rings[j][h],rings[j+1][h],rings[j+1][i]],[(i/sides,j/(len(rings)-1)),((i+1)/sides,j/(len(rings)-1)),((i+1)/sides,(j+1)/(len(rings)-1)),(i/sides,(j+1)/(len(rings)-1))])
    for j in [0,len(rings)-1]:
        cen=mesh.vert(points[j])
        for i in range(sides):
            h=(i+1)%sides
            uv=lambda k:(.5+.45*math.cos(phase+k*math.tau/sides),.5+.45*math.sin(phase+k*math.tau/sides))
            mesh.face([cen,rings[j][i],rings[j][h]],[(.5,.5),uv(i),uv(i+1)])


def mass(mesh,center,scale,segments,rings,seed,lane,axis=None,facet=0):
    """Closed irregular scalloped foliage clump with no duplicate pole vertices."""
    rr=random.Random(seed)
    c=Vector(center); scale=Vector(scale)
    axis=Vector(axis or (0,0,1)).normalized()
    q=Vector((0,1,0))
    if abs(q.dot(axis))>.95: q=Vector((1,0,0))
    u=axis.cross(q).normalized();v=axis.cross(u)
    phase=rr.random()*math.tau
    rand=[rr.uniform(.84,1.15) for _ in range(segments)]
    def uv(i,h): return ((lane+.08+.84*i/segments)/4,.05+.9*h)
    bottom=mesh.vert(c-axis*scale.z*.86)
    levels=[]
    for j in range(1,rings):
        t=j/rings; theta=t*math.pi
        row=[]
        for i in range(segments):
            a=phase+i*math.tau/segments
            scallop=1+.10*math.sin(i*2.7+j*1.8+seed)
            p=c+u*(math.sin(theta)*math.cos(a)*scale.x*rand[i]*scallop)+v*(math.sin(theta)*math.sin(a)*scale.y*rand[i]*scallop)+axis*((-math.cos(theta)+facet*math.sin(i*3.1))*scale.z)
            row.append(mesh.vert(p))
        levels.append(row)
    top=mesh.vert(c+axis*scale.z)
    for i in range(segments):
        h=(i+1)%segments
        mesh.face([bottom,levels[0][h],levels[0][i]],[uv(i+.5,0),uv(i+1,1/rings),uv(i,1/rings)])
        for j in range(len(levels)-1):
            ids=[levels[j][i],levels[j][h],levels[j+1][h],levels[j+1][i]]
            coords=[uv(i,(j+1)/rings),uv(i+1,(j+1)/rings),uv(i+1,(j+2)/rings),uv(i,(j+2)/rings)]
            # Fixed diagonal lets the faceted study keep crisp planes.
            mesh.face([ids[0],ids[1],ids[2]],[coords[0],coords[1],coords[2]])
            mesh.face([ids[0],ids[2],ids[3]],[coords[0],coords[2],coords[3]])
        mesh.face([levels[-1][i],levels[-1][h],top],[uv(i,(rings-1)/rings),uv(i+1,(rings-1)/rings),uv(i+.5,1)])


def tier(mesh,z,radius,seed,lod):
    rr=random.Random(seed)
    n=[14,10,7][lod]
    phase=rr.random()*math.tau
    levels=[]
    for k,(r,h) in enumerate([(.15,.72),(.60,.30),(1,-.05),(.48,-.16)]):
        row=[]
        for i in range(n):
            a=phase+i*math.tau/n
            f=1+.13*math.sin(i*2.3+seed)+.10*math.sin(i*4.1)
            drop=.14 if i%2 else -.07
            row.append(mesh.vert((math.cos(a)*radius*r*f,math.sin(a)*radius*r*f,z+radius*(h+(drop if k==2 else 0)))))
        levels.append(row)
    bottom=mesh.vert((0,0,z-radius*.17))
    top=mesh.vert((0,0,z+radius*.85))
    def uv(i,k): return ((.08+.84*i/n)/4,.08+.84*(1-k/3))
    for i in range(n):
        h=(i+1)%n
        mesh.face([top,levels[0][i],levels[0][h]],[(.12,.95),uv(i,0),uv(i+1,0)])
        for j in range(3):
            a,b,c,d=levels[j][i],levels[j][h],levels[j+1][h],levels[j+1][i]
            mesh.face([a,b,c],[uv(i,j),uv(i+1,j),uv(i+1,j+1)])
            mesh.face([a,c,d],[uv(i,j),uv(i+1,j+1),uv(i,j+1)])
        mesh.face([bottom,levels[-1][h],levels[-1][i]],[(.12,.02),uv(i+1,3),uv(i,3)])


def card(mesh,a,b,width,roll):
    a=Vector(a);b=Vector(b);d=(b-a).normalized()
    q=Vector((0,0,1))
    if abs(q.dot(d))>.98:q=Vector((0,1,0))
    u=d.cross(q).normalized();v=d.cross(u)
    side=(math.cos(roll)*u+math.sin(roll)*v)*width
    # A bent six-vertex spray, substantially less empty alpha area than a full plane.
    mid=a.lerp(b,.48)+v*.08
    ids=[mesh.vert(a-side*.24),mesh.vert(a+side*.24),mesh.vert(mid+side),mesh.vert(b+side*.15),mesh.vert(b-side*.15),mesh.vert(mid-side)]
    coords=[(.38,.025),(.62,.025),(.98,.50),(.61,.99),(.39,.99),(.02,.50)]
    for face in [[0,1,2],[0,2,5],[5,2,3],[5,3,4]]:
        mesh.face([ids[k] for k in face],[coords[k] for k in face])


def frond(mesh,base,axis,length,width,lobes,seed):
    """Serrated, folded, opaque needle frond. Each blade has a real silhouette."""
    rr=random.Random(seed)
    axis=Vector(axis).normalized();base=Vector(base)
    side=axis.cross(Vector((0,0,1))).normalized()
    up=side.cross(axis).normalized()
    # Boughs twist around the supporting branch, preventing parallel leaf surfaces.
    roll=rr.uniform(-.48,.48)
    side,up=side*math.cos(roll)+up*math.sin(roll),up*math.cos(roll)-side*math.sin(roll)
    count=lobes*2+1
    centers=[];left=[];right=[]
    for j in range(count+1):
        t=j/count
        c=base+axis*length*t+up*(math.sin(t*math.pi)*.11-t*t*.17)
        centers.append(mesh.vert(c+up*.045*math.sin(math.pi*t)))
        w=width*(math.sin(math.pi*(t*.86+.07))**.72)*(1 if j%2 else .52)*rr.uniform(.90,1.1)
        sweep=axis*length*(.045 if j%2 else -.018)
        left.append(mesh.vert(c-side*w-sweep-up*.028))
        right.append(mesh.vert(c+side*w-sweep-up*.028))
    for j in range(count):
        u0=.27+.20*j/count;u1=.27+.20*(j+1)/count
        v0=.38+.44*j/count;v1=.38+.44*(j+1)/count
        for boundary,direction in [(left,-1),(right,1)]:
            uv0=(u0,v0-.04);uv1=(u1,v1-.04)
            edge0=(u0+.018*direction,v0+.04);edge1=(u1+.018*direction,v1+.04)
            mesh.face([centers[j],boundary[j],boundary[j+1]],[uv0,edge0,edge1])
            mesh.face([centers[j],boundary[j+1],centers[j+1]],[uv0,edge1,uv1])


def edge_leaves(mesh,c,scale,seed,count):
    rr=random.Random(seed+900)
    c=Vector(c);scale=Vector(scale)
    for j in range(count):
        z=rr.uniform(-.65,.85);a=j*2.399+rr.random()
        n=Vector((math.sqrt(1-z*z)*math.cos(a),math.sqrt(1-z*z)*math.sin(a),z))
        r=Vector((n.x*scale.x,n.y*scale.y,n.z*scale.z))
        base=c+r*.55;tip=c+r*1.30
        side=n.cross(Vector((0,0,1))).normalized()*.15
        mid=c+r*.96
        ids=[mesh.vert(base),mesh.vert(mid+side),mesh.vert(tip),mesh.vert(mid-side)]
        uv=[(.80,.32),(.83,.60),(.88,.94),(.87,.60)]
        mesh.face([ids[0],ids[1],ids[2]],[uv[0],uv[1],uv[2]])
        mesh.face([ids[0],ids[2],ids[3]],[uv[0],uv[2],uv[3]])


def make(style,lod):
    rr=random.Random(820+style*97)
    name=['FacetedFir','PaintedFir','NaturalPine','OldOak'][style]
    parent=bpy.data.objects.new(f'{name}_LOD{lod}',None);sc.collection.objects.link(parent)
    wood=Mesh();leaves=Mesh()
    sides=[8,6,5][lod]
    if style<2:
        height=8.4 if style==0 else 8.6
        pts=[(0,0,0),(.04,-.02,.3),(-.06,.02,2),(.12,-.04,4.3),(.07,.04,6.4),(.02,.02,height)]
        radii=[.39,.28,.22,.16,.09,.018]
    elif style==2:
        height=8.0
        pts=[(0,0,0),(.10,0,.5),(.12,-.05,2.6),(-.20,.08,4.6),(.07,.12,6.3),(.4,.08,7.9)]
        radii=[.42,.29,.24,.17,.095,.022]
    else:
        height=7.4
        pts=[(0,0,0),(-.12,.02,.5),(.12,.05,1.6),(.48,.10,2.8),(.25,.04,4.1),(.78,.10,5.4),(1.02,.11,6.9)]
        radii=[.63,.47,.39,.32,.25,.15,.035]
    tube(wood,pts,radii,sides)
    for k in range([5,4,3][lod]):
        a=k*2.399+.3
        end=(math.cos(a)*(.80 if style<3 else 1.10),math.sin(a)*(.80 if style<3 else 1.1),.035)
        tube(wood,[(0,0,.32),(end[0]*.5,end[1]*.5,.16),end],[.20 if style<3 else .28,.12,.025],max(4,sides-2),a)
    if style==0:
        for k in range(8):
            z=1.45+k*.83;rad=2.15*(1-k/8)**.86+.12
            tier(leaves,z,rad,50+k,lod)
            if lod<2:
                for j in range(3):
                    a=j*math.tau/3+k*.93
                    tube(wood,[(0,0,z+.20),(math.cos(a)*rad*.6,math.sin(a)*rad*.6,z+.02),(math.cos(a)*rad*.94,math.sin(a)*rad*.94,z-.11)],[.075,.041,.012],max(4,sides-2))
        tier(leaves,8.02,.49,108,lod)
    elif style==1:
        for k in range(7):
            z=1.45+k*.94;rad=2.25*(1-k/7)**.82
            count=5
            mass(leaves,(.02,0,z+.12),(rad*.45+.08,rad*.45+.08,.64),[8,6,5][lod],[4,3,3][lod],700+k,1,facet=.06)
            for j in range(count):
                a=j*math.tau/count+k*.75+rr.uniform(-.12,.12)
                direction=Vector((math.cos(a),math.sin(a),0))
                start=Vector((.02,0,z+.30))
                end=direction*rad+Vector((0,0,z-.11))
                mid=start.lerp(end,.5)+Vector((0,0,-.12))
                tube(wood,[start,mid,end],[.083*(1-k*.065),.047,.012],max(4,sides-2))
                for t in ([.25,.64] if lod<2 else [.29,.65]):
                    c=start.lerp(end,t)
                    frond(leaves,c,direction+Vector((0,0,-.85)),rad*.56+.46,rad*.28+.17,[4,2,1][lod],1000+k*50+j*3+int(t*10))
        for j in range(5):
            a=j*math.tau/5
            frond(leaves,(.02,0,7.72),(math.cos(a)*.40,math.sin(a)*.40,.8),.82,.20,[4,2,1][lod],91+j)
    elif style==2:
        for k in range(13):
            z=2.8+k*.36
            a=k*2.399+rr.uniform(-.2,.2)
            radius=(1.9 if k<7 else 1.6)*(1-.032*k)*rr.uniform(.85,1.13)
            direction=Vector((math.cos(a),math.sin(a),0))
            base=Vector((.05,.03,z))
            mid=base+direction*radius*.54+Vector((0,0,.03))
            end=base+direction*radius+Vector((0,0,.48))
            tube(wood,[base,mid,end],[.115*(1-k*.05),.060,.018],sides)
            for j in range([5,4,2][lod]):
                t=.47+j*.12 if lod<2 else .66+j*.20
                origin=base.lerp(end,t)
                dd=direction+Vector((-math.sin(a),math.cos(a),0))*(j%2-.5)*.8+Vector((0,0,.42))
                length=1.12 if lod<2 else 1.55
                tip=origin+dd.normalized()*length
                tube(wood,[origin,tip],[.025,.006],4)
                card(leaves,origin,tip,.43 if lod<2 else .57,a+j*.93)
                if lod==0:card(leaves,origin+dd*.018,tip+Vector((0,0,.03)),.39,a+j*.93+1.57)
        top=Vector(pts[-1])
        for j in range(5):
            a=j*math.tau/5
            origin=top-Vector((0,0,.35))
            tip=top+Vector((math.cos(a)*.52,math.sin(a)*.52,.17))
            tube(wood,[origin,tip],[.02,.005],4)
            card(leaves,origin,tip,.34,a)
    else:
        # Asymmetric branching, one attached bough supporting each canopy group.
        for k in range(12):
            a=k*2.399+.4
            z=2.15+k*.26
            start=Vector((.30,.06,z))
            radius=rr.uniform(1.55,2.70)*(1-.016*k)
            d=Vector((math.cos(a),math.sin(a),0))
            mid=start+d*radius*.55+Vector((0,0,.65))
            end=start+d*radius+Vector((0,0,1.15+rr.random()*.65))
            tube(wood,[start,mid,end],[.17*(1-k*.035),.11,.025],sides)
            mass(leaves,end,(.98,.86,.84),[10,8,6][lod],[5,4,3][lod],k+230,3,facet=.10)
            edge_leaves(leaves,end,(.98,.86,.84),k+230,[10,5,0][lod])
            if lod<2:
                side=d.cross(Vector((0,0,1)))*.68
                twig=mid+side+Vector((0,0,.85))
                tube(wood,[mid,twig],[.065,.013],max(4,sides-2))
                mass(leaves,twig,(.76,.70,.74),[8,6,5][lod],[4,3,3][lod],k+310,3,facet=.1)
                edge_leaves(leaves,twig,(.76,.70,.74),k+310,5 if lod==0 else 0)
        mass(leaves,pts[-1],(1.23,1.04,.95),[10,8,6][lod],[5,4,3][lod],401,3,facet=.09)
    obw=wood.finish(f'SM_{name}_Bark_LOD{lod}',BARK,parent)
    obl=leaves.finish(f'SM_{name}_Foliage_LOD{lod}',NEEDLES if style==2 else FOLIAGE,parent)
    for p in obw.data.polygons:p.use_smooth=True
    for p in obl.data.polygons:p.use_smooth=style in [1,3]
    parent['style']=name;parent['lod']=lod
    bpy.context.view_layer.update()
    return parent,[obw,obl]


def select(obs):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs:ob.select_set(True);ob.hide_set(False)
    bpy.context.view_layer.objects.active=obs[0]


bark,foliage,needles=textures()
BARK=material('M_TreeBark',bark,(.41,.24,.12))
FOLIAGE=material('M_TreeCanopy',foliage,(.29,.46,.22))
FOLIAGE.use_backface_culling=False
NEEDLES=material('M_TreeNeedles_Cutout',needles,(.24,.42,.24),True)
names=['FacetedFir','PaintedFir','NaturalPine','OldOak']
titles=['块面冷杉','团簇冷杉','贴片自然松','团簇老橡树']
records=[];reports={};trees={}
for style,name in enumerate(names):
    record={'id':name,'title':titles[style],'alphaClip':style==2,'lods':[]}
    for lod in range(3):
        parent,obs=make(style,lod);trees[(style,lod)]=(parent,obs)
        report=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002)
        reports[f'{name}_LOD{lod}']=report
        print('QA',name,lod,report['pass'],report['errors'],flush=True)
        points=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
        dimensions=[round(max(p[k] for p in points)-min(p[k] for p in points),3) for k in range(3)]
        tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs)
        record['lods'].append({'level':lod,'triangles':tris,'dimensions':dimensions,'objects':2,'materials':2,'url':f'/models/trees/{name}_LOD{lod}.glb'})
        select([parent]+obs)
        bpy.ops.export_scene.gltf(filepath=str(OUT/f'{name}_LOD{lod}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
        if lod==0:
            for mode in ['material','wire']:
                review_shots.run(objects=[o.name for o in obs],out=str(OUT/f'{name}_{mode}.png'),views=['front','right','top','three_quarter'],mode=mode,res=320)
    selected=[]
    for lod in range(3):
        parent,obs=trees[(style,lod)];selected += [parent]+obs
    select(selected)
    bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
    records.append(record)

(OUT/'qa.json').write_text(json.dumps(reports,indent=2))
(WEB/'manifest.json').write_text(json.dumps({'version':1,'units':'meters','sourceAxes':'Blender Z up; runtime Y up','styles':records},indent=2,ensure_ascii=False))
(UNITY/'manifest.json').write_text((WEB/'manifest.json').read_text())
# Arrange the editable source as four columns, with lower LODs parked behind.
for (style,lod),(parent,obs) in trees.items():
    parent.location=(style*6.5,lod*9,0)
    parent.hide_render=lod>0
    for ob in obs:ob.hide_render=lod>0
for im in [bark,foliage,needles]:im.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'TreeStudies.blend'))
if any(not r['pass'] for r in reports.values()):
    raise RuntimeError('Asset QA needs repair; inspect output/tree-studies/qa.json')
print('TREE_STUDIES_COMPLETE',json.dumps(records),flush=True)

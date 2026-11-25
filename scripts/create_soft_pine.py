"""Add a rounded, scalloped pine study without rewriting the previous four assets."""
from pathlib import Path
import bpy, json, math, random
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
# Reuse modeling primitives only, before the original script's generation entry point.
source=(ROOT/'scripts/create_tree_studies.py').read_text().split('\nbark,foliage,needles=textures()')[0]
exec(compile(source, str(ROOT/'scripts/create_tree_studies.py'), 'exec'))
NAME='SoftPine'
n=512
y,x=np.mgrid[0:n,0:n]/(n-1)
# Deliberately broad color shapes; no noisy photo-texture or baked light/shadow.
brush=.012*np.sin(x*19+y*6)+.006*np.sin(x*77+y*13)
arr=np.ones((n,n,4),dtype=np.float32)
low=np.array([.39,.55,.16]);high=np.array([.69,.80,.36])
arr[:,:,:3]=low+(high-low)*(.24+.76*y[:,:,None])+brush[:,:,None]
canopy=save_texture('T_SoftPine_Canopy',arr)
arr=np.ones((n,n,4),dtype=np.float32)
grain=.032*np.sin(x*math.tau*12+np.sin(y*9)*.8)+.015*np.sin(x*math.tau*31+y*7)
arr[:,:,:3]=np.array([.53,.35,.19])*(.90+.10*y[:,:,None])+grain[:,:,None]
bark=save_texture('T_SoftPine_Bark',arr)
CANOPY=material('M_SoftPineCanopy',canopy,(.57,.72,.29))
BARK=material('M_SoftPineBark',bark,(.53,.35,.19))


def rounded_tier(mesh,base,radius,height,seed,lod):
    """Closed bell-shaped whorl, with a rounded lip and five irregular drooping lobes."""
    rr=random.Random(seed)
    segments=[28,18,10][lod]
    profile=[(.12,.96),(.25,.79),(.44,.59),(.65,.39),(.86,.24),(.98,.18),
             (1.0,.13),(.96,.085),(.74,.075),(.30,.13)]
    if lod==1:profile=[profile[i] for i in [0,1,2,3,4,5,7,9]]
    if lod==2:profile=[profile[i] for i in [0,2,3,5,7,9]]
    phase=rr.uniform(0,math.tau)
    center=Vector((.10*math.sin(base*.75),.06*math.sin(base),base))
    rows=[]
    def uv(i,j):return (i/segments,.10+.82*profile[j][1])
    for j,(rad,z) in enumerate(profile):
        row=[]
        for i in range(segments):
            a=i*math.tau/segments
            lobe=math.cos(a*5+phase)+.22*math.sin(a*3-phase)
            strength=(1-z)**.65
            r=radius*rad*(1+(.16*lobe+.026*math.sin(2*a+phase))*strength)
            zz=height*(z-.075*lobe*strength)
            # Top subtly leans as a living branch rather than a rotational cone.
            p=center+Vector((math.cos(a)*r+.10*z*z,math.sin(a)*r,zz))
            row.append(mesh.vert(p))
        rows.append(row)
    top=mesh.vert(center+Vector((.10,0,height*1.04)))
    bottom=mesh.vert(center+Vector((0,0,height*.15)))
    for i in range(segments):
        k=(i+1)%segments
        mesh.face([top,rows[0][i],rows[0][k]],[( (i+.5)/segments,.99),uv(i,0),uv(i+1,0)])
        for j in range(len(rows)-1):
            mesh.face([rows[j][i],rows[j+1][i],rows[j+1][k],rows[j][k]],
                      [uv(i,j),uv(i,j+1),uv(i+1,j+1),uv(i+1,j)])
        # Non-zero cap UVs, intentionally atlas-stacked.
        mesh.face([bottom,rows[-1][k],rows[-1][i]],[( (i+.5)/segments,.13),uv(i+1,len(rows)-1),uv(i,len(rows)-1)])


trees=[];record={'id':NAME,'title':'柔冠童话松','alphaClip':False,'lods':[]};reports={}
for lod in range(3):
    parent=bpy.data.objects.new(f'{NAME}_LOD{lod}',None);sc.collection.objects.link(parent)
    wood=Mesh();leaves=Mesh();sides=[10,7,5][lod]
    tube(wood,[(0,0,0),(.04,0,.4),(.08,.02,2.3),(-.02,.03,4.5),(-.02,.02,6.8),(.07,0,8.25)],
         [.38,.29,.23,.17,.085,.015],sides)
    for j in range([5,4,3][lod]):
        a=j*2.399
        end=Vector((math.cos(a)*.73,math.sin(a)*.73,.03))
        tube(wood,[(0,0,.25),end*.52+Vector((0,0,.12)),end],[.16,.095,.012],max(4,sides-2),a)
    specs=[(2.30,2.25,2.65),(3.30,1.98,2.42),(4.30,1.65,2.17),
           (5.25,1.32,1.91),(6.15,1.01,1.63),(7.02,.70,1.35)]
    for k,(z,r,h) in enumerate(specs):
        rounded_tier(leaves,z,r,h,172+k*11,lod)
    obw=wood.finish(f'SM_{NAME}_Bark_LOD{lod}',BARK,parent)
    obl=leaves.finish(f'SM_{NAME}_Foliage_LOD{lod}',CANOPY,parent)
    obs=[obw,obl]
    for ob in obs:
        for p in ob.data.polygons:p.use_smooth=True
    bpy.context.view_layer.update()
    result=qa_check.run(objects=[o.name for o in obs],profile='game',eps=.0005,tol=.002)
    reports[f'{NAME}_LOD{lod}']=result
    print('QA',lod,result['pass'],result['errors'],flush=True)
    points=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
    dimensions=[round(max(p[k] for p in points)-min(p[k] for p in points),3) for k in range(3)]
    triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs)
    record['lods'].append({'level':lod,'triangles':triangles,'dimensions':dimensions,'objects':2,'materials':2,'url':f'/models/trees/{NAME}_LOD{lod}.glb'})
    select([parent]+obs)
    bpy.ops.export_scene.gltf(filepath=str(OUT/f'{NAME}_LOD{lod}.glb'),export_format='GLB',use_selection=True,export_materials='EXPORT',export_extras=True)
    for mode in ['material','wire']:
        review_shots.run(objects=[o.name for o in obs],out=str(OUT/f'{NAME}_LOD{lod}_{mode}.png'),views=['front','right','top','three_quarter'],mode=mode,res=400)
    trees.append((parent,obs))
if any(not r['pass'] for r in reports.values()):
    (OUT/'soft-pine-qa.json').write_text(json.dumps(reports,indent=2))
    raise RuntimeError('Soft pine QA failed')
selected=[o for parent,obs in trees for o in [parent]+obs]
select(selected)
bpy.ops.export_scene.fbx(filepath=str(UNITY/'Models'/f'{NAME}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
for im in [canopy,bark]:im.pack()
for lod,(parent,obs) in enumerate(trees):
    parent.location=(lod*6.5,0,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SoftPine.blend'))
manifest=json.loads((WEB/'manifest.json').read_text())
manifest['styles']=[s for s in manifest['styles'] if s['id']!=NAME]+[record]
(WEB/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False))
qa=json.loads((OUT/'qa.json').read_text());qa.update(reports)
(OUT/'qa.json').write_text(json.dumps(qa,indent=2))
(OUT/'soft-pine-qa.json').write_text(json.dumps(reports,indent=2))
print('SOFT_PINE_COMPLETE',json.dumps(record),flush=True)

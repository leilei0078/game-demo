"""Inset, glazed timber windows with real wall recesses and supported stone sills."""
import math
import bpy
import bmesh
from mathutils import Matrix, Vector


def refine_windows():
    collection=next(c for c in bpy.data.collections if c.name.startswith('02'))
    wall=bpy.data.objects['Warm limewashed walls']
    # The saved wall is reused on subsequent runs; the pocket cutters are idempotent.
    old_prefixes=('Dark window recess','Amber panes','Window mullion','Window crossbar','Window sill',
        'Side window frame','Side amber glass','Side window mullion','Side window crossbar','Side window sill','SM_Window_')
    for obj in list(collection.objects):
        if obj.name.startswith(old_prefixes):
            bpy.data.objects.remove(obj,do_unlink=True)
    def material(name,color,roughness,emission=0):
        mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.diffuse_color=(*color,1)
        shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        shader.inputs['Base Color'].default_value=(*color,1)
        shader.inputs['Roughness'].default_value=roughness
        shader.inputs['Metallic'].default_value=0
        shader.inputs['Emission Color'].default_value=(.83,.37,.085,1)
        shader.inputs['Emission Strength'].default_value=emission
        return mat
    oak=material('M_Window_Weathered_oak',(.235,.147,.073),.85)
    cut_oak=material('M_Window_Sash_oak',(.365,.245,.131),.72)
    recess=material('M_Window_Dark_interior',(.027,.033,.029),.98)
    linen=material('M_Window_Linen',(.265,.226,.145),.95,.005)
    stone=material('M_Window_Sill_limestone',(.446,.455,.402),.91)
    iron=material('M_Window_Blackened_iron',(.043,.050,.045),.62)
    glass=[]
    for index,color in enumerate([(.105,.160,.158),(.127,.177,.163),(.096,.145,.151),(.140,.181,.166)]):
        mat=material(f'M_Window_Glass_{index}',color,.26+index*.025,.05)
        shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        shader.inputs['IOR'].default_value=1.48
        shader.inputs['Transmission Weight'].default_value=.38
        shader.inputs['Coat Weight'].default_value=.12
        shader.inputs['Coat Roughness'].default_value=.22
        glass.append(mat)
    assemblies={}
    for key,origin,angle,width,height,columns in [
        ('front_left',(-3.83,.025,2.14),0,.52,.76,2),
        ('front_right',(-1.47,.025,2.14),0,.52,.76,2),
        ('side',(-.94,1.90,2.12),math.pi/2,.86,.98,3),
    ]:
        rotation=Matrix.Rotation(angle,4,'Z')
        transform=Matrix.Translation(Vector(origin))@rotation
        created=[]
        def mesh(label,vertices,faces,mat,role,edge=0):
            name=f'SM_Window_{key}_{label}'
            data=bpy.data.meshes.new(name)
            data.from_pydata(vertices,[],faces)
            bm=bmesh.new()
            bm.from_mesh(data)
            bmesh.ops.recalc_face_normals(bm,faces=bm.faces[:])
            bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>4])
            bm.to_mesh(data)
            bm.free()
            if role=='glass':
                uv=data.uv_layers.new(name='Pane_coordinates')
                left,right=min(v[0] for v in vertices),max(v[0] for v in vertices)
                bottom,top=min(v[2] for v in vertices),max(v[2] for v in vertices)
                for loop in data.loops:
                    p=data.vertices[loop.vertex_index].co
                    uv.data[loop.index].uv=((p.x-left)/(right-left),(p.z-bottom)/(top-bottom))
            obj=bpy.data.objects.new(name,data)
            collection.objects.link(obj)
            obj.matrix_world=transform
            data.materials.append(mat)
            if edge:
                bevel=obj.modifiers.new('Small crafted edge','BEVEL')
                bevel.width=edge
                bevel.segments=1
                bevel.use_clamp_overlap=True
            obj['asset_group']='window_'+key
            obj['window_role']=role
            created.append(obj)
            return obj
        def block(label,position,size,mat,role,edge=.004):
            sx,sy,sz=size
            u,d,z=position
            vertices=[(u+x*sx/2,d+y*sy/2,z+v*sz/2) for x,y,v in [
                (-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
            return mesh(label,vertices,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat,role,edge)
        def lattice(label,outer_width,outer_height,border,division,front,back,mat,role):
            # Extruded 2D grid with one closed boundary around every pane; no overlapping bars.
            xs=[-outer_width/2,-outer_width/2+border]
            for i in range(1,division):
                center=-outer_width/2+border+(outer_width-2*border)*i/division
                xs.extend([center-.014,center+.014])
            xs.extend([outer_width/2-border,outer_width/2])
            zs=[-outer_height/2,-outer_height/2+border,-.014,.014,outer_height/2-border,outer_height/2]
            if division==1:
                zs=[-outer_height/2,-outer_height/2+border,outer_height/2-border,outer_height/2]
            nx,nz=len(xs)-1,len(zs)-1
            occupied={(i,j) for i in range(nx) for j in range(nz)
                      if i in (0,nx-1) or j in (0,nz-1) or (division>1 and (i%2==0 or j==2))}
            vertices=[];faces=[];lookup={}
            def vertex(x,d,z):
                point=(x,d,z)
                if point not in lookup:
                    lookup[point]=len(vertices)
                    vertices.append(point)
                return lookup[point]
            for i,j in sorted(occupied):
                x0,x1,z0,z1=xs[i],xs[i+1],zs[j],zs[j+1]
                for d in (front,back):
                    faces.append(tuple(vertex(x,d,z) for x,z in [(x0,z0),(x1,z0),(x1,z1),(x0,z1)]))
                for neighbor,corners in [((i-1,j),[(x0,z0),(x0,z1)]),((i+1,j),[(x1,z0),(x1,z1)]),
                                         ((i,j-1),[(x0,z0),(x1,z0)]),((i,j+1),[(x0,z1),(x1,z1)])]:
                    if neighbor not in occupied:
                        a,b=corners
                        faces.append((vertex(a[0],front,a[1]),vertex(b[0],front,b[1]),
                                      vertex(b[0],back,b[1]),vertex(a[0],back,a[1])))
            obj=mesh(label,vertices,faces,mat,role,.004)
            holes=[(xs[i],xs[i+1],zs[j],zs[j+1]) for i in range(nx) for j in range(nz) if (i,j) not in occupied]
            return obj,holes
        # Actual cavity in the plaster, deeper than the pane and its interior backing.
        if wall.get('window_pocket_revision') != 'inset-5':
            cutter=block('temporary_pocket',(0,.07,0),(width+.004,.38,height+.004),oak,'cutter',0)
            boolean=wall.modifiers.new('Window pocket '+key,'BOOLEAN')
            boolean.operation='DIFFERENCE'
            boolean.solver='EXACT'
            boolean.object=cutter
            with bpy.context.temp_override(object=wall,active_object=wall,selected_objects=[wall],selected_editable_objects=[wall]):
                bpy.ops.object.modifier_apply(modifier=boolean.name)
            created.remove(cutter)
            bpy.data.objects.remove(cutter,do_unlink=True)
        # Main jamb is a ring; the sash grid sits 10 cm deeper than its front edge.
        lattice('outer_jamb',width+.16,height+.16,.08,1,-.09,.20,oak,'jamb')
        sash,holes=lattice('sash_and_muntins',width+.004,height+.004,.042,columns,.018,.108,cut_oak,'sash')
        block('interior_back',(0,.196,0),(width+.012,.018,height+.012),recess,'interior',.001)
        for index,(left,right,bottom,top) in enumerate(holes):
            block(f'pane_{index}',((left+right)/2,.108,(bottom+top)/2),
                (right-left+.010,.010,top-bottom+.010),glass[(index+(key=='side'))%4],'glass',.001)
        # Narrow folded curtains provide an interior silhouette behind the glass.
        for side in (-1,1):
            for fold in range(2):
                u=side*(width/2-.040-fold*.026)
                block(f'curtain_{side}_{fold}',(u,.169+fold*.008,.025),(.032,.024,height-.015),linen,'curtain',.003)
        # Sloped stone sill: supported inside the wall, with an outward drip edge.
        w=width+.28
        lo=-height/2
        vertices=[(-w/2,-.23,lo-.16),(w/2,-.23,lo-.16),(w/2,.115,lo-.16),(-w/2,.115,lo-.16),
                  (-w/2,-.23,lo-.095),(w/2,-.23,lo-.095),(w/2,.115,lo-.070),(-w/2,.115,lo-.070)]
        mesh('sloped_stone_sill',vertices,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],stone,'sill',.008)
        block('latch_plate',(.012,.015,-.012),(.043,.020,.057),iron,'latch',.003)
        block('latch_handle',(.035,-.0005,-.014),(.068,.016,.014),iron,'latch',.003)
        assemblies[key]=created

    # Reframe the side elevation so its structural timbers do not run through the opening.
    for obj in collection.objects:
        if obj.name.startswith('Side upright') and obj.location.y>1.5:
            obj.location.y=2.50
        if obj.name.startswith('Side diagonal timber'):
            a,b=Vector((-.85,1.01,1.02)),Vector((-.85,1.45,1.53))
            obj.location=(a+b)/2
            obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
            # Original beam's longest axis is local Z, with applied dimensions.
            extent=max(v.co.z for v in obj.data.vertices)-min(v.co.z for v in obj.data.vertices)
            for vertex in obj.data.vertices:
                vertex.co.z *= (b-a).length/extent
    bpy.context.view_layer.update()
    wall['window_pocket_revision']='inset-5'
    return assemblies,wall

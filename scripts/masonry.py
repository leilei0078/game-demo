"""Reusable, tight-jointed running-bond cottage plinth (Blender data API)."""
import random
import bpy


def build_foundation(collection, cx=-2.65, cy=1.55):
    rng = random.Random(108)
    created = []

    def material(name, color):
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.diffuse_color = (*color, 1)
        p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        p.inputs['Base Color'].default_value = (*color, 1)
        p.inputs['Roughness'].default_value = .91
        return m

    limestone = [material('M_Limestone_%02d' % i, color) for i, color in enumerate([
        (.445,.455,.390), (.475,.478,.411), (.425,.444,.389),
        (.460,.468,.408), (.489,.483,.415), (.436,.453,.401),
    ])]
    mortar = material('M_Recessed_lime_mortar', (.305,.321,.275))
    cap = material('M_Door_threshold_limestone', (.505,.508,.440))

    def block(name, pos, size, mat, edge=.009):
        # Mesh vertices carry the dimensions; object transforms stay applied.
        sx, sy, sz = size
        vertices = [(x*sx/2,y*sy/2,z*sz/2) for x,y,z in [
            (-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
            (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1),
        ]]
        faces = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        data = bpy.data.meshes.new(name)
        data.from_pydata(vertices, [], faces)
        data.update()
        obj = bpy.data.objects.new(name, data)
        collection.objects.link(obj)
        obj.location = pos
        data.materials.append(mat)
        obj.color = mat.diffuse_color
        if edge:
            b = obj.modifiers.new('Small dressed stone arris', 'BEVEL')
            b.width = edge
            b.segments = 1
            b.use_clamp_overlap = True
            b.harden_normals = True
            n = obj.modifiers.new('Weighted face normals', 'WEIGHTED_NORMAL')
            n.keep_sharp = True
        obj['asset_part'] = 'cottage_masonry'
        created.append(obj)
        return obj

    # Mortar lies 15 mm behind the dressed stone faces; there are no open cracks.
    xmin,xmax=cx-1.85,cx+1.85
    ymin,ymax=cy-1.71,cy+1.70
    block('SM_Foundation_mortar_core', (cx,cy-.005,.534), (3.67,3.38,.530), mortar, .003)
    gap,depth=.008,.24

    def course(label, a, b, fixed, axis, row, count):
        weights = [rng.uniform(.85,1.16) for _ in range(count)]
        factor = (b-a)/sum(weights)
        cursor = a
        for i,w in enumerate(weights):
            pitch=w*factor
            center=cursor+pitch/2
            width=pitch-gap
            # Closely matched stones, a restrained chamfer and no random rotations.
            z=.272+row*.274+.266/2
            color=limestone[(i+row*2+len(label))%len(limestone)]
            if axis=='X':
                pos=(center,fixed,z); size=(width,depth,.266)
            else:
                pos=(fixed,center,z); size=(depth,width,.266)
            block(f'SM_Foundation_{label}_R{row+1}_{i+1:02d}',pos,size,color,.008+rng.random()*.002)
            cursor+=pitch

    # Alternating corner orientation makes the two courses lock together.
    course('Front',xmin,xmax,ymin+depth/2,'X',0,7)
    course('Back',xmin,xmax,ymax-depth/2,'X',0,7)
    course('Left',ymin+depth+gap,ymax-depth-gap,xmin+depth/2,'Y',0,6)
    course('Right',ymin+depth+gap,ymax-depth-gap,xmax-depth/2,'Y',0,6)
    course('Front',xmin+depth+gap,xmax-depth-gap,ymin+depth/2,'X',1,6)
    course('Back',xmin+depth+gap,xmax-depth-gap,ymax-depth/2,'X',1,6)
    course('Left',ymin,ymax,xmin+depth/2,'Y',1,6)
    course('Right',ymin,ymax,xmax-depth/2,'Y',1,6)
    block('SM_Door_threshold', (cx,cy-1.73,.780), (1.20,.45,.14), cap, .012)
    bpy.context.view_layer.update()
    return created

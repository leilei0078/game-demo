"""Shared, idempotent layout correction for the authored clearing (metres)."""
from itertools import combinations
import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

REVISION = 'clearance-3'


def bounds(objects):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in objects:
        evaluated = obj.evaluated_get(dg)
        points.extend(evaluated.matrix_world @ Vector(p) for p in evaluated.bound_box)
    return [[fn(p[i] for p in points) for i in range(3)] for fn in (min, max)]


def groups():
    result = {}
    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH' and obj.get('asset_group'):
            result.setdefault(obj['asset_group'], []).append(obj)
    return result


def refine_layout():
    if bpy.context.scene.get('clearance_revision') == REVISION:
        return groups()
    barrel_centers = [Vector((-.54, .65)), Vector((-.38, 1.41))]
    pine_centers = [Vector(p) for p in [(-5.15,2.75),(-4.5,3.9),(-.4,4.1),(1.3,3.8)]]
    assets = {}
    bpy.context.view_layer.update()
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        group = None
        if obj.name.startswith(('Curved oak barrel stave', 'Barrel ')):
            lo, hi = bounds([obj])
            center = Vector(((lo[0]+hi[0])/2, (lo[1]+hi[1])/2))
            group = 'barrel_' + str(min(range(2), key=lambda i: (center-barrel_centers[i]).length))
        elif obj.name.startswith(('Supply crate', 'Crate ')):
            group = 'crate_stack'
        elif obj.name.startswith('Stacked split firewood'):
            group = 'firewood'
        elif obj.name.startswith(('Angular pine trunk', 'Layered faceted pine crown')):
            center = obj.location.xy
            nearest = min(range(4), key=lambda i: (center-pine_centers[i]).length)
            if (center-pine_centers[nearest]).length < .15:
                group = 'pine_' + str(nearest)
        elif obj.name.startswith(('Mossy granite boulder', 'Flat moss cap')):
            for index, center in enumerate((Vector((.8,3.0)), Vector((2.3,3.4)), Vector((-4.8,3.4)))):
                if (obj.location.xy-center).length < .12:
                    group = 'border_rock_' + str(index)
        elif obj.name.startswith('Low woodland shrub'):
            if -1.6 < obj.location.x < -.1 and obj.location.y > 3:
                group = 'back_shrubs'
            elif .9 < obj.location.x < 2.1 and 3 < obj.location.y < 3.7:
                group = 'fence_shrubs'
        if group:
            obj['asset_group'] = group
            assets.setdefault(group, []).append(obj)

    offsets = {
        'barrel_0': (.38,-.10,-.01), 'barrel_1': (.06,0,-.01),
        'crate_stack': (0,.20,-.01), 'firewood': (1.55,-.70,-.057),
        'pine_0': (-.50,-.40,-.01), 'pine_1': (1.0,.15,-.01),
        'pine_2': (.20,.55,-.01), 'pine_3': (0,.35,-.01),
        'border_rock_0': (0,.80,0), 'border_rock_1': (0,.60,0),
        'border_rock_2': (-.65,0,0),
        'back_shrubs': (0,.40,0), 'fence_shrubs': (0,.40,0),
    }
    crown_width = {'pine_0': .70, 'pine_1': .65, 'pine_2': .70, 'pine_3': .90}
    for group, objects in assets.items():
        for obj in objects:
            if group in crown_width and obj.name.startswith('Layered faceted'):
                # Narrow crowns in mesh coordinates; retain applied object transforms.
                width = crown_width[group]
                obj.data.transform(Matrix.Diagonal((width,width,1,1)))
            obj.location += Vector(offsets[group])
            if any(abs(v-1) > 1e-6 for v in obj.scale):
                obj.data.transform(Matrix.Diagonal((*obj.scale,1)))
                obj.scale = (1,1,1)
            if obj.name.startswith('Barrel lid seam'):
                # Seat the dark seam inserts into the lid; avoid near-coplanar undersides.
                obj.location.z -= .003
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            caps = [f for f in bm.faces if len(f.verts) > 4]
            if caps:
                bmesh.ops.triangulate(bm,faces=caps)
                bm.to_mesh(obj.data)
            bm.free()

    bpy.context.scene['clearance_revision'] = REVISION
    bpy.context.view_layer.update()
    return assets


def bvh(objects):
    verts, faces = [], []
    dg = bpy.context.evaluated_depsgraph_get()
    for obj in objects:
        evaluated = obj.evaluated_get(dg)
        mesh = evaluated.to_mesh()
        offset = len(verts)
        verts.extend(evaluated.matrix_world @ v.co for v in mesh.vertices)
        faces.extend(tuple(offset+i for i in p.vertices) for p in mesh.polygons)
        evaluated.to_mesh_clear()
    return BVHTree.FromPolygons(verts, faces)


def check_clearance(assets):
    """Separate assets must have conservative AABB gaps; foliage uses mesh tests."""
    cottage = [o for o in bpy.context.scene.objects if o.type == 'MESH' and
               any(c.name.startswith('02') for c in o.users_collection)]
    fence = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith(
             ('Split oak fence', 'Pointed fence cap', 'Fence crossrail', 'Fence diagonal'))
             and bounds([o])[0][1] > 2.5]
    boxes = {key: bounds(obs) for key, obs in assets.items()}
    boxes['cottage_base'] = [[-4.5,-.16,.27],[-.8,3.25,1.5]]
    boxes['back_fence'] = bounds(fence)
    checks = []
    props = ['barrel_0','barrel_1','crate_stack','firewood']
    for first, second in list(combinations(props,2)) + [(p,q) for p in props for q in ('cottage_base','back_fence')]:
        a,b = boxes[first], boxes[second]
        separation = max(max(a[0][i]-b[1][i],b[0][i]-a[1][i]) for i in range(3))
        checks.append({'assets':[first,second], 'bounding_box_gap_m':round(separation,4), 'pass':separation >= .06})
    house_bvh, fence_bvh = bvh(cottage), bvh(fence)
    for name in assets:
        if name.startswith(('pine_','border_rock_','back_shrubs','fence_shrubs')):
            tree = bvh(assets[name])
            for label, target in [('cottage',house_bvh),('back_fence',fence_bvh)]:
                count = len(tree.overlap(target))
                checks.append({'assets':[name,label], 'intersecting_triangle_pairs':count, 'pass':count == 0})
    terrain = bvh([bpy.data.objects['Triangulated mossy clearing']])
    for name in props + [key for key in assets if key.startswith('pine_')]:
        objects = assets[name]
        if name.startswith('pine_'):
            objects = [o for o in objects if o.name.startswith('Angular pine trunk')]
        lo,hi = bounds(objects)
        supported = all(terrain.ray_cast(Vector((x,y,1)),Vector((0,0,-1)),2)[0] is not None
                        for x,y in [((lo[0]+hi[0])/2,(lo[1]+hi[1])/2),
                                    (lo[0],lo[1]),(lo[0],hi[1]),(hi[0],lo[1]),(hi[0],hi[1])])
        checks.append({'asset':name,'terrain_support_at_center_and_corners':supported,'pass':supported})
    return {'pass': all(c['pass'] for c in checks), 'checks':checks, 'bounds_m':boxes,
            'scope':'Loose cottage supplies, four adjacent pines, three border rocks, and nearby shrubs. Structural joins, tree crown layers and supported stacks are intentional. This is not a whole-scene collision guarantee.'}

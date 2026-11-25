"""Original low-poly gameplay kit. Run with Blender --background --python this_file."""
import bpy
import bmesh
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/play'
OUT.mkdir(parents=True, exist_ok=True)
DEST = ROOT / 'public/models/play'
DEST.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, '/Users/tom-lei/.codex/skills/blender-asset-qa/scripts')
import qa_check
import review_shots

# A separate file/process; never reset the user's live Blender session.
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def material(name, color, metal=0, rough=.8):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Metallic'].default_value = metal
    return mat

steel = material('M_Blued_steel', (.19, .27, .32), 1, .54)
edge = material('M_Burnished_edges', (.48, .57, .58), 1, .46)
cloth = material('M_Traveller_teal', (.025, .24, .22))
leather = material('M_Chestnut_leather', (.18, .075, .035))
gold = material('M_Brass', (.6, .36, .09), 1, .45)
dark = material('M_Visor_shadow', (.018, .027, .028))
fur = material('M_Boar_umber', (.22, .12, .065))
snout = material('M_Boar_muzzle', (.32, .18, .12))
ivory = material('M_Worn_ivory', (.74, .68, .47))
rock = material('M_Mossy_slate', (.20, .27, .26))
oremat = material('M_Copper_vein', (.47, .27, .13), 1, .65)
wood = material('M_Fresh_timber', (.47, .29, .12))
bark = material('M_Bark', (.17, .10, .043))
leaf = material('M_Cedar_green', (.12, .25, .12))

assets = {}
active = None
def asset(name):
    global active
    active = bpy.data.collections.new(name)
    scene.collection.children.link(active)
    assets[name] = []

def finish(obj, name, mat, pivot=None):
    obj.name = name
    for c in list(obj.users_collection): c.objects.unlink(obj)
    active.objects.link(obj)
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if pivot is not None:
        delta = obj.location - Vector(pivot)
        for v in obj.data.vertices: v.co += delta
        obj.location = pivot
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    assets[active.name].append(obj)
    obj.select_set(False)
    return obj

def box(name, pos, size, mat, bevel=.025, pivot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Rounded worn corners', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj, name, mat, pivot)

def ico(name, pos, size, mat, sub=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    return finish(obj, name, mat)

def cone(name, pos, radius, height, mat, top=0, vertices=8):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=top, depth=height, location=pos)
    return finish(bpy.context.object, name, mat)

asset('traveller')
box('Body', (0, 0, 1.03), (.58, .35, .62), cloth, .055)
box('Breastplate', (0, -.15, 1.13), (.54, .12, .4), steel, .04)
box('Belt', (0, -.012, .85), (.61, .39, .085), leather, .018)
box('Buckle', (0, -.22, .85), (.13, .04, .085), gold, .012)
box('Cape', (0, .19, .99), (.53, .10, .57), cloth, .035)
box('Head', (0, 0, 1.52), (.38, .34, .38), edge, .065)
box('Visor', (0, -.178, 1.52), (.30, .035, .105), dark, .012)
box('Noseguard', (0, -.204, 1.51), (.045, .04, .22), gold, .008)
for side, x in [('L', -.38), ('R', .38)]:
    box('Arm' + side, (x, -.015, 1.05), (.23, .26, .49), steel, .045, (x, 0, 1.29))
    box('Leg' + side, (x * .48, 0, .49), (.23, .26, .51), leather, .025, (x * .48, 0, .75))
    box('Boot' + side, (x * .48, -.065, .14), (.26, .40, .28), steel, .035)
box('Blade', (.38, -.11, .40), (.09, .055, .68), edge, .012)
box('Crossguard', (.38, -.11, .76), (.32, .10, .065), gold, .015)
box('Grip', (.38, -.11, .83), (.07, .065, .14), leather, .01)
box('Shield', (-.48, -.16, 1.04), (.12, .47, .54), cloth, .055)
ico('ShieldBoss', (-.55, -.16, 1.04), (.10, .13, .13), gold, 1)

asset('boar')
ico('BoarBody', (0, .08, .62), (.40, .64, .39), fur, 2)
ico('BoarHead', (0, -.48, .56), (.29, .34, .28), fur, 1)
box('Snout', (0, -.76, .47), (.34, .21, .18), snout, .04)
for s in [-1, 1]:
    ico('Ear' + str(s), (s * .22, -.36, .80), (.15, .14, .22), fur)
    ico('Eye' + str(s), (s * .185, -.59, .64), (.045, .05, .045), dark)
    ico('Tusk' + str(s), (s * .17, -.69, .44), (.07, .12, .17), ivory)
    for y in [-.32, .43]:
        box('Hoof' + str(s) + str(y), (s * .24, y, .23), (.16, .22, .46), dark, .025)
for i in range(4):
    ico('Bristle' + str(i), (0, -.08 + i * .17, .91), (.10, .13, .15), dark)

asset('copper')
ico('OreBoulder', (0, 0, .55), (.76, .62, .55), rock, 1)
for i in range(5):
    a = i * 2.4
    ico('Copper' + str(i), (.42 * math.cos(a), .35 * math.sin(a), .82 + .06 * (i % 2)), (.22, .19, .17), oremat)

asset('cedar')
cone('CedarTrunk', (0, 0, .94), .21, 1.88, bark, .15)
cone('CedarCrownLow', (0, 0, 1.72), .92, 1.60, leaf, .12, 9)
cone('CedarCrownMid', (0, 0, 2.45), .69, 1.58, leaf, .06, 9)
cone('CedarCrownTop', (0, 0, 3.07), .44, 1.40, leaf, .008, 9)

asset('stump')
cone('StumpBark', (0, 0, .23), .29, .46, bark, .23)
cone('CutEnd', (0, 0, .462), .208, .025, wood, .208)

reports = {}
for name, objects in assets.items():
    # Every component is a closed, flat-color mesh; no image textures or UVs are used.
    individual = {o.name: qa_check.run(objects=[o.name], uv=False) for o in objects}
    assembled = qa_check.run(objects=[o.name for o in objects], uv=False, eps=.0002)
    reports[name] = {'components': individual, 'assembly': assembled,
                     'triangles': sum(len(o.data.polygons) for o in objects),
                     'dimensions_m': [round(max((o.matrix_world @ Vector(c))[i] for o in objects for c in o.bound_box) - min((o.matrix_world @ Vector(c))[i] for o in objects for c in o.bound_box), 3) for i in range(3)]}
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(DEST / (name + '.glb')), use_selection=True,
        export_format='GLB', export_apply=True, export_yup=True, export_animations=False)

scene.render.engine = 'BLENDER_WORKBENCH'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'WayfarerGameplay.blend'))
(OUT / 'qa.json').write_text(json.dumps(reports, indent=2))
for name in ['traveller', 'boar', 'copper', 'cedar']:
    for mode in ['material', 'wire']:
        review_shots.run(objects=[o.name for o in assets[name]], out=str(OUT / (name + '-' + mode + '.png')),
                         views=['front', 'right', 'top', 'three_quarter'], mode=mode, res=280)
print('PLAY_ASSETS', json.dumps({n: {'tris': r['triangles'], 'errors': r['assembly']['errors']} for n, r in reports.items()}))

"""Fresh Blender background process; writes only output/wood-fence."""
from pathlib import Path
import json
import math
import random
import sys
import bpy
import bmesh
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/wood-fence'
for folder in (OUT, OUT / 'textures', OUT / 'exports', OUT / 'qa', OUT / 'preview'):
    folder.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / 'scripts'))
from wood_fence_atlas import make_atlas
SKILLS = Path.home() / '.codex/skills'
sys.path.insert(0, str(SKILLS / 'blender-asset-qa/scripts'))
import qa_check
import review_shots
sys.path.insert(0, str(SKILLS / 'blender-scene-setup/scripts'))
import scene_audit
if bpy.data.filepath:
    raise RuntimeError('Use a fresh background process, not an existing user scene')
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for col in list(bpy.data.collections):
    bpy.data.collections.remove(col)
sc = bpy.context.scene
sc.unit_settings.system = 'METRIC'
sc.unit_settings.scale_length = 1.0
sc.unit_settings.length_unit = 'METERS'


def collection(name):
    col = bpy.data.collections.new(name)
    sc.collection.children.link(col)
    return col


SOURCE = collection('ASSET_WoodFence_Modular')
DISPLAY = collection('ASSET_WoodFence_Assembly')
STAGE = collection('LD_Presentation')
MAT = make_atlas(OUT / 'textures')


class MeshBuilder:
    def __init__(self):
        self.verts, self.faces, self.uv = [], [], []

    def face(self, indices, coords):
        self.faces.append(indices)
        self.uv.append(coords)

    def timber(self, start, end, width, depth, tile, seed, irregular=.008):
        rng = random.Random(seed)
        start, end = Vector(start), Vector(end)
        axis = (end - start).normalized()
        u = Vector((1, 0, 0)) if abs(axis.z) > .8 else Vector((0, 0, 1))
        v = axis.cross(u).normalized()
        u = v.cross(axis).normalized()
        c = min(width, depth) * .15
        cross = [(-width / 2 + c, -depth / 2), (width / 2 - c, -depth / 2),
                 (width / 2, -depth / 2 + c), (width / 2, depth / 2 - c),
                 (width / 2 - c, depth / 2), (-width / 2 + c, depth / 2),
                 (-width / 2, depth / 2 - c), (-width / 2, -depth / 2 + c)]
        ts = [0, .035, .38, .77, .965, 1]
        base = len(self.verts)
        for ring, t in enumerate(ts):
            drift = 0 if ring in (0, len(ts) - 1) else math.sin(t * math.pi)
            center = start.lerp(end, t) + u * rng.uniform(-irregular, irregular) * drift
            center += v * rng.uniform(-irregular * .35, irregular * .35) * drift
            taper = .91 if ring in (0, len(ts) - 1) else 1
            for x, y in cross:
                cut = rng.uniform(-irregular, irregular) if ring == len(ts) - 1 else 0
                if ring == len(ts) - 1 and abs(axis.z) > .8:
                    cut -= abs(x / width) * irregular * 2
                p = center + u * x * taper + v * y * taper + axis * cut
                self.verts.append(tuple(p))
        for ring in range(len(ts) - 1):
            for j in range(8):
                j2 = (j + 1) % 8
                x0, x1 = ((.055, .945) if j in (0, 4) else (.17, .74))
                t0, t1 = .008 + ts[ring] * .855, .008 + ts[ring + 1] * .855
                self.face([base + ring * 8 + j, base + ring * 8 + j2,
                           base + (ring + 1) * 8 + j2, base + (ring + 1) * 8 + j],
                          [((tile + x0) / 8, t0), ((tile + x1) / 8, t0),
                           ((tile + x1) / 8, t1), ((tile + x0) / 8, t1)])
        cap_uv = [((tile + .5 + x / width * .82) / 8, .9375 + y / depth * .102) for x, y in cross]
        for ring, reverse in [(0, True), (len(ts) - 1, False)]:
            ids = [base + ring * 8 + j for j in range(8)]
            for j in range(1, 7):
                ix = [0, j, j + 1]
                if reverse:
                    ix.reverse()
                self.face([ids[k] for k in ix], [cap_uv[k] for k in ix])

    def nail(self, center):
        base, n = len(self.verts), 6
        center = Vector(center)
        for depth, radius in [(0, .0115), (-.0055, .0125), (-.008, .008)]:
            for j in range(n):
                a = j * math.tau / n + .2
                self.verts.append(tuple(center + Vector((math.cos(a) * radius, depth, math.sin(a) * radius))))

        def uv(index):
            a = index % n * math.tau / n + .2
            return (.9375 + math.cos(a) * .035, .9375 + math.sin(a) * .035)

        for ring in range(2):
            for j in range(n):
                ids = [ring * n + j, ring * n + (j + 1) % n,
                       (ring + 1) * n + (j + 1) % n, (ring + 1) * n + j]
                coords = [(.90 + j % 3 * .02, .90 + ring * .02),
                          (.915 + j % 3 * .02, .90 + ring * .02),
                          (.915 + j % 3 * .02, .92 + ring * .02),
                          (.90 + j % 3 * .02, .92 + ring * .02)]
                self.face([base + i for i in ids], coords)
        for ring in [0, 2]:
            for j in range(1, n - 1):
                ix = [ring * n, ring * n + j, ring * n + j + 1]
                self.face([base + i for i in ix], [uv(i) for i in ix])

    def object(self, name, col):
        me = bpy.data.meshes.new(name + '_Mesh')
        me.from_pydata(self.verts, [], self.faces)
        me.materials.append(MAT)
        uv = me.uv_layers.new(name='UV0_Atlas')
        for poly, coords in zip(me.polygons, self.uv):
            for index, coord in zip(poly.loop_indices, coords):
                uv.data[index].uv = coord
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.to_mesh(me)
        bm.free()
        me.update()
        ob = bpy.data.objects.new(name, me)
        col.objects.link(ob)
        ob['nominal_grid_step_m'] = 1.0
        ob['uv_note'] = 'UV0 intentionally reuses wood atlas. Generate UV2 for baked lighting.'
        ob.asset_mark()
        ob.asset_data.description = 'Hewn wooden fence, metric, one shared PBR material'
        for tag in ('wood', 'fence', 'modular', 'game'):
            ob.asset_data.tags.new(tag)
        return ob


def panel(variant):
    builder = MeshBuilder()
    for index, z in enumerate((.37, .84)):
        builder.timber((-.425, .025, z), (.425, .025, z + .004),
                       .11, .09, 4 + index, 800 + index, .003)
    heights = ([1.105, 1.18, 1.135, 1.205, 1.085] if variant == 'A'
               else [1.16, 1.095, 1.205, 1.125, 1.175])
    rng = random.Random(430 if variant == 'A' else 810)
    for k, h in enumerate(heights):
        x = (k - 2) * .161
        lean = rng.uniform(-.014, .014)
        y = -.041 + rng.uniform(-.002, .002)
        thickness = .069
        start = Vector((x, y, .055 + rng.uniform(0, .023)))
        end = Vector((x + lean, y + rng.uniform(-.003, .003), h))
        builder.timber(start, end, .137 + rng.uniform(-.009, .008), thickness,
                       (k + (2 if variant == 'B' else 0)) % 4,
                       40 + k + (50 if variant == 'B' else 0), .009)
        for z in (.37, .84):
            t = (z - start.z) / (end.z - start.z)
            center = start.lerp(end, t)
            builder.nail((center.x, center.y - thickness / 2 + .0025, z + .002))
    return builder.object('SM_WoodFence_Panel' + variant, SOURCE)


panel_a, panel_b = panel('A'), panel('B')
p = MeshBuilder()
p.timber((0, 0, 0), (.003, .001, 1.3), .188, .19, 6, 920, .012)
post = p.object('SM_WoodFence_Post', SOURCE)
for ob in SOURCE.objects:
    ob.hide_render = True
    ob.hide_set(True)


def duplicate(source, name, position=(0, 0, 0), angle=0):
    ob = bpy.data.objects.new(name, source.data.copy())
    DISPLAY.objects.link(ob)
    ob.data.name = name + '_Mesh'
    transform = Matrix.Translation(Vector(position)) @ Matrix.Rotation(angle, 4, 'Z')
    ob.data.transform(transform)
    return ob


def assembly(name, nodes, edges):
    obs = [duplicate(post, name + '_Post' + str(i), position) for i, position in enumerate(nodes)]
    for i, (a, b) in enumerate(edges):
        a, b = Vector(nodes[a]), Vector(nodes[b])
        midpoint = (a + b) / 2
        angle = math.atan2(b.y - a.y, b.x - a.x)
        obs.append(duplicate(panel_a if i % 2 == 0 else panel_b, name + '_Panel' + str(i), midpoint, angle))
    return obs


single = assembly('Single', [(-.5, 0, 0), (.5, 0, 0)], [(0, 1)])
long_run = assembly('LongRun', [(-1.5 + i, 0, 0) for i in range(4)], [(0, 1), (1, 2), (2, 3)])
corner = assembly('Corner', [(-1.5, 0, 0), (-.5, 0, 0), (.5, 0, 0), (1.5, 0, 0),
                             (1.5, 1, 0), (1.5, 2, 0)], [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5)])


def show(obs):
    for ob in list(SOURCE.objects) + list(DISPLAY.objects):
        ob.hide_render = ob not in obs
        ob.hide_set(ob not in obs)
    bpy.context.view_layer.update()


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf8')


show(single)
checks = {}
for name, obs in [('single', single), ('continuous', long_run), ('corner', corner)]:
    show(obs)
    result = qa_check.run(objects=[o.name for o in obs], profile='game')
    checks[name] = result
    write_json(OUT / 'qa' / (name + '.json'), result)
    print('QA', name, result['pass'], result['errors'], result['warnings'], flush=True)
write_json(OUT / 'qa/geometry-report.json', checks)
if any(not r['pass'] for r in checks.values()):
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'WoodFence_working.blend'))
    raise RuntimeError('Geometry QA failed; inspect report before exporting')


def export(obs, name):
    show(obs)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.export_scene.gltf(filepath=str(OUT / 'exports' / (name + '.glb')),
                              export_format='GLB', use_selection=True, export_materials='EXPORT',
                              export_extras=True, export_yup=True)
    bpy.ops.export_scene.fbx(filepath=str(OUT / 'exports' / (name + '.fbx')),
                             use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='RELATIVE')


for obs, name in [([panel_a], 'WoodFence_PanelA'), ([panel_b], 'WoodFence_PanelB'),
                  ([post], 'WoodFence_Post'), (single, 'WoodFence_1m'), (corner, 'WoodFence_CornerDemo')]:
    export(obs, name)
show(single)
for mode in ('random', 'wire'):
    review_shots.run(objects=[o.name for o in single], out=str(OUT / 'qa' / (mode + '.png')),
                     views=['front', 'back', 'right', 'three_quarter'], mode=mode, res=512)

# Neutral studio, with no presentation geometry included in exports.
floor_mat = bpy.data.materials.new('M_Presentation_Sand')
floor_mat.diffuse_color = (.20, .225, .205, 1)
bsdf = next(n for n in floor_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
bsdf.inputs['Base Color'].default_value = (.20, .225, .205, 1)
bsdf.inputs['Roughness'].default_value = .95
floor_mesh = bpy.data.meshes.new('SM_PresentationFloor_Mesh')
floor_mesh.from_pydata([(-200, -200, -.005), (200, -200, -.005), (200, 200, -.005), (-200, 200, -.005)], [], [(0, 1, 2, 3)])
floor_mesh.materials.append(floor_mat)
floor = bpy.data.objects.new('SM_PresentationFloor', floor_mesh)
STAGE.objects.link(floor)
world = bpy.data.worlds.new('W_NeutralStudio')
world.use_nodes = True
world.node_tree.nodes.get('Background').inputs['Color'].default_value = (.72, .79, .86, 1)
world.node_tree.nodes.get('Background').inputs['Strength'].default_value = .35
sc.world = world


def area(name, location, watts, size, color):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.shape, data.size, data.color = watts, 'DISK', size, color
    ob = bpy.data.objects.new(name, data)
    STAGE.objects.link(ob)
    ob.location = location
    ob.rotation_euler = (Vector((0, 0, .6)) - ob.location).to_track_quat('-Z', 'Y').to_euler()
    return ob


area('L_Key', (-3.5, -4.5, 6), 650, 4.0, (1, .89, .75))
area('L_Fill', (4, -1, 4), 300, 4.0, (.78, .87, 1))
area('L_Rim', (0, 4, 5), 550, 3.5, (1, .94, .83))
cam_data = bpy.data.cameras.new('CAM_WoodFence')
cam = bpy.data.objects.new('CAM_WoodFence', cam_data)
STAGE.objects.link(cam)
cam.data.type = 'ORTHO'
cam.data.clip_start, cam.data.clip_end = .05, 400
sc.camera = cam
sc.render.engine = 'CYCLES'
sc.cycles.samples = 48
sc.cycles.use_denoising = True
sc.cycles.adaptive_threshold = .04
sc.cycles.max_bounces = 6
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'METAL'
    prefs.refresh_devices()
    for device in prefs.devices:
        device.use = device.type == 'METAL'
    sc.cycles.device = 'GPU'
except Exception:
    sc.cycles.device = 'CPU'
sc.view_settings.view_transform = 'AgX'
sc.view_settings.look = 'None'
sc.render.image_settings.file_format = 'PNG'
sc.render.resolution_percentage = 100
sc.render.film_transparent = False


def render(name, obs, position, target, scale, width=1600, height=1200):
    show(obs)
    cam.location = position
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale
    sc.render.resolution_x, sc.render.resolution_y = width, height
    sc.render.filepath = str(OUT / 'preview' / (name + '.png'))
    bpy.ops.render.render(write_still=True)


render('single-front', single, (2.4, -5.7, 2.6), (0, 0, .65), 2.35)
render('single-back', single, (-2.5, 5.5, 2.6), (0, 0, .65), 2.35)
render('continuous', long_run, (3.1, -7, 3.1), (0, 0, .65), 4.3, 1800, 1000)
render('corner', corner, (5.3, -7.5, 5), (.05, .5, .58), 5.4, 1800, 1200)
show(single)
cam.location = (2.4, -5.7, 2.6)
cam.rotation_euler = (Vector((0, 0, .65)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam.data.ortho_scale = 2.35
bpy.ops.object.select_all(action='DESELECT')
for ob in single:
    ob.select_set(True)
bpy.context.view_layer.objects.active = single[-1]
for screen in bpy.data.screens:
    for area_ui in screen.areas:
        if area_ui.type == 'VIEW_3D':
            area_ui.spaces.active.region_3d.view_perspective = 'CAMERA'
            area_ui.spaces.active.shading.type = 'MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'WoodFence.blend'))
bpy.ops.file.make_paths_relative()
bpy.ops.file.pack_all()
bpy.data.orphans_purge(do_recursive=True)
audit = scene_audit.run(objects=[o.name for o in list(SOURCE.objects) + list(DISPLAY.objects)])
write_json(OUT / 'qa/scene-audit.json', audit)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'WoodFence.blend'))
metrics = {}
for ob in (panel_a, panel_b, post):
    metrics[ob.name] = {'tris': len(ob.data.polygons), 'verts': len(ob.data.vertices),
                        'dimensions_m': [round(v, 4) for v in ob.dimensions], 'materials': len(ob.material_slots)}
metrics['complete_single_tris'] = sum(len(o.data.polygons) for o in single)
metrics['grid_spacing_m'] = 1.0
metrics['material_count'] = 1
metrics['textures'] = '1024px baseColor, OpenGL normal, ORM; Unity metallic/smoothness also supplied'
write_json(OUT / 'metrics.json', metrics)
print('DONE', json.dumps(metrics), flush=True)

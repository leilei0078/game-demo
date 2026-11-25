"""Build a single placeable fence object from the packed first study."""
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/wood-fence/tiles-v2'
OUT.mkdir(parents=True, exist_ok=True)
QA = OUT / 'qa'
QA.mkdir(exist_ok=True)
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
import review_shots

original = ROOT / 'output/wood-fence/WoodFence.blend'
assert Path(bpy.data.filepath).resolve() == original
scene = bpy.context.scene
vertices, faces, uvs = [], [], []


def face(indices, coords):
    faces.append(indices)
    uvs.append(coords)


def borrow(obj, planks):
    mesh = obj.data
    uv_layer = mesh.uv_layers.active.data
    mapped = {}
    for poly in mesh.polygons:
        ids = list(poly.vertices)
        if planks:
            if min(ids) < 96:
                continue
            board = (min(ids) - 96) // 84
            if board == 2:
                continue
            offset = [-.066, -.036, 0, .036, .066][board]
        else:
            offset = 0
        for index in ids:
            if index not in mapped:
                point = mesh.vertices[index].co.copy()
                point.x += offset
                mapped[index] = len(vertices)
                vertices.append(tuple(point))
        face([mapped[index] for index in ids], [tuple(uv_layer[loop].uv) for loop in poly.loop_indices])


def rail(z, tile):
    radius, height = .044, .050
    left, right = -.5, .5
    sections = []
    for ang in (78, 60, 35, 0):
        angle = math.radians(ang)
        sections.append((left - math.sin(angle)*radius, 0, math.cos(angle)*radius, -height, -.002))
    sections.extend([(left+.061, 0, radius, -height, -.002),
                     (left+.084, .002, radius, -height, height),
                     (-.32, .015, radius, -height, height),
                     (.08, .015, radius, -height, height),
                     (.32, .015, radius, -height, height),
                     (right-.084, .002, radius, -height, height),
                     (right-.061, 0, radius, .002, height)])
    for ang in (0, 35, 60, 78):
        angle = math.radians(ang)
        sections.append((right + math.sin(angle)*radius, 0, math.cos(angle)*radius, .002, height))
    rings = []
    for x, y, half, bot, top in sections:
        bevel = min(.004, half * .38)
        points = [(-half+bevel,bot), (half-bevel,bot), (half,bot+bevel), (half,top-bevel),
                  (half-bevel,top), (-half+bevel,top), (-half,top-bevel), (-half,bot+bevel)]
        ring = []
        for cross, up in points:
            ring.append(len(vertices))
            vertices.append((x, y+cross, z+up))
        rings.append(ring)
    for number, (first, second) in enumerate(zip(rings, rings[1:])):
        t0, t1 = [(.014 + (sections[i][0] + .55)/1.10*.84) for i in (number,number+1)]
        for index in range(8):
            nxt = (index+1) % 8
            low, high = (tile+.055)/8, (tile+.945)/8
            face([first[index], first[nxt], second[nxt], second[index]],
                 [(low,t0), (high,t0), (high,t1), (low,t1)])
    for ring in (rings[0],rings[-1]):
        bounds = [vertices[index] for index in ring]
        ymin, ymax = min(p[1] for p in bounds), max(p[1] for p in bounds)
        zmin, zmax = min(p[2] for p in bounds), max(p[2] for p in bounds)
        coords = [((tile+.1+.8*(p[1]-ymin)/(ymax-ymin))/8,
                   .89+.09*(p[2]-zmin)/(zmax-zmin)) for p in bounds]
        for index in range(1,7):
            picks = [0,index,index+1]
            face([ring[i] for i in picks], [coords[i] for i in picks])


def peg(z):
    rings = []
    for elevation, radius in [(-.009,.014),(.025,.014),(.029,.011)]:
        ring = []
        for index in range(8):
            angle = index*math.tau/8 + .19
            ring.append(len(vertices))
            vertices.append((-.5+radius*math.cos(angle),radius*math.sin(angle),z+elevation))
        rings.append(ring)
    for number,(first, second) in enumerate(zip(rings,rings[1:])):
        for index in range(8):
            nxt=(index+1)%8
            face([first[index], first[nxt], second[nxt], second[index]],
                 [(.900,.91+number*.015),(.940,.91+number*.015),(.940,.925+number*.015),(.900,.925+number*.015)])
    for ring in (rings[0],rings[-1]):
        coords=[(.9375+.03*math.cos(i*math.tau/8),.9375+.03*math.sin(i*math.tau/8)) for i in range(8)]
        for index in range(1,7):
            picks=[0,index,index+1]
            face([ring[i] for i in picks], [coords[i] for i in picks])


material = bpy.data.materials['M_WoodFence']
borrow(bpy.data.objects['SM_WoodFence_Post'], False)
borrow(bpy.data.objects['SM_WoodFence_PanelA'], True)
for elevation, tile_index in [(.37,4),(.84,5)]:
    rail(elevation, tile_index)
    peg(elevation)
for old_collection in list(bpy.data.collections):
    if old_collection.name.startswith('ASSET_WoodFence_'):
        for obj in list(old_collection.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old_collection)
mesh = bpy.data.meshes.new('WoodFence_Tile_1m')
mesh.from_pydata(vertices, [], faces)
mesh.materials.append(material)
layer = mesh.uv_layers.new(name='UV0_Atlas')
for poly, coords in zip(mesh.polygons, uvs):
    for loop, coord in zip(poly.loop_indices, coords):
        layer.data[loop].uv = coord
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
bmesh.ops.triangulate(bm, faces=list(bm.faces))
bm.to_mesh(mesh)
bm.free()
mesh.update()


def collection(name):
    result = bpy.data.collections.new(name)
    scene.collection.children.link(result)
    return result


def instance(name, destination, xy, degrees=0):
    obj = bpy.data.objects.new(name, mesh)
    destination.objects.link(obj)
    obj.location = (*xy,0)
    obj.rotation_euler.z = math.radians(degrees)
    return obj


model = collection('01 · 单块模型')
straight = collection('02 · 直线拼接（三块）')
corner = collection('03 · 直角拼接（三块）')
unit = instance('SM_WoodFence_Tile_1m', model, (0,0))
unit.asset_mark()
unit.asset_data.description = '木栅栏单块：中柱与面板一体。中心间距 1 米，起点下榫、终点上榫。'
straight_units = [instance('Row_'+str(i+1),straight,(i-1,2.15)) for i in range(3)]
corner_units = [instance('Corner_1',corner,(2.25,0)), instance('Corner_2',corner,(3.25,0)),
                instance('Corner_3',corner,(3.75,.5),90)]
units = [unit,*straight_units,*corner_units]
bpy.context.view_layer.update()


def verify_connections(group):
    for before, after in zip(group,group[1:]):
        end = before.matrix_world @ Vector((.5,0,0))
        start = after.matrix_world @ Vector((-.5,0,0))
        assert (end-start).length < 1e-5, (before.name,after.name)
        assert before.data == after.data == mesh


verify_connections(straight_units)
verify_connections(corner_units)
reports = {}
for key, group in [('single',[unit]),('row',straight_units),('corner',corner_units)]:
    result = qa_check.run(objects=[obj.name for obj in group], profile='game')
    print('QA', key, result['errors'], result['warnings'], flush=True)
    reports[key] = result
(QA/'geometry.json').write_text(json.dumps(reports, indent=2))
assert all(result['pass'] for result in reports.values())
for mode in ('random','wire'):
    review_shots.run(objects=[unit.name], out=str(QA / (mode+'.png')), mode=mode,
                     views=['front','back','top','three_quarter'], res=360)


def select_one(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


select_one(unit)
for image in bpy.data.images:
    if image.packed_file:
        image.filepath = '//../textures/'+Path(image.filepath).name
material.node_tree.nodes.active = next(node for node in material.node_tree.nodes
                                      if node.type == 'TEX_IMAGE' and 'BaseColor' in node.image.name)
bpy.ops.export_scene.gltf(filepath=str(OUT/'WoodFenceTile_1m.glb'),export_format='GLB',use_selection=True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'WoodFenceTile_1m.fbx'),use_selection=True,object_types={'MESH'},
                         axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
                         bake_space_transform=True,mesh_smooth_type='FACE',bake_anim=False,path_mode='COPY',embed_textures=True)
camera = scene.camera
floor = bpy.data.objects['SM_PresentationFloor']
scene.cycles.samples = 28
scene.render.resolution_percentage = 100
scene.render.resolution_x, scene.render.resolution_y = 1440, 900
target = Vector((1.05,1.04,.59))
camera.location = target + Vector((4.3,-8,5.9))
camera.rotation_euler = (target-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale = 6.85
scene.render.filepath = str(OUT/'placement.png')
bpy.ops.render.render(write_still=True)
for helper in bpy.data.collections['LD_Presentation'].objects:
    helper.hide_set(True)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            space = area.spaces.active
            space.region_3d.view_perspective = 'ORTHO'
            space.region_3d.view_rotation = camera.rotation_euler.to_quaternion()
            space.region_3d.view_distance = 8.4
            space.region_3d.view_location = target
            space.shading.type = 'SOLID'
            space.shading.color_type = 'TEXTURE'
scene.tool_settings.snap_elements = {'INCREMENT'}
select_one(unit)
bpy.data.orphans_purge(do_recursive=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'WoodFence_Placement.blend'), relative_remap=False)
print('SAVED', len(mesh.polygons), 'tris,', len(units), 'individually selectable objects')

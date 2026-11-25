"""A reusable 1024px wood/iron atlas for the wooden-fence Blender study."""
import math
import bpy
import numpy as np


def make_atlas(out):
    def image(name, pixels, space):
        h, w = pixels.shape[:2]
        im = bpy.data.images.new(name, width=w, height=h, alpha=True)
        im.colorspace_settings.name = space
        im.pixels.foreach_set(np.ascontiguousarray(pixels, dtype=np.float32).ravel())
        im.filepath_raw = str(out / (name + ".png"))
        im.file_format = "PNG"
        im.save()
        return im

    n = 1024
    rgb = np.ones((n, n, 4), dtype=np.float32)
    rough = np.full((n, n), .79, dtype=np.float32)
    metal = np.zeros((n, n), dtype=np.float32)
    height = np.zeros((n, n), dtype=np.float32)
    palette = [(.47, .315, .178), (.52, .359, .211), (.43, .290, .167),
               (.57, .399, .245), (.365, .249, .151), (.405, .279, .171),
               (.48, .328, .196), (.51, .356, .218)]
    for k, base in enumerate(palette):
        rng = np.random.default_rng(714 + k)
        y, x = np.mgrid[0:896, 0:128].astype(float)
        u, v = x / 128, y / 896
        warp = u + .013 * np.sin(v * 20 + k) + .006 * np.sin(v * 49 + u * 9)
        knots, rings = np.zeros_like(u), np.zeros_like(u)
        for ku, kv, size in [(.27 + .045 * (k % 4), .23 + .051 * (k % 3), .070),
                              (.72 - .033 * (k % 3), .69 - .027 * (k % 5), .047)]:
            dx, dy = u - ku, (v - kv) * 5.2
            rad = np.sqrt(dx * dx + dy * dy)
            warp += .055 * np.tanh(dx * 20) * np.exp(-rad * rad / .075)
            knots = np.maximum(knots, np.exp(-(rad / size) ** 2))
            rings += np.exp(-(rad / (size * 3.0)) ** 2) * np.sin(rad * 115) * .055
        large = np.sin(warp * math.tau * 5.8 + .45 * np.sin(v * 12))
        grain = np.sin(warp * math.tau * 24 + np.sin(v * 28) * .4)
        fine = np.sin(warp * math.tau * 54 + np.sin(v * 59) * .5)
        cracks = np.zeros_like(u)
        for j in range(9):
            cx = rng.uniform(.07, .93)
            start, span = rng.uniform(-.12, .87), rng.uniform(.12, .43)
            path = cx + .007 * np.sin(v * rng.uniform(8, 20) + j) + .004 * np.sin(v * 60)
            taper = np.clip(np.minimum(v - start, start + span - v) * 40, 0, 1)
            cracks = np.maximum(cracks, np.exp(-((u - path) / .0038) ** 2) * taper)
        tone = 1 + .070 * large + .021 * grain + .009 * fine + rng.normal(0, .008, u.shape) + rings
        tone -= .27 * knots + .27 * cracks
        # Weathered colour and ground dirt, without baked light or occlusion.
        tone -= np.exp(-v * 27) * (.20 + .035 * np.sin(u * 28))
        strip = np.array(base)[None, None, :] * tone[:, :, None]
        strip += (.028 * (large * .5 + .5))[:, :, None] * np.array([.46, .48, .46])
        rgb[:896, k * 128:(k + 1) * 128, :3] = np.clip(strip, .025, .9)
        rough[:896, k * 128:(k + 1) * 128] = np.clip(.79 + .055 * large + .07 * cracks, .64, .94)
        height[:896, k * 128:(k + 1) * 128] = .18 * large + .065 * grain + .016 * fine - .31 * cracks - .08 * knots
    for k in range(8):
        y, x = np.mgrid[0:128, 0:128].astype(float) / 128
        if k == 7:
            flecks = np.random.default_rng(411).random((128, 128))
            rgb[896:, 896:, :3] = np.array([.20, .18, .145]) * (1 + .17 * flecks[:, :, None])
            rough[896:, 896:] = .62 + .16 * flecks
            metal[896:, 896:] = 1
            continue
        rad = np.sqrt(((x - .33) * 1.15) ** 2 + (y - .42) ** 2)
        rings = np.sin(rad * 89 + .8 * np.sin(x * 14 + y * 9))
        tone = 1 + .09 * rings + .035 * np.sin(rad * 160)
        rgb[896:, k * 128:(k + 1) * 128, :3] = np.array(palette[min(k + 1, 7)]) * 1.11 * tone[:, :, None]
        rough[896:, k * 128:(k + 1) * 128] = .84 + .035 * rings
        height[896:, k * 128:(k + 1) * 128] = .075 * rings
    # Derivatives stay within each tile to avoid atlas-edge normal artefacts.
    dx, dy = np.zeros_like(height), np.zeros_like(height)
    for k in range(8):
        for lo, hi in [(0, 896), (896, 1024)]:
            sy, sx = np.gradient(height[lo:hi, k * 128:(k + 1) * 128])
            dx[lo:hi, k * 128:(k + 1) * 128] = sx
            dy[lo:hi, k * 128:(k + 1) * 128] = sy
    normal = np.stack([-dx * 1.8, -dy * 1.8, np.ones_like(dx)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    normal_rgba = np.ones_like(rgb)
    normal_rgba[:, :, :3] = normal * .5 + .5
    orm = np.ones_like(rgb)
    orm[:, :, 1], orm[:, :, 2] = rough, metal
    unity = np.ones_like(rgb)
    unity[:, :, :3], unity[:, :, 3] = metal[:, :, None], 1 - rough
    bc = image("T_WoodFence_BaseColor", rgb, "sRGB")
    nm = image("T_WoodFence_Normal", normal_rgba, "Non-Color")
    mr = image("T_WoodFence_ORM", orm, "Non-Color")
    image("T_WoodFence_MetallicSmoothness", unity, "Non-Color")
    mat = bpy.data.materials.new("M_WoodFence")
    mat.diffuse_color = (.46, .30, .17, 1)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = .8
    for tex, pos in ((bc, (-700, 280)), (mr, (-700, 0)), (nm, (-700, -300))):
        node = nodes.new("ShaderNodeTexImage")
        node.image, node.location = tex, pos
        if tex == bc:
            links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        elif tex == mr:
            split = nodes.new("ShaderNodeSeparateColor")
            split.location = (-430, 0)
            links.new(node.outputs["Color"], split.inputs["Color"])
            links.new(split.outputs["Green"], bsdf.inputs["Roughness"])
            links.new(split.outputs["Blue"], bsdf.inputs["Metallic"])
        else:
            normal_node = nodes.new("ShaderNodeNormalMap")
            normal_node.location = (-400, -260)
            links.new(node.outputs["Color"], normal_node.inputs["Color"])
            links.new(normal_node.outputs["Normal"], bsdf.inputs["Normal"])
    return mat

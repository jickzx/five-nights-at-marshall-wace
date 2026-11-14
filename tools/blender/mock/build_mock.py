"""Rough placeholder (mock) models for the Unity game.

Run headless from the repo root:
  /Applications/Blender.app/Contents/MacOS/Blender --background --python tools/blender/mock/build_mock.py

Writes Unity/Assets/Resources/Models/Mock/<name>.fbx and assets/previews/mock/contact_sheet.png.
Models are built Z-up in Blender facing -Y, which arrives in Unity facing +Z, Y-up, scale 1.
"""
import math
import os
import random

import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT_DIR = os.path.join(ROOT, "Unity", "Assets", "Resources", "Models", "Mock")
PREVIEW = os.path.join(ROOT, "assets", "previews", "mock", "contact_sheet.png")
random.seed(7)


def enum_ids(rna, prop):
    return [i.identifier for i in rna.properties[prop].enum_items]


def pick(rna, prop, *wanted):
    ids = enum_ids(rna, prop)
    for w in wanted:
        if w in ids:
            return w
        for i in ids:
            if w.lower() in i.lower():
                return i
    raise RuntimeError(f"none of {wanted} in {prop}: {ids}")


# ---------------------------------------------------------------- materials
PALETTE = {
    "black": ((0.02, 0.02, 0.025), 0),
    "char": ((0.07, 0.06, 0.06), 0),
    "rust": ((0.35, 0.12, 0.05), 0),
    "rust_dark": ((0.16, 0.06, 0.03), 0),
    "bone": ((0.80, 0.76, 0.64), 0),
    "blood": ((0.45, 0.02, 0.02), 0),
    "gore": ((0.70, 0.04, 0.05), 0),
    "stone": ((0.22, 0.18, 0.16), 0),
    "metal": ((0.30, 0.30, 0.32), 0),
    "glow_red": ((1.0, 0.08, 0.05), 4),
    "glow_amber": ((1.0, 0.55, 0.1), 3),
    "glow_cyan": ((0.1, 0.9, 0.8), 3),
}
MATS = {}


def mat(key):
    if key in MATS:
        return MATS[key]
    col, emit = PALETTE[key]
    m = bpy.data.materials.new("mock_" + key)
    m.use_nodes = True
    m.diffuse_color = (*col, 1)  # viewport / workbench
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*col, 1)
    bsdf.inputs["Roughness"].default_value = 0.9
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*col, 1)
        bsdf.inputs["Emission Strength"].default_value = emit
    MATS[key] = m
    return m


# ---------------------------------------------------------------- primitives
def _place(obj, loc, scale, rot, m):
    obj.location = loc
    obj.scale = scale
    obj.rotation_euler = [math.radians(a) for a in rot]
    obj.data.materials.append(mat(m))
    return obj


def box(loc, size, m, rot=(0, 0, 0)):
    """Box with its centre at loc and full dimensions size."""
    bpy.ops.mesh.primitive_cube_add(size=1)
    return _place(bpy.context.active_object, loc, size, rot, m)


def cyl(loc, r, h, m, rot=(0, 0, 0), verts=6, r2=None):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=1, depth=1)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=1, radius2=r2 / r, depth=1)
    return _place(bpy.context.active_object, loc, (r, r, h), rot, m)


def cone(loc, r, h, m, rot=(0, 0, 0), verts=4):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=1, radius2=0, depth=1)
    return _place(bpy.context.active_object, loc, (r, r, h), rot, m)


def blob(loc, size, m, jitter=0.25, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1)
    o = bpy.context.active_object
    for v in o.data.vertices:
        v.co *= 1 + random.uniform(-jitter, jitter)
    return _place(o, loc, size, rot, m)


def finish(name, parts):
    """Join parts into one mesh object called name, transforms applied, origin at base centre."""
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = o.data
    me.name = name
    o.name = name
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    minz = min(v.co.z for v in me.vertices)
    off = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, minz))
    for v in me.vertices:
        v.co -= off
    for poly in me.polygons:
        poly.use_smooth = False
    o.location = (0, 0, 0)
    return o


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


# ---------------------------------------------------------------- models
def m_player():
    p = [
        box((-0.13, 0, 0.42), (0.2, 0.24, 0.84), "bone"),
        box((0.13, 0, 0.42), (0.2, 0.24, 0.84), "bone"),
        box((-0.13, -0.04, 0.05), (0.22, 0.32, 0.1), "char"),
        box((0.13, -0.04, 0.05), (0.22, 0.32, 0.1), "char"),
        box((0, 0, 1.12), (0.52, 0.32, 0.6), "bone"),
        box((0, 0, 0.86), (0.54, 0.34, 0.08), "rust"),
        box((0, 0.27, 1.15), (0.42, 0.22, 0.55), "rust"),
        box((-0.34, 0, 1.08), (0.15, 0.17, 0.62), "bone"),
        box((0.34, 0, 1.08), (0.15, 0.17, 0.62), "bone"),
        box((0, 0, 1.6), (0.34, 0.36, 0.36), "bone"),
        box((0, -0.17, 1.62), (0.26, 0.04, 0.16), "glow_amber"),
        box((0.12, 0.08, 1.82), (0.02, 0.02, 0.12), "metal"),
    ]
    return finish("player", p)


def m_beast1():
    p = [
        box((-0.12, 0, 0.55), (0.09, 0.09, 1.1), "black"),
        box((0.12, 0, 0.55), (0.09, 0.09, 1.1), "black"),
        box((0, 0.03, 1.45), (0.3, 0.16, 0.75), "black", rot=(8, 0, 0)),
        box((-0.24, -0.05, 1.1), (0.07, 0.07, 1.15), "black", rot=(-10, 0, -6)),
        box((0.24, -0.05, 1.1), (0.07, 0.07, 1.15), "black", rot=(-10, 0, 6)),
        box((0, -0.05, 1.98), (0.2, 0.24, 0.3), "black"),
        box((0, -0.04, 1.83), (0.06, 0.06, 0.1), "black"),
        box((-0.05, -0.175, 2.02), (0.05, 0.02, 0.035), "glow_amber"),
        box((0.05, -0.175, 2.02), (0.05, 0.02, 0.035), "glow_amber"),
    ]
    for s in (-1, 1):  # claws
        for i in (-1, 0, 1):
            p.append(cone((s * 0.27 + i * 0.03, -0.15, 0.48), 0.02, 0.18, "bone", rot=(180, 0, 0)))
    return finish("beast_stage1", p)


def m_beast2():
    p = [
        box((-0.22, 0.05, 0.6), (0.16, 0.18, 1.2), "rust_dark", rot=(0, 0, 0)),
        box((0.22, 0.05, 0.6), (0.16, 0.18, 1.2), "rust_dark"),
        box((0, 0, 1.65), (0.6, 0.4, 1.0), "rust_dark", rot=(18, 0, 0)),
        box((0, -0.35, 2.25), (0.34, 0.4, 0.34), "rust_dark"),
        box((0, -0.58, 2.18), (0.24, 0.12, 0.14), "blood"),
    ]
    for i in range(5):  # ribs
        z = 1.3 + i * 0.16
        p.append(box((0, -0.23 - i * 0.05, z), (0.66, 0.06, 0.04), "bone", rot=(18, 0, 0)))
    for s in (-1, 1):  # 4 arms
        p.append(box((s * 0.45, -0.15, 1.55), (0.12, 0.12, 1.3), "rust_dark", rot=(-20, 0, s * 15)))
        p.append(box((s * 0.42, -0.25, 1.15), (0.1, 0.1, 1.0), "rust_dark", rot=(-45, 0, s * 25)))
        p.append(cone((s * 0.62, -0.38, 0.88), 0.06, 0.3, "bone", rot=(180, 0, 0)))
        p.append(cone((s * 0.66, -0.66, 0.85), 0.05, 0.26, "bone", rot=(130, 0, 0)))
    for s in (-1, 1):
        p.append(box((s * 0.09, -0.555, 2.32), (0.06, 0.02, 0.04), "glow_red"))
        p.append(box((s * 0.14, -0.555, 2.24), (0.04, 0.02, 0.03), "glow_red"))
    return finish("beast_stage2", p)


def m_beast3():
    p = [
        box((-0.35, 0.1, 0.7), (0.3, 0.32, 1.4), "blood"),
        box((0.35, 0.1, 0.7), (0.3, 0.32, 1.4), "blood"),
        box((0, 0, 2.0), (1.1, 0.8, 1.4), "blood", rot=(15, 0, 0)),
        box((0, -0.25, 2.75), (0.7, 0.6, 0.35), "blood"),
        # split jaw: upper hinged up, lower hinged down, gore inside
        box((0, -0.75, 3.0), (0.6, 0.7, 0.18), "blood", rot=(-25, 0, 0)),
        box((0, -0.75, 2.45), (0.6, 0.7, 0.16), "blood", rot=(30, 0, 0)),
        box((0, -0.6, 2.72), (0.45, 0.4, 0.35), "gore"),
    ]
    for i in range(4):  # teeth
        x = -0.18 + i * 0.12
        p.append(cone((x, -0.95, 2.92), 0.04, 0.2, "bone", rot=(180 - 25, 0, 0)))
        p.append(cone((x, -0.95, 2.6), 0.04, 0.2, "bone", rot=(30, 0, 0)))
    for s in (-1, 1):  # arms + claws
        p.append(box((s * 0.75, -0.2, 1.8), (0.25, 0.25, 1.6), "blood", rot=(-15, 0, s * 12)))
        p.append(cone((s * 0.92, -0.42, 0.95), 0.1, 0.4, "bone", rot=(180, 0, 0)))
        p.append(box((s * 0.62, -0.1, 2.35), (0.3, 0.2, 0.9), "blood", rot=(-60, 0, s * 40)))
    for i in range(4):  # ribs breaking through
        p.append(box((0, -0.42 - i * 0.06, 1.55 + i * 0.2), (1.05, 0.07, 0.06), "bone", rot=(15, 0, 0)))
    for x, y, z, sx in [(0.25, -0.5, 1.4, 0.3), (-0.3, -0.45, 2.2, 0.25), (0.45, -0.3, 2.6, 0.2), (-0.2, -0.3, 0.9, 0.2)]:
        p.append(box((x, y, z), (sx, sx * 0.6, sx), "gore", rot=(random.uniform(0, 45), 20, random.uniform(0, 45))))
    eyes = [(-0.28, 2.85), (0.28, 2.85), (-0.15, 2.92), (0.15, 2.92), (-0.33, 2.7), (0.33, 2.7), (0, 2.88), (-0.22, 2.78), (0.22, 2.78)]
    for x, z in eyes:
        p.append(box((x, -0.56, z), (0.06, 0.02, 0.05), "glow_red"))
    return finish("beast_stage3", p)


def m_ship():
    p = [
        cyl((0, 0, 1.4), 0.9, 4.5, "metal", rot=(90, 0, 0), verts=8),
        cone((0, -2.95, 1.4), 0.9, 1.4, "metal", rot=(90, 0, 0), verts=8),
        cyl((0, 2.45, 1.4), 0.9, 0.4, "rust_dark", rot=(90, 0, 0), verts=8, r2=0.6),
        box((0, 2.68, 1.4), (0.8, 0.06, 0.8), "glow_amber"),
        box((-1.6, 0.5, 1.2), (2.4, 1.6, 0.1), "rust", rot=(0, -8, 0)),
        box((1.6, 0.5, 1.2), (2.4, 1.6, 0.1), "rust", rot=(0, 8, 0)),
        box((0, 1.9, 2.4), (0.1, 1.0, 1.2), "rust"),
        box((0, -2.55, 1.85), (0.9, 0.5, 0.3), "glow_cyan", rot=(-25, 0, 0)),
        # open hatch: dark doorway on the right side + ramp down to ground
        box((0.82, 0.4, 1.35), (0.2, 1.1, 1.3), "black"),
        box((1.45, 0.4, 0.38), (1.5, 1.0, 0.06), "rust_dark", rot=(0, -35, 0)),
    ]
    for x, y in [(-0.7, -1.6), (0.7, -1.6), (-0.7, 1.6), (0.7, 1.6)]:
        p.append(box((x, y, 0.45), (0.1, 0.1, 0.9), "char"))
        p.append(box((x, y, 0.03), (0.35, 0.35, 0.06), "char"))
    return finish("ship", p)


def m_console():
    p = [
        box((0, 0, 0.45), (0.6, 0.5, 0.9), "char"),
        box((0, -0.05, 1.0), (0.9, 0.7, 0.12), "rust_dark", rot=(30, 0, 0)),
        box((0, 0.3, 1.35), (0.9, 0.12, 0.6), "char"),
    ]
    bpy.ops.mesh.primitive_plane_add(size=1)
    scr = _place(bpy.context.active_object, (0, 0.235, 1.38), (0.75, 0.45, 1), (90, 0, 0), "glow_cyan")
    p.append(scr)
    base = finish("console", p)
    # Screen quad is in the base mesh (cyan material). Face normal must point -Y (towards player).
    tiles = []
    for i in range(4):
        x = -0.3 + i * 0.2
        t = box((x, -0.09, 1.07), (0.15, 0.15, 0.04), "glow_amber", rot=(30, 0, 0))
        bpy.ops.object.select_all(action="DESELECT")
        t.select_set(True)
        bpy.context.view_layer.objects.active = t
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        t.name = t.data.name = f"tile_{i}"
        for poly in t.data.polygons:
            poly.use_smooth = False
        t.parent = base
        tiles.append(t)
    return base


def m_rock_a():
    o = blob((0, 0, 0.5), (1.1, 0.9, 0.6), "stone", jitter=0.3)
    return finish("rock_a", [o])


def m_rock_b():
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    for v in o.data.vertices:
        v.co.x += random.uniform(-0.15, 0.15)
        v.co.y += random.uniform(-0.15, 0.15)
        if v.co.z > 0:
            v.co *= 0.6
    _place(o, (0, 0, 0.9), (0.9, 0.8, 1.8), (0, 0, 20), "rust_dark")
    o2 = blob((0.6, 0.3, 0.25), (0.4, 0.35, 0.3), "stone")
    return finish("rock_b", [o, o2])


def m_ruin_pillar():
    p = [
        box((0, 0, 0.15), (0.9, 0.9, 0.3), "stone"),
        cyl((0, 0, 1.8), 0.32, 3.0, "bone", verts=6),
        box((0.35, -0.2, 0.4), (0.3, 0.25, 0.2), "stone", rot=(10, 20, 30)),
        cyl((0, 0, 0.55), 0.4, 0.2, "stone", verts=6),
        cyl((0, 0, 2.2), 0.36, 0.12, "rust", verts=6),
        box((-0.5, 0.3, 0.1), (0.35, 0.3, 0.2), "bone", rot=(5, -10, 60)),
    ]
    top = p[1]
    bpy.context.view_layer.objects.active = top
    for v in top.data.vertices:  # broken slanted top
        if v.co.z > 0:
            v.co.z += 0.2 * v.co.x + 0.1 * v.co.y
    return finish("ruin_pillar", p)


def m_ruin_arch():
    p = []
    for s in (-1, 1):
        p.append(box((s * 1.4, 0, 0.15), (0.8, 0.8, 0.3), "stone"))
        p.append(box((s * 1.4, 0, 1.75), (0.6, 0.6, 2.9), "bone"))
    p.append(box((0, 0, 3.4), (3.6, 0.7, 0.4), "bone"))
    p.append(box((-0.6, 0, 3.75), (1.8, 0.65, 0.3), "stone", rot=(0, 4, 0)))
    p.append(box((0, 0, 3.1), (0.5, 0.75, 0.3), "rust", rot=(0, 0, 0)))
    return finish("ruin_arch", p)


def m_door_sealed():
    p = [
        box((-1.15, 0, 1.5), (0.3, 0.5, 3.0), "stone"),
        box((1.15, 0, 1.5), (0.3, 0.5, 3.0), "stone"),
        box((0, 0, 3.15), (2.6, 0.5, 0.3), "stone"),
        box((0, 0, 1.5), (2.0, 0.25, 3.0), "rust_dark"),
        box((0, -0.13, 1.5), (0.08, 0.02, 2.9), "glow_red"),
        box((0, -0.14, 1.5), (0.45, 0.04, 0.45), "metal", rot=(0, 45, 0)),
        box((0, -0.17, 1.5), (0.15, 0.02, 0.15), "glow_red", rot=(0, 45, 0)),
    ]
    return finish("door_sealed", p)


def m_mural_slab():
    p = [box((0, 0, 1.25), (2.0, 0.3, 2.5), "stone"), box((0, 0, 0.1), (2.3, 0.5, 0.2), "char")]
    glyphs = [(-0.6, 2.0), (-0.2, 2.1), (0.3, 1.95), (0.65, 2.05), (-0.5, 1.5), (0.0, 1.4), (0.5, 1.55),
              (-0.65, 0.9), (-0.1, 0.95), (0.45, 0.85)]
    for i, (x, z) in enumerate(glyphs):
        key = "glow_cyan" if i % 3 == 0 else "bone"
        p.append(box((x, -0.16, z), (0.25, 0.03, 0.08 + 0.1 * (i % 2)), key, rot=(0, (i * 37) % 90, 0)))
    return finish("mural_slab", p)


def m_wreck_debris():
    p = [
        box((0, 0, 0.15), (2.2, 1.2, 0.08), "metal", rot=(8, -12, 15)),
        box((0.9, 0.5, 0.45), (1.4, 0.9, 0.06), "rust", rot=(40, 10, -30)),
        box((-0.8, -0.3, 0.35), (1.0, 0.8, 0.06), "rust_dark", rot=(-25, 30, 50)),
        cyl((0.2, -0.6, 0.3), 0.25, 1.6, "char", rot=(80, 0, 35), verts=6),
        box((-0.2, 0.7, 0.6), (0.12, 0.12, 1.4), "metal", rot=(30, 20, 0)),
        box((0.6, -0.1, 0.25), (0.3, 0.3, 0.06), "glow_amber", rot=(10, 5, 20)),
    ]
    return finish("wreck_debris", p)


def m_terrain_shore():
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=11, y_subdivisions=11, size=20)
    o = bpy.context.active_object
    for v in o.data.vertices:
        t = (v.co.y + 10) / 20  # 0 at -Y edge (shoreline), 1 at +Y edge (inland)
        h = max(0.0, (t - 0.45) / 0.55) ** 1.3 * 3.0
        if 0 < t < 1:
            h += random.uniform(-0.12, 0.12) * (0.3 + t)
        v.co.z = max(0.0, h)
    o.data.materials.append(mat("rust"))
    o.data.materials.append(mat("blood"))
    for poly in o.data.polygons:  # flat lowland reads as blood shallows
        if poly.center.z < 0.05:
            poly.material_index = 1
    return finish("terrain_shore", [o])


MODELS = [m_player, m_beast1, m_beast2, m_beast3, m_ship, m_console, m_rock_a, m_rock_b,
          m_ruin_pillar, m_ruin_arch, m_door_sealed, m_mural_slab, m_wreck_debris, m_terrain_shore]


# ---------------------------------------------------------------- export
def export_fbx(root, path):
    rna = bpy.ops.export_scene.fbx.get_rna_type()
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for c in root.children_recursive:
        c.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={pick(rna, "object_types", "MESH")},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options=pick(rna, "apply_scale_options", "FBX_SCALE_ALL", "ALL"),
        axis_forward=pick(rna, "axis_forward", "-Z"),
        axis_up=pick(rna, "axis_up", "Y"),
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type=pick(rna, "mesh_smooth_type", "FACE"),
        add_leaf_bones=False,
        bake_anim=False,
        path_mode=pick(rna, "path_mode", "AUTO"),
    )


# ---------------------------------------------------------------- contact sheet
def render_tile(root, scene, cam, label, path):
    objs = [root] + list(root.children_recursive)
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (lo + hi) / 2
    size = (hi - lo).length
    d = Vector((0.55, -1.0, 0.45)).normalized()
    cam.location = centre + d * size * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = size * 1.15
    cam.data.clip_end = size * 10
    label.data.body = f"{root.name}  {sum(tris(o) for o in objs)} tris"
    label.parent = cam
    label.location = (-0.48 * size * 1.15, -0.47 * size * 1.15, -1)
    label.scale = [size * 1.15 * 0.07] * 3
    for o in scene.objects:
        o.hide_render = o not in objs and o not in (cam, label)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def contact_sheet(roots):
    scene = bpy.context.scene
    # The engine enum under-reports in background mode; the TypeError lists the real ids.
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError as e:
        ids = [w.strip("'(),") for w in str(e).split("(", 1)[-1].split()]
        scene.render.engine = next(i for i in ids if "WORKBENCH" in i)
    sh = scene.display.shading
    sh.light = pick(bpy.types.View3DShading.bl_rna, "light", "STUDIO")
    sh.color_type = pick(bpy.types.View3DShading.bl_rna, "color_type", "MATERIAL")
    sh.show_object_outline = True
    sh.background_type = pick(bpy.types.View3DShading.bl_rna, "background_type", "VIEWPORT")
    sh.background_color = (0.16, 0.15, 0.15)
    T = 320
    scene.render.resolution_x = scene.render.resolution_y = T
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = pick(bpy.types.ImageFormatSettings.bl_rna, "file_format", "PNG")
    cam_data = bpy.data.cameras.new("sheet_cam")
    cam_data.type = pick(bpy.types.Camera.bl_rna, "type", "ORTHO")
    cam = bpy.data.objects.new("sheet_cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    txt = bpy.data.curves.new("sheet_label", "FONT")
    label = bpy.data.objects.new("sheet_label", txt)
    scene.collection.objects.link(label)
    lm = bpy.data.materials.new("label")
    lm.diffuse_color = (1, 1, 1, 1)
    txt.extrude = 0.02
    txt.materials.append(lm)

    tmp = os.path.join(bpy.app.tempdir or "/tmp", "mock_tiles")
    os.makedirs(tmp, exist_ok=True)
    cols = 4
    rows = math.ceil(len(roots) / cols)
    sheet = np.zeros((rows * T, cols * T, 4), dtype=np.float32)
    sheet[...] = (0.16, 0.15, 0.15, 1)
    for i, r in enumerate(roots):
        p = os.path.join(tmp, f"{r.name}.png")
        render_tile(r, scene, cam, label, p)
        img = bpy.data.images.load(p)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(T, T, 4)
        bpy.data.images.remove(img)
        row, col = divmod(i, cols)
        y0 = (rows - 1 - row) * T  # blender pixels are bottom-up
        sheet[y0:y0 + T, col * T:(col + 1) * T] = px
    out = bpy.data.images.new("contact_sheet", cols * T, rows * T, alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = PREVIEW
    out.file_format = "PNG"
    out.save()


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = pick(bpy.types.UnitSettings.bl_rna, "system", "METRIC")
    bpy.context.scene.unit_settings.scale_length = 1.0
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    roots = []
    for build in MODELS:
        root = build()
        n = tris(root) + sum(tris(c) for c in root.children)
        print(f"[mock] {root.name}: {n} tris")
        export_fbx(root, os.path.join(OUT_DIR, root.name + ".fbx"))
        root.hide_set(False)
        roots.append(root)
    # Spread out so bounding boxes don't overlap during tile renders (not exported after this).
    for i, r in enumerate(roots):
        r.location.x = i * 40
    bpy.context.view_layer.update()
    contact_sheet(roots)
    print("[mock] done")


main()

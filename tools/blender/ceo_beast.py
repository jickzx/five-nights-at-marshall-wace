"""The Principal: a generic, faceless hedge-fund-boss beast in three stages, covered in rival funds'
wordmarks. Not modelled on, named after or labelled as any real person.

Built in a live Blender session (driven over MCP) or headless:
    Blender --background --python tools/blender/ceo_beast.py -- 1 2 3
Needs tools/blender/ceo_beast_atlas.py to have been run first (writes the atlas + cell table).

Authoring axes follow common.py: metres, Z up, +Y forward. Export rotates geometry and bone rests
into FBX Y up like common.deliver, plus a 180 deg turn so the beast faces Unity +Z, the way
BeastController drives it (LookRotation, model local rotation identity). No game code change needed.
Takes: idle 97f, stalk 73f, chase 19f, lunge 37f at 24 fps (same frame counts as the beast they
replace, so the existing .fbx.meta clip ranges stay valid).
"""
import bpy
import importlib
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

HERE = Path(globals().get('__file__', 'tools/blender/ceo_beast.py')).resolve().parent
sys.path.insert(0, str(HERE))
import common  # noqa: E402
importlib.reload(common)
from common import enum, PARTS, WEIGHTS  # noqa: E402

ROOT = common.ROOT
ATLAS = ROOT / 'Unity/Assets/Resources/Models/Textures/beast_ceo_atlas.png'
CELLS = json.loads((HERE / 'ceo_beast_atlas.json').read_text())['cells']
HEIGHT = {1: 2.35, 2: 2.9, 3: 3.55}
FRAMES = {'idle': 97, 'stalk': 73, 'chase': 19, 'lunge': 37}
NOTES = ('The Principal: a faceless, too-thin fund boss in a charcoal suit, lanyard and rival-fund patches '
         '(CITADEL pass, POINT72 back patch, MILLENNIUM, BALYASNY, D.E.SHAW, MAN GROUP, RENTEC, Two Sigma tie). '
         'Stage {s}. In-place stalk/chase; lunge moves the root 0.9 m forward. Texture: Textures/beast_ceo_atlas.png '
         '(point filtered).')
STATE = {'smear': False}
MATS = {}
Z = Vector((0, 0, 1))


# ---------------------------------------------------------------- scene + materials
def clear_scene():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode=enum(bpy.ops.object.mode_set, 'mode', 'OBJECT'))
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials,
                 bpy.data.images, bpy.data.cameras, bpy.data.lights, bpy.data.worlds):
        bpy.data.batch_remove(list(coll))
    PARTS.clear(); WEIGHTS.clear(); common.MATERIALS.clear(); MATS.clear()
    scene = bpy.context.scene
    scene.unit_settings.system = enum(scene.unit_settings, 'system', 'METRIC')
    scene.unit_settings.scale_length = 1
    scene.render.fps = common.FPS
    img = bpy.data.images.load(str(ATLAS))
    img.name = 'beast_ceo_atlas'
    atlas = bpy.data.materials.new('principal_atlas')
    atlas.use_nodes = True
    nt = atlas.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (1, 1, 1, 1)
    bsdf.inputs['Roughness'].default_value = .9
    bsdf.inputs['Metallic'].default_value = 0
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = enum(tex, 'interpolation', 'Closest')
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    MATS['atlas'] = atlas
    eye = bpy.data.materials.new('eye')
    eye.use_nodes = True
    bsdf = next(n for n in eye.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (1, .045, .015, 1)
    bsdf.inputs['Emission Color'].default_value = (1, .045, .015, 1)
    bsdf.inputs['Emission Strength'].default_value = 3
    MATS['eye'] = eye
    coal = bpy.data.materials.new('coal')   # preview floor only, never exported
    coal.use_nodes = True
    next(n for n in coal.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (.03, .012, .012, 1)
    common.MATERIALS['coal'] = coal


# ---------------------------------------------------------------- geometry helpers
def cell_uv(cell, u, v):
    x, y, w, h = CELLS[cell]
    if w == 16:                       # flat swatch: sample its middle only
        u, v = .25 + .5 * u, .25 + .5 * v
    if STATE['smear']:
        y += 128
    return ((x + .5 + u * (w - 1)) / 256, 1 - (y + .5 + (1 - v) * (h - 1)) / 256)


_names = {}


def make(name, verts, faces, uvs, cell, bone):
    _names[name] = _names.get(name, 0) + 1
    if _names[name] > 1:
        name = f'{name}.{_names[name]}'
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    layer = me.uv_layers.new(name='UVMap')
    for poly, fuv in zip(me.polygons, uvs):
        for li, (u, v) in zip(poly.loop_indices, fuv):
            layer.data[li].uv = (u, v) if cell == 'EYE' else cell_uv(cell, u, v)
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    me.materials.append(MATS['eye'] if cell == 'EYE' else MATS['atlas'])
    for p in me.polygons:
        p.use_smooth = False
    PARTS.append(obj)
    WEIGHTS[name] = bone
    return obj


def frame(a, b):
    axis = (b - a).normalized()
    ref = Vector((1, 0, 0)) if abs(axis.x) < .9 else Vector((0, 1, 0))
    side = (ref - axis * ref.dot(axis)).normalized()
    return axis, side, axis.cross(side)


FULL = [(0, 0), (1, 0), (1, 1), (0, 1)]


def prism(name, a, b, wa, da, wb=None, db=None, cell='suit', bone='root', sides=4, cells=None):
    """Tapered prism from a to b. 4 sides = box faces [+side, +front, -side, -back]."""
    a, b = Vector(a), Vector(b)
    wb = wa if wb is None else wb
    db = da if db is None else db
    axis, side, dep = frame(a, b)
    if sides == 4:
        ring = [(1, -1), (1, 1), (-1, 1), (-1, -1)]
    else:
        ring = [(math.cos(math.tau * k / sides), math.sin(math.tau * k / sides)) for k in range(sides)]
    verts = [a + side * (sx * wa / 2) + dep * (sy * da / 2) for sx, sy in ring]
    verts += [b + side * (sx * wb / 2) + dep * (sy * db / 2) for sx, sy in ring]
    n = sides
    cells = cells or {}
    faces, uvs, fcells = [], [], []
    for k in range(n):
        faces.append((k, (k + 1) % n, n + (k + 1) % n, n + k))
        uvs.append(FULL)
        fcells.append(cells.get(k, cell))
    faces.append(tuple(reversed(range(n))))
    uvs.append([((sx + 1) / 2, (sy + 1) / 2) for sx, sy in reversed(ring)])
    fcells.append(cells.get('bottom', cell))
    faces.append(tuple(range(n, 2 * n)))
    uvs.append([((sx + 1) / 2, (sy + 1) / 2) for sx, sy in ring])
    fcells.append(cells.get('top', cell))
    if len(set(fcells)) == 1:
        return make(name, verts, faces, uvs, cell, bone)
    # mixed cells: one object per face group keeps make() simple
    for c in sorted(set(fcells)):
        idx = [i for i, fc in enumerate(fcells) if fc == c]
        used = sorted({v for i in idx for v in faces[i]})
        remap = {v: j for j, v in enumerate(used)}
        make(name, [verts[v] for v in used], [tuple(remap[v] for v in faces[i]) for i in idx],
             [uvs[i] for i in idx], c, bone)


def box(name, center, w, d, h, cell, bone, up=Z, cells=None):
    c = Vector(center)
    up = Vector(up).normalized()
    return prism(name, c - up * h / 2, c + up * h / 2, w, d, cell=cell, bone=bone, cells=cells)


def quad(name, center, n, up, w, h, cell, bone, roll=0.0, pivot=None):
    """Flat card facing n, readable (u left-to-right) from the viewer's side. roll spins it about pivot."""
    c, n, up = Vector(center), Vector(n).normalized(), Vector(up)
    right = (-n).cross(up).normalized()
    up = n.cross(right).normalized()
    pts = [c - right * w / 2 - up * h / 2, c + right * w / 2 - up * h / 2,
           c + right * w / 2 + up * h / 2, c - right * w / 2 + up * h / 2]
    if roll:
        q = Quaternion(n, roll)
        p = Vector(pivot) if pivot is not None else c
        pts = [p + q @ (v - p) for v in pts]
    return make(name, pts, [(0, 1, 2, 3)], [FULL], cell, bone)


def poly(name, pts, n, cell, bone):
    pts = [Vector(p) for p in pts]
    if (pts[1] - pts[0]).cross(pts[2] - pts[0]).dot(Vector(n)) < 0:
        pts.reverse()
    uv = [(.5 + .5 * math.cos(math.tau * i / len(pts)), .5 + .5 * math.sin(math.tau * i / len(pts))) for i in range(len(pts))]
    return make(name, pts, [tuple(range(len(pts)))], [uv], cell, bone)


def chain(prefix, pts, widths, cells, bones, depth=None, sides=4):
    for i in range(len(pts) - 1):
        w0, w1 = widths[i], widths[i + 1]
        prism(f'{prefix}{i}', pts[i], pts[i + 1], w0, (depth or 1) * w0, w1, (depth or 1) * w1,
              cell=cells[i], bone=bones[i], sides=sides)


def lerp(a, b, t):
    return Vector(a).lerp(Vector(b), t)


# ---------------------------------------------------------------- stage layouts (authoring units)
STAGES = {
    1: dict(hip=1.10, legs=[(.11, 0, 1.10), (.11, .0, .57), (.11, -.02, .09)], leg_w=[.15, .12, .11],
            P=(0, 0, 1.10), M=(0, .03, 1.42), N=(0, .09, 1.74), T=(0, .14, 1.97),
            waist=(.34, .21), chest=(.46, .25), bar=.62, head_off=(0, .02, .12), head=(.185, .2, .24), roll=0,
            arm=[(.29, .08, 1.70), (.31, .09, 1.29), (.32, .11, .89)], tip=(.32, .13, .71), sleeve=[.13, .11, .10],
            tie=3, patch_roll=0, fingers=0),
    2: dict(hip=1.30, legs=[(.12, 0, 1.30), (.12, .03, .68), (.12, -.02, .10)], leg_w=[.15, .12, .10],
            P=(0, 0, 1.30), M=(0, .12, 1.62), N=(0, .34, 1.90), T=(0, .55, 2.06),
            waist=(.34, .21), chest=(.48, .27), bar=.72, head_off=(0, .08, .12), head=(.25, .27, .33), roll=.2,
            arm=[(.32, .30, 1.86), (.38, .36, 1.46), (.40, .40, 1.06), (.40, .42, .66)], tip=(.40, .46, .38),
            sleeve=[.13, .11, .10, .06], tie=4, patch_roll=.28, fingers=3),
    3: dict(hip=1.45, legs=[(.14, 0, 1.45), (.14, .16, 1.0), (.14, -.14, .55), (.14, 0, .10)], leg_w=[.15, .12, .08, .07],
            P=(0, 0, 1.45), M=(0, .2, 1.78), N=(0, .5, 2.0), T=(0, .75, 2.08),
            waist=(.36, .23), chest=(.52, .30), bar=.80, head_off=(0, .10, .06), head=(.27, .30, .34), roll=-.12,
            arm=[(.36, .46, 1.95), (.46, .58, 1.55), (.50, .64, 1.12), (.52, .66, .68), (.52, .70, .25)], tip=(.52, .80, .09),
            sleeve=[.13, .11, .07, .06, .05], tie=7, patch_roll=.5, fingers=4),
}


def side_names(base, n, side):
    return [f'{base}{i}.{side}' for i in range(n)]


def build_parts(s):
    L = STAGES[s]
    STATE['smear'] = s == 3
    P, M, N, T = (Vector(L[k]) for k in 'PMNT')
    bones = [('root', (0, 0, 0), (0, 0, .25), None), ('hips', P - Vector((0, 0, .14)), P, 'root'),
             ('spine', P, M, 'hips'), ('chest', M, N, 'spine'), ('neck', N, T, 'chest')]
    # ---- torso
    box('skirt', P + Vector((0, .0, -.06)), L['waist'][0] + .04, L['waist'][1] + .03, .22, 'suit', 'hips')
    prism('torso_lo', P, M, *L['waist'], (L['waist'][0] + L['chest'][0]) / 2, (L['waist'][1] + L['chest'][1]) / 2, cell='suit', bone='spine')
    prism('torso_hi', M, N, (L['waist'][0] + L['chest'][0]) / 2, (L['waist'][1] + L['chest'][1]) / 2, *L['chest'], cell='suit', bone='chest')
    axis, side, dep = frame(M, N)
    box('shoulder_bar', N - axis * .04, L['bar'], L['chest'][1] + .02, .08, 'suit', 'chest', up=axis)
    # ---- shirt V, collar, neck
    front = lambda t, extra=.008: lerp(M, N, t) + dep * (lerp(Vector(((L['waist'][1] + L['chest'][1]) / 4, 0, 0)), Vector((L['chest'][1] / 2, 0, 0)), t).x + extra)
    poly('shirt_v', [front(.98) + side * .11, front(.98) - side * .11, front(.25)], dep, 'shirt', 'chest')
    if s >= 2:  # spatter on the shirt
        quad('shirt_blood', front(.6, .011) + side * .03, dep, axis, .06, .09 if s == 2 else .2, 'blood', 'chest', roll=.4)
    prism('collar', N - axis * .02, N + axis * .05, .17, .17, .15, .15, cell='shirt', bone='neck', sides=5)
    prism('neckpipe', N, T, .1, .1, .085, .085, cell='skin', bone='neck', sides=5)
    # ---- head (blank face on the front, charcoal hair cap)
    hw, hd, hh = L['head']
    H = T + Vector(L['head_off'])
    roll = Quaternion(Vector((0, 1, 0)), L['roll'])
    hup = roll @ Z
    bones.append(('head', T, T + hup * (hh + .1), 'neck'))
    box('head', H, hw, hd, hh, 'skin', 'head', up=hup, cells={1: 'face', 'top': 'coal'})
    box('hair', H + hup * (hh / 2 + .012), hw + .02, hd + .02, .035, 'coal', 'head', up=hup)
    hright = roll @ Vector((-1, 0, 0))
    fz = H + roll @ Vector((0, hd / 2 + .006, 0))
    ew, eh = (.032, .02) if s == 1 else (.045, .028)
    quad('eye_l', fz - hright * hw * .40 + hup * hh * .10, roll @ Vector((0, 1, 0)), hup, ew, eh, 'EYE', 'head')
    quad('eye_r', fz + hright * hw * .37 + hup * hh * .05, roll @ Vector((0, 1, 0)), hup, ew * .85, eh * 1.15, 'EYE', 'head')
    if s == 2:
        quad('mouth', fz - hup * hh * .28, roll @ Vector((0, 1, 0)), hup, hw * .86, .014, 'coal', 'head')
    if s == 3:
        hinge = H - hup * hh * .2 - roll @ Vector((0, hd * .3, 0))
        jaw_c = H - hup * (hh * .55) + roll @ Vector((0, hd * .12, 0))
        bones.append(('jaw', hinge, hinge + roll @ Vector((0, hd * .6, -.05)), 'head'))
        box('jaw', jaw_c, hw * .9, hd * .85, hh * .32, 'skin', 'jaw', up=hup, cells={1: 'dark_blood', 'top': 'dark_blood'})
        for i in range(5):
            x = (i - 2) / 2 * hw * .36
            base = jaw_c + hup * hh * .16 + roll @ Vector((x, hd * .36, 0))
            prism('tooth_lo', base, base + hup * .06, .025, .025, .006, .006, cell='teeth', bone='jaw', sides=4)
            top = H - hup * hh * .5 + roll @ Vector((x * 1.05 + .02, hd * .38, 0))
            prism('tooth_hi', top, top - hup * .055, .024, .024, .006, .006, cell='teeth', bone='head', sides=4)
        quad('face_gash', fz - hup * hh * .1, roll @ Vector((0, 1, 0)), hup, .03, hh * .6, 'blood', 'head', roll=.15)
    # ---- legs
    for sd, sg in (('L', 1), ('R', -1)):
        pts = [Vector((sg * x, y, z)) for x, y, z in L['legs']]
        names = (['thigh', 'shin', 'shin2'][:len(pts) - 1])
        bnames = [f'{b}.{sd}' for b in names]
        parent = 'hips'
        for bn, a, b in zip(bnames, pts, pts[1:]):
            bones.append((bn, a, b, parent)); parent = bn
        cells = ['suit'] * (len(pts) - 1)
        if s == 3:
            cells[-1] = 'skin'
        chain(f'trouser_{sd}', pts, L['leg_w'], cells, bnames, depth=1.0)
        ankle = pts[-1]
        if s >= 2:  # trouser rides up: bare bone-grey ankle
            prism(f'ankle_{sd}', ankle + Vector((0, 0, .1)), ankle, .07, .07, .06, .06, cell='bone', bone=bnames[-1], sides=5)
        toe = ankle + Vector((0, .24 + .04 * s, -ankle.z))
        bones.append((f'foot.{sd}', ankle, Vector((ankle.x, toe.y, .03)), bnames[-1]))
        box(f'shoe_{sd}', Vector((ankle.x, (ankle.y + toe.y) / 2 - .02, .045)), .12, (toe.y - ankle.y) + .08, .09, 'coal', f'foot.{sd}', up=Z)
    # ---- arms
    for sd, sg in (('L', 1), ('R', -1)):
        pts = [Vector((sg * x, y, z)) for x, y, z in L['arm']]
        n = len(pts) - 1
        bnames = [f'upper_arm.{sd}'] + [f'fore{i}.{sd}' for i in range(1, n)]
        parent = 'chest'
        for bn, a, b in zip(bnames, pts, pts[1:]):
            bones.append((bn, a, b, parent)); parent = bn
        tip = Vector((sg * L['tip'][0], L['tip'][1], L['tip'][2]))
        bones.append((f'hand.{sd}', pts[-1], tip, bnames[-1]))
        cells = ['suit'] * n
        if s == 2:
            cells[-1] = 'skin'
        if s == 3:
            cells = ['suit', 'suit', 'skin', 'bone'] if sd == 'L' else ['skin', 'bone', 'skin', 'bone']
        chain(f'sleeve_{sd}', pts, L['sleeve'], cells, bnames, depth=1.0)
        if s == 3 and sd == 'R':  # ragged stump of the missing sleeve
            prism('sleeve_rag', pts[0] + Vector((0, 0, .05)), lerp(pts[0], pts[1], .3), .16, .16, .13, .13, cell='suit', bone=bnames[0], sides=5)
        # sleeve patches on the upper arm's outer face
        a, b = pts[0], pts[1]
        ax, sv, dv = frame(a, b)
        out = sv if sv.x * sg > 0 else -sv
        mid = lerp(a, b, .32) + out * (L['sleeve'][0] / 2 + .012)
        if not (s == 3 and sd == 'R'):
            quad(f'patch_sleeve_{sd}', mid, out, -ax, .125, .0625, 'patch_deshaw' if sd == 'L' else 'patch_balyasny',
                 bnames[0], roll=L['patch_roll'] * sg, pivot=mid - ax * .03 + (-out).cross(-ax).normalized() * .06)
        # cuff, hand
        w = pts[-1]
        cuff_from = lerp(pts[-2], w, .85)
        prism(f'cuff_{sd}', cuff_from, w, .1, .1, .095, .095, cell='blood' if s >= 2 else 'shirt', bone=bnames[-1], sides=5)
        if L['fingers'] == 0:
            prism(f'hand_{sd}', w, tip, .09, .045, .075, .04, cell='skin', bone=f'hand.{sd}')
        else:
            k = L['fingers']
            hx, hs, hd_ = frame(w, tip)
            for i in range(k):
                off = (i - (k - 1) / 2) * .035
                f_tip = tip + hs * off * 1.8
                prism(f'finger_{sd}', w + hs * off * .6, f_tip, .03, .03, .012, .012, cell='skin', bone=f'hand.{sd}', sides=4)
                if s == 3:
                    prism(f'claw_{sd}', f_tip, f_tip + (f_tip - w).normalized() * .07, .014, .014, .002, .002, cell='bone', bone=f'hand.{sd}', sides=4)
    # ---- marks: pass + lanyard (front), back patch, chest patch, hip badge, tie
    collar = N + dep * (L['chest'][1] / 2 + .02) - axis * .02
    card_h = .23   # oversized on purpose: it must read through the PS1 downsample at chase range
    card_c = Vector((0, collar.y + .05, collar.z - (.3 if s == 1 else .34)))
    bones.append(('pass', collar, card_c - Vector((0, 0, card_h / 2)), 'chest'))
    quad('pass_card', card_c, (0, 1, 0), Z, .46, card_h, 'pass_citadel', 'pass', roll=(.18 if s == 3 else 0))
    quad('pass_back', card_c - Vector((0, .004, 0)), (0, -1, 0), Z, .46, card_h, 'card', 'pass')
    for sg in (1, -1):
        top = collar + Vector((sg * .07, -.02, .02))
        bot = card_c + Vector((sg * .17, 0, card_h / 2))
        mid = lerp(top, bot, .5)
        quad('lanyard', mid, (0, 1, 0), (top - bot).normalized(), .028, (top - bot).length, 'ribbon', 'pass')
    tie_top = collar + Vector((0, .012, .01))
    seg = .13 if s < 3 else .15
    bones.append(('tie', tie_top, tie_top - Vector((0, 0, seg * L['tie'])), 'chest'))
    quad('tie_knot', tie_top - Vector((0, 0, .03)), (0, 1, 0), Z, .07, .06, 'rust', 'tie')
    for i in range(L['tie']):
        quad('tie', tie_top - Vector((0, 0, .06 + seg * (i + .5))), (0, 1, 0), Z, .075 - .004 * i * (s < 3), seg, 'tie_twosigma', 'tie')
    back_axis, back_side, back_dep = axis, side, -dep
    back_c = lerp(M, N, .6) - dep * (L['chest'][1] / 2 + .012)
    quad('patch_back', back_c, back_dep, back_axis, .46, .23, 'patch_point72', 'chest', roll=L['patch_roll'] * .4,
         pivot=back_c + back_axis * .09 - back_side * .19)
    lo_axis, lo_side, lo_dep = frame(P, M)
    strip_c = lerp(P, M, .75) - lo_dep * ((L['waist'][1] + L['chest'][1]) / 4 + .012)
    quad('patch_rentec', strip_c, -lo_dep, lo_axis, .2, .1, 'strip_rentec', 'spine')
    chest_c = front(.7, .012) + side * .14
    quad('patch_chest', chest_c, dep, axis, .13, .065, 'patch_millennium', 'chest', roll=-L['patch_roll'],
         pivot=chest_c + axis * .03 + side * .065)
    badge_c = P + Vector((-.13, L['waist'][1] / 2 + .03, -.08))
    bones.append(('badge', badge_c + Vector((0, 0, .08)), badge_c - Vector((0, 0, .04)), 'hips'))
    quad('badge_clip', badge_c + Vector((0, -.004, .06)), (0, 1, 0), Z, .02, .06, 'ribbon', 'badge')
    quad('badge_man', badge_c, (0, 1, 0), Z, .13, .065, 'badge_man', 'badge')
    # ---- stage 3 horrors: ribs through the split back, extra arms from the vents, wounds
    if s == 3:
        quad('back_split', lerp(M, N, .45) - dep * (L['chest'][1] / 2 + .016), -dep, axis, .1, .5, 'dark_blood', 'chest')
        for i in range(4):
            c = lerp(M, N, .15 + .2 * i) - dep * (L['chest'][1] / 2 - .02)
            for sg in (1, -1):
                prism('rib', c, c + side * sg * .2 - dep * .18 + axis * .05, .035, .035, .01, .01, cell='bone', bone='chest', sides=4)
        for sd, sg in (('L', 1), ('R', -1)):
            vent = P + Vector((sg * .16, -.06, .12))
            el = vent + Vector((sg * .16, .14, -.22))
            hand = el + Vector((sg * .02, .22, -.12))
            bones += [(f'xarm.{sd}', vent, el, 'spine'), (f'xfore.{sd}', el, hand, f'xarm.{sd}')]
            prism('xarm', vent, el, .05, .05, .04, .04, cell='skin', bone=f'xarm.{sd}', sides=4)
            prism('xfore', el, hand, .04, .04, .02, .02, cell='bone', bone=f'xfore.{sd}', sides=4)
            for i in range(3):
                prism('xfinger', hand, hand + Vector((sg * (i - 1) * .03, .07, -.03)), .015, .015, .004, .004, cell='skin', bone=f'xfore.{sd}', sides=4)
        for c, n in [(front(.45, .02) - side * .12, dep), (lerp(P, M, .5) + Vector((.19, .05, 0)), Vector((1, 0, 0)))]:
            quad('wound', c, n, Z, .06, .1, 'EYE', 'chest' if n == dep else 'spine', roll=.3)
    return bones


# ---------------------------------------------------------------- animation
def rot(r, bone, axis, angle):
    p = r.pose.bones.get(bone)
    if p is None or not angle:
        return
    p.rotation_mode = 'QUATERNION'
    local = p.bone.matrix_local.to_quaternion().inverted() @ Vector(axis)
    p.rotation_quaternion = Quaternion(local, angle) @ p.rotation_quaternion


def win(t, a, b):
    if t <= a or t >= b:
        return 0.0
    return math.sin(math.pi * (t - a) / (b - a)) ** 2


def smooth(a, b, t):
    x = min(1, max(0, (t - a) / (b - a)))
    return x * x * (3 - 2 * x)


X, Y = (1, 0, 0), (0, 1, 0)


def arm_bones(rig, sd):
    names = [b.name for b in rig.data.bones]
    return [f'upper_arm.{sd}'] + sorted(n for n in names if n.startswith('fore') and n.endswith('.' + sd)) + [f'hand.{sd}']


def leg_bones(rig, sd):
    return [n for n in (f'thigh.{sd}', f'shin.{sd}', f'shin2.{sd}') if n in rig.data.bones]


def make_actions(rig, s):
    lean = {1: .05, 2: .1, 3: .12}[s]
    arms = {sd: arm_bones(rig, sd) for sd in 'LR'}
    legs = {sd: leg_bones(rig, sd) for sd in 'LR'}

    def legs_gait(r, p, amp, bend, heel=0.0):
        for sd, ph in (('L', 0), ('R', math.pi)):
            s_, c_ = math.sin(p + ph), math.cos(p + ph)
            thigh = amp * s_
            knee = -bend * max(0.0, c_) - .05
            rot(r, legs[sd][0], X, thigh)
            if len(legs[sd]) == 3:   # reversed second knee
                rot(r, legs[sd][1], X, knee * .6)
                rot(r, legs[sd][2], X, -knee * .5)
                foot = -(thigh + knee * .1)
            else:
                rot(r, legs[sd][1], X, knee)
                foot = -(thigh + knee)
            rot(r, f'foot.{sd}', X, foot * .85 + heel * max(0.0, s_))

    def idle(r, t):
        b = math.sin(t * math.tau)
        rot(r, 'chest', X, .02 * b)
        rot(r, 'head', Y, .32 * win(t, .12, .5) * (1 if s != 2 else -1.3))
        rot(r, 'head', (0, 0, 1), .08 * win(t, .2, .45))
        g = win(t, .55, .9)   # straightens the tie
        rot(r, arms['R'][0], X, .75 * g)
        rot(r, arms['R'][0], Y, -.35 * g)
        for bn in arms['R'][1:-1]:
            rot(r, bn, X, 1.9 / (len(arms['R']) - 2) * g)
        rot(r, 'tie', Y, .12 * win(t, .7, .85))
        for sd, sg in (('L', 1), ('R', -1)):
            rot(r, arms[sd][0], Y, -sg * .03 * (1 + b))
        rot(r, 'jaw', X, -.12 - .08 * (1 + math.sin(t * math.tau * 2)) / 2)
        for sd in 'LR':  # out-of-sync twitching
            rot(r, f'xarm.{sd}', X, .35 * round(math.sin(t * math.tau * (5 if sd == 'L' else 7) + (1 if sd == 'R' else 0))))
            rot(r, f'xfore.{sd}', X, .4 * max(0, math.sin(t * math.tau * 3 + (sd == 'R'))))
        rot(r, 'pass', X, .03 * b)

    def stalk(r, t):
        p = t * math.tau
        legs_gait(r, p, {1: .3, 2: .34, 3: .4}[s], .55, heel=.2)
        common.root_move(r, (0, 0, .012 * math.cos(2 * p)))
        rot(r, 'spine', Y, .025 * math.sin(p))
        rot(r, 'spine', X, -lean * .5)
        rot(r, 'head', Y, -.025 * math.sin(p))       # head stays locked on the player
        rot(r, 'head', X, lean * .5)
        for sd, ph in (('L', 0), ('R', math.pi)):
            rot(r, arms[sd][0], X, .04 * math.sin(p + ph + math.pi))
        rot(r, 'tie', X, .05 + .04 * math.sin(2 * p))
        rot(r, 'pass', X, .06 + .05 * math.sin(2 * p + .4))
        for sd in 'LR':
            rot(r, f'xarm.{sd}', X, .3 * math.sin(p * 2 + (sd == 'R') * 2))

    def chase(r, t):
        p = t * math.tau
        gallop = s == 3
        legs_gait(r, p, {1: .7, 2: .75, 3: .6}[s], 1.1)
        common.root_move(r, (0, 0, .06 * abs(math.sin(p))))
        rot(r, 'spine', X, -(.22 + lean + (.25 if gallop else 0)))
        rot(r, 'chest', X, -.1 - .05 * math.sin(2 * p))
        rot(r, 'head', X, .3 + (.3 if gallop else 0))
        for sd, sg, ph in (('L', 1, math.pi), ('R', -1, .7)):
            sw = math.sin(p + ph)
            if gallop:   # knuckle gallop: both arms paw forward together
                sw = math.sin(p + (0 if sd == 'L' else .5))
                rot(r, arms[sd][0], X, .2 + .55 * sw)
            else:        # flailing, asymmetric
                rot(r, arms[sd][0], X, .25 + .95 * sw)
                rot(r, arms[sd][0], Y, -sg * (.3 + .2 * math.sin(2 * p + ph)))
            for bn in arms[sd][1:-1]:
                rot(r, bn, X, .25 + .35 * max(0.0, sw))
        rot(r, 'tie', X, .55 + .3 * math.sin(2 * p))       # flaps out from the chest
        rot(r, 'pass', X, .45 + .3 * math.sin(2 * p + .5))
        rot(r, 'badge', X, .4 + .3 * math.sin(2 * p + 1))
        rot(r, 'jaw', X, -.35 - .1 * math.sin(2 * p))
        for sd in 'LR':
            rot(r, f'xarm.{sd}', X, .6 * math.sin(p * 2 + (sd == 'R')))

    def lunge(r, t):
        wind = smooth(0, .33, t) * (1 - smooth(.33, .46, t))
        strike = smooth(.33, .55, t)
        close = smooth(.5, .66, t)
        common.root_move(r, (0, .9 * strike, -.06 * wind))
        rot(r, 'spine', X, .3 * wind - .45 * strike)
        rot(r, 'chest', X, .1 * wind - .2 * strike)
        rot(r, 'head', X, -.15 * wind + .35 * strike)
        for sd, sg in (('L', 1), ('R', -1)):
            rot(r, arms[sd][0], Y, -sg * (1.2 * wind + .15 * strike))
            rot(r, arms[sd][0], X, (1.45 if s < 3 else 1.1) * strike)
            for bn in arms[sd][1:-1]:
                rot(r, bn, X, .5 * strike / max(1, len(arms[sd]) - 2) + .3 * close)
            rot(r, arms[sd][-1], X, .7 * close)
        rot(r, legs['L'][0], X, .45 * strike)
        rot(r, legs['R'][0], X, -.35 * strike)
        rot(r, legs['R'][1], X, -.3 * strike)
        rot(r, 'tie', X, .6 * strike - .3 * close)
        rot(r, 'pass', X, .5 * strike - .25 * close)
        rot(r, 'jaw', X, -.15 - .55 * strike)
        for sd in 'LR':
            rot(r, f'xarm.{sd}', X, .9 * strike)

    for name, fn in (('chase', chase), ('idle', idle), ('lunge', lunge), ('stalk', stalk)):
        common.action(rig, name, FRAMES[name], fn)


# ---------------------------------------------------------------- build + export
def build(s):
    clear_scene()
    _names.clear()
    bones = build_parts(s)
    # scale to the height the game expects
    lo, hi = common.bounds()
    k = HEIGHT[s] / (hi.z - lo.z)
    S = Matrix.Scale(k, 4)
    for o in PARTS:
        o.data.transform(S)
    bones = [(n, Vector(h) * k, Vector(t) * k, p) for n, h, t, p in bones]
    name = f'beast_stage{s}'
    rig = common.skeleton(name, bones)
    mesh = common.join_parts(name + '_mesh')
    make_actions(rig, s)
    rig.animation_data.action = bpy.data.actions['idle']
    bpy.context.scene.frame_set(1)
    return rig, mesh


def export(s, rig, previews=True):
    name = f'beast_stage{s}'
    scene = bpy.context.scene
    rig.animation_data.action = None
    for p in rig.pose.bones:
        p.location = (0, 0, 0); p.rotation_quaternion = (1, 0, 0, 0); p.scale = (1, 1, 1)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    lo, hi = common.bounds()
    for o in PARTS:
        o.data.calc_loop_triangles()
    tris = sum(len(o.data.loop_triangles) for o in PARTS)
    acts = sorted(bpy.data.actions, key=lambda a: a.name)
    clips = [{'name': a.name, 'frame_start': 1, 'frame_end': a['frame_end'], 'fbx_frame_start': 0,
              'fbx_frame_end': a['frame_end'] - 1, 'fps': common.FPS, 'loop': bool(a['loop']),
              'duration_seconds': (a['frame_end'] - 1) / common.FPS} for a in acts]
    bpy.ops.object.select_all(action=enum(bpy.ops.object.select_all, 'action', 'DESELECT'))
    for o in PARTS + [rig]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    model_dir = ROOT / 'Unity/Assets/Resources/Models'
    # Unlike the rest of the kit (which faces FBX -Z), the beast is turned 180 deg so it faces Unity +Z:
    # BeastController drives it with LookRotation and leaves the model's local rotation at identity.
    conversion = Matrix.Rotation(-math.pi / 2, 4, 'X') @ Matrix.Rotation(math.pi, 4, 'Z')
    for o in PARTS:
        o.data.transform(conversion)
    rig.data.transform(conversion)
    bpy.context.view_layer.update()
    for act in acts:
        track = rig.animation_data.nla_tracks.new()
        track.name = act.name
        strip = track.strips.new(act.name, 1, act)
        strip.name = act.name
    fbx = bpy.ops.export_scene.fbx
    fbx(filepath=str(model_dir / f'{name}.fbx'), use_selection=True,
        object_types={enum(fbx, 'object_types', t) for t in ['MESH', 'ARMATURE']},
        axis_forward=enum(fbx, 'axis_forward', '-Z'), axis_up=enum(fbx, 'axis_up', 'Y'),
        use_space_transform=False, bake_space_transform=False,
        apply_unit_scale=True, apply_scale_options=enum(fbx, 'apply_scale_options', 'FBX_SCALE_UNITS'),
        add_leaf_bones=False, use_triangles=True, bake_anim=True,
        bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_simplify_factor=0, bake_anim_step=1, use_custom_props=True,
        path_mode=enum(fbx, 'path_mode', 'RELATIVE'), embed_textures=False)
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
    for o in PARTS:
        o.data.transform(conversion.inverted())
    rig.data.transform(conversion.inverted())
    bpy.context.view_layer.update()
    info = {
        'resource_path': f'Models/{name}',
        'files': {'fbx': f'Unity/Assets/Resources/Models/{name}.fbx', 'blend': f'assets/blender/{name}.blend',
                  'texture': 'Unity/Assets/Resources/Models/Textures/beast_ceo_atlas.png',
                  'preview': f'assets/previews/{name}.png', 'views': f'assets/previews/{name}_views.png'},
        'triangles': tris,
        'bounding_box': {'min': [round(-hi.x, 5), round(lo.z, 5), round(lo.y, 5)],
                         'max': [round(-lo.x, 5), round(hi.z, 5), round(hi.y, 5)]},
        'forward_axis': '+Z (faces Unity forward; the rest of the kit faces -Z)',
        'clips': clips, 'rigged': True, 'mesh_names': [o.name for o in PARTS], 'notes': NOTES.format(s=s),
        'source': 'tools/blender/ceo_beast.py (atlas: tools/blender/ceo_beast_atlas.py)',
        'backup_of_previous': 'assets/backup/beast_drowned_witness/',
    }
    out = ROOT / 'assets/previews/.principal'
    out.mkdir(parents=True, exist_ok=True)
    (out / f'{name}.json').write_text(json.dumps(info, indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'assets/blender' / f'{name}.blend'), copy=True)
    if previews:
        camera, center, span = common.setup_preview(lo, hi)
        scene.frame_set(1)
        scene.render.filepath = str(ROOT / 'assets/previews' / f'{name}.png')
        bpy.ops.render.render(write_still=True)
        common.preview_views(name, camera, center, span)
    print(f'PRINCIPAL_DONE {name}: {tris} tris, bounds {tuple(round(v, 3) for v in lo)} {tuple(round(v, 3) for v in hi)}')
    return info


if __name__ == '__main__' and '--' in sys.argv:
    for st in [int(a) for a in sys.argv[sys.argv.index('--') + 1:]]:
        r, _ = build(st)
        export(st, r)

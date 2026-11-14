"""Deterministic low-poly asset construction, action baking, exports and previews.

Author in Blender metres, Z up, +Y forward. FBX stores Y up, -Z forward.
"""
import bpy
import json
import math
import os
import random
import subprocess
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

ROOT = Path(__file__).resolve().parents[2]
FPS = 24
PALETTE = {
    'coal': ((.018, .024, .028), 0, .88),
    'iron': ((.07, .085, .09), .35, .75),
    'rust': ((.28, .075, .035), .15, .9),
    'orange': ((.55, .16, .045), .1, .85),
    'bone': ((.64, .59, .43), 0, .8),
    'ivory': ((.85, .80, .64), 0, .75),
    'blood': ((.38, .006, .012), .05, .36),
    'flesh': ((.32, .008, .016), 0, .62),
    'viscera': ((.14, .004, .015), 0, .44),
    'visor': ((.025, .065, .065), .55, .21),
    'eye': ((1.0, .045, .015), 0, .45),
    'signal': ((.22, .8, .65), 0, .45),
    'screen': ((.005, .025, .022), 0, .8),
}
MATERIALS = {}
PARTS = []
WEIGHTS = {}


def enum(owner, prop, wanted):
    """Resolve Blender's runtime enum identifiers, including dynamic operator enums."""
    rna = owner.get_rna_type() if hasattr(owner, 'get_rna_type') else owner.bl_rna
    items = rna.properties[prop].enum_items
    match = next((i.identifier for i in items if i.identifier == wanted or i.name == wanted), None)
    if match is None and not hasattr(owner, 'get_rna_type'):
        try:
            setattr(owner, prop, '__ENUM_QUERY__')
        except TypeError as exc:
            import ast, re
            found = re.search(r"not found in (\(.*\))", str(exc))
            if found:
                match = next((v for v in ast.literal_eval(found[1]) if v == wanted), None)
    if match is None:
        # Dynamic enum callbacks expose their accepted values through the operator error.
        if hasattr(owner, 'get_rna_type') and not len(items):
            try:
                owner(**{prop: '__ENUM_QUERY__'})
            except TypeError as exc:
                import ast
                import re
                found = re.search(r"not found in (\(.*\))", str(exc))
                if found:
                    match = next((v for v in ast.literal_eval(found[1]) if v == wanted), None)
        if match is None:
            raise ValueError(f'{prop}: missing {wanted}; have {[i.identifier for i in items]}')
    return match


def reset(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    PARTS.clear()
    WEIGHTS.clear()
    MATERIALS.clear()
    random.seed(1337)
    scene = bpy.context.scene
    scene.unit_settings.system = enum(scene.unit_settings, 'system', 'METRIC')
    scene.unit_settings.scale_length = 1
    scene.render.fps = FPS
    scene['asset_name'] = name
    scene['authoring_axes'] = 'Z up, +Y forward; exported Y up, -Z forward'
    for key, (color, metal, rough) in PALETTE.items():
        mat = bpy.data.materials.new(key)
        mat.diffuse_color = (*color, 1)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (*color, 1)
        bsdf.inputs['Metallic'].default_value = metal
        bsdf.inputs['Roughness'].default_value = rough
        if key in {'eye', 'signal'}:
            bsdf.inputs['Emission Color'].default_value = (*color, 1)
            bsdf.inputs['Emission Strength'].default_value = 3
        mat.use_backface_culling = False
        MATERIALS[key] = mat


def finish_mesh(obj, name, material, bone=None):
    obj.name = name
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.materials.append(MATERIALS[material])
    for poly in obj.data.polygons:
        poly.use_smooth = False
    PARTS.append(obj)
    if bone:
        WEIGHTS[obj.name] = bone
    return obj


def box(name, at, size, material, bone=None, bevel=0, rotation=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    obj = bpy.context.object
    obj.scale = size
    if rotation:
        obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('chipped_edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish_mesh(obj, name, material, bone)


def ellipsoid(name, at, size, material, bone=None, segments=8, rings=4):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=at)
    obj = bpy.context.object
    obj.scale = size
    return finish_mesh(obj, name, material, bone)


def ico(name, at, size, material, bone=None, subdivisions=1, jagged=0):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1, location=at)
    obj = bpy.context.object
    for v in obj.data.vertices:
        v.co *= 1 + random.uniform(-jagged, jagged)
    obj.scale = size
    return finish_mesh(obj, name, material, bone)


def rod(name, a, b, r1, r2, material, bone=None, vertices=6):
    delta = Vector(b) - Vector(a)
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2,
                                    depth=delta.length, location=(Vector(a) + Vector(b)) / 2)
    obj = bpy.context.object
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = delta.to_track_quat('Z', 'Y')
    return finish_mesh(obj, name, material, bone)


def mesh(name, verts, faces, material, bone=None, uv=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    if uv:
        layer = data.uv_layers.new(name='UVMap')
        for poly in data.polygons:
            for i, li in enumerate(poly.loop_indices):
                layer.data[li].uv = [(0, 0), (1, 0), (1, 1), (0, 1)][i % 4]
    return finish_mesh(obj, name, material, bone)


def front_quad(name, cx, y, z, w, h, material, bone=None):
    # Outward normal +Y, readable from the asset's front; UV origin bottom left.
    return mesh(name, [(cx+w/2,y,z-h/2),(cx-w/2,y,z-h/2),
                       (cx-w/2,y,z+h/2),(cx+w/2,y,z+h/2)], [(0,1,2,3)], material, bone, True)


def skeleton(name, bones):
    bpy.ops.object.select_all(action='DESELECT')
    data = bpy.data.armatures.new(name + '_skeleton')
    rig = bpy.data.objects.new(name + '_rig', data)
    bpy.context.collection.objects.link(rig)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for bn, head, tail, parent in bones:
        b = data.edit_bones.new(bn)
        b.head, b.tail = head, tail
        if parent:
            b.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front = True
    for obj in PARTS:
        bn = WEIGHTS.get(obj.name, 'root')
        group = obj.vertex_groups.new(name=bn)
        group.add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
        mod = obj.modifiers.new('rig', 'ARMATURE')
        mod.object = rig
        obj.parent = rig
    return rig


def join_parts(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in PARTS:
        o.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    PARTS[:] = [obj]
    return obj


def world_rotate(rig, bone, axis, angle):
    p = rig.pose.bones.get(bone)
    if p:
        p.rotation_mode = 'QUATERNION'
        local_axis = p.bone.matrix_local.to_quaternion().inverted() @ Vector(axis)
        p.rotation_quaternion = Quaternion(local_axis, angle)


def action(rig, name, end, pose):
    rig.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for frame in range(1, end+1):
        t = (frame-1)/(end-1)
        for p in rig.pose.bones:
            p.rotation_mode = 'QUATERNION'
            p.location = (0, 0, 0)
            p.rotation_quaternion = (1, 0, 0, 0)
            p.scale = (1, 1, 1)
        pose(rig, t)
        for p in rig.pose.bones:
            p.keyframe_insert('location', frame=frame, group=p.name)
            p.keyframe_insert('rotation_quaternion', frame=frame, group=p.name)
            p.keyframe_insert('scale', frame=frame, group=p.name)
    act['loop'] = name in {'idle', 'walk', 'run', 'stalk', 'chase'}
    act['frame_start'] = 1
    act['frame_end'] = end
    return act


def root_move(rig, xyz):
    p = rig.pose.bones['root']
    p.location = p.bone.matrix_local.to_3x3().inverted() @ Vector(xyz)


def humanoid_actions(rig, creature=False, stage=1):
    def idle(r, t):
        s = math.sin(t*math.tau)
        world_rotate(r, 'chest', (1,0,0), s*.035)
        world_rotate(r, 'head', (0,0,1), s*.07)
        for side, sign in [('L',1),('R',-1)]:
            world_rotate(r, 'upper_arm.'+side, (0,1,0), sign*(.035+s*.025))
            world_rotate(r, 'extra_arm.'+side, (0,1,0), sign*(.1+s*.05))
        if creature:
            world_rotate(r, 'jaw.L', (0,1,0), .10+.05*s)
            world_rotate(r, 'jaw.R', (0,1,0), -.10-.05*s)
    action(rig, 'idle', 49, idle)
    for clip, amplitude, end in ([('stalk', .28, 49), ('chase', .85, 25)] if creature else [('walk',.42,33),('run',.8,21)]):
        def gait(r, t, amp=amplitude):
            s = math.sin(t*math.tau)
            root_move(r, (0,0,abs(s)*.025))
            world_rotate(r, 'chest', (1,0,0), -.16 if amp>.5 else -.04)
            for side, sign in [('L',1),('R',-1)]:
                swing = s*sign*amp
                world_rotate(r, 'thigh.'+side, (1,0,0), swing)
                world_rotate(r, 'shin.'+side, (1,0,0), -max(0,-s*sign)*amp*.9)
                world_rotate(r, 'upper_arm.'+side, (1,0,0), -swing*.85)
                world_rotate(r, 'forearm.'+side, (1,0,0), .22+max(0,s*sign)*amp*.5)
                world_rotate(r, 'extra_arm.'+side, (1,0,0), swing*.8)
            world_rotate(r, 'head', (0,0,1), s*.03)
        action(rig, clip, end, gait)
    if creature:
        def lunge(r, t):
            crouch = math.sin(min(t/.3, 1)*math.pi/2)
            strike = max(0,min(1,(t-.3)/.4))
            root_move(r, (0, strike*.9, -.22*crouch+.14*strike))
            world_rotate(r, 'chest', (1,0,0), .15*crouch-.8*strike)
            world_rotate(r, 'head', (1,0,0), .3*strike)
            for side, sign in [('L',1),('R',-1)]:
                world_rotate(r, 'upper_arm.'+side, (1,0,0), 1.35*strike-.25*crouch)
                world_rotate(r, 'extra_arm.'+side, (0,1,0), sign*.65*strike)
                world_rotate(r, 'thigh.'+side, (1,0,0), .3*crouch)
                world_rotate(r, 'shin.'+side, (1,0,0), -.5*crouch)
                world_rotate(r, 'jaw.'+side, (0,1,0), sign*.6*strike)
        action(rig, 'lunge', 25, lunge)
    else:
        def martyr(r, t):
            drop = min(1,t/.58)
            drop = drop*drop*(3-2*drop)
            surrender = max(0,min(1,(t-.35)/.55))
            surrender = surrender*surrender*(3-2*surrender)
            # Vertical thighs and folded shins end with the knees at floor height.
            root_move(r, (0,0,-.34*drop))
            for side, sign in [('L',1),('R',-1)]:
                world_rotate(r, 'shin.'+side, (1,0,0), -math.pi/2*drop)
                world_rotate(r, 'foot.'+side, (1,0,0), math.pi/2*drop)
                world_rotate(r, 'upper_arm.'+side, (0,1,0), -sign*1.16*surrender)
                world_rotate(r, 'forearm.'+side, (1,0,0), .30*surrender)
            world_rotate(r, 'head', (1,0,0), .55*surrender)
            world_rotate(r, 'chest', (1,0,0), .13*surrender)
        action(rig, 'death_martyr', 73, martyr)


def ground():
    """Place irregular geometry on its base without moving its object origin."""
    low = min(v.co.z for obj in PARTS for v in obj.data.vertices)
    for obj in PARTS:
        for v in obj.data.vertices:
            v.co.z -= low


def bounds():
    points = [o.matrix_world @ v.co for o in PARTS for v in o.data.vertices]
    lo = Vector([min(p[i] for p in points) for i in range(3)])
    hi = Vector([max(p[i] for p in points) for i in range(3)])
    return lo, hi


def aim(obj, target):
    obj.rotation_euler = (Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()


def setup_preview(lo, hi):
    scene = bpy.context.scene
    scene.render.engine = enum(scene.render, 'engine', 'EEVEE')
    scene.render.resolution_x = 720
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = enum(scene.render.image_settings, 'file_format', 'PNG')
    scene.world = bpy.data.worlds.new('blood_fog')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.018,.022,.029,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .2
    scene.view_settings.view_transform = enum(scene.view_settings, 'view_transform', 'AgX')
    span = max(hi-lo)
    center = (lo+hi)/2
    bpy.ops.object.camera_add(location=center+Vector((span*.95,span*1.45,span*.75)))
    camera = bpy.context.object
    camera.name = 'preview_camera'
    camera.data.type = enum(camera.data, 'type', 'ORTHO')
    camera.data.ortho_scale = span*1.4
    aim(camera, center)
    scene.camera = camera
    for name, offset, color, power, size in [
        ('warm_key',(-1.1,1.5,1.8),(1,.73,.53),1100,1.5),
        ('cold_fill',(1.4,1,.8),(.45,.68,.75),650,1.4),
        ('blood_rim',(-.6,-1,1.5),(1,.035,.015),1500,1.1),
    ]:
        bpy.ops.object.light_add(type='AREA', location=center+Vector(offset)*span)
        light = bpy.context.object
        light.name = name
        light.data.energy = power*span*span/18
        light.data.color = color
        light.data.shape = enum(light.data, 'shape', 'DISK')
        light.data.size = size*span
        aim(light, center)
    # Preview stage is deliberately excluded from exports and triangle counts.
    bpy.ops.mesh.primitive_plane_add(size=span*200, location=(0,0,lo.z-.025))
    floor = bpy.context.object
    floor.name = 'preview_floor'
    floor.data.materials.append(MATERIALS['coal'])
    return camera, center, span


def ffmpeg_path():
    candidates = [os.environ.get('FFMPEG'), '/opt/homebrew/bin/ffmpeg',
                  '/opt/homebrew/opt/ffmpeg@6/bin/ffmpeg']
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            result = subprocess.run([candidate, '-version'], capture_output=True)
            if result.returncode == 0:
                return candidate
    raise RuntimeError('No working ffmpeg found. Set FFMPEG to an installed executable.')


def preview_video(name, rig, camera, center, span):
    if os.environ.get('ART_SKIP_VIDEO') == '1':
        return
    scene = bpy.context.scene
    scene.render.resolution_x = 480
    scene.render.resolution_y = 480
    # Temporal preview uses the same materials and lighting as the stills.
    frame_dir = ROOT/'assets/previews'/('.frames_'+name)
    frame_dir.mkdir(exist_ok=True)
    scene.eevee.taa_render_samples = 16
    clip_order = ['idle','walk','run','death_martyr','stalk','chase','lunge','hatch_close','takeoff','open']
    acts = sorted(bpy.data.actions, key=lambda a: clip_order.index(a.name))
    frames_per_clip = 24
    number = 0
    for clip_index, act in enumerate(acts):
        rig.animation_data.action = act
        for i in range(frames_per_clip):
            number += 1
            scene.frame_set(round(1+(act['frame_end']-1)*i/(frames_per_clip-1)))
            angle = .35 + .65*math.sin((number-1)/(len(acts)*frames_per_clip)*math.tau)
            focus = center.copy()
            if act.name == 'takeoff':
                focus += rig.pose.bones['root'].matrix.translation
            camera.location = focus+Vector((math.sin(angle)*span*1.8,math.cos(angle)*span*1.8,span*.65))
            aim(camera, focus)
            scene.render.filepath = str(frame_dir/f'{number:04d}.png')
            bpy.ops.render.render(write_still=True)
    filters = []
    font = '/System/Library/Fonts/Menlo.ttc'
    for idx, act in enumerate(acts):
        filters.append(f"drawtext=fontfile={font}:text='{name} / {act.name}':fontcolor=white:fontsize=15:x=14:y=18:box=1:boxcolor=black@0.6:enable='between(t,{idx*2},{idx*2+1.999})'")
    subprocess.run([ffmpeg_path(),'-y','-loglevel','error','-framerate','12',
                    '-i',str(frame_dir/'%04d.png'),'-vf',','.join(filters),'-c:v','libx264',
                    '-pix_fmt','yuv420p','-movflags','+faststart','-crf','20',
                    str(ROOT/'assets/previews'/f'{name}.mp4')],check=True)
    import shutil
    shutil.rmtree(frame_dir)
    manifest_path = ROOT/'Unity/Assets/Resources/Models/manifest.json'
    manifest = json.loads(manifest_path.read_text())
    manifest['models'][name]['files']['video'] = f'assets/previews/{name}.mp4'
    manifest['models'][name]['preview_segments'] = [
        {'clip':act.name, 'start_seconds':i*2, 'end_seconds':(i+1)*2} for i,act in enumerate(acts)]
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')


def deliver(name, rig=None, notes=None):
    scene = bpy.context.scene
    if rig:
        rig.animation_data.action = None
        for p in rig.pose.bones:
            p.location = (0,0,0)
            p.rotation_quaternion = (1,0,0,0)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    lo, hi = bounds()
    for obj in PARTS:
        obj.data.calc_loop_triangles()
    tris = sum(len(obj.data.loop_triangles) for obj in PARTS)
    clips = [{'name':a.name,'frame_start':1,'frame_end':a['frame_end'],
              'fbx_frame_start':0,'fbx_frame_end':a['frame_end']-1,
              'fps':FPS,'loop':bool(a['loop']), 'duration_seconds':(a['frame_end']-1)/FPS}
             for a in bpy.data.actions]
    bpy.ops.object.select_all(action='DESELECT')
    for o in PARTS + ([rig] if rig else []):
        o.select_set(True)
    model_dir = ROOT/'Unity/Assets/Resources/Models'
    model_dir.mkdir(parents=True, exist_ok=True)
    # Convert the actual geometry and bone rest matrices, not the root object.
    # Local bone animation channels remain valid because every rest matrix receives C.
    conversion = Matrix.Rotation(-math.pi/2, 4, 'X')
    for obj in PARTS:
        obj.data.transform(conversion)
    if rig:
        rig.data.transform(conversion)
    bpy.context.view_layer.update()
    if rig:
        for act in bpy.data.actions:
            track = rig.animation_data.nla_tracks.new()
            track.name = act.name
            strip = track.strips.new(act.name, 1, act)
            strip.name = act.name
    fbx = bpy.ops.export_scene.fbx
    fbx(filepath=str(model_dir/f'{name}.fbx'),use_selection=True,
        object_types={enum(fbx,'object_types',t) for t in ['MESH','ARMATURE']},
        axis_forward=enum(fbx,'axis_forward','-Z'),axis_up=enum(fbx,'axis_up','Y'),
        use_space_transform=False, bake_space_transform=False,
        apply_unit_scale=True,apply_scale_options=enum(fbx,'apply_scale_options','FBX_SCALE_UNITS'),
        add_leaf_bones=False,use_triangles=True,bake_anim=bool(rig),
        bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,
        bake_anim_simplify_factor=0,bake_anim_step=1, use_custom_props=True)
    if rig:
        for track in list(rig.animation_data.nla_tracks):
            rig.animation_data.nla_tracks.remove(track)
    for obj in PARTS:
        obj.data.transform(conversion.inverted())
    if rig:
        rig.data.transform(conversion.inverted())
    bpy.context.view_layer.update()
    manifest_path = model_dir/'manifest.json'
    manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {
        'schema_version':1,'frame_range_convention':'frame_start/end are Blender action frames; fbx_frame_start/end are zero-based FBX takes', 'units':'metres','up_axis':'+Y','forward_axis':'-Z','fps':FPS,'models':{}}
    manifest['models'][name] = {
        'resource_path':f'Models/{name}',
        'files':{'fbx':f'Unity/Assets/Resources/Models/{name}.fbx',
                 'blend':f'assets/blender/{name}.blend','preview':f'assets/previews/{name}.png',
                 **({'video':f'assets/previews/{name}.mp4'} if rig and os.environ.get('ART_SKIP_VIDEO') != '1' else {})},
        'triangles':tris,
        'bounding_box':{'min':[round(lo.x,5),round(lo.z,5),round(-hi.y,5)],
                        'max':[round(hi.x,5),round(hi.z,5),round(-lo.y,5)]},
        'clips':clips,'rigged':bool(rig),'mesh_names':[o.name for o in PARTS],
        'notes':notes or '',
    }
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    camera, center, span = setup_preview(lo,hi)
    if rig:
        rig.animation_data.action = bpy.data.actions.get('idle') or bpy.data.actions[0]
    scene.frame_set(1)
    scene.frame_end = max([a['frame_end'] for a in bpy.data.actions] or [1])
    scene.render.filepath = str(ROOT/'assets/previews'/f'{name}.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'assets/blender'/f'{name}.blend'))
    bpy.ops.render.render(write_still=True)
    if rig:
        preview_video(name,rig,camera,center,span)
    print(f'ASSET_DONE {name}: {tris} triangles, clips {[c["name"] for c in clips]}',flush=True)

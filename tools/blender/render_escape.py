"""Render the saved shuttle's takeoff above blood water into a pale horizon.

This adds preview-only scenery and never changes the source blend or any FBX.
ART_OUTPUT_ROOT selects an isolated asset root. ART_SKIP_VIDEO=1 renders only
start/middle/end inspection stills and the hero PNG.
"""
import json
import math
import os
import random
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import ROOT, FPS, aim, enum, ffmpeg_path
import bpy
from mathutils import Vector


def material(name, color, rough=.8, metal=0, emission=0):
    mat=bpy.data.materials.new(name)
    mat.diffuse_color=(*color,1)
    mat.use_nodes=True
    shader=mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=(*color,1)
    shader.inputs['Roughness'].default_value=rough
    shader.inputs['Metallic'].default_value=metal
    if emission:
        shader.inputs['Emission Color'].default_value=(*color,1)
        shader.inputs['Emission Strength'].default_value=emission
    return mat


def geometry(name, vertices, faces, mat):
    data=bpy.data.meshes.new(name)
    data.from_pydata(vertices,[],faces)
    data.update()
    obj=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons:
        polygon.use_smooth=False
    return obj


def world():
    sky=bpy.data.worlds.new('escape_dawn')
    sky.use_nodes=True
    nodes=sky.node_tree.nodes
    nodes.clear()
    coord=nodes.new('ShaderNodeTexCoord')
    separate=nodes.new('ShaderNodeSeparateXYZ')
    remap=nodes.new('ShaderNodeMapRange')
    remap.inputs['From Min'].default_value=-.18
    remap.inputs['From Max'].default_value=.65
    ramp=nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.interpolation=enum(ramp.color_ramp,'interpolation','EASE')
    colors=[(0,(.035,.008,.012,1)),(.16,(.46,.20,.12,1)),
            (.24,(.72,.59,.41,1)),(.43,(.27,.31,.35,1)),(1,(.018,.035,.058,1))]
    ramp.color_ramp.elements.remove(ramp.color_ramp.elements[1])
    for index,(position,color) in enumerate(colors):
        element=ramp.color_ramp.elements[0] if index==0 else ramp.color_ramp.elements.new(position)
        element.position=position
        element.color=color
    background=nodes.new('ShaderNodeBackground')
    background.inputs['Strength'].default_value=.7
    output=nodes.new('ShaderNodeOutputWorld')
    links=sky.node_tree.links
    links.new(coord.outputs['Normal'],separate.inputs[0])
    links.new(separate.outputs['Z'],remap.inputs['Value'])
    links.new(remap.outputs['Result'],ramp.inputs[0])
    links.new(ramp.outputs['Color'],background.inputs['Color'])
    links.new(background.outputs[0],output.inputs['Surface'])
    bpy.context.scene.world=sky


def dress_scene():
    random.seed(142)
    dark=material('escape_ironstone',(.018,.019,.024),.96)
    red=material('escape_blood_water',(.105,.002,.010),.22,.15)
    horizon=material('escape_sun',(.95,.72,.43),.9,emission=1.6)
    geometry('escape_sea',[(-600,-600,-.22),(600,-600,-.22),(600,600,-.22),(-600,600,-.22)],[(0,1,2,3)],red)
    outline=[(-3.2,-4.7),(-1.6,-5.2),(2.2,-4.8),(3.1,-2.3),(3.3,.6),
             (2.1,2.9),(-1.9,3.1),(-3.6,.7),(-3.7,-2.8)]
    vertices=[(x,y,z) for z in [-1.1,-.025] for x,y in outline]
    n=len(outline)
    faces=[tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    geometry('escape_shore',vertices,faces,dark)
    for index,(x,y,sx,sy,sz) in enumerate([(-5,-3,1.8,2.5,.8),(5,0,1.2,1.7,.6),
            (-14,12,1.7,2.6,1.8),(8,18,2.2,3.5,2.8),(-26,34,3.1,5.4,4.2),
            (19,55,3.6,6.2,5.3),(-30,85,7,10,7),(36,120,6,9,9)]):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=(x,y,-.2))
        obj=bpy.context.object
        obj.name=f'escape_rock_{index}'
        obj.scale=(sx,sy,sz)
        obj.rotation_euler=(.05,.08,random.uniform(-.5,.5))
        obj.data.materials.append(dark)
    # A large low sun makes the destination readable without a textured sky.
    sun_center=Vector((-.5,150,18))
    disk=[sun_center+Vector((math.cos(i*math.tau/32)*10,0,math.sin(i*math.tau/32)*10)) for i in range(32)]
    geometry('escape_sun_disk',disk,[tuple(range(32))],horizon)
    bpy.ops.object.light_add(type=enum(bpy.ops.object.light_add,'type','SUN'),location=(0,150,25))
    sun=bpy.context.object
    sun.name='escape_horizon_light'
    sun.data.energy=1.8
    sun.data.color=(1,.73,.48)
    sun.data.angle=math.radians(6)
    aim(sun,(0,0,0))
    for name,position,color,power,size in [
            ('escape_ship_fill',(3,-9,12),(.52,.65,.75),2200,12),
            ('escape_warm_key',(-8,-1,15),(1,.76,.53),2600,10)]:
        bpy.ops.object.light_add(type=enum(bpy.ops.object.light_add,'type','AREA'),location=position)
        light=bpy.context.object
        light.name=name
        light.data.energy=power
        light.data.color=color
        light.data.shape=enum(light.data,'shape','DISK')
        light.data.size=size
        aim(light,(0,-.5,2))


def main():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'assets/blender/ship.blend'))
    scene=bpy.context.scene
    rig=bpy.data.objects['ship_rig']
    action=bpy.data.actions['takeoff']
    for obj in list(scene.objects):
        if obj != rig and obj.parent != rig:
            bpy.data.objects.remove(obj,do_unlink=True)
    rig.animation_data.action=action
    world()
    dress_scene()
    bpy.ops.object.camera_add(location=(9,-16,6))
    camera=bpy.context.object
    camera.name='escape_camera'
    camera.data.lens=42
    camera.data.clip_end=1800
    scene.camera=camera
    scene.render.engine=enum(scene.render,'engine','EEVEE')
    scene.render.resolution_x=1280
    scene.render.resolution_y=720
    scene.render.resolution_percentage=100
    scene.eevee.taa_render_samples=32
    scene.render.fps=FPS
    scene.render.image_settings.file_format=enum(scene.render.image_settings,'file_format','PNG')
    scene.view_settings.view_transform=enum(scene.view_settings,'view_transform','AgX')
    directory=ROOT/'assets/previews'
    directory.mkdir(parents=True,exist_ok=True)
    frames=directory/'.frames_ship_escape'
    frames.mkdir(exist_ok=True)
    first,last=int(action['frame_start']),int(action['frame_end'])
    duration=(last-first)/FPS

    def render_at(t,path):
        frame=first+(last-first)*t
        scene.frame_set(math.floor(frame),subframe=frame%1)
        travel=rig.pose.bones['root'].matrix.translation
        camera.location=Vector((9+t,-16-2*t,6+2*t))
        target=Vector((0,-.5,1.65))+travel*.65
        aim(camera,target)
        scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True)

    for label,t in [('start',0),('middle',.60),('end',1)]:
        render_at(t,frames/f'{label}.png')
    shutil.copyfile(frames/'middle.png',directory/'ship_escape.png')
    encoder=ffmpeg_path()
    subprocess.run([encoder,'-y','-loglevel','error','-i',str(frames/'start.png'),
                    '-i',str(frames/'middle.png'),'-i',str(frames/'end.png'),
                    '-filter_complex','[0:v][1:v][2:v]hstack=inputs=3',
                    '-frames:v','1',str(directory/'ship_escape_review.png')],check=True)
    if os.environ.get('ART_SKIP_VIDEO')!='1':
        count=round(duration*FPS)
        for index in range(count):
            render_at(index/(count-1),frames/f'{index:04d}.png')
        subprocess.run([encoder,'-y','-loglevel','error','-framerate',str(FPS),
                        '-i',str(frames/'%04d.png'),'-frames:v',str(count),'-c:v','libx264','-crf','18',
                        '-pix_fmt','yuv420p','-movflags','+faststart',
                        str(directory/'ship_escape.mp4')],check=True)
        shutil.rmtree(frames)
    manifest_path=ROOT/'Unity/Assets/Resources/Models/manifest.json'
    manifest=json.loads(manifest_path.read_text())
    entry=manifest['models']['ship']
    entry['files']['escape_preview']='assets/previews/ship_escape.png'
    entry['files']['escape_review']='assets/previews/ship_escape_review.png'
    if os.environ.get('ART_SKIP_VIDEO')!='1':
        entry['files']['escape_video']='assets/previews/ship_escape.mp4'
        entry['escape_recording']={'clip':'takeoff','duration_seconds':duration,'fps':FPS,
                                   'resolution':[1280,720],'playback_speed':1}
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    print('ESCAPE_RENDER',json.dumps({'duration_seconds':duration,'fps':FPS,
          'preview':str(directory/'ship_escape.png'),'video':os.environ.get('ART_SKIP_VIDEO')!='1'}),flush=True)


if __name__=='__main__':
    main()

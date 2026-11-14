"""Validate actual FBX transforms, takes, meshes and animated round trips.

Run after build_all.py. Does not require Unity and does not claim Unity runtime coverage.
"""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
import json
import math
import subprocess
from mathutils import Vector
from inspect_fbx import inspect
from common import ffmpeg_path, ROOT
EXPECTED={
 'player':{'idle','walk','run','death_martyr'},
 'beast_stage1':{'idle','stalk','chase','lunge'},
 'beast_stage2':{'idle','stalk','chase','lunge'},
 'beast_stage3':{'idle','stalk','chase','lunge'},
 'ship':{'idle','hatch_close','takeoff'},'console':set(),
 'rock_a':set(),'rock_b':set(),'ruin_pillar':set(),'ruin_arch':set(),
 'door_sealed':{'open'},'mural_slab':set(),'wreck_debris':set(),
 'terrain_shore':set(),'blood_sea':set(),
}


def coordinates(objects):
    bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get()
    coords=[]
    for obj in objects:
        evaluated=obj.evaluated_get(deps)
        data=evaluated.to_mesh()
        coords.extend(evaluated.matrix_world @ v.co for v in data.vertices)
        evaluated.to_mesh_clear()
    return coords


def bbox(coords):
    return [min(v[i] for v in coords) for i in range(3)]+[max(v[i] for v in coords) for i in range(3)]


def pose_samples(rig, objects, clips):
    samples={}
    for clip in clips:
        actions=[a for a in bpy.data.actions if a.name.split('|')[-1]==clip['name']]
        assert len(actions)==1, (clip['name'],[a.name for a in bpy.data.actions])
        rig.animation_data.action=actions[0]
        frames=sorted({1,clip['frame_end'],round((clip['frame_end']+1)/2),round((clip['frame_end']+3)/4)})
        samples[clip['name']]={}
        for frame in frames:
            bpy.context.scene.frame_set(frame)
            samples[clip['name']][frame]=bbox(coordinates(objects))
    return samples


def verify(name, entry):
    fbx=ROOT/entry['files']['fbx']
    for file in entry['files'].values():
        assert (ROOT/file).is_file(), (name,'missing deliverable',file)
    raw=inspect(fbx)
    assert set(raw['takes'])==EXPECTED[name], (name,raw['takes'])
    assert raw['settings']['UpAxis']==[1]
    assert raw['settings']['UpAxisSign']==[1]
    assert raw['settings']['UnitScaleFactor']==[100.0]
    for obj,props in raw['transforms'].items():
        for key,default in [('Lcl Translation',[0]*3),('Lcl Rotation',[0]*3),('Lcl Scaling',[1]*3)]:
            assert max(abs(a-b) for a,b in zip(props.get(key,default),default))<1e-5,(name,obj,key,props)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/entry['files']['blend']))
    source_objects=[bpy.data.objects[n] for n in entry['mesh_names']]
    for obj in source_objects:
        assert all(abs(v)<1e-6 for v in obj.location)
        assert all(abs(v-1)<1e-6 for v in obj.scale)
        assert all(not p.use_smooth for p in obj.data.polygons)
    source_rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    assert len(source_rigs)==int(entry['rigged'])
    samples=pose_samples(source_rigs[0],source_objects,entry['clips']) if source_rigs else {}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=True)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    assert len(rigs)==int(entry['rigged'])
    assert {o.name for o in meshes}==set(entry['mesh_names'])
    for rig in rigs:
        rig.animation_data.action=None
        for p in rig.pose.bones:
            p.matrix_basis.identity()
    bpy.context.scene.frame_set(1)
    # FBX importer restores Blender Z-up coordinates. Compare to the manifest's Y-up bounds.
    rest=coordinates(meshes)
    export_coords=[Vector((v.x,v.z,-v.y)) for v in rest]
    bounds=bbox(export_coords)
    wanted=entry['bounding_box']['min']+entry['bounding_box']['max']
    rest_error=max(abs(a-b) for a,b in zip(bounds,wanted))
    assert abs(bounds[1])<.0001,(name,'origin above or below base',bounds[1])
    assert rest_error<.0001,(name,'rest bounds',bounds,wanted)
    count=0
    for obj in meshes:
        obj.data.calc_loop_triangles()
        count+=len(obj.data.loop_triangles)
    assert count==entry['triangles'],(name,count,entry['triangles'])
    assert count <= 1500, (name,'triangle budget exceeded',count)
    if name=='player':
        assert abs(bounds[4]-bounds[1]-1.8)<.0001
    if name in {'terrain_shore','blood_sea'}:
        assert abs(bounds[3]-bounds[0]-20)<.0001
        assert abs(bounds[5]-bounds[2]-20)<.0001
    if name=='console':
        assert {'screen','tile_0','tile_1','tile_2','tile_3'} <= {o.name for o in meshes}
    if name in {'console','mural_slab'}:
        plane=bpy.data.objects['screen' if name=='console' else 'picture']
        assert len(plane.data.uv_layers)==1
    max_error=0
    if rigs:
        imported=pose_samples(rigs[0],meshes,entry['clips'])
        for clip,frames in samples.items():
            for frame,expected in frames.items():
                error=max(abs(a-b) for a,b in zip(expected,imported[clip][frame]))
                max_error=max(max_error,error)
                assert error<.002,(name,clip,frame,error,expected,imported[clip][frame])
    return {'status':'passed','triangles':count,'takes':raw['takes'],
            'identity_fbx_roots':True,'metres_per_unit':1,
            'rest_bounds_max_error_m':round(rest_error,7),
            'animated_bounds_max_error_m':round(max_error,7),
            'sampled_poses':sum(len(v) for v in samples.values())}


selected = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
manifest=json.loads((ROOT/'Unity/Assets/Resources/Models/manifest.json').read_text())
assert set(manifest['models']) <= set(EXPECTED)
if selected:
    manifest['models'] = {name:manifest['models'][name] for name in selected}
else:
    assert set(manifest['models']) == set(EXPECTED)
report={'scope':'Binary FBX inspection and Blender FBX reimport, not a Unity runtime test',
        'blender_version':bpy.app.version_string,'models':{}}
for name,entry in manifest['models'].items():
    report['models'][name]=verify(name,entry)
    print('VALIDATED',name,json.dumps(report['models'][name]),flush=True)
encoder=ffmpeg_path()
probe=str(Path(encoder).with_name('ffprobe'))
report['recordings']={}
for name,entry in manifest['models'].items():
    if not entry['rigged']:
        continue
    video=ROOT/entry['files']['video']
    info=json.loads(subprocess.check_output([probe,'-v','error','-show_streams','-show_format','-of','json',str(video)]))
    stream=next(s for s in info['streams'] if s['codec_type']=='video')
    assert stream['codec_name']=='h264' and stream['pix_fmt']=='yuv420p'
    expected_width = 1440 if entry.get('preview_segments',[{}])[0].get('playback_speed') == 1 else 480
    assert stream['width']==expected_width and stream['height']==480
    expected_duration = entry['preview_segments'][-1]['end_seconds']
    assert abs(float(info['format']['duration'])-expected_duration)<.05
    subprocess.run([encoder,'-v','error','-i',str(video),'-f','null','-'],check=True)
    report['recordings'][name]={'status':'decoded','codec':'h264','duration_seconds':float(info['format']['duration']),'resolution':[stream['width'],stream['height']]}
if 'ship' in manifest['models'] and 'escape_video' in manifest['models']['ship']['files']:
    entry=manifest['models']['ship']
    video=ROOT/entry['files']['escape_video']
    info=json.loads(subprocess.check_output([probe,'-v','error','-show_streams','-show_format','-of','json',str(video)]))
    stream=next(s for s in info['streams'] if s['codec_type']=='video')
    assert stream['codec_name']=='h264' and stream['pix_fmt']=='yuv420p'
    assert [stream['width'],stream['height']]==entry['escape_recording']['resolution']
    assert abs(float(info['format']['duration'])-entry['escape_recording']['duration_seconds'])<.05
    subprocess.run([encoder,'-v','error','-i',str(video),'-f','null','-'],check=True)
    report['recordings']['ship_escape']={'status':'decoded','codec':'h264',
        'duration_seconds':float(info['format']['duration']),'resolution':[stream['width'],stream['height']]}
report_path = ROOT/'assets/previews/validation.json'
if selected and report_path.exists():
    previous = json.loads(report_path.read_text())
    for key in ['models','recordings']:
        previous.setdefault(key,{}).update(report[key])
    report = previous
report_path.write_text(json.dumps(report,indent=2)+'\n')
print(f'VALIDATED {len(manifest["models"])} ASSETS',flush=True)

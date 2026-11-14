"""Render named actions from saved sources; no modelling or export changes."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *

names=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['player','beast_stage1','beast_stage2','beast_stage3','ship','door_sealed']
for name in names:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'assets/blender'/f'{name}.blend'))
    scene=bpy.context.scene
    rig=next(o for o in scene.objects if o.type=='ARMATURE')
    mesh_names=json.loads((ROOT/'Unity/Assets/Resources/Models/manifest.json').read_text())['models'][name]['mesh_names']
    PARTS[:]=[bpy.data.objects[n] for n in mesh_names]
    lo,hi=bounds()
    preview_video(name,rig,scene.camera,(lo+hi)/2,max(hi-lo))
    print('VIDEO_DONE',name,flush=True)

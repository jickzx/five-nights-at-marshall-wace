"""Heavy alien gate: two leaves withdraw into their frame."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('door_sealed')
    bones=[('root',(0,0,0),(0,0,.2),None)]
    for side,sign in [('L',1),('R',-1)]:
        bn='leaf.'+side
        bones.append((bn,(sign*.52,0,0),(sign*.52,0,.3),'root'))
        box('frame_post',(sign*1.30,0,1.4),(.42,.65,2.8),'iron','root',.07)
        box('frame_foot',(sign*1.30,0,.10),(.67,.85,.2),'rust','root',.04)
        box('door_leaf_'+side,(sign*.535,.03,1.3),(1.05,.27,2.6),'coal',bn,.065)
        box('raised_panel',(sign*.535,.19,1.32),(.72,.08,1.91),'rust',bn,.06)
        for i in range(3):
            box('crossbar',(sign*.535,.255,.55+i*.72),(.82,.08,.10),'iron',bn)
        box('seal_half',(sign*.08,.31,1.4),(.14,.08,.35),'bone',bn)
        box('warning',(sign*.15,.32,2.30),(.045,.04,.22),'eye',bn)
    box('lintel',(0,0,2.79),(3.04,.67,.28),'rust','root',.055)
    rig=skeleton('door_sealed',bones)
    def opening(r,t):
        amount=t*t*(3-2*t)
        for side,sign in [('L',1),('R',-1)]:
            p=r.pose.bones['leaf.'+side]
            p.location=p.bone.matrix_local.to_3x3().inverted() @ Vector((sign*1.15*amount,0,0))
    action(rig,'open',49,opening)
    deliver('door_sealed',rig,'open slides each leaf 1.15 m outwards. Hold the last pose. Allow space beside the frame for the leaves.')

if __name__=='__main__':
    build()

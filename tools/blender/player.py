"""The stranded pressure-suit astronaut. Exactly 1.8 metres in bind pose."""
from common import *


def build():
    reset('player')
    bones = [('root',(0,0,0),(0,0,.2),None),
             ('pelvis',(0,0,.88),(0,0,1.04),'root'),
             ('chest',(0,0,1.04),(0,0,1.4),'pelvis'),
             ('head',(0,0,1.44),(0,0,1.7),'chest')]
    box('pressure_torso',(0,0,1.22),(.48,.29,.45),'coal','chest',.055)
    box('chest_plate',(0,.17,1.24),(.35,.08,.28),'bone','chest',.025)
    box('chest_unit',(0,.221,1.23),(.17,.027,.13),'rust','chest')
    for x in [-.05,0,.05]:
        box('chest_lamp',(x,.24,1.26),(.024,.015,.018),'signal','chest')
    box('waist',(0,0,.96),(.35,.28,.18),'iron','pelvis',.025)
    box('belt',(0,.04,1.02),(.40,.30,.075),'rust','pelvis')
    box('oxygen_pack',(0,-.24,1.22),(.35,.22,.40),'rust','chest',.035)
    for x in [-.12,.12]:
        rod('tank',(x,-.28,1.05),(x,-.28,1.43),.075,.075,'bone','chest',8)
        rod('hose',(x,-.22,1.44),(x,.02,1.48),.035,.035,'iron','chest',6)
    ellipsoid('helmet',(0,0,1.6),(.235,.215,.2),'bone','head',8,4)
    box('visor',(0,.185,1.61),(.33,.09,.18),'visor','head',.025)
    box('visor_glint',(.09,.235,1.67),(.10,.005,.012),'signal','head')
    rod('collar',(0,0,1.42),(0,0,1.47),.16,.16,'iron','chest',8)
    for side, sign in [('L',1),('R',-1)]:
        shoulder=(sign*.29,0,1.37); elbow=(sign*.37,.01,1.10); wrist=(sign*.40,.03,.88)
        hip=(sign*.125,0,.91); knee=(sign*.14,.015,.49); ankle=(sign*.15,0,.13)
        bones += [('upper_arm.'+side,shoulder,elbow,'chest'),('forearm.'+side,elbow,wrist,'upper_arm.'+side),
                  ('thigh.'+side,hip,knee,'pelvis'),('shin.'+side,knee,ankle,'thigh.'+side)]
        rod('upper_arm',shoulder,elbow,.105,.09,'coal','upper_arm.'+side)
        box('shoulder_cap',(sign*.29,0,1.37),(.20,.28,.16),'bone','upper_arm.'+side,.025)
        rod('sleeve',elbow,wrist,.085,.065,'rust','forearm.'+side)
        box('glove',(sign*.40,.035,.86),(.13,.16,.13),'coal','forearm.'+side,.015)
        rod('thigh',hip,knee,.125,.10,'coal','thigh.'+side)
        box('thigh_plate',(sign*.14,.095,.72),(.17,.075,.22),'bone','thigh.'+side,.015)
        rod('shin',knee,ankle,.09,.075,'rust','shin.'+side)
        box('knee_guard',(sign*.14,.10,.49),(.18,.11,.15),'iron','shin.'+side,.025)
        box('boot',(sign*.15,.05,.09),(.19,.32,.18),'coal','shin.'+side,.025)
    rig=skeleton('player',bones)
    join_parts('player_mesh')
    humanoid_actions(rig)
    deliver('player',rig,'1.8 m bind height. In-place walk/run. death_martyr is a held kneeling surrender; clamp at its final frame.')

if __name__ == '__main__':
    build()

"""Shared anatomy across three silhouettes; each stage has its own build entry point."""
from common import *


def build_creature(stage):
    name=f'beast_stage{stage}'
    reset(name)
    h=[0,2.15,2.65,3.25][stage]
    width=[0,.26,.40,.53][stage]
    skin=['','coal','coal','flesh'][stage]
    hip=(0,-.06,h*.48); neck=(0,.08,h*.84)
    bones=[('root',(0,0,0),(0,0,.25),None),('pelvis',hip,(0,0,h*.57),'root'),
           ('chest',(0,0,h*.57),neck,'pelvis'),('head',neck,(0,.20,h*.96),'chest')]
    ico('narrow_pelvis',hip,(width*.72,.18,h*.10),skin,'pelvis')
    ico('ribcage',(0,-.015,h*.69),(width,.23,h*.18),skin,'chest',2)
    rod('spinal_column',(0,-.20,h*.49),(0,-.14,h*.86),.07,.065,'rust' if stage==1 else 'bone','chest')
    rod('neck',neck,(0,.18,h*.91),.085,.10,skin,'head')
    ico('skull',(0,.16,h*.93),(.16+stage*.025,.15+stage*.018,h*.07),skin,'head')
    for side,sign in [('L',1),('R',-1)]:
        sh=(sign*width*.94,0,h*.79); el=(sign*(width+.16),.04,h*.48); hand=(sign*(width+.27),.22,h*.23)
        knee=(sign*width*.60,.13,h*.25); foot=(sign*width*.65,.02,.10)
        thigh=(sign*width*.45,-.03,h*.48)
        bones += [('upper_arm.'+side,sh,el,'chest'),('forearm.'+side,el,hand,'upper_arm.'+side),
                  ('thigh.'+side,thigh,knee,'pelvis'),('shin.'+side,knee,foot,'thigh.'+side)]
        rod('humerus',sh,el,.075+stage*.015,.045,skin,'upper_arm.'+side)
        ico('elbow',el,(.08,.08,.09),'rust' if stage<3 else 'bone','forearm.'+side)
        rod('forearm',el,hand,.05,.065,skin,'forearm.'+side)
        rod('femur',thigh,knee,.09+stage*.012,.045,skin,'thigh.'+side)
        rod('shank',knee,foot,.045,.065,skin,'shin.'+side)
        for toe in [-1,1]:
            rod('splayed_foot',foot,(foot[0]+toe*.075,.32,0),.055,.014,'coal','shin.'+side)
        for digit in range(3):
            a=(hand[0]+sign*digit*.045,hand[1],hand[2])
            b=(a[0]+sign*.035,a[1]+.08,a[2]-.19-(digit%2)*.07)
            rod('finger',a,b,.028,.006,'bone' if stage>1 else 'coal','forearm.'+side,4)
        # The forked crown is the visual identity retained through every stage.
        a=(sign*.14,.08,h*.96); b=(sign*.28,-.015,h*1.035)
        rod('crown_branch',a,b,.048,.018,'coal' if stage<3 else 'bone','head',5)
        rod('crown_tip',b,(sign*.25,.04,h*1.10),.022,0,'coal' if stage<3 else 'bone','head',4)
        eyes=1 if stage<3 else 4
        for i in range(eyes):
            x=sign*(.072+.047*(i%2)); z=h*.95-(i//2)*.095
            ellipsoid('eye',(x,.306+stage*.018,z),(.031,.018,.027),'eye','head',4,3)
        if stage>=2:
            for i in range(5):
                z=h*.61+i*.10
                rib_x=width*(.75+.09*math.sin(i))
                rod('exposed_rib',(sign*.04,.205,z),(sign*rib_x,.15,z+.065),.024,.027,'bone','chest',5)
                rod('rib_hook',(sign*rib_x,.15,z+.065),(sign*width*.89,-.05,z+.11),.027,.016,'bone','chest',5)
            extra_a=(sign*width*.7,-.1,h*.65)
            extra_b=(sign*(width+.45),-.1,h*.45)
            bones += [('extra_arm.'+side,extra_a,extra_b,'chest')]
            rod('extra_upper',extra_a,extra_b,.065,.032,skin,'extra_arm.'+side)
            rod('extra_hook',extra_b,(sign*(width+.49),.25,h*.30),.04,.012,'bone','extra_arm.'+side)
        if stage==3:
            jaw_a=(sign*.06,.21,h*.91); jaw_b=(sign*.20,.37,h*.84)
            bones += [('jaw.'+side,jaw_a,jaw_b,'head')]
            rod('split_mandible',jaw_a,jaw_b,.075,.048,'flesh','jaw.'+side,5)
            for i in range(4):
                a=(sign*(.06+.035*i),.36,h*.905-i*.04)
                rod('tooth',a,(a[0]-sign*.025,a[1]+.015,a[2]+.075),.025,0,'ivory','jaw.'+side,4)
            for i in range(4):
                x=sign*(.10+i*.075); z=h*.59-i*.07
                rod('hanging_tendon',(x,.15,z),(x+sign*.025,.18,z-.30-i*.035),.035,.007,'viscera','chest',5)
                ellipsoid('blood_drop',(x+sign*.025,.18,z-.32-i*.035),(.029,.027,.055),'blood','chest',4,3)
            for i in range(3):
                rod('dorsal_spike',(sign*.18,-.15,h*(.69+i*.06)),
                    (sign*(.34+i*.06),-.38,h*(.74+i*.06)),.065,0,'bone','chest',4)
    if stage==3:
        ico('open_throat',(0,.29,h*.89),(.09,.035,.12),'viscera','head')
        ico('exposed_heart',(0,.245,h*.67),(.12,.08,.15),'blood','chest')
    ground()
    rig=skeleton(name,bones)
    join_parts(name+'_mesh')
    humanoid_actions(rig,True,stage)
    deliver(name,rig,'In-place stalk/chase; lunge contains 0.9 m forward root motion. Stages retain the forked crown and elongated hands.')

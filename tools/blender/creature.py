"""The Drowned Witness: the same fork-crowned scavenger losing its skin and gait."""
from common import *


def rotate(rig, name, angles):
    p = rig.pose.bones.get(name)
    if p is None:
        return
    from mathutils import Euler
    q = p.bone.matrix_local.to_quaternion()
    p.rotation_quaternion = q.inverted() @ Euler(angles, 'XYZ').to_quaternion() @ q


def ease(t):
    t = max(0, min(1, t))
    return t*t*(3-2*t)


def twitch(t, at, width=.025):
    d = abs(t-at)
    return max(0, 1-d/width)**2


def point_bone(rig, name, head, tail):
    bone = rig.pose.bones[name]
    rest = bone.bone
    turn = (rest.tail_local-rest.head_local).rotation_difference(Vector(tail)-Vector(head))
    bone.matrix = Matrix.Translation(head) @ turn.to_matrix().to_4x4() @ rest.matrix_local.to_3x3().to_4x4()
    bpy.context.view_layer.update()


def leg(rig, side, target, pole):
    upper = rig.pose.bones['thigh.'+side]
    rest = upper.bone
    hip = upper.parent.matrix @ upper.parent.bone.matrix_local.inverted() @ rest.head_local
    target = Vector(target)
    length_a = rest.length
    length_b = rig.pose.bones['shin.'+side].bone.length
    direction = target-hip
    distance = min(direction.length, (length_a+length_b)*.998)
    axis = direction.normalized()
    along = (length_a*length_a-length_b*length_b+distance*distance)/(2*distance)
    perpendicular = Vector(pole)-hip
    perpendicular = (perpendicular-axis*perpendicular.dot(axis)).normalized()
    knee = hip+axis*along+perpendicular*math.sqrt(max(0,length_a*length_a-along*along))
    ankle = hip+axis*distance
    point_bone(rig, 'thigh.'+side, hip, knee)
    point_bone(rig, 'shin.'+side, knee, ankle)
    point_bone(rig, 'foot.'+side, ankle, ankle+Vector((0,.3,0)))


def creature_actions(rig, stage, feet, height):
    def alive(r,t, intensity=1):
        breath = math.sin(t*math.tau)
        jerk = twitch(t,.28)-.7*twitch(t,.34)+.8*twitch(t,.76,.018)
        rotate(r,'chest',(-.025+.018*breath, .025*breath, -.025*breath))
        rotate(r,'head',(.035*breath+.10*jerk*intensity,.10*jerk*intensity,.08*breath+.19*jerk*intensity))
        for side,sign in [('L',1),('R',-1)]:
            rotate(r,'upper_arm.'+side,(.025*breath,-sign*.035,sign*.018*breath))
            rotate(r,'forearm.'+side,(sign*.04*breath,.02*jerk,0))
            rotate(r,'extra_arm.'+side,(.04*breath,sign*(.04+.06*breath),sign*.03*jerk))
            rotate(r,'extra_forearm.'+side,(.08*breath,0,sign*.05*jerk))
            rotate(r,'jaw.'+side,(.05+.03*breath,sign*(.10+.08*max(0,breath)+.12*jerk),0))
        for i in range(3):
            rotate(r,f'tendon.{i}',(.10*math.sin(t*math.tau+i),.06*math.sin(t*math.tau+i*2),0))
    action(rig,'idle',97,lambda r,t: alive(r,t,stage*.7))
    for clip,end,lift,travel,stance in [('stalk',73,.12,.48,.70),('chase',19,.43,.92,.43)]:
        fast = clip=='chase'
        def gait(r,t,fast=fast,lift=lift,travel=travel,stance=stance):
            alive(r,t,stage)
            wave = math.sin(t*math.tau)
            root_move(r,(.018*wave,0,((-.20 if stage == 1 else -.16) if fast else -.045)+(.045 if fast else .012)*math.sin(t*math.tau*2)))
            rotate(r,'pelvis',(0,.04*wave,.035*wave))
            rotate(r,'chest',(-.16 if fast else -.07,.06*wave if fast else .015*wave,.055*wave))
            snap = twitch(t,.22,.055)-twitch(t,.71,.04)
            rotate(r,'head',(.12 if fast else .055,.20*snap,.11*wave+.10*snap))
            for side,sign in [('L',1),('R',-1)]:
                phase=(t+(0 if sign==1 else .54))%1
                if phase<stance:
                    y=travel*(.5-phase/stance)
                    z=0
                else:
                    swing=(phase-stance)/(1-stance)
                    y=travel*(-.5+ease(swing))
                    z=lift*math.sin(math.pi*swing)**1.5
                target=feet[side]+Vector((sign*.018*wave,y,z))
                bpy.context.view_layer.update()
                leg(r,side,target,(sign*(.5 if fast else .28),-.45,height*.4))
                # One arm jerks forward while its opposite drags, with independent elbows.
                arm = (-.30+sign*.48*wave) if fast else sign*.13*wave
                rotate(r,'upper_arm.'+side,(arm,sign*(-.12 if fast else -.035),sign*.10*snap))
                rotate(r,'forearm.'+side,((.4 if sign==1 else -.12)+(.45 if fast else .08)*max(0,sign*wave),sign*.15*snap,0))
                rotate(r,'hand.'+side,(.4 if fast else 0,0,sign*.1*snap))
                rotate(r,'extra_arm.'+side,(-sign*.28*wave,sign*(.18 if fast else .04),sign*.13*snap))
                rotate(r,'extra_forearm.'+side,(.25*wave*sign,0,sign*.15*wave))
                rotate(r,'jaw.'+side,(.12,sign*(.25+.12*wave) if fast else sign*.13,0))
        action(rig,clip,end,gait)
    def lunge(r,t):
        crouch=ease(t/.34)
        strike=ease((t-.34)/.17)
        settle=ease((t-.63)/.24)
        root_move(r,(0,.9*strike,-.18*crouch+.24*strike-.06*settle))
        rotate(r,'chest',(.17*crouch-.66*strike+.12*settle,0,.03*math.sin(t*math.pi)))
        rotate(r,'head',(.04+.36*strike,0,.05*twitch(t,.47,.04)))
        for side,sign in [('L',1),('R',-1)]:
            rotate(r,'upper_arm.'+side,(-.28*crouch+2.02*strike-.18*settle,-sign*.18*strike,sign*.06*strike))
            rotate(r,'forearm.'+side,(.27*crouch-.38*strike,0,-sign*.12*strike))
            rotate(r,'extra_arm.'+side,(.5*strike,sign*.44*strike,-sign*.25*strike))
            rotate(r,'extra_forearm.'+side,(.65*strike,0,sign*.2*strike))
            rotate(r,'jaw.'+side,(.10+.35*strike,sign*(.10+.48*strike),0))
            target=feet[side]+Vector((sign*.08*strike,.9*ease((t-.48)/.35)+sign*.14*strike,.17*math.sin(math.pi*max(0,min(1,(t-.34)/.50)))))
            bpy.context.view_layer.update()
            leg(r,side,target,(sign*.35,-.4+.9*strike,height*.37))
        for i in range(3):
            rotate(r,f'tendon.{i}',(-.5*strike+.28*settle,.08*math.sin(t*8+i),0))
    action(rig,'lunge',37,lunge)


def shell(name, levels, material, bone, open_front=False):
    angles=[45,90,150,210,270,315] if open_front else [0,60,120,180,240,300]
    verts=[(math.sin(math.radians(a))*rx,cy+math.cos(math.radians(a))*ry,z)
           for z,rx,ry,cy in levels for a in angles]
    faces=[]
    count=len(angles)
    for j in range(len(levels)-1):
        for i in range(count-1 if open_front else count):
            k=(i+1)%count
            faces.append((j*count+i,j*count+k,(j+1)*count+k,(j+1)*count+i))
    if not open_front:
        faces.extend([tuple(reversed(range(count))),tuple((len(levels)-1)*count+i for i in range(count))])
    return mesh(name,verts,faces,material,bone)


def build_creature(stage):
    name=f'beast_stage{stage}'
    reset(name)
    height=[0,2.36,2.92,3.58][stage]
    h=height
    width=[0,.23,.39,.58][stage]
    skin='coal' if stage<3 else 'flesh'
    pelvis_z=h*.42
    shoulder_z=h*.73
    head_z=h*.83
    head_y=.26+stage*.06
    hip_width=.14+stage*.045
    feet={side:Vector((sign*(hip_width+.04),.12,.12)) for side,sign in [('L',1),('R',-1)]}
    bones=[('root',(0,0,0),(0,0,.2),None),
           ('pelvis',(0,-.09,pelvis_z),(0,-.06,pelvis_z+.2),'root'),
           ('chest',(0,-.06,pelvis_z+.2),(0,-.05,shoulder_z),'pelvis'),
           ('head',(0,.08,shoulder_z+.04),(0,head_y,head_z),'chest')]
    ico('pelvis',(0,-.10,pelvis_z),(.18+stage*.055,.16,.20),skin,'pelvis')
    shell('collapsed_thorax',[(pelvis_z+.1,width*.42,.11,-.09),
                            (h*.61,width,.25,-.06),(shoulder_z,width*.85,.20,-.10)],skin,'chest',stage>=2)
    rod('arched_neck',(0,-.1,shoulder_z-.10),(0,head_y-.10,head_z-.08),.13,.075,skin,'head',5)
    # A narrow, wedge-shaped mask and an unequal forked crown survive all three mutations.
    skull_w=.13+stage*.025
    skull=mesh('witness_mask',[
        (-skull_w,head_y-.13,head_z+.14),(skull_w,head_y-.13,head_z+.14),
        (-skull_w*.95,head_y+.14,head_z+.10),(skull_w*.95,head_y+.14,head_z+.10),
        (-skull_w*.60,head_y+.20,head_z-.14),(skull_w*.60,head_y+.20,head_z-.14),
        (0,head_y-.16,head_z-.15),(0,head_y+.19,head_z+.17)],
        [(0,1,7),(0,7,2),(1,3,7),(2,7,3),(2,3,5,4),(0,2,4,6),(1,6,5,3),(0,6,1),(4,5,6)],
        'iron' if stage<3 else 'bone','head')
    if stage==3:
        skull.data.materials.append(MATERIALS['blood'])
        for i in [4,5,7]:
            skull.data.polygons[i].material_index=1
    for j in range(5):
        z=pelvis_z+.15+j*(shoulder_z-pelvis_z)/5
        ico('vertebra',(0,-.25,z),(.065,.07,.05),'rust' if stage==1 else 'bone','chest')
    for side,sign in [('L',1),('R',-1)]:
        sh=Vector((sign*width,-.04,shoulder_z+(sign*.035)))
        el=Vector((sign*(width+.19),-.07,h*(.47 if sign==1 else .50)))
        hand=Vector((sign*(width+.28),.16,h*(.19 if sign==1 else .24)))
        hip=Vector((sign*hip_width,-.09,pelvis_z))
        knee=Vector((sign*(hip_width+.055),-.14,h*.23))
        ankle=feet[side]
        bones.extend([('upper_arm.'+side,sh,el,'chest'),('forearm.'+side,el,hand,'upper_arm.'+side),
                      ('hand.'+side,hand,hand+Vector((0,.07,-.15)),'forearm.'+side),
                      ('thigh.'+side,hip,knee,'pelvis'),('shin.'+side,knee,ankle,'thigh.'+side),
                      ('foot.'+side,ankle,ankle+Vector((0,.3,0)),'shin.'+side)])
        ico('uneven_shoulder',sh,(.12+stage*.025,.16,.14),skin,'upper_arm.'+side)
        rod('upper_arm',sh,el,.075+stage*.012,.036,skin,'upper_arm.'+side,5)
        rod('forearm',el,hand,.052,.035,skin,'forearm.'+side,5)
        ico('elbow',el,(.06,.07,.065),'iron' if stage<3 else 'bone','forearm.'+side)
        rod('thigh',hip,knee,.095,.045,skin,'thigh.'+side,5)
        rod('shin',knee,ankle,.04,.055,skin,'shin.'+side,5)
        for toe in [-1,1]:
            rod('heel',ankle,(ankle.x+toe*.07,.36,.04),.055,.027,'coal','foot.'+side,4)
            rod('toe',(ankle.x+toe*.07,.36,.04),(ankle.x+toe*.10,.47,.015),.027,0,'coal','foot.'+side,4)
        for digit in range(3):
            a=hand+Vector((sign*(digit-1)*.045,0,0))
            b=a+Vector((sign*.03,.12,-.14-(digit%2)*.04))
            c=b+Vector((-sign*.025,.11,-.11))
            rod('hook_finger',a,b,.024,.018,skin,'hand.'+side,4)
            rod('finger_tip',b,c,.018,0,'bone' if stage>1 else 'coal','hand.'+side,4)
        crown=[(sign*.11,head_y-.03,head_z+.12),
               (sign*(.30 if sign==1 else .28),head_y-.13,h*.95),
               (sign*(.21 if sign==1 else .39),head_y-.05,h if sign==1 else h*.985)]
        for j in range(2):
            rod('forked_crown',crown[j],crown[j+1],.048-j*.016,.022 if j==0 else 0,
                'coal' if stage==1 else 'bone','head',4)
        if stage>=2:
            for i in range(4):
                z=h*.55+i*h*.045
                a=(sign*.10,.24,z-.015);b=(sign*width*.93,.16,z+.065)
                rod('free_rib',a,b,.027,.035,'bone','chest',4)
                rod('rib_root',b,(sign*width*.87,-.13,z+.09),.035,.026,'bone','chest',4)
            ea=(sign*width*.85,-.20,h*.64)
            eb=(sign*(width+.43),-.13,h*.79)
            ec=(sign*(width+.65),.28,h*.43)
            bones.extend([('extra_arm.'+side,ea,eb,'chest'),('extra_forearm.'+side,eb,ec,'extra_arm.'+side)])
            rod('secondary_humerus',ea,eb,.09,.052,skin,'extra_arm.'+side,5)
            rod('secondary_ulna',eb,ec,.052,.023,skin,'extra_forearm.'+side,5)
            rod('secondary_talon',ec,(ec[0]-sign*.12,ec[1]+.20,ec[2]-.20),.04,0,'bone','extra_forearm.'+side,4)
            rod('shoulder_splinter',sh+Vector((0,-.08,.10)),sh+Vector((sign*.22,-.24,.34)),.075,0,'bone','chest',4)
        eye_count=1 if stage<3 else 4
        for i in range(eye_count):
            x=sign*(.075+(i%2)*.075)
            z=head_z+.035-(i//2)*.09
            y=head_y+.207+(i//2)*.018
            ellipsoid('eye_socket',(x,y-.008,z),(.049,.027,.036),'viscera' if stage==3 else 'coal','head',4,2)
            ellipsoid('watching_eye',(x,y+.015,z),(.029,.018,.017),'eye','head',4,2)
        if stage==3:
            ja=(sign*.11,head_y+.11,head_z-.08)
            jb=(sign*.26,head_y+.30,head_z-.40)
            bones.append(('jaw.'+side,ja,jb,'head'))
            rod('torn_mandible',ja,jb,.09,.045,'flesh','jaw.'+side,5)
            rod('mandible_bone',ja,(jb[0],jb[1]+.03,jb[2]-.03),.045,.022,'bone','jaw.'+side,4)
            for i in range(3):
                a=(sign*(.14+i*.045),head_y+.29,head_z-.13-i*.10)
                rod('inward_tooth',a,(a[0]-sign*.085,a[1]+.035,a[2]+.025),.025,0,'ivory','jaw.'+side,3)
            # Lifted skin edges expose dark cavities, rather than painting ribs on a solid torso.
            mesh('flayed_flap',[(sign*width,-.01,h*.72),(sign*width*.75,.19,h*.66),
                               (sign*width*.42,.18,h*.51),(sign*width*.88,.05,h*.45)],
                 [(0,1,2),(0,2,3)],'blood','chest')
            ellipsoid('collar_eye',(sign*.24,.14,h*.74),(.045,.022,.035),'eye','chest',4,2)
    if stage==3:
        ico('throat_void',(0,head_y+.16,head_z-.20),(.13,.08,.23),'viscera','head')
        ico('hanging_heart',(0,.13,h*.56),(.11,.11,.16),'blood','chest')
        for i in range(3):
            a=Vector((-.18+i*.17,.08,pelvis_z+.27))
            b=a+Vector((.07*math.sin(i*2),.07,-.46-.07*(i%2)))
            bone=f'tendon.{i}'
            bones.append((bone,a,b,'chest'))
            rod('viscera_string',a,b,.036,.016,'viscera',bone,4)
            end=b+Vector((-.05,.03,-.14))
            rod('wet_string',b,end,.016,.006,'blood',bone,4)
            ellipsoid('blood_drop',end,(.028,.022,.058),'blood',bone,4,2)
    ground()
    rig=skeleton(name,bones)
    join_parts(name+'_mesh')
    creature_actions(rig,stage,feet,h)
    deliver(name,rig,'Fork-crowned Drowned Witness. Idle twitches; planted stalking feet; asymmetric broken-limbed chase. Lunge includes 0.9 m forward root motion.')

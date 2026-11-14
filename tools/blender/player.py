"""Worn pressure suit, planted locomotion and a slow kneeling surrender."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def shell(name, rings, material, bone):
    """Octagonal sections keep broad readable faces without bevel modifiers."""
    verts = []
    for z, width, depth, center_y in rings:
        verts += [(x*width, y*depth+center_y, z) for x, y in
                  [(-1,-.64),(-.64,-1),(.64,-1),(1,-.64),
                   (1,.64),(.64,1),(-.64,1),(-1,.64)]]
    faces = [tuple(reversed(range(8)))]
    for r in range(len(rings)-1):
        faces += [(r*8+j,r*8+(j+1)%8,(r+1)*8+(j+1)%8,(r+1)*8+j)
                  for j in range(8)]
    faces += [tuple(range((len(rings)-1)*8,len(rings)*8))]
    return mesh(name,verts,faces,material,bone)


def sleeve(name, a, b, radii, material, bone, flatten=1):
    a, b = Vector(a), Vector(b)
    direction = (b-a).normalized()
    across = Vector((1,0,0))
    across = (across-direction*across.dot(direction)).normalized()
    front = direction.cross(across).normalized()
    verts = []
    for t, radius in radii:
        center = a.lerp(b,t)
        for j in range(6):
            angle = math.tau*j/6
            verts.append(center+across*math.cos(angle)*radius+
                         front*math.sin(angle)*radius*flatten)
    faces = [tuple(reversed(range(6)))]
    for r in range(len(radii)-1):
        faces += [(r*6+j,r*6+(j+1)%6,(r+1)*6+(j+1)%6,(r+1)*6+j)
                  for j in range(6)]
    faces += [tuple(range((len(radii)-1)*6,len(radii)*6))]
    return mesh(name,verts,faces,material,bone)


def patch(name, points, y, material, bone):
    return mesh(name,[(x,y,z) for x,z in points],
                [tuple(reversed(range(len(points))))],material,bone)


def rotate(rig, name, x=0, y=0, z=0):
    p = rig.pose.bones[name]
    rest = p.bone.matrix_local.to_quaternion()
    rotation = Quaternion((0,0,1),z) @ Quaternion((0,1,0),y) @ Quaternion((1,0,0),x)
    p.rotation_quaternion = rest.inverted() @ rotation @ rest


def point_bone(rig, name, head, tail, rotation=None):
    p = rig.pose.bones[name]
    rest = p.bone.matrix_local.to_quaternion()
    direction = p.bone.tail_local-p.bone.head_local
    turn = direction.rotation_difference(Vector(tail)-Vector(head)) if rotation is None else rotation
    p.matrix = Matrix.Translation(head) @ (turn @ rest).to_matrix().to_4x4()
    bpy.context.view_layer.update()


def plant_leg(rig, side, hip, ankle, pitch=0):
    """A two-link solution keeps sole contact independent of torso bob."""
    thigh = rig.data.bones['thigh.'+side]
    shin = rig.data.bones['shin.'+side]
    hip, ankle = Vector(hip), Vector(ankle)
    delta = ankle-hip
    distance = min(delta.length,thigh.length+shin.length)
    down = delta.normalized()
    forward = Vector((0,1,0))
    forward = (forward-down*forward.dot(down)).normalized()
    along = (thigh.length**2-shin.length**2+distance**2)/(2*distance)
    bend = math.sqrt(max(0,thigh.length**2-along**2))
    knee = hip+down*along+forward*bend
    point_bone(rig,'thigh.'+side,hip,knee)
    point_bone(rig,'shin.'+side,knee,ankle)
    point_bone(rig,'foot.'+side,ankle,ankle+Vector((0,.2,0)),Quaternion((1,0,0),pitch))


def smooth(value):
    value = max(0,min(1,value))
    return value*value*(3-2*value)


def motions(rig):
    model = PARTS[0]
    foot_points = {}
    for side in ['L','R']:
        group = model.vertex_groups['foot.'+side].index
        ankle = rig.data.bones['foot.'+side].head_local
        foot_points[side] = [v.co-ankle for v in model.data.vertices
                             if any(g.group==group for g in v.groups)]

    def sole_height(side,pitch):
        return -min(p.y*math.sin(pitch)+p.z*math.cos(pitch) for p in foot_points[side])

    def idle(r,t):
        breath = math.sin(t*math.tau)
        rotate(r,'chest',x=-.025+.012*breath,z=.009*breath)
        rotate(r,'head',x=-.035-.012*breath,z=.035*math.sin(t*math.tau))
        for side,sign in [('L',1),('R',-1)]:
            rotate(r,'upper_arm.'+side,x=.035,y=-sign*.025)
            rotate(r,'forearm.'+side,x=.07+.01*breath)
            rotate(r,'hand.'+side,x=-.04)
    action(rig,'idle',97,idle)

    for clip,end,stride,lift,stance in [('walk',33,.20,.10,.62),('run',19,.29,.24,.40)]:
        running = clip == 'run'
        def gait(r,t,running=running,stride=stride,lift=lift,stance=stance):
            wave = math.sin(t*math.tau)
            rise = math.sin(t*math.tau*2)**2
            drop = (-.075+.035*rise) if running else (-.035+.008*rise)
            sway = .010*wave if running else .015*wave
            root_move(r,(sway,0,drop))
            bpy.context.view_layer.update()
            rotate(r,'chest',x=-.16 if running else -.04,z=.055*wave)
            rotate(r,'head',x=.09 if running else .025,z=-.035*wave)
            for side,sign in [('L',1),('R',-1)]:
                u = (t+(0 if side=='L' else .5))%1
                if u < stance:
                    phase = u/stance
                    forward = stride*(1-2*phase)
                    pitch = .12*(1-smooth(phase/.18))-.28*smooth((phase-.75)/.25)
                    sole = sole_height(side,pitch)
                    height = sole
                else:
                    phase = (u-stance)/(1-stance)
                    forward = stride*(-1+2*smooth(phase))
                    pitch = -.28*(1-phase)+.12*phase
                    sole = sole_height(side,pitch)
                    height = sole+lift*math.sin(phase*math.pi)
                hip = (sign*.125+sway,0,.955+drop)
                ankle = (sign*.125,forward,height)
                plant_leg(r,side,hip,ankle,pitch)
                swing = -math.cos((t+(0 if side=='L' else .5))*math.tau)
                rotate(r,'upper_arm.'+side,x=swing*(.60 if running else .31),y=-sign*.035)
                rotate(r,'forearm.'+side,x=(.95 if running else .20)+swing*.10)
                rotate(r,'hand.'+side,x=-.10)
        action(rig,clip,end,gait)

    def martyr(r,t):
        seconds=t*7
        drop=smooth((seconds-.65)/2.35)
        opening=smooth((seconds-2.25)/2.50)
        looking=smooth((seconds-3.55)/2.05)
        bow=math.sin(math.pi*min(1,seconds/3.4))*.17
        root_move(r,(0,.015*drop,-.384*drop))
        bpy.context.view_layer.update()
        rotate(r,'chest',x=-bow+.065*looking)
        rotate(r,'head',x=-.15*math.sin(math.pi*min(1,seconds/3.6))+.49*looking)
        for side,sign in [('L',1),('R',-1)]:
            pitch=-2.05*drop
            height=sole_height(side,pitch)
            plant_leg(r,side,(sign*.125,.015*drop,.955-.384*drop),
                      (sign*.125,-.31*drop,height),pitch)
            rotate(r,'upper_arm.'+side,x=.08*opening,y=-sign*1.00*opening)
            rotate(r,'forearm.'+side,x=.10+.19*opening,y=-sign*.08*opening)
            rotate(r,'hand.'+side,x=-.08+.46*opening,z=sign*.12*opening)
    action(rig,'death_martyr',169,martyr)


def build():
    reset('player')
    bones=[('root',(0,0,0),(0,0,.2),None),
           ('pelvis',(0,0,.955),(0,0,1.075),'root'),
           ('chest',(0,0,1.075),(0,0,1.43),'pelvis'),
           ('head',(0,0,1.48),(0,0,1.70),'chest')]
    shell('pressure_torso',[(1.035,.165,.125,0),(1.12,.19,.15,0),
                           (1.37,.235,.15,0),(1.46,.16,.115,0)],'coal','chest')
    shell('pelvic_seal',[(.88,.16,.115,0),(.96,.19,.14,0),(1.065,.175,.13,0)],'rust','pelvis')
    box('waist_webbing',(0,.005,1.045),(.365,.285,.045),'iron','pelvis')
    box('belt_clasp',(.045,.155,1.045),(.075,.035,.055),'bone','pelvis')
    patch('ceramic_breastplate',[(-.17,1.12),(.155,1.12),(.20,1.32),(.13,1.40),
                                (-.14,1.40),(-.20,1.31)],.157,'bone','chest')
    patch('chest_plate_rust',[(-.17,1.12),(-.125,1.145),(-.142,1.20),(-.175,1.217)],.159,'rust','chest')
    patch('chest_blood_drag',[(.12,1.13),(.139,1.13),(.16,1.205),(.17,1.21),
                              (.176,1.285),(.159,1.275)],.1595,'viscera','chest')
    box('chest_air_unit',(-.015,.179,1.225),(.18,.05,.105),'iron','chest')
    box('air_unit_face',(-.015,.207,1.23),(.143,.008,.062),'rust','chest')
    box('oxygen_warning',(-.062,.214,1.246),(.025,.006,.013),'signal','chest')
    for x in [-.005,.025,.055]:
        box('regulator_slot',(x,.213,1.235),(.012,.005,.035),'coal','chest')
    for sign in [-1,1]:
        rod('harness',(sign*.15,.167,1.10),(sign*.175,.16,1.38),.018,.018,'rust','chest',4)
    box('mission_patch',(-.09,.166,1.34),(.071,.008,.042),'rust','chest')
    box('patch_mark',(-.09,.173,1.34),(.013,.008,.027),'ivory','chest')
    shell('pack_frame',[(1.02,.13,.075,-.19),(1.39,.15,.075,-.19),
                        (1.43,.11,.06,-.19)],'iron','chest')
    rod('oxygen_bottle',(.025,-.29,1.08),(.025,-.29,1.39),.105,.105,'bone','chest',8)
    rod('bottle_head',(.025,-.29,1.39),(.025,-.29,1.435),.105,.04,'rust','chest',8)
    box('tank_strap',(.025,-.29,1.18),(.225,.205,.043),'rust','chest')
    box('tank_lower_guard',(.025,-.29,1.065),(.26,.23,.055),'coal','chest')
    rod('air_hose',(-.12,-.25,1.40),(-.195,-.04,1.45),.024,.024,'rust','chest',6)
    rod('air_hose',(-.195,-.04,1.45),(-.155,.12,1.38),.024,.024,'rust','chest',6)
    rod('neck_seal',(0,0,1.44),(0,0,1.51),.123,.123,'iron','chest',8)
    shell('helmet_shell',[(1.505,.135,.135,0),(1.57,.17,.156,0),
                         (1.72,.175,.155,0),(1.80,.125,.105,-.018)],'bone','head')
    patch('visor_frame',[(-.105,1.555),(.105,1.555),(.143,1.595),(.143,1.715),
                         (.105,1.742),(-.105,1.742),(-.143,1.715),(-.143,1.595)],.158,'iron','head')
    patch('opaque_visor',[(-.098,1.574),(.098,1.574),(.131,1.605),(.131,1.701),
                          (.097,1.725),(-.097,1.725),(-.131,1.701),(-.131,1.605)],.162,'visor','head')
    patch('visor_scuff',[(-.097,1.703),(-.080,1.695),(-.039,1.709),(-.083,1.712)],.163,'bone','head')
    patch('helmet_scar',[(.074,1.768),(.113,1.741),(.124,1.746),(.092,1.784)],.131,'rust','head')
    box('helmet_receiver',(-.171,-.005,1.65),(.038,.13,.12),'rust','head')
    box('receiver_light',(-.192,.036,1.665),(.009,.027,.015),'signal','head')
    for side,sign in [('L',1),('R',-1)]:
        upper='upper_arm.'+side; fore='forearm.'+side; hand='hand.'+side
        thigh='thigh.'+side; shin='shin.'+side; foot='foot.'+side
        shoulder=(sign*.255,0,1.40); elbow=(sign*.325,0,1.14)
        wrist=(sign*.35,.015,.94); fingertips=(sign*.36,.025,.845)
        hip=(sign*.125,0,.955); knee=(sign*.125,0,.525); ankle=(sign*.125,0,.145)
        bones += [(upper,shoulder,elbow,'chest'),(fore,elbow,wrist,upper),
                  (hand,wrist,fingertips,fore),(thigh,hip,knee,'pelvis'),
                  (shin,knee,ankle,thigh),(foot,ankle,(sign*.125,.20,.145),shin)]
        sleeve('upper_sleeve',shoulder,elbow,[(0,.105),(.2,.115),(.75,.086),(1,.072)],'rust',upper,.9)
        sleeve('shoulder_plate',shoulder,elbow,[(-.04,.114),(.24,.121),(.43,.098)],
               'bone' if side=='L' else 'iron',upper,1.0)
        sleeve('forearm_sleeve',elbow,wrist,[(0,.072),(.27,.088),(.82,.068),(1,.055)],'rust',fore)
        sleeve('wrist_seal',elbow,wrist,[(.83,.073),(1,.066)],'iron',fore)
        sleeve('glove',wrist,fingertips,[(0,.055),(.6,.065),(1,.042)],'coal',hand,.75)
        rod('thumb',(sign*.323,.041,.91),(sign*.312,.06,.868),.025,.018,'coal',hand,4)
        if side=='L':
            box('wrist_instrument',(sign*.343,.075,1.005),(.09,.026,.065),'iron',fore)
            box('wrist_display',(sign*.343,.09,1.009),(.048,.008,.028),'screen',fore)
        sleeve('thigh_fabric',hip,knee,[(0,.116),(.26,.13),(.78,.10),(1,.09)],'coal',thigh)
        patch('thigh_armour',[(sign*.125-.075,.63),(sign*.125+.075,.63),
                              (sign*.125+.087,.83),(sign*.125+.06,.88),
                              (sign*.125-.07,.86)],.103,'bone' if side=='L' else 'rust',thigh)
        patch('thigh_scuff',[(sign*.125-.055,.66),(sign*.125-.03,.655),
                             (sign*.125+.04,.71),(sign*.125+.025,.72)],.105,'rust' if side=='L' else 'iron',thigh)
        sleeve('shin_fabric',knee,ankle,[(0,.092),(.25,.101),(.8,.075),(1,.075)],'rust',shin)
        box('knee_pad',(sign*.125,.078,.525),(.16,.105,.125),'iron',shin)
        box('knee_inset',(sign*.125,.134,.525),(.105,.012,.068),'bone',shin)
        box('shin_seam',(sign*.125,.086,.34),(.02,.016,.16),'coal',shin)
        boot=shell('boot',[(0,.10,.17,.065),(.07,.10,.17,.065),(.18,.08,.11,.025)],'coal',foot)
        for vertex in boot.data.vertices:
            vertex.co.x += sign*.125
        box('boot_sole',(sign*.125,.065,.017),(.205,.34,.034),'iron',foot)
        box('boot_toecap',(sign*.125,.182,.073),(.174,.105,.085),'rust',foot)
    rig=skeleton('player',bones)
    join_parts('player_mesh')
    motions(rig)
    deliver('player',rig,'1.8 m pressure suit. Foot-planted in-place walk/run. death_martyr lasts 7 s: knees settle, arms open, head lifts, then a held final pose. Clamp at its final frame.')

if __name__=='__main__':
    build()

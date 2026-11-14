"""Faceted salvage shuttle with an open ramp, folding gear and staged escape."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def ease(t, start=0, end=1):
    u = max(0, min(1, (t-start)/(end-start)))
    return u*u*(3-2*u)


def warm_material():
    mat = MATERIALS['signal'].copy()
    mat.name = 'escape_amber'
    color = (1, .39, .09, 1)
    mat.diffuse_color = color
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = color
    shader.inputs['Emission Color'].default_value = color
    shader.inputs['Emission Strength'].default_value = 4
    MATERIALS['amber'] = mat


def hull_panel(name, points, material, bone='root'):
    return mesh(name, points, [tuple(range(len(points)))], material, bone)


def nozzle(name, center, axis, outer, inner, material='iron'):
    c = Vector(center)
    n = Vector(axis).normalized()
    u = Vector((1, 0, 0))
    v = n.cross(u)
    vertices = []
    for radius, depth in [(outer, 0), (inner, 0), (inner*.8, -.12), (outer*.98, -.20)]:
        vertices.extend(c+n*depth+(u*math.cos(i*math.tau/8)+v*math.sin(i*math.tau/8))*radius
                        for i in range(8))
    faces = []
    for i in range(8):
        j = (i+1)%8
        faces.extend([(i,j,8+j,8+i),(8+i,8+j,16+j,16+i),(i,24+i,24+j,j)])
    mesh(name, vertices, faces, material, 'root')
    rod(name+'_core', c-n*.13, c-n*.115, inner*.68, inner*.68, 'amber', 'root', 8)


def plume(name, at, direction, bone, length=.38):
    c, d = Vector(at), Vector(direction).normalized()
    u = Vector((1,0,0))
    v = d.cross(u)
    vertices = [c+(u*math.cos(i*math.tau/6)+v*math.sin(i*math.tau/6))*.105 for i in range(6)]
    vertices += [c+d*(length*(.58 if i%2 else .82))+(u*math.cos(i*math.tau/6)+v*math.sin(i*math.tau/6))*.045 for i in range(6)]
    vertices += [c+d*length]
    faces = [(i,(i+1)%6,6+(i+1)%6,6+i) for i in range(6)]
    faces += [(6+i,6+(i+1)%6,12) for i in range(6)]
    mesh(name, vertices, faces, 'amber', bone)


def build():
    reset('ship')
    warm_material()
    # A broad armored belly narrows to a blunt prow. The cabin sits inside it.
    sections = [(-2.35,.83,.69,1.11),(-1.65,1.12,.52,1.0),
                (1.25,1.10,.57,1.11),(2.55,.43,.91,1.23),(2.9,.12,1.10,1.27)]
    verts = []
    for y,w,low,high in sections:
        verts += [(-w*.68,y,low),(w*.68,y,low),(w,y,high),(-w,y,high)]
    faces = [(0,3,2,1)]
    for ring in range(len(sections)-1):
        for side in range(4):
            a=ring*4+side; b=ring*4+(side+1)%4
            faces.append((a,b,b+4,a+4))
    faces.append((16,17,18,19))
    mesh('armored_keel',verts,faces,'rust','root')
    box('cabin_deck',(0,-.15,1.02),(2.03,3.75,.12),'iron','root')
    hull_panel('prow_armor',[(-1.10,1.25,1.12),(-.43,2.55,1.24),(-.12,2.9,1.28),
                           (.12,2.9,1.28),(.43,2.55,1.24),(1.10,1.25,1.12)],'bone')
    hull_panel('prow_spine',[(-.15,1.27,1.14),(-.10,2.89,1.30),(.10,2.89,1.30),(.15,1.27,1.14)],'coal')
    hull_panel('port_cabin',[(-1.09,-1.85,1.06),(-1.09,1.25,1.12),(-.85,.56,3.10),(-.87,-1.57,3.10)],'bone')
    hull_panel('cabin_back',[(-1.09,-1.85,1.06),(-.87,-1.57,3.10),(.87,-1.57,3.10),(1.09,-1.85,1.06)],'rust')
    hull_panel('roof_port',[(-.87,-1.57,3.10),(-.85,.56,3.10),(0,.71,3.29),(0,-1.72,3.29)],'iron')
    hull_panel('roof_starboard',[(0,-1.72,3.29),(0,.71,3.29),(.85,.56,3.10),(.87,-1.57,3.10)],'coal')
    # The side doorway remains open and is physically connected to the ramp.
    hull_panel('entry_rear_frame',[(1.09,-1.85,1.06),(.87,-1.57,3.10),(1.03,-1.42,3.02),(1.11,-1.42,1.06)],'bone')
    hull_panel('entry_top_frame',[(1.03,-1.42,3.02),(.87,-1.57,3.10),(.85,.56,3.10),(1.03,.16,3.02)],'bone')
    hull_panel('starboard_cheek',[(1.11,.16,1.06),(1.03,.16,3.02),(.85,.56,3.10),(1.10,1.25,1.12)],'bone')
    for x,s in [(-1,-1),(1,1)]:
        hull_panel('windscreen_'+str(s),[(s*.045,.72,3.25),(s*.82,.58,3.10),
                    (s*1.04,1.64,1.54),(s*.045,1.83,1.57)],'visor')
        rod('canopy_edge',(s*.83,.58,3.11),(s*1.05,1.65,1.53),.035,.035,'iron','root',4)
        rod('eyebrow_light',(s*.19,.70,3.25),(s*.65,.62,3.15),.014,.014,'signal','root',4)
        hull_panel('cockpit_chin',[(s*.035,1.84,1.56),(s*1.05,1.65,1.53),(s*1.10,1.25,1.12),(s*.035,2.45,1.25)],'rust')
    rod('windscreen_mullion',(0,.73,3.27),(0,1.85,1.55),.03,.03,'iron','root',4)
    box('pilot_seat',(0,-.04,1.29),(.58,.55,.15),'coal','root')
    box('pilot_seat_back',(0,-.34,1.60),(.58,.13,.69),'coal','root',.035)
    box('seat_restraint',(-.17,-.25,1.61),(.075,.045,.52),'rust','root')
    box('cockpit_dashboard',(0,.87,1.53),(1.54,.48,.18),'iron','root')
    hull_panel('cockpit_blank_screen',[(-.34,.68,1.63),(.34,.68,1.63),(.34,.98,1.82),(-.34,.98,1.82)],'screen')
    for x in [-.56,-.43,.43,.56]:
        box('cockpit_key',(x,.71,1.64),(.08,.13,.025),'signal','root')
    rod('control_grip',(.40,.47,1.41),(.40,.44,1.67),.025,.025,'coal','root',5)
    box('entry_sill',(1.11,-.63,1.03),(.16,1.57,.12),'iron','root')
    rod('entry_beacon',(1.05,-1.40,2.90),(1.05,-1.40,2.67),.025,.025,'amber','root',4)
    # The hatch matches the doorway height and tilts inward as it seats.
    hx,hz=1.12,1.04
    doorway_top=3.02
    ez=.12
    hatch_height=doorway_top-hz
    ex=hx+math.sqrt(hatch_height*hatch_height+.09*.09-(hz-ez)**2)
    mesh('hatch',[(hx,-1.40,hz),(ex,-1.40,ez),(ex,.14,ez),(hx,.14,hz),
                  (hx,-1.40,hz+.07),(ex,-1.40,ez+.07),(ex,.14,ez+.07),(hx,.14,hz+.07)],
         [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'rust','hatch')
    for i in range(1,6):
        x=hx+(ex-hx)*i/6; z=hz+(ez-hz)*i/6+.09
        rod('ramp_tread',(x,-1.29,z),(x,.03,z),.018,.018,'bone','hatch',4)
    mark=[]
    for t,y in [(.15,-.90),(.82,-.90),(.86,-.69),(.15,-.69)]:
        mark.append((hx+(ex-hx)*t,y,hz+(ez-hz)*t-.012))
    hull_panel('hatch_rescue_mark',mark,'bone','hatch')
    bones=[('root',(0,0,0),(0,0,.3),None),
           ('hatch',(hx,-1.40,hz),(hx,.14,hz),'root')]
    for side,sign in [('L',1),('R',-1)]:
        x=sign*1.63
        mesh('engine_pylon_'+side,[(sign*.88,-2.10,1.12),(sign*2.03,-3.00,.99),
             (sign*2.12,-1.95,1.04),(sign*.99,-1.48,1.18),
             (sign*.88,-2.10,1.29),(sign*2.03,-3.00,1.16),
             (sign*2.12,-1.95,1.21),(sign*.99,-1.48,1.35)],
             [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'rust','root')
        rod('engine_'+side,(x,-3.39,1.15),(x,-1.68,1.15),.41,.46,'iron','root',8)
        rod('engine_nose_'+side,(x,-1.68,1.15),(x,-1.40,1.15),.46,.21,'bone','root',8)
        rod('engine_belt_'+side,(x,-2.74,1.15),(x,-2.59,1.15),.43,.43,'rust','root',8)
        rod('engine_rear_'+side,(x,-3.39,1.15),(x,-3.72,1.15),.41,.34,'coal','root',8)
        nozzle('engine_nozzle_'+side,(x,-3.92,1.15),(0,-1,0),.34,.25)
        jet='jet.'+side
        bones.append((jet,(x,-3.93,1.15),(x,-4.31,1.15),'root'))
        plume('exhaust_'+side,(x,-3.93,1.15),(0,-1,0),jet)
        nozzle('lift_nozzle_'+side,(sign*.75,-.70,.34),(0,0,-1),.18,.125)
        lift='lift.'+side
        bones.append((lift,(sign*.75,-.70,.32),(sign*.75,-.70,.08),'root'))
        plume('lift_exhaust_'+side,(sign*.75,-.70,.32),(0,0,-1),lift,.24)
        gear='gear.'+side
        bones.append((gear,(sign*.88,-1.40,.72),(sign*1.27,-1.40,.12),'root'))
        rod('landing_piston_'+side,(sign*.88,-1.40,.72),(sign*1.27,-1.40,.12),.065,.045,'iron',gear,6)
        rod('landing_brace_'+side,(sign*.69,-1.76,.65),(sign*1.27,-1.40,.17),.035,.035,'rust',gear,5)
        box('landing_pad_'+side,(sign*1.27,-1.40,.06),(.47,.62,.12),'coal',gear)
        # Small swept stabilizers and an offset tail make the silhouette unmistakable.
        mesh('tail_fin_'+side,[(sign*.69,-1.41,2.92),(sign*.82,-2.01,2.57),
                              (sign*1.10,-2.14,3.47),(sign*.72,-1.60,3.27),
                              (sign*.77,-1.41,2.92),(sign*.90,-2.01,2.57),
                              (sign*1.18,-2.14,3.47),(sign*.80,-1.60,3.27)],
             [(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'rust','root')
        box('navigation_lamp_'+side,(sign*2.08,-2.22,1.25),(.09,.17,.09),'signal' if sign==1 else 'eye','root')
        hull_panel('lower_hull_scar_'+side,[(sign*1.123,-.78,.80),(sign*1.123,-.36,.91),
                                         (sign*1.123,-.05,.84),(sign*1.123,-.44,.76)],'coal')
    bones.append(('gear.front',(0,1.10,.72),(0,1.49,.13),'root'))
    rod('front_landing_piston',(0,1.10,.72),(0,1.49,.13),.065,.05,'iron','gear.front',6)
    box('front_landing_pad',(0,1.49,.06),(.48,.64,.12),'coal','gear.front')
    # Paint repairs, a broken aerial and shallow cuts stay geometric at PS1 resolution.
    hull_panel('port_repair_plate',[(-1.095,-1.14,1.15),(-1.095,-.66,1.18),(-1.00,-.68,2.00),(-1.006,-1.09,1.94)],'rust')
    hull_panel('port_worn_stripe',[(-1.04,.17,1.59),(-1.035,.43,1.65),(-.972,.15,2.22),(-.982,-.08,2.18)],'ivory')
    hull_panel('roof_rescue_stripe',[(-.12,-1.58,3.297),(-.12,.42,3.297),(.12,.42,3.297),(.12,-1.58,3.297)],'bone')
    hull_panel('starboard_paint_chip',[(1.085,.28,1.31),(1.055,.51,1.55),(1.02,.45,1.86),(1.065,.27,1.68)],'rust')
    def aft_y(z):
        return -1.85+(z-1.06)*.28/2.04-.016
    hull_panel('aft_vent',[(-.48,aft_y(1.48),1.48),(.48,aft_y(1.48),1.48),
                           (.35,aft_y(2.50),2.50),(-.35,aft_y(2.50),2.50)],'coal')
    for z,w in [(1.72,.44),(1.99,.40),(2.26,.36)]:
        rod('aft_vent_louver',(-w,aft_y(z)-.015,z),(w,aft_y(z)-.015,z),.022,.022,'iron','root',4)
    rod('broken_aerial',(-.51,-1.29,3.21),(-.55,-1.38,3.58),.02,.013,'iron','root',5)
    rod('bent_aerial_tip',(-.55,-1.38,3.58),(-.69,-1.45,3.65),.013,.005,'iron','root',5)
    rig=skeleton('ship',bones)
    close_angle=-(math.atan2(hatch_height,-.09)+math.atan2(hz-ez,ex-hx))
    def jets(r,strength,t):
        for side,phase in [('L',0),('R',.9)]:
            flicker=1+.13*math.sin(t*math.tau*31+phase)+.07*math.sin(t*math.tau*47)
            r.pose.bones['jet.'+side].scale=(strength*.8,strength*2.0*flicker,strength*.8)
            r.pose.bones['lift.'+side].scale=(strength*.7,strength*1.5*flicker,strength*.7)
    def idle(r,t):
        jets(r,.001,t)
    action(rig,'idle',73,idle)
    def close(r,t):
        jets(r,.001,t)
        # A small deliberate pause before the heavy hatch pulls in and seats.
        world_rotate(r,'hatch',(0,1,0),close_angle*ease(t,.12,.94))
    action(rig,'hatch_close',73,close)
    def takeoff(r,t):
        world_rotate(r,'hatch',(0,1,0),close_angle)
        ignition=ease(t,.04,.22)
        lift=ease(t,.18,.56)
        departure=max(0,(t-.48)/.52)
        tremor=ignition*(1-ease(t,.18,.36))
        root_move(r,(.012*math.sin(t*math.tau*28)*tremor+1.6*departure**2,
                     1.2*lift+12.0*departure**2,
                     .008*math.sin(t*math.tau*34)*tremor+1.85*lift+10.5*departure**2))
        pitch=.035*lift+.34*ease(t,.52,.95)
        bank=-.16*math.sin(ease(t,.48,1)*math.pi)
        root=r.pose.bones['root']
        inverse=root.bone.matrix_local.to_quaternion().inverted()
        root.rotation_quaternion=(Quaternion(inverse@Vector((1,0,0)),pitch) @
                                  Quaternion(inverse@Vector((0,1,0)),bank))
        fold=ease(t,.35,.57)
        world_rotate(r,'gear.L',(0,1,0),2.2*fold)
        world_rotate(r,'gear.R',(0,1,0),-2.2*fold)
        world_rotate(r,'gear.front',(1,0,0),-2.2*fold)
        jets(r,.001+.6*ignition+.4*ease(t,.48,.75),t)
    action(rig,'takeoff',217,takeoff)
    deliver('ship',rig,'Faceted salvage shuttle with a side boarding ramp, cockpit, twin rear engines and three folding landing feet. The side entry is 1.58 m wide with at least 1.91 m vertical clearance over the ramp and sill. Hatch starts open. hatch_close lasts 3 seconds. takeoff lasts 9 seconds: ignition tremor, gentle lift, gear retraction, bank and accelerating ascent. Warm exhaust meshes animate with the rig. Forward is -Z in Unity; takeoff ends at (1.6,12.35,-13.2) m relative to its starting root.')


if __name__=='__main__':
    build()

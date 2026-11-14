"""Pressure-sealed alien gate with a delayed mechanical unseal and heavy slide."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def slab(name, outline, front, back, material, bone):
    n=len(outline)
    verts=[(x,y,z) for y in [back,front] for x,z in outline]
    faces=[tuple(range(n)),tuple(reversed(range(n,2*n)))]
    faces.extend([(i,n+i,n+(i+1)%n,(i+1)%n) for i in range(n)])
    return mesh(name,verts,faces,material,bone)


def build():
    reset('door_sealed')
    bones=[('root',(0,0,0),(0,0,.2),None)]
    for side,sign in [('L',1),('R',-1)]:
        bn='leaf.'+side
        bones.append((bn,(sign*.54,0,0),(sign*.54,0,.3),'root'))
        post=[(sign*x,z) for x,z in [(1.12,0),(1.64,0),(1.64,2.66),(1.49,3.02),(1.12,3.02)]]
        if sign<0:
            post.reverse()
        slab('frame_post_'+side,post,.38,-.35,'iron','root')
        box('frame_foot',(sign*1.38,.025,.10),(.72,.94,.2),'viscera','root',.045)
        box('frame_cheek',(sign*1.39,.407,1.43),(.27,.065,2.32),'rust','root',.045)
        box('frame_inner_seal',(sign*1.095,.19,1.40),(.055,.065,2.63),'coal','root')
        outline=[(sign*x,z) for x,z in [(.015,.12),(1.10,.12),(1.10,2.58),(.97,2.74),(.015,2.74)]]
        if sign<0:
            outline.reverse()
        slab('door_leaf_'+side,outline,.12,-.14,'coal',bn)
        panel=[(sign*x,z) for x,z in [(.18,.34),(.88,.34),(.96,.56),(.96,2.36),(.82,2.50),(.18,2.50)]]
        if sign<0:
            panel.reverse()
        slab('stamped_panel_'+side,panel,.162,.115,'rust',bn)
        rib=[(sign*x,z) for x,z in [(.29,.48),(.43,.48),(.78,1.10),(.78,1.72),(.43,2.32),(.29,2.32),(.61,1.66),(.61,1.16)]]
        if sign<0:
            rib.reverse()
        slab('pressed_chevron_'+side,rib,.21,.16,'iron',bn)
        box('central_seal',(sign*.061,.18,1.40),(.055,.075,2.49),'iron',bn,.01)
        for z in [.45,2.37]:
            box('hinge_lock',(sign*.97,.21,z),(.16,.12,.15),'iron',bn,.015)
            rod('lock_pin',(sign*.97,.265,z),(sign*.97,.29,z),.026,.026,'bone',bn,6)
        for z in [.66,.86,1.06]:
            box('vent',(sign*.27,.174,z),(.10,.015,.024),'coal',bn)
        medallion=[(sign*x,z) for x,z in [(.015,1.10),(.23,1.25),(.30,1.47),(.13,1.68),(.015,1.68)]]
        if sign<0:
            medallion.reverse()
        slab('split_seal_'+side,medallion,.258,.18,'bone',bn)
        rod('seal_cut',(sign*.045,.267,1.25),(sign*.18,.267,1.47),.016,.016,'coal',bn,4)
        box('warning',(sign*1.39,.449,2.45),(.045,.025,.19),'eye','root',.005)
        for z in [.72,1.09,1.93,2.15]:
            rod('frame_bolt',(sign*1.39,.444,z),(sign*1.39,.462,z),.029,.029,'coal','root',6)
        rod('upper_roller',(sign*.72,-.09,2.70),(sign*.72,-.09,2.86),.07,.07,'iron',bn,8)
    box('lintel',(0,.015,2.94),(3.34,.78,.30),'rust','root',.06)
    box('guide_rail',(0,-.065,2.81),(4.60,.20,.11),'iron','root',.02)
    box('lower_guide',(0,-.035,.055),(4.60,.34,.11),'iron','root',.02)
    box('header_plate',(0,.422,2.965),(1.52,.045,.13),'coal','root',.018)
    for x in [-.57,-.38,.38,.57]:
        box('worn_header_mark',(x,.45,2.965),(.09,.018,.031),'bone','root')
    rig=skeleton('door_sealed',bones)

    def opening(r,t):
        def smooth(value):
            value=max(0,min(1,value))
            return value*value*(3-2*value)
        unlatch=smooth((t-.08)/.15)
        travel=smooth((t-.26)/.62)
        for side,sign in [('L',1),('R',-1)]:
            p=r.pose.bones['leaf.'+side]
            p.location=p.bone.matrix_local.to_3x3().inverted() @ Vector((sign*1.19*travel,-.06*unlatch,0))
    action(rig,'open',85,opening)
    deliver('door_sealed',rig,'open lasts 3.5 seconds: holds, unseals 0.06 m rearward, then slides each leaf 1.19 m outwards and holds. Conservative clear opening is 2.04 m wide by 2.60 m high above a 0.11 m threshold. Rails require a 4.6 m-wide placement envelope; hold the last pose.')

if __name__=='__main__':
    build()

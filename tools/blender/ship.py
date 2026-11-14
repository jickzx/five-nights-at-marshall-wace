"""Battered side-entry escape boat with an actual open cabin and hinge rig."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *
from console import geometry as console_geometry


def build():
    reset('ship')
    mesh('keel',[(-.925,-1.875,.34),(.925,-1.875,.34),(.70,2.12,.34),(-.70,2.12,.34),
                 (-.925,-1.875,.82),(.925,-1.875,.82),(.85,1.80,.82),(-.85,1.80,.82)],
         [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'rust','root')
    box('cabin_floor',(0,0,.88),(1.70,3.35,.13),'iron','root')
    box('port_wall',(-.84,0,1.48),(.14,3.4,1.22),'bone','root',.045)
    box('aft_wall',(0,-1.65,1.5),(1.7,.17,1.25),'rust','root',.06)
    box('starboard_front',(.84,.98,1.4),(.15,1.36,.95),'bone','root',.04)
    box('starboard_rear',(.84,-1.35,1.4),(.15,.45,.95),'bone','root',.03)
    box('roof',(0,-.1,2.15),(1.94,3.35,.20),'coal','root')
    # Open front cockpit visor, framed on three sides; the console remains visible through it.
    for x in [-.78,.78]:
        rod('windscreen_frame',(x,1.78,1.13),(x,1.45,2.12),.055,.055,'iron','root',6)
    box('dashboard',(0,1.70,1.12),(1.6,.30,.20),'rust','root',.045)
    console_geometry('cockpit_', 'root', (0,1.3,.85), .67)
    box('pilot_seat',(0,.25,1.09),(.52,.55,.15),'coal','root',.03)
    box('pilot_seat_back',(0,0,1.43),(.52,.14,.66),'coal','root',.03)
    # Door is authored open as a ramp; rotating around the Y-aligned hinge closes it.
    mesh('hatch',[(.92,-1.08,.85),(2.1,-1.08,.07),(2.1,.32,.07),(.92,.32,.85),
                  (.92,-1.08,.95),(2.1,-1.08,.17),(2.1,.32,.17),(.92,.32,.95)],
         [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'rust','hatch')
    for i in range(4):
        x=1.1+i*.22; z=.85-(x-.92)*.66
        rod('ramp_tread',(x,-1.0,z+.11),(x,.24,z+.11),.025,.025,'iron','hatch',4)
    for side,sign in [('L',1),('R',-1)]:
        x=sign*1.28
        rod('engine',(x,.65,1.05),(x,1.8,1.05),.38,.27,'iron','root',8)
        rod('engine_band',(x,.85,1.05),(x,1.05,1.05),.40,.40,'rust','root',8)
        rod('exhaust',(x,.62,1.05),(x,.53,1.05),.27,.22,'coal','root',8)
        rod('engine_light',(x,.52,1.05),(x,.50,1.05),.15,.15,'signal','root',8)
        for y in [-1.25,1.15]:
            rod('landing_strut',(sign*.65,y,.62),(sign*.95,y,.12),.07,.07,'iron','root',6)
            box('landing_skid',(sign*.95,y,.06),(.38,.55,.12),'coal','root',.025)
        box('nav_light',(sign*.94,1.35,2.18),(.12,.2,.06),'signal','root')
        for i in range(3):
            box('hull_patch',(sign*.925,-1.0+i*.78,1.6),(.035,.32,.22),'rust','root')
    mesh('tail_fin',[(-.04,-1.40,2.25),(-.04,-1.40,2.78),(-.04,-.6,2.25),
                     (.04,-1.40,2.25),(.04,-1.40,2.78),(.04,-.6,2.25)],
         [(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],'rust','root')
    rod('antenna',(-.6,-1.15,2.22),(-.6,-1.15,2.72),.026,.012,'iron','root',5)
    box('roof_mark',(0,-.5,2.26),(.27,1.2,.02),'bone','root')
    rig=skeleton('ship',[('root',(0,0,0),(0,0,.3),None),('hatch',(.92,-1.08,.85),(.92,.32,.85),'root')])
    action(rig,'idle',49,lambda r,t: None)
    def close(r,t):
        world_rotate(r,'hatch',(0,1,0),-2.15*(t*t*(3-2*t)))
    action(rig,'hatch_close',37,close)
    def takeoff(r,t):
        close(r,1)
        lift=max(0,(t-.15)/.85)
        root_move(r,(0,3*lift*lift,5*lift*lift))
        world_rotate(r,'root',(1,0,0),.15*lift)
    action(rig,'takeoff',97,takeoff)
    deliver('ship',rig,'Open side hatch and visible cockpit console. hatch_close rotates the ramp shut. takeoff starts shut and rises 5 m with 3 m forward root motion.')

if __name__=='__main__':
    build()

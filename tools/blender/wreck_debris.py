"""Collapsed pressure vessel, bent ribs and a severed red cable."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('wreck_debris')
    mesh('torn_hull',[(-1,-.6,.12),(.9,-.65,.1),(1.2,.1,.28),(.45,.7,.16),(-.6,.63,.32),
                      (-.8,-.47,.55),(.75,-.50,.48),(.82,.03,.72),(.30,.51,.56),(-.43,.48,.86)],
         [(0,1,6,5),(1,2,7,6),(2,3,8,7),(3,4,9,8)],'rust')
    for i in range(3):
        x=-.60+i*.5
        rod('broken_rib',(x,-.52,.13),(x,-.3,.93-i*.11),.055,.035,'bone',vertices=6)
        rod('rib_end',(x,-.3,.93-i*.11),(x,.15,.68-i*.10),.035,.015,'iron',vertices=6)
    rod('tank',(-.65,0,.23),(.55,.15,.30),.24,.24,'iron',vertices=8)
    rod('torn_pipe',(.70,-.30,.1),(.95,.35,.45),.08,.075,'coal',vertices=6)
    for i in range(4):
        box('shard',(-.80+i*.52,.45,.08),(.35,.24,.12),'rust',bevel=.015,rotation=(0,0,i*.8))
    rod('cable',(-.4,.2,.04),(.1,.8,.04),.02,.02,'blood',vertices=5)
    rod('cable',(.1,.8,.04),(.75,.6,.04),.02,.02,'blood',vertices=5)
    ground()
    join_parts('wreck_debris')
    deliver('wreck_debris')

if __name__=='__main__':
    build()

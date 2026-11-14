"""An angular gateway with a 2.3 m clear opening."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('ruin_arch')
    for sign in [-1,1]:
        box('foot',(sign*1.38,0,.12),(.83,.96,.24),'iron',bevel=.07)
        for i in range(4):
            box('jamb',(sign*1.38,0,.53+i*.52),(.46,.72,.48),'coal',bevel=.045)
        rod('sloping_lintel',(sign*1.38,0,2.35),(sign*.52,0,3.12),.34,.30,'rust',vertices=4)
        box('incision',(sign*1.38,.375,1.45),(.055,.018,1.1),'blood')
    box('keystone',(0,0,3.12),(1.12,.76,.43),'iron',bevel=.06)
    box('seal',(0,.398,3.12),(.16,.025,.20),'bone',rotation=(0,math.pi/4,0))
    join_parts('ruin_arch')
    deliver('ruin_arch',notes='Clear passage width 2.3 m. Static gateway, suitable for framing a mural or sealed door.')

if __name__=='__main__':
    build()

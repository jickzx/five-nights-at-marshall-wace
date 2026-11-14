"""An eroded alien pillar with narrow red incisions, no readable language."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('ruin_pillar')
    box('plinth',(0,0,.13),(1.20,1.05,.26),'iron',bevel=.10)
    for i in range(4):
        box('stone_course',(.02*math.sin(i),0,.59+i*.57),(.74-i*.035,.70,.51),'coal',bevel=.065,
            rotation=(0,0,.025*math.sin(i)))
    box('broken_cap',(.08,0,2.79),(.95,.83,.29),'rust',bevel=.08,rotation=(0,.07,.03))
    for i in range(4):
        box('inlay',(-.18+i*.10,.36,1.4+(i%2)*.16),(.023,.012,.51),'blood')
    ico('fallen_chip',(.55,.3,.13),(.25,.31,.16),'rust',subdivisions=2,jagged=.14)
    ground()
    join_parts('ruin_pillar')
    deliver('ruin_pillar')

if __name__=='__main__':
    build()

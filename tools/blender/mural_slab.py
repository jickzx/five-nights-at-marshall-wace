"""Broken standing tablet with an unoccluded blank picture face."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('mural_slab')
    box('slab',(0,0,1.08),(1.60,.26,2.16),'coal',bevel=.095)
    box('border',(0,.155,1.15),(1.42,.075,1.83),'rust',bevel=.045)
    front_quad('picture',0,.198,1.15,1.22,1.60,'bone')
    for sign in [-1,1]:
        box('base_support',(sign*.56,0,.13),(.34,.66,.26),'iron',bevel=.045)
        for z in [.41,1.9]:
            ico('corner_rivet',(sign*.66,.21,z),(.05,.035,.05),'bone')
    ico('chip',(.77,.12,.10),(.23,.25,.13),'rust',subdivisions=2)
    ground()
    deliver('mural_slab',notes='picture is a blank UV quad, 1.22 m wide and 1.60 m high, facing exported -Z. Replace its material with the game mural.')

if __name__=='__main__':
    build()

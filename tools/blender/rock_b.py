"""A tall split basalt fang, distinct at a distance from rock_a."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('rock_b')
    ico('spire',(-.15,0,1.0),(.55,.65,1.13),'coal',subdivisions=2,jagged=.20)
    ico('broken_fang',(.39,.05,.65),(.32,.43,.75),'rust',subdivisions=2,jagged=.23)
    for i in range(3):
        ico('base',(-.5+i*.45,.3,.19),(.30,.38,.26),'iron',subdivisions=2,jagged=.2)
    ground()
    join_parts('rock_b')
    deliver('rock_b')

if __name__=='__main__':
    build()

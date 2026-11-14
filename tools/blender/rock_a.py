"""A squat broken ironstone boulder, with faceted satellites."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('rock_a')
    ico('boulder',(0,0,.68),(1.05,.82,.73),'iron',subdivisions=2,jagged=.18)
    ico('rust_vein',(.25,.40,.68),(.74,.35,.47),'rust',subdivisions=2,jagged=.2)
    for i in range(3):
        ico('scree',(-.65+i*.59,.5,.17),(.30,.32,.22),'coal',subdivisions=2,jagged=.26)
    ground()
    join_parts('rock_a')
    deliver('rock_a')

if __name__=='__main__':
    build()

"""A flat 20 m blood tile. Two triangles deliberately leave waves to the game."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('blood_sea')
    mesh('blood_sea',[(-10,-10,0),(10,-10,0),(10,10,0),(-10,10,0)],[(0,1,2,3)],'blood',uv=True)
    deliver('blood_sea',notes='Flat 20 x 20 m plane, 2 triangles. Tile at water height y=0. No baked waves. Unity can replace the material with animated blood.')

if __name__=='__main__':
    build()

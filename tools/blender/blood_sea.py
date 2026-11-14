"""A flat twenty-metre blood surface, ready for the game's water material."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('blood_sea')
    material=MATERIALS['blood']
    material.name='blood_sea_surface'
    material.diffuse_color=(.255,.004,.010,1)
    bsdf=material.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=material.diffuse_color
    bsdf.inputs['Metallic'].default_value=.10
    bsdf.inputs['Roughness'].default_value=.22
    mesh('blood_sea',[(-10,-10,0),(10,-10,0),(10,10,0),(-10,10,0)],[(0,1,2,3)],'blood',uv=True)
    deliver('blood_sea',notes='Flat 20 x 20 m plane, two triangles, UVs span 0..1. Deep saturated blood colour and low roughness. No baked ripples or waves. Tile at water height Unity y=0, or y=0.36 to meet terrain_shore. The game may replace the material with animated blood.')

if __name__=='__main__':
    build()

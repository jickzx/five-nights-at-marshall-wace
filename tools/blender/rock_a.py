"""Low ironstone shelf split along a red mineral seam."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def stone(name, outline, levels, material):
    n = len(outline)
    vertices = []
    for z, scale, dx, dy in levels:
        vertices.extend([(x*scale+dx,y*scale+dy,z+dz*scale) for x,y,dz in outline])
    faces = [tuple(reversed(range(n)))]
    for row in range(len(levels)-1):
        for i in range(n):
            a=row*n+i; b=row*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    faces.append(tuple(range((len(levels)-1)*n,len(levels)*n)))
    obj=mesh(name,vertices,faces,material)
    for key in ['coal','rust','viscera']:
        obj.data.materials.append(MATERIALS[key])
    for poly in obj.data.polygons:
        if poly.index in {5,14,23,32}:
            poly.material_index=2
        elif 1<=poly.index<=n:
            poly.material_index=3
        elif poly.index in {8,17,26,35}:
            poly.material_index=1
    return obj


def build():
    reset('rock_a')
    outline=[(-1.12,-.37,.04),(-.72,-.77,-.02),(.20,-.84,.05),(.89,-.53,0),
             (1.0,.18,.07),(.53,.68,-.03),(-.17,.81,.02),(-.88,.55,0),(-1.15,.10,-.04)]
    stone('ironstone_shelf',outline,[(.08,1,0,0),(.27,1.02,0,0),(.72,.95,-.05,0),
                                      (1.20,.78,-.12,-.06),(1.38,.43,-.24,-.03)],'iron')
    stone('sheared_plate',[(x*.40,y*.53,z*.3) for x,y,z in outline],
          [(.04,1,.97,.21),(.18,1.05,.97,.21),(.58,.82,.89,.26),(.82,.62,.79,.29)],'rust')
    for index,(x,y,size) in enumerate([(-1.11,.52,.28),(.61,-.75,.23),(.21,.95,.20),(-.76,-.83,.18)]):
        stone('talus_'+str(index),[(a*size,b*size,c*size) for a,b,c in outline],
              [(.025,1,x,y),(.10+size*.38,.85,x+.03,y),(.13+size*.72,.28,x-.04,y+.03)],'coal')
    ground()
    join_parts('rock_a')
    deliver('rock_a',notes='Broad ironstone shelf with an exposed red fracture and a dark blood-tide band at the base. Footprint approximately 2.8 x 2.1 m.')

if __name__=='__main__':
    build()

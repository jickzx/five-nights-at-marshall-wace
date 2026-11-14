"""Split basalt fang, two opposed fracture faces above a drowned base."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def fang(name, rings, outline, exposed):
    n=len(outline)
    verts=[(x*s+dx,y*s+dy,z+dz*s) for z,s,dx,dy in rings for x,y,dz in outline]
    faces=[tuple(reversed(range(n)))]
    for j in range(len(rings)-1):
        faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for i in range(n)]
    faces.append(tuple(range((len(rings)-1)*n,len(rings)*n)))
    obj=mesh(name,verts,faces,'coal')
    for key in ['iron','rust','viscera']:
        obj.data.materials.append(MATERIALS[key])
    for poly in obj.data.polygons:
        poly.material_index = 3 if 1<=poly.index<=n else (2 if (poly.index-1)%n in exposed else 1)
    return obj


def build():
    reset('rock_b')
    left=[(-.58,-.35,.02),(-.18,-.55,-.04),(.22,-.40,.03),(.29,.21,-.03),
          (-.12,.48,.02),(-.58,.25,-.01)]
    fang('main_fang',[(.06,1.15,-.29,0),(.28,1.1,-.29,0),(1.15,.92,-.27,.02),
                       (1.96,.65,-.49,.04),(2.59,.18,-.43,.03)],left,{2})
    right=[(-.25,-.36,.01),(.30,-.42,-.02),(.60,-.12,.03),(.48,.36,-.02),
           (.01,.45,.04),(-.23,.12,-.02)]
    fang('broken_fang',[(.05,1.0,.40,0),(.27,1.04,.40,0),(1.04,.84,.43,.04),
                         (1.81,.54,.53,.03),(1.99,.22,.60,.05)],right,{5})
    for index,(x,y,s) in enumerate([(-.8,.34,.33),(.16,.57,.30),(.84,-.29,.26),(-.13,-.56,.23)]):
        fang('fallen_split_'+str(index),[(.03,s,x,y),(.20,s*.9,x+.02,y),(.34,s*.35,x-.03,y)],left,{1})
    ground()
    join_parts('rock_b')
    deliver('rock_b',notes='Tall split basalt fang, 2.6 m high. The narrow open fissure and opposed red fracture faces remain readable in silhouette. Place the dark base below the blood waterline.')

if __name__=='__main__':
    build()

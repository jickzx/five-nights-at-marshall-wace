"""A shattered votive tablet with a clear, UV-mapped picture face."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('mural_slab')
    outline=[(-.84,.16),(-.76,2.24),(-.62,2.41),(-.18,2.38),(.02,2.52),
             (.48,2.42),(.80,2.12),(.83,.26),(.65,.11),(-.52,.08)]
    outline.reverse()
    n=len(outline)
    verts=[(x,y,z) for y in [-.19,.18] for x,z in outline]
    faces=[tuple(range(n)),tuple(reversed(range(n,2*n)))]
    faces.extend([(i,n+i,n+(i+1)%n,(i+1)%n) for i in range(n)])
    slab=mesh('broken_slab',verts,faces,'coal')
    slab.data.materials.append(MATERIALS['iron'])
    slab.data.materials.append(MATERIALS['rust'])
    for poly in slab.data.polygons:
        if poly.index>1:
            poly.material_index=2 if poly.index in {4,5,6,7} else 1
    box('drowned_foot',(0,-.015,.10),(1.62,.70,.20),'viscera',bevel=.055)
    for sign in [-1,1]:
        box('picture_border',(sign*.681,.205,1.24),(.12,.075,1.90),'rust',bevel=.014)
        box('edge_moulding',(sign*.766,.192,1.28),(.036,.055,1.83),'iron')
    for z in [.315,2.165]:
        box('picture_border',(0,.205,z),(1.43,.075,.12),'rust',bevel=.014)
    front_quad('picture',0,.247,1.24,1.22,1.60,'bone')
    for sign in [-1,1]:
        for z in [.33,2.15]:
            rod('recessed_pin',(sign*.676,.247,z),(sign*.676,.269,z),.034,.034,'iron',vertices=6)
        rod('votive_cut',(0,.202,2.28),(sign*.11,.202,2.39),.015,.015,'blood',vertices=4)
    # The broken crown and rear fault stop outside the reserved picture rectangle.
    mesh('rear_fault',[(-.42,-.192,.34),(-.37,-.192,1.0),(-.16,-.192,1.43),(-.20,-.192,2.17),
                       (-.16,-.193,1.0),(-.05,-.193,1.43),(-.17,-.193,2.17)],
         [(4,1,0),(4,5,2,1),(5,6,3,2)],'rust')
    mesh('fallen_corner',[(.65,.25,0),(.99,.30,0),(.86,.64,0),(.62,.53,0),
                          (.71,.32,.22),(.80,.51,.16)],
         [(0,3,2,1),(0,1,4),(1,2,5,4),(2,3,5),(3,0,4,5)],'rust')
    deliver('mural_slab',notes='picture is a blank UV quad, 1.22 m wide and 1.60 m high, centered at Unity (0, 1.24, -0.247), facing -Z. Picture rectangle is completely unobstructed; assign the game mural material to that mesh.')

if __name__=='__main__':
    build()

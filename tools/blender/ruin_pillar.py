"""A single eroded alien monolith with a broken crown and drowned footing."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('ruin_pillar')
    ring=[(-.48,-.32),(-.31,-.48),(.34,-.44),(.47,-.24),(.44,.30),(.29,.43),(-.31,.43),(-.47,.27)]
    levels=[(0,1.37,0),(.19,1.37,0),(.32,1.02,0),(1.17,.94,-.025),(2.52,.83,.025),(3.01,.80,.035)]
    verts=[]
    for j,(z,scale,dx) in enumerate(levels):
        for i,(x,y) in enumerate(ring):
            chip=[-.08,.11,.17,-.06,-.25,.10,-.05,-.19][i] if j==5 else 0
            verts.append((x*scale+dx,y*scale,z+chip))
    n=len(ring)
    faces=[tuple(reversed(range(n)))]
    for j in range(len(levels)-1):
        faces.extend([(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for i in range(n)])
    faces.append(tuple(range(5*n,6*n)))
    pillar=mesh('eroded_monolith',verts,faces,'iron')
    for key in ['coal','rust','viscera']:
        pillar.data.materials.append(MATERIALS[key])
    for poly in pillar.data.polygons:
        if 1<=poly.index<=n:
            poly.material_index=3
        elif (poly.index-1)%n==3 and poly.index>n:
            poly.material_index=2
        elif (poly.index-1)%n==7 or poly.index==41:
            poly.material_index=1
    # Narrow red cuts share the forked seal used on the gate and mural.
    for x in [-.13,.13]:
        rod('buried_inlay',(x,.407,.65),(x,.370,2.08),.015,.015,'blood',vertices=4)
    for sign in [-1,1]:
        rod('forked_seal',(0,.371,2.16),(sign*.21,.365,2.43),.022,.022,'rust',vertices=4)
    rod('seal_stem',(0,.371,1.89),(0,.371,2.16),.022,.022,'bone',vertices=4)
    for z in [.83,1.0,1.17]:
        box('scored_notch',(-.31,.404-(z-.8)*.027,z),(.09,.012,.014),'coal',rotation=(0,.12,0))
    mesh('fallen_crown',[(-.62,.23,0),(-.33,.48,0),(-.72,.76,0),(-.92,.43,0),
                         (-.65,.30,.25),(-.42,.46,.15),(-.74,.64,.12)],
         [(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,4,6),(3,0,4),(4,5,6)],'rust')
    join_parts('ruin_pillar')
    deliver('ruin_pillar',notes='3.1 m eroded monolith. Forked sacrificial seal faces exported -Z; lower 0.19 m carries a blood-tide band. Base origin at ground.')

if __name__=='__main__':
    build()

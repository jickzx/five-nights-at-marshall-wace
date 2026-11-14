"""A cleft, angular alien gate cut from two massive weathered stones."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def carved_stone(name, outline, depth):
    cx=sum(x for x,z in outline)/len(outline)
    cz=sum(z for x,z in outline)/len(outline)
    n=len(outline)
    verts=[]
    for y,scale in [(-depth/2,.97),(-depth/2+.065,1),(depth/2-.065,1),(depth/2,.97)]:
        verts.extend([(cx+(x-cx)*scale,y,cz+(z-cz)*scale) for x,z in outline])
    faces=[tuple(range(n))]
    for row in range(3):
        faces.extend([(row*n+i,(row+1)*n+i,(row+1)*n+(i+1)%n,row*n+(i+1)%n) for i in range(n)])
    faces.append(tuple(reversed(range(3*n,4*n))))
    obj=mesh(name,verts,faces,'iron')
    obj.data.materials.append(MATERIALS['coal'])
    obj.data.materials.append(MATERIALS['rust'])
    for poly in obj.data.polygons:
        if n+1<=poly.index<=2*n:
            poly.material_index=1
        elif poly.index in {4,6,8,27}:
            poly.material_index=2
    return obj


def build():
    reset('ruin_arch')
    outline=[(1.15,.13),(1.76,.13),(1.73,2.43),(1.51,2.73),(.50,3.48),
             (.16,3.36),(.16,2.92),(.66,2.61),(1.15,2.12)]
    for sign in [-1,1]:
        profile=list(outline)
        if sign>0:
            profile[2:3]=[(1.73,1.54),(1.64,1.70),(1.73,1.84),(1.73,2.43)]
        else:
            profile[4:5]=[(.69,3.34),(.61,3.28),(.50,3.48)]
        points=[(sign*x,z) for x,z in profile]
        if sign<0:
            points.reverse()
        carved_stone('stone_arch_'+str(sign),points,.78)
        box('drowned_foot',(sign*1.455,0,.10),(.83,1.04,.20),'viscera',bevel=.055)
        box('foot_shoulder',(sign*1.455,0,.23),(.72,.88,.12),'rust',bevel=.03)
        rod('inner_red_cut',(sign*1.24,.404,.43),(sign*1.24,.404,2.13),.016,.016,'blood',vertices=4)
        rod('sloping_red_cut',(sign*1.24,.402,2.13),(sign*.34,.402,2.94),.016,.016,'blood',vertices=4)
        for z in [.58,.76,.94]:
            box('eroded_tally',(sign*1.58,.402,z),(.10,.018,.018),'coal',rotation=(0,sign*.14,0))
    carved_stone('hanging_keystone',[(-.24,2.99),(0,2.76),(.24,2.99),(.20,3.49),(-.17,3.53)],.88)
    for sign in [-1,1]:
        rod('forked_seal',(0,.462,3.13),(sign*.12,.462,3.28),.022,.022,'bone',vertices=4)
    rod('seal_stem',(0,.462,2.95),(0,.462,3.13),.022,.022,'rust',vertices=4)
    mesh('broken_foot_chip',[(1.75,.30,0),(2.02,.40,0),(1.91,.75,0),(1.67,.65,0),
                             (1.82,.46,.25),(1.73,.60,.12)],
         [(0,3,2,1),(0,1,4),(1,2,5,4),(2,3,5),(3,0,4,5)],'iron')
    ground()
    join_parts('ruin_arch')
    deliver('ruin_arch',notes='2.30 m clear opening below 2.12 m; crown rises to 3.53 m. Forked alien seal faces exported -Z. Keep the small fallen chip on the right outside the walking path.')

if __name__=='__main__':
    build()

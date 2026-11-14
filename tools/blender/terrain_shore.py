"""20 x 20 m triangulated shore; dry back edge, submerged front edge."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('terrain_shore')
    verts=[]
    n=12
    for j in range(n+1):
        y=-10+20*j/n
        for i in range(n+1):
            x=-10+20*i/n
            # Zero at y=4. Repeated tiles share the same edge profile on X.
            height=.10*(4-y) if y<=4 else -.06*(y-4)
            ripple=.11*math.sin(i*math.tau/n)*math.sin(j*math.pi/n) if y<3 else 0
            verts.append((x,y,height+ripple))
    faces=[]
    for j in range(n):
        for i in range(n):
            a=j*(n+1)+i;b=a+1;c=a+n+1;d=c+1
            faces.extend([(a,b,d),(a,d,c)] if (i+j)%2 else [(a,b,c),(b,d,c)])
    obj=mesh('terrain_shore',verts,faces,'iron')
    obj.data.materials.append(MATERIALS['rust'])
    obj.data.materials.append(MATERIALS['coal'])
    for poly in obj.data.polygons:
        poly.material_index=random.choices([0,1,2],[5,2,4])[0]
    ground()
    deliver('terrain_shore',notes='20 x 20 m, origin at the lowest base. Waterline is y=0.36 in Unity, at z=-4. Dry edge z=+10, submerged edge z=-10. Place blood_sea at (0, 0.36, -14) to meet the shore. Repeat shore along X in 20 m steps.')

if __name__=='__main__':
    build()

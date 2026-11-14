"""Twenty-metre eroded ironstone shore descending into a flat blood sea."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def elevation(x,y):
    if y>=4:
        return -.06*(y-4)
    fade=min(1,max(0,(3.2-y)/3.5))
    seam=math.sin((x+10)*math.pi/20)**2
    ridge=.19*math.sin(x*.58+y*.12)*math.sin((y+10)*math.pi/14)
    channel_center=1.2+.17*(y-3)
    channel=-.16*math.exp(-((x-channel_center)/1.6)**2)
    terrace=.20*max(0,min(1,(-y-3.2)/2))*math.sin((y+10)*math.pi/14)
    return .10*(4-y)+(ridge+channel+terrace)*fade*seam


def build():
    reset('terrain_shore')
    n=20
    verts=[]
    for j in range(n+1):
        y=-10+20*j/n
        for i in range(n+1):
            x=-10+20*i/n
            verts.append((x,y,elevation(x,y)))
    faces=[]
    for j in range(n):
        for i in range(n):
            a=j*(n+1)+i;b=a+1;c=a+n+1;d=c+1
            faces.extend([(a,b,d),(a,d,c)] if (i+j)%2 else [(a,b,c),(b,d,c)])
    obj=mesh('terrain_shore',verts,faces,'iron')
    for key in ['rust','coal','viscera','blood']:
        obj.data.materials.append(MATERIALS[key])
    for poly in obj.data.polygons:
        x=sum(verts[v][0] for v in poly.vertices)/3
        y=sum(verts[v][1] for v in poly.vertices)/3
        tide=1.0+.35*math.cos(x*.5)
        if y>3.4:
            poly.material_index=3
        elif y>tide:
            poly.material_index=2
        elif abs(x-(1.2+.17*(y-3)))<.7 and y>-.9:
            poly.material_index=3
        elif y<-.2 and math.sin(x*.42+y*.28)+.45*math.cos(y*.75)>.75:
            poly.material_index=1
        else:
            poly.material_index=0
    # Long shallow runnels break the wet shoreline without adding waves or
    # protruding decoration that would obstruct the player capsule.
    for index,(cx,start,end,width) in enumerate([(-4.2,.2,4,.10),(1.2,-1.4,4,.18),(6.1,1.1,4,.07)]):
        points=[]
        steps=6
        for j in range(steps+1):
            y=start+(end-start)*j/steps
            x=cx+.18*math.sin(j*.9+index)
            w=width*(.25+.75*j/steps)
            points.extend([(x-w,y,elevation(x-w,y)+.004),(x+w,y,elevation(x+w,y)+.004)])
        mesh('blood_runnel_'+str(index),points,[(j*2,j*2+1,j*2+3,j*2+2) for j in range(steps)],'viscera')
    ground()
    join_parts('terrain_shore')
    deliver('terrain_shore',notes='20 x 20 m. Origin at lowest base, waterline remains Unity y=0.36 at z=-4; dry edge z=+10 and submerged edge z=-10. Place blood_sea at (0,0.36,-14). Repeats along X every 20 m with matching edge heights. Eroded channels stay shallow for walking; the wet band is geometry colour, not a separate water surface.')

if __name__=='__main__':
    build()

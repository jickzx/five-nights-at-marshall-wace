"""Ruptured pressure-vessel hull with bent flanges and exposed inner ribs."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def build():
    reset('wreck_debris')
    # A torn section of a cylindrical hull. The jagged rim follows a fracture,
    # while the inner skin remains visible through the missing upper half.
    angles=[-2.68,-2.18,-1.67,-1.15,-.64,-.18,.12]
    xs=[-1.16,-.70,.06,.69,1.18]
    verts=[]
    for row,x in enumerate(xs):
        for i,angle in enumerate(angles):
            radius=.76 if row in [1,2,3] else .72
            tear=([-.14,.11,-.06,.13,-.16,.10,-.09][i] if row==4 else
                  [.03,-.11,.10,-.10,.07,-.14,.15][i] if row==0 else 0)
            rim_damage=([0,-.23,.10,-.27,-.03][row] if i==6 else 0)
            verts.append((x+tear,math.cos(angle)*radius,math.sin(angle)*radius+.81+rim_damage))
    n=len(angles)
    faces=[]
    for j in range(len(xs)-1):
        for i in range(n-1):
            a=j*n+i
            if j==1 and i==5:
                continue
            faces.append((a,a+1,a+1+n,a+n))
    hull=mesh('ruptured_hull',verts,faces,'rust')
    hull.data.materials.append(MATERIALS['iron'])
    for poly in hull.data.polygons:
        poly.material_index=1 if poly.index in {2,3,8,9,14,15} else 0
    inner=[(x,y*.91,(z-.81)*.91+.81) for x,y,z in verts]
    mesh('blackened_inner_skin',inner,[tuple(reversed(face)) for face in faces],'coal')
    edges={}
    for face in faces:
        for a,b in zip(face,face[1:]+face[:1]):
            key=tuple(sorted((a,b)))
            edges.setdefault(key,[]).append((a,b))
    rim_faces=[]
    total=len(verts)
    for uses in edges.values():
        if len(uses)==1:
            a,b=uses[0]
            rim_faces.append((b,a,a+total,b+total))
    mesh('torn_exposed_edges',verts+inner,rim_faces,'bone')
    for x in [-.76,.02,.72]:
        points=[(x,math.cos(a)*.70,math.sin(a)*.70+.81) for a in angles[:-1]]
        for a,b in zip(points,points[1:]):
            rod('internal_frame_rib',a,b,.035,.035,'iron',vertices=5)
    for side in [-1,1]:
        for x in [-.84,-.35,.23,.73]:
            y=side*.57
            rod('sheared_rivet',(x,y,.32),(x,y,.37),.022,.022,'bone',vertices=6)
    rod('ruptured_tank',(-.62,.10,.26),(.47,.16,.31),.20,.20,'iron',vertices=8)
    rod('tank_rust_collar',(-.46,.11,.27),(-.39,.115,.273),.215,.215,'rust',vertices=8)
    rod('severed_pipe',(.69,.06,.21),(1.07,.38,.37),.075,.075,'coal',vertices=6)
    rod('open_pipe_rim',(1.02,.34,.35),(1.09,.40,.38),.081,.081,'rust',vertices=6)
    mesh('peeled_hull_strip',[(.77,.67,.22),(1.03,.58,.48),(1.29,.61,.67),(1.49,.88,.47),
                              (.93,.86,.18),(1.18,.78,.43),(1.41,.81,.61),(1.58,1.03,.42)],
         [(0,1,5,4),(1,2,6,5),(2,3,7,6)],'rust')
    for x,y,angle in [(-1.22,.54,.2),(.40,.92,-.4),(1.31,-.35,.7)]:
        mesh('fractured_plate',[(x-.24,y-.14,.015),(x+.22,y-.10,.025),(x+.14,y+.14,.10),
                                 (x-.14,y+.18,.12),(x-.07,y,.19)],
             [(0,1,4),(1,2,4),(2,3,4),(3,0,4),(3,2,1,0)],'iron')
    cable=[(-.33,.23,.07),(-.46,.69,.045),(-.12,1.01,.035),(.31,1.11,.035),(.63,.96,.04)]
    for a,b in zip(cable,cable[1:]):
        rod('severed_red_cable',a,b,.023,.023,'blood',vertices=5)
    rod('exposed_conductor',(.63,.96,.04),(.76,.91,.04),.011,.011,'bone',vertices=5)
    ground()
    join_parts('wreck_debris')
    deliver('wreck_debris',notes='Ruptured pressure-vessel section, open interior and bent ribs. Low scattered plates and a severed cable extend beyond the main hull; use a simple hull collider if collision is required.')

if __name__=='__main__':
    build()

"""Blank, UV-mapped screen and four independently movable tile objects."""
from common import *


def geometry(prefix='', bone=None, offset=(0,0,0), scale=1):
    start=len(PARTS)
    box(prefix+'pedestal',(0,0,.48),(.55,.45,.96),'iron',bone,.065)
    box(prefix+'foot',(0,0,.07),(.85,.65,.14),'rust',bone,.035)
    box(prefix+'terminal_body',(0,.03,1.08),(1.08,.45,.38),'rust',bone,.06)
    box(prefix+'screen_bezel',(0,.27,1.14),(.89,.06,.25),'coal',bone,.025)
    front_quad(prefix+'screen',0,.306,1.14,.77,.17,'screen',bone)
    for i in range(4):
        tile=box(prefix+f'tile_{i}',((i-1.5)*.22,.29,.93),(.185,.10,.13),'bone',bone,.012)
        tile['interaction']='press 0.025 m along local +Z in exported coordinates; restore after release'
    for x in [-.48,.48]:
        rod(prefix+'support',(x,-.1,.75),(x,-.1,1.28),.035,.035,'iron',bone,6)
    for i in range(3):
        box(prefix+'status',(.39,.31,1.2-i*.055),(.023,.014,.021),'signal',bone)
    for obj in PARTS[start:]:
        for v in obj.data.vertices:
            v.co = v.co*scale+Vector(offset)
    return PARTS[start:]


def build():
    reset('console')
    geometry()
    deliver('console',notes='screen is a blank front-facing UV quad. tile_0..tile_3 are independent meshes. Screen and tile faces point -Z in exports.')

if __name__=='__main__':
    build()

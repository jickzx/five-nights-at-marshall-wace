"""Battered cast-metal terminal with a blank screen and four pressable keys."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import *


def geometry(prefix='', bone=None, offset=(0,0,0), scale=1):
    start = len(PARTS)

    def part(name, at, size, material, bevel=0, rotation=None):
        return box(prefix+name, at, size, material, bone, bevel, rotation)

    def profile(name, yz, width, material):
        n = len(yz)
        vertices = [(x, y, z) for x in [-width/2, width/2] for y, z in yz]
        faces = [tuple(reversed(range(n))), tuple(range(n, 2*n))]
        faces += [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)]
        return mesh(prefix+name, vertices, faces, material, bone)

    part('foot', (0,-.015,.065), (.82,.67,.13), 'rust', .055)
    part('rubber_mount', (0,-.015,.14), (.52,.46,.07), 'coal', .02)
    profile('pedestal', [(-.19,.17),(.17,.17),(.10,.85),(-.13,.97),(-.22,.72)], .43, 'iron')
    part('pedestal_front_plate', (0,.147,.43), (.29,.018,.39), 'rust', .025)
    for z in [.30,.38,.46,.54]:
        part('pedestal_vent', (0,.160,z), (.19,.015,.018), 'coal')
    profile('terminal_casting', [(-.27,.90),(.28,.90),(.36,1.02),(.30,1.34),
                                  (.18,1.43),(-.19,1.43),(-.30,1.29)], 1.08, 'rust')
    profile('left_cheek', [(-.27,.91),(.26,.91),(.345,1.02),(.285,1.34),
                          (.17,1.415),(-.19,1.415),(-.29,1.28)], .055, 'iron')
    cheek = PARTS[-1]
    for vertex in cheek.data.vertices:
        vertex.co.x -= .554
    profile('right_cheek', [(-.27,.91),(.26,.91),(.345,1.02),(.285,1.34),
                           (.17,1.415),(-.19,1.415),(-.29,1.28)], .055, 'iron')
    for vertex in PARTS[-1].data.vertices:
        vertex.co.x += .554
    part('screen_bezel', (0,.303,1.235), (.93,.055,.285), 'coal', .018)
    part('screen_inner_lip', (0,.334,1.235), (.835,.014,.229), 'iron', .008)
    front_quad(prefix+'screen', 0,.343,1.235,.79,.19,'screen',bone)
    part('key_recess', (0,.345,1.001), (.96,.035,.167), 'coal', .012)
    for i in range(4):
        tile = part(f'tile_{i}', ((i-1.5)*.226,.374,1.002), (.193,.063,.125), 'bone', .014)
        tile['interaction'] = 'press 0.025 m along local +Z in exported coordinates; restore after release'
    for sign in [-1,1]:
        for z in [1.08,1.34]:
            rod(prefix+'guard_mount', (sign*.555,.01,z),(sign*.622,.01,z),.034,.034,'iron',bone,6)
        rod(prefix+'grab_rail', (sign*.622,.01,1.08),(sign*.622,.01,1.34),.031,.031,'bone',bone,6)
        for z in [.93,1.36]:
            rod(prefix+'case_bolt', (sign*.485,.303,z),(sign*.485,.322,z),.021,.021,'iron',bone,6)
    part('brow_strip', (0,.268,1.393), (.72,.052,.028), 'iron')
    for i in range(3):
        part('status', (.443,.345,1.31-i*.065), (.019,.018,.025), 'signal' if i<2 else 'blood', .003)
    part('rear_service_panel', (0,-.304,1.169), (.79,.017,.215), 'coal', .025)
    for i in range(6):
        part('cooling_fin', ((i-2.5)*.109,-.319,1.18), (.049,.032,.137), 'iron')
    cable = [(.20,-.25,.96),(.26,-.36,.80),(.19,-.40,.55),(.16,-.37,.23),(.27,-.28,.08)]
    for i,(a,b) in enumerate(zip(cable,cable[1:])):
        rod(prefix+'armoured_cable', a,b,.025,.025,'coal',bone,6)
    for i in range(2):
        part('worn_warning', (-.265+i*.11,.293,1.39), (.065,.015,.025), 'bone')
    for obj in PARTS[start:]:
        for vertex in obj.data.vertices:
            vertex.co = vertex.co*scale+Vector(offset)
    return PARTS[start:]


def build():
    reset('console')
    geometry()
    deliver('console', notes='screen is a blank 0.79 x 0.19 m front-facing UV quad. tile_0..tile_3 are independent meshes. Screen and tile faces point -Z in exports. Keys depress 0.025 m along exported +Z.')

if __name__ == '__main__':
    build()

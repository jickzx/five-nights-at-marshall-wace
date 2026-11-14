"""Inspect the actual binary FBX contract without a Unity installation."""
import json
import sys
from pathlib import Path
import bpy
from io_scene_fbx import parse_fbx
ROOT=Path(__file__).resolve().parents[2]


def children(node, identifier):
    return [e for e in node.elems if e.id==identifier]


def properties(node):
    containers=children(node,b'Properties70')
    return {e.props[0].decode():list(e.props[4:]) for e in containers[0].elems} if containers else {}


def inspect(path):
    root,version=parse_fbx.parse(str(path))
    settings=properties(children(root,b'GlobalSettings')[0])
    objs=children(root,b'Objects')[0]
    models=children(objs,b'Model')
    roots=[m for m in models if m.props[2] in (b'Null',b'Mesh')]
    transforms={m.props[1].split(b'\x00')[0].decode():properties(m) for m in roots}
    takes=[m.props[1].split(b'\x00')[0].decode() for m in children(objs,b'AnimationStack')]
    return {'version':version,'settings':{k:settings[k] for k in ['UpAxis','UpAxisSign','FrontAxis','FrontAxisSign','UnitScaleFactor']},'takes':takes,
            'transforms':{n:{k:v for k,v in p.items() if k.startswith('Lcl')} for n,p in transforms.items()}}


if __name__=='__main__':
    for path in sorted((ROOT/'Unity/Assets/Resources/Models').glob('*.fbx')):
        result=inspect(path)
        print(path.name,json.dumps(result))

"""Blender --background --python tools/blender/build_all.py -- [--pass1] [--only NAME ...]."""
import argparse
import importlib
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))

ALL=['beast_stage1','player','ship','console','beast_stage2','beast_stage3',
     'rock_a','rock_b','ruin_pillar','ruin_arch','door_sealed','mural_slab',
     'wreck_debris','terrain_shore','blood_sea']
args=argparse.ArgumentParser()
args.add_argument('--pass1',action='store_true')
args.add_argument('--only',nargs='+',choices=ALL)
options=args.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
for name in options.only or (ALL[:4] if options.pass1 else ALL):
    mod=importlib.import_module(name)
    if name.startswith('beast_stage'):
        mod.build_creature(int(name[-1]))
    else:
        mod.build()

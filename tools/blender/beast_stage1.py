import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from creature import build_creature
if __name__ == '__main__':
    build_creature(1)

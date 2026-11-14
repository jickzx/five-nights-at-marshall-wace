"""Compose multi-angle review sheets from Blender renders (system Python + Pillow)."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('spec', type=Path)
args = parser.parse_args()
spec = json.loads(args.spec.read_text())
font = ImageFont.truetype('/System/Library/Fonts/Menlo.ttc', 16)
small = ImageFont.truetype('/System/Library/Fonts/Menlo.ttc', 13)
for sheet in spec:
    columns = sheet.get('columns', 3)
    size = sheet.get('cell_size', 320)
    cells = sheet['cells']
    rows = (len(cells) + columns - 1) // columns
    canvas = Image.new('RGB', (columns * size, 55 + rows * (size + 28)), '#100e12')
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 17), sheet['title'], font=font, fill='#e5d9be')
    for index, cell in enumerate(cells):
        x, y = index % columns * size, 55 + index // columns * (size + 28)
        source = Image.open(cell['path']).convert('RGB').resize((size, size), Image.Resampling.LANCZOS)
        canvas.paste(source, (x, y))
        draw.text((x + 8, y + size + 5), cell['label'], font=small, fill='#b6aaa3')
    canvas.save(sheet['output'])

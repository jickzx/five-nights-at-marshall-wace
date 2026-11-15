"""Builds the 256x256 point-filtered atlas for the "Principal" beast (rival-fund wordmarks).

Run with a system python3 that has Pillow:  python3 tools/blender/ceo_beast_atlas.py
Top half (y 0-127) is the clean suit; the bottom half (y 128-255) repeats the exact same layout
smeared with blood, so stage 3 just offsets V by half the atlas.

Marks are plain block-capital pixel wordmarks of the funds' names (no real logos reproduced),
and no real person is depicted or named anywhere.
"""
import json
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Unity/Assets/Resources/Models/Textures/beast_ceo_atlas.png'
FONT = '/System/Library/Fonts/Supplemental/Arial Black.ttf'
NARROW = '/System/Library/Fonts/Supplemental/Arial Narrow Bold.ttf'

IVORY = (222, 210, 172)
BONE = (165, 152, 112)
COAL = (16, 18, 20)
CHAR = (34, 36, 40)
RUST = (112, 34, 18)
BLOOD = (110, 4, 8)
DARK_BLOOD = (52, 2, 6)
SKIN = (92, 104, 96)      # grey-green: the only cool hue in the game
EYE = (255, 24, 8)
NAVY = (22, 34, 64)

# (name, x, y, w, h). Every UV in the Blender build is taken from this table.
CELLS = {
    'pass_citadel':   (0, 0, 128, 64),
    'patch_point72':  (128, 0, 128, 64),
    'patch_millennium': (0, 64, 64, 32),
    'patch_balyasny': (64, 64, 64, 32),
    'patch_deshaw':   (128, 64, 64, 32),
    'badge_man':      (192, 64, 64, 32),
    'strip_rentec':   (0, 96, 64, 32),
    'tie_twosigma':   (64, 96, 32, 32),
    'ribbon':         (96, 96, 16, 32),
    'face':           (112, 96, 32, 32),
    'suit':           (144, 96, 32, 32),
    # 16x16 flat swatches
    'shirt':  (176, 96, 16, 16), 'skin':  (192, 96, 16, 16), 'coal': (208, 96, 16, 16),
    'blood':  (224, 96, 16, 16), 'dark_blood': (240, 96, 16, 16),
    'bone':   (176, 112, 16, 16), 'teeth': (192, 112, 16, 16), 'eye': (208, 112, 16, 16),
    'rust':   (224, 112, 16, 16), 'card':  (240, 112, 16, 16),
}
SWATCH = {'shirt': IVORY, 'skin': SKIN, 'coal': COAL, 'blood': BLOOD, 'dark_blood': DARK_BLOOD,
          'bone': BONE, 'teeth': IVORY, 'eye': EYE, 'rust': RUST, 'card': (236, 232, 220)}


def fit_text(draw, box, text, fill, font_path=FONT, max_size=40, pad=2):
    x, y, w, h = box
    size = max_size
    while size > 5:
        font = ImageFont.truetype(font_path, size)
        l, t, r, b = draw.textbbox((0, 0), text, font=font)
        if r - l <= w - 2 * pad and b - t <= h - 2 * pad:
            break
        size -= 1
    draw.text((x + (w - (r - l)) // 2 - l, y + (h - (b - t)) // 2 - t), text, font=font, fill=fill)


def patch(draw, name, bg, fg, text, border=None, font_path=FONT, text_box=None):
    x, y, w, h = CELLS[name]
    draw.rectangle([x, y, x + w - 1, y + h - 1], fill=bg)
    if border:
        draw.rectangle([x, y, x + w - 1, y + h - 1], outline=border)
        draw.rectangle([x + 2, y + 2, x + w - 3, y + h - 3], outline=border)  # stitched edge
    fit_text(draw, text_box or (x + 2, y + 2, w - 4, h - 4), text, fg, font_path)


def clean(img):
    d = ImageDraw.Draw(img)
    d.fontmode = '1'   # no anti-aliasing: hard PS1 pixels
    # Conference pass: ivory card, rust header band, big CITADEL, small role line.
    x, y, w, h = CELLS['pass_citadel']
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=(236, 232, 220))
    d.rectangle([x, y, x + w - 1, y + 13], fill=RUST)
    fit_text(d, (x + 4, y + 1, w - 8, 12), 'ALPHA SUMMIT  DELEGATE', IVORY, NARROW)
    fit_text(d, (x + 3, y + 15, w - 6, 34), 'CITADEL', COAL)
    d.rectangle([x + 6, y + 51, x + w - 7, y + 59], fill=COAL)
    fit_text(d, (x + 8, y + 51, w - 16, 9), 'ALL ACCESS', (236, 232, 220), NARROW, pad=0)
    d.rectangle([x, y, x + w - 1, y + h - 1], outline=CHAR)
    # Big back patch.
    patch(d, 'patch_point72', COAL, IVORY, 'POINT72', border=IVORY, text_box=(132, 6, 120, 52))
    patch(d, 'patch_millennium', NAVY, IVORY, 'MILLENNIUM', border=IVORY, font_path=NARROW)
    patch(d, 'patch_balyasny', IVORY, NAVY, 'BALYASNY', border=NAVY, font_path=NARROW)
    patch(d, 'patch_deshaw', COAL, IVORY, 'D.E.SHAW', border=IVORY, font_path=NARROW)
    patch(d, 'badge_man', (236, 232, 220), COAL, 'MAN GROUP', border=RUST, font_path=NARROW)
    patch(d, 'strip_rentec', RUST, IVORY, 'RENTEC', border=IVORY)
    # Two Sigma tie tile: rust with an ivory "2Σ" glyph and a thin diagonal stripe.
    x, y, w, h = CELLS['tie_twosigma']
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=RUST)
    for i in range(-w, w, 16):
        d.line([x + i, y + h - 1, x + i + h - 1, y], fill=(80, 22, 12), width=2)
    fit_text(d, (x + 4, y + 6, 24, 20), '2Σ', IVORY, FONT)
    # Lanyard ribbon: rust with tiny ivory ticks.
    x, y, w, h = CELLS['ribbon']
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=RUST)
    for yy in range(y + 2, y + h, 6):
        d.rectangle([x + 6, yy, x + 9, yy + 2], fill=IVORY)
    # Face: blank grey-green, flat charcoal hair cap on the top rows.
    x, y, w, h = CELLS['face']
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=SKIN)
    d.rectangle([x, y, x + w - 1, y + 7], fill=CHAR)
    d.rectangle([x + 6, y + 18, x + 25, y + 19], fill=(80, 90, 84))  # faint brow shadow only
    # Suit: charcoal with faint pinstripes.
    x, y, w, h = CELLS['suit']
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=CHAR)
    for xx in range(x + 3, x + w, 8):
        d.line([xx, y, xx, y + h - 1], fill=(56, 58, 62))
    for name, colour in SWATCH.items():
        x, y, w, h = CELLS[name]
        d.rectangle([x, y, x + w - 1, y + h - 1], fill=colour)


def smear(img):
    """Copy the clean half down and swipe it with blood: same layout, offset by 128 px."""
    top = img.crop((0, 0, 256, 128))
    img.paste(top, (0, 128))
    d = ImageDraw.Draw(img)
    rnd = random.Random(72)
    # Hand swipes: fat diagonal streaks over the wordmarks.
    for _ in range(26):
        x0 = rnd.randint(0, 250); y0 = rnd.randint(128, 250)
        dx = rnd.randint(18, 60); dy = rnd.randint(-10, 10)
        d.line([x0, y0, x0 + dx, y0 + dy], fill=rnd.choice([BLOOD, BLOOD, DARK_BLOOD]), width=rnd.randint(3, 7))
    for _ in range(140):  # spatter
        x0 = rnd.randint(0, 255); y0 = rnd.randint(128, 255); r = rnd.randint(0, 2)
        d.rectangle([x0, y0, x0 + r, y0 + r], fill=rnd.choice([BLOOD, DARK_BLOOD]))
    # Cracked conference pass.
    d.line([20, 128, 60, 170, 48, 192], fill=COAL, width=2)
    # Shirt swatch goes red, skin bruises; keep eye/teeth/bone readable.
    for name, colour in {'shirt': (150, 30, 24), 'skin': (84, 70, 66), 'card': (170, 120, 100)}.items():
        x, y, w, h = CELLS[name]
        d.rectangle([x, y + 128, x + w - 1, y + h - 1 + 128], fill=colour)
    for name in ['eye', 'teeth', 'bone', 'blood', 'dark_blood', 'coal']:
        x, y, w, h = CELLS[name]
        d.rectangle([x, y + 128, x + w - 1, y + h - 1 + 128], fill=SWATCH[name])


def main():
    img = Image.new('RGB', (256, 256), COAL)
    clean(img)
    smear(img)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT)
    (ROOT / 'tools/blender/ceo_beast_atlas.json').write_text(json.dumps({'size': 256, 'cells': CELLS}, indent=1) + '\n')
    img.resize((768, 768), Image.NEAREST).save(ROOT / 'assets/previews/beast_ceo_atlas_x3.png')
    print('wrote', OUT)


if __name__ == '__main__':
    main()

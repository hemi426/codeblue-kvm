#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""
Draws the callout labels on the VBUS sense photo for hardware/VBUS-SENSE.md:

    hardware/images/vbus-sense-clean.jpg   the unlabelled photo (input)
    hardware/images/vbus-sense.jpg         the labelled copy the doc shows

Label positions are given in the pixels of the original 2576-pixel-wide photo
and scaled to whatever width the clean copy has. For a new photo, adjust
LABELS (box corner, then the point the leader line touches).

Needs Pillow (pip install pillow). From the repo root:

    python tools/label_vbus_photo.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'hardware' / 'images' / 'vbus-sense-clean.jpg'
DST = ROOT / 'hardware' / 'images' / 'vbus-sense.jpg'
REF_WIDTH = 2576        # width the coordinates below were measured on
OUT_WIDTH = 1200

ACCENT = (21, 101, 192)     # CodeBlue blue, #1565C0
WHITE = (255, 255, 255)
SUBTEXT = (200, 215, 235)
BOX = (10, 14, 22, 215)     # near-black, slightly see-through

# title, subtitle, box top-left, point the leader line touches
LABELS = [
    ('Sense pad', 'USB-OTG, USB side', (1640, 720), (1515, 806)),
    ('100 kΩ', 'sense pad → GPIO21', (430, 1640), (1450, 1780)),
    ('150 kΩ', 'GPIO21 → GND', (1700, 1380), (1935, 2230)),
    ('GPIO21', None, (430, 1930), (1516, 2250)),
    ('GND', None, (2120, 1820), (2345, 2235)),
]


def font(name, size):
    for path in (f'/usr/share/fonts/truetype/dejavu/{name}.ttf', f'{name}.ttf',
                 'C:/Windows/Fonts/segoeuib.ttf' if 'Bold' in name else 'C:/Windows/Fonts/segoeui.ttf'):
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            pass
    return ImageFont.load_default()


def main():
    im = Image.open(SRC).convert('RGBA')
    k = im.width / REF_WIDTH
    s = lambda v: round(v * k)
    big, small = font('DejaVuSans-Bold', s(58)), font('DejaVuSans', s(44))
    over = Image.new('RGBA', im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    pad, gap = s(26), s(18)

    for title, sub, (x, y), (tx, ty) in LABELS:
        x, y, tx, ty = s(x), s(y), s(tx), s(ty)
        tb = d.textbbox((0, 0), title, font=big)
        w, h = tb[2] - tb[0], tb[3] - tb[1]
        if sub:
            sb = d.textbbox((0, 0), sub, font=small)
            w = max(w, sb[2] - sb[0])
            h += sb[3] - sb[1] + gap
        box = (x, y, x + w + 2 * pad, y + h + 2 * pad)
        # leader line from the box edge nearest the target, then a dot on it
        cx, cy = min(max(tx, box[0]), box[2]), min(max(ty, box[1]), box[3])
        d.line((cx, cy, tx, ty), fill=WHITE + (255,), width=max(2, s(7)))
        r = s(16)
        d.ellipse((tx - r, ty - r, tx + r, ty + r), fill=ACCENT + (255,),
                  outline=WHITE + (255,), width=max(1, s(5)))
        d.rounded_rectangle(box, radius=s(22), fill=BOX, outline=ACCENT + (255,), width=max(2, s(6)))
        d.text((x + pad, y + pad - tb[1]), title, font=big, fill=WHITE + (255,))
        if sub:
            d.text((x + pad, y + pad + (tb[3] - tb[1]) + gap - sb[1]), sub, font=small, fill=SUBTEXT + (255,))

    out = Image.alpha_composite(im, over).convert('RGB')
    out = out.resize((OUT_WIDTH, round(out.height * OUT_WIDTH / out.width)), Image.LANCZOS)
    out.save(DST, quality=88, optimize=True)    # writes no camera metadata
    print(f'{DST.relative_to(ROOT)}: {out.width} x {out.height}')


if __name__ == '__main__':
    main()

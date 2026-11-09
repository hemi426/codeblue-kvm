#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""
Draws the CodeBlue logo, a heartbeat on a blue circle, and writes every copy
the project uses, so they all come from this one drawing:

    viewer/CodeBlue.Viewer/CodeBlue.ico   the viewer's icon (16 to 256 px)
    assets/codeblue.svg                   for the docs site and README
    assets/codeblue-512.png               for places that want a bitmap

Needs Pillow (pip install pillow). From the repo root:

    python tools/make_icon.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

BLUE = "#1565C0"  # "code blue"; also the docs site's accent colour

# The heartbeat, as points on a unit square: x across, y as a fraction of
# the spike height below (+) or above (-) the centre line.
TRACE = [(0.10, 0.10), (0.33, 0.10), (0.45, -0.95), (0.58, 0.95), (0.68, 0.10), (0.90, 0.10)]
SPIKE = 0.30   # spike height, as a fraction of the icon
MARGIN = 0.03  # transparent border around the circle


def stroke_for(size: int) -> float:
    """Line width as a fraction of the icon: heavier at small sizes, so it stays legible."""
    return 0.075 if size >= 64 else 0.09 if size >= 32 else 0.105


def hex_rgb(colour: str) -> tuple[int, int, int]:
    return tuple(int(colour[i:i + 2], 16) for i in (1, 3, 5))


def render(size: int) -> Image.Image:
    ss = 16  # draw large, then scale down, for clean anti-aliasing
    w = size * ss
    im = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    m = w * MARGIN
    d.ellipse([m, m, w - m, w - m], fill=hex_rgb(BLUE) + (255,))

    pts = [(x * w, w / 2 + y * SPIKE * w) for x, y in TRACE]
    width = stroke_for(size) * w
    d.line(pts, fill=(255, 255, 255, 255), width=int(width), joint="curve")
    r = width / 2
    for x, y in (pts[0], pts[-1]):  # round ends
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, 255))
    return im.resize((size, size), Image.LANCZOS)


def svg() -> str:
    n = 256
    r = n / 2 - n * MARGIN
    pts = " ".join(f"{x * n:.1f},{n / 2 + y * SPIKE * n:.1f}" for x, y in TRACE)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {n} {n}" width="{n}" height="{n}">\n'
        f'  <title>CodeBlue</title>\n'
        f'  <circle cx="{n / 2}" cy="{n / 2}" r="{r:.1f}" fill="{BLUE}"/>\n'
        f'  <polyline points="{pts}" fill="none" stroke="#fff" stroke-width="{stroke_for(n) * n:.1f}"\n'
        f'            stroke-linecap="round" stroke-linejoin="round"/>\n'
        f'</svg>\n'
    )


def main():
    root = Path(__file__).resolve().parent.parent
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]

    ico = root / "viewer" / "CodeBlue.Viewer" / "CodeBlue.ico"
    images = [render(s) for s in sizes]
    images[-1].save(ico, sizes=[(s, s) for s in sizes], append_images=images[:-1])

    assets = root / "assets"
    assets.mkdir(exist_ok=True)
    (assets / "codeblue.svg").write_text(svg(), encoding="utf-8", newline="\n")
    render(512).save(assets / "codeblue-512.png")

    for path in (ico, assets / "codeblue.svg", assets / "codeblue-512.png"):
        print(f"wrote {path.relative_to(root)}")


if __name__ == "__main__":
    main()

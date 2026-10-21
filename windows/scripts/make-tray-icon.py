#!/usr/bin/env python3
"""Generate windows/src/Akoya.App/Assets/akoya.ico: Akoya's dot logo (columns of 2-4-6-4-2 dots,
centred vertically) in white on transparent, at 16, 20, 24 and 32 px (100/125/150/200 % tray sizes).

Each size is drawn on its own at 8x and box-downsampled, with the dot size and spacing tuned per size so the
dots stay distinct at 16 px. Requires Pillow. Run from the repo root:

    python3 windows/scripts/make-tray-icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

COLUMNS = (2, 4, 6, 4, 2)
SUPERSAMPLE = 8
# size -> (dot diameter, horizontal pitch, vertical pitch), px at the final size.
GEOMETRY = {
    16: (2.0, 3.2, 2.7),
    20: (2.7, 3.9, 3.3),
    24: (3.2, 4.6, 3.95),
    32: (4.2, 6.1, 5.2),
}
OUT = Path(__file__).resolve().parent.parent / "src" / "Akoya.App" / "Assets" / "akoya.ico"


def render(size: int) -> Image.Image:
    dot, pitch_x, pitch_y = GEOMETRY[size]
    s = SUPERSAMPLE
    big = Image.new("L", (size * s, size * s), 0)
    draw = ImageDraw.Draw(big)
    width = dot + pitch_x * (len(COLUMNS) - 1)
    tallest = max(COLUMNS)
    height = dot + pitch_y * (tallest - 1)
    left = (size - width) / 2
    top = (size - height) / 2
    for column, count in enumerate(COLUMNS):
        x = left + column * pitch_x
        first_row = (tallest - count) / 2
        for row in range(count):
            y = top + (first_row + row) * pitch_y
            draw.ellipse([x * s, y * s, (x + dot) * s - 1, (y + dot) * s - 1], fill=255)
    alpha = big.resize((size, size), Image.Resampling.BOX)
    icon = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    icon.putalpha(alpha)
    return icon


def main() -> None:
    images = [render(size) for size in sorted(GEOMETRY)]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    largest = images[-1]
    largest.save(OUT, format="ICO", sizes=[im.size for im in images], append_images=images[:-1])
    print(f"wrote {OUT} ({', '.join(f'{im.width}px' for im in images)})")


if __name__ == "__main__":
    main()

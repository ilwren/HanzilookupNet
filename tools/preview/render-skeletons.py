#!/usr/bin/env python3
"""Renders the sub-stroke "skeleton" of a few characters straight from ``data/mmah.json``.

This is the same reconstruction the ``HanziStrokePreview`` control draws: every packed sub-stroke
becomes a line of length ``length / 255 * sqrt(2) / 2`` centred on ``(packed >> 4) / 15`` /
``(packed & 15) / 15`` and rotated by ``direction * pi / 128`` with ``dx = cos(a), dy = -sin(a)``.
The result is a quick visual sanity check of the data layout and of that inverse operation - a
recognizable 一, 十, 人, 學 ... means the port reads the packed table the right way round.

Requires Pillow:  pip install pillow   (or: pip install --target /tmp/pylibs pillow)

Usage:
    python3 tools/preview/render-skeletons.py [characters...] [--out docs/skeleton-reconstruction.png]
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import pathlib
import sys

DEFAULT_CHARACTERS = ["一", "十", "人", "大", "中", "水", "明", "學", "好", "你", "書", "鱻"]
CELL, PAD, HEADER, COLUMNS = 180, 12, 26, 6

RED, GREEN, BLUE = "#C62828", "#2E7D32", "#1565C0"


def decode_table(data: dict) -> bytes:
    base64_text = data["substrokes"]
    return base64.b64decode(base64_text + "=" * (-len(base64_text) % 4))


def segments_of(table: bytes, row: list) -> list[tuple[float, float, float, float]]:
    """Returns the (x1, y1, x2, y2) lines of a repository row, in the 0..1 normalized space."""
    offset, count = row[3], row[2]
    lines = []
    for i in range(count):
        direction, length, packed = table[offset + i * 3 : offset + i * 3 + 3]
        if packed == 0:  # 0 means the sub-stroke has no centre, so it cannot be drawn
            continue
        angle = direction * math.pi / 128.0
        centre_x, centre_y = (packed >> 4) / 15.0, (packed & 15) / 15.0
        half = length / 255.0 * math.sqrt(2.0) / 2.0
        dx, dy = math.cos(angle) * half, -math.sin(angle) * half
        lines.append((centre_x - dx, centre_y - dy, centre_x + dx, centre_y + dy))
    return lines


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("characters", nargs="*", default=DEFAULT_CHARACTERS)
    parser.add_argument("--data", default="data/mmah.json")
    parser.add_argument("--out", default="docs/skeleton-reconstruction.png")
    args = parser.parse_args()

    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("Pillow is required: pip install pillow", file=sys.stderr)
        return 1

    with open(args.data, encoding="utf-8") as handle:
        data = json.load(handle)
    table = decode_table(data)
    rows = {row[0]: row for row in data["chars"]}

    names = args.characters or DEFAULT_CHARACTERS
    row_count = (len(names) + COLUMNS - 1) // COLUMNS
    width = COLUMNS * (CELL + PAD) + PAD
    height = row_count * (CELL + PAD + HEADER) + PAD
    image = Image.new("RGB", (width, height), "#F6F7F9")
    draw = ImageDraw.Draw(image)

    for index, name in enumerate(names):
        if name not in rows:
            print(f"{name!r} is not in the data file, skipped", file=sys.stderr)
            continue
        row = rows[name]
        left = PAD + (index % COLUMNS) * (CELL + PAD)
        top = PAD + (index // COLUMNS) * (CELL + PAD + HEADER)

        draw.rounded_rectangle([left, top, left + CELL, top + CELL], 10, fill="white", outline="#DCE0E6")
        for fraction in (0.25, 0.5, 0.75):
            draw.line([left + CELL * fraction, top, left + CELL * fraction, top + CELL], fill="#EDF0F4")
            draw.line([left, top + CELL * fraction, left + CELL, top + CELL * fraction], fill="#EDF0F4")
        draw.line([left, top, left + CELL, top + CELL], fill="#EDF0F4")
        draw.line([left + CELL, top, left, top + CELL], fill="#EDF0F4")

        lines = segments_of(table, row)
        for x1, y1, x2, y2 in lines:
            px1, py1 = left + CELL * x1, top + CELL * y1
            px2, py2 = left + CELL * x2, top + CELL * y2
            draw.line([px1, py1, px2, py2], fill=BLUE, width=7)
            draw.ellipse([px1 - 5, py1 - 5, px1 + 5, py1 + 5], fill=RED)  # where the pen went down
            draw.ellipse([px2 - 5, py2 - 5, px2 + 5, py2 + 5], fill=GREEN)  # where it was lifted
        draw.text(
            (left, top + CELL + 6),
            f"{name}  {row[1]} strokes / {len(lines)} sub-strokes",
            fill="#5A6572",
        )

    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out)
    print(f"wrote {out} ({image.width}x{image.height})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

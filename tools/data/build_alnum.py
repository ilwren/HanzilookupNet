#!/usr/bin/env python3
"""Builds `data/alnum.json` - a character repository for digits, Latin letters and punctuation.

The output has exactly the same shape as `data/mmah.json` (a `chars` array of
`[character, strokeCount, subStrokeCount, byteOffset]` plus a base64 `substrokes` table of three
bytes per sub-stroke), so `HanziData.Load`, `Matcher` and `StrokeSkeleton` need to know nothing about
it; it can simply be concatenated with the Chinese repository (see `HanziData.Concat`).

Usage:
    python3 build_alnum.py [--output ../data/alnum.json]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import hanzi_encode as he
import stroke_preprocess as sp
from alnum_templates import TEMPLATES, templates_in_256

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

SOURCE = (
    "Authored stroke templates (tools/data/alnum_templates.py); no public stroke-order corpus "
    "exists for Latin letters or digits, unlike Han characters."
)

# Templates are authored sparsely (2-9 points per stroke) but are re-sampled to this spacing before
# being encoded.  It is not cosmetic: the analyzer cuts a stroke into sub-strokes by comparing the
# path length through three consecutive samples with the straight distance between the outer two, so
# a very sparsely sampled curve turns into far more sub-strokes than the same curve captured from a
# pen.  Measured over 216 simulated handwritings of all 72 glyphs, encoding at 10 units instead of
# "as authored" raises top-1 from 21% to 47% and top-5 from 32% to 78%.
TEMPLATE_SPACING = 10


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=os.path.join(REPO_ROOT, "data", "alnum.json"))
    args = parser.parse_args()

    characters = [(character, [sp.resample(stroke, TEMPLATE_SPACING) for stroke in templates_in_256(character)])
                  for character in TEMPLATES]
    document = he.encode_characters(characters, he.CENTER_MODE_ANALYZER)
    document["name"] = "alnum"
    document["source"] = SOURCE

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))

    problems = []
    sub_strokes = sum(row[2] for row in document["chars"])
    print(f"{args.output}: {len(document['chars'])} characters, {sub_strokes} sub-strokes, "
          f"{os.path.getsize(args.output)} bytes")

    # A couple of invariants that would silently ruin recognition if they broke.
    for character, strokes in characters:
        stroke_count, analyzed = he.analyze(strokes, he.CENTER_MODE_ANALYZER)
        if stroke_count != len(strokes):
            problems.append(f"{character!r}: {len(strokes)} template strokes but the analyzer kept "
                            f"{stroke_count} (a stroke collapsed as degenerate)")
        for _, length, center_x, center_y in analyzed:
            if length == 0:
                problems.append(f"{character!r}: a sub-stroke quantized to length 0, which the matcher "
                                f"scores as NaN - usually a stroke whose ends coincide")
            if center_x == 0 and center_y == 0:
                problems.append(f"{character!r}: a sub-stroke centred on (0, 0), which the packed format "
                                f"reserves for 'no centre'")
        print(f"  {character!r}: {stroke_count} strokes -> {len(analyzed)} sub-strokes")

    if problems:
        print("\nFAILED - the repository would contain characters that cannot be matched:")
        for problem in problems:
            print(f"  {problem}")
        raise SystemExit(1)


if __name__ == "__main__":
    main()
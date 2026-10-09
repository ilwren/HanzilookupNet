#!/usr/bin/env python3
"""Picks the default values of `StrokePreprocessingOptions`, measuring both corpora at once.

The two repositories pull in different directions, so a setting is only worth shipping if it is good
for both:

* `mmah.json` (9507 Chinese characters) is the corpus the parameters have to serve first - it is what
  the library exists for;
* `alnum.json` (72 digits / Latin letters / punctuation) is a much smaller candidate set with much
  coarser geometry, and reacts differently to the re-sampling step.

Each preset is scored as top-1 accuracy on simulated handwriting: median strokes written with a
varying sample speed, tremor, a small rotation and a small scale error, then run through the preset.

Usage:
    python3 tune.py [--cjk-sample 100] [--cjk-stride 10] [--tries 3]
"""

from __future__ import annotations

import argparse
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import hanzi_encode as he
import hanzi_match as hm
import stroke_preprocess as sp
from alnum_templates import TEMPLATES, templates_in_256
from check_alnum import write_like_a_hand
from evaluate import MEDIANS, REPO_ROOT

PRESETS = {
    "rdp2.5+rs3+sm5":     {"min_distance": 1.0, "epsilon": 2.5, "spacing": 3.0, "smooth_window": 5},
    "rdp2.5+rs3+sm9":     {"min_distance": 1.0, "epsilon": 2.5, "spacing": 3.0, "smooth_window": 9},
    "rdp2.5+rs3+sm13":    {"min_distance": 1.0, "epsilon": 2.5, "spacing": 3.0, "smooth_window": 13},
    "rdp3+rs3+sm13":      {"min_distance": 1.0, "epsilon": 3.0, "spacing": 3.0, "smooth_window": 13},
    "rdp2.5+rs4+sm13":    {"min_distance": 1.0, "epsilon": 2.5, "spacing": 4.0, "smooth_window": 13},
    "rdp3+rs4+sm9":       {"min_distance": 1.0, "epsilon": 3.0, "spacing": 4.0, "smooth_window": 9},
    "rdp4+rs4+sm9":       {"min_distance": 1.0, "epsilon": 4.0, "spacing": 4.0, "smooth_window": 9},
    "rs3+sm9+rdp2.5":     {"min_distance": 1.0, "spacing": 3.0, "smooth_window": 9, "epsilon": 2.5, "smooth_after": True},
    "rs3+sm13+rdp2.5":    {"min_distance": 1.0, "spacing": 3.0, "smooth_window": 13, "epsilon": 2.5, "smooth_after": True},
    "rs4+sm13+rdp3":      {"min_distance": 1.0, "spacing": 4.0, "smooth_window": 13, "epsilon": 3.0, "smooth_after": True},
    "rs4+sm9+rdp2.5":     {"min_distance": 1.0, "spacing": 4.0, "smooth_window": 9, "epsilon": 2.5, "smooth_after": True},
    "rs4+sm17+rdp3":      {"min_distance": 1.0, "spacing": 4.0, "smooth_window": 17, "epsilon": 3.0, "smooth_after": True},
}


def simulate_cjk(stroke, rng):
    """Median strokes come with their own point spacing; sample them the way a mouse would."""
    dense = sp.resample(stroke, 1.0)
    points = []
    walked = 0.0
    step = speed * rng.uniform(0.5, 1.8)
    angle = math.radians(rng.uniform(-3.5, 3.5))
    scale = rng.uniform(0.93, 1.07)
    cos_a, sin_a = math.cos(angle), math.sin(angle)
    for index, (x, y) in enumerate(dense):
        if index and walked >= step:
            walked = 0.0
            step = rng.uniform(0.6, 1.6) * speed * rng.uniform(0.5, 1.8)
        sx, sy = x * scale, y * scale
        points.append((sx * cos_a - sy * sin_a + rng.gauss(0, jitter),
                       sx * sin_a + sy * cos_a + rng.gauss(0, jitter)))
        walked += 1.0
    return points


def measure(matcher, written, preset, tries, alnum=False):
    """Top-1 accuracy of `preset`; the same simulated handwritings are reused for every preset."""
    drawings = []
    for character, strokes in written:
        for attempt in range(tries):
            rng = random.Random(f"tune:{character}:{attempt}")
            if alnum:
                drawings.append((character, write_like_a_hand(strokes, rng, jitter, speed)))
            else:
                drawings.append((character, [simulate_cjk(stroke, rng) for stroke in strokes]))

    top1 = 0
    for character, drawn in drawings:
        prepared = [sp.preprocess(stroke, **preset) for stroke in drawn]
        stroke_count, analyzed = he.analyze(prepared, he.CENTER_MODE_ANALYZER)
        names = [item[0] for item in matcher.match_analyzed(analyzed, stroke_count, limit=1)]
        top1 += int(bool(names) and names[0] == character)
    return top1, len(drawings)


def main() -> None:
    global jitter, speed
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cjk-sample", type=int, default=100)
    parser.add_argument("--cjk-stride", type=int, default=10)
    parser.add_argument("--tries", type=int, default=3)
    parser.add_argument("--jitter", type=float, default=1.6)
    parser.add_argument("--speed", type=float, default=4.0)
    args = parser.parse_args()
    jitter, speed = args.jitter, args.speed

    with open(MEDIANS, encoding="utf-8") as handle:
        medians = he.find_medians_in_js(handle.read())
    if args.cjk_sample:
        medians = medians[::max(1, len(medians) // args.cjk_sample)][:args.cjk_sample]

    cjk_repository = hm.Repository.load(os.path.join(REPO_ROOT, "data", "mmah.json"))
    if args.cjk_stride > 1:
        keep = [(row, cjk_repository.table[row[3]:row[3] + row[2] * 3])
                for index, row in enumerate(cjk_repository.characters) if index % args.cjk_stride == 0]
        characters, table, offset = [], bytearray(), 0
        for row, blob in keep:
            characters.append((row[0], row[1], row[2], offset))
            table += blob
            offset += len(blob)
        cjk_repository = hm.Repository(characters, bytes(table))

    alnum_repository = hm.Repository.load(os.path.join(REPO_ROOT, "data", "alnum.json"))

    # One simulation per character, shared by every preset: the comparison is apples to apples.
    cjk_written = [(character, strokes) for character, strokes in medians]
    alnum_written = [(character, templates_in_256(character)) for character in TEMPLATES]

    print(f"Chinese: {len(cjk_written)} characters from a {len(cjk_repository.characters)} character "
          f"repository; alnum: {len(alnum_written)} from {len(alnum_repository.characters)}")
    print(f"jitter {args.jitter}, speed {args.speed}, {args.tries} tries each\n")
    print(f"{'preset':22s} {'汉字 top-1':>12s} {'alnum top-1':>12s} {'mean':>8s}")

    rows = []
    for name, preset in PRESETS.items():
        cjk_matcher = hm.Matcher(cjk_repository)
        alnum_matcher = hm.Matcher(alnum_repository)
        cjk_top1, cjk_n = measure(cjk_matcher, cjk_written, preset, args.tries)
        alnum_top1, alnum_n = measure(alnum_matcher, alnum_written, preset, args.tries, alnum=True)
        cjk_ratio = cjk_top1 / cjk_n
        alnum_ratio = alnum_top1 / alnum_n
        rows.append((name, cjk_ratio, alnum_ratio))
        print(f"{name:22s} {cjk_ratio:11.1%} {alnum_ratio:11.1%} {(cjk_ratio + alnum_ratio) / 2:7.1%}",
              flush=True)

    best = max(rows, key=lambda row: row[1])
    best_mean = max(rows, key=lambda row: (row[1] + row[2]) / 2)
    print(f"\nbest for Chinese : {best[0]} ({best[1]:.1%})")
    print(f"best on average  : {best_mean[0]} ({(best_mean[1] + best_mean[2]) / 2:.1%})")


if __name__ == "__main__":
    main()
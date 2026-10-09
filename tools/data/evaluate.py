#!/usr/bin/env python3
"""Measures recognition accuracy on *simulated handwriting*.

The shipped character data is median strokes - clean, evenly sampled polylines.  Feeding those back
into the matcher is a self-fulfilling test, so this script instead writes each character the way a
person (or a mouse) would: re-sampled at a varying speed, with hand tremor, a slight rotation and a
slight scale change.  It then reports top-1 / top-5 accuracy with and without the preprocessing
pipeline of `stroke_preprocess.py`, which is the number that justifies that code.

Usage:
    python3 evaluate.py [--sample 400] [--seed 7] [--jitter 1.6] [--speed 4.0]
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

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MEDIANS = os.environ.get("MMAH_MEDIANS", "/tmp/mj/HanziLookupJS-master/library/data/x-mmah-medians.js")


def _smooth(values: list[float], window: int) -> list[float]:
    """Centred moving average over `window` samples, shrinking the window at the two ends."""
    half = window // 2
    result = []
    for i in range(len(values)):
        lo = max(0, i - half)
        hi = min(len(values), i + half + 1)
        chunk = values[lo:hi]
        result.append(sum(chunk) / len(chunk))
    return result


def tremor(count: int, sigma: float, correlation: float, rng) -> tuple[list[float], list[float]]:
    """Two correlated noise series with the given standard deviation.

    Real tremor is *smooth*: a hand drifts over roughly a pen-width and wanders slowly.  White noise
    per sample would instead produce a polyline whose direction changes by tens of degrees between
    neighbouring points, which no cleanup stage can honestly be expected to remove - and tuning
    against that model would pick settings that only make sense for the model.  So the noise is
    generated white and low-passed over `correlation` samples, then rescaled to `sigma`.
    """
    import statistics

    window = max(1, int(round(correlation)))
    series = []
    for _ in range(2):
        raw = [rng.gauss(0.0, 1.0) for _ in range(count)]
        smoothed = _smooth(raw, window)
        deviation = statistics.pstdev(smoothed) or 1.0
        series.append([value / deviation * sigma for value in smoothed])
    return series[0], series[1]


def simulate(stroke, rng, jitter: float, speed: float, correlation: float = 12.0):
    """Re-samples one stroke at a varying speed and adds tremor, rotation and scale error."""
    # Random per-stroke speed, so points are unevenly spaced like a real fast/slow drag.
    local_speed = speed * rng.uniform(0.5, 1.8)
    dense = sp.resample(stroke, 1.0)
    if len(dense) < 2:
        return [tuple(p) for p in stroke]

    angle = math.radians(rng.uniform(-3.5, 3.5))
    scale = rng.uniform(0.93, 1.07)
    cos_a, sin_a = math.cos(angle), math.sin(angle)
    drift_x, drift_y = tremor(len(dense), jitter, correlation, rng)
    result = []
    walked = 0.0
    step = rng.uniform(0.6, 1.6) * local_speed
    for i, point in enumerate(dense):
        if i and walked >= step:
            walked = 0.0
            step = rng.uniform(0.6, 1.6) * local_speed
        x = point[0] * scale + drift_x[i]
        y = point[1] * scale + drift_y[i]
        result.append((x * cos_a - y * sin_a, x * sin_a + y * cos_a))
        walked += 1.0
    return result


def sub_stroke_count(strokes, **pre):
    prepared = [sp.preprocess(stroke, **pre) for stroke in strokes] if any(pre.values()) else strokes
    return he.analyze(prepared, he.CENTER_MODE_ANALYZER)[1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sample", type=int, default=400, help="characters to test (0 = all)")
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--jitter", type=float, default=1.6, help="tremor sigma, in 256ths")
    parser.add_argument("--speed", type=float, default=4.0, help="mean sample spacing")
    parser.add_argument("--data", default=os.path.join(REPO_ROOT, "data", "mmah.json"))
    args = parser.parse_args()

    with open(MEDIANS, encoding="utf-8") as handle:
        medians = he.find_medians_in_js(handle.read())
    if args.sample:
        medians = medians[::max(1, len(medians) // args.sample)][:args.sample]

    matcher = hm.Matcher(hm.Repository.load(args.data))

    presets = {
        "raw": {},
        "preprocessed": {"min_distance": 1.0, "smooth_window": 5, "epsilon": 6.0, "spacing": 6.0},
    }

    stats = {name: {"top1": 0, "top5": 0, "sub_in": 0, "sub_data": 0, "n": 0} for name in presets}
    for character, strokes in medians:
        rng = random.Random(f"{args.seed}:{character}")
        drawn = [simulate(stroke, rng, args.jitter, args.speed) for stroke in strokes]
        for name, pre in presets.items():
            prepared = [sp.preprocess(stroke, **pre) for stroke in drawn]
            stroke_count, analyzed = he.analyze(prepared, he.CENTER_MODE_ANALYZER)
            results = matcher.match_analyzed(analyzed, stroke_count, limit=5)
            names = [item[0] for item in results]
            stats[name]["n"] += 1
            stats[name]["top1"] += int(bool(names) and names[0] == character)
            stats[name]["top5"] += int(character in names)
            stats[name]["sub_in"] += len(analyzed)
            data_row = matcher.repository.index.get(character)
            if data_row:
                stats[name]["sub_data"] += data_row[2]
        if stats["raw"]["n"] % 100 == 0:
            done = stats["raw"]["n"]
            raw = stats["raw"]
            pre = stats["preprocessed"]
            print(f"  {done:4d} tested: raw top1 {raw['top1']/done:5.1%} "
                  f"(sub-strokes {raw['sub_in']/done:5.1f} vs data {raw['sub_data']/done:4.1f}) | "
                  f"preprocessed top1 {pre['top1']/done:5.1%} "
                  f"(sub-strokes {pre['sub_in']/done:5.1f})", flush=True)

    print()
    print(f"simulated handwriting: {len(medians)} characters, jitter {args.jitter}, "
          f"speed {args.speed}, seed {args.seed}")
    for name in presets:
        stat = stats[name]
        n = stat["n"]
        print(f"  {name:14s} top-1 {stat['top1']/n:6.1%}   top-5 {stat['top5']/n:6.1%}   "
              f"sub-strokes written {stat['sub_in']/n:5.2f} (data: {stat['sub_data']/n:4.2f})")


if __name__ == "__main__":
    main()
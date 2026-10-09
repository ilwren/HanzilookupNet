#!/usr/bin/env python3
"""Sweeps the preprocessing parameters and prints top-1 / top-5 accuracy for each preset.

The simulated strokes are generated once per character and then run through every preset, so the
comparison is apples to apples (same tremor, same speed, same seed) and the sweep is affordable.

Usage:  python3 sweep_preprocess.py [--sample 200] [--jitter 1.6]
"""

from __future__ import annotations

import argparse
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import hanzi_encode as he
import hanzi_match as hm
import stroke_preprocess as sp
from evaluate import MEDIANS, REPO_ROOT, simulate

PRESETS = {
    # "raw" is intentionally omitted: it explodes to >100 sub-strokes and is ~10x slower to score.
    "dedupe only":        {"min_distance": 1.0},
    "smooth 3":           {"min_distance": 1.0, "smooth_window": 3},
    "rdp 1.5":            {"min_distance": 1.0, "epsilon": 1.5},
    "rdp 2.0":            {"min_distance": 1.0, "epsilon": 2.0},
    "rdp 2.5":            {"min_distance": 1.0, "epsilon": 2.5},
    "rdp 3.0":            {"min_distance": 1.0, "epsilon": 3.0},
    "rdp 4.0":            {"min_distance": 1.0, "epsilon": 4.0},
    "resample 3":         {"min_distance": 1.0, "spacing": 3.0},
    "resample 4":         {"min_distance": 1.0, "spacing": 4.0},
    "resample 6":         {"min_distance": 1.0, "spacing": 6.0},
    "rdp2 + rs4":         {"min_distance": 1.0, "epsilon": 2.0, "spacing": 4.0},
    "rdp2.5 + rs4":       {"min_distance": 1.0, "epsilon": 2.5, "spacing": 4.0},
    "rdp3 + rs4":         {"min_distance": 1.0, "epsilon": 3.0, "spacing": 4.0},
    "rdp2 + rs6":         {"min_distance": 1.0, "epsilon": 2.0, "spacing": 6.0},
    "rdp3 + rs6":         {"min_distance": 1.0, "epsilon": 3.0, "spacing": 6.0},
    "rdp3 + rs8":         {"min_distance": 1.0, "epsilon": 3.0, "spacing": 8.0},
    "smooth + rdp2 + rs4": {"min_distance": 1.0, "smooth_window": 3, "epsilon": 2.0, "spacing": 4.0},
    "smooth + rdp3 + rs6": {"min_distance": 1.0, "smooth_window": 3, "epsilon": 3.0, "spacing": 6.0},
}


PREPROCESS_PRESETS = {
    "rdp2.5+rs3.5": {"min_distance": 1.0, "epsilon": 2.5, "spacing": 3.5},
    "rdp3+rs3.5":   {"min_distance": 1.0, "epsilon": 3.0, "spacing": 3.5},
    "rdp3+rs4":     {"min_distance": 1.0, "epsilon": 3.0, "spacing": 4.0},
    "rdp3.5+rs4":   {"min_distance": 1.0, "epsilon": 3.5, "spacing": 4.0},
    "rdp4+rs4":     {"min_distance": 1.0, "epsilon": 4.0, "spacing": 4.0},
    "rdp3+rs4.5":   {"min_distance": 1.0, "epsilon": 3.0, "spacing": 4.5},
    "rdp3.5+rs4.5": {"min_distance": 1.0, "epsilon": 3.5, "spacing": 4.5},
    "rdp2.5+rs4":   {"min_distance": 1.0, "epsilon": 2.5, "spacing": 4.0},
    "rdp3+rs3":     {"min_distance": 1.0, "epsilon": 3.0, "spacing": 3.0},
    "rdp2.5+rs2.5": {"min_distance": 1.0, "epsilon": 2.5, "spacing": 2.5},
    "rdp3+rs2.5":   {"min_distance": 1.0, "epsilon": 3.0, "spacing": 2.5},
    "rdp2.5+rs3":   {"min_distance": 1.0, "epsilon": 2.5, "spacing": 3.0},
    "rdp3.5+rs3":   {"min_distance": 1.0, "epsilon": 3.5, "spacing": 3.0},
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sample", type=int, default=200)
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--jitter", type=float, default=1.6)
    parser.add_argument("--speed", type=float, default=4.0)
    parser.add_argument("--data", default=os.path.join(REPO_ROOT, "data", "mmah.json"))
    parser.add_argument("--presets", default="", help="comma separated subset of preset names")
    parser.add_argument("--repo-stride", type=int, default=1,
                        help="use every Nth repository character (targets are always kept); "
                             "the pure Python matcher is too slow for a full sweep otherwise")
    args = parser.parse_args()

    with open(MEDIANS, encoding="utf-8") as handle:
        medians = he.find_medians_in_js(handle.read())
    if args.sample:
        medians = medians[::max(1, len(medians) // args.sample)][:args.sample]

    # Simulate once, reuse for every preset.
    simulated = []
    for character, strokes in medians:
        rng = random.Random(f"{args.seed}:{character}")
        simulated.append((character, [simulate(s, rng, args.jitter, args.speed) for s in strokes]))

    presets = dict(PRESETS)
    presets.update(PREPROCESS_PRESETS)
    if args.presets:
        presets = {}
        for name in (n.strip() for n in args.presets.split(",")):
            if not name:
                continue
            if name in dict(PRESETS, **PREPROCESS_PRESETS):
                presets[name] = dict(PRESETS, **PREPROCESS_PRESETS)[name]
            else:
                # Also accept a spelling, so a sweep does not have to edit this file first:
                #   eps2.5+rs3+sm5  ->  {"min_distance": 1.0, "epsilon": 2.5, "spacing": 3.0, ...}
                preset = {"min_distance": 1.0}
                for part in name.split("+"):
                    for key, prefix in (("epsilon", "eps"), ("spacing", "rs"), ("smooth_window", "sm")):
                        if part.startswith(prefix) and part[len(prefix):].replace(".", "").isdigit():
                            preset[key] = int(part[len(prefix):]) if key == "smooth_window" \
                                else float(part[len(prefix):])
                if len(preset) == 1:
                    parser.error(f"unknown preset: {name}")
                presets[name] = preset

    full = hm.Repository.load(args.data)
    if args.repo_stride > 1:
        targets = {character for character, _ in simulated}
        keep = [(row, full.table[row[3]:row[3] + row[2] * 3])
                for index, row in enumerate(full.characters)
                if index % args.repo_stride == 0 or row[0] in targets]
        characters, table, offset = [], bytearray(), 0
        for row, blob in keep:
            characters.append((row[0], row[1], row[2], offset))
            table += blob
            offset += len(blob)
        full = hm.Repository(characters, bytes(table))
    print(f"repository: {len(full.characters)} of {len(hm.Repository.load(args.data).characters)} characters",
          flush=True)
    matcher = hm.Matcher(full)
    stats = {name: [0, 0, 0] for name in presets}

    started = time.time()
    seen = [0]
    for character, drawn in simulated:
        seen[0] += 1
        for name, pre in presets.items():
            prepared = [sp.preprocess(stroke, **pre) for stroke in drawn]
            stroke_count, analyzed = he.analyze(prepared, he.CENTER_MODE_ANALYZER)
            names = [item[0] for item in matcher.match_analyzed(analyzed, stroke_count, limit=5)]
            stats[name][0] += int(bool(names) and names[0] == character)
            stats[name][1] += int(character in names)
            stats[name][2] += len(analyzed)
        if seen[0] % 25 == 0:
            print(f"  ... {seen[0]:4d} characters, {time.time()-started:5.1f}s", flush=True)

    total = len(simulated)
    print(f"\n{total} characters, jitter {args.jitter}, speed {args.speed}, seed {args.seed}\n")
    print(f"{'preset':24s} {'top-1':>8s} {'top-5':>8s} {'sub-strokes':>12s}")
    for name in presets:
        top1, top5, subs = stats[name]
        print(f"{name:24s} {top1/total:7.1%} {top5/total:8.1%} {subs/total:12.2f}")


if __name__ == "__main__":
    main()
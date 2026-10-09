#!/usr/bin/env python3
"""Stroke analysis + data packing, a faithful mirror of the C# recognizer.

This module exists so that new character repositories can be produced outside the .NET build:
`hanzilookup-js` ships median stroke polylines (one list of points per stroke, in a 256x256 square
with y growing downwards), and `mmah.json` is exactly those medians run through the analysis of
`src/HanziLookup/AnalyzedCharacter.cs` and packed three bytes per sub-stroke.

The functions here are line by line transliterations of that C# code, including its JavaScript
quirks (`Math.round` is "round half towards +Infinity", the pivot detector's magic constants, the
bounding square normalization).  `tools/data/verify_encoder.py` checks the transliteration against
the shipped `data/mmah.json`, byte for byte, for all 9507 characters - so anything encoded here is
encoded the same way the original library encodes.

Usage:
    python3 hanzi_encode.py medians.js data.json [--name NAME] [--source SOURCE]
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import re
from typing import Iterable, Sequence

# --- constants mirrored from src/HanziLookup/AnalyzedCharacter.cs -------------------
MIN_SEGMENT_LENGTH = 12.5
MAX_LOCAL_LENGTH_RATIO = 1.1
MAX_RUNNING_LENGTH_RATIO = 1.09

# Coordinate space of the median data (the "classic" HanziLookup square).
COORDINATE_SPACE = 256.0

# Which centre formula to use.  The runtime analyzer and the offline generator disagree (see
# `_converter_center`), so this is explicit rather than guessed.
CENTER_MODE_ANALYZER = "analyzer"    # src/HanziLookup/AnalyzedCharacter.cs - for input strokes
CENTER_MODE_CONVERTER = "converter"  # mmah-convert/Analyzer.cs - for the bytes inside a data file


def js_round(value: float) -> float:
    """JavaScript `Math.round`: half rounds towards +Infinity (so -0.5 -> -0, -1.5 -> -1)."""
    return math.floor(value + 0.5)


def _distance(a: Sequence[float], b: Sequence[float]) -> float:
    return math.hypot(a[0] - b[0], a[1] - b[1])


def bounding_rect(strokes: Sequence[Sequence[Sequence[float]]]) -> tuple[float, float, float, float]:
    """(left, top, right, bottom) over every point of every stroke."""
    xs = [p[0] for stroke in strokes for p in stroke]
    ys = [p[1] for stroke in strokes for p in stroke]
    if not xs:
        return (0.0, 0.0, 0.0, 0.0)
    return (min(xs), min(ys), max(xs), max(ys))


def _is_degenerate(stroke: Sequence[Sequence[float]]) -> bool:
    """True when a stroke cannot yield sub-strokes (fewer than 2 points, or no extent)."""
    if len(stroke) < 2:
        return True
    xs = [p[0] for p in stroke]
    ys = [p[1] for p in stroke]
    return min(xs) == max(xs) and min(ys) == max(ys)


def get_pivot_indexes(points: Sequence[Sequence[float]]) -> list[int]:
    """Indexes of the points that start a sub-stroke (mirror of AnalyzedCharacter.GetPivotIndexes)."""
    count = len(points)
    markers = [False] * count
    prev_pt_ix = 0
    first_pt_ix = 0
    pivot_pt_ix = 1
    markers[0] = True
    local_length = _distance(points[first_pt_ix], points[pivot_pt_ix])
    running_length = local_length

    for i in range(2, count):
        next_point = points[i]
        pivot_length = _distance(points[pivot_pt_ix], next_point)
        local_length += pivot_length
        running_length += pivot_length
        dist_from_previous = _distance(points[prev_pt_ix], next_point)
        dist_from_first = _distance(points[first_pt_ix], next_point)
        if (local_length > MAX_LOCAL_LENGTH_RATIO * dist_from_previous
                or running_length > MAX_RUNNING_LENGTH_RATIO * dist_from_first):
            if markers[prev_pt_ix] and _distance(points[prev_pt_ix], points[pivot_pt_ix]) < MIN_SEGMENT_LENGTH:
                markers[prev_pt_ix] = False
            markers[pivot_pt_ix] = True
            running_length = pivot_length
            first_pt_ix = pivot_pt_ix

        local_length = pivot_length
        prev_pt_ix = pivot_pt_ix
        pivot_pt_ix = i

    markers[pivot_pt_ix] = True
    if (markers[prev_pt_ix] and prev_pt_ix != 0
            and _distance(points[prev_pt_ix], points[pivot_pt_ix]) < MIN_SEGMENT_LENGTH):
        markers[prev_pt_ix] = False

    return [i for i, marked in enumerate(markers) if marked]


def _norm_dist(a: Sequence[float], b: Sequence[float], rect: tuple[float, float, float, float]) -> float:
    """Length of a segment normalized by the diagonal of the bounding square, capped at 1."""
    left, top, right, bottom = rect
    width = right - left
    height = bottom - top
    dimension_squared = width * width if width > height else height * height
    normalizer = math.sqrt(dimension_squared + dimension_squared)
    if normalizer == 0:
        return float("nan")
    return min(_distance(a, b) / normalizer, 1.0)


def _get_direction(a: Sequence[float], b: Sequence[float]) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    return math.pi - math.atan2(dy, dx)


def _norm_center(a: Sequence[float], b: Sequence[float], rect: tuple[float, float, float, float]):
    """Mid point of a segment, normalized by the side of the bounding square."""
    left, top, right, bottom = rect
    x = (a[0] + b[0]) / 2
    y = (a[1] + b[1]) / 2
    width = right - left
    height = bottom - top
    if width > height:
        side = width
        x = x - left
        y = y - top + (side - height) / 2
    else:
        side = height
        x = x - left + (side - width) / 2
        y = y - top
    return (x / side, y / side)


def _normalizer(rect: tuple[float, float, float, float]) -> float:
    """The diagonal of a square whose side is the larger dimension of the bounding box."""
    width = rect[2] - rect[0]
    height = rect[3] - rect[1]
    dimension_squared = width * width if width > height else height * height
    return math.sqrt(dimension_squared + dimension_squared)


def _converter_center(x: float, y: float, normalizer: float) -> tuple[float, float]:
    """The centre formula of the *offline data generator* (`mmah-convert/Analyzer.cs`).

    This is deliberately different from the runtime analyzer, and the difference matters.  The
    generator wrote:

        centerX = normDist(new Point { X = 0, Y = ptCenter.Y }, ptCenter);
        centerY = normDist(new Point { X = ptCenter.X, Y = 0 }, ptCenter);

    i.e. the centre is the segment's **absolute** position in the 256x256 canvas divided by the
    diagonal of the character's bounding square, capped at 1 - no subtraction of the bounding
    rectangle's origin, and no padding to a square.  `data/mmah.json` was produced that way, so a
    repository written with the analyzer's formula would be scored against data it was never
    comparable with.  `verify_encoder.py` proves this one reproduces the shipped file byte for byte.
    """
    if normalizer == 0:
        return (float("nan"), float("nan"))
    return (min(x / normalizer, 1.0), min(y / normalizer, 1.0))


def build_sub_strokes(
    stroke: Sequence[Sequence[float]],
    rect: tuple[float, float, float, float],
    center_mode: str = CENTER_MODE_ANALYZER,
):
    """Decomposes one stroke into quantized sub-strokes: (direction, length, centerX, centerY)."""
    pivot_indexes = get_pivot_indexes(stroke)
    result = []
    prev_ix = 0
    for ix in pivot_indexes:
        if ix == prev_ix:
            continue
        direction = _get_direction(stroke[prev_ix], stroke[ix])
        direction = js_round(direction * 256.0 / math.pi / 2.0)
        if direction == 256:
            direction = 0
        norm_length = js_round(_norm_dist(stroke[prev_ix], stroke[ix], rect) * 255)
        if center_mode == CENTER_MODE_CONVERTER:
            mid_x = (stroke[prev_ix][0] + stroke[ix][0]) / 2
            mid_y = (stroke[prev_ix][1] + stroke[ix][1]) / 2
            center = _converter_center(mid_x, mid_y, _normalizer(rect))
        else:
            center = _norm_center(stroke[prev_ix], stroke[ix], rect)
        result.append((
            int(js_round(direction)),
            int(js_round(norm_length)),
            int(js_round(center[0] * 15)),
            int(js_round(center[1] * 15)),
        ))
        prev_ix = ix
    return result


def analyze(strokes: Sequence[Sequence[Sequence[float]]], center_mode: str = CENTER_MODE_ANALYZER):
    """Analyzes raw strokes.  Returns (stroke_count, [sub-stroke, ...]) for one character."""
    rect = bounding_rect(strokes)
    all_sub_strokes = []
    stroke_count = 0
    for stroke in strokes:
        if _is_degenerate(stroke):
            continue
        stroke_count += 1
        all_sub_strokes.extend(build_sub_strokes(stroke, rect, center_mode))
    return stroke_count, all_sub_strokes


def pack(sub_strokes: Iterable[Sequence[int]]) -> bytes:
    """Packs sub-strokes into the 3-bytes-per-sub-stroke table of the data file."""
    out = bytearray()
    for direction, length, center_x, center_y in sub_strokes:
        out.append(direction & 0xFF)
        out.append(length & 0xFF)
        out.append(((center_x & 0x0F) << 4) | (center_y & 0x0F))
    return bytes(out)


def encode_characters(
    characters: Sequence[tuple[str, Sequence[Sequence[Sequence[float]]]]],
    center_mode: str = CENTER_MODE_CONVERTER,
) -> dict:
    """Encodes `[(character, [stroke, ...]), ...]` into the repository JSON structure.

    Defaults to the generator's centre formula, because the result of this function is the content
    of a data file, which is compared against analyzer output at match time.
    """
    rows = []
    table = bytearray()
    for character, strokes in characters:
        stroke_count, sub_strokes = analyze(strokes, center_mode)
        rows.append([character, stroke_count, len(sub_strokes), len(table)])
        table += pack(sub_strokes)
    return {"chars": rows, "substrokes": base64.b64encode(bytes(table)).decode("ascii")}


def parse_medians_js(text: str) -> list[tuple[str, list[list[list[float]]]]]:
    """Parses the `HanziLookup.MediansMMAH = [...]` array of the upstream data file.

    The file is JavaScript, but the payload is JSON once the assignment is stripped, so no parser
    beyond `json.loads` is needed.
    """
    start = text.index("[")
    end = text.rindex("]") + 1
    return [(entry[0], entry[1]) for entry in json.loads(text[start:end])]


def find_medians_in_js(text: str) -> list[tuple[str, list[list[list[float]]]]]:
    """Same as `parse_medians_js` but tolerant of several variable arrays in one file."""
    match = re.search(r"HanziLookup\.\w+\s*=\s*\[", text)
    if match is None:
        return parse_medians_js(text)
    start = match.end() - 1
    depth = 0
    for index in range(start, len(text)):
        if text[index] == "[":
            depth += 1
        elif text[index] == "]":
            depth -= 1
            if depth == 0:
                return _load_medians(text[start:index + 1])
    raise ValueError("unbalanced array in the medians file")


def _load_medians(array_text: str) -> list[tuple[str, list[list[list[float]]]]]:
    """`json.loads` of the array, tolerating the trailing comma the upstream file ends with."""
    entries = json.loads(re.sub(r",\s*\]$", "]", array_text.rstrip()))
    return [(entry[0], entry[1]) for entry in entries]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("medians", help="medians .js file (HanziLookup.MediansMMAH = [...])")
    parser.add_argument("output", help="repository JSON file to write")
    parser.add_argument("--name", default=None, help="the variable name to look for in the medians file")
    parser.add_argument("--source", default=None, help="provenance string stored next to the data")
    args = parser.parse_args()

    text = open(args.medians, encoding="utf-8").read()
    characters = find_medians_in_js(text)
    document = encode_characters(characters)

    if args.source:
        document["source"] = args.source
        document["name"] = args.name or "custom"

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))

    sub_stroke_total = sum(row[2] for row in document["chars"])
    print(f"{args.output}: {len(document['chars'])} characters, "
          f"{sub_stroke_total} sub-strokes, {len(document['substrokes'])} base64 chars")


if __name__ == "__main__":
    main()
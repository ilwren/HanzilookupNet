#!/usr/bin/env python3
"""Stroke preprocessing: the reference for `src/HanziLookup/StrokePreprocessor.cs`.

The shipped character data is *median* strokes: smooth, evenly sampled polylines.  A pointing
device produces neither.  Two things differ, and both feed straight into the pivot detector of the
analyzer (`localLength > 1.1 * distFromPrevious`), which is what decides where a stroke is cut into
sub-strokes:

* hand tremor and uneven sampling speed make consecutive points jitter, so the length ratios trip
  constantly and one straight stroke explodes into a handful of sub-strokes;
* a mouse that moves fast reports few, far apart points, so each segment's direction is noisy.

Cleaning the input before it is analysed is therefore not a cosmetic step - it is what makes the
matcher see the same kind of geometry it was calibrated on.  The C# port implements exactly this
pipeline; this module is the executable specification, and `evaluate.py` measures what it buys.
"""

from __future__ import annotations

import math
from typing import Sequence

Point = tuple[float, float]


def dedupe(points: Sequence[Point], min_distance: float) -> list[Point]:
    """Drops points that are (almost) on top of their predecessor."""
    if not points:
        return []
    result = [tuple(points[0])]
    for point in points[1:]:
        last = result[-1]
        if math.hypot(point[0] - last[0], point[1] - last[1]) >= min_distance:
            result.append(tuple(point))
    return result


def moving_average(points: Sequence[Point], window: int) -> list[Point]:
    """Centred moving average; `window` is the number of points averaged (odd values only)."""
    if window < 3 or len(points) < window:
        return [tuple(point) for point in points]
    window += 1 - window % 2  # make it odd
    half = window // 2
    result = [tuple(point) for point in points[:half]]
    for i in range(half, len(points) - half):
        chunk = points[i - half:i + half + 1]
        result.append((sum(p[0] for p in chunk) / window, sum(p[1] for p in chunk) / window))
    result.extend(tuple(point) for point in points[len(points) - half:])
    return result


def _rdp_one(points: Sequence[Point], first: int, last: int, epsilon: float, keep: list[bool]) -> None:
    """Recursive Ramer-Douglas-Peucker between two indexes, marking the points to keep."""
    if last <= first + 1:
        return
    start = points[first]
    end = points[last]
    dx = end[0] - start[0]
    dy = end[1] - start[1]
    norm = math.hypot(dx, dy)
    worst_index = -1
    worst = 0.0
    for i in range(first + 1, last):
        point = points[i]
        if norm == 0.0:
            distance = math.hypot(point[0] - start[0], point[1] - start[1])
        else:
            distance = abs(dy * point[0] - dx * point[1] + end[0] * start[1] - end[1] * start[0]) / norm
        if distance > worst:
            worst = distance
            worst_index = i
    if worst > epsilon and worst_index > 0:
        keep[worst_index] = True
        _rdp_one(points, first, worst_index, epsilon, keep)
        _rdp_one(points, worst_index, last, epsilon, keep)


def simplify(points: Sequence[Point], epsilon: float) -> list[Point]:
    """Ramer-Douglas-Peucker: keeps the corners, throws away the tremor between them."""
    points = [tuple(point) for point in points]
    if len(points) < 3 or epsilon <= 0:
        return points
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    # Iterative (explicit stack) version: deep recursion on long strokes is a stack risk.
    stack = [(0, len(points) - 1)]
    while stack:
        first, last = stack.pop()
        if last <= first + 1:
            continue
        start, end = points[first], points[last]
        dx = end[0] - start[0]
        dy = end[1] - start[1]
        norm = math.hypot(dx, dy)
        worst_index, worst = -1, 0.0
        for i in range(first + 1, last):
            point = points[i]
            if norm == 0.0:
                distance = math.hypot(point[0] - start[0], point[1] - start[1])
            else:
                distance = abs(dy * point[0] - dx * point[1] + end[0] * start[1] - end[1] * start[0]) / norm
            if distance > worst:
                worst, worst_index = distance, i
        if worst > epsilon and worst_index > 0:
            keep[worst_index] = True
            stack.append((first, worst_index))
            stack.append((worst_index, last))
    return [point for point, wanted in zip(points, keep) if wanted]


def resample(points: Sequence[Point], spacing: float) -> list[Point]:
    """Re-spaces the polyline to about `spacing`, **keeping every vertex**.

    This is what removes the "my mouse moved faster there" part of the input's geometry: with even
    spacing along a straight run, the analyzer sees `localLength == distFromPrevious` and produces no
    spurious pivots.

    The vertices matter just as much.  The analyzer decides that a stroke turns by comparing the path
    length through three *consecutive samples* with the straight distance between the outer two - so a
    corner is only detected when one of the three samples sits on it.  A resampler that marches a
    fixed grid along the path can straddle a corner (last sample just before it, next one just after),
    which flattens the corner completely: an "L" then analyses as a single diagonal.  Filling each
    segment separately keeps every corner and still spaces each straight run evenly.
    """
    if len(points) < 2 or spacing <= 0:
        return [tuple(point) for point in points]

    result: list[Point] = [tuple(points[0])]
    for index in range(len(points) - 1):
        a = points[index]
        b = points[index + 1]
        segment = math.hypot(b[0] - a[0], b[1] - a[1])
        steps = max(1, int(round(segment / spacing)))
        for step in range(1, steps + 1):
            ratio = step / steps
            result.append((a[0] + (b[0] - a[0]) * ratio, a[1] + (b[1] - a[1]) * ratio))

    return result


def preprocess(stroke: Sequence[Point],
               min_distance: float = 0.0,
               smooth_window: int = 0,
               epsilon: float = 0.0,
               spacing: float = 0.0) -> list[Point]:
    """The full pipeline, in the order the C# implementation applies it."""
    points = [tuple(point) for point in stroke]
    if min_distance > 0:
        points = dedupe(points, min_distance)
    if smooth_window >= 3:
        points = moving_average(points, smooth_window)
    if epsilon > 0:
        points = simplify(points, epsilon)
    if spacing > 0:
        points = resample(points, spacing)
    return points
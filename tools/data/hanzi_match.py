#!/usr/bin/env python3
"""The matcher, mirrored from `src/HanziLookup/Matcher.cs`.

Why a Python copy?  The character data in `data/` is generated and quality-checked outside the .NET
build (there is no local dotnet SDK in the environment this data is authored in), and answering
"does this hand written `4` match the template `4` best?" needs a matcher.  This module is the
reference implementation of the scoring rules; `verify_encoder.py` and `check_alnum.py` use it.

`self_test()` checks it against the scores the C# port produces (which are themselves pinned by
`tests/HanziLookup.Tests/TestData/reference-vectors.json`), so a drift between this mirror and the
shipped C# shows up as a failure rather than as quietly wrong data.
"""

from __future__ import annotations

import base64
import json
import math
import os
from typing import Sequence

from hanzi_encode import CENTER_MODE_ANALYZER, analyze, bounding_rect, build_sub_strokes

DEFAULT_LOOSENESS = 0.15
MAX_CHARACTER_STROKE_COUNT = 48
MAX_CHARACTER_SUB_STROKE_COUNT = 64
SKIP_PENALTY_MULTIPLIER = 1.75
CORRECT_NUMBER_OF_STROKES_BONUS = 0.1
CORRECT_NUMBER_OF_STROKES_CAP = 10
SKIP_PENALTY_BASE = -0.33
NEGATIVE_INFINITY = float("-inf")


def js_round(value: float) -> int:
    """JavaScript `Math.round` (half away from -Infinity, i.e. half up)."""
    return math.floor(value + 0.5)


class CubicCurve2D:
    """Port of `src/HanziLookup/CubicCurve2D.cs`."""

    def __init__(self, x1, y1, cx1, cy1, cx2, cy2, x2, y2):
        self.x1, self.y1, self.cx1, self.cy1, self.cx2, self.cy2, self.x2, self.y2 = (
            x1, y1, cx1, cy1, cx2, cy2, x2, y2)

    def _bx(self):
        return 3.0 * (self.cx2 - self.cx1) - self._cx()

    def _by(self):
        return 3.0 * (self.cy2 - self.cy1) - self._cy()

    def _cx(self):
        return 3.0 * (self.cx1 - self.x1)

    def _cy(self):
        return 3.0 * (self.cy1 - self.y1)

    def _ax(self):
        return self.x2 - self.x1 - self._bx() - self._cx()

    def _ay(self):
        return self.y2 - self.y1 - self._by() - self._cy()

    def y_on_curve(self, t):
        t2 = t * t
        return self._ay() * t * t2 + self._by() * t2 + self._cy() * t + self.y1

    def solve_for_x(self, x):
        a = self._ax()
        b = self._bx()
        c = self._cx()
        d = self.x1 - x
        f = (3.0 * c / a - b * b / (a * a)) / 3.0
        g = (2.0 * b * b * b / (a * a * a) - 9.0 * b * c / (a * a) + 27.0 * d / a) / 27.0
        h = g * g / 4.0 + f * f * f / 27.0

        if h > 0:
            u = 0.0 - g
            r = u / 2 + math.pow(h, 0.5)
            s8 = math.pow(r, 0.333333333333333333333333333)
            t8 = u / 2 - math.pow(h, 0.5)
            v8 = math.pow(0.0 - t8, 0.33333333333333333333)
            return [s8 - v8 - b / (3 * a)]

        if f == 0.0 and g == 0.0 and h == 0.0:
            return [-math.pow(d / a, 1.0 / 3.0)]

        i = math.sqrt(g * g / 4.0 - h)
        j = math.pow(i, 1.0 / 3.0)
        k = math.acos(-g / (2 * i))
        m = math.cos(k / 3.0)
        n = math.sqrt(3.0) * math.sin(k / 3.0)
        p = b / (3.0 * a) * -1
        return [
            2.0 * j * math.cos(k / 3.0) - b / (3.0 * a),
            -j * (m + n) + p,
            -j * (m - n) + p,
        ]

    def first_solution_for_x(self, x):
        for d in self.solve_for_x(x):
            if -1e-8 <= d <= 1.00000001:
                if 0.0 <= d <= 1.0:
                    return d
                return 0.0 if d < 0.0 else 1.0
        return float("nan")


def _init_cubic_curve_score_table(curve: CubicCurve2D, num_samples: int):
    x = curve.x1
    x_inc = (curve.x2 - curve.x1) / num_samples
    table = [0.0] * num_samples
    for i in range(num_samples):
        t = curve.first_solution_for_x(min(x, curve.x2))
        table[i] = curve.y_on_curve(t)
        x += x_inc
    return table


class Repository:
    """A character repository, i.e. what `HanziData` is in C#."""

    def __init__(self, characters, table: bytes):
        self.characters = characters          # [(char, stroke_count, sub_stroke_count, offset)]
        self.table = table
        self.index = {row[0]: row for row in characters}
        self._decoded = None

    @staticmethod
    def load(path: str) -> "Repository":
        with open(path, encoding="utf-8") as handle:
            document = json.load(handle)
        table = base64.b64decode(document["substrokes"])
        return Repository([tuple(row) for row in document["chars"]], table)

    @staticmethod
    def concat(*repositories: "Repository") -> "Repository":
        """Merges repositories into one, exactly as `HanziData.Concat` does on the C# side."""
        characters: list[tuple] = []
        table = bytearray()
        seen = set()
        for repository in repositories:
            for character, stroke_count, sub_stroke_count, offset in repository.characters:
                if character in seen:
                    continue
                seen.add(character)
                characters.append((character, stroke_count, sub_stroke_count, len(table)))
                table += repository.table[offset:offset + sub_stroke_count * 3]
        return Repository(characters, bytes(table))

    def decoded(self):
        """Every character's sub-strokes, decoded once (the C# matcher reads the same bytes)."""
        if self._decoded is None:
            decoded = []
            for _, _, count, offset in self.characters:
                strokes = []
                for i in range(count):
                    base = offset + i * 3
                    direction = self.table[base]
                    length = self.table[base + 1]
                    packed = self.table[base + 2]
                    strokes.append((direction, length,
                                    (packed & 0xF0) >> 4 if packed else 0,
                                    packed & 0x0F if packed else 0,
                                    packed > 0))
                decoded.append(strokes)
            self._decoded = decoded
        return self._decoded

    def sub_strokes(self, character: str):
        _, _, count, offset = self.index[character]
        out = []
        for i in range(count):
            base = offset + i * 3
            direction = self.table[base]
            length = self.table[base + 1]
            packed = self.table[base + 2]
            has_center = packed > 0
            out.append((direction, length,
                        (packed & 0xF0) >> 4 if has_center else 0,
                        packed & 0x0F if has_center else 0,
                        has_center))
        return out


class Matcher:
    """Port of `src/HanziLookup/Matcher.cs` (default options)."""

    def __init__(self, repository: Repository, looseness: float = DEFAULT_LOOSENESS):
        self.repository = repository
        self._matrix = None
        self.looseness = DEFAULT_LOOSENESS if looseness == 0.0 else looseness
        self._direction_table = _init_cubic_curve_score_table(
            CubicCurve2D(0, 1.0, 0.5, 1.0, 0.25, -2, 1.0, 1.0), 256)
        self._length_table = _init_cubic_curve_score_table(
            CubicCurve2D(0, 0, 0.25, 1.0, 0.75, 1.0, 1.0, 1.0), 129)
        self._position_table = [1.0 - math.sqrt(i) / 22 for i in range(451)]

    # --- looseness curves -------------------------------------------------------
    def _strokes_range(self, stroke_count: int) -> int:
        if self.looseness == 0:
            return 0
        if self.looseness == 1:
            return MAX_CHARACTER_STROKE_COUNT
        curve = CubicCurve2D(0, 0, 0.35, stroke_count * 0.4, 0.6, stroke_count,
                            1, MAX_CHARACTER_STROKE_COUNT)
        return js_round(curve.y_on_curve(curve.first_solution_for_x(self.looseness)))

    def _sub_strokes_range(self, sub_stroke_count: int) -> int:
        if self.looseness == 1.0:
            return MAX_CHARACTER_SUB_STROKE_COUNT
        y0 = sub_stroke_count * 0.25
        ctrl1_y = 1.5 * y0
        curve = CubicCurve2D(0, y0, 0.4, ctrl1_y, 0.75, 1.5 * ctrl1_y,
                            1, MAX_CHARACTER_SUB_STROKE_COUNT)
        return js_round(curve.y_on_curve(curve.first_solution_for_x(self.looseness)))

    # --- scoring ----------------------------------------------------------------
    def _direction_score(self, direction1: int, direction2: int, input_length: int) -> float:
        theta = min(abs(direction1 - direction2), len(self._direction_table) - 1)
        score = self._direction_table[theta]
        if input_length < 64:
            score += min(1.0, 1.0 - score) * (1 - input_length / 64.0)
        return score

    def _length_score(self, length1: int, length2: int) -> float:
        if length1 > length2:
            if length1 == 0:
                return float("nan")
            ratio = js_round((length2 << 7) / float(length1))
        else:
            if length2 == 0:
                return float("nan")
            ratio = js_round((length1 << 7) / float(length2))
        if ratio != ratio or ratio < 0 or ratio >= len(self._length_table):
            return float("nan")
        return self._length_table[ratio]

    def _sub_stroke_score(self, input_ss, compare_ss) -> float:
        score = self._length_score(input_ss[1], compare_ss[1]) * \
            self._direction_score(input_ss[0], compare_ss[0], input_ss[1])
        if compare_ss[4]:
            dx = input_ss[2] - compare_ss[2]
            dy = input_ss[3] - compare_ss[3]
            squared = min(dx * dx + dy * dy, len(self._position_table) - 1)
            closeness = self._position_table[int(squared)]
            score = score * closeness if score > 0 else score / closeness
        return score

    def match_analyzed(self, input_sub_strokes, input_stroke_count: int, limit: int = 8):
        """`input_sub_strokes` is the flattened list of analyzer-mode sub-strokes."""
        if not input_sub_strokes or input_stroke_count == 0:
            return []
        sub_stroke_count = len(input_sub_strokes)
        stroke_range = self._strokes_range(input_stroke_count)
        min_strokes = max(input_stroke_count - stroke_range, 1)
        max_strokes = min(input_stroke_count + stroke_range, MAX_CHARACTER_STROKE_COUNT)
        sub_strokes_range = self._sub_strokes_range(sub_stroke_count)

        # The C# score matrix is square, starts at 65 x 65 (MAX_CHARACTER_SUB_STROKE_COUNT + 1) and
        # grows only when the *input* has more sub-strokes than that; it is reused between candidates.
        dimension = max(MAX_CHARACTER_SUB_STROKE_COUNT + 1, sub_stroke_count + 1)
        if self._matrix is None or len(self._matrix) < dimension:
            self._matrix = [[0.0] * dimension for _ in range(dimension)]
        matrix = self._matrix
        for i in range(dimension):
            penalty = SKIP_PENALTY_BASE * SKIP_PENALTY_MULTIPLIER * i
            matrix[0][i] = penalty
            matrix[i][0] = penalty

        decoded = self.repository.decoded()
        results = []
        for index, (character, cmp_strokes, cmp_sub_strokes, _) in enumerate(self.repository.characters):
            if cmp_strokes < min_strokes or cmp_strokes > max_strokes:
                continue
            compare = decoded[index]

            for x in range(sub_stroke_count):
                input_ss = input_sub_strokes[x]
                row = matrix[x]
                next_row = matrix[x + 1]
                input_penalty = input_ss[1] / 256.0 * SKIP_PENALTY_MULTIPLIER
                for y in range(cmp_sub_strokes):
                    if abs(x - y) <= sub_strokes_range:
                        compare_ss = compare[y]
                        skip1 = row[y + 1] - input_penalty
                        skip2 = next_row[y] - compare_ss[1] / 256.0 * SKIP_PENALTY_MULTIPLIER
                        skip_score = skip1 if skip1 > skip2 else skip2
                        previous = row[y] + self._sub_stroke_score(input_ss, compare_ss)
                        next_row[y + 1] = previous if previous > skip_score else skip_score
                    else:
                        next_row[y + 1] = NEGATIVE_INFINITY

            score = matrix[sub_stroke_count][cmp_sub_strokes]
            if input_stroke_count == cmp_strokes and input_stroke_count < CORRECT_NUMBER_OF_STROKES_CAP:
                bonus = (CORRECT_NUMBER_OF_STROKES_BONUS
                         * max(CORRECT_NUMBER_OF_STROKES_CAP - input_stroke_count, 0)
                         / CORRECT_NUMBER_OF_STROKES_CAP)
                score += bonus * score
            results.append((character, score))


        results.sort(key=lambda item: -item[1])
        best: list = []
        seen = set()
        for character, score in results:
            if character in seen:
                continue
            seen.add(character)
            best.append((character, score))
            if len(best) >= limit:
                break
        return best

    def match_strokes(self, strokes, limit: int = 8):
        stroke_count, sub_strokes = analyze(strokes, CENTER_MODE_ANALYZER)
        return self.match_analyzed(sub_strokes, stroke_count, limit)


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def self_test() -> None:
    """Checks the mirror against scores the C# port is pinned to."""
    repository = Repository.load(os.path.join(REPO_ROOT, "data", "mmah.json"))
    matcher = Matcher(repository)

    horizontal = [[[32.0, 128.0], [224.0, 128.0]]]
    results = matcher.match_strokes(horizontal, limit=8)
    assert results[0][0] == "一", results[:3]
    assert abs(results[0][1] - 1.0199310536683939) < 1e-12, results[0]

    finite = [item for item in results if math.isfinite(item[1])]
    assert len(finite) == 3, finite

    print("self test OK: 一 =", results[0][1], "| 8 results,", len(finite), "finite")


if __name__ == "__main__":
    self_test()
# Reference vector generator

`generate-reference-vectors.mjs` runs the **original JavaScript implementation**
(`hanzilookup-js@1.0.3`, installed from npm) over a fixed set of inputs and writes
`tests/HanziLookup.Tests/TestData/reference-vectors.json`. The xunit test
`ReferenceVectorTests` then replays those vectors against the C# port, which is how "the port behaves like
the original" is verified instead of just asserted.

The vectors contain:

* **33 recognition cases**: synthetic strokes (straight lines, a right angle, a dense jittered line, a box
  drawn with four strokes, different looseness values, `limit = 1`), the real median strokes of
  一 人 大 中 水 明 你 好 學 書 and a sparse variant of each, and the empty input. Every case stores the
  raw strokes, the complete analysis (bounding rectangle, pivot indexes, sub-strokes), the expected matches
  with full precision scores, the diagnostic counters, and whether the JavaScript callback was invoked.
* the three **score tables** of the matcher (direction 256, length 129, position 451 entries),
* samples of the **cubic curve solver** (`solveForX`, `getFirstSolutionForX`) for the four curves the
  algorithm uses,
* the SHA-256 of the decoded sub-stroke table, so a swapped data file is detected.

JSON cannot express infinities, so scores are written as `inf`, `-inf` and `nan` strings.

## Usage

```bash
cd tools/reference
npm install        # installs exactly hanzilookup-js@1.0.3 (see package-lock.json)

# fewest cases (no external files):
node generate-reference-vectors.mjs --data ../../data/mmah.json \
                                    --out ../../tests/HanziLookup.Tests/TestData/reference-vectors.json

# recommended: include the real median strokes
git clone --depth 1 https://github.com/gugray/HanziLookupJS /tmp/HanziLookupJS
node generate-reference-vectors.mjs --data ../../data/mmah.json \
                                    --medians /tmp/HanziLookupJS/library/data/x-mmah-medians.js \
                                    --out ../../tests/HanziLookup.Tests/TestData/reference-vectors.json
```

The median strokes come from HanziLookupJS (`library/data/x-mmah-medians.js`) and are not part of this
repository - only the generated JSON is committed. Both the npm package and the medians file are covered by
the licenses listed in `THIRD-PARTY-NOTICES.md`.

Running the generator bumps the `generatedAt` timestamp and (when npm installs a newer patch release)
could change the expected scores; the tests would fail in that case, which is the point - the port is
pinned to a specific implementation, not to "whatever the npm package does today".

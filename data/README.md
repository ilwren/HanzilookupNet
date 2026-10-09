# Character data

`mmah.json` is the character repository the recognizer matches against:

* **9507 characters**, 156493 sub-strokes, 469479 packed bytes, 827 KB.
* JSON shape:
  ```json
  {
    "chars": [["一", 1, 1, 39], ...],       // character, stroke count, sub-stroke count, byte offset
    "substrokes": "vZg3nSss2+f+..."          // base64: 3 bytes per sub-stroke
  }
  ```
  Per sub-stroke: **direction** (0..255, `round(angle * 256 / PI / 2)`), **length** (0..255, normalized by
  the diagonal of the bounding square) and a **packed centre** (high nibble `x`, low nibble `y`, 0..15
  each; `0` means "not stored", which the shipped data never uses).
* SHA-256 of the decoded sub-stroke table (asserted by the test suite):
  `9d0472cdf61e6c15805fd2f570b6002dc59bc20d904d212407cbfabc44970982`.

The file is taken verbatim from
[gugray/HanziLookupJS `dist/mmah.json`](https://github.com/gugray/HanziLookupJS/blob/master/dist/mmah.json),
which is generated from [Make Me a Hanzi](https://github.com/skishore/makemeahanzi)'s `graphics.txt` (see
the `mmah-convert/` tool in that repository).

## `alnum.json` - digits, Latin letters and punctuation

A second, much smaller repository in exactly the same format: **72 characters** - the digits `0-9`, the
letters `A-Z` and `a-z`, and the ten marks `. , ! ? - + = / ( )` - 272 sub-strokes, 2288 packed bytes. It is not part of `hanzilookup-js`; it is generated here by

```bash
python3 tools/data/build_alnum.py      # writes data/alnum.json
python3 tools/data/check_alnum.py      # writes every glyph back and requires it to be recognized
```

* The stroke shapes live in `tools/data/alnum_templates.py` (unit box, 256 × 256) and are re-sampled to
  even spacing before they are encoded, so the stored geometry matches what the preprocessing pipeline
  produces for a real stroke.
* The **centres are encoded relative to each character's own bounding box**, not to the diagonal of the
  data set, because these glyphs are normalized to the full box anyway (see `StrokePreprocessor`).
* Round glyphs (`0`, `O`, `o`, `Q`, `8`) are authored with a deliberate gap where the pen lifts. A
  perfectly closed loop would have its start and end at the same point, i.e. zero length, and a
  zero-length sub-stroke scores `NaN` - which would make the character permanently unmatchable.
* `check_alnum.py` is the gate: it writes each glyph the way a pointing device would (variable speed,
  tremor, rotation, scale error), runs it through the preprocessing pipeline and requires the matcher to
  hand the character back out of a repository containing all 72. It reaches **95.4% top-1 / 100% top-5**
  over 216 simulated handwritings. CI runs it on every push.
* What it cannot do is tell you the case. The matcher normalizes every character by its own bounding
  box, so `c`/`C`, `x`/`X`, `s`/`S` are literally the same shape and come back as two entries in the
  candidate list. The demo has a case selector for that reason.

Loading both repositories at once is one call - `HanziData.Concat` re-bases the byte offsets, so the
merged repository is a valid repository in its own right:

```csharp
var merged = HanziData.Concat(HanziData.Load("data/mmah.json"), HanziData.Load("data/alnum.json"));
var matcher = new Matcher(merged);
```

The Chinese side keeps its 9507 characters either way; expect `-` and `丨` to compete with `一` and
`I`, since they *are* the same line.

## License

The data is derived from the **Arphic PL KaitiM GB** and **Arphic PL UKai** fonts and is redistributed
under the **Arphic Public License**, reproduced in `LICENSE-APL.txt`. Keep both files together when you
redistribute them.

## Using another data set

* `orig.json` (Kiung's original data, GPL) can be loaded the same way: `HanziData.Load("data/orig.json")`.
  It is *not* included in this repository; download it from the upstream project if you want it. Note that
  the tests which assert specific scores and counters expect `mmah.json`.
* The demo and the test project copy `mmah.json` to their output directory. If the file is missing, the
  demo shows a hint instead of crashing, and the tests fail with a message pointing here.

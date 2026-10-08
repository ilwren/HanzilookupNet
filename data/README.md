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

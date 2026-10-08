# Third-party notices and provenance

This repository contains a C# rewrite ("port") of the JavaScript library that ships as the npm package
`hanzilookup-js`, plus the character data that the original needs to work. None of these files were
written from scratch: the chain of authorship is documented here so that users can honour each license.

## 1. Original HanziLookup / HanziLookupJS

* **Author:** Jordan Kiang (original HanziLookup), Gabor L Ugray (HanziLookupJS).
* **Source:** <https://github.com/gugray/HanziLookupJS> (version `1a57d6a`, "master" as of 2026-10-08).
* **License:** GNU GPL v3 - see `LICENSE` in this repository (reproduced verbatim from the upstream
  project).
* **What was used:** the recognition algorithm (stroke analysis into sub-strokes, the score tables, the
  DP matching), the demo UI concept of `library/src/drawingBoard.js` (256 × 256 canvas, 米字格 grid,
  mouse/touch input, red sub-stroke and blue bounding box overlay), the `library/data/x-mmah-medians.js`
  median strokes used to generate the test vectors, and `LICENSE-APL`, which is reproduced for the data.

## 2. The npm package `hanzilookup-js` 1.0.3

* **Author:** Edward Nguyen (monokaijs).
* **Source:** <https://www.npmjs.com/package/hanzilookup-js> (`dist/hanzilookup.cjs.js`, 677 lines).
* **License:** ISC, as declared by the package itself.
* **What was used:** the concrete structure that was ported line by line - the `AnalyzedCharacter`,
  `AnalyzedStroke`, `SubStroke`, `Matcher`, `MatchCollector`, `CharacterMatch`, `CubicCurve2D`,
  `decodeCompact`, `data` and `init` API, including its numeric conventions and its quirks. The
  `tools/reference/package-lock.json` file pins exactly this version for the reference-vector generator.

Because the original work behind that package is GPL v3, this repository as a whole is licensed under
**GPL v3** as well. If you only want the permissive terms the npm package declares, check the provenance
above with your own legal counsel before relicensing.

## 3. Character data `data/mmah.json`

* **Upstream:** <https://github.com/gugray/HanziLookupJS/blob/master/dist/mmah.json>
  (identical file: 9507 characters, 469479 packed sub-stroke bytes, SHA-256
  `9d0472cdf61e6c15805fd2f570b6002dc59bc20d904d212407cbfabc44970982`).
* **Derived from:** Make Me a Hanzi's `graphics.txt` (<https://github.com/skishore/makemeahanzi>), which is
  ultimately derived from the fonts **Arphic PL KaitiM GB** and **Arphic PL UKai**.
* **License:** the Arphic Public License; the text is reproduced verbatim in `data/LICENSE-APL.txt`
  (upstream: <http://ftp.gnu.org/non-gnu/chinese-fonts-truetype/LICENSE>). Redistribution and modification
  are permitted under the terms of that license - keep the notice and the license text with the data, and
  make the license available to anyone you share it with.

`data/orig.json` (Kiung's original data, GPL) is **not** included in this repository. The port can load it
instead of `mmah.json` if you obtain it from upstream; the code, the Avalonia controls and the test
vectors are independent of which data set is used, and the tests that assert specific scores expect
`mmah.json`.

## 4. NuGet dependencies

| Package | License | Used by |
| --- | --- | --- |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `AvaloniaUI.DiagnosticsSupport` | MIT | `HanziLookup.Avalonia`, `HanziLookup.Demo` |
| `Microsoft.NET.Test.Sdk` | MIT | `HanziLookup.Tests` |
| `xunit`, `xunit.runner.visualstudio` | Apache-2.0 | `HanziLookup.Tests` |

No third-party package is required by `src/HanziLookup` itself.

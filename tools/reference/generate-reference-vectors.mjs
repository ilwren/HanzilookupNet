/**
 * Generates the reference vectors used by the C# test-suite.
 *
 * The vectors are produced by running the *original* JavaScript implementation
 * (`hanzilookup-js@1.0.3`, the npm package this repository is a C# rewrite of)
 * so that the ported C# code can be checked against the reference behaviour
 * bit-for-bit (for the integer parts of the analysis) and within a very small
 * epsilon (for the floating point match scores).
 *
 * Usage:
 *   npm install                     # installs hanzilookup-js@1.0.3
 *   node generate-reference-vectors.mjs \
 *        --data ../../data/mmah.json \
 *        --medians <path to HanziLookupJS/library/data/x-mmah-medians.js> \
 *        --out ../../tests/HanziLookup.Tests/TestData/reference-vectors.json
 *
 * `--medians` is optional: when supplied, real "median" stroke data of a
 * handful of characters is used as input for the recognition cases, otherwise
 * only synthetic strokes are used.
 */

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import vm from 'node:vm';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);

// Magic constants of the JS implementation, repeated here so the generated
// vectors are self-describing.
const MAX_STROKE = 48;
const MAX_SUB_STROKE = 64;
const { AnalyzedCharacter, CubicCurve2D, Matcher, data, decodeCompact } = require('hanzilookup-js');

// ---------------------------------------------------------------------------
// arguments
// ---------------------------------------------------------------------------
function arg(name, fallback = null) {
  const ix = process.argv.indexOf('--' + name);
  return ix >= 0 && ix + 1 < process.argv.length ? process.argv[ix + 1] : fallback;
}

const dataPath = path.resolve(arg('data', '../../data/mmah.json'));
const mediansPath = arg('medians');
const outPath = path.resolve(arg('out', '../../tests/HanziLookup.Tests/TestData/reference-vectors.json'));

// ---------------------------------------------------------------------------
// load the character data exactly like `init()` does in the browser
// ---------------------------------------------------------------------------
data['mmah'] = JSON.parse(fs.readFileSync(dataPath, 'utf8'));
data['mmah'].substrokes = decodeCompact(data['mmah'].substrokes);

const substrokeBytes = data['mmah'].substrokes;
const dataHash = crypto.createHash('sha256').update(Buffer.from(substrokeBytes)).digest('hex');

// ---------------------------------------------------------------------------
// optional: real median strokes per character
// ---------------------------------------------------------------------------
/** @type {Map<string, number[][][]>} */
const medians = new Map();
if (mediansPath && fs.existsSync(mediansPath)) {
  const sandbox = { HanziLookup: {} };
  vm.createContext(sandbox);
  vm.runInContext(fs.readFileSync(mediansPath, 'utf8'), sandbox, { filename: mediansPath });
  for (const [character, strokes] of sandbox.HanziLookup.MediansMMAH) {
    medians.set(character, strokes);
  }
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------
function analysis(rawStrokes) {
  const c = new AnalyzedCharacter(rawStrokes);
  return {
    bounds: [c.left, c.top, c.right, c.bottom],
    subStrokeCount: c.subStrokeCount,
    strokes: c.analyzedStrokes.map(s => ({
      pivotIndexes: s.pivotIndexes,
      subStrokes: s.subStrokes.map(ss => ({
        direction: ss.direction,
        length: ss.length,
        centerX: ss.centerX,
        centerY: ss.centerY
      }))
    }))
  };
}

function encodeScore(score) {
  // JSON has no way of expressing the infinities that the score matrix can
  // legitimately produce (a skipped/out-of-range comparison yields -Infinity).
  if (Number.isNaN(score)) return 'nan';
  if (score === Number.POSITIVE_INFINITY) return 'inf';
  if (score === Number.NEGATIVE_INFINITY) return '-inf';
  return score;
}

function match(rawStrokes, looseness, limit) {
  const char = new AnalyzedCharacter(rawStrokes);
  const matcher = new Matcher('mmah', looseness);
  let collected = null;
  matcher.match(char, limit, m => { collected = m; });
  return {
    // NOTE: hanzilookup-js 1.0.3 never invokes the callback when the input has
    // no strokes at all (it returns the empty result instead), so `collected`
    // stays null in that case. The C# port simply returns an empty list.
    callbackInvoked: collected !== null,
    matches: (collected ?? []).map(m => ({ character: m.character, score: encodeScore(m.score) })),
    counters: matcher.getCounters()
  };
}

function makeCase(name, rawStrokes, looseness, limit, note) {
  const analyzed = analysis(rawStrokes);
  const result = match(rawStrokes, looseness, limit);
  return {
    name,
    note: note ?? null,
    looseness,
    limit,
    rawStrokes,
    analysis: analyzed,
    expected: result.matches,
    callbackInvoked: result.callbackInvoked,
    counters: result.counters
  };
}

// ---------------------------------------------------------------------------
// score tables (public fields of the JS Matcher)
// ---------------------------------------------------------------------------
const referenceMatcher = new Matcher('mmah', 0.15);
const scoreTables = {
  direction: referenceMatcher.DIRECTION_SCORE_TABLE,
  length: referenceMatcher.LENGTH_SCORE_TABLE,
  position: referenceMatcher.POS_SCORE_TABLE
};

// ---------------------------------------------------------------------------
// cubic curve solver samples
// ---------------------------------------------------------------------------
function curveSample(name, args, xs) {
  const curve = new CubicCurve2D(...args);
  return {
    name,
    args,
    samples: xs.map(x => ({
      x,
      solveForX: curve.solveForX(x).map(encodeScore),
      getFirstSolutionForX: encodeScore(curve.getFirstSolutionForX(x))
    }))
  };
}

const sampleXs = [0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.5, 0.75, 0.9, 0.95, 1.0, 1.05, -0.05];
const curves = [
  curveSample('stroke-range curve (4 strokes)', [0, 0, 0.35, 1.6, 0.6, 4, 1, MAX_STROKE], sampleXs),
  curveSample('sub-stroke-range curve (y0 = 1.5)', [0, 1.5, 0.4, 2.25, 0.75, 3.375, 1, MAX_SUB_STROKE], sampleXs),
  curveSample('direction score curve', [0, 1.0, 0.5, 1.0, 0.25, -2, 1.0, 1.0], sampleXs),
  curveSample('length score curve', [0, 0, 0.25, 1.0, 0.75, 1.0, 1.0, 1.0], sampleXs)
];

// ---------------------------------------------------------------------------
// synthetic strokes (exactly reproducible on every machine)
// ---------------------------------------------------------------------------
const cases = [];

cases.push(makeCase('empty-input', [], 0.15, 8, 'No strokes at all.'));
cases.push(makeCase('single-horizontal-stroke',
  [[[32, 128], [224, 128]]], 0.15, 8, 'Two points, one straight stroke.'));
cases.push(makeCase('three-point-vertical-stroke',
  [[[128, 24], [128, 128], [128, 232]]], 0.15, 8, 'Vertical stroke drawn downwards.'));
cases.push(makeCase('four-point-right-angle',
  [[[40, 40], [210, 40], [210, 210]]], 0.15, 8, 'Horizontal then vertical (㇕ shape).'));
cases.push(makeCase('dense-jittered-line',
  (() => {
    const pts = [];
    for (let i = 0; i <= 60; ++i) {
      const t = i / 60;
      pts.push([Math.round(30 + 190 * t), Math.round(40 + 170 * t + 6 * Math.sin(t * 12))]);
    }
    return [pts];
  })(), 0.3, 5, 'Dense sampling with deterministic jitter - stresses the pivot algorithm.'));
cases.push(makeCase('box-four-strokes',
  [
    [[40, 40], [210, 40]],
    [[210, 40], [210, 210]],
    [[210, 210], [40, 210]],
    [[40, 210], [40, 40]]
  ], 0.15, 8, 'A square drawn with four strokes.'));
cases.push(makeCase('strict-looseness',
  [[[48, 48], [200, 60], [120, 200]]], 0.0, 8, 'looseness = 0 is coerced to the default (0.15) by the JS constructor.'));
cases.push(makeCase('permissive-looseness',
  [[[48, 48], [200, 60], [120, 200]]], 1.0, 8, 'Maximum looseness.'));
for (const looseness of [0.05, 0.25, 0.5, 0.75, 0.9]) {
  cases.push(makeCase(`looseness-${looseness}`,
    [[[48, 48], [200, 60], [120, 200]], [[60, 210], [190, 215]]], looseness, 6,
    `Looseness ${looseness}: exercises the cubic curve solver that derives the stroke/sub-stroke ranges.`));
}
cases.push(makeCase('limit-one',
  [[[48, 48], [200, 60], [120, 200]]], 0.15, 1, 'Only the best match is requested.'));

// ---------------------------------------------------------------------------
// real median strokes
// ---------------------------------------------------------------------------
const medianCharacters = ['一', '人', '大', '中', '水', '明', '你', '好', '學', '書'];
for (const character of medianCharacters) {
  const strokes = medians.get(character);
  if (!strokes) continue;
  cases.push(makeCase(`median-${character}`, strokes, 0.15, 8,
    `Median strokes of 「${character}」 taken from x-mmah-medians.js.`));
  // A sparse variant: every other point is dropped, which is what a coarse
  // pointer input looks like.
  const sparse = strokes.map(s => s.filter((_, i) => i % 2 === 0));
  if (sparse.every(s => s.length >= 2)) {
    cases.push(makeCase(`median-${character}-sparse`, sparse, 0.15, 8,
      `Every other point of the median strokes of 「${character}」.`));
  }
}

// ---------------------------------------------------------------------------
// write out
// ---------------------------------------------------------------------------
const output = {
  generatedBy: 'hanzilookup-js@1.0.3 (dist/hanzilookup.cjs.js)',
  generatedWith: `node ${process.version}`,
  generatedAt: new Date().toISOString(),
  scoreTables,
  curves,
  data: {
    file: path.basename(dataPath),
    characterCount: data['mmah'].chars.length,
    substrokeByteCount: substrokeBytes.length,
    substrokeSha256: dataHash
  },
  cases
};

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(output, null, 2) + '\n');
console.log(`wrote ${output.cases.length} cases to ${outPath}`);
console.log(`data: ${output.data.characterCount} characters, ${output.data.substrokeByteCount} bytes, sha256 ${dataHash}`);

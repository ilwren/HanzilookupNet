# HanziLookupJS (ES Module Version)

A library for handwritten Chinese character recognition, compatible with Browsers and React Native (logic layer).
This is a modern ES Module rewrite of the original **HanziLookupJS** by Gabor L Ugray, optimized for use with bundlers like Rollup, Webpack, and Vite.

## Installation

```bash
npm install hanzilookup-js
```

## Features

- **ES Module Support**: Full support for `import` / `export`.
- **Tree-shakable**: Import only what you need.
- **Typed**: Includes TypeScript definitions.
- **React Native Compatible**: Core logic is decoupled from the DOM.
- **Data Decoupled**: Load character data from any source (URL, imports, etc.).

## Usage

### 1. Initialization

You need to load the character data (`mmah.json` or `orig.json`) before performing lookups. You can use the built-in `init` helper for fetching from a URL, or manually simplify the process.

**Using the `init` helper (Browser/XHR):**
```javascript
import { init, Matcher, AnalyzedCharacter } from 'hanzilookup-js';

// Load data (e.g., from a public folder or CDN)
init('mmah', '/data/mmah.json', (success) => {
  if (!success) {
    console.error('Failed to load data');
    return;
  }
  startApp();
});
```

**Manual Data Loading (React Native / Bundled):**
In environments like React Native where you might `require` the JSON directly:
```javascript
import { Matcher, AnalyzedCharacter, data, decodeCompact } from 'hanzilookup-js';
import mmahData from './assets/mmah.json';

// Manually populate the data store
data['mmah'] = mmahData;
// The substrokes data needs to be decoded from base64
data['mmah'].substrokes = decodeCompact(data['mmah'].substrokes);

const matcher = new Matcher('mmah');
```

### 2. Recognizing Characters

Once initialized, use `AnalyzedCharacter` to process strokes and `Matcher` to find matches.

```javascript
// strokes is an array of strokes, where each stroke is an array of [x, y] points.
// Example: [[[10,10], [15,15], ...], [[50,10], ...]]
const rawStrokes = [
  [[54, 33], [58, 38], [80, 77], [85, 87]], // Stroke 1
  [[48, 48], [55, 48], [159, 42], [150, 60]]  // Stroke 2
];

const char = new AnalyzedCharacter(rawStrokes);
const matcher = new Matcher('mmah'); // 'mmah' matches the key used in init/data

matcher.match(char, 8, (matches) => {
  // matches is an array of { character: string, score: number }
  matches.forEach(match => {
    console.log(`Character: ${match.character}, Score: ${match.score}`);
  });
});
```

## API

### `init(dataName: string, url: string, ready: (success: boolean) => void)`
Fetch and initialize character data from a URL.
- `dataName`: Key to store data under (e.g., 'mmah', 'orig').
- `url`: Path to the JSON data file.
- `ready`: Callback when loaded.

### `Matcher(dataName: string, looseness?: number)`
Class for performing character lookups.
- `match(analyzedChar, limit, callback)`: Find top `limit` matches.

### `AnalyzedCharacter(rawStrokes: number[][][])`
Class that processes raw stroke data (arrays of x,y coordinates) into a format suitable for matching.

## Attribution
Original code by Gabor L Ugray.
Rewritten and maintained for modern JS ecosystems.

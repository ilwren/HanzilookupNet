export class SubStroke {
    constructor(direction: number, length: number, centerX: number, centerY: number);
    direction: number;
    length: number;
    centerX: number;
    centerY: number;
}

export class AnalyzedStroke {
    constructor(points: number[][], pivotIndexes: number[], subStrokes: SubStroke[]);
    points: number[][];
    pivotIndexes: number[];
    subStrokes: SubStroke[];
}

export class AnalyzedCharacter {
    constructor(rawStrokes: number[][][]);
    top: number;
    bottom: number;
    left: number;
    right: number;
    analyzedStrokes: AnalyzedStroke[];
    subStrokeCount: number;
}

export class CharacterMatch {
    constructor(character: string, score: number);
    character: string;
    score: number;
}

export class CubicCurve2D {
    constructor(x1: number, y1: number, ctrlx1: number, ctrly1: number, ctrlx2: number, ctrly2: number, x2: number, y2: number);
    x1(): number;
    x2(): number;
    getYOnCurve(t: number): number;
    solveForX(x: number): number[];
    getFirstSolutionForX(x: number): number;
}

export function decodeCompact(base64: string): Uint8Array;

export class MatchCollector {
    constructor(limit: number);
    fileMatch(match: CharacterMatch): void;
    getMatches(): CharacterMatch[];
}

export class Matcher {
    constructor(dataName: string, looseness?: number);
    match(analyzedChar: AnalyzedCharacter, limit: number, ready: (matches: CharacterMatch[]) => void): void;
    getCounters(): { chars: number; subStrokes: number };
}

export function init(dataName: string, url: string, ready: (success: boolean) => void): void;

export const data: { [key: string]: any };

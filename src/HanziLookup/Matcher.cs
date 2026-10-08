namespace HanziLookup;

/// <summary>
/// Matches an <see cref="AnalyzedCharacter"/> against a character repository and returns the best
/// candidates. This is the equivalent of the <c>Matcher</c> class of the JavaScript implementation.
/// </summary>
/// <remarks>
/// <para>
/// Typical usage:
/// <code>
/// var data = HanziData.Load("data/mmah.json");
/// var matcher = new Matcher(data);
///
/// var strokes = new List&lt;IReadOnlyList&lt;StrokePoint&gt;&gt;
/// {
///     new[] { new StrokePoint(32, 128), new StrokePoint(224, 128) }
/// };
///
/// foreach (var match in matcher.Match(new AnalyzedCharacter(strokes), limit: 8))
/// {
///     Console.WriteLine($"{match.Character}: {match.Score:0.000}");
/// }
/// </code>
/// </para>
/// <para>
/// Matching is CPU bound (a full scan compares the input against several thousand characters), so
/// <see cref="MatchAsync"/> runs it on a thread pool thread. A single instance can be used from
/// multiple threads; matching is serialized internally because the score matrix is reused between
/// candidates - exactly as in the original implementation.
/// </para>
/// </remarks>
public sealed class Matcher
{
    /// <summary>Looseness used when none is given (the JavaScript <c>DEFAULT_LOOSENESS</c>).</summary>
    public const double DefaultLooseness = 0.15;

    /// <summary>Maximum number of strokes a repository character can have (mirrors <c>MAX_CHARACTER_STROKE_COUNT</c>).</summary>
    public const int MaxCharacterStrokeCount = 48;

    /// <summary>Maximum number of sub-strokes a repository character can have (mirrors <c>MAX_CHARACTER_SUB_STROKE_COUNT</c>).</summary>
    public const int MaxCharacterSubStrokeCount = 64;

    private const double SkipPenaltyMultiplier = 1.75;
    private const double CorrectNumberOfStrokesBonus = 0.1;
    private const int CorrectNumberOfStrokesCap = 10;
    private const double SkipPenaltyBase = -0.33;

    private readonly object _sync = new();
    private readonly HanziCharacter[] _characters;
    private readonly byte[] _subStrokes;
    private readonly double[] _directionScoreTable;
    private readonly double[] _lengthScoreTable;
    private readonly double[] _positionScoreTable;

    private double[][] _scoreMatrix;
    private int _matrixDimension;
    private double _looseness;
    private int _charsChecked;
    private int _subStrokesCompared;

    /// <summary>Creates a matcher for a repository.</summary>
    /// <param name="data">The character repository to match against.</param>
    /// <param name="looseness">
    /// How far the matcher may stray from the input, 0 (strict) to 1 (permissive). Note that, like the
    /// JavaScript constructor (<c>looseness || DEFAULT_LOOSENESS</c>), a value of 0 selects
    /// <see cref="DefaultLooseness"/>; assign to <see cref="Looseness"/> afterwards to use a strict 0.
    /// </param>
    public Matcher(HanziData data, double looseness = DefaultLooseness)
    {
        ArgumentNullException.ThrowIfNull(data);

        Data = data;
        _characters = data.CharacterArray;
        _subStrokes = data.SubStrokeArray;
        _looseness = CoerceLooseness(looseness);

        _matrixDimension = MaxCharacterSubStrokeCount + 1;
        _scoreMatrix = BuildScoreMatrix(_matrixDimension);

        var directionCurve = new CubicCurve2D(0, 1.0, 0.5, 1.0, 0.25, -2, 1.0, 1.0);
        _directionScoreTable = InitCubicCurveScoreTable(directionCurve, 256);
        var lengthCurve = new CubicCurve2D(0, 0, 0.25, 1.0, 0.75, 1.0, 1.0, 1.0);
        _lengthScoreTable = InitCubicCurveScoreTable(lengthCurve, 129);
        _positionScoreTable = new double[451];
        for (var i = 0; i <= 450; ++i)
        {
            _positionScoreTable[i] = 1 - Math.Sqrt(i) / 22;
        }
    }

    /// <summary>Creates a matcher for a repository registered in the <see cref="HanziDataStore"/>.</summary>
    /// <param name="dataName">Name the repository was registered under (the JavaScript <c>dataName</c>).</param>
    /// <param name="looseness">See <see cref="Matcher(HanziData, double)"/>.</param>
    public Matcher(string dataName, double looseness = DefaultLooseness)
        : this(HanziDataStore.Get(dataName), looseness)
    {
    }

    /// <summary>The repository this matcher works with.</summary>
    public HanziData Data { get; }

    /// <summary>
    /// How far the matcher may stray from the input. Unlike the constructor, the setter accepts every
    /// value as-is, which is how a strict <c>0</c> can be selected.
    /// </summary>
    public double Looseness
    {
        get => _looseness;
        set => _looseness = value;
    }

    /// <summary>Score table used for direction differences (256 entries, indexed by angle difference).</summary>
    public IReadOnlyList<double> DirectionScoreTable => _directionScoreTable;

    /// <summary>Score table used for length ratios (129 entries, indexed by ratio * 128).</summary>
    public IReadOnlyList<double> LengthScoreTable => _lengthScoreTable;

    /// <summary>Score table used for positional closeness (451 entries, indexed by squared distance).</summary>
    public IReadOnlyList<double> PositionScoreTable => _positionScoreTable;

    /// <summary>
    /// Finds the best matches for <paramref name="analyzedCharacter"/>.
    /// </summary>
    /// <param name="analyzedCharacter">The analysed input character.</param>
    /// <param name="limit">Maximum number of matches to return.</param>
    /// <param name="options">Optional knobs; the default reproduces the JavaScript behaviour.</param>
    /// <returns>Matches, best score first (at most <paramref name="limit"/> of them).</returns>
    public IReadOnlyList<CharacterMatch> Match(
        AnalyzedCharacter analyzedCharacter,
        int limit,
        MatchOptions options = default)
        => MatchCore(analyzedCharacter, limit, options, CancellationToken.None);

    /// <summary>Finds the best matches on a thread pool thread.</summary>
    /// <param name="analyzedCharacter">The analysed input character.</param>
    /// <param name="limit">Maximum number of matches to return.</param>
    /// <param name="options">Optional knobs; the default reproduces the JavaScript behaviour.</param>
    /// <param name="cancellationToken">Cancels the scan (it is polled every 1024 characters).</param>
    public Task<IReadOnlyList<CharacterMatch>> MatchAsync(
        AnalyzedCharacter analyzedCharacter,
        int limit,
        MatchOptions options = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => MatchCore(analyzedCharacter, limit, options, cancellationToken), cancellationToken);
    }

    /// <summary>Counters of the most recent <see cref="Match(AnalyzedCharacter, int, MatchOptions)"/> call.</summary>
    public MatcherCounters GetCounters() => new(_charsChecked, _subStrokesCompared);

    private static double CoerceLooseness(double looseness)
        => looseness == 0.0 || double.IsNaN(looseness) ? DefaultLooseness : looseness;

    private IReadOnlyList<CharacterMatch> MatchCore(
        AnalyzedCharacter analyzedCharacter,
        int limit,
        MatchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyzedCharacter);

        lock (_sync)
        {
            // Diagnostic counters.
            _charsChecked = 0;
            _subStrokesCompared = 0;

            var matchCollector = new MatchCollector(limit);

            // Edge case: an empty input finds nothing.
            if (analyzedCharacter.AnalyzedStrokes.Count == 0)
            {
                return matchCollector.GetMatches();
            }

            // Flat format: matching needs this.
            var inputSubStrokes = analyzedCharacter.FlattenedSubStrokes;

            // Some pre-computed looseness magic.
            var strokeCount = analyzedCharacter.AnalyzedStrokes.Count;
            var subStrokeCount = analyzedCharacter.SubStrokeCount;
            var strokeRange = GetStrokesRange(strokeCount);
            var minimumStrokes = Math.Max(strokeCount - strokeRange, 1);
            var maximumStrokes = Math.Min(strokeCount + strokeRange, MaxCharacterStrokeCount);
            var subStrokesRange = GetSubStrokesRange(subStrokeCount);
            var minSubStrokes = Math.Max(subStrokeCount - subStrokesRange, 1);
            var maxSubStrokes = Math.Min(subStrokeCount + subStrokesRange, MaxCharacterSubStrokeCount);

            EnsureScoreMatrix(inputSubStrokes.Length);

            // Iterate over all characters in the repository.
            for (var cix = 0; cix != _characters.Length; ++cix)
            {
                if ((cix & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var repoChar = _characters[cix];
                var cmpStrokeCount = repoChar.StrokeCount;
                var cmpSubStrokes = repoChar.SubStrokeCount;

                if (cmpStrokeCount < minimumStrokes || cmpStrokeCount > maximumStrokes)
                {
                    continue;
                }

                if (options.FilterBySubStrokeCount && (cmpSubStrokes < minSubStrokes || cmpSubStrokes > maxSubStrokes))
                {
                    continue;
                }

                var match = MatchOne(strokeCount, inputSubStrokes, subStrokesRange, repoChar);
                matchCollector.FileMatch(match);
            }

            return matchCollector.GetMatches();
        }
    }

    /// <summary>Translates the looseness setting into a tolerance for the number of strokes.</summary>
    private double GetStrokesRange(int strokeCount)
    {
        if (_looseness == 0)
        {
            return 0;
        }

        if (_looseness == 1)
        {
            return MaxCharacterStrokeCount;
        }

        const double ctrl1X = 0.35;
        var ctrl1Y = strokeCount * 0.4;
        const double ctrl2X = 0.6;
        var ctrl2Y = strokeCount;

        var curve = new CubicCurve2D(
            0, 0,
            ctrl1X, ctrl1Y,
            ctrl2X, ctrl2Y,
            1, MaxCharacterStrokeCount);

        var t = curve.GetFirstSolutionForX(_looseness);
        return JsMath.Round(curve.GetYOnCurve(t));
    }

    /// <summary>Translates the looseness setting into a tolerance for the number of sub-strokes.</summary>
    private double GetSubStrokesRange(int subStrokeCount)
    {
        if (_looseness == 1.0)
        {
            return MaxCharacterSubStrokeCount;
        }

        var y0 = subStrokeCount * 0.25;
        const double ctrl1X = 0.4;
        var ctrl1Y = 1.5 * y0;
        const double ctrl2X = 0.75;
        var ctrl2Y = 1.5 * ctrl1Y;

        var curve = new CubicCurve2D(
            0, y0,
            ctrl1X, ctrl1Y,
            ctrl2X, ctrl2Y,
            1, MaxCharacterSubStrokeCount);

        var t = curve.GetFirstSolutionForX(_looseness);
        return JsMath.Round(curve.GetYOnCurve(t));
    }

    private static double[][] BuildScoreMatrix(int dimension)
    {
        var matrix = new double[dimension][];
        for (var i = 0; i < dimension; i++)
        {
            matrix[i] = new double[dimension];
        }

        for (var i = 0; i < dimension; i++)
        {
            var penalty = SkipPenaltyBase * SkipPenaltyMultiplier * i;
            matrix[i][0] = penalty;
            matrix[0][i] = penalty;
        }

        return matrix;
    }

    /// <summary>
    /// Makes sure the (reused) score matrix has room for the input's sub-strokes. The JavaScript
    /// implementation would throw for inputs with more than 64 sub-strokes; growing the matrix keeps
    /// such inputs working.
    /// </summary>
    private void EnsureScoreMatrix(int inputSubStrokeCount)
    {
        var required = inputSubStrokeCount + 1;
        if (required > _matrixDimension)
        {
            _matrixDimension = required;
            _scoreMatrix = BuildScoreMatrix(required);
        }
    }

    private CharacterMatch MatchOne(
        int inputStrokeCount,
        SubStroke[] inputSubStrokes,
        double subStrokesRange,
        HanziCharacter repoChar)
    {
        ++_charsChecked;
        var score = ComputeMatchScore(inputSubStrokes, subStrokesRange, repoChar);
        if (inputStrokeCount == repoChar.StrokeCount && inputStrokeCount < CorrectNumberOfStrokesCap)
        {
            var bonus = CorrectNumberOfStrokesBonus
                        * Math.Max(CorrectNumberOfStrokesCap - inputStrokeCount, 0)
                        / CorrectNumberOfStrokesCap;
            score += bonus * score;
        }

        return new CharacterMatch(repoChar.Character, score);
    }

    private double ComputeMatchScore(
        SubStroke[] inputSubStrokes,
        double subStrokesRange,
        HanziCharacter repoChar)
    {
        var scoreMatrix = _scoreMatrix;
        var repoSubStrokeCount = repoChar.SubStrokeCount;
        var offset = repoChar.SubStrokeOffset;

        for (var x = 0; x < inputSubStrokes.Length; x++)
        {
            var inputDirection = inputSubStrokes[x].Direction;
            var inputLength = inputSubStrokes[x].Length;
            var inputCenterX = inputSubStrokes[x].CenterX;
            var inputCenterY = inputSubStrokes[x].CenterY;

            for (var y = 0; y < repoSubStrokeCount; y++)
            {
                var newScore = double.NegativeInfinity;
                if (Math.Abs(x - y) <= subStrokesRange)
                {
                    var compareDirection = _subStrokes[offset + y * 3];
                    var compareLength = _subStrokes[offset + y * 3 + 1];

                    var hasCompareCenter = false;
                    var compareCenterX = 0;
                    var compareCenterY = 0;
                    var bCenter = _subStrokes[offset + y * 3 + 2];
                    if (bCenter > 0)
                    {
                        compareCenterX = (bCenter & 0xf0) >> 4;
                        compareCenterY = bCenter & 0x0f;
                        hasCompareCenter = true;
                    }

                    var skip1Score = scoreMatrix[x][y + 1] - inputLength / 256.0 * SkipPenaltyMultiplier;
                    var skip2Score = scoreMatrix[x + 1][y] - compareLength / 256.0 * SkipPenaltyMultiplier;
                    var skipScore = Math.Max(skip1Score, skip2Score);

                    var matchScore = ComputeSubStrokeScore(
                        inputDirection,
                        inputLength,
                        compareDirection,
                        compareLength,
                        inputCenterX,
                        inputCenterY,
                        hasCompareCenter,
                        compareCenterX,
                        compareCenterY);

                    var previousScore = scoreMatrix[x][y];
                    newScore = Math.Max(previousScore + matchScore, skipScore);
                }

                scoreMatrix[x + 1][y + 1] = newScore;
            }
        }

        return scoreMatrix[inputSubStrokes.Length][repoSubStrokeCount];
    }

    private double ComputeSubStrokeScore(
        int inputDirection,
        int inputLength,
        int compareDirection,
        int compareLength,
        int inputCenterX,
        int inputCenterY,
        bool hasCompareCenter,
        int compareCenterX,
        int compareCenterY)
    {
        ++_subStrokesCompared;

        var directionScore = GetDirectionScore(inputDirection, compareDirection, inputLength);
        var lengthScore = GetLengthScore(inputLength, compareLength);
        var score = lengthScore * directionScore;

        if (hasCompareCenter)
        {
            var dx = inputCenterX - compareCenterX;
            var dy = inputCenterY - compareCenterY;
            var squaredDistance = dx * dx + dy * dy;

            // Centres are nibble packed, so a squared distance can never exceed 450; the clamp only
            // protects against hand-built repositories with out-of-range centres.
            if (squaredDistance >= _positionScoreTable.Length)
            {
                squaredDistance = _positionScoreTable.Length - 1;
            }

            var closeness = _positionScoreTable[squaredDistance];
            if (score > 0)
            {
                score *= closeness;
            }
            else
            {
                score /= closeness;
            }
        }

        return score;
    }

    private double GetDirectionScore(int direction1, int direction2, int inputLength)
    {
        var theta = Math.Abs(direction1 - direction2);
        if (theta >= _directionScoreTable.Length)
        {
            theta = _directionScoreTable.Length - 1;
        }

        var directionScore = _directionScoreTable[theta];
        if (inputLength < 64)
        {
            var shortLengthBonusMax = Math.Min(1.0, 1.0 - directionScore);
            var shortLengthBonus = shortLengthBonusMax * (1 - inputLength / 64.0);
            directionScore += shortLengthBonus;
        }

        return directionScore;
    }

    private double GetLengthScore(int length1, int length2)
    {
        double ratio;
        if (length1 > length2)
        {
            if (length1 == 0)
            {
                return double.NaN;
            }

            ratio = JsMath.Round((length2 << 7) / (double)length1);
        }
        else
        {
            if (length2 == 0)
            {
                // Both lengths are zero: JavaScript computes 0/0 and ends up with undefined,
                // which propagates as NaN into the score.
                return double.NaN;
            }

            ratio = JsMath.Round((length1 << 7) / (double)length2);
        }

        if (double.IsNaN(ratio) || ratio < 0 || ratio >= _lengthScoreTable.Length)
        {
            return double.NaN;
        }

        return _lengthScoreTable[(int)ratio];
    }

    private static double[] InitCubicCurveScoreTable(CubicCurve2D curve, int numSamples)
    {
        var x1 = curve.X1;
        var x2 = curve.X2;
        var range = x2 - x1;
        var x = x1;
        var xInc = range / numSamples;
        var scoreTable = new double[numSamples];
        for (var i = 0; i < numSamples; i++)
        {
            var t = curve.GetFirstSolutionForX(Math.Min(x, x2));
            scoreTable[i] = curve.GetYOnCurve(t);
            x += xInc;
        }

        return scoreTable;
    }
}

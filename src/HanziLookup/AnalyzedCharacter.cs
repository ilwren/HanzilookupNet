namespace HanziLookup;

/// <summary>
/// A handwritten character: the raw strokes analysed into sub-strokes, which is the input of
/// <see cref="Matcher.Match(AnalyzedCharacter, int, MatchOptions)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a direct port of the <c>AnalyzedCharacter</c> class of the <c>hanzilookup-js</c> package
/// (v1.0.3), which itself derives from Gabor L Ugray's HanziLookupJS.
/// </para>
/// <para>
/// The analysis has three steps:
/// </para>
/// <list type="number">
///   <item><description>The bounding rectangle of all captured points is computed; it is used to
///   normalize the geometry so that recognition is translation and scale invariant.</description></item>
///   <item><description>Each stroke is decomposed into "pivot" points: points where the stroke turns
///   or where it stops being straight (see <see cref="AnalyzedStroke.PivotIndexes"/>).</description></item>
///   <item><description>Consecutive pivots are turned into <see cref="SubStroke"/>s with a quantized
///   direction, length and centre.</description></item>
/// </list>
/// <para>
/// All arithmetic follows the JavaScript implementation operation by operation; see
/// <c>docs/PORT-NOTES.md</c> for the two deliberate deviations (degenerate strokes are skipped
/// instead of producing <c>NaN</c>, and a stroke needs at least two points).
/// </para>
/// </remarks>
public sealed class AnalyzedCharacter
{
    // Magic constants used in the decomposition of a stroke into sub-strokes.
    private const double MinSegmentLength = 12.5;
    private const double MaxLocalLengthRatio = 1.1;
    private const double MaxRunningLengthRatio = 1.09;

    private readonly double _rawTop;
    private readonly double _rawBottom;
    private readonly double _rawLeft;
    private readonly double _rawRight;

    /// <summary>Creates an empty, "no strokes captured" character.</summary>
    public static AnalyzedCharacter Empty { get; } = new(Array.Empty<IReadOnlyList<StrokePoint>>());

    /// <summary>Analyzes the given raw strokes.</summary>
    /// <param name="rawStrokes">
    /// One list of points per stroke, in the order the strokes were drawn.
    /// The coordinate space is arbitrary (the shipped data uses a 256 x 256 square, y grows downwards).
    /// </param>
    public AnalyzedCharacter(IReadOnlyList<IReadOnlyList<StrokePoint>> rawStrokes)
    {
        ArgumentNullException.ThrowIfNull(rawStrokes);

        var bounds = GetBoundingRect(rawStrokes);
        _rawLeft = bounds.Left;
        _rawTop = bounds.Top;
        _rawRight = bounds.Right;
        _rawBottom = bounds.Bottom;

        var analyzedStrokes = new List<AnalyzedStroke>(rawStrokes.Count);
        var flattened = new List<SubStroke>();
        var subStrokeCount = 0;

        for (var i = 0; i < rawStrokes.Count; ++i)
        {
            var stroke = rawStrokes[i];
            if (stroke is null || IsDegenerate(stroke))
            {
                // A stroke without extent cannot produce meaningful sub-strokes; the JavaScript
                // implementation turns it into NaNs here, this port simply ignores it.
                continue;
            }

            var pivotIndexes = GetPivotIndexes(stroke);
            var subStrokes = BuildSubStrokes(stroke, pivotIndexes);
            subStrokeCount += subStrokes.Length;
            analyzedStrokes.Add(new AnalyzedStroke(stroke, pivotIndexes, subStrokes));
            flattened.AddRange(subStrokes);
        }

        AnalyzedStrokes = analyzedStrokes;
        SubStrokeCount = subStrokeCount;
        FlattenedSubStrokeArray = flattened.ToArray();

        // JavaScript clamps the bounding rectangle into "sane" values for the empty / out of range cases.
        Top = _rawTop <= 256 ? _rawTop : 0;
        Bottom = _rawBottom >= 0 ? _rawBottom : 256;
        Left = _rawLeft <= 256 ? _rawLeft : 0;
        Right = _rawRight >= 0 ? _rawRight : 256;
    }

    /// <summary>The analysed strokes, in drawing order.</summary>
    public IReadOnlyList<AnalyzedStroke> AnalyzedStrokes { get; }

    /// <summary>The number of strokes that could be analysed (equals <c>AnalyzedStrokes.Count</c>).</summary>
    public int StrokeCount => AnalyzedStrokes.Count;

    /// <summary>Total number of sub-strokes of all analysed strokes.</summary>
    public int SubStrokeCount { get; }

    /// <summary>Top edge of the bounding rectangle of the captured points.</summary>
    public double Top { get; }

    /// <summary>Bottom edge of the bounding rectangle of the captured points.</summary>
    public double Bottom { get; }

    /// <summary>Left edge of the bounding rectangle of the captured points.</summary>
    public double Left { get; }

    /// <summary>Right edge of the bounding rectangle of the captured points.</summary>
    public double Right { get; }

    /// <summary>True when no stroke could be analysed.</summary>
    public bool IsEmpty => AnalyzedStrokes.Count == 0;

    /// <summary>
    /// All sub-strokes of all analysed strokes in one flat list, in the order the matcher compares
    /// them (stroke by stroke, pivot by pivot).
    /// </summary>
    public IReadOnlyList<SubStroke> FlattenedSubStrokes => FlattenedSubStrokeArray;

    /// <summary>The flattened sub-strokes as an array; the matcher (same assembly) iterates this one.</summary>
    internal SubStroke[] FlattenedSubStrokeArray { get; }

    /// <summary>Analyzes a set of <see cref="RawStroke"/>s.</summary>
    public static AnalyzedCharacter FromStrokes(IEnumerable<RawStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        var list = new List<IReadOnlyList<StrokePoint>>();
        foreach (var stroke in strokes)
        {
            list.Add(stroke);
        }

        return new AnalyzedCharacter(list);
    }

    /// <summary>
    /// Calculates the rectangle that bounds all points of all raw strokes. Like the original, the
    /// accumulation seeds are <c>Number.MAX_SAFE_INTEGER</c> / <c>Number.MIN_SAFE_INTEGER</c>, which
    /// is what the constructor's clamping turns into the "default" box for empty input.
    /// </summary>
    private static (double Left, double Top, double Right, double Bottom) GetBoundingRect(
        IReadOnlyList<IReadOnlyList<StrokePoint>> rawStrokes)
    {
        var left = JsMath.MaxSafeInteger;
        var top = JsMath.MaxSafeInteger;
        var right = JsMath.MinSafeInteger;
        var bottom = JsMath.MinSafeInteger;

        for (var i = 0; i < rawStrokes.Count; ++i)
        {
            var stroke = rawStrokes[i];
            if (stroke is null)
            {
                continue;
            }

            for (var j = 0; j < stroke.Count; ++j)
            {
                var pt = stroke[j];
                if (pt.X < left)
                {
                    left = pt.X;
                }

                if (pt.X > right)
                {
                    right = pt.X;
                }

                if (pt.Y < top)
                {
                    top = pt.Y;
                }

                if (pt.Y > bottom)
                {
                    bottom = pt.Y;
                }
            }
        }

        return (left, top, right, bottom);
    }

    /// <summary>
    /// True when a stroke cannot yield sub-strokes: it has fewer than two points, or all of its
    /// points coincide (a tap without movement).
    /// </summary>
    private static bool IsDegenerate(IReadOnlyList<StrokePoint> stroke)
    {
        if (stroke.Count < 2)
        {
            return true;
        }

        var minX = stroke[0].X;
        var maxX = minX;
        var minY = stroke[0].Y;
        var maxY = minY;
        for (var i = 1; i < stroke.Count; ++i)
        {
            var pt = stroke[i];
            if (pt.X < minX)
            {
                minX = pt.X;
            }

            if (pt.X > maxX)
            {
                maxX = pt.X;
            }

            if (pt.Y < minY)
            {
                minY = pt.Y;
            }

            if (pt.Y > maxY)
            {
                maxY = pt.Y;
            }
        }

        return minX == maxX && minY == maxY;
    }

    /// <summary>Normalized distance between two points: length / (side * sqrt(2)), capped at 1.</summary>
    private double NormDist(StrokePoint a, StrokePoint b)
    {
        var width = _rawRight - _rawLeft;
        var height = _rawBottom - _rawTop;

        // The normalizer is the diagonal of a square whose sides have the larger dimension of the
        // bounding box.
        var dimensionSquared = width > height ? width * width : height * height;
        var normalizer = Math.Sqrt(dimensionSquared + dimensionSquared);
        var distanceNormalized = normalizer == 0 ? double.NaN : a.DistanceTo(b) / normalizer;

        // Cap at 1.
        return Math.Min(distanceNormalized, 1);
    }

    /// <summary>Angle of the segment from <paramref name="a"/> to <paramref name="b"/>, in radians.</summary>
    private static double GetDirection(StrokePoint a, StrokePoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dir = Math.Atan2(dy, dx);
        return Math.PI - dir;
    }

    private int[] GetPivotIndexes(IReadOnlyList<StrokePoint> points)
    {
        var markers = new bool[points.Count];
        var prevPtIx = 0;
        var firstPtIx = 0;
        var pivotPtIx = 1;
        markers[0] = true;
        var localLength = points[firstPtIx].DistanceTo(points[pivotPtIx]);
        var runningLength = localLength;
        for (var i = 2; i < points.Count; ++i)
        {
            var nextPoint = points[i];
            var pivotLength = points[pivotPtIx].DistanceTo(nextPoint);
            localLength += pivotLength;
            runningLength += pivotLength;
            var distFromPrevious = points[prevPtIx].DistanceTo(nextPoint);
            var distFromFirst = points[firstPtIx].DistanceTo(nextPoint);
            if (localLength > MaxLocalLengthRatio * distFromPrevious ||
                runningLength > MaxRunningLengthRatio * distFromFirst)
            {
                if (markers[prevPtIx] && points[prevPtIx].DistanceTo(points[pivotPtIx]) < MinSegmentLength)
                {
                    markers[prevPtIx] = false;
                }

                markers[pivotPtIx] = true;
                runningLength = pivotLength;
                firstPtIx = pivotPtIx;
            }

            localLength = pivotLength;
            prevPtIx = pivotPtIx;
            pivotPtIx = i;
        }

        markers[pivotPtIx] = true;
        if (markers[prevPtIx] &&
            points[prevPtIx].DistanceTo(points[pivotPtIx]) < MinSegmentLength &&
            prevPtIx != 0)
        {
            markers[prevPtIx] = false;
        }

        var count = 0;
        for (var i = 0; i < markers.Length; ++i)
        {
            if (markers[i])
            {
                ++count;
            }
        }

        var result = new int[count];
        var index = 0;
        for (var i = 0; i < markers.Length; ++i)
        {
            if (markers[i])
            {
                result[index++] = i;
            }
        }

        return result;
    }

    /// <summary>Centre of the segment, normalized by the bounding square (0..1 in the usual case).</summary>
    private (double X, double Y) GetNormCenter(StrokePoint a, StrokePoint b)
    {
        var x = (a.X + b.X) / 2;
        var y = (a.Y + b.Y) / 2;
        double side;
        if (_rawRight - _rawLeft > _rawBottom - _rawTop)
        {
            side = _rawRight - _rawLeft;
            var height = _rawBottom - _rawTop;
            x = x - _rawLeft;
            y = y - _rawTop + (side - height) / 2;
        }
        else
        {
            side = _rawBottom - _rawTop;
            var width = _rawRight - _rawLeft;
            x = x - _rawLeft + (side - width) / 2;
            y = y - _rawTop;
        }

        return (x / side, y / side);
    }

    private SubStroke[] BuildSubStrokes(IReadOnlyList<StrokePoint> points, int[] pivotIndexes)
    {
        var result = new List<SubStroke>(pivotIndexes.Length > 1 ? pivotIndexes.Length - 1 : 1);
        var prevIx = 0;
        for (var i = 0; i < pivotIndexes.Length; ++i)
        {
            var ix = pivotIndexes[i];
            if (ix == prevIx)
            {
                continue;
            }

            var direction = GetDirection(points[prevIx], points[ix]);
            direction = JsMath.Round(direction * 256.0 / Math.PI / 2.0);
            if (direction == 256)
            {
                direction = 0;
            }

            var normLength = JsMath.Round(NormDist(points[prevIx], points[ix]) * 255);
            var center = GetNormCenter(points[prevIx], points[ix]);
            var centerX = JsMath.Round(center.X * 15);
            var centerY = JsMath.Round(center.Y * 15);

            result.Add(new SubStroke(
                JsMath.RoundToInt32(direction),
                JsMath.RoundToInt32(normLength),
                JsMath.RoundToInt32(centerX),
                JsMath.RoundToInt32(centerY)));

            prevIx = ix;
        }

        return result.ToArray();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"AnalyzedCharacter(strokes: {AnalyzedStrokes.Count}, subStrokes: {SubStrokeCount}, bounds: ({Left}, {Top})-({Right}, {Bottom}))";
}

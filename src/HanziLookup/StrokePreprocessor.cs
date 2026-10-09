namespace HanziLookup;

/// <summary>
/// Tuning knobs of <see cref="StrokePreprocessor"/>.
/// </summary>
/// <remarks>
/// The defaults are the ones <c>tools/data/sweep_preprocess.py</c> and <c>tools/data/tune.py</c>
/// picked on simulated handwriting: median strokes written out at a varying speed, with correlated
/// tremor, a small rotation and a small scale error (see <c>tools/data/evaluate.py</c>).  They are
/// expressed in the recognizer's coordinate space, i.e. in the same units as
/// <see cref="StrokePoint"/> - 256 for the shipped data.
///
/// The effect is not subtle.  On characters written that way, top-1 accuracy goes from 0% (nothing
/// matches at all: one straight stroke explodes into 137 sub-strokes where the data has 16, so the
/// right character is filtered out before scoring) to over 90%.  For the 72 digits and Latin letters
/// of <c>data/alnum.json</c> it is 95.4% top-1 and 100% top-5 over 216 simulated handwritings.
///
/// These are defaults, not constants: this is a record with init-only properties, so an application
/// that knows its input device can retune any step, and <see cref="None"/> turns the whole layer off
/// to get the original behaviour back.
/// </remarks>
public sealed record StrokePreprocessingOptions
{
    /// <summary>Points closer together than this are dropped before anything else (0 disables it).</summary>
    public double MinPointDistance { get; init; } = 1.0;

    /// <summary>
    /// Ramer-Douglas-Peucker tolerance: how far a captured point may sit from the straight line
    /// between its neighbours before it is kept as a corner (0 disables simplification).
    /// </summary>
    public double SimplifyEpsilon { get; init; } = 6.0;

    /// <summary>
    /// Spacing of the re-sampled polyline (0 disables re-sampling).  This is the parameter that
    /// matters most: the analyzer decides where a stroke is cut into sub-strokes by comparing the
    /// length of three consecutive samples with the distance between the outer two, so evenly
    /// spaced input behaves like the median data the character repository was built from.
    /// </summary>
    public double ResampleSpacing { get; init; } = 6.0;

    /// <summary>
    /// Size of the centred moving average applied before simplification, in captured points
    /// (0, or less than 3, disables it).
    /// </summary>
    public int SmoothWindow { get; init; } = 5;

    /// <summary>The options used unless a caller passes its own.</summary>
    public static StrokePreprocessingOptions Default { get; } = new();

    /// <summary>Options that pass every captured stroke through untouched.</summary>
    public static StrokePreprocessingOptions None { get; } = new()
    {
        MinPointDistance = 0,
        SimplifyEpsilon = 0,
        ResampleSpacing = 0,
        SmoothWindow = 0,
    };

    /// <summary>True when at least one step is enabled.</summary>
    public bool IsEnabled =>
        MinPointDistance > 0 || SimplifyEpsilon > 0 || ResampleSpacing > 0 || SmoothWindow >= 3;
}

/// <summary>
/// Cleans captured strokes up before they are analysed, so that the recognizer sees the same kind of
/// geometry as the character repository it is matched against.
/// </summary>
/// <remarks>
/// <para>
/// The character data (for example <c>data/mmah.json</c>) is built from <em>median</em> strokes:
/// smooth polylines with evenly spaced points. A pointing device produces neither. Hand tremor and
/// a speed that varies along the stroke make the analyzer's pivot detector fire constantly, and one
/// straight stroke explodes into a dozen sub-strokes - at which point the matcher cannot find the
/// character even though a person would say the input was perfect.
/// </para>
/// <para>
/// The pipeline is, in order: drop duplicate points, a moving average, Ramer-Douglas-Peucker
/// simplification (keeps corners, discards tremor) and equidistant re-sampling that keeps every
/// vertex. It is a port of
/// <c>tools/data/stroke_preprocess.py</c>, which is where the defaults were measured; it deliberately
/// lives <em>outside</em> <see cref="AnalyzedCharacter"/> so that the JavaScript port stays a port -
/// <c>new AnalyzedCharacter(strokes)</c> still behaves exactly like the original.
/// </para>
/// </remarks>
public static class StrokePreprocessor
{
    /// <summary>Runs the full pipeline over one captured stroke.</summary>
    /// <param name="stroke">The captured points, in order.</param>
    /// <param name="options">Tuning knobs; <see cref="StrokePreprocessingOptions.Default"/> when omitted.</param>
    /// <returns>The cleaned stroke, or the input itself when nothing is enabled.</returns>
    public static RawStroke Process(IReadOnlyList<StrokePoint> stroke, StrokePreprocessingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        options ??= StrokePreprocessingOptions.Default;

        if (!options.IsEnabled || stroke.Count == 0)
        {
            return stroke as RawStroke ?? new RawStroke(stroke);
        }

        var points = stroke;
        if (options.MinPointDistance > 0)
        {
            points = Deduplicate(points, options.MinPointDistance);
        }

        if (options.SmoothWindow >= 3)
        {
            points = Smooth(points, options.SmoothWindow);
        }

        if (options.SimplifyEpsilon > 0)
        {
            points = Simplify(points, options.SimplifyEpsilon);
        }

        if (options.ResampleSpacing > 0)
        {
            points = Resample(points, options.ResampleSpacing);
        }

        return new RawStroke(points);
    }

    /// <summary>Runs the pipeline over several strokes.</summary>
    public static IReadOnlyList<RawStroke> Process(
        IEnumerable<IReadOnlyList<StrokePoint>> strokes,
        StrokePreprocessingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        var result = new List<RawStroke>();
        foreach (var stroke in strokes)
        {
            result.Add(Process(stroke, options));
        }

        return result;
    }

    /// <summary>Drops points that are (almost) on top of their predecessor.</summary>
    public static IReadOnlyList<StrokePoint> Deduplicate(IReadOnlyList<StrokePoint> points, double minDistance)
    {
        if (points.Count < 2 || minDistance <= 0)
        {
            return points;
        }

        var result = new List<StrokePoint>(points.Count) { points[0] };
        for (var i = 1; i < points.Count; ++i)
        {
            if (points[result.Count - 1].DistanceTo(points[i]) >= minDistance)
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Centred moving average. <paramref name="window"/> is rounded up to the next odd number; the
    /// first and last <c>window / 2</c> points are left alone so that the stroke keeps its endpoints.
    /// </summary>
    public static IReadOnlyList<StrokePoint> Smooth(IReadOnlyList<StrokePoint> points, int window)
    {
        if (window < 3 || points.Count < window)
        {
            return points;
        }

        if (window % 2 == 0)
        {
            ++window;
        }

        var half = window / 2;
        var result = new List<StrokePoint>(points.Count);
        for (var i = 0; i < half && i < points.Count; ++i)
        {
            result.Add(points[i]);
        }

        for (var i = half; i < points.Count - half; ++i)
        {
            var x = 0.0;
            var y = 0.0;
            for (var j = i - half; j <= i + half; ++j)
            {
                x += points[j].X;
                y += points[j].Y;
            }

            result.Add(new StrokePoint(x / window, y / window));
        }

        for (var i = Math.Max(half, points.Count - half); i < points.Count; ++i)
        {
            result.Add(points[i]);
        }

        return result;
    }

    /// <summary>
    /// Ramer-Douglas-Peucker simplification: keeps the points where the stroke really turns and
    /// drops the ones that only wobble.
    /// </summary>
    public static IReadOnlyList<StrokePoint> Simplify(IReadOnlyList<StrokePoint> points, double epsilon)
    {
        if (points.Count < 3 || epsilon <= 0)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        // Iterative on purpose: strokes can have thousands of points and recursion would be a
        // stack risk.
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            if (last <= first + 1)
            {
                continue;
            }

            var start = points[first];
            var end = points[last];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var norm = Math.Sqrt(dx * dx + dy * dy);

            var worstIndex = -1;
            var worst = 0.0;
            for (var i = first + 1; i < last; ++i)
            {
                var distance = PerpendicularDistance(points[i], start, end, dx, dy, norm);
                if (distance > worst)
                {
                    worst = distance;
                    worstIndex = i;
                }
            }

            if (worst > epsilon && worstIndex > 0)
            {
                keep[worstIndex] = true;
                stack.Push((first, worstIndex));
                stack.Push((worstIndex, last));
            }
        }

        var result = new List<StrokePoint>(keep.Length);
        for (var i = 0; i < keep.Length; ++i)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Re-spaces the polyline to about <paramref name="spacing"/>, **keeping every vertex**.
    /// </summary>
    /// <remarks>
    /// This is what removes the "my mouse moved faster there" part of the input's geometry: with even
    /// spacing along a straight run, the analyzer sees <c>localLength == distFromPrevious</c> and
    /// produces no spurious pivots.
    /// <para>
    /// The vertices matter just as much. The analyzer decides that a stroke turns by comparing the
    /// path length through three <em>consecutive samples</em> with the straight distance between the
    /// outer two, so a corner is only detected when one of those three samples sits on it. Marching a
    /// fixed grid along the path can straddle a corner - the last sample just before it and the next
    /// one just after - which flattens the corner completely: an "L" then analyses as a single
    /// diagonal. Filling each segment separately keeps every corner and still spaces each straight
    /// run evenly.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<StrokePoint> Resample(IReadOnlyList<StrokePoint> points, double spacing)
    {
        if (points.Count < 2 || spacing <= 0)
        {
            return points;
        }

        var total = 0.0;
        for (var i = 0; i < points.Count - 1; ++i)
        {
            total += points[i].DistanceTo(points[i + 1]);
        }

        if (total <= 0)
        {
            return points;
        }

        var capacity = (int)Math.Round(total / spacing) + points.Count + 1;
        var result = new List<StrokePoint>(capacity) { points[0] };
        for (var i = 0; i < points.Count - 1; ++i)
        {
            var a = points[i];
            var b = points[i + 1];
            var segment = a.DistanceTo(b);
            var steps = Math.Max(1, (int)Math.Round(segment / spacing));
            for (var step = 1; step <= steps; ++step)
            {
                var ratio = (double)step / steps;
                result.Add(new StrokePoint(
                    a.X + (b.X - a.X) * ratio,
                    a.Y + (b.Y - a.Y) * ratio));
            }
        }

        return result;
    }

    private static double PerpendicularDistance(
        StrokePoint point,
        StrokePoint start,
        StrokePoint end,
        double dx,
        double dy,
        double norm)
    {
        if (norm == 0.0)
        {
            return point.DistanceTo(start);
        }

        // |cross product| / |direction| is the distance to the infinite line.
        var cross = dy * point.X - dx * point.Y + end.X * start.Y - end.Y * start.X;
        return Math.Abs(cross) / norm;
    }
}
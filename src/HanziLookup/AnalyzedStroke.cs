namespace HanziLookup;

/// <summary>
/// The result of analysing a single raw stroke: the captured points, the indexes of the "pivot"
/// points that were detected in it, and the sub-strokes that connect those pivots.
/// </summary>
public sealed class AnalyzedStroke
{
    /// <summary>Creates an analyzed stroke.</summary>
    /// <param name="points">The captured points of the raw stroke.</param>
    /// <param name="pivotIndexes">Indexes into <paramref name="points"/> of the detected pivots (sorted, ascending).</param>
    /// <param name="subStrokes">The sub-strokes between consecutive pivots.</param>
    public AnalyzedStroke(
        IReadOnlyList<StrokePoint> points,
        IReadOnlyList<int> pivotIndexes,
        IReadOnlyList<SubStroke> subStrokes)
    {
        Points = points ?? throw new ArgumentNullException(nameof(points));
        PivotIndexes = pivotIndexes ?? throw new ArgumentNullException(nameof(pivotIndexes));
        SubStrokes = subStrokes ?? throw new ArgumentNullException(nameof(subStrokes));
    }

    /// <summary>The captured points of the raw stroke.</summary>
    public IReadOnlyList<StrokePoint> Points { get; }

    /// <summary>Indexes into <see cref="Points"/> of the detected pivot points (sorted, ascending).</summary>
    public IReadOnlyList<int> PivotIndexes { get; }

    /// <summary>The sub-strokes between consecutive pivots.</summary>
    public IReadOnlyList<SubStroke> SubStrokes { get; }

    /// <inheritdoc />
    public override string ToString() => $"AnalyzedStroke(points: {Points.Count}, subStrokes: {SubStrokes.Count})";
}

namespace HanziLookup;

/// <summary>
/// A straight segment reconstructed from a <see cref="SubStroke"/> descriptor, expressed in
/// normalized coordinates (a square from (0,0) to (1,1) representing the bounding square of the
/// character). Multiply by the size of the target area to draw it.
/// </summary>
/// <remarks>
/// The reconstruction is the inverse of the analysis performed by <see cref="AnalyzedCharacter"/>:
/// the stored direction is <c>round(angle * 256 / PI / 2)</c> where <c>angle = PI - atan2(dy, dx)</c>
/// is measured on the captured points, the stored length is the segment length normalized by the
/// diagonal of the bounding square and the stored centre is normalized by the side of the square.
/// Reconstructing a whole character from its descriptors therefore approximates its "skeleton"
/// without needing the original strokes - which is what makes it possible to show a preview of a
/// recognition candidate.
/// </remarks>
public readonly struct SkeletonSegment
{
    /// <summary>Creates a segment.</summary>
    public SkeletonSegment(StrokePoint start, StrokePoint end, int direction, int length)
    {
        Start = start;
        End = end;
        Direction = direction;
        Length = length;
    }

    /// <summary>Start point (the point the stroke was drawn from).</summary>
    public StrokePoint Start { get; }

    /// <summary>End point (the point the stroke was drawn towards).</summary>
    public StrokePoint End { get; }

    /// <summary>The quantized direction of the segment (0..255).</summary>
    public int Direction { get; }

    /// <summary>The quantized length of the segment (0..255).</summary>
    public int Length { get; }

    /// <summary>The mid point of the segment.</summary>
    public StrokePoint Center => new((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);

    /// <summary>Returns a copy of this segment scaled by <paramref name="scale"/>.</summary>
    public SkeletonSegment Scale(double scale) =>
        new(new StrokePoint(Start.X * scale, Start.Y * scale), new StrokePoint(End.X * scale, End.Y * scale), Direction, Length);

    /// <inheritdoc />
    public override string ToString() => $"({Start.X:0.###}, {Start.Y:0.###}) -> ({End.X:0.###}, {End.Y:0.###})";
}

/// <summary>
/// Turns sub-stroke descriptors back into drawable segments - the basis for visualizing the
/// analysis of captured strokes and for previewing recognition candidates.
/// </summary>
public static class StrokeSkeleton
{
    /// <summary>Reconstructs the segment of a single sub-stroke, in the normalized 0..1 space.</summary>
    public static SkeletonSegment FromSubStroke(SubStroke subStroke)
    {
        ArgumentNullException.ThrowIfNull(subStroke);

        // Inverse of dir() + the quantization that AnalyzedCharacter applies.
        var angle = subStroke.Direction * Math.PI / 128.0;
        var dx = Math.Cos(angle);
        var dy = -Math.Sin(angle);

        // The length was normalized by the diagonal of the bounding square (side * sqrt(2)) while
        // the centre is expressed in units of the side, hence the sqrt(2) on the length.
        var halfLength = subStroke.Length / 255.0 * Math.Sqrt(2.0) / 2.0;

        var centerX = subStroke.CenterX / 15.0;
        var centerY = subStroke.CenterY / 15.0;

        var start = new StrokePoint(centerX - dx * halfLength, centerY - dy * halfLength);
        var end = new StrokePoint(centerX + dx * halfLength, centerY + dy * halfLength);
        return new SkeletonSegment(start, end, subStroke.Direction, subStroke.Length);
    }

    /// <summary>Reconstructs the segments of all given sub-strokes.</summary>
    public static IReadOnlyList<SkeletonSegment> FromSubStrokes(IEnumerable<SubStroke> subStrokes)
    {
        ArgumentNullException.ThrowIfNull(subStrokes);
        var segments = new List<SkeletonSegment>();
        foreach (var subStroke in subStrokes)
        {
            segments.Add(FromSubStroke(subStroke));
        }

        return segments;
    }

    /// <summary>Reconstructs the skeleton of an analyzed (captured) character.</summary>
    public static IReadOnlyList<SkeletonSegment> FromAnalyzedCharacter(AnalyzedCharacter analyzedCharacter)
    {
        ArgumentNullException.ThrowIfNull(analyzedCharacter);
        return FromSubStrokes(analyzedCharacter.FlattenedSubStrokes);
    }

    /// <summary>
    /// Reconstructs the skeleton of a character stored in a repository, straight from the packed
    /// sub-stroke table, in the normalized 0..1 space.
    /// </summary>
    public static IReadOnlyList<SkeletonSegment> FromRepositoryCharacter(HanziData data, HanziCharacter character)
    {
        ArgumentNullException.ThrowIfNull(data);
        return FromPackedSubStrokes(data.GetSubStrokeBytes(character));
    }

    /// <summary>Reconstructs the segments of a packed sub-stroke table (3 bytes per sub-stroke).</summary>
    public static IReadOnlyList<SkeletonSegment> FromPackedSubStrokes(ReadOnlySpan<byte> packedSubStrokes)
    {
        var count = packedSubStrokes.Length / 3;
        var segments = new List<SkeletonSegment>(count);
        for (var i = 0; i < count; ++i)
        {
            segments.Add(FromPackedSubStroke(packedSubStrokes, i));
        }

        return segments;
    }

    /// <summary>Reconstructs the segment of the <paramref name="index"/>-th packed sub-stroke.</summary>
    public static SkeletonSegment FromPackedSubStroke(ReadOnlySpan<byte> packedSubStrokes, int index)
    {
        var offset = index * 3;
        if (offset < 0 || offset + 2 >= packedSubStrokes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var direction = packedSubStrokes[offset];
        var length = packedSubStrokes[offset + 1];
        var center = packedSubStrokes[offset + 2];

        // A packed centre of 0 means "not stored" for the matcher; no character in the shipped data
        // uses it, and it is rendered as (0, 0) here.
        var centerX = (center & 0xf0) >> 4;
        var centerY = center & 0x0f;
        return FromSubStroke(new SubStroke(direction, length, centerX, centerY));
    }
}

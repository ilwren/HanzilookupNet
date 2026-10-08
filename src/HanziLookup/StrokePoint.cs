namespace HanziLookup;

/// <summary>
/// A single point of a handwriting stroke.
/// </summary>
/// <remarks>
/// The JavaScript implementation represents points as <c>[x, y]</c> number pairs. The coordinate
/// space is arbitrary; the classic HanziLookup test-bed uses a square from (0,0) to (256,256) with
/// <c>y</c> growing downwards, and the character data shipped with this project uses that space.
/// </remarks>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate (grows downwards, like a canvas).</param>
public readonly record struct StrokePoint(double X, double Y)
{
    /// <summary>Euclidean distance between this point and <paramref name="other"/>.</summary>
    public double DistanceTo(StrokePoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

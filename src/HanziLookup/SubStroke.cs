namespace HanziLookup;

/// <summary>
/// A straight sub-stroke: the smallest unit the matcher works with.
/// </summary>
/// <remarks>
/// Sub-strokes are produced by <see cref="AnalyzedCharacter"/> from raw strokes. All four values are
/// quantized integers (like the bytes stored in the character data), which is what makes matching
/// fast and tolerant to handwriting variation:
/// <list type="bullet">
///   <item><description><see cref="Direction"/> is the angle of the segment, 0..255, where
///   <c>direction = round(angle * 256 / PI / 2)</c> and <c>angle</c> is measured in the canvas
///   coordinate system (y grows downwards).</description></item>
///   <item><description><see cref="Length"/> is the length of the segment normalized by the diagonal
///   of the bounding square of the character, scaled to 0..255.</description></item>
///   <item><description><see cref="CenterX"/> / <see cref="CenterY"/> are the coordinates of the
///   segment's mid point normalized by the side of the bounding square, scaled to 0..15
///   (they are stored in a single nibble-packed byte in the data file).</description></item>
/// </list>
/// </remarks>
public sealed class SubStroke
{
    /// <summary>Creates a sub-stroke from its quantized components.</summary>
    public SubStroke(int direction, int length, int centerX, int centerY)
    {
        Direction = direction;
        Length = length;
        CenterX = centerX;
        CenterY = centerY;
    }

    /// <summary>Direction of the segment: 0..255, i.e. <c>round(angle * 256 / PI / 2)</c>.</summary>
    public int Direction { get; }

    /// <summary>Normalized length of the segment: 0..255.</summary>
    public int Length { get; }

    /// <summary>Normalized horizontal centre of the segment: 0..15.</summary>
    public int CenterX { get; }

    /// <summary>Normalized vertical centre of the segment: 0..15.</summary>
    public int CenterY { get; }

    /// <inheritdoc />
    public override string ToString() => $"SubStroke(direction: {Direction}, length: {Length}, center: ({CenterX}, {CenterY}))";
}

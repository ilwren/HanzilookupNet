namespace HanziLookup;

/// <summary>
/// One entry of a character data repository: the character itself, how many strokes and sub-strokes
/// it has, and where its sub-strokes live in the packed sub-stroke table of the repository.
/// </summary>
/// <remarks>
/// In the JavaScript implementation and in the JSON data file this is the array
/// <c>[character, strokeCount, subStrokeCount, subStrokeOffset]</c>.
/// </remarks>
public readonly struct HanziCharacter
{
    /// <summary>Creates a repository entry.</summary>
    public HanziCharacter(string character, int strokeCount, int subStrokeCount, int subStrokeOffset)
    {
        Character = character ?? throw new ArgumentNullException(nameof(character));
        StrokeCount = strokeCount;
        SubStrokeCount = subStrokeCount;
        SubStrokeOffset = subStrokeOffset;
    }

    /// <summary>The character (or component) this entry describes.</summary>
    public string Character { get; }

    /// <summary>How many strokes the reference character has.</summary>
    public int StrokeCount { get; }

    /// <summary>How many sub-strokes the reference character is made of.</summary>
    public int SubStrokeCount { get; }

    /// <summary>Offset of the first sub-stroke in the repository's packed sub-stroke table (3 bytes per sub-stroke).</summary>
    public int SubStrokeOffset { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Character} (strokes: {StrokeCount}, subStrokes: {SubStrokeCount}, offset: {SubStrokeOffset})";
}

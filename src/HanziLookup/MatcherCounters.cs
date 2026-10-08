namespace HanziLookup;

/// <summary>
/// Diagnostic counters of the last <see cref="Matcher.Match(AnalyzedCharacter, int, MatchOptions)"/>
/// call, mirroring the <c>{ chars, subStrokes }</c> object returned by <c>getCounters()</c> in the
/// JavaScript implementation.
/// </summary>
/// <param name="CharactersChecked">How many repository characters passed the stroke count pre-filter and were compared.</param>
/// <param name="SubStrokesCompared">How many individual sub-stroke comparisons were performed.</param>
public readonly record struct MatcherCounters(int CharactersChecked, int SubStrokesCompared)
{
    /// <summary>A zeroed counter set.</summary>
    public static MatcherCounters Zero => default;

    /// <inheritdoc />
    public override string ToString() => $"characters checked: {CharactersChecked:n0}, sub-strokes compared: {SubStrokesCompared:n0}";
}

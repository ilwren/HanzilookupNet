namespace HanziLookup;

/// <summary>
/// Options for a match run.
/// </summary>
/// <remarks>
/// The default value of this type parameterizes a run in exactly the same way the JavaScript
/// implementation does; the one extra knob is <see cref="FilterBySubStrokeCount"/>.
/// </remarks>
public readonly record struct MatchOptions
{
    /// <summary>
    /// Reject repository characters whose number of sub-strokes lies outside the matching window
    /// before comparing them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The JavaScript implementation contains this pre-filter, but it is inert: the entry it tests
    /// (<c>repoChar[2]</c>) is a <em>number</em>, so the expression it evaluates is
    /// <c>undefined &gt; x</c>, which is always false. As a result the original returns candidates
    /// that were never really compared, with a score of <see cref="double.NegativeInfinity"/>,
    /// whenever there are fewer than <c>limit</c> real matches.
    /// </para>
    /// <para>
    /// Because this library is a port, the default (<c>false</c>) reproduces that behaviour, and
    /// <see cref="Strict"/> - which enables the filter - removes the trailing non-matches. The
    /// relative order and the scores of the remaining candidates are identical either way, so
    /// enabling the filter only ever drops entries whose score is <see cref="double.NegativeInfinity"/>.
    /// </para>
    /// </remarks>
    public bool FilterBySubStrokeCount { get; init; }

    /// <summary>Behaviour of the JavaScript implementation, including its inert sub-stroke count pre-filter.</summary>
    public static MatchOptions JavaScriptCompatible => default;

    /// <summary>Behaviour of this library by default: identical to <see cref="JavaScriptCompatible"/>.</summary>
    public static MatchOptions Default => default;

    /// <summary>Enables the sub-stroke count pre-filter, removing trailing <c>-Infinity</c> matches.</summary>
    public static MatchOptions Strict => new MatchOptions { FilterBySubStrokeCount = true };
}

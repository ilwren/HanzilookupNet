namespace HanziLookup;

/// <summary>
/// A single recognition result: the matched character together with its score.
/// Higher scores are better. A match can legitimately have a score of
/// <see cref="double.NegativeInfinity"/>, which is what the JavaScript implementation produces for
/// candidates whose sub-stroke count places them outside the matching window; see
/// <see cref="MatchOptions.FilterBySubStrokeCount"/> for removing those results.
/// </summary>
public sealed class CharacterMatch
{
    /// <summary>Creates a match.</summary>
    public CharacterMatch(string character, double score)
    {
        Character = character ?? throw new ArgumentNullException(nameof(character));
        Score = score;
    }

    /// <summary>The matched character (a single character, or a component such as ⺀).</summary>
    public string Character { get; }

    /// <summary>The match score; higher is better.</summary>
    public double Score { get; }

    /// <summary>True when the score is a real number (i.e. the candidate was actually compared).</summary>
    public bool HasFiniteScore => !double.IsInfinity(Score) && !double.IsNaN(Score);

    /// <inheritdoc />
    public override string ToString() => HasFiniteScore ? $"{Character} ({Score:0.####})" : $"{Character} ({Score})";
}

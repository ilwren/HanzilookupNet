namespace HanziLookup;

/// <summary>
/// Keeps the <c>limit</c> best matches while a repository is being scanned: a port of the
/// <c>MatchCollector</c> of the JavaScript implementation (best first, no duplicate characters).
/// </summary>
/// <remarks>
/// The collector is intentionally simple - inserting into a small sorted array - exactly like the
/// original. It is not thread safe; <see cref="Matcher"/> serializes access to it.
/// </remarks>
public sealed class MatchCollector
{
    private readonly CharacterMatch?[] _matches;
    private int _count;

    /// <summary>Creates a collector that keeps at most <paramref name="limit"/> matches.</summary>
    public MatchCollector(int limit)
    {
        _matches = new CharacterMatch?[limit > 0 ? limit : 0];
    }

    /// <summary>The number of matches collected so far.</summary>
    public int Count => _count;

    /// <summary>The configured capacity (the <c>limit</c> passed to the constructor).</summary>
    public int Limit => _matches.Length;

    /// <summary>Offers a match to the collector; it is kept when it belongs to the current best set.</summary>
    public void FileMatch(CharacterMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        // JavaScript returns an empty result for limit <= 0; short circuit here rather than
        // indexing into an empty array below.
        if (_matches.Length == 0)
        {
            return;
        }

        // Already at limit: don't bother if the new match's score is smaller than the current minimum.
        if (_count == _matches.Length && match.Score <= _matches[_matches.Length - 1]!.Score)
        {
            return;
        }

        // Remove if we already have this character with a lower score.
        // If it returns true, we should skip the new match (it is already there with a higher score).
        if (RemoveExistingLower(match))
        {
            return;
        }

        // Where does the new match go? (Keep the array sorted, largest score first.)
        var pos = FindSlot(match.Score);

        // Defensive: the slot search can only return a position inside the array (see the checks
        // above), but an out-of-range write is much worse than a dropped match.
        if (pos >= _matches.Length)
        {
            return;
        }

        // Slide the rest to the right.
        for (var i = _matches.Length - 1; i > pos; --i)
        {
            _matches[i] = _matches[i - 1];
        }

        // Replace at position.
        _matches[pos] = match;

        // Increase the count if we are just now filling up.
        if (_count < _matches.Length)
        {
            ++_count;
        }
    }

    /// <summary>Returns the matches collected so far, best first.</summary>
    public IReadOnlyList<CharacterMatch> GetMatches()
    {
        if (_count == 0)
        {
            return Array.Empty<CharacterMatch>();
        }

        var result = new CharacterMatch[_count];
        for (var i = 0; i < _count; ++i)
        {
            result[i] = _matches[i]!;
        }

        return result;
    }

    private int FindSlot(double score)
    {
        var ix = 0;
        while (ix < _count)
        {
            if (_matches[ix]!.Score < score)
            {
                return ix;
            }

            ++ix;
        }

        return ix;
    }

    private bool RemoveExistingLower(CharacterMatch match)
    {
        var ix = -1;
        for (var i = 0; i < _count; ++i)
        {
            if (string.Equals(_matches[i]!.Character, match.Character, StringComparison.Ordinal))
            {
                ix = i;
                break;
            }
        }

        // Not there yet: we're good, the match doesn't need to be skipped.
        if (ix == -1)
        {
            return false;
        }

        // New score is not better: skip this match.
        if (match.Score <= _matches[ix]!.Score)
        {
            return true;
        }

        // Remove the existing match; don't skip the new one. Means shifting the array left.
        for (var i = ix; i < _matches.Length - 1; ++i)
        {
            _matches[i] = _matches[i + 1];
        }

        --_count;
        return false;
    }
}

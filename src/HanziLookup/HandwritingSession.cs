using System.Collections.ObjectModel;
using System.Diagnostics;

namespace HanziLookup;

/// <summary>Event data for <see cref="HandwritingSession.RecognitionCompleted"/>.</summary>
public sealed class RecognitionCompletedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public RecognitionCompletedEventArgs(
        AnalyzedCharacter analysis,
        IReadOnlyList<CharacterMatch> results,
        MatcherCounters counters,
        TimeSpan duration)
    {
        Analysis = analysis;
        Results = results;
        Counters = counters;
        Duration = duration;
    }

    /// <summary>The analysis of the strokes that were matched.</summary>
    public AnalyzedCharacter Analysis { get; }

    /// <summary>The matches, best first.</summary>
    public IReadOnlyList<CharacterMatch> Results { get; }

    /// <summary>Diagnostic counters of the match run.</summary>
    public MatcherCounters Counters { get; }

    /// <summary>How long the match run took.</summary>
    public TimeSpan Duration { get; }
}

/// <summary>
/// The mutable state of an interactive handwriting input: the strokes captured so far, their
/// analysis, and the most recent recognition results.
/// </summary>
/// <remarks>
/// <para>
/// This class contains no UI code; it is what a view (such as the Avalonia control shipped in
/// <c>HanziLookup.Avalonia</c>, or the demo application) observes. It raises
/// <see cref="Changed"/> whenever strokes or results change and
/// <see cref="RecognitionCompleted"/> after every match run.
/// </para>
/// <para>
/// <see cref="RecognizeAsync"/> runs the match on a thread pool thread; the state is updated after
/// the await, so call it from the UI thread and the continuation returns there.
/// </para>
/// </remarks>
public sealed class HandwritingSession
{
    /// <summary>Creates a session over a matcher.</summary>
    public HandwritingSession(Matcher matcher, int resultLimit = 8)
    {
        Matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        ResultLimit = resultLimit;
    }

    /// <summary>Creates a session over a repository.</summary>
    public HandwritingSession(HanziData data, double looseness = Matcher.DefaultLooseness, int resultLimit = 8)
        : this(new Matcher(data, looseness), resultLimit)
    {
    }

    /// <summary>The matcher used for recognition.</summary>
    public Matcher Matcher { get; }

    /// <summary>The captured strokes, in drawing order.</summary>
    public ObservableCollection<RawStroke> Strokes { get; } = new();

    /// <summary>How many candidate characters to keep (8 by default, like the JavaScript demo).</summary>
    public int ResultLimit { get; set; }

    /// <summary>Options for the next match run.</summary>
    public MatchOptions Options { get; set; } = MatchOptions.Default;

    /// <summary>When true (the default) every added, removed or cleared stroke triggers a match run.</summary>
    public bool AutoRecognize { get; set; } = true;

    /// <summary>The analysis of the current strokes, or <c>null</c> when nothing was analysed yet.</summary>
    public AnalyzedCharacter? Analysis { get; private set; }

    /// <summary>The most recent recognition results, best first.</summary>
    public IReadOnlyList<CharacterMatch> Results { get; private set; } = Array.Empty<CharacterMatch>();

    /// <summary>Counters of the most recent match run.</summary>
    public MatcherCounters LastCounters { get; private set; }

    /// <summary>Duration of the most recent match run.</summary>
    public TimeSpan LastDuration { get; private set; }

    /// <summary>Raised when strokes or results changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after a match run completed.</summary>
    public event EventHandler<RecognitionCompletedEventArgs>? RecognitionCompleted;

    /// <summary>True when at least one stroke was captured.</summary>
    public bool HasStrokes => Strokes.Count > 0;

    /// <summary>Adds a captured stroke; recognizes again when <see cref="AutoRecognize"/> is set.</summary>
    public void AddStroke(RawStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        Strokes.Add(stroke);
        Analysis = AnalyzedCharacter.FromStrokes(Strokes);
        Changed?.Invoke(this, EventArgs.Empty);

        if (AutoRecognize)
        {
            Recognize();
        }
    }

    /// <summary>Adds several captured strokes at once.</summary>
    public void AddStrokes(IEnumerable<RawStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        foreach (var stroke in strokes)
        {
            Strokes.Add(stroke);
        }

        Analysis = AnalyzedCharacter.FromStrokes(Strokes);
        Changed?.Invoke(this, EventArgs.Empty);

        if (AutoRecognize)
        {
            Recognize();
        }
    }

    /// <summary>Removes the most recently added stroke.</summary>
    public bool RemoveLastStroke()
    {
        if (Strokes.Count == 0)
        {
            return false;
        }

        Strokes.RemoveAt(Strokes.Count - 1);
        Analysis = AnalyzedCharacter.FromStrokes(Strokes);
        Changed?.Invoke(this, EventArgs.Empty);

        if (AutoRecognize)
        {
            Recognize();
        }

        return true;
    }

    /// <summary>Removes all strokes and results.</summary>
    public void Clear()
    {
        Strokes.Clear();
        Analysis = null;
        Results = Array.Empty<CharacterMatch>();
        LastCounters = MatcherCounters.Zero;
        LastDuration = TimeSpan.Zero;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Analyzes the current strokes without matching them.</summary>
    public AnalyzedCharacter Analyze()
    {
        var analysis = AnalyzedCharacter.FromStrokes(Strokes);
        Analysis = analysis;
        return analysis;
    }

    /// <summary>Analyzes the current strokes and matches them, on the calling thread.</summary>
    public IReadOnlyList<CharacterMatch> Recognize()
    {
        var analysis = Analyze();
        var stopwatch = Stopwatch.StartNew();
        var results = Matcher.Match(analysis, ResultLimit, Options);
        stopwatch.Stop();

        CompleteRecognition(analysis, results, stopwatch.Elapsed);
        return results;
    }

    /// <summary>Analyzes the current strokes and matches them on a thread pool thread.</summary>
    public async Task<IReadOnlyList<CharacterMatch>> RecognizeAsync(CancellationToken cancellationToken = default)
    {
        var analysis = Analyze();
        var stopwatch = Stopwatch.StartNew();
        var results = await Matcher.MatchAsync(analysis, ResultLimit, Options, cancellationToken).ConfigureAwait(true);
        stopwatch.Stop();

        CompleteRecognition(analysis, results, stopwatch.Elapsed);
        return results;
    }

    private void CompleteRecognition(AnalyzedCharacter analysis, IReadOnlyList<CharacterMatch> results, TimeSpan duration)
    {
        Analysis = analysis;
        Results = results;
        LastCounters = Matcher.GetCounters();
        LastDuration = duration;

        RecognitionCompleted?.Invoke(this, new RecognitionCompletedEventArgs(analysis, results, LastCounters, duration));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

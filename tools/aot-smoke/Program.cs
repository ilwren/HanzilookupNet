using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HanziLookup;

namespace HanziLookup.AotSmoke;

/// <summary>
/// A console program that exercises the recognizer end to end and is published as a native AOT
/// binary in CI. It asserts values that the reference vectors and the shipped data file pin down,
/// so it doubles as a check that the port survives full native compilation (no IL, no JIT).
/// </summary>
internal static class Program
{
    // Anchors of the "single-horizontal-stroke" reference vector: input (32, 128) -> (224, 128).
    private const double HorizontalTopScore = 1.0199310536683939;

    // The shipped mmah.json: 9507 characters, 156493 sub-strokes packed into 469479 bytes.
    private const int CharacterCount = 9507;
    private const int SubStrokeByteCount = 469479;
    private const int SubStrokeCount = 156493;

    private static int s_failures;

    public static async Task<int> Main(string[] args)
    {
        var dataPath = ResolveDataFile(args);
        if (dataPath is null)
        {
            Console.Error.WriteLine("mmah.json was not found next to the binary or in the repository layout.");
            return 2;
        }

        Console.WriteLine($"native AOT smoke test · .NET {Environment.Version} · {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine($"data: {Path.GetFileName(dataPath)} ({new FileInfo(dataPath).Length:n0} bytes)");

        var stopwatch = Stopwatch.StartNew();

        // 1. Loading and parsing: System.Text.Json + the base64 compact decoder, both reflection-free.
        var data = HanziData.Load(dataPath);
        Check("character count", CharacterCount, data.Count);
        Check("sub-stroke byte count", SubStrokeByteCount, data.SubStrokes.Length);
        Check("sub-stroke count", SubStrokeCount, data.SubStrokes.Length / 3);

        var one = data.Find("一");
        Check("一 is in the repository", true, one is not null);
        Check("一 packed bytes", "0,180,119",
            string.Join(",", data.GetSubStrokeBytes(one!.Value).ToArray()));
        Check("missing characters", true, data.Find("無") is null);

        Check("decodeCompact, padded", "18,52", string.Join(",", CompactDataDecoder.Decode("EjQ=")));
        Check("decodeCompact, garbage character", "18,52,64",
            string.Join(",", CompactDataDecoder.Decode("EjR\u0001")));

        // 2. Analysis of a horizontal stroke.
        var analysis = AnalyzedCharacter.FromStrokes(new[]
        {
            new RawStroke(new[] { new StrokePoint(32, 128), new StrokePoint(224, 128) })
        });
        Check("stroke count", 1, analysis.StrokeCount);
        Check("sub-stroke count", 1, analysis.SubStrokeCount);
        Check("bounds", "(32, 128, 224, 128)",
            $"({analysis.Left:0}, {analysis.Top:0}, {analysis.Right:0}, {analysis.Bottom:0})");
        var inputSubStroke = analysis.FlattenedSubStrokes[0];
        Check("sub-stroke", "dir 0, len 180, centre (8, 8)",
            $"dir {inputSubStroke.Direction}, len {inputSubStroke.Length}, " +
            $"centre ({inputSubStroke.CenterX}, {inputSubStroke.CenterY})");

        // 3. Matching against the whole repository.
        var matcher = new Matcher(data);
        var matches = matcher.Match(analysis, 8);
        Check("result count", 8, matches.Count);
        Check("best match", "一", matches[0].Character);
        Check("best score", HorizontalTopScore, matches[0].Score, 1e-12);
        Check("finite scores", 3, matches.Count(match => match.HasFiniteScore));

        var counters = matcher.GetCounters();
        Check("counters", "8 characters / 8 sub-strokes",
            $"{counters.CharactersChecked} characters / {counters.SubStrokesCompared} sub-strokes");

        Check("strict result count", 3, matcher.Match(analysis, 8, MatchOptions.Strict).Count);

        // 4. The packed table -> skeleton path the preview controls use.
        var repositoryOne = StrokeSkeleton.FromRepositoryCharacter(data, one!.Value);
        Check("一 skeleton segments", 1, repositoryOne.Count);
        Check("一 skeleton length", 180, repositoryOne[0].Length);
        Check("一 skeleton centre x", 7.0 / 15.0, repositoryOne[0].Center.X, 1e-12);
        Check("一 skeleton start x", 7.0 / 15.0 - 180.0 / 255.0 * Math.Sqrt(2) / 2,
            repositoryOne[0].Start.X, 1e-12);

        var analyzedSkeleton = StrokeSkeleton.FromAnalyzedCharacter(analysis);
        Check("skeleton from the input", repositoryOne[0].Length, analyzedSkeleton[0].Length);
        Check("學 skeleton segments", 25,
            StrokeSkeleton.FromRepositoryCharacter(data, data.Find("學")!.Value).Count);

        // 5. The session wrapper the Avalonia controls drive.
        var session = new HandwritingSession(data, looseness: 0.15, resultLimit: 8);
        session.AddStroke(new RawStroke(new[] { new StrokePoint(32, 128), new StrokePoint(224, 128) }));
        var sessionResults = await session.RecognizeAsync().ConfigureAwait(false);
        Check("session best match", "一", sessionResults[0].Character);
        Check("session analysis", 1, session.Analysis!.SubStrokeCount);
        Check("session counters", 8, session.LastCounters.CharactersChecked);

        // 6. The static registry (its Names property was one of the AOT-sensitive spots).
        HanziDataStore.Register("mmah", data);
        Check("data store lookup", true,
            HanziDataStore.TryGet("mmah", out var stored) && ReferenceEquals(stored, data));
        Check("data store names", true, HanziDataStore.Names.Contains("mmah"));

        stopwatch.Stop();
        Console.WriteLine($"elapsed: {stopwatch.Elapsed.TotalMilliseconds:0} ms");

        if (s_failures > 0)
        {
            Console.Error.WriteLine($"{s_failures} check(s) FAILED");
            return 1;
        }

        Console.WriteLine("all checks passed · native AOT ✓");
        return 0;
    }

    private static void Check(string name, object expected, object actual)
    {
        if (Equals(expected, actual))
        {
            Console.WriteLine($"  ok   {name}: {actual}");
            return;
        }

        Report(name, expected, actual);
    }

    private static void Check(string name, double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) <= tolerance)
        {
            Console.WriteLine($"  ok   {name}: {actual.ToString("R", CultureInfo.InvariantCulture)}");
            return;
        }

        Report(name, expected.ToString("R", CultureInfo.InvariantCulture),
            actual.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void Report(string name, object expected, object actual)
    {
        s_failures++;
        Console.Error.WriteLine($"  FAIL {name}: expected {expected}, got {actual}");
    }

    private static string? ResolveDataFile(IReadOnlyList<string> args)
    {
        var candidates = new List<string>();
        if (args.Count > 0)
        {
            candidates.Add(args[0]);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data", "mmah.json"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "mmah.json"));

        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && directory is not null; i++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "data", "mmah.json"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}

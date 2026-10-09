using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>
/// Tests for <see cref="StrokePreprocessor"/> and for the accuracy problem it exists to solve.
/// </summary>
/// <remarks>
/// <para>
/// The numbers quoted in the comments come from <c>tools/data/evaluate.py</c>, which writes the 9507
/// characters of <c>mmah.json</c> the way a pointing device would (variable speed, tremor,
/// rotation, scale error) and measures top-1 / top-5 accuracy with and without the pipeline.
/// </para>
/// <para>
/// <see cref="Hand"/> is a deterministic stand-in for a pointing device - uneven sampling plus
/// tremor, but reproducible, so the expected outcome of the end-to-end tests can be verified outside
/// the .NET build (it was, with <c>tools/data/</c>).
/// </para>
/// </remarks>
public sealed class StrokePreprocessingTests
{
    private static HanziData Chinese() => HanziData.Load(TestPaths.Require(TestPaths.DataFile, "mmah.json"));

    /// <summary>
    /// Walks a polyline the way a hand does: the distance between reported points varies, and every
    /// point is off by a deterministic wobble.  Deterministic on purpose - see the remarks.
    /// </summary>
    internal static RawStroke Hand(IEnumerable<StrokePoint> ideal, double amplitude = 2.0, double baseStep = 2.0)
    {
        var points = new List<StrokePoint>();
        var path = ideal.ToArray();
        var index = 0;

        for (var s = 0; s + 1 < path.Length; ++s)
        {
            var a = path[s];
            var b = path[s + 1];
            var length = a.DistanceTo(b);
            if (length <= 0)
            {
                continue;
            }

            var travelled = 0.0;
            var step = baseStep;
            while (travelled <= length)
            {
                var ratio = travelled / length;
                var x = a.X + (b.X - a.X) * ratio;
                var y = a.Y + (b.Y - a.Y) * ratio;
                points.Add(new StrokePoint(
                    x + amplitude * Math.Sin(index * 1.7),
                    y + amplitude * Math.Cos(index * 2.3)));
                travelled += step;
                step = baseStep + baseStep * (Fraction(index * 0.6180339887));
                ++index;
            }
        }

        points.Add(path[path.Length - 1]);
        return new RawStroke(points);
    }

    /// <summary>The fractional part of a non-negative value (the golden-ratio spacing pattern).</summary>
    private static double Fraction(double value) => value - Math.Floor(value);

    [Fact]
    public void DefaultOptionsAreEnabled()
    {
        Assert.True(StrokePreprocessingOptions.Default.IsEnabled);
        Assert.False(StrokePreprocessingOptions.None.IsEnabled);
    }

    [Fact]
    public void DeduplicateDropsPointsOnTopOfEachOther()
    {
        var points = new[]
        {
            new StrokePoint(0, 0),
            new StrokePoint(0.2, 0),
            new StrokePoint(0.4, 0),
            new StrokePoint(10, 0),
        };

        var result = StrokePreprocessor.Deduplicate(points, 1.0);

        Assert.Equal(2, result.Count);
        Assert.Equal(new StrokePoint(0, 0), result[0]);
        Assert.Equal(new StrokePoint(10, 0), result[1]);
    }

    [Fact]
    public void SimplifyKeepsCornersAndDropsWobble()
    {
        // A right angle with a shaky hand on the way there.
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 20; ++i)
        {
            points.Add(new StrokePoint(i * 5.0, i % 2 == 0 ? 0.6 : -0.6));
        }

        for (var i = 1; i <= 10; ++i)
        {
            points.Add(new StrokePoint(100 + (i % 2 == 0 ? 0.6 : -0.6), i * 5.0));
        }

        var result = StrokePreprocessor.Simplify(points, 2.0);

        // Three points: start, the corner, the end.  The corner is the captured point (100, 0.6) -
        // simplification picks points out of the input, it does not invent them.
        Assert.Equal(3, result.Count);
        Assert.Equal(new StrokePoint(0, 0.6), result[0]);
        Assert.Equal(new StrokePoint(100, 0.6), result[1]);
        Assert.Equal(new StrokePoint(100.6, 50), result[2]);
    }

    [Fact]
    public void ResampleSpacesPointsEvenly()
    {
        var points = new[]
        {
            new StrokePoint(0, 0),
            new StrokePoint(90, 0),
            new StrokePoint(100, 0),
        };

        var result = StrokePreprocessor.Resample(points, 10.0);

        Assert.True(result.Count >= 10);
        for (var i = 1; i < result.Count; ++i)
        {
            var distance = result[i - 1].DistanceTo(result[i]);
            Assert.True(distance is > 0 and < 11.0, $"step {i} was {distance}");
        }

        Assert.Equal(0.0, result[0].X, 6);
        Assert.Equal(100.0, result[result.Count - 1].X, 6);
    }

    /// <summary>
    /// A stroke is re-spaced even when it is a single straight segment - that is the whole point of the
    /// step.  A stroke without extent (a tap) is the only thing returned untouched.
    /// </summary>
    [Fact]
    public void ResampleSpacesATwoPointStrokeAndLeavesATapAlone()
    {
        var line = new[] { new StrokePoint(32, 128), new StrokePoint(224, 128) };
        Assert.Equal(33, StrokePreprocessor.Resample(line, 6.0).Count);

        var tap = new[] { new StrokePoint(10, 10) };
        Assert.Single(StrokePreprocessor.Resample(tap, 6.0));
    }

    /// <summary>
    /// The corner survives re-sampling.  Marching a fixed grid along the path would put one sample
    /// before the corner and the next one after it and flatten the turn - which is why an "L" written
    /// this way analyses as one straight diagonal instead of two sub-strokes.
    /// </summary>
    [Fact]
    public void ResampleKeepsCorners()
    {
        var corner = new[] { new StrokePoint(0, 0), new StrokePoint(100, 0), new StrokePoint(100, 100) };

        var result = StrokePreprocessor.Resample(corner, 6.0);

        Assert.Contains(new StrokePoint(100, 0), result);
        Assert.Contains(new StrokePoint(100, 100), result);
    }

    [Fact]
    public void SmoothKeepsTheEndpoints()
    {
        var points = new[] { new StrokePoint(0, 0), new StrokePoint(1, 9), new StrokePoint(2, 0) };
        var result = StrokePreprocessor.Smooth(points, 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(0.0, result[0].Y, 6);
        Assert.Equal(0.0, result[2].Y, 6);
        Assert.Equal(3.0, result[1].Y, 6);
    }

    /// <summary>
    /// The regression this whole feature exists for.  A single horizontal stroke written by hand
    /// becomes 12 sub-strokes when the raw samples are analysed, which is more than the 1 that 一 has -
    /// and the stroke-count filter then throws the character away before a single score is computed.
    /// </summary>
    [Fact]
    public void WobblyStraightStrokeStaysOneSubStroke()
    {
        var shaky = Hand(new[] { new StrokePoint(36, 126), new StrokePoint(130, 131), new StrokePoint(220, 127) });

        var raw = new AnalyzedCharacter(new[] { (IReadOnlyList<StrokePoint>)shaky });
        var cleaned = new AnalyzedCharacter(new[]
        {
            (IReadOnlyList<StrokePoint>)StrokePreprocessor.Process(shaky)
        });

        Assert.Equal(12, raw.SubStrokeCount);
        Assert.Equal(1, cleaned.SubStrokeCount);
    }

    [Fact]
    public void SessionPreprocessingChangesTheAnalysis()
    {
        var data = Chinese();
        var shaky = Hand(new[] { new StrokePoint(36, 126), new StrokePoint(130, 131), new StrokePoint(220, 127) });

        var withPipeline = new HandwritingSession(data) { AutoRecognize = false };
        withPipeline.AddStroke(shaky);
        var cleaned = withPipeline.Analysis!.SubStrokeCount;

        var withoutPipeline = new HandwritingSession(data) { AutoRecognize = false, Preprocessing = null };
        withoutPipeline.AddStroke(shaky);
        var raw = withoutPipeline.Analysis!.SubStrokeCount;

        Assert.Equal(1, cleaned);
        Assert.Equal(12, raw);
    }

    [Fact]
    public void SessionStoresExactlyWhatWasDrawn()
    {
        var session = new HandwritingSession(Chinese()) { AutoRecognize = false };
        var stroke = Hand(new[] { new StrokePoint(0, 0), new StrokePoint(60, 0), new StrokePoint(120, 4) });

        session.AddStroke(stroke);

        Assert.Same(stroke, session.Strokes[0]);
        Assert.Equal(stroke.Count, session.Strokes[0].Count);
    }

    /// <summary>
    /// End to end: a hand-written 一 comes back as 一, and the same input without the pipeline does
    /// not - raw it analyses as 15 sub-strokes where the data has one, so the right character is
    /// filtered out before scoring and something unrelated comes back instead.
    /// </summary>
    [Fact]
    public void HandwrittenHorizontalStrokeIsRecognized()
    {
        var matcher = new Matcher(Chinese());
        var shaky = Hand(new[] { new StrokePoint(36, 126), new StrokePoint(130, 131), new StrokePoint(220, 127) });

        var raw = matcher.Match(new AnalyzedCharacter(new[] { (IReadOnlyList<StrokePoint>)shaky }), 5);
        var preprocessed = matcher.Match(
            new AnalyzedCharacter(new[] { (IReadOnlyList<StrokePoint>)StrokePreprocessor.Process(shaky) }),
            5);

        Assert.Equal("一", preprocessed[0].Character);

        // The raw samples do not find 一 - and they used to find nothing usable at all, which is the
        // worse failure: the matcher now always answers with something, so what matters here is that
        // the answer is wrong.
        Assert.DoesNotContain("一", raw.Select(match => match.Character));
    }

    /// <summary>人 - two strokes, one corner: 31 raw sub-strokes, 2 after the pipeline.</summary>
    [Fact]
    public void HandwrittenTwoStrokeCharacterIsRecognized()
    {
        var matcher = new Matcher(Chinese());
        var strokes = StrokePreprocessor.Process(new IReadOnlyList<StrokePoint>[]
        {
            Hand(new[] { new StrokePoint(122, 42), new StrokePoint(112, 92), new StrokePoint(86, 150), new StrokePoint(40, 198) }),
            Hand(new[] { new StrokePoint(118, 104), new StrokePoint(146, 148), new StrokePoint(180, 180), new StrokePoint(216, 200) }),
        });

        var analysis = AnalyzedCharacter.FromStrokes(strokes);
        var results = matcher.Match(analysis, 5);

        Assert.Equal(2, analysis.StrokeCount);
        Assert.Equal(2, analysis.SubStrokeCount);
        Assert.Equal("人", results[0].Character);
        Assert.Equal("入", results[1].Character);
    }

    /// <summary>十 - a cross of two straight strokes: 32 raw sub-strokes, 2 after the pipeline.</summary>
    [Fact]
    public void HandwrittenCrossIsRecognized()
    {
        var matcher = new Matcher(Chinese());
        var strokes = StrokePreprocessor.Process(new IReadOnlyList<StrokePoint>[]
        {
            Hand(new[] { new StrokePoint(24, 128), new StrokePoint(232, 124) }),
            Hand(new[] { new StrokePoint(128, 36), new StrokePoint(128, 232) }),
        });

        var results = matcher.Match(AnalyzedCharacter.FromStrokes(strokes), 5);

        Assert.Equal("十", results[0].Character);
        Assert.Equal("丁", results[1].Character);
    }

    [Fact]
    public void ProcessIsIdempotentOnAlreadyCleanInput()
    {
        var clean = new RawStroke(new[]
        {
            new StrokePoint(20, 20),
            new StrokePoint(120, 24),
            new StrokePoint(220, 20),
        });

        var once = StrokePreprocessor.Process(clean);
        var twice = StrokePreprocessor.Process(once);

        Assert.Equal(once.Count, twice.Count);
        for (var i = 0; i < once.Count; ++i)
        {
            Assert.Equal(once[i].X, twice[i].X, 6);
            Assert.Equal(once[i].Y, twice[i].Y, 6);
        }
    }

    [Fact]
    public void ProcessHandlesEmptyAndSinglePointInput()
    {
        Assert.Empty(StrokePreprocessor.Process(Array.Empty<StrokePoint>()));
        Assert.Single(StrokePreprocessor.Process(new[] { new StrokePoint(1, 2) }));
    }

    [Fact]
    public void ProcessSeveralStrokesAtOnce()
    {
        var strokes = new[]
        {
            (IReadOnlyList<StrokePoint>)new[] { new StrokePoint(0, 0), new StrokePoint(60, 0) },
            (IReadOnlyList<StrokePoint>)new[] { new StrokePoint(0, 60), new StrokePoint(0, 120) },
        };

        var result = StrokePreprocessor.Process(strokes);

        Assert.Equal(2, result.Count);
        Assert.All(result, stroke => Assert.True(stroke.Count >= 2));
        Assert.Equal(
            "6",
            StrokePreprocessingOptions.Default.ResampleSpacing.ToString("0", CultureInfo.InvariantCulture));
    }
}
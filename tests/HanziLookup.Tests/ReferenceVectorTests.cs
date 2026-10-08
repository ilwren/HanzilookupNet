using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>
/// Compares the C# port against the output of the original JavaScript implementation
/// (<c>hanzilookup-js@1.0.3</c>) for every generated case.
/// </summary>
/// <remarks>
/// The integer parts of the analysis (pivot indexes, sub-stroke direction / length / centres, bounding
/// rectangle) must match exactly; score tables, curve roots and match scores are compared with a small
/// relative tolerance because the mathematical library functions of the .NET runtime and of V8 can
/// differ in the last bits. A mismatch therefore indicates a real porting problem.
/// </remarks>
public sealed class ReferenceVectorTests
{
    private const double Tolerance = 1e-9;

    private static readonly HanziData Data = HanziData.Load(TestPaths.Require(TestPaths.DataFile, "The character data"));

    private static readonly ReferenceVectors Vectors =
        ReferenceVectors.Load(TestPaths.Require(TestPaths.ReferenceVectorsFile, "The reference vectors"));

    public static TheoryData<string> CaseNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var referenceCase in Vectors.Cases)
            {
                data.Add(referenceCase.Name);
            }

            return data;
        }
    }

    public static TheoryData<string> CurveNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var curve in Vectors.Curves)
            {
                data.Add(curve.Name);
            }

            return data;
        }
    }

    private static ReferenceCase GetCase(string name) =>
        Vectors.Cases.FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"Reference case '{name}' does not exist.");

    [Fact]
    public void Vectors_were_generated_by_the_reference_implementation()
    {
        Assert.Contains("hanzilookup-js@1.0.3", Vectors.GeneratedBy);
        Assert.NotEmpty(Vectors.Cases);
    }

    [Fact]
    public void Character_data_matches_the_vectors()
    {
        Assert.Equal(Vectors.Data.CharacterCount, Data.Count);
        Assert.Equal(Vectors.Data.SubStrokeByteCount, Data.SubStrokes.Length);

        var hash = Convert.ToHexString(SHA256.HashData(Data.SubStrokes.Span.ToArray())).ToLowerInvariant();
        Assert.Equal(Vectors.Data.SubStrokeSha256, hash);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Analysis_matches_the_reference(string name)
    {
        var referenceCase = GetCase(name);
        var analyzed = new AnalyzedCharacter(referenceCase.RawStrokes);
        var expected = referenceCase.Expected;

        Assert.Equal(expected.Bounds[0], analyzed.Left);
        Assert.Equal(expected.Bounds[1], analyzed.Top);
        Assert.Equal(expected.Bounds[2], analyzed.Right);
        Assert.Equal(expected.Bounds[3], analyzed.Bottom);
        Assert.Equal(expected.SubStrokeCount, analyzed.SubStrokeCount);
        Assert.Equal(expected.Strokes.Count, analyzed.AnalyzedStrokes.Count);

        for (var i = 0; i < expected.Strokes.Count; ++i)
        {
            var expectedStroke = expected.Strokes[i];
            var actualStroke = analyzed.AnalyzedStrokes[i];

            Assert.Equal(expectedStroke.PivotIndexes, actualStroke.PivotIndexes);
            Assert.Equal(expectedStroke.SubStrokes.Count, actualStroke.SubStrokes.Count);

            for (var j = 0; j < expectedStroke.SubStrokes.Count; ++j)
            {
                var expectedSubStroke = expectedStroke.SubStrokes[j];
                var actualSubStroke = actualStroke.SubStrokes[j];

                Assert.Equal(expectedSubStroke.Direction, actualSubStroke.Direction);
                Assert.Equal(expectedSubStroke.Length, actualSubStroke.Length);
                Assert.Equal(expectedSubStroke.CenterX, actualSubStroke.CenterX);
                Assert.Equal(expectedSubStroke.CenterY, actualSubStroke.CenterY);
            }
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Matching_matches_the_reference(string name)
    {
        var referenceCase = GetCase(name);
        var matcher = new Matcher(Data, referenceCase.Looseness);
        var analyzed = new AnalyzedCharacter(referenceCase.RawStrokes);

        var matches = matcher.Match(analyzed, referenceCase.Limit);

        Assert.Equal(referenceCase.ExpectedMatches.Count, matches.Count);
        for (var i = 0; i < matches.Count; ++i)
        {
            Assert.Equal(referenceCase.ExpectedMatches[i].Character, matches[i].Character);
            AssertScoresEqual(referenceCase.ExpectedMatches[i].Score, matches[i].Score);
        }

        var counters = matcher.GetCounters();
        Assert.Equal(referenceCase.Counters.Characters, counters.CharactersChecked);
        Assert.Equal(referenceCase.Counters.SubStrokes, counters.SubStrokesCompared);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Strict_mode_only_removes_matches_with_non_finite_scores(string name)
    {
        var referenceCase = GetCase(name);
        var analyzed = new AnalyzedCharacter(referenceCase.RawStrokes);

        var compatible = new Matcher(Data, referenceCase.Looseness).Match(analyzed, referenceCase.Limit);
        var strict = new Matcher(Data, referenceCase.Looseness).Match(analyzed, referenceCase.Limit, MatchOptions.Strict);
        var expected = compatible.Where(m => m.HasFiniteScore).ToList();

        Assert.Equal(expected.Count, strict.Count);
        for (var i = 0; i < expected.Count; ++i)
        {
            Assert.Equal(expected[i].Character, strict[i].Character);
            AssertScoresEqual(expected[i].Score, strict[i].Score);
        }

        Assert.All(strict, m => Assert.True(m.HasFiniteScore));
    }

    [Fact]
    public void Empty_input_returns_no_matches_even_though_the_javascript_callback_is_never_invoked()
    {
        var referenceCase = GetCase("empty-input");
        Assert.False(referenceCase.CallbackInvoked);
        Assert.Empty(referenceCase.ExpectedMatches);

        var analyzed = new AnalyzedCharacter(referenceCase.RawStrokes);
        Assert.Empty(new Matcher(Data).Match(analyzed, 8));
    }

    [Fact]
    public void Score_tables_match_the_reference()
    {
        var matcher = new Matcher(Data);
        AssertScoresEqual(Vectors.DirectionScoreTable, matcher.DirectionScoreTable);
        AssertScoresEqual(Vectors.LengthScoreTable, matcher.LengthScoreTable);
        AssertScoresEqual(Vectors.PositionScoreTable, matcher.PositionScoreTable);
    }

    [Theory]
    [MemberData(nameof(CurveNames))]
    public void Cubic_curve_solver_matches_the_reference(string name)
    {
        var referenceCurve = Vectors.Curves.First(c => c.Name == name);
        var curve = new CubicCurve2D(
            referenceCurve.Arguments[0],
            referenceCurve.Arguments[1],
            referenceCurve.Arguments[2],
            referenceCurve.Arguments[3],
            referenceCurve.Arguments[4],
            referenceCurve.Arguments[5],
            referenceCurve.Arguments[6],
            referenceCurve.Arguments[7]);

        foreach (var sample in referenceCurve.Samples)
        {
            var solutions = curve.SolveForX(sample.X);
            Assert.Equal(sample.Solutions.Count, solutions.Length);
            for (var i = 0; i < solutions.Length; ++i)
            {
                AssertScoresEqual(sample.Solutions[i], solutions[i]);
            }

            AssertScoresEqual(sample.FirstSolution, curve.GetFirstSolutionForX(sample.X));
        }
    }

    [Fact]
    public void Decode_compact_matches_the_reference_bytes()
    {
        var json = File.ReadAllText(TestPaths.DataFile);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var base64 = document.RootElement.GetProperty("substrokes").GetString()!;

        var decoded = CompactDataDecoder.Decode(base64);

        Assert.Equal(Vectors.Data.SubStrokeByteCount, decoded.Length);
        var hash = Convert.ToHexString(SHA256.HashData(decoded)).ToLowerInvariant();
        Assert.Equal(Vectors.Data.SubStrokeSha256, hash);
        Assert.Equal(Vectors.Data.SubStrokeByteCount, Data.SubStrokes.Length);
    }

    private static void AssertScoresEqual(double expected, double actual)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual))
        {
            Assert.True(double.IsNaN(expected) && double.IsNaN(actual), $"expected {expected}, got {actual}");
            return;
        }

        if (double.IsInfinity(expected) || double.IsInfinity(actual))
        {
            Assert.Equal(expected, actual);
            return;
        }

        var scale = Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        Assert.True(
            Math.Abs(expected - actual) <= Tolerance * scale,
            $"expected {expected:R}, got {actual:R} (difference {Math.Abs(expected - actual):R})");
    }

    private static void AssertScoresEqual(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; ++i)
        {
            AssertScoresEqual(expected[i], actual[i]);
        }
    }
}

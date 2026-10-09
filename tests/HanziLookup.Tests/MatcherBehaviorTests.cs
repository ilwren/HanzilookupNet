using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>
/// Behavioural tests of <see cref="Matcher"/> that do not depend on the JavaScript reference: input
/// validation, ordering, limits, the async entry point and cancellation.
/// </summary>
/// <remarks>
/// Some expectations are exact numbers of compared characters; those depend on the shipped
/// <c>mmah.json</c> data file, whose contents are pinned by <see cref="ReferenceVectorTests"/>.
/// </remarks>
public sealed class MatcherBehaviorTests
{
    private static readonly HanziData Data = HanziData.Load(TestPaths.Require(TestPaths.DataFile, "The character data"));

    private static AnalyzedCharacter OneStroke() =>
        new(new[] { RawStroke.FromPoints((32, 128), (224, 128)) });

    private static AnalyzedCharacter ThreeStrokes() =>
        new(new[]
        {
            RawStroke.FromPoints((40, 40), (210, 40)),
            RawStroke.FromPoints((210, 40), (210, 210)),
            RawStroke.FromPoints((40, 210), (40, 40))
        });

    [Fact]
    public void Matches_are_ordered_by_descending_score_and_respect_the_limit()
    {
        var matcher = new Matcher(Data);
        var matches = matcher.Match(OneStroke(), 5);

        Assert.Equal(5, matches.Count);
        for (var i = 1; i < matches.Count; ++i)
        {
            Assert.True(
                matches[i - 1].Score >= matches[i].Score,
                $"matches must be sorted: {matches[i - 1]} before {matches[i]}");
        }
    }

    [Fact]
    public void The_best_match_for_a_horizontal_stroke_is_the_horizontal_character()
    {
        var matches = new Matcher(Data).Match(OneStroke(), 8);

        Assert.Equal("一", matches[0].Character);
        Assert.Equal(1.0199310536683939, matches[0].Score, 9);
    }

    [Fact]
    public void The_best_match_for_a_vertical_stroke_is_the_vertical_character()
    {
        var matches = new Matcher(Data).Match(new AnalyzedCharacter(new[] { RawStroke.FromPoints((128, 24), (128, 232)) }), 8);

        Assert.Equal("丨", matches[0].Character);
        Assert.Equal(0.9789703620716774, matches[0].Score, 9);
    }

    [Fact]
    public void Uncompared_candidates_are_returned_with_a_negative_infinity_score_by_default()
    {
        // The sub-stroke pre-filter of the JavaScript implementation is inert, so when fewer
        // characters pass the stroke-count filter than the requested limit, the result is padded
        // with candidates that were never actually compared (-Infinity score). MatchOptions.Strict
        // removes exactly those entries.
        var analyzed = OneStroke();

        var compatible = new Matcher(Data).Match(analyzed, 8);
        var strict = new Matcher(Data).Match(analyzed, 8, MatchOptions.Strict);

        // Only the eight characters of the data file that consist of a single stroke pass the filter.
        Assert.Equal(8, compatible.Count);
        Assert.Equal(3, compatible.Count(m => m.HasFiniteScore));
        Assert.Equal(3, strict.Count);
        Assert.All(strict, m => Assert.True(m.HasFiniteScore));

        // ... and the finite part of both results is identical.
        Assert.Equal(
            compatible.Where(m => m.HasFiniteScore).Select(m => m.Character),
            strict.Select(m => m.Character));
    }

    /// <summary>
    /// Input that nothing passes the pre-filter for must still come back with candidates.
    /// </summary>
    /// <remarks>
    /// At the default looseness a one-stroke input only looks at one-stroke characters. A character
    /// written in one continuous drag (连笔) is exactly that - one stroke holding a dozen sub-strokes -
    /// and this repository has no one-stroke character at all, so the fast pass finds nothing that was
    /// really compared. An empty candidate list is not a usable answer, so the matcher retries
    /// without the pre-filter.
    /// </remarks>
    [Fact]
    public void Input_the_pre_filter_rejects_outright_still_gets_candidates()
    {
        var repository = new HanziData(
            new[] { new HanziCharacter("鬱", 12, 12, 0), new HanziCharacter("龘", 12, 12, 36) },
            SyntheticSubStrokes(24));
        var matcher = new Matcher(repository);
        var input = new AnalyzedCharacter(new[] { RawStroke.FromPoints((20, 20), (140, 140)) });

        var results = matcher.Match(input, 4);

        Assert.NotEmpty(results);
        Assert.All(results, match => Assert.True(match.HasFiniteScore));
        Assert.Contains(results, match => match.Character == "鬱" || match.Character == "龘");
    }

    /// <summary>A sub-stroke table of <paramref name="count"/> synthetic entries.</summary>
    private static byte[] SyntheticSubStrokes(int count)
    {
        var bytes = new byte[count * 3];
        for (var i = 0; i < count; ++i)
        {
            bytes[i * 3] = (byte)(10 + i * 3);
            bytes[i * 3 + 1] = (byte)(200 - i * 2);
            bytes[i * 3 + 2] = (byte)(0x11 + i);
        }

        return bytes;
    }

    [Fact]
    public void An_empty_input_finds_nothing()
    {
        var matcher = new Matcher(Data);

        Assert.Empty(matcher.Match(AnalyzedCharacter.Empty, 8));
        Assert.Equal(MatcherCounters.Zero, matcher.GetCounters());
    }

    [Fact]
    public void Looseness_controls_how_many_candidates_are_compared()
    {
        var analyzed = ThreeStrokes();

        var strictMatcher = new Matcher(Data) { Looseness = 0 };
        strictMatcher.Match(analyzed, 8);

        var defaultMatcher = new Matcher(Data);
        defaultMatcher.Match(analyzed, 8);

        var permissiveMatcher = new Matcher(Data) { Looseness = 1 };
        permissiveMatcher.Match(analyzed, 8);

        Assert.Equal(87, strictMatcher.GetCounters().CharactersChecked);
        Assert.Equal(299, defaultMatcher.GetCounters().CharactersChecked);
        Assert.Equal(Data.Count, permissiveMatcher.GetCounters().CharactersChecked);
    }

    [Fact]
    public void The_constructor_coerces_zero_looseness_to_the_default_like_javascript_does()
    {
        var matcher = new Matcher(Data, 0);

        Assert.Equal(Matcher.DefaultLooseness, matcher.Looseness, 12);
    }

    [Fact]
    public void The_looseness_property_accepts_zero()
    {
        var matcher = new Matcher(Data) { Looseness = 0 };

        Assert.Equal(0, matcher.Looseness, 12);
    }

    [Fact]
    public async Task MatchAsync_returns_the_same_results_as_Match()
    {
        var analyzed = ThreeStrokes();
        var synchronous = new Matcher(Data).Match(analyzed, 8);
        var asynchronous = await new Matcher(Data).MatchAsync(analyzed, 8);

        Assert.Equal(synchronous.Count, asynchronous.Count);
        for (var i = 0; i < synchronous.Count; ++i)
        {
            Assert.Equal(synchronous[i].Character, asynchronous[i].Character);
            Assert.Equal(synchronous[i].Score, asynchronous[i].Score);
        }
    }

    [Fact]
    public async Task MatchAsync_honours_an_already_cancelled_token()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new Matcher(Data).MatchAsync(OneStroke(), 8, MatchOptions.Default, source.Token));
    }

    [Fact]
    public void Counters_report_that_characters_were_compared()
    {
        var matcher = new Matcher(Data);
        matcher.Match(OneStroke(), 8);

        var counters = matcher.GetCounters();
        Assert.True(counters.CharactersChecked > 0);
        Assert.True(counters.SubStrokesCompared > 0);
        Assert.True(counters.CharactersChecked <= Data.Count);
    }

    [Fact]
    public void MatchCollector_keeps_the_best_and_removes_duplicates()
    {
        var collector = new MatchCollector(3);

        collector.FileMatch(new CharacterMatch("一", 1.0));
        collector.FileMatch(new CharacterMatch("二", 0.5));
        collector.FileMatch(new CharacterMatch("三", 0.9));
        collector.FileMatch(new CharacterMatch("一", 0.2)); // worse duplicate: ignored
        collector.FileMatch(new CharacterMatch("一", 1.5)); // better duplicate: replaces
        collector.FileMatch(new CharacterMatch("四", 0.1)); // worse than the current minimum: ignored

        var matches = collector.GetMatches();
        Assert.Equal(new[] { "一", "三", "二" }, matches.Select(m => m.Character));
        Assert.Equal(3, collector.Limit);
        Assert.Equal(3, collector.Count);
    }

    [Fact]
    public void Matchers_can_be_shared_between_threads()
    {
        var matcher = new Matcher(Data);

        var results = Enumerable.Range(0, 8)
            .AsParallel()
            .Select(_ => matcher.Match(ThreeStrokes(), 4))
            .ToList();

        Assert.Equal(8, results.Count);
        Assert.All(results, r => Assert.Equal(results[0][0].Character, r[0].Character));
    }

    [Fact]
    public void A_matcher_can_be_created_from_the_data_store()
    {
        const string name = "unit-test-data";
        HanziDataStore.Register(name, Data);
        try
        {
            var matcher = new Matcher(name);
            Assert.Same(Data, matcher.Data);
            Assert.True(HanziDataStore.TryGet(name, out var fetched));
            Assert.Same(Data, fetched);
            Assert.Contains(name, HanziDataStore.Names);
        }
        finally
        {
            Assert.True(HanziDataStore.Remove(name));
        }

        Assert.Throws<KeyNotFoundException>(() => new Matcher(name));
    }
}

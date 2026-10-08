using System.Collections.Generic;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>
/// Hand-checked expectations for the stroke analysis: they pin down the quantization of sub-strokes
/// (direction / length / centre) and the handling of degenerate strokes.
/// </summary>
public sealed class AnalysisTests
{
    private static IReadOnlyList<IReadOnlyList<StrokePoint>> Many(params IReadOnlyList<StrokePoint>[] strokes) => strokes;

    [Fact]
    public void Horizontal_stroke_is_quantized_as_expected()
    {
        var analyzed = new AnalyzedCharacter(Many(RawStroke.FromPoints((32, 128), (224, 128))));

        Assert.False(analyzed.IsEmpty);
        Assert.Equal(1, analyzed.StrokeCount);
        Assert.Equal(1, analyzed.SubStrokeCount);

        var stroke = analyzed.AnalyzedStrokes[0];
        Assert.Equal(new[] { 0, 1 }, stroke.PivotIndexes);

        var subStroke = Assert.Single(stroke.SubStrokes);
        Assert.Equal(0, subStroke.Direction); // drawn towards the right
        Assert.Equal(180, subStroke.Length);  // 192 / (192 * sqrt(2)) * 255
        Assert.Equal(8, subStroke.CenterX);   // centre of the box, 0.5 * 15 rounded
        Assert.Equal(8, subStroke.CenterY);
    }

    [Fact]
    public void Vertical_stroke_is_quantized_as_expected()
    {
        var analyzed = new AnalyzedCharacter(Many(RawStroke.FromPoints((128, 24), (128, 128), (128, 232))));

        var subStroke = Assert.Single(analyzed.AnalyzedStrokes[0].SubStrokes);
        Assert.Equal(192, subStroke.Direction); // drawn downwards
        Assert.Equal(180, subStroke.Length);
        Assert.Equal(8, subStroke.CenterX);
        Assert.Equal(8, subStroke.CenterY);
    }

    [Fact]
    public void Pivot_detection_splits_a_right_angle_into_two_sub_strokes()
    {
        var analyzed = new AnalyzedCharacter(Many(RawStroke.FromPoints((40, 40), (210, 40), (210, 210))));

        Assert.Equal(2, analyzed.SubStrokeCount);

        var stroke = analyzed.AnalyzedStrokes[0];
        Assert.Equal(new[] { 0, 1, 2 }, stroke.PivotIndexes);
        Assert.Equal(0, stroke.SubStrokes[0].Direction);
        Assert.Equal(192, stroke.SubStrokes[1].Direction);
        Assert.Equal(8, stroke.SubStrokes[0].CenterX);
        Assert.Equal(15, stroke.SubStrokes[1].CenterX);
    }

    [Fact]
    public void A_stroke_with_many_points_is_simplified_to_few_sub_strokes()
    {
        var stroke = new RawStroke();
        for (var i = 0; i <= 120; ++i)
        {
            // A straight line sampled densely: the pivot detection must not produce 120 sub-strokes.
            stroke.Add(30 + i, 40 + i * 0.25);
        }

        var analyzed = new AnalyzedCharacter(Many(stroke));

        Assert.Equal(1, analyzed.SubStrokeCount);
    }

    [Fact]
    public void Each_raw_stroke_becomes_one_analyzed_stroke()
    {
        var analyzed = new AnalyzedCharacter(Many(
            RawStroke.FromPoints((40, 40), (210, 40)),
            RawStroke.FromPoints((210, 40), (210, 210)),
            RawStroke.FromPoints((210, 210), (40, 210)),
            RawStroke.FromPoints((40, 210), (40, 40))));

        Assert.Equal(4, analyzed.StrokeCount);
        Assert.Equal(4, analyzed.SubStrokeCount);
        Assert.Equal(new[] { 0, 192, 128, 64 }, new[]
        {
            analyzed.AnalyzedStrokes[0].SubStrokes[0].Direction,
            analyzed.AnalyzedStrokes[1].SubStrokes[0].Direction,
            analyzed.AnalyzedStrokes[2].SubStrokes[0].Direction,
            analyzed.AnalyzedStrokes[3].SubStrokes[0].Direction
        });
    }

    [Fact]
    public void Empty_input_has_no_strokes_and_default_bounds()
    {
        var analyzed = new AnalyzedCharacter(System.Array.Empty<IReadOnlyList<StrokePoint>>());

        Assert.True(analyzed.IsEmpty);
        Assert.Equal(0, analyzed.SubStrokeCount);
        Assert.Empty(analyzed.FlattenedSubStrokes);
        Assert.Equal(0.0, analyzed.Left);
        Assert.Equal(0.0, analyzed.Top);
        Assert.Equal(256.0, analyzed.Right);
        Assert.Equal(256.0, analyzed.Bottom);
    }

    [Fact]
    public void Degenerate_strokes_are_skipped_instead_of_producing_NaN()
    {
        // A tap without movement, and a stroke whose points all coincide: the reference
        // implementation produces NaNs for these, this port ignores them.
        var singlePoint = new AnalyzedCharacter(Many(RawStroke.FromPoints((50, 50))));
        var coincident = new AnalyzedCharacter(Many(RawStroke.FromPoints((50, 50), (50, 50), (50, 50))));

        Assert.True(singlePoint.IsEmpty);
        Assert.True(coincident.IsEmpty);
    }

    [Fact]
    public void A_degenerate_stroke_still_contributes_to_the_bounding_rectangle()
    {
        // The bounding rectangle is computed over all captured points, exactly like the original,
        // so a stray tap moves it (and therefore the normalization) even though it is not analysed.
        var analyzed = new AnalyzedCharacter(Many(
            RawStroke.FromPoints((50, 50), (50, 50)),
            RawStroke.FromPoints((32, 128), (224, 128))));

        Assert.Equal(1, analyzed.StrokeCount);
        Assert.Equal(50.0, analyzed.Top);
        Assert.Equal(11, analyzed.AnalyzedStrokes[0].SubStrokes[0].CenterY);
    }

    [Fact]
    public void FromStrokes_accepts_the_collection_used_by_the_session()
    {
        var strokes = new List<RawStroke>
        {
            new(new[] { new StrokePoint(32, 128), new StrokePoint(224, 128) })
        };

        var analyzed = AnalyzedCharacter.FromStrokes(strokes);

        Assert.Equal(1, analyzed.StrokeCount);
        Assert.Equal(1, analyzed.SubStrokeCount);
    }

    [Fact]
    public void RawStroke_collects_points_and_reports_its_extent()
    {
        var stroke = new RawStroke(4);
        stroke.Add(new StrokePoint(1, 2));
        stroke.Add(3, 4);

        Assert.Equal(2, stroke.Count);
        Assert.Equal(new StrokePoint(1, 2), stroke[0]);
        Assert.Equal(new StrokePoint(3, 4), stroke[1]);
        Assert.Equal(2, stroke.Points.Count);
        Assert.Equal(System.Math.Sqrt(8), stroke[0].DistanceTo(stroke[1]), 10);

        stroke.Clear();
        Assert.Empty(stroke);
    }
}

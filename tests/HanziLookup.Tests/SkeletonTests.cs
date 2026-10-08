using System;
using System.Linq;
using Xunit;

namespace HanziLookup.Tests;

/// <summary>
/// Tests of the segment reconstruction used for drawing: a sub-stroke descriptor must expand back
/// into a segment that has the direction and length it was computed from (within the quantization
/// step), and every repository character must expand into exactly as many segments as it stores
/// sub-strokes.
/// </summary>
public sealed class SkeletonTests
{
    private static readonly HanziData Data = HanziData.Load(TestPaths.Require(TestPaths.DataFile, "The character data"));

    private static readonly ReferenceVectors Vectors =
        ReferenceVectors.Load(TestPaths.Require(TestPaths.ReferenceVectorsFile, "The reference vectors"));

    [Fact]
    public void A_horizontal_segment_is_reconstructed_horizontally()
    {
        var segment = StrokeSkeleton.FromSubStroke(new SubStroke(0, 180, 8, 8));

        // The centre is stored as 8/15 = 0.5333 (the exact 0.5 does not fit the nibble scaling) and
        // the length as 180/255 of the diagonal, i.e. ~0.998 of the side of the square.
        Assert.Equal(0.0342, segment.Start.X, 3);
        Assert.Equal(0.5333, segment.Start.Y, 3);
        Assert.Equal(1.0324, segment.End.X, 3);
        Assert.Equal(0.5333, segment.End.Y, 3);
    }

    [Fact]
    public void A_vertical_segment_is_reconstructed_vertically()
    {
        // Direction 192 is "downwards" in the coordinate system of the captured points.
        var segment = StrokeSkeleton.FromSubStroke(new SubStroke(192, 180, 8, 8));

        Assert.Equal(0.5333, segment.Start.X, 3);
        Assert.Equal(0.0342, segment.Start.Y, 3);
        Assert.Equal(0.5333, segment.End.X, 3);
        Assert.Equal(1.0324, segment.End.Y, 3);
    }

    [Fact]
    public void The_reconstructed_direction_matches_the_geometry_of_the_pivot_points()
    {
        // One quantization step of the direction is PI/128 radians (~1.4 degrees); a faithful
        // reconstruction must stay within half of that.
        var tolerance = Math.PI / 256 + 1e-6;
        var checkedSegments = 0;

        foreach (var referenceCase in Vectors.Cases)
        {
            var analyzed = new AnalyzedCharacter(referenceCase.RawStrokes);
            foreach (var stroke in analyzed.AnalyzedStrokes)
            {
                var pivots = stroke.PivotIndexes;
                var segments = StrokeSkeleton.FromSubStrokes(stroke.SubStrokes);

                Assert.Equal(stroke.SubStrokes.Count, segments.Count);

                var previousPivot = 0;
                for (var i = 0; i < segments.Count; ++i)
                {
                    var pivotIndex = pivots.First(p => p > previousPivot);
                    var start = stroke.Points[previousPivot];
                    var end = stroke.Points[pivotIndex];

                    var expected = Math.Atan2(end.Y - start.Y, end.X - start.X);
                    var actual = Math.Atan2(
                        segments[i].End.Y - segments[i].Start.Y,
                        segments[i].End.X - segments[i].Start.X);

                    var difference = Math.Abs(Normalize(expected - actual));
                    Assert.True(
                        difference <= tolerance,
                        $"{referenceCase.Name}: segment {i} points {DifferenceInDegrees(difference)} deg off " +
                        $"(tolerance {DifferenceInDegrees(tolerance)} deg)");

                    checkedSegments++;
                    previousPivot = pivotIndex;
                }
            }
        }

        Assert.True(checkedSegments > 100, $"expected to check many segments, checked {checkedSegments}");
    }

    [Fact]
    public void Every_repository_character_expands_to_its_stored_number_of_segments()
    {
        var checkedCharacters = 0;
        foreach (var character in Data.Characters)
        {
            var segments = StrokeSkeleton.FromRepositoryCharacter(Data, character);
            Assert.Equal(character.SubStrokeCount, segments.Count);
            checkedCharacters++;

            foreach (var segment in segments)
            {
                Assert.InRange(segment.Start.X, -0.5, 1.5);
                Assert.InRange(segment.Start.Y, -0.5, 1.5);
                Assert.InRange(segment.End.X, -0.5, 1.5);
                Assert.InRange(segment.End.Y, -0.5, 1.5);
            }
        }

        Assert.Equal(Data.Count, checkedCharacters);
    }

    [Fact]
    public void The_skeleton_of_a_known_character_has_the_expected_shape()
    {
        var entry = Data.Find("一");
        Assert.NotNull(entry);

        var segments = StrokeSkeleton.FromRepositoryCharacter(Data, entry.Value);

        var segment = Assert.Single(segments);
        Assert.Equal(0, segment.Direction); // drawn towards the right
        Assert.True(Math.Abs(segment.End.X - segment.Start.X) > 0.9);
        Assert.True(Math.Abs(segment.End.Y - segment.Start.Y) < 0.05);
        Assert.Equal(7.0 / 15.0, segment.Center.X, 6);
        Assert.Equal(7.0 / 15.0, segment.Center.Y, 6);
    }

    [Fact]
    public void A_multi_stroke_character_expands_to_several_segments()
    {
        var entry = Data.Find("學");
        Assert.NotNull(entry);
        Assert.Equal(25, entry.Value.SubStrokeCount);

        var segments = StrokeSkeleton.FromRepositoryCharacter(Data, entry.Value);

        Assert.Equal(25, segments.Count);
        Assert.True(segments.Select(s => s.Direction).Distinct().Count() > 4);
    }

    [Fact]
    public void Unknown_characters_have_no_skeleton()
    {
        Assert.Null(Data.Find("A"));
        Assert.Null(Data.Find(""));
        Assert.Null(Data.Find("XYZ"));
    }

    [Fact]
    public void The_analysis_of_a_captured_character_expands_to_its_sub_strokes()
    {
        var analyzed = new AnalyzedCharacter(new[]
        {
            RawStroke.FromPoints((40, 40), (210, 40)),
            RawStroke.FromPoints((210, 40), (210, 210))
        });

        var segments = StrokeSkeleton.FromAnalyzedCharacter(analyzed);

        Assert.Equal(analyzed.SubStrokeCount, segments.Count);
        Assert.Equal(2, segments.Count);
    }

    private static double Normalize(double radians)
    {
        var twoPi = Math.PI * 2;
        var value = radians % twoPi;
        if (value > Math.PI)
        {
            value -= twoPi;
        }
        else if (value < -Math.PI)
        {
            value += twoPi;
        }

        return value;
    }

    private static double DifferenceInDegrees(double radians) => radians * 180.0 / Math.PI;
}

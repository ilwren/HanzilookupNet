using Avalonia;
using Avalonia.Media;
using HanziLookup;

namespace HanziLookup.Avalonia;

/// <summary>
/// Drawing helpers shared by the controls of this package: the 米字格 guide grid, captured strokes,
/// reconstructed sub-stroke skeletons, bounding boxes and pivot markers.
/// </summary>
/// <remarks>
/// Everything is expressed in the coordinate space of the recognizer (a square, 256 x 256 by
/// default); the helpers scale it onto the rectangle they are given, so a control can render at any
/// size.
/// </remarks>
public static class StrokeAnalysisRenderer
{
    /// <summary>Draws the dashed guide grid (border, diagonals and centre lines) of a writing square.</summary>
    public static void DrawGrid(DrawingContext context, Rect rect, IBrush brush, double thickness = 0.5)
    {
        var pen = new Pen(brush, thickness, new DashStyle(new double[] { 4, 4 }, 0));

        context.DrawRectangle(null, pen, rect);
        context.DrawLine(pen, rect.TopLeft, rect.BottomRight);
        context.DrawLine(pen, rect.TopRight, rect.BottomLeft);
        context.DrawLine(pen, new Point(rect.Center.X, rect.Top), new Point(rect.Center.X, rect.Bottom));
        context.DrawLine(pen, new Point(rect.Left, rect.Center.Y), new Point(rect.Right, rect.Center.Y));
    }

    /// <summary>Converts a point of the recognizer's coordinate space onto a rectangle.</summary>
    public static Point ToControl(StrokePoint point, Rect rect, double coordinateSpace = 256.0)
    {
        if (coordinateSpace <= 0)
        {
            return rect.TopLeft;
        }

        var scaleX = rect.Width / coordinateSpace;
        var scaleY = rect.Height / coordinateSpace;
        return new Point(rect.X + point.X * scaleX, rect.Y + point.Y * scaleY);
    }

    /// <summary>Converts a normalized (0..1) point onto a rectangle.</summary>
    public static Point ToControlNormalized(StrokePoint point, Rect rect)
        => new(rect.X + point.X * rect.Width, rect.Y + point.Y * rect.Height);

    /// <summary>Draws a set of captured strokes.</summary>
    public static void DrawStrokes(
        DrawingContext context,
        Rect rect,
        IEnumerable<RawStroke> strokes,
        IBrush brush,
        double thickness = 3.0,
        double coordinateSpace = 256.0)
    {
        var pen = new Pen(brush, thickness, null, PenLineCap.Round, PenLineJoin.Round);

        foreach (var stroke in strokes)
        {
            DrawStroke(context, rect, stroke, pen, coordinateSpace);
        }
    }

    /// <summary>Draws a single captured stroke.</summary>
    public static void DrawStroke(
        DrawingContext context,
        Rect rect,
        IReadOnlyList<StrokePoint> points,
        IPen pen,
        double coordinateSpace = 256.0,
        int pointCount = -1)
    {
        var count = pointCount < 0 || pointCount > points.Count ? points.Count : pointCount;
        for (var i = 1; i < count; ++i)
        {
            context.DrawLine(pen, ToControl(points[i - 1], rect, coordinateSpace), ToControl(points[i], rect, coordinateSpace));
        }

        // A single captured point (or a very short stroke) is still worth showing.
        if (count == 1)
        {
            var pt = ToControl(points[0], rect, coordinateSpace);
            context.DrawEllipse(pen.Brush, null, pt, pen.Thickness / 2, pen.Thickness / 2);
        }
    }

    /// <summary>Draws the reconstructed skeleton of sub-strokes (normalized 0..1 coordinates).</summary>
    public static void DrawSkeleton(
        DrawingContext context,
        Rect rect,
        IReadOnlyList<SkeletonSegment> segments,
        IBrush? lineBrush = null,
        IBrush? markerBrush = null,
        double thickness = 1.5)
    {
        var linePen = lineBrush is null ? null : new Pen(lineBrush, thickness, null, PenLineCap.Round);
        var markerFill = markerBrush;

        foreach (var segment in segments)
        {
            var start = ToControlNormalized(segment.Start, rect);
            var end = ToControlNormalized(segment.End, rect);

            if (linePen is not null)
            {
                context.DrawLine(linePen, start, end);
            }

            if (markerFill is not null)
            {
                var radius = Math.Max(1.5, thickness);
                context.DrawEllipse(markerFill, null, start, radius, radius);
                context.DrawEllipse(markerFill, null, end, radius, radius);
            }
        }
    }

    /// <summary>Draws the bounding rectangle of an analyzed character.</summary>
    public static void DrawBoundingBox(
        DrawingContext context,
        Rect rect,
        AnalyzedCharacter analyzedCharacter,
        IBrush brush,
        double thickness = 1.0,
        double coordinateSpace = 256.0)
    {
        var topLeft = ToControl(new StrokePoint(analyzedCharacter.Left, analyzedCharacter.Top), rect, coordinateSpace);
        var bottomRight = ToControl(new StrokePoint(analyzedCharacter.Right, analyzedCharacter.Bottom), rect, coordinateSpace);
        var pen = new Pen(brush, thickness, new DashStyle(new double[] { 3, 3 }, 0));
        context.DrawRectangle(null, pen, new Rect(topLeft, bottomRight));
    }

    /// <summary>Draws a small marker on every pivot point that the analysis detected.</summary>
    public static void DrawPivots(
        DrawingContext context,
        Rect rect,
        AnalyzedCharacter analyzedCharacter,
        IBrush brush,
        double radius = 3.0,
        double coordinateSpace = 256.0)
    {
        foreach (var stroke in analyzedCharacter.AnalyzedStrokes)
        {
            foreach (var index in stroke.PivotIndexes)
            {
                if (index < 0 || index >= stroke.Points.Count)
                {
                    continue;
                }

                var pt = ToControl(stroke.Points[index], rect, coordinateSpace);
                context.DrawEllipse(brush, null, pt, radius, radius);
            }
        }
    }

    /// <summary>Draws the analysis of a character: bounding box, skeleton and pivot markers.</summary>
    public static void DrawAnalysis(
        DrawingContext context,
        Rect rect,
        AnalyzedCharacter analyzedCharacter,
        IBrush skeletonBrush,
        IBrush pivotBrush,
        IBrush boundingBoxBrush,
        double coordinateSpace = 256.0)
    {
        DrawBoundingBox(context, rect, analyzedCharacter, boundingBoxBrush, 1.0, coordinateSpace);
        DrawSkeleton(context, rect, StrokeSkeleton.FromAnalyzedCharacter(analyzedCharacter), skeletonBrush, null, 1.5);
        DrawPivots(context, rect, analyzedCharacter, pivotBrush, 3.0, coordinateSpace);
    }
}

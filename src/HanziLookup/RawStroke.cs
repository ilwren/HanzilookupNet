using System.Collections;

namespace HanziLookup;

/// <summary>
/// A mutable stroke as captured from a pointing device: an ordered list of <see cref="StrokePoint"/>s
/// that are recorded between pointer-down and pointer-up.
/// </summary>
/// <remarks>
/// Only <see cref="AnalyzedCharacter"/> is needed by the recognizer; this type exists so that
/// interactive front-ends (and the Avalonia control in this repository) have a natural place to
/// collect points in.
/// </remarks>
public sealed class RawStroke : IReadOnlyList<StrokePoint>
{
    private readonly List<StrokePoint> _points;

    /// <summary>Creates an empty stroke.</summary>
    public RawStroke() => _points = new List<StrokePoint>();

    /// <summary>Creates a stroke from a sequence of points.</summary>
    public RawStroke(IEnumerable<StrokePoint> points) => _points = new List<StrokePoint>(points ?? throw new ArgumentNullException(nameof(points)));

    /// <summary>Creates an empty stroke with an initial capacity.</summary>
    public RawStroke(int capacity) => _points = new List<StrokePoint>(capacity);

    /// <summary>The points captured so far.</summary>
    public IReadOnlyList<StrokePoint> Points => _points;

    /// <summary>Number of points in the stroke.</summary>
    public int Count => _points.Count;

    /// <summary>Gets the point at <paramref name="index"/>.</summary>
    public StrokePoint this[int index] => _points[index];

    /// <summary>Appends a point.</summary>
    public void Add(StrokePoint point) => _points.Add(point);

    /// <summary>Appends a point given by its coordinates.</summary>
    public void Add(double x, double y) => _points.Add(new StrokePoint(x, y));

    /// <summary>Removes all points.</summary>
    public void Clear() => _points.Clear();

    /// <summary>Removes the point at <paramref name="index"/>.</summary>
    public void RemoveAt(int index) => _points.RemoveAt(index);

    /// <summary>Creates a stroke from raw coordinate pairs, e.g. <c>RawStroke.FromCoordinates(new[] { new[] { 10d, 20d } })</c>.</summary>
    public static RawStroke FromCoordinates(IEnumerable<IReadOnlyList<double>> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        var stroke = new RawStroke();
        foreach (var pair in coordinates)
        {
            stroke.Add(pair[0], pair[1]);
        }

        return stroke;
    }

    /// <summary>Creates a stroke from <c>(x, y)</c> tuples.</summary>
    public static RawStroke FromPoints(params (double X, double Y)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var stroke = new RawStroke(points.Length);
        foreach (var (x, y) in points)
        {
            stroke.Add(x, y);
        }

        return stroke;
    }

    /// <inheritdoc />
    public IEnumerator<StrokePoint> GetEnumerator() => _points.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

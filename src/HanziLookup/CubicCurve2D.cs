namespace HanziLookup;

/// <summary>
/// A 2D cubic Bézier curve and the solving helper the matcher uses to convert a "looseness" value
/// into a search range for the number of strokes / sub-strokes.
/// </summary>
/// <remarks>
/// Direct port of the <c>CubicCurve2D</c> class of the JavaScript implementation (where it is a
/// function returning an object literal). The root solver is the closed form solution of the
/// reduced cubic; it is deliberately kept as-is, including its handling of the degenerate cases,
/// because the score tables of <see cref="Matcher"/> are derived from it and must match the
/// JavaScript results bit for bit.
/// </remarks>
public sealed class CubicCurve2D
{
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _ctrlX1;
    private readonly double _ctrlY1;
    private readonly double _ctrlX2;
    private readonly double _ctrlY2;
    private readonly double _x2;
    private readonly double _y2;

    /// <summary>Creates a cubic Bézier curve from its two anchor and two control points.</summary>
    public CubicCurve2D(
        double x1,
        double y1,
        double ctrlX1,
        double ctrlY1,
        double ctrlX2,
        double ctrlY2,
        double x2,
        double y2)
    {
        _x1 = x1;
        _y1 = y1;
        _ctrlX1 = ctrlX1;
        _ctrlY1 = ctrlY1;
        _ctrlX2 = ctrlX2;
        _ctrlY2 = ctrlY2;
        _x2 = x2;
        _y2 = y2;
    }

    /// <summary>The x coordinate of the first anchor point.</summary>
    public double X1 => _x1;

    /// <summary>The x coordinate of the second anchor point.</summary>
    public double X2 => _x2;

    /// <summary>Evaluates the y coordinate of the curve at parameter <paramref name="t"/>.</summary>
    public double GetYOnCurve(double t)
    {
        var ay = GetCubicAy();
        var by = GetCubicBy();
        var cy = GetCubicCy();
        var tSquared = t * t;
        var tCubed = t * tSquared;
        return ay * tCubed + by * tSquared + cy * t + _y1;
    }

    /// <summary>
    /// Solves the curve for <paramref name="x"/>: returns the parameters <c>t</c> where the curve's
    /// x coordinate equals <paramref name="x"/> (one or three values; empty when there is no real root).
    /// </summary>
    public double[] SolveForX(double x)
    {
        // "a" and "b" refer to the coefficients of the reduced cubic, not to the control points.
        var a = GetCubicAx();
        var b = GetCubicBx();
        var c = GetCubicCx();
        var d = _x1 - x;
        var f = (3.0 * c / a - b * b / (a * a)) / 3.0;
        var g = (2.0 * b * b * b / (a * a * a) - 9.0 * b * c / (a * a) + 27.0 * d / a) / 27.0;
        var h = g * g / 4.0 + f * f * f / 27.0;

        if (h > 0)
        {
            // There is only one real root.
            var u = 0 - g;
            var r = u / 2 + Math.Pow(h, 0.5);
            var s8 = Math.Pow(r, 0.333333333333333333333333333);
            var t8 = u / 2 - Math.Pow(h, 0.5);
            var v8 = Math.Pow(0 - t8, 0.33333333333333333333);
            var x3 = s8 - v8 - b / (3 * a);
            return new[] { x3 };
        }

        if (f == 0.0 && g == 0.0 && h == 0.0)
        {
            // All three roots are real and equal.
            return new[] { -Math.Pow(d / a, 1.0 / 3.0) };
        }

        // All three roots are real (h <= 0).
        var i = Math.Sqrt(g * g / 4.0 - h);
        var j = Math.Pow(i, 1.0 / 3.0);
        var k = Math.Acos(-g / (2 * i));
        var l = j * -1;
        var m = Math.Cos(k / 3.0);
        var n = Math.Sqrt(3.0) * Math.Sin(k / 3.0);
        var p = b / (3.0 * a) * -1;
        return new[]
        {
            2.0 * j * Math.Cos(k / 3.0) - b / (3.0 * a),
            l * (m + n) + p,
            l * (m - n) + p
        };
    }

    /// <summary>
    /// Returns the first solution of <see cref="SolveForX(double)"/> that lies in the curve's
    /// parameter range (0..1, with a small tolerance), clamped to the range; NaN when there is none.
    /// </summary>
    public double GetFirstSolutionForX(double x)
    {
        var solutions = SolveForX(x);
        for (var i = 0; i < solutions.Length; ++i)
        {
            var d = solutions[i];
            if (d >= -1e-8 && d <= 1.00000001)
            {
                if (d >= 0.0 && d <= 1.0)
                {
                    return d;
                }

                if (d < 0.0)
                {
                    return 0.0;
                }

                return 1.0;
            }
        }

        return double.NaN;
    }

    private double GetCubicAx() => _x2 - _x1 - GetCubicBx() - GetCubicCx();

    private double GetCubicAy() => _y2 - _y1 - GetCubicBy() - GetCubicCy();

    private double GetCubicBx() => 3.0 * (_ctrlX2 - _ctrlX1) - GetCubicCx();

    private double GetCubicBy() => 3.0 * (_ctrlY2 - _ctrlY1) - GetCubicCy();

    private double GetCubicCx() => 3.0 * (_ctrlX1 - _x1);

    private double GetCubicCy() => 3.0 * (_ctrlY1 - _y1);
}

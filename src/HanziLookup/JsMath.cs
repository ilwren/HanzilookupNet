namespace HanziLookup;

/// <summary>
/// Numeric helpers that reproduce the semantics of JavaScript's <c>Math</c> object, which the
/// ported algorithm depends on:
/// <list type="bullet">
///   <item><description><c>Math.round()</c> rounds halves towards positive infinity (unlike <see cref="Math.Round(double)"/>,
///   which rounds to even by default).</description></item>
///   <item><description><c>Number.MAX_SAFE_INTEGER</c> is used as the initial value of the bounding rectangle.</description></item>
/// </list>
/// </summary>
internal static class JsMath
{
    /// <summary>JavaScript's <c>Number.MAX_SAFE_INTEGER</c> (2^53 - 1).</summary>
    public const double MaxSafeInteger = 9007199254740991d;

    /// <summary>JavaScript's <c>Number.MIN_SAFE_INTEGER</c> (-(2^53 - 1)).</summary>
    public const double MinSafeInteger = -9007199254740991d;

    /// <summary>Equivalent of JavaScript's <c>Math.round(value)</c>: halves round towards +Infinity.</summary>
    public static double Round(double value) => Math.Floor(value + 0.5);

    /// <summary>
    /// <see cref="Round(double)"/> followed by a conversion to <see cref="int"/>.
    /// The conversion is range checked (and NaN safe) so that pathological input can never
    /// produce an undefined value; for every input the algorithm accepts this is identical
    /// to JavaScript's <c>Math.round()</c>.
    /// </summary>
    public static int RoundToInt32(double value)
    {
        var rounded = Math.Floor(value + 0.5);
        if (double.IsNaN(rounded))
        {
            return 0;
        }

        if (rounded >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (rounded <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)rounded;
    }
}

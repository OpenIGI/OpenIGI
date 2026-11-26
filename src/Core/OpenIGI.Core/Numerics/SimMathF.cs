namespace OpenIGI.Core.Numerics;

/// <summary>
/// The simulation's transcendental functions in single precision, the same bits on every machine: <see cref="SimMath"/>'s in
/// double, rounded once to a float.
/// </summary>
/// <remarks>
/// The twin of <see cref="MathF"/> as <see cref="SimMath"/> is of <see cref="Math"/>, so that a call site keeps its precision
/// when it changes over. A double result within an ulp of the true value, rounded to a float, is within half a float ulp and a
/// hair -- closer than most platforms' own single-precision routines.
/// </remarks>
public static class SimMathF
{
    /// <summary>The sine of an angle.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its sine.</returns>
    public static float Sin(float x) => (float)SimMath.Sin(x);

    /// <summary>The cosine of an angle.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its cosine.</returns>
    public static float Cos(float x) => (float)SimMath.Cos(x);

    /// <summary>The sine and the cosine of an angle together.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its sine and cosine.</returns>
    public static (float Sin, float Cos) SinCos(float x) => (Sin(x), Cos(x));

    /// <summary>The tangent of an angle.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its tangent.</returns>
    public static float Tan(float x) => (float)SimMath.Tan(x);

    /// <summary>The arc sine.</summary>
    /// <param name="x">A value from minus one to one.</param>
    /// <returns>The angle, radians.</returns>
    public static float Asin(float x) => (float)SimMath.Asin(x);

    /// <summary>The arc cosine.</summary>
    /// <param name="x">A value from minus one to one.</param>
    /// <returns>The angle, radians.</returns>
    public static float Acos(float x) => (float)SimMath.Acos(x);

    /// <summary>The arc tangent.</summary>
    /// <param name="x">The tangent.</param>
    /// <returns>The angle, radians.</returns>
    public static float Atan(float x) => (float)SimMath.Atan(x);

    /// <summary>The angle of a point from the positive x axis.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="x">The point's x.</param>
    /// <returns>The angle, radians, from -pi to pi.</returns>
    public static float Atan2(float y, float x) => (float)SimMath.Atan2(y, x);

    /// <summary>e raised to a power.</summary>
    /// <param name="x">The power.</param>
    /// <returns>e to it.</returns>
    public static float Exp(float x) => (float)SimMath.Exp(x);

    /// <summary>The natural logarithm.</summary>
    /// <param name="x">A positive value.</param>
    /// <returns>Its logarithm.</returns>
    public static float Log(float x) => (float)SimMath.Log(x);

    /// <summary>The hyperbolic tangent.</summary>
    /// <param name="x">The value.</param>
    /// <returns>Its hyperbolic tangent.</returns>
    public static float Tanh(float x) => (float)SimMath.Tanh(x);

    /// <summary>A value raised to a power.</summary>
    /// <param name="x">The base.</param>
    /// <param name="y">The power.</param>
    /// <returns>x to the y.</returns>
    public static float Pow(float x, float y) => (float)SimMath.Pow(x, y);
}

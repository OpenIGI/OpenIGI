namespace OpenIGI.Core.Numerics;

/// <summary>
/// One x87 register value under the control word the original runs its physics with, <c>0x0E7F</c>:
/// 53-bit precision, rounding toward zero.
/// </summary>
/// <remarks>
/// <para>
/// With precision control at 53 bits every <c>fadd</c>, <c>fsub</c>, <c>fmul</c>, <c>fdiv</c> and
/// <c>fsqrt</c> rounds its result to an IEEE double's significand, so a register value is exactly a double
/// -- but rounded <b>toward zero</b>, where .NET rounds to nearest. Each operator here computes the
/// round-to-nearest result, recovers the exact error with an error-free transformation (TwoSum for a sum,
/// a fused multiply-add for a product, quotient or root) and steps one ulp toward zero when rounding went
/// away from it.
/// </para>
/// <para>
/// A store through <c>fstp dword</c> truncates as well: <see cref="F"/>. Truncating to double and then to
/// float equals truncating once to float, so the two roundings never compound. Loads (<c>fld dword</c>,
/// <c>fld qword</c>, <c>fild</c>) are exact and are the implicit conversions.
/// </para>
/// <para>
/// Only the order of operations the disassembly shows is reproduced: C# evaluates <c>a * b + c * d + e</c>
/// as <c>((a * b) + (c * d)) + e</c>, which is how the pseudo-code in the original
/// groups every sum. Never let two plain <see langword="float"/>s meet in an operator -- that is .NET
/// float arithmetic, rounded to nearest; one side must already be an <see cref="X87"/>.
/// </para>
/// </remarks>
/// <param name="value">The register value.</param>
public readonly struct X87(double value)
{
    /// <summary>Gets the register value.</summary>
    public double Value { get; } = value;

    /// <summary>Gets the value as <c>fstp dword</c> stores it: truncated to single precision.</summary>
    public float F => ToFloat(Value);

    /// <summary>Loads a float exactly (<c>fld dword</c>).</summary>
    /// <param name="value">The float.</param>
    public static implicit operator X87(float value) => new(value);

    /// <summary>Loads a double exactly (<c>fld qword</c>).</summary>
    /// <param name="value">The double.</param>
    public static implicit operator X87(double value) => new(value);

    /// <summary>The sum, rounded toward zero.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    public static X87 operator +(X87 a, X87 b) => new(Add(a.Value, b.Value));

    /// <summary>The difference, rounded toward zero.</summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    public static X87 operator -(X87 a, X87 b) => new(Add(a.Value, -b.Value));

    /// <summary>The product, rounded toward zero.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    public static X87 operator *(X87 a, X87 b) => new(Multiply(a.Value, b.Value));

    /// <summary>The quotient, rounded toward zero.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    public static X87 operator /(X87 a, X87 b) => new(Divide(a.Value, b.Value));

    /// <summary><c>fchs</c>: exact.</summary>
    /// <param name="a">The operand.</param>
    public static X87 operator -(X87 a) => new(-a.Value);

    /// <summary><c>fsqrt</c>, rounded toward zero.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The root.</returns>
    public static X87 Sqrt(X87 a) => new(SquareRoot(a.Value));

    /// <summary><c>fabs</c>: exact.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The magnitude.</returns>
    public static X87 Abs(X87 a) => new(Math.Abs(a.Value));

    /// <summary>Truncates a register value to single precision, as <c>fstp dword</c> does under RC = chop.</summary>
    /// <param name="value">The register value.</param>
    /// <returns>The float nearest <paramref name="value"/> on the side of zero.</returns>
    public static float ToFloat(double value)
    {
        float nearest = (float)value;
        if (float.IsInfinity(nearest) && !double.IsInfinity(value))
        {
            return value > 0 ? float.MaxValue : float.MinValue;
        }

        if (Math.Abs((double)nearest) > Math.Abs(value))
        {
            return nearest > 0 ? MathF.BitDecrement(nearest) : MathF.BitIncrement(nearest);
        }

        return nearest;
    }

    /// <summary>A sum rounded toward zero.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The truncated sum.</returns>
    public static double Add(double a, double b)
    {
        double sum = a + b;
        if (!double.IsFinite(sum))
        {
            return sum;
        }

        // TwoSum: the exact error of the rounded sum.
        double bVirtual = sum - a;
        double aVirtual = sum - bVirtual;
        double error = (a - aVirtual) + (b - bVirtual);
        return TowardZero(sum, error);
    }

    /// <summary>A product rounded toward zero.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The truncated product.</returns>
    public static double Multiply(double a, double b)
    {
        double product = a * b;
        return double.IsFinite(product) ? TowardZero(product, Math.FusedMultiplyAdd(a, b, -product)) : product;
    }

    /// <summary>A quotient rounded toward zero.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The truncated quotient.</returns>
    public static double Divide(double a, double b)
    {
        double quotient = a / b;
        if (!double.IsFinite(quotient) || quotient == 0)
        {
            return quotient;
        }

        // a = q*b + r exactly, so the true quotient is q + r/b.
        double remainder = Math.FusedMultiplyAdd(-quotient, b, a);
        return TowardZero(quotient, b < 0 ? -remainder : remainder);
    }

    /// <summary>A square root rounded toward zero.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The truncated root.</returns>
    public static double SquareRoot(double a)
    {
        double root = Math.Sqrt(a);
        if (!double.IsFinite(root) || root == 0)
        {
            return root;
        }

        return Math.FusedMultiplyAdd(-root, root, a) < 0 ? Math.BitDecrement(root) : root;
    }

    private static double TowardZero(double rounded, double error)
    {
        if (error == 0 || rounded == 0)
        {
            return rounded;
        }

        // Rounding went away from zero exactly when the error points back toward it.
        if (rounded > 0 && error < 0)
        {
            return Math.BitDecrement(rounded);
        }

        if (rounded < 0 && error > 0)
        {
            return Math.BitIncrement(rounded);
        }

        return rounded;
    }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

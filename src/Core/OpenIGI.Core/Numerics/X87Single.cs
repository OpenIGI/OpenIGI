namespace OpenIGI.Core.Numerics;

/// <summary>
/// One x87 register value under the control word I.G.I. 2's client runs with, <c>0x0C7F</c>: 24-bit
/// precision, rounding toward zero.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why 24 bits.</b> The client creates its Direct3D 8 device without <c>D3DCREATE_FPU_PRESERVE</c>
/// (<c>Direct3D_CreateDevice</c>, <c>igi2ed 0x55BB80</c>, behaviour flags <c>0x80</c> then <c>0x20</c>), and
/// Direct3D then sets the FPU to single precision. The game afterwards only ORs the rounding-control bits
/// in (<c>Real_SetFPUStateIEEE</c>, <c>igi2 0x444C40</c>, called once the mode is set by
/// <c>Direct3D_SetMode</c>, <c>igi2ed 0x55BAC9</c>, and again after every device reset and movie), so it
/// runs at single precision, truncating. The first game kept 53 bits (<see cref="X87"/>).
/// </para>
/// <para>
/// Precision control rounds the result of <c>fadd</c>, <c>fsub</c>, <c>fmul</c>, <c>fdiv</c> and
/// <c>fsqrt</c> to a 24-bit significand while the register keeps its wide exponent. Loads are exact: a
/// <c>double</c> loaded with <c>fld qword</c> keeps all 53 bits until the next operation rounds its result.
/// Each operator here takes <see cref="X87"/>'s truncated 53-bit result and truncates it again to 24 bits;
/// truncations nest, so that equals truncating the exact result once.
/// </para>
/// <para>
/// As with <see cref="X87"/>, only the disassembly's order of operations is reproduced, and two plain
/// <see langword="float"/>s must never meet in an operator: one side must already be an
/// <see cref="X87Single"/>.
/// </para>
/// </remarks>
/// <param name="value">The register value.</param>
public readonly struct X87Single(double value) : IX87<X87Single>
{
    // A double's fraction has 52 bits; a 24-bit significand keeps the top 23 of them.
    private const long DroppedFractionMask = (1L << 29) - 1;

    /// <summary>Gets the register value.</summary>
    public double Value { get; } = value;

    /// <summary>Gets the value as <c>fstp dword</c> stores it: truncated to single precision.</summary>
    public float F => X87.ToFloat(Value);

    /// <summary>Loads a float exactly (<c>fld dword</c>).</summary>
    /// <param name="value">The float.</param>
    public static implicit operator X87Single(float value) => new(value);

    /// <summary>Loads a double exactly (<c>fld qword</c>).</summary>
    /// <param name="value">The double.</param>
    public static implicit operator X87Single(double value) => new(value);

    /// <summary>The sum, rounded toward zero to 24 bits.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    public static X87Single operator +(X87Single a, X87Single b) => new(Chop(X87.Add(a.Value, b.Value)));

    /// <summary>The difference, rounded toward zero to 24 bits.</summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    public static X87Single operator -(X87Single a, X87Single b) => new(Chop(X87.Add(a.Value, -b.Value)));

    /// <summary>The product, rounded toward zero to 24 bits.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    public static X87Single operator *(X87Single a, X87Single b) => new(Multiply(a.Value, b.Value));

    /// <summary>The quotient, rounded toward zero to 24 bits.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    public static X87Single operator /(X87Single a, X87Single b) => new(Chop(X87.Divide(a.Value, b.Value)));

    /// <summary><c>fchs</c>: exact.</summary>
    /// <param name="a">The operand.</param>
    public static X87Single operator -(X87Single a) => new(-a.Value);

    /// <summary><c>fsqrt</c>, rounded toward zero to 24 bits.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The root.</returns>
    public static X87Single Sqrt(X87Single a) => new(Chop(X87.SquareRoot(a.Value)));

    /// <summary><c>fabs</c>: exact.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The magnitude.</returns>
    public static X87Single Abs(X87Single a) => new(Math.Abs(a.Value));

    /// <summary><c>fsin</c>: precision control does not apply to it.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The sine.</returns>
    public static X87Single Sin(X87Single a) => new(SimMath.Sin(a.Value));

    /// <summary><c>fcos</c>: precision control does not apply to it.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The cosine.</returns>
    public static X87Single Cos(X87Single a) => new(SimMath.Cos(a.Value));

    /// <summary>The arc tangent of <paramref name="y"/> over <paramref name="x"/>, <c>fpatan</c>, which precision control does not narrow.</summary>
    /// <param name="y">The rise.</param>
    /// <param name="x">The run.</param>
    /// <returns>The angle at full precision.</returns>
    public static X87Single Atan2(X87Single y, X87Single x) => new(SimMath.Atan2(y.Value, x.Value));

    /// <summary><c>fistp</c> under rounding toward zero: the integer part.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The truncated integer.</returns>
    public static int ToInt32(X87Single a) => (int)Math.Truncate(a.Value);

    /// <summary>A product rounded toward zero to 24 bits: the same value as <c>Chop(X87.Multiply(a, b))</c>, found without a fused multiply-add where it can be.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The truncated product.</returns>
    /// <remarks>
    /// <para>
    /// The double product <c>p</c> is within half a double ulp of the exact one. When <c>p</c> is not itself on the 24-bit grid,
    /// the grid value below it in magnitude is also the one below the exact product, so truncating <c>p</c> is the answer. Only
    /// a <c>p</c> that rounding may have carried onto the grid needs the exact error -- unless the factors' significant bits fit a
    /// double between them, when the product was exact.
    /// </para>
    /// <para>
    /// Why it matters: a browser has no fused multiply-add instruction, and <see cref="Math.FusedMultiplyAdd"/> there is a
    /// software routine. Every I.G.I. 2 skinning and bone multiply paid for one, a tenth of the browser build's CPU. Most products
    /// now take the first branch.
    /// </para>
    /// </remarks>
    public static double Multiply(double a, double b)
    {
        double product = a * b;
        if (double.IsNormal(product))
        {
            long bits = BitConverter.DoubleToInt64Bits(product);
            if ((bits & DroppedFractionMask) != 0)
            {
                return BitConverter.Int64BitsToDouble(bits & ~DroppedFractionMask);
            }

            if (double.IsNormal(a) && double.IsNormal(b) && SignificantBits(a) + SignificantBits(b) <= 53)
            {
                return product;
            }
        }

        return Chop(X87.Multiply(a, b));
    }

    // The bits of a normal double's significand up to its last set one.
    private static int SignificantBits(double value)
    {
        long significand = (BitConverter.DoubleToInt64Bits(value) & ((1L << 52) - 1)) | (1L << 52);
        return 53 - System.Numerics.BitOperations.TrailingZeroCount(significand);
    }

    /// <summary>Truncates a register value to a 24-bit significand, keeping its exponent.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The truncated value.</returns>
    public static double Chop(double value)
    {
        if (!double.IsNormal(value))
        {
            return value;
        }

        long bits = BitConverter.DoubleToInt64Bits(value);
        return BitConverter.Int64BitsToDouble(bits & ~DroppedFractionMask);
    }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

namespace OpenIGI.Core.Numerics;

/// <summary>
/// One x87 register value under a precision setting: the arithmetic code both games' physics share is written once
/// against this and runs at each game's precision.
/// </summary>
/// <remarks>
/// <para>
/// The two games ran the same engine's maths under different control words: the first at 53 bits
/// (<see cref="X87"/>), the sequel at 24 (<see cref="X87Single"/>). Where both compiled a function to the same order
/// of operations, one generic transcription over <typeparamref name="TSelf"/> reproduces each bit for bit. Where
/// they did not, each game keeps its own.
/// </para>
/// <para>
/// A generic transcription follows the store points (<c>fstp dword</c>, <see cref="F"/>) of the 53-bit build. At 24
/// bits every result already fits a float's significand, so a store there changes nothing and the sequel's code,
/// which keeps more of its intermediates in registers, agrees with it.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The precision.</typeparam>
public interface IX87<TSelf>
    where TSelf : struct, IX87<TSelf>
{
    /// <summary>Gets the register value.</summary>
    double Value { get; }

    /// <summary>Gets the value as <c>fstp dword</c> stores it: truncated to single precision.</summary>
    float F { get; }

    /// <summary>Loads a float exactly (<c>fld dword</c>).</summary>
    /// <param name="value">The float.</param>
    static abstract implicit operator TSelf(float value);

    /// <summary>Loads a double exactly (<c>fld qword</c>).</summary>
    /// <param name="value">The double.</param>
    static abstract implicit operator TSelf(double value);

    /// <summary>The sum, rounded toward zero at the precision.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The sum.</returns>
    static abstract TSelf operator +(TSelf a, TSelf b);

    /// <summary>The difference, rounded toward zero at the precision.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The difference.</returns>
    static abstract TSelf operator -(TSelf a, TSelf b);

    /// <summary>The product, rounded toward zero at the precision.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The product.</returns>
    static abstract TSelf operator *(TSelf a, TSelf b);

    /// <summary>The quotient, rounded toward zero at the precision.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient.</returns>
    static abstract TSelf operator /(TSelf a, TSelf b);

    /// <summary>The negation, <c>fchs</c>: exact.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The negated value.</returns>
    static abstract TSelf operator -(TSelf a);

    /// <summary>The square root, <c>fsqrt</c>, rounded toward zero at the precision.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The root.</returns>
    static abstract TSelf Sqrt(TSelf a);

    /// <summary>The magnitude, <c>fabs</c>: exact.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The magnitude.</returns>
    static abstract TSelf Abs(TSelf a);
}

namespace OpenIGI.Core.Numerics;

/// <summary>
/// The engine's own pseudo-random generator — a three-word lagged shift-and-add.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>0x4B4740</c> and its two wrappers. Everything that has to look random in the
/// original draws from one of these, and the streams are picked by <c>0x416D20</c>, which in the retail
/// build ignores its argument and returns the single global stream at <c>dword_57BABC + 44</c>.
/// </para>
/// <code>
/// c ^= a >> 7;   // logical shift
/// b ^= c;
/// a += b;
/// return (u16)a * (1 / 65536.0);
/// </code>
/// <para>
/// <b>The generator is reproduced rather than substituted</b> because the order and the count of the
/// draws is part of the game's behaviour: a spread sample takes four draws in a fixed order, and the
/// recoil's rejection loop takes an unbounded even number. A different generator would still look
/// random and would not reproduce a shot.
/// </para>
/// <para>
/// The value is quantised to <b>1/65536</b>: only the low sixteen bits of the accumulator are used, so
/// <see cref="NextSingle"/> yields one of 65536 values in <c>[0, 1)</c> and never reaches one.
/// </para>
/// </remarks>
public sealed class IgiRandom
{
    /// <summary>The reciprocal the original multiplies the sixteen-bit draw by.</summary>
    /// <remarks>Spelled <c>0.0000152587890625</c>, which is exactly <c>2⁻¹⁶</c>.</remarks>
    public const double Scale = 0.0000152587890625;

    private uint _a;
    private uint _b;
    private uint _c;

    /// <summary>Initializes a new instance of the <see cref="IgiRandom"/> class.</summary>
    /// <param name="a">The accumulator's initial value.</param>
    /// <param name="b">The second word's initial value.</param>
    /// <param name="c">The third word's initial value.</param>
    /// <remarks>
    /// The original's seeding is not read, so a caller supplies the words. An all-zero state is a fixed
    /// point of the recurrence and stays zero, which is why the parameterless constructor does not use
    /// one.
    /// </remarks>
    public IgiRandom(uint a, uint b, uint c)
    {
        _a = a;
        _b = b;
        _c = c;
    }

    /// <summary>Initializes a new instance of the <see cref="IgiRandom"/> class from one seed.</summary>
    /// <param name="seed">Any value; zero is spread across the three words rather than left as-is.</param>
    /// <remarks>
    /// A convenience, not a transcription — how the original seeds its streams has not been read, and
    /// this note is here so nothing mistakes the constants for retail ones.
    /// </remarks>
    public IgiRandom(uint seed = 1u)
        : this(seed | 1u, seed ^ 0x9E3779B9u, ~seed | 2u)
    {
    }

    /// <summary>Sets the three words, as <c>0x4B4720</c> does.</summary>
    /// <param name="a">The accumulator.</param>
    /// <param name="b">The second word.</param>
    /// <param name="c">The third word.</param>
    public void Seed(uint a, uint b, uint c)
    {
        _a = a;
        _b = b;
        _c = c;
    }

    /// <summary>Mixes a seed into the words, as <c>0x4B47E0</c> does: <c>a ^= s, b += s &lt;&lt; 15, c -= s &lt;&lt; 9</c>.</summary>
    /// <param name="seed">The seed.</param>
    public void Mix(uint seed)
    {
        _a ^= seed;
        _b += seed << 15;
        _c -= seed << 9;
    }

    /// <summary>Seeds the global stream the way a level's start does.</summary>
    /// <remarks>
    /// LevelFlow's handler (<c>0x488ED0</c>) calls <c>0x416D90</c>, which resets the stream at
    /// <c>dword_57BABC + 44</c> to C8FB1A07 / 39EA7756 / 7FF13CCD (<c>0x4B4720</c>) and mixes in 0x19111911
    /// (<c>0x4B47E0</c>), leaving D1EA0316 / C672F756 / 5DBF1ACD -- read live on the probe, the same every run.
    /// Every level-load and per-tick draw then comes from this one stream in walk order.
    /// </remarks>
    public void SeedForLevel()
    {
        Seed(0xC8FB1A07u, 0x39EA7756u, 0x7FF13CCDu);
        Mix(0x19111911u);
    }

    /// <summary>Draws the next raw word — <c>0x4B4740</c> itself, the whole accumulator.</summary>
    /// <returns>The accumulator after the step.</returns>
    /// <remarks>Some callers test it directly, such as the parity <c>0x412CE0</c> picks a sound by.</remarks>
    public uint NextRaw()
    {
        _c ^= _a >> 7;
        _b ^= _c;
        _a += _b;
        Drew?.Invoke(_a);
        return _a;
    }

    /// <summary>Gets or sets a diagnostic observer told of every step, with the accumulator after it.</summary>
    /// <remarks>
    /// For the draw-order diff against the probe's <c>[rng]</c> log: which caller drew, on which tick. Nothing in
    /// the game sets it.
    /// </remarks>
    public Action<uint>? Drew { get; set; }

    /// <summary>Draws the next value.</summary>
    /// <returns>A value in <c>[0, 1)</c>, quantised to <see cref="Scale"/>.</returns>
    public double NextDouble() => (ushort)NextRaw() * Scale;

    /// <summary>Draws the next value as a <see cref="float"/>.</summary>
    /// <returns>A value in <c>[0, 1)</c>.</returns>
    /// <remarks>
    /// The original returns a <c>double</c> from the x87 stack and the callers narrow it where they
    /// store it, so narrowing here matches every consumer that keeps the result in a <c>float</c>.
    /// </remarks>
    public float NextSingle() => (float)NextDouble();

    /// <summary>Draws a bounded integer, as <c>0x4B47C0</c> does.</summary>
    /// <param name="exclusiveBound">The bound; the result is below it.</param>
    /// <returns>A value in <c>[0, exclusiveBound)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exclusiveBound"/> is not positive.</exception>
    /// <remarks>
    /// The original is <c>rawDraw % bound</c> over the <b>full 32-bit</b> accumulator — not over the
    /// sixteen-bit value <see cref="NextDouble"/> scales — so this is a different projection of the same
    /// step, and the two are not interchangeable.
    /// </remarks>
    public uint NextBounded(uint exclusiveBound)
    {
        ArgumentOutOfRangeException.ThrowIfZero(exclusiveBound);

        _c ^= _a >> 7;
        _b ^= _c;
        _a += _b;
        Drew?.Invoke(_a);
        return _a % exclusiveBound;
    }
}

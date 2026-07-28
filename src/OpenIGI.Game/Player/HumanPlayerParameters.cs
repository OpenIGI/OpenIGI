using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The 43 values <c>DefineHumanPlayerGeneral</c> publishes, converted into the units the engine runs in.
/// </summary>
/// <remarks>
/// <para>
/// The native (<c>0x410070</c>) scales six of its arguments as it stores them, and the scales are what
/// give the arguments their meaning: speeds are authored in <b>km/h</b>, distances in <b>metres</b> and
/// durations in <b>seconds</b>, and all three become engine units per tick, engine units, and ticks.
/// The conversions are read from the disassembly — the decompiler chains the x87 stack slot and makes
/// them look cumulative, which they are not.
/// </para>
/// <para>
/// Only the arguments whose consumer has been identified are named. The rest keep their index, because
/// naming them would be a guess; see <see cref="RawArguments"/> and the original.
/// </para>
/// </remarks>
public sealed class HumanPlayerParameters
{
    /// <summary>The number of arguments the native takes.</summary>
    public const int ArgumentCount = 43;

    /// <summary>The first argument written into the first 17-row table.</summary>
    public const int FirstTableArgument = 9;

    /// <summary>The number of rows in each of the two tables.</summary>
    public const int TableRowCount = 17;

    /// <summary>Engine units per metre — the original's <c>WORLD_METER</c> (<c>dbl_533498</c>).</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Ticks per second (<c>dbl_533488</c>).</summary>
    public const double TicksPerSecond = 30.0;

    /// <summary>
    /// Engine units per tick for one km/h — <c>flt_5334A4 * flt_5334A0</c>, i.e.
    /// <c>4096000 * (1/108000)</c>.
    /// </summary>
    /// <remarks>
    /// Kept as the product of the original's two single-precision constants rather than as a tidier
    /// closed form, because the rounding of each is observable: the retail 27 km/h lands on exactly
    /// 1024 units per tick, which a recomputed factor need not reproduce.
    /// </remarks>
    public static readonly float UnitsPerTickPerKilometrePerHour = 4096000f * 9.259259059035685e-06f;

    private readonly float[] _raw;
    private readonly float[] _firstTable;
    private readonly float[] _secondTable;

    /// <summary>Initializes a new instance of the <see cref="HumanPlayerParameters"/> class.</summary>
    /// <param name="arguments">The <see cref="ArgumentCount"/> values as the script pushes them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="arguments"/> is the wrong length.</exception>
    public HumanPlayerParameters(IReadOnlyList<float> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != ArgumentCount)
        {
            throw new ArgumentException(
                $"DefineHumanPlayerGeneral takes exactly {ArgumentCount} arguments.",
                nameof(arguments));
        }

        _raw = [.. arguments];

        DeltaTranslationScale = _raw[0];
        JumpHorizontalSpeed = _raw[1] * UnitsPerTickPerKilometrePerHour;
        JumpVerticalSpeed = _raw[2] * UnitsPerTickPerKilometrePerHour;
        Speed3 = _raw[3] * UnitsPerTickPerKilometrePerHour;
        Distance4 = (float)(_raw[4] * UnitsPerMetre);
        Distance5 = (float)(_raw[5] * UnitsPerMetre);
        Duration6Ticks = IgiMath.TruncToInt32(_raw[6] * TicksPerSecond);
        Value7 = _raw[7];
        Value8 = _raw[8];

        _firstTable = new float[TableRowCount];
        _secondTable = new float[TableRowCount];
        for (int i = 0; i < TableRowCount; i++)
        {
            _firstTable[i] = _raw[FirstTableArgument + i];
            _secondTable[i] = _raw[FirstTableArgument + TableRowCount + i];
        }
    }

    /// <summary>Gets the arguments exactly as the script pushed them, before any conversion.</summary>
    public IReadOnlyList<float> RawArguments => _raw;

    /// <summary>
    /// Gets the factor the animation's delta translation is multiplied by while
    /// <c>Human+1324</c> is clear (argument 0; retail 1.75).
    /// </summary>
    public float DeltaTranslationScale { get; }

    /// <summary>Gets the jump's horizontal speed in units per tick (argument 1; retail 17.5 km/h).</summary>
    public float JumpHorizontalSpeed { get; }

    /// <summary>Gets the jump's vertical speed in units per tick (argument 2; retail 27 km/h).</summary>
    public float JumpVerticalSpeed { get; }

    /// <summary>Gets argument 3 in units per tick — a speed read by <c>0x408460</c> (retail 0.5 km/h).</summary>
    public float Speed3 { get; }

    /// <summary>Gets argument 4 in units — a distance read by <c>0x40DFC0</c> (retail 0.85 m).</summary>
    public float Distance4 { get; }

    /// <summary>Gets argument 5 in units — a distance read by <c>0x40DFC0</c> (retail 0.85 m).</summary>
    public float Distance5 { get; }

    /// <summary>Gets argument 6 in ticks — a duration read by <c>0x40DFC0</c> (retail 0.25 s).</summary>
    /// <remarks>
    /// Stored with <c>fistp</c>, so it <b>truncates</b>: the retail 0.25 s is 7 ticks, not 8. Rounding
    /// here would be a one-tick behaviour change.
    /// </remarks>
    public int Duration6Ticks { get; }

    /// <summary>Gets argument 7, copied into <c>HumanPlayer+3336</c> by the constructor (retail 3).</summary>
    public float Value7 { get; }

    /// <summary>Gets argument 8, read by <c>0x412F80</c> (retail 0.5).</summary>
    public float Value8 { get; }

    /// <summary>Gets arguments 9..25 — field <c>+0</c> of the 17 rows at <c>0x5389F8</c>.</summary>
    /// <remarks>What the rows enumerate is unresolved; see the original.</remarks>
    public IReadOnlyList<float> FirstTable => _firstTable;

    /// <summary>Gets arguments 26..42 — field <c>+0</c> of the 17 rows at <c>0x538B08</c>.</summary>
    /// <remarks>What the rows enumerate is unresolved; see the original.</remarks>
    public IReadOnlyList<float> SecondTable => _secondTable;
}

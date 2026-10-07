namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The player tuning in the units the game runs in, <c>HumanPlayer_tGeneral</c> (<c>igi2 0x6D902C</c>).
/// </summary>
/// <param name="DeltaTranslationScale">The scale on the animation-driven translation.</param>
/// <param name="ForwardJumpSpeed">The forward speed a jump adds, world units per tick.</param>
/// <param name="UpwardJumpSpeed">The upward speed a jump starts with, world units per tick.</param>
/// <param name="InAirControlSpeed">The speed each held direction adds per tick in the air, world units per tick.</param>
/// <param name="PeekOffsetX">How far a peek leans sideways, world units.</param>
/// <param name="PeekOffsetZ">How far a peek rises from a crouch, world units.</param>
/// <param name="PeekTime">How long a peek takes to reach its length, ticks.</param>
/// <param name="MaxDamageScale">The scale on the player's maximum damage.</param>
/// <param name="ElectricFenceDamageScale">The scale on an electric fence's damage.</param>
/// <param name="ThrowingBaseVelocity">The base speed of a thrown item, km/h as written.</param>
/// <param name="DamageMultipliers">The single-player damage multipliers, by <see cref="Igi2DamagePart"/>.</param>
/// <param name="DamageMultipliersMultiplayer">The multiplayer damage multipliers, by <see cref="Igi2DamagePart"/>.</param>
/// <remarks>
/// Before the player script has run every value is zero, as the original's zero-initialised global is.
/// </remarks>
public sealed record Igi2HumanPlayerGeneral(
    float DeltaTranslationScale,
    float ForwardJumpSpeed,
    float UpwardJumpSpeed,
    float InAirControlSpeed,
    float PeekOffsetX,
    float PeekOffsetZ,
    int PeekTime,
    float MaxDamageScale,
    float ElectricFenceDamageScale,
    float ThrowingBaseVelocity,
    IReadOnlyList<float> DamageMultipliers,
    IReadOnlyList<float> DamageMultipliersMultiplayer)
{
    /// <summary>World units per metre.</summary>
    public const float UnitsPerMetre = 4096f;

    /// <summary>Game ticks per second.</summary>
    public const float TicksPerSecond = 30f;

    // The two constants HumanPlayer_UpdateParameters multiplies a km/h figure by: 1000 m × 4096 units, and
    // one over 3600 s × 30 ticks, each a float in the original.
    private const float MetresPerKilometreInUnits = 4096000f;
    private static readonly float PerHourInTicks = BitConverter.Int32BitsToSingle(0x371B5837);

    /// <summary>Gets the tuning before any player script has run.</summary>
    public static Igi2HumanPlayerGeneral Unset { get; } =
        new(0f, 0f, 0f, 0f, 0f, 0f, 0, 0f, 0f, 0f, new float[17], new float[17]);

    /// <summary>Converts a config item's figures, as <c>HumanPlayer_UpdateParameters</c> (<c>0x4BD1F0</c>) does.</summary>
    /// <param name="item">The config item.</param>
    /// <returns>The tuning in world units and ticks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public static Igi2HumanPlayerGeneral From(HumanPlayerConfigItemTask item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new Igi2HumanPlayerGeneral(
            item.MovementSpeedScale,
            PerTick(item.ForwardJumpSpeed),
            PerTick(item.UpwardJumpSpeed),
            PerTick(item.InAirControlSpeed),
            item.PeekLengthSideways * UnitsPerMetre,
            item.PeekLengthUp * UnitsPerMetre,
            (int)(item.PeekTime * (double)TicksPerSecond),
            item.MaxDamageScale,
            item.ElectricFenceDamageScale,
            item.ThrowingBaseVelocity,
            item.DamageMultipliers(),
            item.DamageMultipliersMultiplayer());
    }

    /// <summary>Converts km/h to world units per tick, in the original's order and precision.</summary>
    /// <param name="kilometresPerHour">The speed as the script writes it.</param>
    /// <returns>The speed per tick.</returns>
    public static float PerTick(float kilometresPerHour) =>
        (float)(kilometresPerHour * (double)MetresPerKilometreInUnits * PerHourInTicks);
}

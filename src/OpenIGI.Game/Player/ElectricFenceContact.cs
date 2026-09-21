
namespace OpenIGI.Game.Player;

/// <summary>The result of the port's current electric-fence contact rule.</summary>
public readonly record struct ElectricFenceShock(float Damage, bool IsLethal);

/// <summary>Retail electric-fence contact handling for the player.</summary>
/// <remarks>
/// Gameplay, not presentation: a damage factor, a cooldown and the zone the shock lands on. It sat
/// among the renderers only because the code that calls it does, and it moved here — beside
/// <see cref="HumanDamage"/>, whose zone it names — the first time that folder was audited rather than
/// inherited.
/// </remarks>
/// <remarks>
/// <para>
/// The powered fence callback at <c>0x43FF70</c> dispatches to the HumanPlayer handler at
/// <c>0x412F80</c>. That handler selects zone 13, adds the HumanPlayer QVM argument-8 dose directly
/// (retail <c>0.5</c>; the player zone's multiplier is one), and writes a 45-tick guard. The player
/// update decrements that guard once per tick through <c>0x4113C0</c>.
/// </para>
/// <para>
/// The handler does not pass through generic hit damage or difficulty scaling, and it does not post a
/// soldier ground-impact event. Keeping the rule here makes that behavior regressible without
/// broadening the desktop game's private mission bootstrap into a test API.
/// </para>
/// </remarks>
public static class ElectricFenceContact
{
    /// <summary>
    /// The dose used when the level supplies no HumanPlayer tuning — the retail argument-8 value.
    /// </summary>
    public const float DefaultDamageFactor = 0.5f;

    /// <summary>The native fence-contact bookkeeping zone.</summary>
    public const int Zone = HumanDamage.FrontZone;

    /// <summary>Native fence-contact lockout written by <c>0x412F80</c>.</summary>
    public const int CooldownTicks = 45;

    /// <summary>
    /// Advances the native per-player shock guard and returns the observed fence when it may deal damage.
    /// </summary>
    public static T? Advance<T>(ref int cooldownTicks, T? observedContact)
        where T : class
    {
        if (cooldownTicks > 0)
        {
            cooldownTicks--;
        }

        return TryShock(ref cooldownTicks, observedContact);
    }

    /// <summary>Returns a fence only when the native per-player shock guard has expired.</summary>
    /// <remarks>
    /// This is used by an explicit fence interaction, which happens outside the ordinary movement
    /// contact update and therefore must not decrement the once-per-tick guard itself.
    /// </remarks>
    public static T? TryShock<T>(ref int cooldownTicks, T? observedContact)
        where T : class
    {
        if (observedContact is null || cooldownTicks > 0)
        {
            return null;
        }

        cooldownTicks = CooldownTicks;
        return observedContact;
    }

    /// <summary>Applies the native direct health dose, bypassing generic hit and difficulty scaling.</summary>
    /// <param name="health">The player's damage accumulator.</param>
    /// <param name="damageFactor">
    /// The level's HumanPlayer argument 8, from <see cref="HumanPlayerParameters.ElectricFenceDose"/>.
    /// A level that retunes the player must shock it by its own number, not by the retail one.
    /// </param>
    public static ElectricFenceShock Shock(HumanHealth health, float damageFactor)
    {
        ArgumentNullException.ThrowIfNull(health);
        float damage = health.ApplyDirect(Zone, damageFactor);
        return new ElectricFenceShock(damage, health.IsDead);
    }
}

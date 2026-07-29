namespace OpenIGI.Game.Weapons;

/// <summary>
/// The pair of animations one <see cref="WeaponAction"/> plays, and how they are blended in.
/// </summary>
/// <param name="FullBody">The full-body clip, or <c>-1</c> to leave the body alone.</param>
/// <param name="UpperBody">The upper-body clip, or <c>-1</c> to leave the torso alone.</param>
/// <param name="LoopFullBody">Whether the full-body clip repeats.</param>
/// <param name="LoopUpperBody">Whether the upper-body clip repeats.</param>
/// <param name="FullBodyBlendSeconds">How long the full-body clip eases in.</param>
/// <param name="UpperBodyBlendSeconds">How long the upper-body clip eases in.</param>
public readonly record struct WeaponAnimations(
    int FullBody,
    int UpperBody,
    bool LoopFullBody,
    bool LoopUpperBody,
    float FullBodyBlendSeconds,
    float UpperBodyBlendSeconds);

/// <summary>
/// Which of a weapon's thirteen animations each action plays.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>0x4137E0</c>, which is the whole of the weapon animation dispatch: a switch on
/// the action picks one full-body and one upper-body clip out of the weapon record, then hands them to
/// <c>0x489F20</c> and <c>0x489ED0</c>.
/// </para>
/// <para>
/// The table is where the <b>upper-body split</b> becomes concrete: every action names <i>both</i> a
/// full-body and an upper-body clip, and the human plays whichever its skeleton has. That is why a
/// weapon's <c>UpperBody*</c> ids resolve in the player's animation set and its full-body ids do not —
/// the player's legs are already running a locomotion state, so only the torso track is used.
/// </para>
/// <para>
/// The blend is <c>0.4</c> seconds for the standing and moving actions and immediate for firing,
/// reloading and <see cref="WeaponAction.StandImmediate"/> — a shot has to look instant.
/// </para>
/// </remarks>
public static class WeaponAnimationTable
{
    /// <summary>The blend the standing and moving actions ease in over, in seconds.</summary>
    public const float DefaultBlendSeconds = 0.40000001f;

    /// <summary>Picks the animations an action plays on a weapon.</summary>
    /// <param name="weapon">The weapon being held.</param>
    /// <param name="action">What the human is doing.</param>
    /// <param name="fireVariant">
    /// Which firing clip <see cref="WeaponAction.Fire"/> uses — 0, 1 or 2, cycling through
    /// <see cref="WeaponType.FireAnimation1"/> to <see cref="WeaponType.FireAnimation3"/>. Ignored by
    /// every other action.
    /// </param>
    /// <returns>The pair to play. Either id may be <c>-1</c>, meaning play nothing on that track.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An action the original's switch does not name returns a pair of <c>-1</c>, matching its
    /// <c>default:</c>, which returns without touching either track.
    /// </remarks>
    public static WeaponAnimations Select(WeaponType weapon, WeaponAction action, int fireVariant = 0)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        const float eased = DefaultBlendSeconds;
        const float immediate = 0f;

        return action switch
        {
            WeaponAction.Stand or WeaponAction.StandAlternate =>
                new(weapon.StandAnimation, weapon.UpperBodyStandAnimation, true, true, eased, eased),

            WeaponAction.Walk =>
                new(weapon.MoveAnimation, weapon.UpperBodyWalkAnimation, true, true, eased, eased),

            // Run shares the full-body moving clip with Walk; only the torso differs.
            WeaponAction.Run =>
                new(weapon.MoveAnimation, weapon.UpperBodyRunAnimation, true, true, eased, eased),

            WeaponAction.Crouch =>
                new(weapon.StandAnimation, weapon.UpperBodyCrouchAnimation, true, true, eased, eased),

            WeaponAction.CrouchRun =>
                new(weapon.StandAnimation, weapon.UpperBodyCrouchRunAnimation, true, true, eased, eased),

            // Firing and reloading are one-shot and blend in immediately.
            WeaponAction.FireTimed =>
                new(weapon.FireAnimation1, weapon.UpperBodyFireAnimation, false, false, immediate, immediate),

            WeaponAction.Reload =>
                new(weapon.ReloadAnimation, weapon.UpperBodyReloadAnimation, false, false, immediate, immediate),

            WeaponAction.Fire =>
                new(FireClip(weapon, fireVariant), weapon.UpperBodyFireAnimation, false, false, immediate, immediate),

            // Standing again, but snapped rather than eased on the full-body track.
            WeaponAction.StandImmediate =>
                new(weapon.StandAnimation, weapon.UpperBodyStandAnimation, true, true, immediate, immediate),

            _ => new(-1, -1, false, false, immediate, immediate),
        };
    }

    private static int FireClip(WeaponType weapon, int variant) => variant switch
    {
        1 => weapon.FireAnimation2,
        2 => weapon.FireAnimation3,
        _ => weapon.FireAnimation1,
    };
}

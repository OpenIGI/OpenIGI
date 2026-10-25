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
/// The table is where the <b>upper-body split</b> becomes concrete, and the split is between two
/// <i>skeletons</i> rather than two tracks on one. <c>0x489F20</c> plays the full-body id on <b>bone 0
/// of the first-person view rig</b> — <c>human-&gt;view-&gt;boneObj</c>, which <c>0x464660</c> built
/// from skeleton 5 — and <c>0x489ED0</c> plays the upper-body id on <b>bone 1 of the human's own
/// skeleton-0 rig</b>, with orientation type 5 over the root orientation at <c>Human+1616</c>.
/// </para>
/// <para>
/// So a weapon's full-body ids are ids in <c>005.iff</c> and its <c>UpperBody*</c> ids are ids in
/// <c>000.iff</c>. An earlier note here read the full-body ids as simply not resolving in the player's
/// set; they resolve, in the other set. <c>RetailHumanViewTests</c> checks every full-body id of every
/// one of the 24 weapons against <c>005.iff</c>.
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

    /// <summary>The SPAS-12 reload's opening clip, a number <c>0x472AE0</c> carries itself (<c>0x3D</c>).</summary>
    public const int ShellReloadOpenClip = 0x3D;

    /// <summary>The clip one shell goes in with (<c>0x3E</c>).</summary>
    public const int ShellReloadInsertClip = 0x3E;

    /// <summary>The reload's closing clip (<c>0x3F</c>).</summary>
    public const int ShellReloadCloseClip = 0x3F;

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

            // Both tracks go quiet past the third variant, not just the full-body one.
            WeaponAction.Fire when fireVariant is < 0 or >= 3 =>
                new(-1, -1, false, false, immediate, immediate),

            WeaponAction.Fire =>
                new(FireClip(weapon, fireVariant), weapon.UpperBodyFireAnimation, false, false, immediate, immediate),

            // 0x40FB90 starts the medipack's raw second fire slot with the loop flag set after its
            // one-shot entry clip completes. Do not substitute FireAnimation1 when that slot is -1:
            // the retail dispatcher receives the raw value, and replaying the entry clip made a held
            // medipack look like repeated one-shot injections (and, in some records, a reload).
            WeaponAction.MedipackLoop when weapon.Category == WeaponCategory.Medipack =>
                new(
                    weapon.FireAnimation2,
                    weapon.UpperBodyFireAnimation,
                    true,
                    true,
                    immediate,
                    immediate),

            // 0x472AE0 plays its three clips by number on the view rig alone (0x489F20), leaving the torso as it was.
            WeaponAction.ShellReloadOpen =>
                new(ShellReloadOpenClip, -1, false, false, eased, immediate),

            WeaponAction.ShellReloadInsert =>
                new(ShellReloadInsertClip, -1, false, false, immediate, immediate),

            WeaponAction.ShellReloadClose =>
                new(ShellReloadCloseClip, -1, false, false, immediate, immediate),

            // Standing again, but snapped rather than eased on the full-body track.
            WeaponAction.StandImmediate =>
                new(weapon.StandAnimation, weapon.UpperBodyStandAnimation, true, true, immediate, immediate),

            _ => new(-1, -1, false, false, immediate, immediate),
        };
    }

    /// <summary>
    /// How much of a clip <see cref="WeaponAction.FireTimed"/> skips, given one uniform sample.
    /// </summary>
    /// <param name="uniformSample">A draw on 0–1 from the engine's own stream.</param>
    /// <returns>The clip time to start at, in the clip's own units.</returns>
    /// <remarks>
    /// <para>
    /// The original computes <c>flt_A54658 * (rand01() * 0.175) * 30.0</c> and hands it to
    /// <c>sub_4D65B0</c>, which writes it to the clip's <b><c>+44</c></b>. <c>+44</c> is the
    /// <i>clock</i> — <c>sub_4D4B60</c> does <c>clip[+44] += clip[+40]</c> and compares <c>+44</c>
    /// against the duration — and the <i>rate</i> is <c>+40</c>, which a different function sets. So a
    /// shot's animation <b>starts part-way in</b> rather than playing at a random speed. An earlier note
    /// here had it the other way round.
    /// </para>
    /// <para>
    /// <c>flt_A54658</c> is <c>4800 / 30 = 160</c>, set once by <c>sub_4D2D30(30.0)</c>. The same
    /// constant converts a blend from seconds to ticks, which is the cross-check on its value.
    /// </para>
    /// </remarks>
    public static float TimedFireStart(float uniformSample) =>
        ClipTimeScale * (uniformSample * TimedFireSpread) * 30f;

    /// <summary>The engine's clip-time scale, <c>4800 / 30</c>.</summary>
    public const float ClipTimeScale = 160f;

    /// <summary>How much of the clip a timed shot may skip, before scaling.</summary>
    /// <remarks><c>0.175</c>, the literal in <c>0x4137E0</c>'s action 6.</remarks>
    public const float TimedFireSpread = 0.175f;

    // The switch is a chain of equality tests with no wrap: at three and above BOTH ids stay at their
    // initial -1, and -1 means play nothing. It is not `variant % 3`. In retail the fall-through is
    // unreachable because the striking state caps its own requests below three, but a port that wrapped
    // instead would play a fourth swing the original does not.
    private static int FireClip(WeaponType weapon, int variant) => variant switch
    {
        0 => weapon.FireAnimation1,
        1 => weapon.FireAnimation2,
        2 => weapon.FireAnimation3,
        _ => -1,
    };
}

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The mutable half of a gun: what changes as it is fired.
/// </summary>
/// <remarks>
/// <para>
/// These are the fields of the original's 432-byte <c>Gun</c> instance that the firing path reads and
/// writes — <c>+316</c> to <c>+424</c>. The immutable half is the <see cref="WeaponType"/> record the
/// gun caches at <c>+296</c> and the <see cref="AmmoType"/> it looks up by calibre at <c>+300</c>.
/// </para>
/// <para>
/// Named rather than numbered because the offsets are meaningless out of context, but each property's
/// remarks give its original offset so the mapping stays checkable.
/// </para>
/// </remarks>
public sealed class GunState
{
    /// <summary>Gets or sets the ticks left before another round may go off.</summary>
    /// <remarks>The original's <c>+316</c>, decremented once per tick and never below zero.</remarks>
    public int CooldownTicks { get; set; }

    /// <summary>Gets or sets how many rounds this trigger pull has fired.</summary>
    /// <remarks>
    /// The original's <c>+320</c>. Reset to zero the moment the trigger is released, which is what
    /// makes releasing and pulling again restart a burst.
    /// </remarks>
    public int RoundsThisBurst { get; set; }

    /// <summary>Gets or sets the rounds left in the magazine.</summary>
    /// <remarks>
    /// The original's <c>+324</c>, starting at <see cref="GunTiming.UnloadedClipSentinel"/> until the
    /// first tick replaces it with the weapon's capacity.
    /// </remarks>
    public int RoundsInClip { get; set; } = GunTiming.UnloadedClipSentinel;

    /// <summary>Gets or sets how long the trigger has been held, ramping 0 to 1.</summary>
    /// <remarks>The original's <c>+328</c>; see <see cref="GunTiming.AdvanceTriggerRamp"/>.</remarks>
    public float TriggerRamp { get; set; }

    /// <summary>Gets or sets the recoil phase, charged on each round and decaying to zero.</summary>
    /// <remarks>The original's <c>+408</c>; see <see cref="GunTiming.AdvanceRecoilPhase"/>.</remarks>
    public float RecoilPhase { get; set; }

    /// <summary>Gets or sets the first effect sub-counter.</summary>
    /// <remarks>The original's <c>+420</c>, paired with <see cref="FirstSubCounterDelay"/>.</remarks>
    public int FirstSubCounter { get; set; }

    /// <summary>Gets or sets the ticks left before <see cref="FirstSubCounter"/> steps.</summary>
    /// <remarks>The original's <c>+424</c>.</remarks>
    public int FirstSubCounterDelay { get; set; }

    /// <summary>Gets or sets the second effect sub-counter.</summary>
    /// <remarks>The original's <c>+412</c>, paired with <see cref="SecondSubCounterDelay"/>.</remarks>
    public int SecondSubCounter { get; set; }

    /// <summary>Gets or sets the ticks left before <see cref="SecondSubCounter"/> steps.</summary>
    /// <remarks>The original's <c>+416</c>.</remarks>
    public int SecondSubCounterDelay { get; set; }

    /// <summary>Loads the magazine if it has never been loaded.</summary>
    /// <param name="weapon">The weapon this gun is.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The first thing the per-tick does (<c>0x478640</c>), and only ever once — after this the count
    /// is whatever firing and reloading leave it at.
    /// </remarks>
    public void EnsureLoaded(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        if (RoundsInClip == GunTiming.UnloadedClipSentinel)
        {
            RoundsInClip = weapon.RoundsPerClip;
        }
    }

    /// <summary>Applies everything a round going off changes.</summary>
    /// <param name="weapon">The weapon being fired.</param>
    /// <param name="consumesAmmunition">Whether the gun has an ammunition record to draw from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The tail of <c>Gun_Fire</c>. The magazine is only decremented when it is above zero <i>and</i>
    /// the gun has an ammunition record, so an emplacement gun fires for ever.
    /// </remarks>
    public void OnRoundFired(WeaponType weapon, bool consumesAmmunition)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        int rate = weapon.RoundsPerMinute;

        RoundsThisBurst++;
        CooldownTicks = GunTiming.TicksPerRound(rate);

        if (consumesAmmunition && RoundsInClip > 0)
        {
            RoundsInClip--;
        }

        RecoilPhase = GunTiming.RecoilPhaseOnFire;
        FirstSubCounter = GunTiming.FirstSubCounterStart;
        SecondSubCounter = GunTiming.SecondSubCounterStart;
        FirstSubCounterDelay = GunTiming.FirstSubCounterPeriod(rate);
        SecondSubCounterDelay = GunTiming.SecondSubCounterPeriod(rate);
    }

    /// <summary>Advances one tick of the gun's clock.</summary>
    /// <param name="weapon">The weapon this gun is.</param>
    /// <param name="triggerHeld">Whether the trigger is down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Order matters and is the original's: load, then cool down, then decay the recoil, then step the
    /// two sub-counters, then advance the trigger ramp.
    /// </remarks>
    public void Tick(WeaponType weapon, bool triggerHeld)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        EnsureLoaded(weapon);

        if (CooldownTicks != 0)
        {
            CooldownTicks--;
        }

        int rate = weapon.RoundsPerMinute;
        RecoilPhase = GunTiming.AdvanceRecoilPhase(RecoilPhase, rate);

        if (FirstSubCounter != 0)
        {
            if (FirstSubCounterDelay != 0)
            {
                FirstSubCounterDelay--;
            }
            else
            {
                FirstSubCounter++;
                FirstSubCounterDelay = GunTiming.FirstSubCounterPeriod(rate);
            }
        }

        if (SecondSubCounter != 0)
        {
            if (SecondSubCounterDelay != 0)
            {
                SecondSubCounterDelay--;
            }
            else
            {
                SecondSubCounter++;
                SecondSubCounterDelay = GunTiming.SecondSubCounterPeriod(rate);
            }
        }

        // The trigger ramp is not part of the original's per-tick - Gun_Fire advances it - but it is a
        // per-tick quantity and keeping it here is what makes the state consistent after one Tick call.
        TriggerRamp = GunTiming.AdvanceTriggerRamp(TriggerRamp, triggerHeld);

        if (!triggerHeld)
        {
            RoundsThisBurst = 0;
        }
    }
}

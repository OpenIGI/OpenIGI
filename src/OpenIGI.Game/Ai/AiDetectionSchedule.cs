namespace OpenIGI.Game.Ai;

/// <summary>
/// The three-level cost cascade the detection sweep runs behind: a phase gate on the whole sweep, then a
/// rate-limited cone test per candidate, then a rate-limited ray behind that.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x489750</c> (the sweep) and <c>0x4898D0</c> (the per-candidate test). The AI never looks at
/// everyone every tick:
/// </para>
/// <list type="number">
/// <item>
/// <b>the sweep runs one tick in four</b>, and the AI's own phase decides which — so AIs are <b>staggered</b>
/// across the four ticks rather than all sweeping together;
/// </item>
/// <item>
/// the <b>cone test</b> runs at most once per <see cref="ConeInterval"/> sweeps;
/// </item>
/// <item>
/// the <b>line-of-sight ray</b> runs at most once per <see cref="RayInterval"/> sweeps, and <b>only while the
/// target is already in a cone</b> — so the expensive test is never paid for someone behind you.
/// </item>
/// </list>
/// <para>
/// Detection latency is therefore the product of the three, not any one of them. The sweep is also
/// <b>O(every human in the level)</b> per AI: it walks the engine's global registry, with a fixed slot per
/// index and no spatial index at all.
/// </para>
/// </remarks>
public sealed class AiDetectionSchedule
{
    /// <summary>How many ticks the sweep phases are spread over.</summary>
    /// <remarks><c>0x489750</c> compares the AI's phase against <c>tick % 4</c>.</remarks>
    public const int PhaseCount = 4;

    /// <summary>Initializes a new instance of the <see cref="AiDetectionSchedule"/> class.</summary>
    /// <param name="phase">Which of the four ticks this AI sweeps on.</param>
    /// <param name="coneInterval">The cone test's period in sweeps, from the vision header's <c>+4</c>.</param>
    /// <param name="rayInterval">The ray's period in sweeps, from the vision header's <c>+8</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The phase is outside <c>[0, 4)</c> or an interval is negative.</exception>
    public AiDetectionSchedule(int phase, int coneInterval, int rayInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(phase);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(phase, PhaseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(coneInterval);
        ArgumentOutOfRangeException.ThrowIfNegative(rayInterval);

        Phase = phase;
        ConeInterval = coneInterval;
        RayInterval = rayInterval;
    }

    /// <summary>Gets which of the four ticks this AI sweeps on — the vision header's <c>+12</c>.</summary>
    public int Phase { get; }

    /// <summary>Gets the cone test's period in sweeps.</summary>
    public int ConeInterval { get; }

    /// <summary>Gets the ray's period in sweeps.</summary>
    public int RayInterval { get; }

    /// <summary>Gets a value indicating whether the sweep runs on a tick.</summary>
    /// <param name="tick">The tick counter.</param>
    /// <returns><see langword="true"/> when this AI's phase comes up.</returns>
    public bool SweepsOn(int tick) => Phase == tick % PhaseCount;

    /// <summary>Runs one candidate through the cascade, as <c>0x4898D0</c> does.</summary>
    /// <param name="slot">The candidate's slot, updated in place.</param>
    /// <param name="coneTest">Whether the target is inside a cone. Called only when the cone cooldown is spent.</param>
    /// <param name="rayTest">
    /// Whether the line to the target is clear. Called only when the target is in a cone and the ray cooldown is
    /// spent.
    /// </param>
    /// <param name="distance">The current distance, which is stored unconditionally on every sweep.</param>
    /// <exception cref="ArgumentNullException">A test is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The cooldown arithmetic is worth stating exactly, because it is off by one from the obvious reading: a
    /// test sets its cooldown to <c>interval + 1</c> and the same call then decrements it, leaving
    /// <c>interval</c>. The cooldown has to reach zero again before the next test, which takes <c>interval</c>
    /// further sweeps — so the effective period is <b><c>interval + 1</c> sweeps</b>, and an interval of zero
    /// tests on every sweep.
    /// </para>
    /// <para>
    /// The asymmetry that matters: <b>losing the cone clears the visible flag at once, but gaining it does not
    /// set it</b> — the ray still has to run. So an AI loses sight of you instantly and re-acquires you no
    /// sooner than the ray period allows.
    /// </para>
    /// <para>
    /// And the ray's cooldown is decremented <b>only while the target is in a cone</b>, so time spent out of
    /// the cone does not count towards the next ray. Stepping out of view and back in does not shorten the
    /// wait.
    /// </para>
    /// </remarks>
    public void Sweep(AiDetectionSlot slot, Func<bool> coneTest, Func<bool> rayTest, double distance)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(coneTest);
        ArgumentNullException.ThrowIfNull(rayTest);

        if (slot.ConeCooldown == 0)
        {
            slot.IsInCone = coneTest();
            if (!slot.IsInCone)
            {
                // Leaving the cone takes the visibility with it, without waiting for a ray.
                slot.IsVisible = false;
            }

            slot.ConeCooldown = ConeInterval + 1;
        }

        if (slot.IsInCone)
        {
            if (slot.RayCooldown == 0)
            {
                slot.IsVisible = rayTest();
                slot.RayCooldown = RayInterval + 1;
            }

            slot.RayCooldown--;
        }

        slot.ConeCooldown--;
        slot.Distance = distance;
    }
}

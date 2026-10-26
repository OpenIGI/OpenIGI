using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What the patrol combat arm decided to do this tick — <c>sub_48BFD0</c>'s eight outcomes.</summary>
/// <remarks>
/// The numbering is the original's own: <c>0x48ABA0</c>'s stance per-ticks switch on exactly these
/// values.
/// </remarks>
public enum AiPatrolAction
{
    /// <summary>Reload — <c>0x48DB10</c>, <c>"Stand - Reload"</c>.</summary>
    Reload = 0,

    /// <summary>Throw a grenade — <c>0x48DBF0</c>, <c>"Stand - Throwing grenade"</c>.</summary>
    ThrowGrenade = 1,

    /// <summary>Fire from where it stands — <c>0x48D8B0</c>, <c>"Stand - Fire"</c>.</summary>
    Fire = 2,

    /// <summary>Melee — <c>0x48DBB0</c>, <c>"Stand - CloseCombat Trigger"</c>, channel 9.</summary>
    CloseCombat = 3,

    /// <summary>Walk back onto its own claimed node — <c>0x48C760</c>, <c>"Combat - RePosition"</c>.</summary>
    RePosition = 4,

    /// <summary>Advance on, or hunt for, the target — <c>0x48C860</c>, <c>"Combat - Follow"</c>.</summary>
    Follow = 5,

    /// <summary>Turn and watch — <c>0x48CAB0</c>, <c>"Combat - LookForTarget"</c>.</summary>
    LookForTarget = 6,

    /// <summary>End the action.</summary>
    End = 7,
}

/// <summary>What <c>sub_48BE30</c> decided about throwing itself out of the way.</summary>
public enum AiEvasiveAction
{
    /// <summary>Nothing this tick.</summary>
    None = -1,

    /// <summary>Drop prone and fight from there.</summary>
    LieDown = 0,

    /// <summary>Roll left.</summary>
    RollLeft = 1,

    /// <summary>Roll right.</summary>
    RollRight = 2,
}

/// <summary>Which sides a roll has three metres of nav node on — <c>sub_4526E0</c>.</summary>
[Flags]
public enum AiRollSpace
{
    /// <summary>Neither side.</summary>
    None = 0,

    /// <summary>Three metres of node to the left.</summary>
    Left = 1,

    /// <summary>Three metres of node to the right.</summary>
    Right = 2,
}

/// <summary>What the patrol arm can see of the body and the world this tick.</summary>
/// <param name="HasDetectionSlot">
/// Whether the vision subsystem holds a slot for the target — <c>HumanAI+15844</c>. Without one the
/// action ends.
/// </param>
/// <param name="Visible">The slot's <c>+41</c>: inside a cone <i>and</i> a clear line.</param>
/// <param name="DistanceToTargetInUnits">The argument block's <c>+8</c>, <c>vTargetDistance</c>.</param>
/// <param name="CurrentNodeId">The node the soldier is standing on — <c>HumanAI+15916</c>.</param>
/// <param name="StandingOnOwnNode">
/// Whether the body is inside its current node's box with a deviance of 0.3 — the test
/// <c>sub_4F9A40</c> performs when there is no follow destination.
/// </param>
/// <param name="AmmoInClip">Rounds left in the held weapon's clip.</param>
/// <param name="HasGrenade">
/// <c>HumanAI+15869</c>. <c>0x44FBA0</c> sets it when any carried inventory weapon has weapon category
/// 4 (<c>WEAPONTYPE_GRENADE</c>); the held slot does not need to be the grenade.
/// </param>
/// <param name="AimersOnTarget">How many attackers already hold an aiming mode on this target.</param>
/// <param name="FreePositionTowardTarget">
/// Runs <c>FindFreePosition</c> with the toward-the-target test <b>on</b>, answering
/// <see langword="null"/> when it finds nothing. A search claims the node it accepts, so the chooser
/// calls it only on the branch where <c>sub_48BFD0</c> does.
/// </param>
/// <param name="FreePositionAwayFromTarget">
/// Runs it with that test <b>off</b> — the search query.
/// </param>
public readonly record struct AiPatrolSense(
    bool HasDetectionSlot,
    bool Visible,
    double DistanceToTargetInUnits,
    int CurrentNodeId,
    bool StandingOnOwnNode,
    int AmmoInClip,
    bool HasGrenade,
    int AimersOnTarget,
    Func<int?>? FreePositionTowardTarget,
    Func<int?>? FreePositionAwayFromTarget);

/// <summary>
/// The move-and-fight arm — <c>0x48ABA0</c>'s decision half, i.e. <c>sub_48BFD0</c> (the action choice)
/// and <c>sub_48BE30</c> (the evasive decision).
/// </summary>
/// <remarks>
/// <para>
/// <c>AIAction_Combat</c> dispatches every <c>PATROL_*</c>, <c>SECURITY_PATROL_*</c>,
/// <c>MAFIA_PATROL_*</c> and <c>SPETNAZ_PATROL_*</c> type here — most soldiers in the game — where the
/// twelve <c>GUARD_*</c> types, the sniper and Priboi go to <see cref="AiGuardCombat"/> instead. The two
/// arms are genuinely different behaviours and this one is <b>not</b> built on the other: a guard holds
/// its ground, a patrol advances, repositions and hunts.
/// </para>
/// <para>
/// This class is the chooser only. The four behaviours it selects need the movement stepper and the
/// stance channels, which live on the soldier; keeping the decision pure is what makes it testable
/// against the retail settings rows.
/// </para>
/// </remarks>
public sealed class AiPatrolCombat
{
    /// <summary>
    /// The melee range — <c>HumanAI+15792</c>, a <see langword="double"/> whose default is 6144.0.
    /// </summary>
    /// <remarks>
    /// 1.5 m. The constructor writes the field as two dword stores, <c>0</c> then <c>0x40B80000</c>, and
    /// nothing else in the image writes it — so this is the shipped melee range for every AI. The second
    /// store is not a separate <c>float</c> field starting at 5.75, as it can be misread; it is the upper
    /// half of this double.
    /// </remarks>
    public const double CloseCombatRangeInUnits = 6144.0;

    /// <summary>How many attackers crowd a patrol out of firing — three, where a guard's figure is four.</summary>
    /// <remarks>
    /// Both are literal in the binary and they genuinely disagree; the third-and-later attacker on a
    /// patrol goes to <see cref="AiPatrolAction.LookForTarget"/> instead of firing.
    /// </remarks>
    public const int CrowdedAimerCount = 3;

    /// <summary>The search cycle's period, in ticks — one second.</summary>
    public const int SearchCycleTicks = 30;

    /// <summary>The last tick of the search cycle spent watching rather than moving.</summary>
    public const int SearchWatchTicks = 15;

    /// <summary>How often an evasion may even be considered — <c>HumanAI+15864</c>, ten ticks.</summary>
    public const int EvasionCooldownTicks = 10;

    private readonly AiCombatParameters _parameters;
    private readonly Func<double> _random;

    private int _notVisibleTicks;
    private int _evasionAllowedAtTick = -1;
    private AiEvasiveAction _lastRoll = AiEvasiveAction.None;

    /// <summary>Initializes a new instance of the <see cref="AiPatrolCombat"/> class.</summary>
    /// <param name="parameters">The AI's combat row, keyed by type and difficulty.</param>
    /// <param name="random">A source of values in <c>[0, 1)</c>.</param>
    /// <param name="closeQuarters">
    /// Whether the chooser considers the rifle butt and the grenade. Anya's and Ekk's arm (<c>0x48AC60</c>)
    /// zeroes the close-combat range and runs its own chooser, <c>0x48AE90</c>, which has neither test.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public AiPatrolCombat(AiCombatParameters parameters, Func<double> random, bool closeQuarters = true)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);
        _parameters = parameters;
        _random = random;
        _closeQuarters = closeQuarters;
    }

    private readonly bool _closeQuarters;

    /// <summary>Gets the node the soldier is walking to, or −1 — <c>HumanAI+15848</c>.</summary>
    public int DestinationNodeId { get; private set; } = -1;

    /// <summary>Gets how many consecutive ticks the target has been out of sight — the block's <c>+24</c>.</summary>
    /// <remarks>
    /// <b>Finding somewhere new to look resets this to zero</b>, which is what makes a patrol hunt for as
    /// long as there is anywhere left to hunt. The targeting timeout only starts running once the search
    /// is exhausted.
    /// </remarks>
    public int NotVisibleTicks => _notVisibleTicks;

    /// <summary>Gets the direction of the last roll, so the next one goes the other way.</summary>
    public AiEvasiveAction LastRoll => _lastRoll;

    /// <summary>Arms the behaviour.</summary>
    public void Begin()
    {
        DestinationNodeId = -1;
        _notVisibleTicks = 0;
        _evasionAllowedAtTick = -1;
        _lastRoll = AiEvasiveAction.None;
    }

    /// <summary>Chooses this tick's action — <c>sub_48BFD0</c>.</summary>
    /// <param name="sense">What the body and the world look like.</param>
    /// <returns>The action to install.</returns>
    public AiPatrolAction Choose(in AiPatrolSense sense)
    {
        // Read AND cleared, exactly as the guard arm's miss counter is: only the not-visible path below
        // writes it back, so it counts CONSECUTIVE ticks.
        int lostTicks = _notVisibleTicks;
        _notVisibleTicks = 0;

        if (!sense.HasDetectionSlot)
        {
            return AiPatrolAction.End;
        }

        // (a) Standing somewhere other than the node it claimed, and not already walking anywhere.
        if (DestinationNodeId == -1 && !sense.StandingOnOwnNode)
        {
            return AiPatrolAction.RePosition;
        }

        if (sense.Visible)
        {
            // (b) Close enough for the rifle butt.
            if (_closeQuarters && sense.DistanceToTargetInUnits < CloseCombatRangeInUnits)
            {
                return AiPatrolAction.CloseCombat;
            }

            if (sense.AmmoInClip > 0)
            {
                // (c) Too far to shoot from here? Walk at him.
                if (sense.DistanceToTargetInUnits > _parameters.TrackingDistanceInUnits
                    && sense.FreePositionTowardTarget?.Invoke() is int toward
                    && toward != sense.CurrentNodeId)
                {
                    DestinationNodeId = toward;
                    return AiPatrolAction.Follow;
                }

                // (d) Grenade? With a loaded clip 0x48BFD0 draws against +15756, the row's
                // "grenade throw when reload probability" -- not the plain one, which is (f)'s.
                if (WantsGrenade(sense, _parameters.GrenadeThrowWhenReloadingProbability))
                {
                    return AiPatrolAction.ThrowGrenade;
                }

                // (e) Is somebody else already shooting at him?
                return sense.AimersOnTarget >= CrowdedAimerCount
                    ? AiPatrolAction.LookForTarget
                    : AiPatrolAction.Fire;
            }

            // (f) Empty clip: a grenade instead of reloading, drawn against +15752.
            return WantsGrenade(sense, _parameters.GrenadeThrowProbability)
                ? AiPatrolAction.ThrowGrenade
                : AiPatrolAction.Reload;
        }

        // (g) Out of sight. This is the search.
        _notVisibleTicks = lostTicks + 1;

        if (_notVisibleTicks % SearchCycleTicks <= SearchWatchTicks)
        {
            return WatchOrGiveUp();
        }

        if (sense.FreePositionAwayFromTarget?.Invoke() is int away && away != sense.CurrentNodeId)
        {
            DestinationNodeId = away;
            _notVisibleTicks = 0;   // <<< somewhere new to look RESETS the timeout
            return AiPatrolAction.Follow;
        }

        return WatchOrGiveUp();
    }

    /// <summary>Decides whether to throw itself out of the way — <c>sub_48BE30</c>.</summary>
    /// <param name="currentTick">The engine tick, for the ten-tick cooldown.</param>
    /// <param name="distanceToTargetInUnits">The argument block's <c>+8</c>.</param>
    /// <param name="rollSpace">Which sides have three metres of nav node — <c>sub_4526E0</c>.</param>
    /// <param name="underFire">Whether TakingDamage, Gunshot, or GunshotMiss was drained this tick.</param>
    /// <param name="onForbiddenNode">Whether the current node carries <c>NODECRITERIA_STAIR</c>.</param>
    /// <returns>What to do, or <see cref="AiEvasiveAction.None"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>The cooldown is set unconditionally, before any other gate</b>, so an evasion is considered at
    /// most three times a second however the rest of the tests come out.
    /// </para>
    /// <para><c>ai+2232/+2236/+2252</c> are the drained-count array entries for event ids 15, 16 and 20:
    /// TakingDamage, Gunshot and GunshotMiss. <c>dword_A7B2D8</c> is initialized from
    /// <c>NODECRITERIA_STAIR</c> by <c>0x508F80</c>.</para>
    /// </remarks>
    public AiEvasiveAction ChooseEvasion(
        int currentTick,
        double distanceToTargetInUnits,
        AiRollSpace rollSpace,
        bool underFire,
        bool onForbiddenNode)
    {
        // The first draw is taken BEFORE the cooldown test in the original, and it is the one the
        // roll/lie-down comparison uses. Drawing it here keeps the random stream identical.
        double rollOrLieDown = _random();

        if (_evasionAllowedAtTick >= 0 && _evasionAllowedAtTick >= currentTick)
        {
            return AiEvasiveAction.None;
        }

        _evasionAllowedAtTick = currentTick + EvasionCooldownTicks;

        if (!underFire)
        {
            return AiEvasiveAction.None;
        }

        if (onForbiddenNode)
        {
            return AiEvasiveAction.None;
        }

        if (_random() > _parameters.EvasiveActionProbability)
        {
            return AiEvasiveAction.None;
        }

        // The name reads the opposite way round to the behaviour: this is the chance of ROLLING, and the
        // soldier lies down when the draw EXCEEDS it. Zero means always lie down, tested exactly.
        float rollProbability = _parameters.EvasiveStandRollOrLieDownProbability;
        if (rollProbability == 0.0f
            || (rollProbability < rollOrLieDown && distanceToTargetInUnits > _parameters.LieDownMinimumDistanceInUnits))
        {
            return AiEvasiveAction.LieDown;
        }

        double side = _random();
        float sideProbability = _parameters.EvasiveStandRollSideProbability;

        // A soldier cannot roll the same way twice: each side is excluded by the last direction taken.
        bool canLeft = rollSpace.HasFlag(AiRollSpace.Left) && _lastRoll != AiEvasiveAction.RollLeft;
        bool canRight = rollSpace.HasFlag(AiRollSpace.Right) && _lastRoll != AiEvasiveAction.RollRight;

        if (canLeft && (sideProbability == 0.0f || sideProbability < side))
        {
            _lastRoll = AiEvasiveAction.RollLeft;
            return AiEvasiveAction.RollLeft;
        }

        if (canRight && (sideProbability == 1.0f || sideProbability >= side))
        {
            _lastRoll = AiEvasiveAction.RollRight;
            return AiEvasiveAction.RollRight;
        }

        return AiEvasiveAction.None;
    }

    /// <summary>Clears the follow destination — what <c>Follow</c> does once it arrives.</summary>
    public void ArrivedAtDestination() => DestinationNodeId = -1;

    /// <summary>Points a running Follow at a new node — the re-search <c>0x48C860</c> makes.</summary>
    /// <param name="node">The node found toward the target.</param>
    public void RedirectTo(int node) => DestinationNodeId = node;

    private AiPatrolAction WatchOrGiveUp() =>
        _notVisibleTicks > _parameters.TargetingTimeoutTicks
            ? AiPatrolAction.End
            : AiPatrolAction.LookForTarget;

    private bool WantsGrenade(in AiPatrolSense sense, float probability) =>
        _closeQuarters
        && sense.HasGrenade
        && sense.DistanceToTargetInUnits > _parameters.GrenadeMinimumDistanceInUnits
        && sense.DistanceToTargetInUnits < _parameters.GrenadeMaximumDistanceInUnits
        && _random() <= probability;
}

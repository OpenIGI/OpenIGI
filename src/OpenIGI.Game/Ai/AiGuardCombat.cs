using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Ai;

/// <summary>Which of the guard behaviour's three sub-states owns the tick.</summary>
/// <remarks>
/// The original keeps this as a function pointer at <c>action+36</c>: null is the stand tick,
/// <c>sub_48D8B0</c> the firing one and <c>sub_48DB10</c> the reload. A sub-state that returns true
/// clears the pointer and gives the tick back.
/// </remarks>
public enum AiCombatPhase
{
    /// <summary>Standing, deciding — <c>"Guard Combat - Stand"</c>, no sub-state installed.</summary>
    Stand = 0,

    /// <summary>Spending a burst — <c>"Stand - Fire"</c>.</summary>
    Fire = 1,

    /// <summary>Reloading — <c>"Stand - Reload"</c>.</summary>
    Reload = 2,

    /// <summary>The action has ended.</summary>
    Ended = 3,
}

/// <summary>What the body looks like to the combat behaviour this tick.</summary>
/// <param name="HasDetectionSlot">
/// Whether the vision subsystem holds a slot for the target at all — <c>HumanAI+15844</c>. Without one
/// the action ends immediately.
/// </param>
/// <param name="InCone">The slot's <c>+40</c>: the target is inside a view cone.</param>
/// <param name="Visible">The slot's <c>+41</c>: the cone <i>and</i> a clear line.</param>
/// <param name="TargetPosition">Where to aim — the target's position raised by half a metre.</param>
/// <param name="DistanceToTargetInUnits">How far the target is, for the accuracy interpolation.</param>
/// <param name="LocomotionState">The body's current locomotion state — <c>Human+764</c>.</param>
/// <param name="AmmoInClip">How many rounds are left in the held weapon's clip.</param>
/// <param name="Category">The held weapon's category.</param>
/// <param name="ClipSize">The held weapon's clip size.</param>
/// <param name="BurstSize">The held weapon's own burst length.</param>
/// <param name="AimersOnTarget">
/// How many attackers already hold <see cref="AiAimMode.Aiming"/> on this target, this one included.
/// </param>
public readonly record struct AiCombatSense(
    bool HasDetectionSlot,
    bool InCone,
    bool Visible,
    Vector3d TargetPosition,
    double DistanceToTargetInUnits,
    int LocomotionState,
    int AmmoInClip,
    WeaponCategory Category,
    int ClipSize,
    int BurstSize,
    int AimersOnTarget);

/// <summary>What the combat behaviour asks the body for this tick.</summary>
/// <param name="Ended">The action is over and the AI should go back to what it was doing.</param>
/// <param name="Aim">The aim mode to write into the shared target record, if any.</param>
/// <param name="TurnTo">A world point to turn toward, or <see langword="null"/> to leave the facing alone.</param>
/// <param name="Aimed">Whether the turn reached its two-degree tolerance, once one was asked for.</param>
/// <param name="PullTrigger">Raise input slot <see cref="Player.PlayerInputSlot.Fire"/> this tick.</param>
/// <param name="ReleaseTrigger">Write zero to that slot this tick.</param>
/// <param name="PressReload">
/// Raise input slot <see cref="Player.PlayerInputSlot.Reload"/> — the reload that goes through the
/// player's own interface.
/// </param>
/// <param name="ReloadWeaponDirectly">Ask the weapon to reload itself instead.</param>
/// <param name="FirePose">Raise the firing-pose channel, <see cref="AiCombatChannel.Fire"/>.</param>
/// <param name="StartedBurst">The burst that has just been set up, when one has.</param>
public readonly record struct AiCombatCommand(
    bool Ended,
    AiAimMode? Aim,
    Vector3d? TurnTo,
    bool Aimed,
    bool PullTrigger,
    bool ReleaseTrigger,
    bool PressReload,
    bool ReloadWeaponDirectly,
    bool FirePose,
    AiCombatBurst? StartedBurst);

/// <summary>
/// A guard standing its ground and fighting — <c>0x48D000</c>, <c>0x48D080</c>, <c>0x48D8B0</c>,
/// <c>0x48D8E0</c> and <c>0x48DB10</c> as one tick machine.
/// </summary>
/// <remarks>
/// <para>
/// This is the behaviour <c>AIAction_Combat</c> installs for the sniper, Priboi and every one of the
/// twelve <c>GUARD_*</c> types. It does not move: it turns to the target, waits for its stance, sets up
/// a burst, spends it, reloads when the clip runs dry and gives up after the settings row's targeting
/// timeout without sight.
/// </para>
/// <para>
/// <b>It never shoots.</b> It raises the trigger slot of the input frame and lets the body's own weapon
/// machine turn that into a round, which is what the original does — see the original
/// §11.7.
/// </para>
/// </remarks>
public sealed class AiGuardCombat
{
    /// <summary>How close the facing has to be before a shot counts as aimed — two degrees.</summary>
    public const double AimToleranceRadians = 0.034906585;

    private readonly AiCombatParameters _parameters;
    private readonly Func<double> _random;

    private int _missTicks;
    private int _roundsLeft;
    private int _ammoLastTick;
    private int _shotDelayTicks;
    private bool _reloadThroughInput;
    private bool _firstFire;
    private Vector3d _trackedAim;
    private Vector3d _targetThisTick;

    /// <summary>Initializes a new instance of the <see cref="AiGuardCombat"/> class.</summary>
    /// <param name="parameters">The AI's combat row.</param>
    /// <param name="random">A source of values in <c>[0, 1)</c>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public AiGuardCombat(AiCombatParameters parameters, Func<double> random)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);
        _parameters = parameters;
        _random = random;
    }

    /// <summary>Gets which sub-state owns the tick.</summary>
    public AiCombatPhase Phase { get; private set; } = AiCombatPhase.Stand;

    /// <summary>Gets how many ticks in a row the target has been out of sight.</summary>
    /// <remarks>
    /// Cleared at the top of every tick and written back only when the target is not visible, so one
    /// glimpse resets the timeout. The action ends when it passes
    /// <see cref="AiCombatParameters.TargetingTimeoutTicks"/>.
    /// </remarks>
    public int MissTicks => _missTicks;

    /// <summary>Gets how many rounds are left in the current burst — <c>HumanAI+15956</c>.</summary>
    public int RoundsLeftInBurst => _roundsLeft;

    /// <summary>Gets how many bursts have been completed — <c>HumanAI+15952</c>.</summary>
    public int BurstsFired { get; private set; }

    /// <summary>Gets the aim point the firing tick chases the target with — <c>HumanAI+15984</c>.</summary>
    public Vector3d TrackedAim => _trackedAim;

    /// <summary>Gets the delay left before the next single shot — <c>HumanAI+15968</c>.</summary>
    public int ShotDelayTicks => _shotDelayTicks;

    /// <summary>Arms the behaviour — <c>0x48D000</c>.</summary>
    /// <remarks>
    /// The original also fills <c>HumanAI+15828</c> with the initial movement delay here, but nothing
    /// reads that counter (§10.5) so there is nothing to carry.
    /// </remarks>
    public void Begin()
    {
        Phase = AiCombatPhase.Stand;
        _missTicks = 0;
        _roundsLeft = 0;
        _shotDelayTicks = 0;
        BurstsFired = 0;
        _firstFire = true;
    }

    /// <summary>Cancels the currently installed fire/reload child without restarting the engagement.</summary>
    /// <remarks>
    /// A standing roll replaces action slot <c>+36</c> in <c>0x48BC00</c>. That discards the child but
    /// does not rerun <c>0x48D000</c>, so completed-burst count and the first-fire flag must survive.
    /// </remarks>
    public void CancelChild()
    {
        Phase = AiCombatPhase.Stand;
        _roundsLeft = 0;
        _shotDelayTicks = 0;
    }

    /// <summary>Cancels the whole engagement because a higher-priority AI event took control.</summary>
    /// <remarks>
    /// Flashbang, grenade and explosion handlers replace the combat action in the retail pending-action
    /// list. Marking only the child as ended leaves the soldier's outer <c>Engaging</c> activity alive, so
    /// <see cref="AiSoldier.Tick"/> reaches <c>Fight</c> before the scripted animation or panic runner and
    /// the guard keeps firing through the reaction. Re-entry is deliberate: <see cref="Begin"/> arms a
    /// fresh engagement after the interrupt action finishes.
    /// </remarks>
    public void Cancel()
    {
        Phase = AiCombatPhase.Ended;
        _roundsLeft = 0;
        _shotDelayTicks = 0;
        Aimed = false;
    }

    /// <summary>Gets whether the next burst is the first of this engagement, and so half as accurate.</summary>
    /// <remarks>
    /// The engagement's argument block carries a byte that <c>sub_48B9C0</c> takes as its second
    /// argument and the caller then <b>clears</b>, so it fires exactly once per engagement. The two arms
    /// keep it in different places — the guard arm at <c>argBlock+40</c> (<c>0x48D080</c>) and the patrol
    /// arm at <c>argBlock+32</c> (<c>0x48BC00</c>), which is I.G.I. 2's <c>isFirstFire</c>. That is why
    /// <c>ai.md</c> §11.5 and §12.1 named different offsets and <b>both were right</b>; see
    /// <c>igi2-crosscheck.md</c> §8 item 1.
    /// </remarks>
    public bool FirstFirePending => _firstFire;

    /// <summary>Runs one tick.</summary>
    /// <param name="sense">What the body looks like this tick.</param>
    /// <param name="standing">Whether the body is in its standing stance.</param>
    /// <param name="primaryViewLengthInUnits">The AI's primary view length, for the accuracy scaling.</param>
    /// <returns>What the body should do.</returns>
    public AiCombatCommand Update(in AiCombatSense sense, bool standing, double primaryViewLengthInUnits)
    {
        if (Phase == AiCombatPhase.Ended)
        {
            return new AiCombatCommand { Ended = true };
        }

        // The target's position is re-read at the top of every tick, both by the stand tick and by the
        // firing sub-state (0x48D8B0 does it before calling 0x48D8E0).
        _targetThisTick = sense.TargetPosition;

        // Cleared here and written back only on the not-visible path, so it counts CONSECUTIVE ticks.
        int misses = _missTicks;
        _missTicks = 0;

        if (!sense.HasDetectionSlot)
        {
            return End();
        }

        if (Phase == AiCombatPhase.Fire)
        {
            return Firing(sense);
        }

        if (Phase == AiCombatPhase.Reload)
        {
            return Reloading(sense, standing);
        }

        if (!standing)
        {
            return default;
        }

        if (!sense.Visible)
        {
            _missTicks = misses + 1;
            if (_missTicks > _parameters.TargetingTimeoutTicks)
            {
                return End();
            }

            if (!sense.InCone)
            {
                // "Turning to target" -- the only turn in the behaviour that is not an aim.
                return new AiCombatCommand
                {
                    Aim = AiAimMode.None,
                    TurnTo = _targetThisTick,
                };
            }
        }

        if (sense.AmmoInClip <= 0)
        {
            Phase = AiCombatPhase.Reload;
            return new AiCombatCommand { Aim = AiAimMode.Reloading };
        }

        if (!sense.Visible)
        {
            return new AiCombatCommand { Aim = AiAimMode.None };
        }

        if (sense.AimersOnTarget >= AiCombatTarget.MaximumAimers)
        {
            return new AiCombatCommand { Aim = AiAimMode.Crowded };
        }

        // The first burst of an engagement is deliberately made half as accurate, and the flag is
        // consumed here exactly as the original clears its argument-block byte after the call.
        AiCombatBurst burst = AiCombatBurst.Begin(
            _parameters,
            sense.Category,
            sense.ClipSize,
            sense.BurstSize,
            sense.DistanceToTargetInUnits,
            primaryViewLengthInUnits,
            _random(),
            halveAccuracy: _firstFire);

        _firstFire = false;

        _roundsLeft = burst.Rounds;
        _ammoLastTick = sense.AmmoInClip;
        _shotDelayTicks = 0;
        _trackedAim = _targetThisTick;
        Phase = AiCombatPhase.Fire;

        return new AiCombatCommand { Aim = AiAimMode.Aiming, StartedBurst = burst };
    }

    /// <summary>Reports whether the turn the last command asked for reached its tolerance.</summary>
    /// <param name="aimed">What the turn solver returned.</param>
    /// <remarks>
    /// The firing tick holds fire until the facing is inside <see cref="AimToleranceRadians"/>, and the
    /// turn is what knows. Feeding the answer back keeps the solver out of this class.
    /// </remarks>
    public void ReportAimed(bool aimed) => Aimed = aimed;

    /// <summary>Gets whether the last turn reached its tolerance.</summary>
    public bool Aimed { get; private set; }

    private AiCombatCommand Firing(in AiCombatSense sense)
    {
        // The tracked aim point chases the real one at the rate of Combat firing Aim precision.
        Vector3d delta = new(
            _targetThisTick.X - _trackedAim.X,
            _targetThisTick.Y - _trackedAim.Y,
            _targetThisTick.Z - _trackedAim.Z);

        if (delta.X != 0.0 || delta.Y != 0.0 || delta.Z != 0.0)
        {
            _trackedAim = new Vector3d(
                _trackedAim.X + (delta.X * _parameters.AimPrecision),
                _trackedAim.Y + (delta.Y * _parameters.AimPrecision),
                _trackedAim.Z + (delta.Z * _parameters.AimPrecision));
        }

        if (!AiCombatBurst.FiresInBursts(sense.Category))
        {
            return default;
        }

        if (_roundsLeft <= 0 || sense.AmmoInClip <= 0)
        {
            BurstsFired++;
            Phase = AiCombatPhase.Stand;
            return default;
        }

        bool singleShot = sense.BurstSize == 1;

        if (sense.AmmoInClip != _ammoLastTick)
        {
            _ammoLastTick = sense.AmmoInClip;
            _roundsLeft--;

            _shotDelayTicks = singleShot
                ? (int)(((_random() * _parameters.RandomShotDelaySeconds)
                    + _parameters.MinimumShotDelaySeconds) * AiCombatParameters.TicksPerSecond)
                : 0;

            if (singleShot)
            {
                // A semi-automatic weapon has to see the trigger RELEASED between rounds, and not
                // writing the slot this tick is exactly that release.
                return new AiCombatCommand { FirePose = true, TurnTo = _trackedAim };
            }
        }

        if (!Aimed)
        {
            return new AiCombatCommand { FirePose = true, TurnTo = _trackedAim };
        }

        if (_shotDelayTicks > 0)
        {
            _shotDelayTicks--;
            return new AiCombatCommand { FirePose = true, TurnTo = _trackedAim };
        }

        return new AiCombatCommand { FirePose = true, TurnTo = _trackedAim, PullTrigger = true };
    }

    private AiCombatCommand Reloading(in AiCombatSense sense, bool standing)
    {
        if (sense.AmmoInClip > 0 && standing)
        {
            _reloadThroughInput = !_reloadThroughInput;
            Phase = AiCombatPhase.Stand;
            return default;
        }

        return new AiCombatCommand
        {
            ReleaseTrigger = true,
            PressReload = _reloadThroughInput,
            ReloadWeaponDirectly = !_reloadThroughInput,
        };
    }

    private AiCombatCommand End()
    {
        Phase = AiCombatPhase.Ended;
        return new AiCombatCommand { Ended = true, Aim = AiAimMode.None };
    }
}

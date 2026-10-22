using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>A soldier's tick, its death and its respawn.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>Marks the soldier dead; it stops thinking, but the body keeps being stepped.</summary>
    /// <param name="fallVelocity">
    /// The death-fall solver's corrective velocity (<c>0x45D5B0</c>), in engine units per tick. The death
    /// state's per-tick adds it to the position on every tick, which is what stops a body sliding into
    /// whatever its death clip's root motion was about to travel through.
    /// </param>
    /// <param name="facing">The heading the death turn (<c>0x45DAA0</c>) left the body at.</param>
    public void Kill(Vector3d fallVelocity = default, float? facing = null)
    {
        Activity = AiSoldierActivity.Dead;
        DeathFallVelocity = fallVelocity;
        _deathClipEnded = false;
        Route = null;
        _route.Clear();
        DestinationNode = -1;
        _scriptedFire = null;
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _goToNode = -1;
        _combatInstalled = false;
        _runningAction = null;
        Combat = new AiCombatCommand();
        _channelWrites.Clear();
        _rawInputWrites.Clear();
        _patrolOneShotChild = null;
        _patrolRollChild = null;
        _patrolProne = false;
        _patrolKneeling = false;
        _deadClaimsReleased = false;
        _combatFreePosition = null;
        _gunnerCombat?.End();

        // Death leaves the shared target record. The original's combat action ends when its owner dies
        // and the end function (0x48D7D0) runs sub_4512A0, which swap-removes this attacker and frees
        // the record when it was the last. Without the release a dead guard kept holding its AIMING
        // slot, and once four had died mid-fight Count(Aiming) stayed >= 4 for ever -- every guard that
        // engaged afterwards was pushed to Crowded and stood there, seeing the player and doing
        // nothing, until the level was restarted.
        SetTarget(null);

        // Death ends every action on the stack. The claims -- the alarm panel's handles at ai+15024/+15028 and
        // the node cell at +16032 -- are kept until the death clip has run (see TickDeadAi).
        _pendingActions.Clear();
        if (facing is { } heading)
        {
            Facing = Igi1AiGeometry.Wrap(heading);
        }
    }

    /// <summary>Creates the fresh HumanAI instance installed by a GuardGenerator respawn.</summary>
    /// <param name="position">The authored soldier spawn position.</param>
    /// <param name="facing">The authored soldier heading.</param>
    /// <param name="weapon">
    /// Reads the respawned brain's weapon state. The owner is supplied so renderer callbacks cannot retain
    /// the dead brain while querying mounted-gun state.
    /// </param>
    /// <returns>A new brain with the same level wiring and reset AI execution state.</returns>
    /// <remarks>
    /// IGI1's <c>0x488000</c>/<c>0x488040</c> path constructs the whole stored child subtree again. A new
    /// <see cref="Igi1HumanAi"/> is the managed equivalent: CREATE will run again on its first tick, while
    /// immutable level services (graph, scripts, collision queries and combat row) are reattached here.
    /// The caller must assign a unique <see cref="RuntimeId"/> before registering the new brain alongside
    /// retained corpses, and rebind view-tracking callbacks to its new rendered body.
    /// </remarks>
    public Igi1HumanAi Respawned(
        Vector3d position,
        float facing,
        Func<Igi1HumanAi, AiCombatWeaponState>? weapon = null)
    {
        int seed = Math.Max(0, RuntimeId - 1);
        Igi1HumanAi fresh = new(_graph, position, facing, seed & 3, seed, _random, SoldierClass)
        {
            PlayerIsEnemy = PlayerIsEnemy,
            RuntimeId = RuntimeId,
        };

        fresh.SetLineOfSight(_sight);
        fresh.SetEyeTransform(_eyePosition, _eyeOrientation);
        fresh.SetGroundQuery(_ground);
        fresh.SetPatrolPaths(_paths);
        fresh.SetResponseRouter(_responseRouter);
        fresh.SetScriptSound(_scriptSound);
        fresh.SetDoorQuery(_doors);
        fresh.SetAlarmStatusResolver(_alarmStatus);
        fresh.SetGunnerStatusResolver(_gunnerStatus);
        fresh.SetStationaryGunResolver(_stationaryGun);
        fresh.SetDefaultTarget(_defaultTarget);
        fresh.SetNodeOccupancy(_occupancy);
        fresh.SetGrenadeAvailable(HasGrenade);

        if (_claims is not null && _locateTask is not null && _activateTask is not null)
        {
            fresh.SetActivateSupport(_claims, _locateTask, _activateTask);
        }

        if (_host is not null && _script is not null)
        {
            fresh.SetScript(_host, _script, _archetypeScript);
        }

        if (CombatParameters is not null && weapon is not null)
        {
            fresh.SetCombat(_type, CombatParameters, () => weapon(fresh));
        }

        return fresh;
    }

    /// <summary>Gets the corrective velocity the death-fall solver returned, in engine units per tick.</summary>
    public Vector3d DeathFallVelocity { get; private set; }

    // Whether the death clip has ended, after which the corpse no longer slides.
    private bool _deathClipEnded;

    /// <summary>
    /// A dead soldier's HumanAI tick, before its body's: once the death clip has run (Human+828, set by
    /// <c>0x45C740</c> the tick after the clip ends), <c>0x44F410</c> lets go of the alarm panel's claim handles and
    /// its node cell, so a guard shot on its way to the alarm leaves it for the next one only once the body lies still.
    /// </summary>
    public void TickDeadAi()
    {
        if (Activity != AiSoldierActivity.Dead || !_deathClipEnded || _deadClaimsReleased)
        {
            return;
        }

        _deadClaimsReleased = true;
        ReleaseActivateClaims();
        _occupancy?.ReleaseDead(this);
    }

    // Whether TickDeadAi has let go of the dead soldier's claims.
    private bool _deadClaimsReleased;

    /// <summary>
    /// Steps a dead body for one tick — the death state's per-tick (<c>0x45C700</c>) joined to the soldier
    /// update's ordinary root-motion move and ground query (<c>0x45D080</c>).
    /// </summary>
    /// <param name="rootMotionDelta">
    /// What the death clip's own root-motion track moved this tick, in the body's model space. The soldier
    /// update rotates exactly this into world space and hands it to the movement integrator.
    /// </param>
    /// <param name="deathClipEnded">Whether the death clip ended this tick, after which the corpse stops sliding.</param>
    /// <remarks>
    /// <para>
    /// Three things move a corpse, and all three are here: the clip's root motion, the solver's corrective
    /// velocity, and the ground query snapping the body onto whatever is under it. Nothing else does — the
    /// solver's own output is purely horizontal (its vertical component is forced to zero before the
    /// arithmetic starts), so a body <em>falls</em> because its death clip's root track goes down and the
    /// ground catches it.
    /// </para>
    /// <para>
    /// A soldier with no ground query keeps the Z its clip gives it, which is the same fallback the living
    /// path takes. When a ground service exists but has no support for a placed upper floor, the last feet
    /// height is retained and the renderer's root sample supplies the visible fall pose.
    /// </para>
    /// </remarks>
    public void TickDeadBody(Vector3f rootMotionDelta, bool deathClipEnded)
    {
        if (Activity != AiSoldierActivity.Dead)
        {
            return;
        }

        Vector3d local = new(rootMotionDelta.X, rootMotionDelta.Y, rootMotionDelta.Z);
        Vector3d world = SoldierDeathFall.RotateAboutZ(local, Facing);

        // 0x45C700 adds the fall solver's velocity until the death clip ends, then hands the corpse to
        // 0x45C740, which adds nothing more -- from the tick after: the tick the clip ends still slides.
        Vector3d slide = _deathClipEnded ? default : DeathFallVelocity;
        _deathClipEnded |= deathClipEnded;
        double x = Position.X + world.X + slide.X;
        double y = Position.Y + world.Y + slide.Y;
        double z = Position.Z + world.Z + slide.Z;

        if (_ground is not null && _ground(x, y, z + GroundProbeAboveUnits, out double groundZ))
        {
            z = groundZ;
        }
        else if (_ground is not null)
        {
            // Some placed upper floors are visual-only in the remaster's collision index. A live guard
            // on one of those floors stays at the authored/nav height, but the death clip's descending
            // root delta used to push its Human position below that plane. The renderer then also applies
            // the clip's current root height, putting the entire corpse under the floor (Mission 4 guards
            // 417-419). When a world ground service exists but cannot find a replacement support, retain
            // the last valid feet height; the death pose still lowers the pelvis through RootSample.
            z = Position.Z;
        }

        Position = new Vector3d(x, y, z);
    }

    /// <summary>Runs one 30 Hz tick.</summary>
    /// <param name="tick">The engine tick counter, which the detection phase gate keys off.</param>
    /// <param name="enemy">The enemy's world position — the player's eye.</param>
    /// <param name="rootMotionThisTick">
    /// How far the soldier's current animation moves it this tick along its own forward axis, in engine
    /// units — signed, so a clip that steps back moves the soldier back. The engine moves a human by its
    /// clip's root motion and nothing else, so this is the only speed source.
    /// </param>
    /// <param name="alarmOn">Whether an alarm the soldier watches is sounding.</param>
    /// <param name="alarmWasOn">Whether that alarm sounded on the preceding tick, for the falling-edge event.</param>
    /// <param name="enemyAlive">
    /// Whether the enemy is still a valid target. A dead enemy is not: the detection scan (<c>0x489750</c>)
    /// unregisters a human that reports itself invalid, so the soldier loses it and leaves combat instead
    /// of firing at the corpse. <see langword="false"/> reproduces that.
    /// </param>
    /// <param name="enemyFeet">
    /// The enemy task's position — its feet, which is what combat aims at (<c>sub_451380</c> raises it
    /// 0.5 m, 0.25 m crouched). Defaults to <paramref name="enemy"/> when the caller has no body.
    /// </param>
    /// <param name="enemyCrouching">Whether the enemy is crouching, which halves the aim raise.</param>
    /// <param name="rootMotionLateral">The clip's root motion along the body's own right axis, in engine units.</param>
    /// <param name="enemyIsSoldier">
    /// Whether the fed enemy is another soldier rather than the player. The player is only a target to a
    /// hostile team, so a friendly never registers it; another soldier is a target to any soldier whose
    /// team differs from the enemy's own, regardless of the player relationship.
    /// </param>
    public void Tick(
        int tick,
        Vector3d enemy,
        double rootMotionThisTick,
        bool alarmOn = false,
        bool alarmWasOn = false,
        bool enemyAlive = true,
        Vector3d? enemyFeet = null,
        bool enemyCrouching = false,
        bool enemyIsSoldier = false,
        double rootMotionLateral = 0.0)
    {
        // One human on offer. Without a body position the point given is tested as it is; with one, the
        // mark is the body's own mid-point.
        Vector3d feet = enemyFeet ?? enemy;
        Vector3d mark = enemyFeet is null
            ? enemy
            : new Vector3d(
                feet.X,
                feet.Y,
                feet.Z + DetectionMarkAboveFeet(enemyCrouching));
        AiDetectionCandidate candidate = new(
            enemyIsSoldier ? OfferedSoldierId : AiDetectionCandidate.PlayerId,
            OtherSide: enemyIsSoldier || PlayerIsEnemy,
            Alive: enemyAlive,
            mark,
            feet,
            enemyCrouching,
            _defaultTarget);
        TickAmong(tick, [candidate], rootMotionThisTick, alarmOn, alarmWasOn, rootMotionLateral);
    }

    // The id the one-candidate form gives an offered soldier.
    private const int OfferedSoldierId = int.MaxValue;

    /// <summary>Runs one 30 Hz tick against every human the scan offers.</summary>
    /// <param name="tick">The engine tick counter, which the detection phase gate keys off.</param>
    /// <param name="candidates">
    /// Every human the scan offers this tick, in the global human list's order — the player and every
    /// soldier, each described from this soldier's side.
    /// </param>
    /// <param name="rootMotionThisTick">The clip's root motion along the body's forward axis, signed, in units.</param>
    /// <param name="alarmOn">Whether an alarm the soldier watches is sounding.</param>
    /// <param name="alarmWasOn">Whether that alarm sounded on the preceding tick, for the falling-edge event.</param>
    /// <param name="rootMotionLateral">The clip's root motion along the body's own right axis, in engine units.</param>
    public void TickAmong(
        int tick,
        IReadOnlyList<AiDetectionCandidate> candidates,
        double rootMotionThisTick,
        bool alarmOn = false,
        bool alarmWasOn = false,
        double rootMotionLateral = 0.0)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (Activity == AiSoldierActivity.Dead)
        {
            return;
        }

        _lastTick = tick;
        _rootMotionThisTick = rootMotionThisTick;
        _rootLateralThisTick = rootMotionLateral;
        OfferCandidates(candidates);
        _enemyAlive = TargetHuman?.Candidate.Alive ?? true;

        // 0x44F410 zeroes the whole input-channel block (+15476, 0xA0 bytes) before anything runs: only the
        // action that runs this tick writes to it, so a suspended one leaves nothing behind.
        _rawInputWrites.Clear();
        _channelWrites.Clear();
        _gaitPress = false;
        WalksForward = false;
        _tenseRunThisTick = false;
        Combat = new AiCombatCommand();
        DetectionShoutRequestedThisTick = false;
        AlertPingDueThisTick = false;

        // A scripted one-shot clip releases the animation slot the moment the renderer says it ended, so the
        // soldier goes idle and its script's IDLE branch may request the clip again. That holds for staged
        // cutscene actors too: 0x48DEC0 completes PlayAnimation once channel 3 leaves the clip, and the
        // original replays mission 8's guard 3336 (clip 49) and mission 1's 1666 (clip 242) every cycle,
        // each replay adding the clip's root motion again.
        if (_scriptedAnimation && _animationFinished)
        {
            _animationFinished = false;
            RequestedAnimation = -1;
            _scriptedAnimation = false;
        }

        // An AI's FIRST event is always its creation: before initialisation only type 0 passes admission, and
        // the mask is not consulted at all. Never posting it left every archetype script's
        // CREATE branch unrun -- which is where guard.qsc sets its view length, alarm length, alpha and
        // gamma. Guards were therefore seeing through the ENGINE DEFAULTS from 0x44F060 (+/-57.3 degrees
        // vertical, +/-59.5 horizontal, 26 m) instead of the archetype's own 40 m / 50 m / 50 / 45.
        //
        // And that is ALL the first tick does. 0x44F410's first pass (+15636 still clear) dispatches CREATE and
        // jumps straight to the pump and the action runner (0x44FD50, 0x44FEC0): no sweep, no detection, no
        // alarm or idle-animation check and no IDLE, which the next tick is the first to post. Probed on
        // mission 4: CREATE on an AI's first tick, IDLE -- and with it the patrol -- one tick later, at level
        // start and when a container opens alike. The port posted IDLE alongside CREATE, and every soldier ran
        // a tick ahead of retail's, its sweeps a tick out of phase.
        if (!_created)
        {
            _created = true;
            if (HasScript)
            {
                Events.DispatchCreate(Dispatch);
            }
            else
            {
                Events.Post(AiEventRecord.Simple(AiEvent.Create), owner: 0, worldTests: null);
            }

            RunScript();
            if (_pendingActions.Tick() is null)
            {
                RunWithoutAction();
            }

            StepBodyOrWait();
            return;
        }

        // 0x44F410: the first tick the AI finds its Human dead (0x451330) ends every action on the stack (0x450FF0 for
        // each), hands AIEVENT_DEAD to the script there and then (0x44FC90), and latches +15034. A dead Human is never
        // scanned again.
        if (HumanDead)
        {
            if (!_deathDispatched)
            {
                _deathDispatched = true;
                foreach (AiPendingAction action in _pendingActions.Actions.ToArray())
                {
                    FinishAction(action);
                }

                if (HasScript)
                {
                    Dispatch(AiEvent.Dead, PriorityPositionOf(AiEvent.Dead));
                }
            }

            return;
        }

        // The idle-animation deadline (ai+15200): passing it posts AIEVENT_ANIMATION and re-arms.
        if (_nextAnimationTick >= 0 && tick >= _nextAnimationTick)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Animation), owner: 0, worldTests: null);
            _nextAnimationTick = tick + _animationMinTicks
                + NextBounded(_animationRandomTicks);
        }

        // The detection scan (0x489750) runs on the soldier's phase tick: it registers each human by the
        // mode-6 rule -- other side and alive into the enemy list, own side and dead into the fallen-friend
        // list -- unregisters the rest, and sweeps every registered slot through the cone and then the
        // sight ray behind it (0x4898D0), the expensive test paid only for someone inside a cone.
        if (_schedule.SweepsOn(tick))
        {
            Scan(tick);
        }
        else if (!_registeredOnce)
        {
            // A soldier the level has just handed its humans has, in the original, been scanning since the
            // level began: the humans it would have registered are registered already.
            Register(tick);
        }

        _registeredOnce = true;

        // Before a fight the soldier's attention goes to the first enemy in sight, in registration order --
        // the one whose detection event the pump will hand the script first.
        if (!_combatInstalled && _enemies.Find(human => human.Slot.IsVisible) is { } seen)
        {
            _targetId = seen.Candidate.Id;
        }

        if (TargetSlot.IsVisible)
        {
            LastEventDistance = TargetSlot.Distance;
            _lastSeenFeet = TargetFeet;
            SetTarget(TargetRecord);
            Target?.Refresh(_lastSeenFeet);
        }

        // 0x44F410 posts ENEMYDETECTION for every enemy in sight and FRIENDLYDETECTION for every fallen
        // friend in sight, every tick, each naming its human -- which is who a SetCombat it triggers fights.
        // No shipped script declares FRIENDLYDETECTION, so admission drops those, as in the original. Each post
        // reads the human's life as it is now (0x463170), not as the last sweep registered it: an enemy killed since
        // stays in the list, and in sight, until the next sweep, but is no longer posted.
        foreach (Igi1AiTrackedHuman enemy in _enemies)
        {
            if (enemy.Slot.IsVisible && enemy.Candidate.Alive)
            {
                PostDetection(AiEvent.EnemyDetection, enemy);
            }
        }

        foreach (Igi1AiTrackedHuman friend in _fallenFriends)
        {
            if (friend.Slot.IsVisible && !friend.Candidate.Alive)
            {
                PostDetection(AiEvent.FriendlyDetection, friend);
            }
        }

        if (alarmOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOn), owner: 0, worldTests: null);
        }
        else if (alarmWasOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOff), owner: 0, worldTests: null);
        }

        // 0x44F410 recomputes the alert latch from scratch every tick from two causes only: the alarm, or a
        // combat record (+15016) whose target the detection scan still registers (0x489BE0 tests the slot's
        // registered byte, not its visibility). Hearing, damage and alerts do not set it, and a fighting
        // guard that loses sight keeps the alarm view length for as long as its target lives.
        Alerted = alarmOn
            || (_combatInstalled && Target is not null && TargetSlot.IsRegistered);

        // 0x44F410 posts no COMBAT of its own -- that is the SetCombat runner's (0x48E560), which the script
        // reaches from ENEMYDETECTION -- and posts IDLE whenever the action stack is empty (0x450DC0).
        // Declared priority, not a pending-event test, keeps IDLE behind the events that matter: a "nothing
        // pending" condition never let IDLE through while the alarm posted ALARMON every tick, which froze
        // a guard in place for the rest of the alarm.
        if (!_pendingActions.HasPending)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Idle), owner: 0, worldTests: null);
        }

        // ONE event type dispatches per tick, in priority order, and the script decides what to do about it.
        RunScript();

        // 0x44FEC0, every tick, after the pump: the head of the action stack runs, and only the head.
        // Whatever lies beneath it is suspended rather than cancelled, and carries on once everything
        // above it has finished -- a pushable fight outlasts a flashbang's flinch.
        if (_pendingActions.Tick() is null)
        {
            RunWithoutAction();
        }

        StepBodyOrWait();
    }

    /// <summary>Gets or sets whether the soldier's Human is dead (<c>0x451330</c>), which the AI reads at the top of its tick.</summary>
    public bool HumanDead { get; set; }

    // +15034: AIEVENT_DEAD has been dispatched.
    private bool _deathDispatched;
}

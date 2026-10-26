using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>The move-and-fight arm the <c>PATROL_*</c> types and the named characters fight with — <c>0x48ABA0</c> and the behaviours it installs.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>Runs the move-and-fight arm for one tick — <c>0x48ABA0</c>.</summary>
    /// <param name="tick">The engine tick used by the ten-tick evasion cooldown.</param>
    /// <param name="enemy">Where the target is.</param>
    /// <remarks>
    /// <para>
    /// The chooser decides (<see cref="AiPatrolCombat.Choose"/>) and this turns the decision into
    /// movement, turns and channel writes. Fire and reload are handed to the shared sub-actions, which
    /// is what the original does — it installs the same <c>0x48D8B0</c> and <c>0x48DB10</c> from both
    /// arms.
    /// </para>
    /// <para>
    /// Grenade and close-combat are standing one-shot children. They raise channels 8 and 9 until the
    /// body leaves state 0, then complete while states 8/9 own animations 58/70 to completion.
    /// </para>
    /// </remarks>
    private void FightAsPatrol(int tick, Vector3d enemy)
    {
        AiCombatWeaponState weapon = _weapon!();
        TrackTargetNode(LiveTargetNodePoint() ?? LiveTargetFeet(enemy));
        ChoosePatrolEvasion(tick, enemy);
        if (RunPatrolChild(enemy, weapon) || RunPatrolMovement(enemy) || WaitForPatrolPosture())
        {
            return;
        }

        AiPatrolAction action = ChoosePatrolAction(enemy, weapon, out bool visible);
        ApplyPatrolAction(action, enemy, weapon, visible);
    }

    /// <summary>The evasion the stand per-tick asks for before anything else — <c>0x48BE30</c>, out of <c>0x48BC00</c>.</summary>
    /// <param name="tick">The engine tick used by the ten-tick evasion cooldown.</param>
    /// <param name="enemy">Where the target is.</param>
    private void ChoosePatrolEvasion(int tick, Vector3d enemy)
    {
        // 0x48BC00 asks 0x48BE30 before it runs an already-installed child and before its ordinary
        // chooser. The drained event counters are the exact three words the original tests. Follow,
        // RePosition and LookForTarget replace that per-tick while they run, so a moving soldier never
        // asks, and the named characters' per-tick (0x48ACC0) never calls it at all.
        if (!_patrolProne && Arm == AiCombatArm.Patrol && !IsPatrolMovementInstalled)
        {
            bool underFire = Events.DrainedCount(AiEvent.TakingDamage) > 0
                || Events.DrainedCount(AiEvent.Gunshot) > 0
                || Events.DrainedCount(AiEvent.GunshotMiss) > 0;
            bool onStair = _graph?.NodeAt(CurrentNode) is { } current
                && (current.Criteria & GraphNodeCriteria.Stair) != 0;
            AiEvasiveAction evasion = _patrol!.ChooseEvasion(
                tick,
                CombatDistance(enemy),
                AvailableRollSpace(),
                underFire,
                onStair);

            if (evasion == AiEvasiveAction.LieDown)
            {
                // sub_48AE70 installs the prone arm with no child, which cancels whatever was running.
                _patrolProne = true;
                _patrolRollChild = null;
                _patrolOneShotChild = null;
                _combat!.CancelChild();
            }
            else if (evasion is AiEvasiveAction.RollLeft or AiEvasiveAction.RollRight)
            {
                _patrolRollChild = evasion;
                _patrolOneShotChild = null;
                _combat!.CancelChild();
            }
        }
    }

    /// <summary>Runs whatever child the arm has installed — a roll, the prone hold, a one-shot, or a fire or reload.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="weapon">The held weapon's state this tick.</param>
    /// <returns><see langword="true"/> when the child owns the tick and the chooser is not asked.</returns>
    private bool RunPatrolChild(Vector3d enemy, AiCombatWeaponState weapon)
    {
        // 0x48D830/0x48D870 keep writing the same raw slot until the body reports the kneeling stance.
        // The body then falls through state 19 to standing after the child stops writing.
        if (_patrolRollChild is { } roll)
        {
            _channelWrites.Clear();
            Combat = default;
            if (CombatState == HumanSoldierStates.KneelingState)
            {
                _patrolRollChild = null;
            }
            else
            {
                WriteRawInputSlot(roll == AiEvasiveAction.RollLeft ? 2 : 3, 1f);
            }

            return true;
        }

        // 0x48C540 holds logical channel 2 throughout the prone behavior and chooses only once the body
        // is in the type's prone state (sub_48EFE0(C, 2)); releasing the channel selects the get-up.
        if (_patrolProne)
        {
            _channelWrites.Clear();
            WriteChannel(AiCombatChannel.LieDown, 1f);
            if (!AiCombatChannels.IsInStance(_type, AiStanceChannel.Prone, CombatState))
            {
                Combat = default;
                return true;
            }
        }

        // 0x48DBF0 and 0x48DBB0 share this exact protocol: while standing, write the action channel;
        // once the body changes state, the child is complete. The parent does not choose again until
        // the one-shot body state itself has returned to standing.
        if (_patrolOneShotChild is { } oneShot)
        {
            _channelWrites.Clear();
            Combat = default;
            if (CombatState == HumanSoldierStates.StandState)
            {
                WriteChannel(
                    oneShot == AiPatrolAction.ThrowGrenade
                        ? AiCombatChannel.ThrowGrenade
                        : AiCombatChannel.CloseCombat,
                    1f);
            }
            else
            {
                _patrolOneShotChild = null;
            }

            return true;
        }

        // sub_48BC00 keeps an installed child callback in action+36 and runs it until it completes;
        // sub_48BFD0 is not called again in the middle of a fire/reload. Re-running the chooser every
        // tick let a patrol count its own Aiming slot, switch itself to Crowded and strand an active
        // firing phase without ever reaching the trigger.
        if (_combat!.Phase != AiCombatPhase.Stand)
        {
            _channelWrites.Clear();
            HoldPosture();
            RunSharedSubAction(enemy, weapon);
            return true;
        }

        return false;
    }

    /// <summary>Runs the movement behaviour that replaces the per-tick while it is installed — Follow, LookForTarget or RePosition.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <returns><see langword="true"/> when a movement behaviour owns the tick.</returns>
    private bool RunPatrolMovement(Vector3d enemy)
    {
        // 0x48C860 replaces the arm's per-tick until the soldier arrives: the chooser is not asked, so a
        // chaser runs its whole leg instead of stopping to shoot the moment the target is in range. It
        // re-searches only when the target has moved onto another node.
        if (IsPatrolMovementInstalled && PatrolAction == AiPatrolAction.Follow)
        {
            _channelWrites.Clear();
            Combat = default;
            _tenseRunThisTick = true;

            // 0x48C860 runs: the run setter, every tick of the leg.
            SetGait(walking: false);

            // 0x48C860: the flag 0x48A920 raised buys one search, toward the target only while it is seen
            // (sub_48B050's third argument is the target record's visible byte, +41), and the walk is redirected
            // only to a node other than the one it stands on. Searching only while the target was visible, and on
            // a node change noticed only then, sent mission 4's guard 411 back the way it came the moment it saw
            // the squad again, where retail's ran its leg out.
            if (_followResearch)
            {
                _followResearch = false;
                if (FindFreePosition(LiveTargetFeet(enemy), towardTarget: TargetSlot.IsVisible) is int nearer
                    && nearer != CurrentNode)
                {
                    _patrol!.RedirectTo(nearer);
                }
            }

            FollowTarget(enemy);
            return true;
        }

        // 0x48C760 likewise replaces the per-tick until the walk to the claimed cell arrives (0x4522A0 returns
        // true and 0x48AE70 reinstalls the parent). The node the soldier stands on changes along the way
        // without ending it, so a patrol caught mid-leg runs the whole leg -- re-asking the chooser each tick
        // stopped it at the first node it crossed.
        // 0x48CAB0, the tick after the stand arm chose it: one turn toward where the target is (0x451380's point,
        // to within 2 degrees), and the parent per-tick is reinstalled for the next tick. It walks nowhere.
        if (_lookForTargetLatched)
        {
            _lookForTargetLatched = false;
            _channelWrites.Clear();
            Combat = default;
            _tenseRunThisTick = true;
            TurnTowards(LiveTargetNodePoint() ?? AimPointOf(enemy, TargetCrouching), 0.034906585f);
            return true;
        }

        if (_repositionLatched && PatrolAction == AiPatrolAction.RePosition)
        {
            _channelWrites.Clear();
            Combat = default;
            _tenseRunThisTick = true;
            // The walk was planned when RePosition was installed; the latch only follows it. Re-planning
            // here would hand an arrived soldier a fresh one-point route every tick, and it would never end.
            if (_route.Count > 0)
            {
                AdvanceInCombat();
            }

            // Arrived (or nowhere left to walk): 0x4522A0 reports it on the tick the queue empties, and 0x48AE70
            // reinstalls the parent per-tick for the next one.
            if (_route.Count == 0)
            {
                _repositionLatched = false;
            }

            return true;
        }

        return false;
    }

    /// <summary>Holds the arm until the body is in the posture its chooser runs in, as <c>0x48BC00</c> and <c>0x48C280</c> do.</summary>
    /// <returns><see langword="true"/> while the arm waits on the body and the chooser is not asked.</returns>
    private bool WaitForPatrolPosture()
    {
        // A one-shot child (grenade throw, melee, roll) owns the body until its clip completes — the
        // retail child at action+36, which the stand tick runs INSTEAD of the chooser and clears when
        // it reports done (0x48BC00). The machine leaves StandState for the throw/melee/roll and
        // returns to it when finished, which is the completion signal.
        if (!_patrolProne && (_patrolOneShotChild is not null || _patrolRollChild is not null))
        {
            // A roll's own done test is reaching the KNEEL state (0x48D870 returns sub_48EFE0(C, 1) --
            // rolls end kneeling); the throw/melee children complete back at stand.
            bool rollDone = _patrolRollChild is not null
                && CombatState == HumanSoldierStates.KneelingState;
            if (CombatState == HumanSoldierStates.StandState || rollDone)
            {
                _patrolOneShotChild = null;
                _patrolRollChild = null;
            }
            else
            {
                _channelWrites.Clear();
                Combat = default;
                return true;
            }
        }

        // "Waiting for stand state" — retail's stand tick runs the chooser only when the body's live
        // locomotion state IS the type's stand state (sub_48EFE0(C, 0), the same off_541658 channel
        // table AiCombatChannels models; 0x48BC00 logs exactly that string otherwise). The run states
        // are exempt because in retail the movement behaviours (Follow/RePosition/LookForTarget)
        // REPLACE the whole per-tick and are never stand-gated — the port folds them into this one
        // function, so 17/18 must pass or the hunt oscillates move → run state → gate → stand → move.
        if (!_patrolProne && !_patrolKneeling
            && CombatState != HumanSoldierStates.StandState
            && CombatState != HumanSoldierStates.RunState
            && CombatState != HumanSoldierStates.RunFiringState)
        {
            _channelWrites.Clear();
            Combat = default;

            // ...and an animation the patrol was playing is stopped: channel 3 at -1 (0x48BC00).
            if (AiCombatChannels.IsInStance(_type, AiStanceChannel.AnimationPlaying, CombatState))
            {
                WriteChannel(AiCombatChannel.PlayAnimation, -1f);
            }

            return true;
        }

        // "Waiting for kneel state" -- 0x48C280 holds channel 1 and chooses only in the kneel state.
        if (_patrolKneeling && !AiCombatChannels.IsInStance(_type, AiStanceChannel.Kneeling, CombatState))
        {
            _channelWrites.Clear();
            HoldPosture();
            Combat = default;
            return true;
        }

        // A negative "Combat liedown mim distance" stands the kneel and prone arms straight back up:
        // both dispatchers test the row value at +15800 (settings item +620 x 4096) and become the
        // standing arm when it is below zero (0x48C280 / 0x48C540).
        if ((_patrolProne || _patrolKneeling)
            && (CombatParameters?.LieDownMinimumDistanceInUnits ?? 0.0) < 0.0)
        {
            _patrolProne = false;
            _patrolKneeling = false;
            _channelWrites.Clear();
        }

        return false;
    }

    /// <summary>Asks the patrol chooser what to do — <c>sub_48BFD0</c>.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="weapon">The held weapon's state this tick.</param>
    /// <param name="visible">Receives whether the target was visible when the chooser was asked.</param>
    /// <returns>What the chooser chose.</returns>
    private AiPatrolAction ChoosePatrolAction(Vector3d enemy, AiCombatWeaponState weapon, out bool visible)
    {
        double distance = CombatDistance(enemy);
        visible = TargetSlot.IsVisible;

        // Only the branch that needs a search runs one, as the original's call sites do: a search CLAIMS
        // the node it accepts, so running both every tick moved the soldier's claim about on ticks the
        // chooser never used it -- and, rejecting its own claim next time, flipped between two nodes.
        // sub_48B050 places the target with 0x451380 -- the human itself, live -- not the shared record the
        // aim and the distance read, so a search out of sight still knows where the target went.
        Vector3d live = LiveTargetFeet(enemy);
        Func<int?> toward = () => FindFreePosition(live, towardTarget: true);
        Func<int?> away = () => FindFreePosition(live, towardTarget: false);

        // "On its own node" gates the whole chooser: rule (a) walks back before anything else fires.
        // Retail's RePosition (0x48C760) walks the BODY onto the node and completes; the port walks the
        // route table, so two cases produce a walk that can never finish and left the soldier
        // repositioning for ever -- seeing the player, engaging, and never firing a shot:
        //  - no graph (CurrentNode is -1): there is nowhere to walk back to at all;
        //  - the route back to its own node is EMPTY (the table says it is already there) while the
        //    body stands outside the node's radius test.
        // Both mean "no walk exists that gets you more on your node than you are", which is what
        // standing on it is FOR -- so both count as standing on it.
        bool standingOnOwnNode = CurrentNode < 0
            || IsStandingOnNode(CurrentNode)
            || (_route.Count == 0 && DestinationNode == CurrentNode);

        // sub_48BFD0 begins with sub_4511F0(target, self, -1). The patrol is no longer occupying an
        // aimer slot while it decides whether a new child action may be installed.
        Target?.Aim(this, AiAimMode.None);

        AiPatrolSense sense = new(
            HasDetectionSlot: TargetSlot.IsRegistered,
            Visible: visible,
            DistanceToTargetInUnits: distance,
            CurrentNodeId: CurrentNode,
            StandingOnOwnNode: standingOnOwnNode,
            AmmoInClip: weapon.AmmoInClip,
            HasGrenade: HasGrenade,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0,
            FreePositionTowardTarget: toward,
            FreePositionAwayFromTarget: away);

        _chooserCalls++;
        AiPatrolAction action = _patrol!.Choose(sense);
        PatrolAction = action;
        _channelWrites.Clear();
        return action;
    }

    /// <summary>Turns the chooser's decision into movement, turns and channel writes.</summary>
    /// <param name="action">What the chooser chose.</param>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="weapon">The held weapon's state this tick.</param>
    /// <param name="visible">Whether the target was visible when the chooser was asked.</param>
    private void ApplyPatrolAction(AiPatrolAction action, Vector3d enemy, AiCombatWeaponState weapon, bool visible)
    {
        // The kneel and prone arms answer a move with the stand arm (0x48BBD0) and the move's own per-tick
        // on the same tick, writing the posture channel 0: the soldier sets off as it rises, with no
        // wait for the stand state and no second decision.
        //
        // The STAND arm only installs it: 0x48BC00 hands Follow and RePosition to 0x48AE70 with their entries
        // (0x48C850, 0x48C750), which set the run function to the movement's per-tick and return -- so the walk
        // starts on the next tick, from the latched branches above. Probed on mission 4: patrol 411 chose Follow on
        // tick 4385, wrote its first forward on 4386 (the body entering run state 17 then) and moved from 4387.
        bool standArmInstallsMove = !_patrolProne && !_patrolKneeling
            && action is AiPatrolAction.RePosition or AiPatrolAction.Follow or AiPatrolAction.LookForTarget;
        if ((_patrolProne || _patrolKneeling)
            && action is AiPatrolAction.RePosition or AiPatrolAction.Follow or AiPatrolAction.LookForTarget)
        {
            _patrolProne = false;
            _patrolKneeling = false;
        }

        HoldPosture();

        Combat = default;

        switch (action)
        {
            case AiPatrolAction.End:
                // Every arm ends the action outright (case 8 -> sub_450FF0); the body rises by itself
                // once nothing holds its posture channel.
                EndCombat();
                return;

            case AiPatrolAction.CloseCombat:
                if (_patrolProne || _patrolKneeling)
                {
                    // Close enough to strike from the ground: the arm only becomes the stand arm, and the
                    // strike is chosen again once the body is up.
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                // 0x48DBB0 only raises channel 9; it does not turn the body. The blow is the body's, at the clip's
                // hit frame (StrikeCloseCombat).
                _patrolOneShotChild = action;
                WriteChannel(AiCombatChannel.CloseCombat, 1f);
                return;

            case AiPatrolAction.ThrowGrenade:
                if (_patrolProne || _patrolKneeling)
                {
                    // The prone and kneel arms only log "liedown/kneel throw grenade"; neither installs
                    // a child (0x48C540, 0x48C280).
                    return;
                }

                // 0x48DBF0 only raises channel 8; it does not turn the body.
                _patrolOneShotChild = action;
                WriteChannel(AiCombatChannel.ThrowGrenade, 1f);
                return;

            case AiPatrolAction.LookForTarget:
                Target?.Aim(this, AiAimMode.Crowded);

                // 0x48AE70 with the entry 0x48CAA0: the stand arm installs 0x48CAB0, which runs on the next tick;
                // the kneel and prone arms answer with the stand arm and the turn on this one.
                if (standArmInstallsMove)
                {
                    _lookForTargetLatched = true;
                }
                else
                {
                    TurnTowards(LiveTargetNodePoint() ?? AimPointOf(enemy, TargetCrouching), 0.034906585f);
                }

                return;

            case AiPatrolAction.RePosition:
                // 0x48C760 walks to the CLAIMED cell (HumanAI+16048) on the claimed node (+16032), not to the
                // node the soldier last stood on -- for a patrol caught mid-leg that is where its leg was
                // going. Without a claim it only logs "HumanAI #%d has no nodeoccupationitem" and stands.
                if (_occupancy is null)
                {
                    HeadFor(CurrentNode);
                    if (!standArmInstallsMove)
                    {
                        AdvanceInCombat();
                    }

                    _repositionLatched = _route.Count > 0;
                }
                else if (_occupancy.ClaimedNode(this) is int claimed)
                {
                    HeadFor(claimed);
                    if (!standArmInstallsMove)
                    {
                        AdvanceInCombat();
                    }

                    _repositionLatched = _route.Count > 0;
                }

                return;

            case AiPatrolAction.Follow:
                // The out-of-sight search that chose this walk spends the re-search flag (0x48C212).
                if (!visible)
                {
                    _followResearch = false;
                }

                if (!standArmInstallsMove)
                {
                    FollowTarget(enemy);
                }

                return;

            case AiPatrolAction.Fire:
            case AiPatrolAction.Reload:
            default:
                RunSharedSubAction(enemy, weapon);
                return;
        }
    }

    /// <summary>Whether a movement behaviour — Follow, RePosition or a walking LookForTarget — owns the tick.</summary>
    private bool IsPatrolMovementInstalled =>
        (PatrolAction == AiPatrolAction.Follow && _patrol!.DestinationNodeId >= 0)
        || (PatrolAction == AiPatrolAction.RePosition && _repositionLatched)
        || _lookForTargetLatched;

    /// <summary>Holds the posture channel the kneel and prone arms, and their children, write every tick.</summary>
    private void HoldPosture()
    {
        if (_patrolProne)
        {
            WriteChannel(AiCombatChannel.LieDown, 1f);
        }
        else if (_patrolKneeling)
        {
            WriteChannel(AiCombatChannel.Kneel, 1f);
        }
    }

    /// <summary>Returns which three-metre lateral roll probes remain inside the current node.</summary>
    private AiRollSpace AvailableRollSpace()
    {
        if (_graph?.NodeAt(CurrentNode) is not { } node)
        {
            return AiRollSpace.None;
        }

        // Orientation column 0 is the body's right vector. At heading zero forward is +Y and right +X.
        double rightX = Math.Cos(Facing);
        double rightY = Math.Sin(Facing);
        const double Probe = 3.0 * GraphNode.MetreInUnits;
        AiRollSpace space = AiRollSpace.None;
        if (IsInsideNodeBox(
            new Vector3d(Position.X - (rightX * Probe), Position.Y - (rightY * Probe), Position.Z),
            node,
            devianceMetres: 0.0))
        {
            space |= AiRollSpace.Left;
        }

        if (IsInsideNodeBox(
            new Vector3d(Position.X + (rightX * Probe), Position.Y + (rightY * Probe), Position.Z),
            node,
            devianceMetres: 0.0))
        {
            space |= AiRollSpace.Right;
        }

        return space;
    }

    /// <summary>Advances on, or hunts for, the target — <c>0x48C860</c>, <c>"Combat - Follow"</c>.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <remarks>
    /// <b>A patrol fires while it is running at you</b>, with no burst set up and no aim solve — one
    /// round per tick straight into the trigger slot, whenever the target is inside a <b>16.3°</b> cone
    /// about its forward axis (<c>dot &gt; 0.96</c>).
    /// </remarks>
    private void FollowTarget(Vector3d enemy)
    {
        int destination = _patrol!.DestinationNodeId;
        if (destination < 0)
        {
            return;
        }

        WriteChannel(AiCombatChannel.Stand, 1f);

        if (!HeadFor(destination))
        {
            // A stale or unreachable combat node cannot remain an installed Follow action. Retail's
            // movement action completes when it cannot produce another waypoint, allowing the chooser to
            // install Fire/LookForTarget on the next tick. Keeping the destination forever is the port-only
            // "guard sees me and stares" dead state.
            _route.Clear();
            DestinationNode = -1;
            _patrol.ArrivedAtDestination();
            Target?.Aim(this, AiAimMode.None);
            if (TargetSlot.IsVisible)
            {
                TurnTowards(AimPointOf(enemy, TargetCrouching), (float)AiGuardCombat.AimToleranceRadians);
            }
            return;
        }

        AdvanceInCombat();

        // 0x4522A0 to the claimed cell: the leg ends on the tick its queue empties -- standing on the destination
        // node is not enough while the cell inside it is still ahead -- and 0x48AE70 reinstalls the parent.
        if (_route.Count == 0)
        {
            _patrol.ArrivedAtDestination();
            Target?.Aim(this, AiAimMode.None);
            return;
        }

        if (!TargetSlot.IsVisible)
        {
            Target?.Aim(this, AiAimMode.None);
            return;
        }

        if (FacesForFollowFire(AimPointOf(enemy, TargetCrouching)))
        {
            if ((Target?.Count(AiAimMode.Aiming) ?? 0) >= AiCombatTarget.MaximumAimers
                && Target?.ModeOf(this) != AiAimMode.Aiming)
            {
                Target?.Aim(this, AiAimMode.Crowded);
                return;
            }

            // Retail's Follow raises the RUN signal (ai+15580 -> slot 26) and the FIRE slot
            // (ai+15500 -> slot 6) and NOTHING ELSE -- 0x48C860 never touches the fire-pose channel
            // (sub_48EF70(_, 6, _) appears only in the standing fire child 0x48D8E0). Raising the
            // fire pose here shoved the body machine into the STANDING fire chain from a moving
            // start (stand -> rack 11 -> ready 10 -> ...), where the stand-state gate then cut the
            // command each tick: guards visibly cycled the rack/recover clips at their destination
            // without one round leaving, because the trigger only forwards from states 6/18/22/27.
            // With run + trigger alone the machine goes 17 -> 18 (run-firing), which DOES forward it.
            Combat = new AiCombatCommand { PullTrigger = true };
            Target?.Aim(this, AiAimMode.Aiming);
            return;
        }

        Target?.Aim(this, AiAimMode.None);
    }

    /// <summary>Where the target really is, raised to where the combat code aims — <c>sub_451380</c>.</summary>
    /// <remarks>
    /// The guard's turn to an unseen target and every burst's aim point read the target task's own position
    /// (0.5 m up, 0.25 m crouched), not the shared record the rest of combat reads; only the distance, and a
    /// knife guard's turn, use the record.
    /// </remarks>
    // 0x451380 reads the target's own origin (Human+32, the pose's root above the feet), not a fixed standing height.
    private Vector3d TrueAimPoint() => TargetHuman?.Candidate.NodeQueryPoint ?? AimPointOf(TargetFeet, TargetCrouching);

    /// <summary>The fire and reload sub-actions the two arms share.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="weapon">The held weapon's state this tick.</param>
    private void RunSharedSubAction(Vector3d enemy, AiCombatWeaponState weapon)
    {
        Vector3d aim = TrueAimPoint();
        AiCombatSense sense = new(
            HasDetectionSlot: TargetSlot.IsRegistered,
            InCone: TargetSlot.IsInCone,
            Visible: TargetSlot.IsVisible,
            TargetPosition: aim,
            DistanceToTargetInUnits: Vector3d.Distance(Position, aim),
            LocomotionState: AiCombatChannels.StateOf(_type, AiStanceChannel.Standing),
            AmmoInClip: weapon.AmmoInClip,
            Category: weapon.Category,
            ClipSize: weapon.ClipSize,
            BurstSize: weapon.BurstSize,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0);

        // The reload the arm installs finishes in the arm's own pose, and the prone one leaves the input/direct
        // alternation alone (0x48DB10 / 0x48DD10 / 0x48DE00).
        AiStanceChannel pose = _patrolProne ? AiStanceChannel.Prone
            : _patrolKneeling ? AiStanceChannel.Kneeling
            : AiStanceChannel.Standing;
        AiCombatCommand command = _combat!.Update(
            sense,
            standing: true,
            Vision.Primary.Length,
            reloadPose: AiCombatChannels.IsInStance(_type, pose, CombatState),
            togglesReload: !_patrolProne);
        Combat = command;

        // The shared fire sub-action ending ends the whole engagement, exactly as it does in the guard
        // arm -- the patrol arm used to DISCARD this, so a patrol whose machine ended kept "fighting"
        // with a dead behaviour and never fired again.
        if (command.Ended)
        {
            EndCombat();
            return;
        }

        if (command.FirePose)
        {
            WriteChannel(AiCombatChannel.Fire, 1f);
        }

        if (command.PressReload)
        {
            WriteRawInputSlot(8, 1f);
        }

        if (command.StartedBurst is { } burst)
        {
            HitProbability = burst.HitProbability;
        }

        if (command.Aim is { } mode && Target is not null)
        {
            Target.Aim(this, mode);
        }

        if (command.TurnTo is { } point)
        {
            _combat.ReportAimed(TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians));
        }
    }

    /// <summary>The cosine a following patrol fires within — <c>0x48C860</c>'s <c>0.96</c>, a 16.3° cone.</summary>
    private const double FollowFireCosine = 0.96;

    /// <summary>Whether the record point lies inside the running fire cone — <c>0x48C860</c>.</summary>
    /// <param name="recordPoint">The shared record's point: the target's origin raised (<c>0x451380</c>).</param>
    /// <remarks>
    /// The test is three-dimensional: the view's forward (the run levels the view, <c>0x451540</c>) against the
    /// direction from the AI's view position (its feet) to the record point, which stands about a metre and a half
    /// over them. So the cone narrows with the height difference, and a target a few metres off is above it: a
    /// running patrol holds its fire until it is clear of the target, and only fires at range.
    /// </remarks>
    private bool FacesForFollowFire(Vector3d recordPoint)
    {
        double dx = recordPoint.X - Position.X;
        double dy = recordPoint.Y - Position.Y;
        double dz = recordPoint.Z - Position.Z;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length == 0.0)
        {
            return false;
        }

        double forwardX = -Math.Sin(Facing);
        double forwardY = Math.Cos(Facing);
        return ((forwardX * dx) + (forwardY * dy)) / length > FollowFireCosine;
    }

    /// <summary>Sets a destination, but only when it is a new one.</summary>
    /// <param name="node">Where to head.</param>
    /// <remarks>
    /// <see cref="GoTo"/> clears the route and re-anchors the leg origin every time it is called, so a
    /// combat behaviour that asks for the same destination on every tick would restart the leg forever
    /// and the soldier would never actually travel. The original sets the destination once
    /// (<c>HumanAI+15848</c>) and the movement stepper walks the queue; this keeps that shape.
    /// </remarks>
    private bool HeadFor(int node)
    {
        if (DestinationNode == node)
        {
            return CurrentNode == node || _route.Count > 0;
        }

        // The combat walk finishes at the claimed sub-slot inside the node (0x4522A0 with ai+16048).
        return GoTo(node, _combatFreePosition);
    }

    /// <summary>How close to the claimed sub-slot counts as standing on it — half a metre.</summary>
    private const double FreePositionArrivalInUnits = 2048.0;

    /// <summary>Steps the soldier along its combat route, the same walk the patrol path uses.</summary>
    /// <remarks>
    /// The move-and-fight arm sets a Follow/RePosition destination but the original then <em>walks</em> to
    /// it — the AI steers its <c>Human</c> through the input frame and the body's mover carries it. Without
    /// this the port set a destination it never travelled, which is exactly "guards engage but don't chase".
    /// The distance covered is the run clip's own root motion (M8), so no speed is invented. Once the route
    /// delivers the body to the node, the walk finishes at the <b>claimed sub-slot</b> inside it — retail's
    /// <c>sub_4522A0</c> against the position at <c>HumanAI+16048</c>.
    /// </remarks>
    private void AdvanceInCombat()
    {
        if (_route.Count > 0)
        {
            // The walk clip plays while the walker writes forward, so the next tick has root motion to move by.
            Advance();
            _movingInCombat = WalksForward;
        }
    }

    private bool _patrolKneeling;

    // How many times the patrol chooser has run -- for scenario traces.
    private int _chooserCalls;

    // Whether RePosition (0x48C760) owns the per-tick until its walk arrives.
    private bool _repositionLatched;

    // Whether LookForTarget (0x48CAB0) is the run function: installed by the stand arm, it runs one tick.
    private bool _lookForTargetLatched;

    // Slot 26, the tense run, which RePosition, Follow and LookForTarget write first thing on every tick they run
    // (HumanAI+15580 = 1.0).
    private bool _tenseRunThisTick;

    /// <summary>Where the target human stands now, seen or not -- what <c>0x451380</c> reads (its <c>+32</c>).</summary>
    /// <param name="fallback">The point to use when no human is tracked as the target.</param>
    /// <remarks>
    /// The shared record's copy (<c>+272</c>) is refreshed only while the target is seen or an event arrives
    /// (<c>0x48A920</c>); the node lookup <c>0x4530A0</c> and the free-position search <c>0x48B050</c> both call
    /// <c>0x451380</c> instead, which reads the target human itself. Mission 4's guard 411 lost sight of the squad for
    /// 36 ticks and its node still moved from 13 to 14 halfway through.
    /// </remarks>
    private Vector3d LiveTargetFeet(Vector3d fallback) => TargetHuman is null ? fallback : TargetFeet;

    // 0x451380's point for a node search: the target's origin raised half a metre (0x4530A0, 0x48B050).
    private Vector3d? LiveTargetNodePoint() => TargetHuman?.Candidate.NodeQueryPoint;

    /// <summary>The combat action's distance to its target -- <c>0x48A920</c>'s <c>+16</c> of the action block.</summary>
    /// <param name="recordFeet">The shared record's position, as the port keeps it: the target's feet.</param>
    /// <remarks>
    /// Measured from the soldier's own origin (Human+32) to the record's point, which <c>0x451380</c> stored as the
    /// target's origin raised half a metre (a quarter crouched). Close combat, the lie-down range, the walk toward the
    /// target and the grenade window all compare against it.
    /// </remarks>
    private double CombatDistance(Vector3d recordFeet) =>
        Vector3d.Distance(OriginPosition, AimPointOf(recordFeet, TargetCrouching));

    /// <summary>0x48A920's tracking of the node nearest the target -- <c>HumanAI+15852</c> and <c>+15856</c>.</summary>
    /// <param name="enemy">Where the target is.</param>
    private void TrackTargetNode(Vector3d enemy)
    {
        int node = NearestNode(enemy);
        if (node < 0)
        {
            _followTargetNode = -1;
        }
        else if (_followTargetNode == -1)
        {
            _followTargetNode = node;
        }
        else if (_followTargetNode != node)
        {
            _followTargetNode = node;
            _followResearch = true;
        }
    }
}

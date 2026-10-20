using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The level's soldiers as the game runs them: their brains, their health and weapons, what they hear and see, and
/// the player's own body where it is a soldier's.
/// </summary>
/// <remarks>
/// What draws them is <c>SoldierRenderer</c>, which holds one of these and reads it; this holds nothing it draws. The
/// soldiers are walked through <see cref="Roster"/>, group by group in the order the level made them, which is the
/// order their brains tick in.
/// </remarks>
public sealed class SoldierSimulation : IHumanEvents
{
    private const string LogCategory = "Soldier";

    // The blade's own sound, which 0x45D3C0 adds to the grunt when a knife lands.
    private const string KnifeStabSound = "stab_01";

    // Each soldier's view by its task, for the hit events the task receives.
    private readonly Dictionary<HumanTask, SoldierView> _viewsByTask = [];

    private readonly IOpenIgiLogger _logger;

    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SoldierSimulation"/> class.</summary>
    /// <param name="logger">Where the soldiers report.</param>
    public SoldierSimulation(IOpenIgiLogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Marks the simulation as gone with its renderer.</summary>
    internal void Retire() => _disposed = true;

    /// <summary>Forgets the ground heights the bodies were set down on, for a new level.</summary>
    internal void ForgetGroundSnaps() => _groundSnaps.Clear();

    /// <summary>
    /// Gets or sets what is told once a soldier's mount is settled for the tick -- on a stationary gun or off it -- which is
    /// when its drawn weapon follows.
    /// </summary>
    internal Action<SoldierView, AiSoldier>? MountSettled { get; set; }

    /// <summary>Gets or sets what is told as a soldier falls dead: where its body lies and how, for the corpse that is drawn.</summary>
    internal Action<SoldierFall>? BodyFell { get; set; }

    private int _deathsSoFar;

    /// <summary>Puts a soldier into the general death state — <c>0x45C440</c>, joined up.</summary>
    /// <remarks>
    /// <para>
    /// Four things happen here and every one of them is read. The body <b>turns to the round</b>
    /// (<c>0x45DAA0</c>); the death <b>animation is chosen</b> from the killing weapon's category, the zone
    /// struck and which way the round came (<see cref="SoldierDeath.Choose"/>); the <b>fall solver</b>
    /// fans its nine rays and returns the corrective velocity (<see cref="SoldierDeathFall"/>); and the
    /// brain is told, so it steps the body every tick from then on instead of freezing it.
    /// </para>
    /// </remarks>
    internal void Die(
        SoldierView view,
        HumanHit hit,
        Vector3d origin,
        Vector3d end,
        WeaponCategory category,
        SoldierDeathMode mode)
    {
        Matrix3f orientation = view.Orientation;
        Vector3f forward = new(orientation.M12, orientation.M22, orientation.M32);

        HumanSoldierClass soldierClass = view.Soldier.Class;
        Brains.TryGetValue(view, out AiSoldier? brain);
        int scriptOverride = brain?.DeathAnimationOverride ?? -1;
        Vector3d travel = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        float facing = GammaOf(view);

        int animation;
        if (brain?.Body.PostureDeathAnimation is { } postureDeath)
        {
            // A kneeling or prone body dies in its posture's own state (0x45C5A0, 0x45C650), which plays a
            // fixed clip and does not turn.
            animation = postureDeath;
        }
        else
        {
            // 0x45C440 turns the body to the round (0x45DAA0) only in its thrown branch, which the script's
            // override skips: a crumple falls with the facing it died with.
            bool front = true;
            if (scriptOverride == -1 && soldierClass.TurnsToTheShot(mode))
            {
                front = SoldierDeath.TurnToTheShot(travel, forward, ref facing);
            }

            // The engine's own rand(2). Alternating is not the same distribution, but the two retail
            // head-row entries are equal and the two crumple clips are a pair, so what the flip has to do
            // is vary them.
            int flip = _deathsSoFar++ & 1;
            animation = soldierClass.ChooseDeathAnimation(mode, hit.Zone, front, flip, scriptOverride);
        }

        Vector3d fall = default;
        if (animation >= 0 && view.Bones.Set.TryGet(animation, out BoneAnimation clip) && Cast is { } cast)
        {
            fall = SoldierDeathFall.Solve(
                clip,
                view.Position,
                facing,
                (from, to) => cast(from, to, out WorldRayHit blocked)
                    ? new SoldierDeathRay(true, blocked.Position, Vector3d.Distance(from, blocked.Position))
                    : default);
        }

        view.PlaceAt(view.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));
        view.Kill(animation);

        if (brain is not null)
        {
            brain.Kill(fall, facing);
        }

        // And the death cry, from here rather than from AiSoldier.Kill: the per-tick loop calls Kill on
        // every corpse it steps, so a hook there would restart the sound thirty times a second. This is
        // the alive-to-dead edge, and it happens once.
        DeathSound?.Invoke(view.Position);
        FootCursors.Remove(view);

        // Everything above is the original's, and it stays that way whatever follows: the body's
        // authoritative position, the clip that moves it and what the guards around it are told are all
        // exactly what they were. The ragdoll its renderer seeds from the fall replaces THE POSE THAT IS
        // DRAWN, and nothing else.
        if (FindAgent(view) is { } agent)
        {
            Vector3f carried = view.RootMotion();
            double grounded = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d ragdollOrigin = new(view.Position.X, view.Position.Y, grounded + view.RootSample.Z);
            Vector3d dropPosition = view.Position;
            Matrix3f dropRotation = view.Orientation;
            WeaponType? droppedWeapon = WeaponForDrop(agent.CarriedWeaponType, agent.WeaponType);
            if (droppedWeapon is not null)
            {
                BonePose pose = view.UnrotatedPose();
                if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
                {
                    Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
                    Vector3f offset = view.Orientation.Transposed().Transform(hand);
                    dropPosition = ragdollOrigin + new Vector3d(offset.X, offset.Y, offset.Z);
                    dropRotation = view.Orientation * pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
                }
            }

            // What the body was already doing. A soldier's clip root motion is its only velocity -- the port gives
            // one nothing else -- and it is a per-tick delta already, so it goes in as it stands, turned out of body
            // space by the heading.
            Vector3d velocity = SoldierDeathFall.RotateAboutZ(new Vector3d(carried.X, carried.Y, carried.Z), facing);
            BodyFell?.Invoke(new SoldierFall(view, ragdollOrigin, hit.Bone, travel, category, velocity, droppedWeapon is not null));

            if (droppedWeapon is { } dropped)
            {
                WeaponDrop?.Invoke(dropped, dropPosition, dropRotation, DropVelocity(velocity, travel));
            }
        }
    }

    /// <summary>One soldier's update: its rig, then its AI and movement.</summary>
    internal void TickSoldier(SoldierView view, AiSoldier brain)
    {
        if (!ConditionalContainerGate.IsTaskActive(view.Soldier) || !Ticked.Add(view))
        {
            return;
        }

        bool collectDiagnostics = CollectDiagnostics;
        SoldierEnemy enemy = Enemy?.Invoke() ?? default;
        bool enemyAlive = enemy.Alive;
        bool enemyCrouching = enemy.Crouching;

        // Vision watches the mark; combat aims at the feet + 0.5 m (sub_451380). With no body to speak of,
        // the point given is the mark itself.
        Vector3d feet = enemy.Feet ?? enemy.Eye;
        Vector3d playerMark = enemy.Feet is null
            ? enemy.Eye
            : new Vector3d(
                feet.X,
                feet.Y,
                feet.Z + AiSoldier.DetectionMarkAboveFeet(enemyCrouching));

        // Every soldier's own rig advances (a group's shared mesh is only the model).
        view.Tick();

        // Read here, not after the clip selection below: the event belongs to the interval this
        // Tick just advanced, and SetMoving may replace the clip further down. A fresh BoneClip has
        // no interval yet, so asking afterwards would quietly swallow the step.
        if (!view.IsDead && view.Bones.SourceOf(0) is { } bone0)
        {
            if (bone0.CrossedEvent(FootstepSounds.EventId))
            {
                ReportFootstep(view);
            }

            // Authored animation triggers: event 1052 plays "gunpunch_01" (animation 70 swing),
            // event 1040 plays "bodyfall_9", etc.
            if (AnimationTriggers is not null)
            {
                foreach (AnimTriggerDefinition trigger in AnimationTriggers.Definitions.Values)
                {
                    if (bone0.CrossedEvent(trigger.EventId))
                    {
                        ScriptSound?.Invoke(trigger.Resource, view.Position, false);
                    }
                }
            }
            else if (bone0.CrossedEvent(1052))
            {
                ScriptSound?.Invoke("gunpunch_01", view.Position, false);
            }

            // Melee strike connects at the swing's impact frame (event 1052, time 1280.0 in 001.iff).
            // If a strike is pending against the player, deliver it at this exact moment.
            if (bone0.CrossedEvent(1052)
                && brain.ConsumeMeleeStrike(out float strikeDamage)
                && brain.PlayerIsEnemy
                && brain.TargetIsPlayer)
            {
                MeleeStrikeSink?.Invoke(brain.Position, strikeDamage);
            }
        }

        // Child Train collision starts at its authored placeholder and receives its derived consist
        // transform before this update. Retry registration here so the first live mesh pose can reproduce
        // retail's pre-scene Human_SetParent call. This is deliberately before animation root motion: the
        // authored Human position is the parent origin.
        SoldierAgent? member = FindAgent(view);
        if (member is { IsCutsceneActor: true })
        {
            AttachStagedActor(member);
        }

        // The AI runs second, so the root motion it is handed is this tick's, not last tick's.
        if (brain.RequestedAnimation >= 0
            && view.Bones.SourceOf(0)?.EndedThisTick == true)
        {
            brain.ReportAnimationFinished();
        }

        if (member is { IsCutsceneActor: true })
        {
            AiPatrolCommandKind? patrolCommand = brain.Route?.Current?.Kind;
            bool authoredAnimation = brain.RequestedAnimation >= 0
                || patrolCommand == AiPatrolCommandKind.Animation;
            if (authoredAnimation)
            {
                AttachCutsceneActorToMovingGround(
                    view,
                    brain.Position,
                    member.Position * WorldScene.UnitsPerMetre);
                Vector3f rootDelta = view.RootMotion();
                if (Cutscenes?.AdvanceAttachedActor(view.Soldier, rootDelta) == true)
                {
                    // Native attached movement carries Human+32 with the parent while the local
                    // frame keeps the relative pose. Track the ride on the brain so a later
                    // detach (Level 1's 1201 consist unregisters at the scene boundary) continues
                    // from the wagon instead of the stale pre-attach spawn 10 m behind it.
                    if (Cutscenes.TryGetAttachedDrawTransform(
                        view.Soldier, out Vector3d attachedNow, out _))
                    {
                        brain.SeparateTo(attachedNow * WorldScene.UnitsPerMetre);
                    }
                }
                else if (rootDelta.X != 0f || rootDelta.Y != 0f || rootDelta.Z != 0f)
                {
                    // The ordinary Human mover consumes the clip's root delta into Human+32 even
                    // when the AI action itself is a non-locomotion PlayAnimation. Cutscene jumps
                    // use exactly that path; keeping the task's authored spawn here froze Jones in
                    // mid-air while EditCamera continued targeting the moving Human.
                    // The brain's own body step (0x45D080 -> 0x4614B0) already carries the horizontal
                    // part this tick, so only the vertical part is added here. Adding both doubled
                    // every staged actor's sway: mission 8's guard 3336 drifted 61 cm where the
                    // original's drifts 30.
                    brain.SeparateTo(brain.Position + new Vector3d(0d, 0d, rootDelta.Z));
                    member.HasCutsceneRootMotion = true;
                }

            }

            if (patrolCommand is { } command && command.IsMovement())
            {
                member.HasCutsceneGameplayMovement = true;
            }
        }

        if (view.IsDead)
        {
            // A corpse is still stepped. The death state's per-tick (0x45C700) adds the fall solver's
            // corrective velocity, the soldier update moves the body by the death clip's own root
            // motion, and the ground query catches it. Freezing the body here is what left it standing.
            if (brain.Activity != AiSoldierActivity.Dead)
            {
                brain.Kill();
            }

            brain.TickDeadBody(view.RootMotion(), view.Bones.SourceOf(0)?.EndedThisTick ?? false);
            view.PlaceAt(brain.Position, view.Orientation);
            SoldierAgent? corpse = FindAgent(view);
            if (corpse is not null)
            {
                corpse.FiredThisTick = false;
            }
            WeaponSound?.Invoke(brain.RuntimeId, false, corpse?.WeaponType, brain.Position);
            return;
        }

        // The clip's own delta in the body's frame: forward and sideways, signed.
        Vector3f clipDelta = view.RootMotion();
        double travel = clipDelta.Y;
        double lateral = clipDelta.X;
        if (brain.SoldierClass.ScalesEkkRootMotion && brain.Type == AiType.Ekk)
        {
            // 0x4550B0 scales a female soldier's delta translation by flt_56E1E8 when her brain is Ekk.
            double scale = PlayerDeltaTranslationScale?.Invoke() ?? HumanMotion.DefaultDeltaTranslationScale;
            travel *= scale;
            lateral *= scale;
        }

        // Retail ground truth, measured by hooking 0x48E920 in the original: a walking guard covers
        // 160-185 units a tick, about 1.28 m/s, varying through the gait cycle. Recorded here so the
        // port's own figure can be compared without another instrumented run.
        if (collectDiagnostics && brain.Activity == AiSoldierActivity.Patrolling && travel > 0.0)
        {
            TravelSamples++;
            TravelTotal += travel;
            TravelMax = Math.Max(TravelMax, travel);
        }

        // The alarm the soldier watches is the one ITS OWN +15044 names -- resolved per AI, as the
        // retail per-tick resolves the control task each tick. First sight of an id lazily makes
        // the state, since the ids arrive only once the CREATE dispatch has run the script.
        bool alarmOn = false;
        bool alarmWasOn = false;
        if (brain.AlarmControlId != -1)
        {
            if (AlarmControls?.TryGet(brain.AlarmControlId, out AiAlarmControlState? watched) == true)
            {
                alarmOn = watched!.IsOn;
                alarmWasOn = watched.WasOn;
            }
        }

        // The detection scan is offered every human -- the player and every soldier -- and the brain
        // fights whoever the event that started its fight named; it aims at that human.
        brain.TickAmong(
            TickCount,
            CandidatesFor(view, brain, feet, playerMark, enemyAlive, enemyCrouching),
            travel,
            alarmOn,
            alarmWasOn,
            lateral);
        Vector3d aimFeet = brain.TargetFeet;
        bool aimCrouching = brain.TargetCrouching;

        // Fallback for headless environments or missing animation clips: if event 1052 could not
        // be evaluated, deliver any pending strike immediately so damage is not dropped.
        if (_combatClipMissing.Contains(view) || view.Bones.SourceOf(0) is null)
        {
            if (brain.ConsumeMeleeStrike(out float meleeDamage) && brain.TargetIsPlayer && brain.PlayerIsEnemy)
            {
                ScriptSound?.Invoke("gunpunch_01", brain.Position, false);
                MeleeStrikeSink?.Invoke(brain.Position, meleeDamage);
            }
        }

        if (member is not null)
        {
            MountSettled?.Invoke(view, brain);
            if (collectDiagnostics && brain.Arm == AiCombatArm.Gunner)
            {
                string report = $"runtime={brain.RuntimeId} gunnerId={brain.GunnerId} "
                    + $"mounted={(brain.IsMounted ? "true" : "false")} "
                    + $"task={(brain.MountedGun is null ? "-" : brain.MountedGun.TaskId.ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                    + $"activity={brain.Activity}";
                if (_lastGunnerRuntimeReports.GetValueOrDefault(brain.RuntimeId) != report)
                {
                    _lastGunnerRuntimeReports[brain.RuntimeId] = report;
                    _logger.Information(LogCategory + ".Gunner", $"{report}");
                }

                if (TickCount % 300 == 1)
                {
                    string timer = ScriptVariables?.TryRead("LevelTimer_1608.nTick", out QvmValue timerValue) == true
                        ? timerValue.ToString()
                        : "missing";
                    string dispatched = brain.LastDispatchedEvent?.ToString() ?? "-";
                    _logger.Information(
                        LogCategory + ".Gunner",
                        $"diagnostic runtime={brain.RuntimeId} lastEvent={dispatched} "
                        + $"timer1608={timer}");
                }
            }
        }

        // The pump half of the [aiflow] diff: an event that was QUEUED shows up here the tick it is
        // drained, whether or not any script acted on it. Noise delivered but never drained points at
        // the pump; drained but never acted on points at the script/actions.
        if (collectDiagnostics && TickCount - _lastDrainLogTick >= 30)
        {
            int gunshot = brain.Events.DrainedCount(AiEvent.Gunshot);
            int miss = brain.Events.DrainedCount(AiEvent.GunshotMiss);
            int impact = brain.Events.DrainedCount(AiEvent.GroundImpact);
            int explosion = brain.Events.DrainedCount(AiEvent.Explosion);
            int grenadeThrown = brain.Events.DrainedCount(AiEvent.GrenadeThrown);
            int grenadeLand = brain.Events.DrainedCount(AiEvent.GrenadeLand);
            int flashbang = brain.Events.DrainedCount(AiEvent.Flashbang);
            int walk = brain.Events.DrainedCount(AiEvent.Walk);
            int damage = brain.Events.DrainedCount(AiEvent.TakingDamage);
            if (gunshot + miss + impact + explosion + grenadeThrown + grenadeLand + flashbang + walk + damage > 0)
            {
                _lastDrainLogTick = TickCount;
                _logger.Information(
                    LogCategory + ".Flow",
                    $"#{brain.RuntimeId} drained gunshot={gunshot} miss={miss} "
                        + $"explosion={explosion} grenadeThrown={grenadeThrown} "
                        + $"grenadeLand={grenadeLand} flashbang={flashbang} "
                        + $"impact={impact} walk={walk} dmg={damage} act={brain.Activity}");
            }
        }

        if (member is not null)
        {
            member.FiredThisTick = false;
        }

        // The body machine consumes this tick's AI channel writes before the weapon consumes the
        // body's forwarded trigger. Running it after FireIfAsked let a round leave while the body
        // was still in rack/ready/recover and skipped state 18 entirely during a running burst.
        // 0x45D080 ticks the body every tick whatever the AI is doing, which is what lets a patrolling
        // guard flinch; its state drives the clip in a fight, and a flinch whatever the activity.
        // A gunner on its gun is in the use-object state (36), which only a fresh press of slot 24 -- the
        // dismount -- or death leaves.
        int? combatState = null;
        bool animationDone = (view.Bones.SourceOf(0)?.EndedThisTick ?? false)
            || _combatClipMissing.Contains(view);
        int bodyState = brain.TickCombatState(animationDone);
        if (!brain.IsMounted
            && brain.Activity is AiSoldierActivity.Engaging or AiSoldierActivity.FiringAtTask)
        {
            combatState = bodyState;
        }

        // The grenade leaves the hand when the throw state ENDS -- the clip has swung. The
        // decision to throw was the combat arm's own (the retail probability, distance band and
        // has-grenade tests in AiPatrolCombat); this is only the release.
        int previousCombatState = _lastCombatState.TryGetValue(view, out int rememberedState)
            ? rememberedState
            : -1;
        _lastCombatState[view] = combatState ?? -1;
        if (brain.SoldierClass.GrenadeThrowState is { } grenadeState
            && previousCombatState == grenadeState
            && (combatState ?? -1) != grenadeState
            && Grenades is { } lobbed)
        {
            ThrowAiGrenade(brain, feet, lobbed);
        }

        bool fired = FireIfAsked(view, brain, aimFeet, aimCrouching);
        WeaponType? soundingWeapon = brain.MountedGun?.Weapon ?? member?.WeaponType;
        Vector3d soundPosition = brain.MountedGun?.Position ?? brain.EyePosition();
        WeaponSound?.Invoke(brain.RuntimeId, fired, soundingWeapon, soundPosition);

        // sub_48A920, IGI.exe: detected_01..06, once per combat-action argument block. The
        // dword_5416CC cooldown is process/mission-wide and requires strictly more than 90 ticks.
        if (brain.DetectionShoutRequestedThisTick
            && (LastDetectionShoutTick == -1 || TickCount - LastDetectionShoutTick > 90))
        {
            int variant = checked((int)AiRandom.NextBounded(6) + 1);
            ScriptSound?.Invoke($"detected_{variant:00}", brain.Position, true);
            LastDetectionShoutTick = TickCount;
        }

        // An engaged guard pulls in the guards beside it: AIEVENT_ALERT with a 7 m reach and filter
        // 0x26, so only its own side admits it (sub_48A920 -> sub_48AB10). This is how one guard's
        // fight wakes the room even before anybody fires.
        if (brain.AlertPingDueThisTick)
        {
            foreach ((SoldierView neighbourView, AiSoldier neighbour) in Brains)
            {
                if (!ReferenceEquals(neighbour, brain)
                    && neighbourView.Soldier.Team == view.Soldier.Team
                    && ConditionalContainerGate.IsTaskActive(neighbourView.Soldier))
                {
                    neighbour.ReceiveAlert(brain.Position, brain.RuntimeId, brain.TargetId);
                }
            }
        }
        if (collectDiagnostics)
        {
            ReportChannels(brain);
        }

        // A patrol ANIMATION command names a clip outright; a soldier in a fight plays its WEAPON's
        // own clips; otherwise the gait picks between the walk cycle and the soldier's stand
        // animation.
        HumanSoldierClass soldierClass = brain.SoldierClass;
        int walkAnimation = soldierClass.WalkAnimation;
        int runAnimation = brain.Body.StateTable.AnimationOf(soldierClass.RunState);
        if (brain.Body.IsFlinching)
        {
            // A flinch owns the body over any other clip: every per-tick leaves for it on the pulse.
            if (!view.PlayStateAnimation(brain.Body.CurrentAnimation, soldierClass.States.Loops(brain.Body.State)))
            {
                _combatClipMissing.Add(view);
            }
        }
        else if (brain.RequestedAnimation >= 0)
        {
            view.PlayStateAnimation(brain.RequestedAnimation, loops: false);
        }
        else if (brain.IsMovingInCombat || (brain.Activity == AiSoldierActivity.Panicking && brain.WalksForward))
        {
            // The soldier is travelling a combat route this tick -- advancing, repositioning or hunting a
            // lost target while it watches. A chasing guard RUNS (retail's Combat-Follow runs in state 17
            // = anim 48 at ~4.4 m/s), and the run clip's own root motion is what carries it at that speed
            // (AiSoldier.AdvanceInCombat reads it). Fall back to the walk if the set has no run clip.
            //
            // This test MUST come before the combat-state branch: an engaged hunter otherwise plays
            // the fire pose, whose zero root motion feeds AdvanceInCombat a zero step -- the exact
            // "engaging, never moving" stall. Retail's Follow is a full-body run state (17), not the
            // stand cycle with feet glued down.
            if (runAnimation < 0 || !view.SetMoving(true, runAnimation))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else if (brain.IsMounted)
        {
            // HumanSoldier state 36 is the attached emplacement pose. It keeps ownership while
            // the StationaryGun task reads slot 6 and fires; it does not enter the ordinary
            // 0/12/10/6 carried-weapon body cycle. It is the body's, not the brain's: a gunner the
            // Activate action put on the gun stays on it between fights.
            int mountedState = AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Mounted);
            int animation = brain.Body.StateTable.AnimationOf(mountedState);
            if (animation < 0
                || !view.PlayStateAnimation(animation, soldierClass.States.Loops(mountedState)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (combatState is { } combatBodyState)
        {
            int anim = brain.Body.CurrentAnimation;
            bool placed = anim >= 0
                && view.PlayStateAnimation(anim, soldierClass.States.Loops(combatBodyState));
            if (placed)
            {
                _combatClipMissing.Remove(view);
            }
            else
            {
                view.SetMoving(false, walkAnimation);
                _combatClipMissing.Add(view);
            }
        }
        else if (brain.Activity == AiSoldierActivity.FallenFlat)
        {
            int state = AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Prone);
            int animation = brain.Body.StateTable.AnimationOf(state);
            if (animation < 0
                || !view.PlayStateAnimation(animation, soldierClass.States.Loops(state)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (brain.IsCrouching)
        {
            // A patrol's Crouch command holds the kneel channel every tick (0x48E920): the body goes
            // down (state 13) and holds the kneel (19) until a walk releases it.
            int state = brain.CombatState;
            int animation = brain.Body.CurrentAnimation;
            if (animation < 0 || !view.PlayStateAnimation(animation, brain.Body.StateTable.Loops(state)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (brain.WalksForward && !brain.IsWalking)
        {
            // The patrol cursor's gait flag (+21) is applied EVERY tick by the retail walker
            // (0x48E920): a RunTo leg installs the run gait and it stays until the next movement
            // command changes it. The alarm-response routes (level 1's 2600/2605-2616) are authored
            // as RunTo, so an alarmed guard RUNS between its posts -- playing the walk clip here is
            // what made them amble out of the barracks at 1.28 m/s. The run clip's own root motion
            // carries the brain at run speed; sets without one fall back to the walk.
            if (runAnimation < 0 || !view.SetMoving(true, runAnimation))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else
        {
            // The body walks while the walker writes forward, and stands while it holds -- at a door,
            // on arrival, or turning on the spot.
            view.SetMoving(brain.WalksForward, walkAnimation);
        }

        view.PlaceAt(brain.Position, brain.Orientation);
    }

    /// <summary>Turns a raised trigger into a round, at the weapon's own cadence.</summary>
    /// <remarks>
    /// <para>
    /// The AI never fires: it holds slot 6 down and the weapon decides when a round comes out, which is
    /// the split the original has between <c>0x48D8E0</c> and the human's weapon tick <c>0x462E50</c>.
    /// The clip is what the AI watches to count its burst, so it has to drain here and nowhere else.
    /// </para>
    /// <para>
    /// The reload takes the weapon's own <c>Reload Time</c>; both of the AI's two reload routes reach
    /// the same place, so they are not told apart here.
    /// </para>
    /// </remarks>
    private bool FireIfAsked(SoldierView view, AiSoldier brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        SoldierAgent? member = FindAgent(view);
        AiStationaryGun? mounted = brain.MountedGun;
        WeaponType? weapon = mounted?.Weapon ?? member?.WeaponType;
        if (weapon is null)
        {
            return false;
        }

        if (mounted is not null)
        {
            if (mounted.FireCooldownTicks > 0)
            {
                mounted.FireCooldownTicks--;
            }
        }
        else if (member is not null)
        {
            if (member.FireCooldown > 0)
            {
                member.FireCooldown--;
            }

            if (member.ReloadTicks > 0 && --member.ReloadTicks == 0)
            {
                member.AmmoInClip = weapon.RoundsPerClip;
            }
        }

        AiCombatCommand command = brain.Combat;

        // 0x478BA0 zeroes the gun's rounds-this-pull count whenever the trigger is up.
        if (!command.PullTrigger && member is not null)
        {
            member.RoundsThisPull = 0;
        }

        if (mounted is null && member is not null
            && (command.PressReload || command.ReloadWeaponDirectly)
            && member.ReloadTicks == 0
            && member.AmmoInClip < weapon.RoundsPerClip)
        {
            member.ReloadTicks = Math.Max(1, (int)(weapon.ReloadTimeSeconds * 30f));
            return false;
        }

        // IGI 1 does not hand slot 6 straight to the gun. The HumanSoldier body forwards it only from
        // states 6, 18, 22 and 27 (standing, running, kneeling and prone fire respectively). Previously
        // this method ran before the body state tick, so rounds left during rack/ready/recover clips.
        bool bodyForwardsTrigger = mounted is not null
            || (!brain.CombatStateChangedThisTick && brain.Body.IsFiring);
        bool blocked = mounted is not null
            ? mounted.FireCooldownTicks > 0 || mounted.AmmunitionSample <= 0
            : member is null || member.FireCooldown > 0 || member.AmmoInClip <= 0 || member.ReloadTicks > 0;
        if (!command.PullTrigger || !bodyForwardsTrigger || blocked)
        {
            return false;
        }

        if (mounted is not null)
        {
            if (!mounted.ConsumeRound())
            {
                return false;
            }
            mounted.FireCooldownTicks = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
        }
        else
        {
            member!.AmmoInClip--;
            member.FireCooldown = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
        }
        RoundsFired++;
        if (member is not null)
        {
            member.FiredThisTick = true;
            member.ShotSequence++;
        }

        // The round leaves along where the GUN is pointed, not at the target's true position. Retail's
        // round is a hit-scan ray resolved in the fire tick (GunShot per-tick 0x47B750): the guard's body
        // was turned onto the TRACKED aim point (0x48D8E0 holds fire until the facing is within 2 degrees
        // of it), the ray flies that way, and the hit probability rides the cast's own context (slot+68,
        // loaded from Human+1236 at spawn, sub_474360) to be rolled only when the trace actually meets a
        // human. So the tracked aim's lag against a MOVING target is a real, geometric source of misses,
        // and a round that fails the roll flies on to whatever is behind.
        //
        // A burst-firing guard carries its aim in Combat.TurnTo (the tracked point, started on the target
        // at burst setup, 0x48B9C0). A follow-firing patrol has no aim solve at all (ai.md 13.7) -- it
        // sprays along its own facing whenever the target is inside a 16.3-degree cone, so its ray runs
        // along the body's heading at the aim point's height.
        Vector3d target = brain.Combat.TurnTo ?? SprayTarget(brain, enemyFeet, enemyCrouching);
        Vector3d origin = mounted?.Position ?? brain.EyePosition();
        Vector3d aim = SpreadRound(brain, mounted, weapon, member, origin, target);
        OnRound?.Invoke(origin, aim, brain.HitProbability, weapon, view);

        // Every other soldier near the shot hears it -- the same AIEVENT_GUNSHOT a player's shot raises.
        NotifyGunshot(origin, weapon.AiDetectionRange, except: view);
        return true;
    }

    private readonly HashSet<SoldierView> _combatClipMissing = [];

    private readonly Dictionary<SoldierView, int> _lastCombatState = [];

    private int _lastDrainLogTick = -100;

    private readonly Dictionary<int, string> _lastGunnerRuntimeReports = [];

    private const string TraceLogCategory = LogCategory + ".Trace";

    /// <summary>How far above the declared position the ground probe starts — 0.5 m.</summary>
    internal const double ProbeAboveInUnits = 2048.0;

    /// <summary>How far below the declared position the probe reaches — 2 m, the sliding step-down budget.</summary>
    // The terrain query itself only reaches this far down (TerrainHeightQuery.SearchDepthUnits), so
    // widening the acceptance window to the same depth settles every surface the query can find.
    // Level 4 authors several soldiers 2.3-3.1m above the terrain (larger than the old 2m window),
    // which otherwise float; a placed surface the ray cast finds (a roof, a vehicle) is higher than the
    // terrain and wins, so a soldier standing on one is not pulled down. A character far above the
    // terrain (a rooftop watcher) has no surface within the search depth and is left alone.
    private const double ProbeBelowInUnits = 16385.0;

    /// <summary>
    /// Retail IGI.exe Human_GroundCollision raises a standing human's task position by 1638.4
    /// engine units before subtracting the animated root height and querying the supporting surface.
    /// </summary>
    private const double HumanGroundCheckLiftInUnits = 1638.4;

    /// <summary>
    /// Retail Human_GroundCollision supplies -40960.0 as the first distance to
    /// World_GetNextZSpecificMesh. This is deliberately deeper than the normal two-metre
    /// placement probe so a staged cutscene actor can acquire a vehicle below its task origin.
    /// </summary>
    private const double HumanGroundProbeDownInUnits = 40960.0;

    // The soldiers as the simulation walks them: each drawn group's roster, in the groups' order.
    internal readonly List<SoldierModelGroup> Roster = [];

    internal readonly Dictionary<SoldierView, HumanHealth> Health = [];

    private readonly Dictionary<SoldierView, HumanRecoil> _recoil = [];

    // FUN_004D9620 writes Human+0xA0 from FUN_004D0950(model), which reads the authored model bound
    // at model+0x78. This is the exact value used by the retail detector; the derived vertex radius is
    // not a substitute because it is materially smaller for the retail soldier models.
    internal readonly Dictionary<SoldierView, float> DetectorMarkerScaleMetres = [];

    internal readonly IgiRandom AiRandom = new();

    // Each soldier's combat row, resolved from its own AIType at the difficulty in force. It is what
    // gives a guard its maximum health -- sub_406F70 writes the row's "Damage scale" over Human+600,
    // so a guard is worth 0.3 of a person on easy and 0.8 on hard, not the constructor's 1.0.
    internal readonly Dictionary<SoldierView, AiCombatParameters> Combat = [];

    // A dead soldier cannot stay in a shared group: the group is skinned from ONE representative's pose, so
    // a corpse there would keep standing in the group's idle animation. Each gets its own single-member
    // group instead, ticked and re-skinned from its own rig, which is what lets it fall.
    internal readonly Dictionary<SoldierView, AiSoldier> Brains = [];

    internal readonly Dictionary<int, AiSoldier> BrainsByRuntimeId = [];

    internal readonly Dictionary<GuardGeneratorTask, SoldierView> GuardGenerators = [];

    internal LevelExpressionRuntime? GuardExpressions;

    internal ScriptVarRegistry? ScriptVariables;

    internal readonly EnemyDetectorOverlay BinocularOverlay = new();

    // The soldiers the last binocular scan drew a contact for, and the team colour their marker uses.
    // Kept so the remastered optics can glow exactly those bodies through cover without re-deriving the
    // detector's own visibility, keyed by view so the outline pass can find each one's mesh and group.
    internal readonly Dictionary<SoldierView, BinocularMarkerColor> BinocularDetected = [];

    // LOS is a simulation fact, not a render fact. Cache it for the fixed tick so a second render
    // of the same simulation state does not cast the same three rays again.
    internal readonly Dictionary<SoldierView, bool> BinocularVisibility = [];

    internal int BinocularVisibilityTick = int.MinValue;

    // One prepared rig per skeleton, shared by every corpse wearing it. Building it walks the whole bone
    // graph twice, so it must not happen per death -- and it holds no live state, so it need not.
    internal readonly Dictionary<BoneAnimationSet, RagdollSkeleton> RagdollRigs = [];

    internal RagdollWorld? RagdollWorld;

    internal IGroundHeightField? Terrain;

    internal WorldRayCast? Cast;

    internal WorldRayCast? Sight;

    internal SoldierRound? OnRound;

    private string _lastCombatReport = string.Empty;

    // The alarm chain: the level-wide activation claims (dword_5BDA28), the tasks that have actually
    // been pressed, and one AlarmControl state (0x4408A0's update) per control id the soldiers name.
    // The control turns on when any soldier's named trigger has been pressed -- a recorded stand-in
    // for the control's two authored condition expressions, which the port does not evaluate yet.
    private readonly AiActivationClaims _activationClaims = new();

    private readonly HashSet<int> _activatedTasks = [];

    internal AiAlarmControlRegistry? AlarmControls;

    internal AiCombatTarget PlayerTarget = new("HumanPlayer");

    private int _playerTeam;

    // One combat-target record per opposing soldier, created lazily as each is first engaged. The
    // friendly-vs-guard gate fight needs a real enemy record to aim at and fight, not just the player:
    // per-brain enemy selection swaps the default target onto one of these when an opposing soldier is
    // nearer than any hostile player, so Harrison's team at the gate trades fire with the guards.
    private readonly Dictionary<SoldierView, AiCombatTarget> _npcTargets = [];

    // Remembers which opposing NPC each brain was fighting. A dead NPC disappears from the next
    // nearest-target sweep; keeping this identity for one handoff lets the renderer tell AiSoldier
    // that its current target died instead of accidentally reporting the still-alive player.
    internal WorldInteractionRegistry? Interactions;

    private Action<int>? _aiTaskActivator;

    internal Action<WeaponType, Vector3d, Matrix3f, Vector3d>? WeaponDrop;

    private CollisionTriangleGather? _dropTriangles;

    private WorldRayCast? _dropTrace;

    internal Action<string, Vector3d, bool>? ScriptSound;

    internal Action<int, bool, WeaponType?, Vector3d>? WeaponSound;

    internal GunPickupRegistry? DroppedWeaponPickups;

    private GameTask? _levelRoot;

    // The level's doors: ticked here (their update runs on the task pass, before the AIs), queried by
    // each walker's door hop, and switched out of the collision index while they stand open.
    internal DoorRegistry? Doors;

    // Where the player is, read by each soldier as it updates (BeginTick).
    internal Func<SoldierEnemy>? Enemy;

    // The bodies that have updated this tick, so none updates twice and EndTick can find any the walk missed.
    internal readonly HashSet<SoldierView> Ticked = new(ReferenceEqualityComparer.Instance);

    internal bool CollectDiagnostics;

    private bool _alarmWasOn;

    private ElevatorRegistry? _elevators;

    private ObjectCollisionIndex? _solids;

    /// <summary>Gets whether any authored alarm control is sounding on the current simulation tick.</summary>
    /// <remarks>Used by placed security cameras, whose alarm loop follows the mission-wide alarm state.</remarks>
    public bool AnyAlarmOn => AlarmControls?.AnyOn ?? false;

    /// <summary>Gives the renderer the level's doors and the index their collision is switched in.</summary>
    /// <param name="doors">The door registry, or <see langword="null"/> for a level with none.</param>
    /// <param name="solids">The placed-object collision index.</param>
    public void SetDoors(DoorRegistry? doors, ObjectCollisionIndex? solids)
    {
        Doors = doors;
        _solids = solids;

        // Human_GroundCollision runs during retail task registration, before the first CutScene tick.
        // That is when Mission 1 actor 1666 acquires train car 1888 (and actor 1505 the helicopter),
        // preserving the authored parent-local origin through every later shot. Waiting for the visible
        // landing produces the right vehicle but a permanently shifted local offset inside the wagon.
        // Moving vehicle placements receive their derived transforms immediately before the first
        // Soldier tick. Ground registration must wait for that publication; probing the build-time
        // placement here can mistake a nearby dormant vehicle for the actor's support.
    }

    /// <summary>Gives the renderer the level's moving elevators.</summary>
    /// <param name="elevators">The elevator registry, or <see langword="null"/>.</param>
    public void SetElevators(ElevatorRegistry? elevators) => _elevators = elevators;

    /// <summary>Publishes a player-used task to alarm and AI activation conditions.</summary>
    public void ActivateTask(int taskId) => _activatedTasks.Add(taskId);

    /// <summary>Connects an AI's input-channel activation to the live world interaction task.</summary>
    public void SetAiTaskActivator(Action<int>? activator) => _aiTaskActivator = activator;

    /// <summary>Connects gunner AIs to the level's live StationaryGun tasks.</summary>
    public void UseInteractions(WorldInteractionRegistry? interactions) => Interactions = interactions;

    /// <summary>Sounds a soldier's death cry, at the position the body fell from.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make deaths silent.</param>
    /// <remarks>
    /// A callback rather than a <c>SoundSystem</c> reference, for the reason every other seam on this
    /// class is one: the renderer knows when a soldier dies and should not also know how a sound is
    /// played. The argument is the body's position in <b>engine units</b>.
    /// </remarks>
    public void SetDeathSound(Action<Vector3d>? report) => DeathSound = report;

    /// <summary>Publishes a gun when a dead soldier's weapon becomes a world pickup.</summary>
    public void SetWeaponDropSink(Action<WeaponType, Vector3d, Matrix3f, Vector3d>? report) => WeaponDrop = report;

    /// <summary>Gives dropped weapons the world they collide with.</summary>
    /// <param name="triangles">The solid triangles near a box, terrain included, for the realistic body.</param>
    /// <param name="trace">A cast against everything solid, terrain included, for the original's drop probe.</param>
    public void SetDroppedWeaponWorld(CollisionTriangleGather? triangles, WorldRayCast? trace)
    {
        _dropTriangles = triangles;
        _dropTrace = trace;
        RagdollWorld = null;
    }

    /// <summary>Gives dropped weapons a collision world after they leave a dead soldier.</summary>
    public void UseDroppedWeaponPickups(GunPickupRegistry? pickups) => DroppedWeaponPickups = pickups;

    /// <summary>Sounds a soldier's footstep, already resolved against the ground it landed on.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make soldiers walk silently.</param>
    public void SetFootstepSound(Action<Footstep>? report) => _footstepSound = report;

    /// <summary>Gives the renderer the material table a footstep picks its sound from.</summary>
    /// <param name="materials">The level's materials, or <see langword="null"/> for none.</param>
    public void UseMaterials(GameMaterialRegistry? materials) => _materials = materials;

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>Guards walk on the same ground the player does; see the player camera's copy.</remarks>
    public int? TerrainGameMaterialIndex { get; set; }

    /// <summary>Sounds a soldier's grunt at being hit, rate-limited the way the original is.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make hits silent.</param>
    /// <remarks>The argument is the body's position in engine units.</remarks>
    public void SetHitSound(Action<string, int, Vector3d>? report) => _hitSound = report;

    /// <summary>Reports a close-combat strike against the player, already probability-gated.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to drop strikes.</param>
    /// <remarks>
    /// Arguments are the attacker's position in engine units and the row's damage factor. The
    /// mission owns the player's health, so it applies the hit; the renderer only reports it.
    /// Strikes against non-player targets are consumed and discarded at the tick site: NPC-on-NPC
    /// melee has no victim pipeline yet (issue #235 follow-up).
    /// </remarks>
    public void SetMeleeStrikeSink(Action<Vector3d, float>? report) => MeleeStrikeSink = report;

    /// <summary>Supplies the authored animation triggers from <c>animtrigger/animtrigger.qvm</c>.</summary>
    public void SetAnimationTriggers(AnimTriggerRegistry? triggers) => AnimationTriggers = triggers;

    /// <summary>Plays a definition requested by <c>AIAction_PlaySound</c>.</summary>
    public void SetScriptSound(Action<string, Vector3d, bool>? report) => ScriptSound = report;

    /// <summary>Reports every AI weapon tick, including quiet ticks needed to end automatic loops.</summary>
    public void SetWeaponSound(Action<int, bool, WeaponType?, Vector3d>? report) => WeaponSound = report;

    /// <summary>Sets how long a struck soldier stays quiet for, in ticks.</summary>
    /// <param name="cooldown">Draws the gap; see <c>HumanVariantSound.HitCooldownTicks</c>.</param>
    public void SetHitCooldown(Func<int>? cooldown) => _hitCooldown = cooldown;

    /// <summary>Sets where the player's delta-translation scale is read from — <c>humanplayer.qvm</c> argument 0.</summary>
    /// <param name="scale">Reads the scale; <see langword="null"/> uses the retail default.</param>
    /// <remarks>Ekk moves by it too: see <see cref="HumanSoldierClass.ScalesEkkRootMotion"/>.</remarks>
    public void SetPlayerDeltaTranslationScale(Func<float>? scale) => PlayerDeltaTranslationScale = scale;

    /// <summary>Plays a struck soldier's grunt unless it is still inside its cooldown.</summary>
    private void ReportHitSound(
        SoldierView view,
        string prefix = HumanVariantSound.AiHitPrefix,
        int variants = HumanVariantSound.AiHitVariants)
    {
        if (_hitSound is null || _hitCooldown is null)
        {
            return;
        }

        // now >= human[529], then human[529] = now + 30..60. Held per soldier, as the original holds
        // it per human, so one guard's grunt does not silence another's.
        if (_hitQuietUntil.TryGetValue(view, out int quietUntil) && TickCount < quietUntil)
        {
            return;
        }

        _hitQuietUntil[view] = TickCount + _hitCooldown();
        _hitSound(prefix, variants, view.Position);
    }

    /// <summary>Resolves what a soldier's step landed on and hands the footstep out.</summary>
    /// <remarks>
    /// The material comes from its own short downward cast rather than from the ground query that moved
    /// the body — the original probes separately, from 0.05 m above the ground point to 0.5 m below it,
    /// which is short enough not to punch through a floor into whatever is beneath it.
    /// </remarks>
    internal void ReportFootstep(SoldierView view)
    {
        if (_footstepSound is null || _materials is null || Cast is null)
        {
            return;
        }

        // The probe answers only for OBJECTS -- the original asks the hit task for its material, and
        // open terrain is no task in this port. A step that finds nothing solid landed on the ground,
        // so it takes the Ground row; see FootstepSounds.TerrainMaterialName.
        (Vector3d probeFrom, Vector3d probeTo) = FootstepSounds.Probe(view.Position);
        Vector3d where = view.Position;
        GameMaterial? material;

        // As the player: a face that names no walk sound is not the floor, so keep looking down.
        if (Cast(probeFrom, probeTo, out WorldRayHit hit)
            && _materials.TryGet(hit.Material, out GameMaterial struck)
            && FootstepSounds.NamesAWalk(struck))
        {
            material = struck;
            where = hit.Position;
        }
        else if (TerrainGameMaterialIndex is int terrainRow && _materials.TryGet(terrainRow, out GameMaterial declared))
        {
            material = declared;
        }
        else if (!_materials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground))
        {
            return;
        }
        else
        {
            material = ground;
        }

        // One cursor per soldier, so two guards walking abreast do not step in unison.
        FootCursors.TryGetValue(view, out int cursor);
        string sound = FootstepSounds.Next(material, ref cursor, out int slot);
        FootCursors[view] = cursor;

        if (sound.Length > 0)
        {
            // 0x463920 is one handler for every human, so a guard walking in the rain lays down the
            // same wet layer the player does. The slot is what names it.
            _footstepSound(new Footstep(
                sound,
                where,
                FootstepSounds.HearingRadiusInUnits(material),
                slot));
        }
    }

    internal Action<Vector3d>? DeathSound;

    private Action<string, int, Vector3d>? _hitSound;

    internal Action<Vector3d, float>? MeleeStrikeSink;

    internal AnimTriggerRegistry? AnimationTriggers;

    private Func<int>? _hitCooldown;

    internal Func<float>? PlayerDeltaTranslationScale;

    private readonly Dictionary<SoldierView, int> _hitQuietUntil = [];

    private Action<Footstep>? _footstepSound;

    private GameMaterialRegistry? _materials;

    internal readonly Dictionary<SoldierView, int> FootCursors = [];

    private readonly HashSet<string> _reportedChannels = new(StringComparer.Ordinal);

    /// <summary>This frame's ground-snap results by queried position; see <see cref="SnapToGround"/>.</summary>
    private readonly Dictionary<(double X, double Y, double Z), double> _groundSnaps = [];

    internal int TickCount;

    internal int TravelSamples;

    internal double TravelTotal;

    internal double TravelMax;

    internal int LastDetectionShoutTick = -1;

    internal SoldierView? PlayerBodyView;

    // The player's third-person body's model and collision mesh, which rounds aimed at the player are scored on.
    internal SoldierBodyModel? PlayerBodyModel;

    internal bool PlayerBodyClimbing;

    internal bool PlayerBodyShown;

    internal bool PlayerBodyFollowing;

    // The torso request the followed body last acted on. -1 is "none seen": PlayerCamera's counter
    // starts at zero and only ever climbs, so this forces a re-issue the first time the follow takes
    // the body and every time it takes it back off a ladder, a keypad or a death.
    internal int PlayerBodyUpperBodySequence = -1;

    internal bool PlayerBodySuppressRootHeight;

    internal double PlayerBodyVisualLift;

    internal double PlayerBodyRootHeightAtStart;

    internal GrenadeSimulation? Grenades;

    /// <summary>
    /// Lists every resolved soldier for the developer inspector, in a form that outlives them.
    /// </summary>
    /// <param name="into">The list to fill; cleared first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A read-only projection rather than access to the soldiers themselves. The inspector runs on the
    /// far side of an interface that must not see gameplay types, and a panel holding a live
    /// <c>SoldierView</c> across a mission change would be holding a corpse the loader had forgotten
    /// about. Everything the panel needs is copied out here, once per frame it draws.
    /// </para>
    /// <para>
    /// The index into the filled list is the handle the inspector's commands use, which is why the
    /// order is the stable group-then-member walk rather than anything sorted.
    /// </para>
    /// </remarks>
    public void Snapshot(List<SoldierSnapshot> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;
                bool dead = view.IsDead || (Health.TryGetValue(view, out HumanHealth? health) && health.IsDead);

                into.Add(new SoldierSnapshot(
                    view.Soldier.Name ?? string.Empty,
                    view.Position / WorldScene.UnitsPerMetre,
                    dead,
                    view.CurrentAnimation,
                    Health.TryGetValue(view, out HumanHealth? hp) ? hp.RemainingFraction : -1f,
                    member.IsCutsceneActor));
            }
        }
    }

    /// <summary>The through-wall glow colour for a detector marker class.</summary>
    internal static Bgra32 MarkerGlow(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgba(96, 220, 120, 200),
        BinocularMarkerColor.Disabled => Bgra32.FromRgba(96, 150, 255, 200),
        _ => Bgra32.FromRgba(255, 72, 72, 210),
    };

    /// <summary>Projects living, visible soldiers through the native ten-slot binocular detector.</summary>
    /// <param name="camera">The presented eye the contacts are projected through.</param>
    /// <param name="horizontalTangent">The optics' horizontal half-angle tangent (the zoom).</param>
    /// <param name="screenWidth">The back buffer width in pixels.</param>
    /// <param name="screenHeight">The back buffer height in pixels.</param>
    /// <param name="tick">The millisecond tick driving slot lifetime and the entry pulse.</param>
    /// <param name="maxDetectionMetres">
    /// The furthest a contact may be and still be marked, in metres of forward distance. The remastered
    /// optics pass a finite range; the vanilla optics pass positive infinity, staying retail-uncapped.
    /// </param>
    /// <param name="simulationTick">
    /// The fixed simulation tick used to cache line-of-sight results. Leave it at its default when
    /// calling this method from a one-off diagnostic projection.
    /// </param>
    public IReadOnlyList<EnemyOverlayBox> BinocularTargets(
        Camera3D camera,
        float horizontalTangent,
        int screenWidth,
        int screenHeight,
        int tick,
        float maxDetectionMetres,
        int simulationTick = int.MinValue)
    {
        if (horizontalTangent <= 0f)
        {
            BinocularOverlay.Clear();
            return [];
        }

        if (simulationTick != int.MinValue && BinocularVisibilityTick != simulationTick)
        {
            BinocularVisibility.Clear();
            BinocularVisibilityTick = simulationTick;
        }

        BinocularOverlay.BeginScan();
        BinocularDetected.Clear();
        float verticalTangent = horizontalTangent * screenHeight / Math.Max(1f, screenWidth);
        Vector3d eyeUnits = new(
            camera.Position.X * WorldScene.UnitsPerMetre,
            camera.Position.Y * WorldScene.UnitsPerMetre,
            camera.Position.Z * WorldScene.UnitsPerMetre);

        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            if (view.IsDead || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            // FUN_00470F70 sizes the marker as projectionScale * Human+0xA0 / depth, where Human+0xA0
            // is the model's authored MESH+0x78 bound. For the soldier LODs that value is ~0.94 m --
            // a half-body radius -- so the rectangle only frames the body when it is centred on the
            // model's own origin, which sits that same ~0.94 m above the grounded feet. view.Position
            // is the grounded foot contact, so lift the marker centre by the bound to reach the retail
            // body origin; leaving it at the feet drew the box half into the ground, over the shins.
            if (!DetectorMarkerScaleMetres.TryGetValue(view, out float markerScaleMetres)
                || markerScaleMetres <= 0f)
            {
                continue;
            }

            Vector3d feetUnits = view.Position;
            double bodyCentreUnits = markerScaleMetres * WorldScene.UnitsPerMetre;
            Vector3d targetUnits = new(feetUnits.X, feetUnits.Y, feetUnits.Z + bodyCentreUnits);
            Vector3d delta = new(
                targetUnits.X / WorldScene.UnitsPerMetre - camera.Position.X,
                targetUnits.Y / WorldScene.UnitsPerMetre - camera.Position.Y,
                targetUnits.Z / WorldScene.UnitsPerMetre - camera.Position.Z);
            Vector3d viewPosition = camera.Orientation.Transposed().Transform(delta);
            if (viewPosition.Z <= 0f)
            {
                continue;
            }

            // The remastered detector has a maximum range: a contact past it is not marked at all.
            // Measured on the forward depth, which is the distance the marker label reports, so the cap
            // is the number the player reads. Skipped here, before the occlusion sweep, so a distant
            // soldier costs nothing.
            if (viewPosition.Z > maxDetectionMetres)
            {
                continue;
            }

            float screenX = screenWidth * 0.5f
                + (float)(viewPosition.X / (viewPosition.Z * horizontalTangent) * screenWidth * 0.5f);
            float screenY = screenHeight * 0.5f
                - (float)(viewPosition.Y / (viewPosition.Z * verticalTangent) * screenHeight * 0.5f);
            if (screenX < -8f || screenX > screenWidth + 8f || screenY < -8f || screenY > screenHeight + 8f)
            {
                continue;
            }

            // FUN_00471B60 gates a contact on FUN_004D03D0, a view-frustum cull that the in-front and
            // on-screen tests above already reproduce. The retail engine then hides fully-occluded
            // humans through its sector/portal visibility, which this reimplementation lacks. Approximate
            // that with a line-of-sight sweep: probe the feet, the body origin and the head, and keep the
            // contact when ANY of them is unobstructed. A guard whose upper body clears a low container is
            // still marked; one standing completely behind a wall is dropped, as the original drops it.
            if (Sight is { } lineOfSight)
            {
                bool anyVisible;
                if (simulationTick != int.MinValue
                    && BinocularVisibility.TryGetValue(view, out bool cachedVisibility))
                {
                    anyVisible = cachedVisibility;
                }
                else
                {
                    Vector3d headUnits = new(feetUnits.X, feetUnits.Y, feetUnits.Z + (2d * bodyCentreUnits));
                    anyVisible =
                        HasLineOfSight(lineOfSight, eyeUnits, targetUnits)
                        || HasLineOfSight(lineOfSight, eyeUnits, headUnits)
                        || HasLineOfSight(lineOfSight, eyeUnits, feetUnits);
                    if (simulationTick != int.MinValue)
                    {
                        BinocularVisibility[view] = anyVisible;
                    }
                }
                if (!anyVisible)
                {
                    continue;
                }
            }

            // FUN_00470EC0 writes the transformed depth into the value passed to FUN_00471F60;
            // the retail slot's distance is view-space Z, not Euclidean eye-to-target distance.
            float distance = (float)viewPosition.Z;

            float boxWidth = (float)(markerScaleMetres * screenWidth
                / (Math.Max(0.001, viewPosition.Z) * horizontalTangent));
            float boxHeight = (float)(markerScaleMetres * screenHeight
                / (Math.Max(0.001, viewPosition.Z) * verticalTangent));

            // FUN_00471B60 passes the target's colour flags to FUN_00471F60: extraParam1 is the
            // green flag (Team != 1, Human+0x252) and extraParam2 is the blue flag (the +0x3317
            // non-collidable byte). Live scanned soldiers are collidable, so extraParam2 stays 0;
            // team-1 guards fall through to the retail hostile-red rectangle.
            BinocularOverlay.Upsert(
                brain.RuntimeId,
                distance,
                configFlag: 1,
                screenX,
                screenY,
                eventParam: 0,
                extraParam1: view.Soldier.Team != 1 ? 1 : 0,
                extraParam2: 0,
                tick,
                boxWidth,
                boxHeight);

            // Record the contact for the through-wall glow, in the same team colour its marker uses.
            BinocularDetected[view] = view.Soldier.Team != 1
                ? BinocularMarkerColor.Team
                : BinocularMarkerColor.Hostile;
        }

        BinocularOverlay.ExpireInactive(tick);
        return BinocularOverlay.OverlayBoxes(tick);
    }

    /// <summary>How far past a see-through hit the next probe starts, so it clears that thin surface.</summary>
    private const double SightSkipEpsilonUnits = 64d;

    /// <summary>The most see-through layers a single sight probe steps through before giving up.</summary>
    private const int SightSkipLimit = 8;

    /// <summary>Whether the eye can see a point along a plain cast, any contact blocking.</summary>
    /// <remarks>
    /// The binocular overlay's rule: the retail detector culls by sector/portal visibility, which
    /// see-through surfaces do not bound. See <see cref="HasLineOfSight(WorldTraceCast, Vector3d, Vector3d, double, GameTask)"/>.
    /// </remarks>
    internal static bool HasLineOfSight(WorldRayCast cast, Vector3d from, Vector3d to)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return HasLineOfSight(
            (in WorldTraceRequest request, out WorldTraceHit hit) =>
            {
                bool found = cast(request.Origin, request.End, out WorldRayHit plain);
                hit = found
                    ? WorldTraceHit.FromRayHit(request, plain, WorldTraceHitKind.StaticObject)
                    : default;
                return found;
            },
            from,
            to,
            objectTolerance: 1.0);
    }

    /// <summary>Whether the eye can see a point, treating glass and fences as non-occluding.</summary>
    /// <param name="trace">The world trace.</param>
    /// <param name="from">The eye.</param>
    /// <param name="to">The point looked at.</param>
    /// <param name="objectTolerance">
    /// How far along the eye-to-point segment, as a fraction, an object contact has to be before it stops
    /// counting as cover. <c>1</c> makes every contact block.
    /// </param>
    /// <remarks>
    /// <para>
    /// A cast returns only its NEAREST contact, so a single probe would stop at a chain-link fence and
    /// never notice a solid wall right behind it. This steps the ray past each transparent surface
    /// (glass, wire) and keeps looking, so a guard behind a fence is seen but one behind the wall the
    /// fence stands in front of is not. See <see cref="GameMaterial.IsSightTransparent"/>.
    /// </para>
    /// <para>
    /// Terrain always blocks. An object contact blocks only nearer than the tolerance: the AI's sight ray
    /// (<c>0x489B20</c>) is visible when <c>0x4CE410</c> reports no contact <em>or</em> the contact's segment
    /// fraction is at least 0.79, and <c>0x4CE410</c> forces the fraction to zero for a terrain hit.
    /// </para>
    /// </remarks>
    /// <param name="ignoredOwner">The viewer itself, when it has a body the ray would otherwise meet -- a vehicle.</param>
    /// <returns><see langword="true"/> when the line is clear.</returns>
    public static bool HasLineOfSight(
        WorldTraceCast trace,
        Vector3d from,
        Vector3d to,
        double objectTolerance,
        GameTask? ignoredOwner = null)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Vector3d segment = new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        double segmentLength = Math.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y) + (segment.Z * segment.Z));
        Vector3d origin = from;
        double travelled = 0d;
        for (int step = 0; step <= SightSkipLimit; step++)
        {
            // Portal faces are not traced. They describe the doorway and window openings for the renderer's
            // sector visibility, not collision, so 0x4CE410's object handlers never meet them; a closed
            // door leaf is its own collision geometry and still blocks.
            if (!trace(new WorldTraceRequest(origin, to, WorldTraceMask.World, IgnoredOwner: ignoredOwner), out WorldTraceHit hit))
            {
                return true;
            }

            bool transparent = GameMaterial.IsSightTransparent(hit.Material);
            if (!transparent || step == SightSkipLimit)
            {
                // The traversal budget is a safety bound, not evidence that the remainder is clear: past
                // it, whatever the cast reports is cover.
                // A static object blocks wherever it is met. 0x489B20 does accept a returned fraction of 0.79 or
                // more, but the handlers never return one for placed geometry: 359 retail sight casts logged by
                // the probe came back either clear at 1.0 or blocked at exactly 0.0 -- including a mission-1
                // tower guard's ray that met the small barracks' roof 82% of the way along, which the old
                // "past 0.79 is clear" rule let through. Only a moving object keeps the tolerance, unmeasured.
                if (hit.Kind is WorldTraceHitKind.Terrain or WorldTraceHitKind.StaticObject || segmentLength <= 0d)
                {
                    return false;
                }

                return (travelled + hit.Distance) / segmentLength >= objectTolerance;
            }

            double remaining = segmentLength - travelled;
            double advance = hit.Distance + SightSkipEpsilonUnits;
            if (advance >= remaining)
            {
                return true;
            }

            travelled += advance;
            double fraction = travelled / segmentLength;
            origin = new(from.X + (segment.X * fraction), from.Y + (segment.Y * fraction), from.Z + (segment.Z * fraction));
        }

        return true;
    }

    /// <summary>Gets or sets the active authored cutscene transform source.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets the living soldiers the retail map computer plots as guard contacts.</summary>
    public IReadOnlyList<Vector3d> MapContacts()
    {
        List<Vector3d> contacts = [];
        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            if (ConditionalContainerGate.IsTaskActive(view.Soldier)
                && !view.IsDead
                && (!Health.TryGetValue(view, out HumanHealth? health) || health is not { IsDead: true }))
            {
                contacts.Add(brain.Position);
            }
        }

        return contacts;
    }

    /// <summary>
    /// Every live soldier as a security camera's scan is offered it — <c>0x45FE30</c>'s description, in the
    /// global human list's order.
    /// </summary>
    /// <remarks>
    /// The lens scans for team 1 (<c>0x4415C0</c>), so a team-1 soldier counts as the cameras' own side: its
    /// body is what a camera raises the alarm on at once. The mark is the one the guards' own scan tests
    /// (<c>sub_465820</c>), the same for a body as for a standing soldier.
    /// </remarks>
    public IReadOnlyList<SecurityCameraSubject> CameraSubjects()
    {
        List<SecurityCameraSubject> subjects = [];
        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d feet = new(
                view.Position.X,
                view.Position.Y,
                SnapToGround(view.Position.X, view.Position.Y, view.Position.Z));
            bool dead = view.IsDead
                || brain.Activity == AiSoldierActivity.Dead
                || (Health.TryGetValue(view, out HumanHealth? health) && health.IsDead);
            subjects.Add(new SecurityCameraSubject(
                view,
                new Vector3d(feet.X, feet.Y, feet.Z + AiSoldier.DetectionMarkAboveFeet(crouching: false)),
                CamerasSide: view.Soldier.Team == 1,
                Alive: !dead,
                view.Soldier));
        }

        return subjects;
    }

    /// <summary>Gets the live AI brains for focused renderer-path tests.</summary>
    internal IReadOnlyList<AiSoldier> BrainsForTesting => [.. Brains.Values];

    internal IReadOnlyList<KeyValuePair<SoldierView, AiSoldier>> BrainPairsForTesting => [.. Brains];

    /// <summary>Marks every soldier drawn close up, as a frame looking at all of them would.</summary>
    internal void ObserveAllForTesting()
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                member.InView = true;
            }
        }
    }

    private WorldTraceCast? _sightTraceForTesting;

    /// <summary>Whether an AI sight ray from one point to another is clear, for scenario runs.</summary>
    internal bool ClearSightForTesting(Vector3d from, Vector3d to) =>
        _sightTraceForTesting is { } trace
        && HasLineOfSight(trace, from, to, AiSoldier.SightObstructionTolerance);

    /// <summary>A soldier's accumulated damage, for scenario runs.</summary>
    internal float DamageForTesting(SoldierView soldier) =>
        Health.TryGetValue(soldier, out HumanHealth? health) ? health.Damage : 0f;

    /// <summary>What an AI sight ray from one point to another meets first, for scenario traces.</summary>
    internal string DescribeSightForTesting(Vector3d from, Vector3d to)
    {
        if (_sightTraceForTesting is not { } trace)
        {
            return "no trace";
        }

        bool clear = HasLineOfSight(trace, from, to, AiSoldier.SightObstructionTolerance);
        WorldTraceRequest request = new(from, to, WorldTraceMask.World) { TracePortals = false };
        Vector3d along = to - from;
        return trace(in request, out WorldTraceHit hit)
            ? $"clear={clear} facing={((hit.Normal.X * along.X) + (hit.Normal.Y * along.Y) + (hit.Normal.Z * along.Z) < 0 ? "front" : "BACK")} hit {hit.Kind} at {hit.Distance / 4096.0:0.00} m of {Vector3d.Distance(from, to) / 4096.0:0.00} m, fraction {hit.Fraction:0.000}, material {hit.Material} '{hit.Label}' owner {hit.Owner?.Id}, at ({hit.Position.X / 4096.0:0.0},{hit.Position.Y / 4096.0:0.0},{hit.Position.Z / 4096.0:0.0})"
            : $"clear={clear} no contact";
    }

    /// <summary>Resolves a human task's live pose for an EditCamera link or target.</summary>
    public CutsceneTargetPose? CutscenePose(int taskId)
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (member.View.Soldier.Id == taskId && ConditionalContainerGate.IsTaskActive(member.View.Soldier))
                {
                    if (Cutscenes?.TryGetAttachedDrawTransform(
                        member.View.Soldier, out Vector3d attached, out Matrix3f attachedOrientation) == true)
                    {
                        return new CutsceneTargetPose(
                            attached * WorldScene.UnitsPerMetre,
                            attachedOrientation);
                    }

                    if (member.IsCutsceneActor)
                    {
                        // Grounded on its graph, as TickPlacementOf draws it.
                        if (!member.HasCutsceneGameplayMovement
                            && Brains.TryGetValue(member.View, out AiSoldier? stagedBrain) && stagedBrain.HasGraph && stagedBrain.HasGroundQuery
                            && !stagedBrain.GraphInParentFrame && stagedBrain.StoodOnGraph)
                        {
                            return new CutsceneTargetPose(
                                member.View.Position + new Vector3d(0d, 0d, member.View.RootSample.Z),
                                member.View.Orientation);
                        }

                        if (member.HasCutsceneGameplayMovement)
                        {
                            double groundedZ = SnapToGround(
                                member.View.Position.X,
                                member.View.Position.Y,
                                member.View.Position.Z);
                            return new CutsceneTargetPose(
                                new Vector3d(
                                    member.View.Position.X,
                                    member.View.Position.Y,
                                    groundedZ + member.View.RootSample.Z),
                                member.View.Orientation);
                        }

                        if (member.HasCutsceneRootMotion)
                        {
                            return new CutsceneTargetPose(member.View.Position, member.View.Orientation);
                        }

                        // A stationary staged task retains its authored model origin. Native
                        // 0x462390 derives feet by subtracting the bone/root offset from that origin;
                        // it does not treat the authored origin itself as a floor height.
                        HumanSoldierTask staged = member.View.Soldier;
                        return new CutsceneTargetPose(
                            staged.Position,
                            Matrix3f.FromEngineEulerAngles(0f, 0f, staged.Gamma));
                    }
                    return new CutsceneTargetPose(member.View.Position, member.View.Orientation);
                }
            }
        }

        return null;
    }

    /// <summary>How high above a soldier's feet its broad-phase sphere is centred, in world units.</summary>
    internal const double PickChestUnits = 3686.0;

    /// <summary>The broad-phase cull radius, in world units — generous (a soldier plus its outstretched arms).</summary>
    internal const double PickCullRadiusUnits = 8192.0;

    /// <summary>
    /// Names the detection state of the soldier under a ray, toward a target eye — the overlay's "why won't this
    /// guard see me" readout. Picks the nearest soldier the ray's body sphere touches and asks its brain which
    /// gate (cone, line of sight) is passing.
    /// </summary>
    /// <param name="originUnits">The ray origin in engine units.</param>
    /// <param name="direction">The ray direction; need not be normalised.</param>
    /// <param name="reachUnits">How far to look, in units.</param>
    /// <param name="enemyEyeUnits">The eye the guards actually test against — the player's, in units.</param>
    /// <param name="info">The formatted detection line, empty when no soldier is under the ray.</param>
    /// <returns><see langword="true"/> when a soldier was under the ray and had a brain to ask.</returns>
    public bool TryDetectionInfo(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        Vector3d enemyEyeUnits,
        out string info)
    {
        info = string.Empty;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        AiSoldier? nearest = null;
        SoldierView? nearestView = null;
        double nearestT = double.MaxValue;

        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            double snappedZ = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d centre = new(view.Position.X, view.Position.Y, snappedZ + PickChestUnits);
            if (RaySphere(originUnits, dir, centre, PickCullRadiusUnits, reachUnits, out double t) && t < nearestT)
            {
                nearestT = t;
                nearest = brain;
                nearestView = view;
            }
        }

        if (nearest is null)
        {
            return false;
        }

        (double distU, bool inCone, bool los, bool alerted, bool visible) = nearest.ProbeDetection(enemyEyeUnits);
        string cone = inCone ? "IN" : "out";
        string clear = los ? "clear" : "BLOCKED";
        string alert = alerted ? "yes" : "no";
        string seen = visible ? "YES" : "no";
        SoldierAgent? member = nearestView is null ? null : FindAgent(nearestView);
        string task = nearestView?.Soldier.Name ?? "?";
        string weapon = member?.WeaponType?.ScriptId ?? "NONE";
        string aim = nearest.Target?.ModeOf(nearest).ToString() ?? "None";
        int aimers = nearest.Target?.Count(AiAimMode.Aiming) ?? 0;
        string patrol = nearest.Arm == AiCombatArm.Patrol ? $"/{nearest.PatrolAction}" : string.Empty;
        string distance = (distU / WorldScene.UnitsPerMetre).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        info =
            $"detect: dist={distance}m cone={cone} los={clear} alert={alert} "
            + $"seen={seen} {nearest.Activity} task={task} ai={nearest.Type}/{nearest.Arm} script={(nearest.HasScript ? 1 : 0)} "
            + $"phase={nearest.CombatPhase}{patrol} aim={aim}:{aimers} weapon={weapon} "
            + $"ammo={member?.AmmoInClip ?? 0} trig={(nearest.Combat.PullTrigger ? 1 : 0)}";
        return true;
    }

    // Nearest positive entry of a ray (unit direction) into a sphere, within reach. Distance is along the ray.
    internal static bool RaySphere(Vector3d origin, Vector3d dir, Vector3d centre, double radius, double reach, out double t)
    {
        t = 0.0;
        double ox = origin.X - centre.X;
        double oy = origin.Y - centre.Y;
        double oz = origin.Z - centre.Z;
        double b = (ox * dir.X) + (oy * dir.Y) + (oz * dir.Z);
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);
        double disc = (b * b) - c;
        if (disc < 0.0)
        {
            return false;
        }

        double root = Math.Sqrt(disc);
        double near = -b - root;
        double far = -b + root;
        t = near >= 0.0 ? near : far;
        return t >= 0.0 && t <= reach;
    }

    /// <summary>Puts a soldier's feet on whatever is under its declared position.</summary>
    /// <remarks>
    /// <para>
    /// In the original the level's declared <c>Position</c> is only where a soldier <em>starts</em>: its
    /// update runs the same movement integrator as the player's, whose ground pass (<c>0x462390</c>)
    /// snaps the body onto the surface below within the first ticks. The port does not tick soldiers
    /// yet, so a body drawn with its skeleton root at the declared height stands buried to the waist
    /// wherever the position was authored at floor level.
    /// </para>
    /// <para>
    /// This reproduces the settled state, not the mechanism: probe down from half a metre above the
    /// declared position — placed objects first, terrain as the fallback, highest surface wins — and
    /// stand the <b>skin's lowest vertex</b> on it. The original's own foot reference is
    /// <c>0x48A340</c>'s bone-0-plus-<c>Human+1300</c> offset, and the <c>+1300</c> term is still
    /// unread (<c>player.md</c> §14.9), so the mesh's own extent is the honest stand-in. A recorded
    /// divergence, retired with the soldier update.
    /// </para>
    /// </remarks>
    internal static Vector3d Stand(HumanSoldierTask soldier, SoldierModelGroup group, IGroundHeightField? terrain, WorldRayCast? cast)
    {
        Vector3d declared = soldier.Position;
        double probeTop = declared.Z + ProbeAboveInUnits;
        double groundZ = double.MinValue;
        bool foundGround = false;

        if (cast is not null
            && cast(
                new Vector3d(declared.X, declared.Y, probeTop),
                new Vector3d(declared.X, declared.Y, declared.Z - ProbeBelowInUnits),
                out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
            foundGround = true;
        }

        if (terrain is not null
            && terrain.TryGetGround(declared.X, declared.Y, probeTop, out TerrainGround ground)
            && ground.Height >= declared.Z - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = foundGround ? Math.Max(groundZ, ground.Height) : ground.Height;
            foundGround = true;
        }

        if (!foundGround)
        {
            groundZ = declared.Z;
        }

        return new Vector3d(
            declared.X / WorldScene.UnitsPerMetre,
            declared.Y / WorldScene.UnitsPerMetre,
            (groundZ / WorldScene.UnitsPerMetre) - group.Body.LowestVertexZ);
    }

    /// <summary>The collision surface under a point, in world units — placed objects first, terrain second.</summary>
    /// <param name="x">The point's X, in world units.</param>
    /// <param name="y">Its Y.</param>
    /// <param name="declaredZ">The feet the query starts from; returned unchanged when nothing is under them.</param>
    /// <returns>The highest floor within the probe, or <paramref name="declaredZ"/> if none.</returns>
    /// <remarks>
    /// The same query <see cref="Stand"/> runs, without the mesh lift — for grounding a live soldier's feet
    /// each frame. It chooses the highest support in the probe and can therefore lower a soldier whose
    /// authored spawn is slightly above the terrain, as well as lift one authored just below its floor.
    /// </remarks>
    internal double SnapToGround(double x, double y, double declaredZ)
    {
        // One collision cast and one terrain descent per distinct position per FRAME: the shadow
        // cascades and the geometry pass each resolve every soldier's feet from the same positions,
        // and this query was being paid once per pass. Cleared in BeginFrame, so a moved soldier is
        // re-queried the moment the simulation actually moved it.
        (double X, double Y, double Z) key = (x, y, declaredZ);
        if (_groundSnaps.TryGetValue(key, out double cached))
        {
            return cached;
        }

        double groundZ = double.MinValue;
        bool foundGround = false;
        double probeTop = declaredZ + ProbeAboveInUnits;

        if (Cast is not null
            && Cast(new Vector3d(x, y, probeTop), new Vector3d(x, y, declaredZ - ProbeBelowInUnits), out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
            foundGround = true;
        }

        if (Terrain is not null
            && Terrain.TryGetGround(x, y, probeTop, out TerrainGround ground)
            && ground.Height >= declaredZ - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = foundGround ? Math.Max(groundZ, ground.Height) : ground.Height;
            foundGround = true;
        }

        // Cached whether or not anything was found, because "nothing is under these feet" costs the
        // same two queries to establish as a floor does and is just as true for the rest of the frame.
        double snapped = foundGround ? groundZ : declaredZ;
        _groundSnaps[key] = snapped;
        return snapped;
    }

    /// <summary>Gives every soldier an AI that patrols the level's nav graph and reacts to the player.</summary>
    /// <param name="graphs">
    /// The level's nav graphs, keyed by the id of the <c>AIGraph</c> task that loads each. A soldier is
    /// bound to the one its own <c>HumanAI</c>'s <c>Graph ID</c> names; one that names a graph the level
    /// does not ship gets no brain rather than being routed over somebody else's node ids.
    /// </param>
    /// <param name="sight">The world trace used for line of sight, or <see langword="null"/> to see through walls.</param>
    /// <param name="scripts">Where the archetype behaviour script is read from, or <see langword="null"/> for none.</param>
    /// <param name="natives">The registry the behaviour script resolves its AI natives against.</param>
    /// <param name="levelRoot">
    /// The level's task tree, which the authored patrol paths are collected from. Without it a soldier has
    /// no path to walk and stands where the level put it.
    /// </param>
    /// <param name="config">
    /// The combat table out of <c>common/ai/settings.qsc</c>, or <see langword="null"/> to leave every
    /// soldier on the constructor's defaults.
    /// </param>
    /// <param name="difficulty">Which of the table's three columns to read.</param>
    /// <param name="variables">The live level variables used by AlarmControl expressions.</param>
    /// <returns>How many soldiers got a brain.</returns>
    /// <remarks>
    /// The phase is the soldier's index modulo four, which is what staggers the level's detection sweeps
    /// across the four ticks the original spreads them over (<c>ai.md</c> §5.1).
    /// </remarks>
    public int Animate(
        IReadOnlyDictionary<int, GraphFile> graphs,
        WorldTraceCast? sight = null,
        ResourceRegistry? scripts = null,
        NativeRegistry? natives = null,
        GameTask? levelRoot = null,
        HumanAiConfigRegistry? config = null,
        AiDifficulty difficulty = AiDifficulty.Normal,
        ScriptVarRegistry? variables = null)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Sight = sight is null ? null : WorldTrace.AsRayCast(sight, WorldTraceMask.World);
        _sightTraceForTesting = sight;
        Brains.Clear();
        BrainsByRuntimeId.Clear();
        GuardGenerators.Clear();
        GuardExpressions = variables is null ? null : new LevelExpressionRuntime(variables);
        ScriptVariables = variables;
        LastDetectionShoutTick = -1;
        Combat.Clear();
        Health.Clear();
        _activatedTasks.Clear();
        PlayerTarget = new AiCombatTarget("HumanPlayer");
        _levelRoot = levelRoot;
        // The player's team decides which brains may fire on it. A same-team human is never an enemy, so
        // a friendly patrol (team 0, the same as the player) is told not to engage. Resolved once per level
        // from the HumanPlayer task the mission declares -- the same source the spawn uses -- and handed to
        // each brain as its PlayerIsEnemy gate below.
        _playerTeam = levelRoot is not null && PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn)
            ? spawn.Team
            : 0;
        PlayerTask = levelRoot is null ? null : PlayerSpawn.FindGameplayTask(levelRoot);
        Humans.Clear();
        if (levelRoot is not null)
        {
            Humans.AddRange(levelRoot.DescendantsAndSelf().OfType<IHumanAiEventTarget>());
        }
        AlarmControls = levelRoot is not null && variables is not null
            ? AiAlarmControlRegistry.Build(levelRoot, variables)
            : null;
        if (graphs.Count == 0)
        {
            // No graphs means no patrolling -- but a brain does not need a node to see or to shoot, so
            // the soldiers are still armed and still fight from where they stand.
            _logger.Warning(LogCategory, "The level ships no nav graph, so no soldier can patrol.");
        }

        // The level's authored patrol paths, keyed by the PatrolPath task's own id -- what AIAction_Patrol's
        // first argument names (ai.md 7.1).
        AiPatrolPathRegistry? paths = levelRoot is null ? null : AiPatrolPathRegistry.Build(levelRoot);
        if (paths is not null)
        {
            _logger.Information(LogCategory, $"{paths.Count} authored patrol path(s) in this level.");
        }

        AiScriptHost? host = scripts is not null && natives is not null
            ? new AiScriptHost(natives, _logger, AiRandom)
            : null;
        Dictionary<string, QvmProgram?> scriptCache = new(StringComparer.Ordinal);

        // One occupancy registry per level -- the global claim list at 0x57D768. Every brain shares it,
        // which is what stops a squad's free-position searches all electing the same node.
        AiNodeOccupancy occupancy = new();

        int index = 0;
        int patrolling = 0;
        int perTask = 0;
        int unroutable = 0;
        Dictionary<int, int> graphUse = [];
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;

                // Each soldier walks the graph ITS OWN HumanAI names, not the level's biggest one. A
                // soldier whose graph the level does not ship still gets a BRAIN -- nothing in detection
                // or the stand-and-fight arm needs a node, so leaving it brainless made an inert prop
                // that neither saw nor fought where retail's would still shoot back. It just cannot
                // patrol or reposition, which is what the graph is actually for.
                HumanAiTask? brainTask = FindMind(view);
                int graphId = brainTask?.GraphId ?? 0;
                if (!graphs.TryGetValue(graphId, out GraphFile? graph) || graph.RouteTable is null)
                {
                    graph = null;
                    if (brainTask is not null)
                    {
                        unroutable++;
                    }
                }

                Vector3d initialPosition = view.Position;
                if (!CinematicActor.IsStaged(view.Soldier))
                {
                    double groundedZ = SnapToGround(initialPosition.X, initialPosition.Y, initialPosition.Z);
                    if (groundedZ != initialPosition.Z)
                    {
                        _logger.Information(
                            LogCategory + ".Ground",
                            $"soldier={view.Soldier.Id} declaredZ={initialPosition.Z:0.0} " +
                            $"groundedZ={groundedZ:0.0} delta={(groundedZ - initialPosition.Z) / WorldScene.UnitsPerMetre:0.000}m");
                        initialPosition = new Vector3d(initialPosition.X, initialPosition.Y, groundedZ);
                        view.PlaceAt(initialPosition, view.Orientation);
                    }
                }

                AiSoldier brain = new(graph, initialPosition, GammaOf(view), index & 3, index, AiRandom, view.Soldier.Class);
                // A soldier on the player's own team must not engage the player. The brain's noise and
                // vision gates read this: detection and every player-origin gunshot/impact/walk event are
                // refused for a same-team player, so the friendly patrol stands down instead of shooting us.
                brain.PlayerIsEnemy = view.Soldier.Team != _playerTeam;
                BindViewTracking(brain, view);
                brain.RuntimeId = index + 1;
                brain.SetResponseRouter(source =>
                {
                    if (BrainsByRuntimeId.TryGetValue(source, out AiSoldier? recipient))
                    {
                        recipient.ReceiveAlertResponse();
                    }
                });
                brain.SetDefaultTarget(PlayerTarget);
                brain.SetAlarmStatusResolver(AlarmControls is { } controls ? controls.IsOn : null);
                brain.SetStationaryGunResolver(
                    Interactions is { } interactions ? interactions.FindStationaryGun : null);
                brain.SetPatrolPaths(paths);
                brain.SetNodeOccupancy(occupancy);
                brain.SetGroundQuery(MakeGroundQuery());
                if (Doors is { } levelDoors)
                {
                    brain.SetDoorQuery(levelDoors.Nearest);
                }
                if (levelRoot is not null)
                {
                    GameTask taskRoot = levelRoot;
                    brain.SetActivateSupport(
                        _activationClaims,
                        (id, out position) =>
                        {
                            return TryGetTaskPosition(taskRoot, id, out position);
                        },
                        id =>
                        {
                            _activatedTasks.Add(id);
                            if (brain.Arm == AiCombatArm.Gunner
                                || Interactions?.FindStationaryGun(id) is not null)
                            {
                                _logger.Information(
                                    LogCategory + ".Gunner",
                                    $"runtime={brain.RuntimeId} activation reached task={id} "
                                        + $"gunnerId={brain.GunnerId}");
                            }
                            _aiTaskActivator?.Invoke(id);
                        });
                }
                if (graph is not null)
                {
                    graphUse[graphId] = graphUse.GetValueOrDefault(graphId) + 1;
                }
                if (sight is not null)
                {
                    // HumanAI and the binocular overlay share the same transparent-surface rule. A raw
                    // nearest-hit query stops at Level 4's material-14 window and made the village gunner
                    // blind to a player immediately outside, even though the remainder of the ray was clear.
                    // Step past retail Glass/Water/Fence/Ladder faces, while an ordinary wall or physical
                    // closed-door leaf remains cover.
                    brain.SetLineOfSight((from, to) => HasLineOfSight(sight, from, to, AiSoldier.SightObstructionTolerance));
                }

                // Which script this soldier runs: its own MISSION:AI/<taskid>.qvm when the level ships one,
                // otherwise the archetype its AIType selects (ai.md 7.5a and 2.3). The per-task script is
                // what names the soldier's patrol path, so without it a guard has nothing to patrol.
                // When a per-task script exists, the archetype is passed separately so DefaultHandler can
                // delegate to it — the mechanism the original uses at 0x44E060.
                HumanAiTask? mind = brainTask;
                if (host is not null && scripts is not null)
                {
                    QvmProgram? perTaskScript = null;
                    if (mind is not null && mind.Id != GameTask.NoId)
                    {
                        perTaskScript = LoadScript(scripts, scriptCache, AiScriptApi.MissionScriptPath(mind.Id));
                        if (perTaskScript is not null)
                        {
                            perTask++;
                        }
                    }

                    AiArchetype archetype = mind?.Archetype ?? AiArchetype.Guard;
                    QvmProgram? archetypeScript = LoadScript(scripts, scriptCache, AiScriptApi.ScriptPath(archetype));

                    QvmProgram? primary = perTaskScript ?? archetypeScript;
                    QvmProgram? fallback = perTaskScript is not null ? archetypeScript : null;

                    if (primary is not null)
                    {
                        brain.SetScript(host, primary, fallback);
                        patrolling++;
                    }
                }

                Brains[view] = brain;
                BrainsByRuntimeId[brain.RuntimeId] = brain;
                TakeEvents(view);
                index++;
            }
        }

        // AFTER the brains exist: BindCombatRows installs the behaviour on each, so running it before the
        // loop would have found no brains to install it on and every guard would have gone on standing
        // there looking at the player.
        BindCombatRows(config, difficulty);
        PrepareGuardGenerators(levelRoot);

        int hasRun = 0, total = 0;
        foreach (SoldierView v in Roster.SelectMany(g => g.Agents).Select(a => a.View))
        {
            total++;
            if (v.Bones.Set.TryGet(v.Soldier.Class.RunAnimation, out _))
            {
                hasRun++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{Brains.Count} soldier(s) with brains ({patrolling} scripted, {perTask} from their own " +
            $"MISSION:AI script); {hasRun} of {total} soldier set(s) hold their class's run clip, the rest " +
            "fall back to the walk when chasing.");
        _logger.Information(
            LogCategory,
            $"Soldiers per nav graph: " +
            string.Join(", ", graphUse.OrderBy(p => p.Key).Select(p => $"graph{p.Key}:{p.Value}")) +
            (unroutable > 0
                ? $"; {unroutable} soldier(s) name a graph the level does not ship (they fight, but cannot patrol)"
                : string.Empty));
        return Brains.Count;
    }

    /// <summary>Reports what every soldier's AI is actually doing, for diagnosing a level that looks wrong.</summary>
    /// <remarks>
    /// Written for the "why is nobody patrolling?" question, which needs to distinguish four different
    /// failures that look identical on screen: no brain, no script, a script that never asked to patrol, and
    /// a patrol that started but cannot move.
    /// </remarks>
    public void LogAiSummary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Dictionary<string, int> activities = [];
        Dictionary<int, int> paths = [];
        int withRoute = 0;
        int moving = 0;

        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            string key = brain.Activity.ToString();
            activities[key] = activities.GetValueOrDefault(key) + 1;

            if (brain.Route is { } route)
            {
                withRoute++;
                paths[route.Path.Id] = paths.GetValueOrDefault(route.Path.Id) + 1;
                if (route.Current is { } command && command.Kind.IsMovement())
                {
                    moving++;
                }
            }

            _ = view;
        }

        _logger.Information(
            LogCategory,
            $"AI: {Brains.Count} brain(s); {withRoute} on a patrol path; {moving} on a movement command.");

        foreach ((string activity, int count) in activities.OrderByDescending(p => p.Value))
        {
            _logger.Information(LogCategory, $"AI:   activity {activity}: {count}");
        }

        _logger.Information(
            LogCategory,
            $"AI: patrol paths in use: {string.Join(", ", paths.Keys.Order())}");

        if (TravelSamples > 0)
        {
            double mean = TravelTotal / TravelSamples;
            _logger.Information(
                LogCategory,
                $"AI: walk step over {TravelSamples} sample(s): mean {mean:F1} u/tick " +
                $"({mean * 30.0 / WorldScene.UnitsPerMetre:F2} m/s), max {TravelMax:F1} u/tick. " +
                $"Retail measures 160-185 u/tick (about 1.28 m/s).");
        }
    }

    /// <summary>Logs how the level's fight is going, once per change.</summary>
    /// <remarks>
    /// One line, and only when the numbers move, so it is a running commentary rather than a per-tick
    /// flood. It is what tells a "the guards do nothing" report apart into its three possible causes:
    /// nobody sees the player, somebody sees him and never engages, or somebody engages and never
    /// fires.
    /// </remarks>
    internal void ReportCombat()
    {
        int seeing = 0;
        int engaging = 0;
        int chasing = 0;
        foreach (AiSoldier brain in Brains.Values)
        {
            if (brain.SeesEnemy)
            {
                seeing++;
            }

            if (brain.Activity == AiSoldierActivity.Engaging)
            {
                engaging++;
            }

            if (brain.IsMovingInCombat)
            {
                chasing++;
            }
        }

        if (seeing == 0 && engaging == 0 && RoundsFired == 0)
        {
            return;
        }

        // The port-side [aiflow] line, for diffing against the retail probe's capture: every two
        // seconds, one decision-state line per engaged soldier.
        if (engaging > 0 && TickCount - _lastCombatDetailTick >= 60)
        {
            _lastCombatDetailTick = TickCount;
            foreach (AiSoldier brain in Brains.Values)
            {
                if (brain.Activity == AiSoldierActivity.Engaging)
                {
                    _logger.Information(LogCategory + ".Flow", $"#{brain.RuntimeId} {brain.DescribeCombat()}");
                }
            }
        }

        string report =
            $"{seeing} soldier(s) see the player, {engaging} engaging, {chasing} chasing, {RoundsFired} round(s) fired.";
        if (report == _lastCombatReport)
        {
            return;
        }

        _lastCombatReport = report;
        _logger.Information(LogCategory, $"AI: {report}");
    }

    private int _lastCombatDetailTick;

    /// <summary>Logs each input channel a fight asks for, once per type and channel.</summary>
    /// <remarks>
    /// Deliberately the same shape as the probe's <c>[combat] channel</c> line from the retail game (logged
    /// here under the <c>Soldier.Combat</c> category rather than a tag), so
    /// a run of the port and a capture of the original can be put side by side. The slot is resolved
    /// through the port's transcription of <c>off_54151C</c>; if it disagrees with the probe's, the
    /// transcription is wrong.
    /// </remarks>
    internal void ReportChannels(AiSoldier brain)
    {
        foreach (AiChannelWrite write in brain.ChannelWrites)
        {
            string line = $"channel type={(int)brain.Type} channel={(int)write.Channel} "
                + $"-> slot {write.Slot} value={write.Value:0.00}";

            if (_reportedChannels.Add(line))
            {
                _logger.Information(LogCategory + ".Combat", line);
            }
        }
    }

    internal static AiCombatWeaponState CombatWeaponState(AiSoldier brain, SoldierAgent held)
    {
        if (brain.MountedGun is { Weapon: { } mounted } gun)
        {
            return new AiCombatWeaponState(
                gun.AmmunitionSample,
                mounted.RoundsPerClip,
                mounted.MaximumRoundsPerBurst,
                mounted.Category);
        }

        return new AiCombatWeaponState(
            held.AmmoInClip,
            held.WeaponType?.RoundsPerClip ?? 0,
            held.WeaponType?.MaximumRoundsPerBurst ?? 0,
            held.WeaponType?.Category ?? WeaponCategory.Knife);
    }

    /// <summary>Chooses the simulation weapon that a dead soldier drops, independent of its drawn model.</summary>
    internal static WeaponType? WeaponForDrop(WeaponType? carriedWeaponType, WeaponType? _renderedWeaponType) =>
        carriedWeaponType;

    /// <summary>
    /// Where a guard's round really goes: along the gun, with the weapon's cone about it — <c>0x478BA0</c>,
    /// the same Gun_Fire the player's rounds come out of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retail's round leaves along the firer's own orientation (the matrix its fire message carries, or the
    /// gun's), not along a line to the target, and is then turned by rand³ × the weapon's spread about a
    /// random bearing -- the minimum spread for the first four rounds of a pull, the maximum after. A guard
    /// only aims by turning its body and view onto the point (to within 2 degrees before it opens fire), so
    /// those two errors are all the accuracy it has.
    /// </para>
    /// <para>
    /// Measured on the retail game with the probe (mission 1, Hard, a stationary guard at 15-22 m): 242
    /// rounds passed 0-3.5 m from the player and 7 hit -- about 3%. Casting every round exactly at the aim
    /// point and leaving only the probability roll made 107 of 181 hit.
    /// </para>
    /// </remarks>
    internal Vector3d SpreadRound(
        AiSoldier brain,
        AiStationaryGun? mounted,
        WeaponType weapon,
        SoldierAgent? member,
        Vector3d origin,
        Vector3d target)
    {
        double reach = Math.Max(Vector3d.Distance(origin, target), WorldScene.UnitsPerMetre);

        // The emplacement's barrel is aimed by its own task; a carried gun points where the soldier looks.
        Vector3f forward;
        Vector3f right;
        Vector3f up;
        if (mounted is not null)
        {
            Vector3d line = target - origin;
            forward = new Vector3f((float)(line.X / reach), (float)(line.Y / reach), (float)(line.Z / reach)).Normalized();
            right = Vector3f.Cross(forward, new Vector3f(0f, 0f, 1f)).Normalized();
            up = Vector3f.Cross(right, forward).Normalized();
        }
        else
        {
            Matrix3f view = brain.ViewOrientation.Transposed();
            forward = ToFloat(view.Transform(new Vector3d(0.0, 1.0, 0.0)));
            right = ToFloat(view.Transform(new Vector3d(1.0, 0.0, 0.0)));
            up = ToFloat(view.Transform(new Vector3d(0.0, 0.0, 1.0)));
        }

        int roundsThisPull = member?.RoundsThisPull ?? 0;
        if (member is not null)
        {
            member.RoundsThisPull++;
        }

        float cone = GunFiring.ConeAngle(weapon, roundsThisPull);
        Vector3f direction = forward;
        if (cone != 0f)
        {
            Span<float> samples =
            [
                AiRandom.NextSingle(),
                AiRandom.NextSingle(),
                AiRandom.NextSingle(),
                AiRandom.NextSingle(),
            ];
            (float radius, float bearing) = GunFiring.SampleCone(cone, samples);
            direction = GunFiring.OffsetInCone(forward, right, up, radius, bearing);
        }

        return new Vector3d(
            origin.X + (direction.X * reach),
            origin.Y + (direction.Y * reach),
            origin.Z + (direction.Z * reach));

        static Vector3f ToFloat(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }

    /// <summary>Where a follow-firing patrol's round goes: along its own facing, at the aim point's height.</summary>
    /// <param name="brain">The shooter.</param>
    /// <param name="enemyFeet">The target's feet, in world units.</param>
    /// <param name="enemyCrouching">Whether the target is crouching.</param>
    /// <remarks>
    /// The follow fire has no burst and no aim solve (ai.md §13.7): the trigger is pressed whenever the
    /// target is inside a 16.3° cone about the shooter's forward axis, and the round flies where the BODY
    /// points. The port's facing has no pitch, so the ray borrows the aim point's height and keeps the
    /// facing's horizontal error — which is the part that decides the spray's geometric misses.
    /// </remarks>
    internal static Vector3d SprayTarget(AiSoldier brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        Vector3d aimPoint = AiSoldier.AimPointOf(enemyFeet, enemyCrouching);
        Vector3d eye = brain.EyePosition();
        double dx = aimPoint.X - eye.X;
        double dy = aimPoint.Y - eye.Y;
        double reach = Math.Sqrt((dx * dx) + (dy * dy));
        if (reach < 1.0)
        {
            return aimPoint;
        }

        return new Vector3d(
            eye.X + (-Math.Sin(brain.Facing) * reach),
            eye.Y + (Math.Cos(brain.Facing) * reach),
            aimPoint.Z);
    }

    /// <summary>Delivers a gunshot to every soldier in earshot — the hearing side of <see cref="AiEvent.Gunshot"/>.</summary>
    /// <param name="origin">Where the shot was fired, in world units.</param>
    /// <param name="silenced">Whether the weapon is silenced (cuts the range to a thirtieth).</param>
    /// <param name="except">A soldier not to notify — the shooter, which does not hear its own shot as news.</param>
    /// <remarks>
    /// The range gate (30 m, or 1 m silenced) is applied per soldier in
    /// <see cref="AiSoldier.HearGunshot(Vector3d, bool)"/>.
    /// The event is queued and pumped on the soldier's next tick; what it does about it is its archetype
    /// script's business.
    /// </remarks>
    public void NotifyGunshot(Vector3d origin, bool silenced, SoldierView? except = null)
        => NotifyGunshot(
            origin,
            silenced ? AiScriptApi.SilencedGunshotRange : AiScriptApi.GunshotRange,
            except);

    /// <summary>Delivers a gunshot using the firing weapon's exact AI detection range.</summary>
    public void NotifyGunshot(Vector3d origin, double range, SoldierView? except = null)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            if (except is not null && Brains.TryGetValue(except, out AiSoldier? shooter) && ReferenceEquals(shooter, brain))
            {
                return false;
            }

            // The record carries the shooter's side (the player's, when nobody is excepted), and a
            // listener on that side does not admit it.
            int shooterTeam = except?.Soldier.Team ?? _playerTeam;
            int shooterId = except?.Soldier.Id ?? AiDetectionCandidate.PlayerId;
            return brain.HearGunshot(origin, range, fromSameSide: shooterTeam == view.Soldier.Team, shooterId);
        });
        Broadcast(noise, AiEvent.Gunshot, except?.Soldier.Team ?? _playerTeam, HumanNoiseFilter, range, origin, (GameTask?)except?.Soldier ?? PlayerTask);
        ReportNoise("gunshot", noise.Heard, range);
    }

    /// <summary>Delivers IGI1's type-21 explosion alert to every active soldier in the blast envelope.</summary>
    /// <param name="origin">The explosion centre in engine units.</param>
    /// <param name="rangeUnits">The authored full-radius plus falloff envelope.</param>
    /// <remarks>
    /// This is intentionally separate from <see cref="NotifyGunshot(Vector3d, double, SoldierView?)"/>. Retail registers and queues
    /// <c>AIEVENT_EXPLOSION</c> (21), so an explosive object must not masquerade as a rifle shot.
    /// </remarks>
    public void NotifyExplosion(
        Vector3d origin,
        double rangeUnits = AiSoldier.FlashbangEventRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveExplosion(origin, rangeUnits);
        });
        Broadcast(noise, AiEvent.Explosion, -1, AiEventFilter.InRange, rangeUnits, origin, null);
        ReportNoise("explosion", noise.Heard, rangeUnits);
    }

    /// <summary>Delivers a player's world-impact event to every guard inside its authored radius.</summary>
    /// <param name="impact">The selected nearest world contact.</param>
    /// <param name="shooterPosition">The player's live position for source-based script actions.</param>
    /// <param name="radiusInUnits">Five metres for a round; five or ten metres for a landing.</param>
    public void NotifyGroundImpact(
        Vector3d impact,
        Vector3d shooterPosition,
        double radiusInUnits = AiSoldier.GroundImpactRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            return brain.ReceiveGroundImpact(impact, shooterPosition, radiusInUnits);
        });
        Broadcast(noise, AiEvent.GroundImpact, _playerTeam, HumanNoiseFilter, radiusInUnits, impact, PlayerTask);
        ReportNoise("impact", noise.Heard, radiusInUnits);
    }

    /// <summary>Delivers the player's footstep noise to every guard the step's radius reaches.</summary>
    /// <param name="step">Where the foot landed, in units.</param>
    /// <param name="walkerPosition">The player's live position.</param>
    /// <param name="radiusUnits">The ground material's hearing radius × 4096 — <c>0x463920</c>.</param>
    public void NotifyWalk(Vector3d step, Vector3d walkerPosition, double radiusUnits) =>
        NotifyHumanNoise(AiEvent.Walk, step, walkerPosition, radiusUnits);

    /// <summary>Delivers a noise the player makes to every guard it reaches — see <see cref="AiSoldier.ReceiveHumanNoise"/>.</summary>
    /// <param name="type">The event: a walk, a door, a fence climb or a ladder step.</param>
    /// <param name="position">Where the noise is.</param>
    /// <param name="source">The player's live position.</param>
    /// <param name="radiusUnits">How far it carries.</param>
    public void NotifyHumanNoise(
        AiEvent type,
        Vector3d position,
        Vector3d source,
        double radiusUnits = AiSoldier.HumanNoiseRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            return brain.ReceiveHumanNoise(type, position, source, radiusUnits);
        });
        Broadcast(noise, type, _playerTeam, HumanNoiseFilter, radiusUnits, position, PlayerTask);
        ReportNoise(type.ToString().ToLowerInvariant(), noise.Heard, radiusUnits);
    }

    /// <summary>Delivers IGI1's visible grenade-takeoff event (type 17).</summary>
    public void NotifyGrenadeThrown(Vector3d origin)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveGrenadeThrown(origin);
        });
        Broadcast(noise, AiEvent.GrenadeThrown, -1, AiEventFilter.InView, -1.0, origin, null);
        ReportNoise("grenade-thrown", noise.Heard, AiSoldier.GrenadeLandRadiusInUnits);
    }

    /// <summary>Delivers IGI1's five-metre grenade-land event (type 18).</summary>
    public void NotifyGrenadeLand(
        Vector3d origin,
        double radiusUnits = AiSoldier.GrenadeLandRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveGrenadeLand(origin, radiusUnits);
        });
        Broadcast(noise, AiEvent.GrenadeLand, -1, AiEventFilter.InRange, radiusUnits, origin, null);
        ReportNoise("grenade-land", noise.Heard, radiusUnits);
    }

    /// <summary>Delivers IGI1's ten-metre in-view flashbang event (type 19).</summary>
    public void NotifyFlashbang(
        Vector3d origin,
        double radiusUnits = AiSoldier.FlashbangEventRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveFlashbang(origin, radiusUnits);
        });
        Broadcast(noise, AiEvent.Flashbang, -1, AiEventFilter.InView | AiEventFilter.InRange, radiusUnits, origin, null);
        ReportNoise("flashbang", noise.Heard, radiusUnits);
    }

    /// <summary>
    /// Offers a soldier's round to every other soldier it passes — <c>0x4606F0</c> for rounds the player did
    /// not fire.
    /// </summary>
    /// <param name="origin">The muzzle.</param>
    /// <param name="liveEnd">Where the round stopped: the surface, the body it met, or the end of its reach.</param>
    /// <param name="shooter">The soldier that fired.</param>
    /// <param name="struck">The soldier the round struck, which takes TAKINGDAMAGE rather than a near miss.</param>
    public void NotifyRoundPassed(Vector3d origin, Vector3d liveEnd, SoldierView shooter, SoldierView? struck)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        int source = shooter.Soldier.Id;
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier)
                || ReferenceEquals(view, shooter)
                || ReferenceEquals(view, struck))
            {
                return false;
            }

            bool otherSide = view.Soldier.Team != shooter.Soldier.Team;
            return brain.ReceiveGunshotMiss(origin, liveEnd, shooter.Position, otherSide, source);
        });
        Broadcast(noise);
        ReportNoise("near-miss", noise.Heard, AiSoldier.GunshotMissRadiusInUnits);
    }

    /// <summary>
    /// Delivers the original's separate near-miss event to every guard whose live eye lies within two
    /// metres of this projectile segment.
    /// </summary>
    internal void NotifyGunshotMiss(
        Vector3d origin,
        Vector3d end,
        SoldierView? struck,
        Vector3d shooterPosition)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            // A body the round actually damaged receives TAKINGDAMAGE, not the avoidance event for a miss.
            if (struck is not null && ReferenceEquals(struck, view))
            {
                return false;
            }

            return brain.ReceiveGunshotMiss(origin, end, shooterPosition);
        });
        Broadcast(noise);
        ReportNoise("near-miss", noise.Heard, AiSoldier.GunshotMissRadiusInUnits);
    }

    /// <summary>One rate-limited delivery line per noise kind — the receipt half of the [aiflow] diff.</summary>
    /// <summary>Gets or sets the level's humans in the order they joined the human list, every game's, which AI events are sent to.</summary>
    /// <remarks>Made when the soldiers are animated, from the level's tree; a guard spawned since joins at the end.</remarks>
    internal List<IHumanAiEventTarget> Humans { get; } = [];

    /// <summary>Gets or sets the humans another game made for the level, which hear its AI events after its own.</summary>
    public LevelJoiners? Joined { get; set; }

    // A human's noise as its senders post it (0x463920, 0x479780): in range, of a living human, from the other side.
    private const AiEventFilter HumanNoiseFilter = AiEventFilter.InRange | AiEventFilter.SubjectIsHuman | AiEventFilter.OtherSide;

    // The engine's broadcast (0x450CE0): the event to every human on the list, each taking it in its own handler. A near
    // miss is weighed listener by listener, so it carries no record of its own: another game's humans hear a round go by
    // through the level's bodies instead.
    private void Broadcast(Igi1Noise noise) =>
        Broadcast(noise, new HumanAiEvent(Type: -1, Team: -1, Flags: 0, Radius: 0f, Position: null, Source: null, Subject: null, Context: noise));

    // The broadcast of a record the way its sender posts it, which another game's humans read; this game's read the noise.
    private void Broadcast(Igi1Noise noise, AiEvent type, int team, AiEventFilter filter, double radius, Vector3d position, GameTask? source) =>
        Broadcast(noise, new HumanAiEvent((int)type, team, (int)filter, (float)radius, position, source, source, noise));

    private void Broadcast(Igi1Noise noise, in HumanAiEvent aiEvent) =>
        HumanAiEvents.Send(
            (Humans.Count > 0 ? Humans : Brains.Keys.Select(static view => (IHumanAiEventTarget)view.Soldier))
                .Concat(Joined?.Humans ?? []),
            aiEvent);

    /// <inheritdoc/>
    void IHumanEvents.SendEvent(HumanTask human, in HumanAiEvent aiEvent)
    {
        if (aiEvent.Context is Igi1Noise noise
            && _viewsByTask.TryGetValue(human, out SoldierView? view)
            && Brains.TryGetValue(view, out AiSoldier? brain)
            && noise.Hear(view, brain))
        {
            noise.Heard++;
        }
    }

    // Project I.G.I.'s own context for an AI event: the listening brain's test for it, and how many heard.
    private sealed class Igi1Noise(Func<SoldierView, AiSoldier, bool> hear)
    {
        public int Heard { get; set; }

        public bool Hear(SoldierView view, AiSoldier brain) => hear(view, brain);
    }

    private void ReportNoise(string kind, int heard, double radiusUnits)
    {
        if (heard == 0 || TickCount - _lastNoiseLogTick < 30)
        {
            return;
        }

        _lastNoiseLogTick = TickCount;
        _logger.Information(
            LogCategory + ".Flow",
            FormattableString.Invariant(
                $"noise {kind} heard by {heard} soldier(s) (radius {radiusUnits / 4096.0:0.0} m)"));
    }

    private int _lastNoiseLogTick = -100;

    /// <summary>The skinned model a soldier wears, so its ragdoll can be sized from its own geometry.</summary>
    internal MefModel? FindModel(SoldierView view) => FindRoster(view)?.Body.Model;

    /// <summary>
    /// The humans the detection scan offers one soldier this tick — <c>0x489750</c> over each human's own
    /// description (<c>0x45FE30</c>).
    /// </summary>
    /// <remarks>
    /// The global human list's order: the player first, then every soldier whose container is live, the
    /// soldier itself left out. Each is described from this soldier's side, so the scan can register the
    /// living of the other side as enemies and the dead of its own as fallen friends.
    /// </remarks>
    internal List<AiDetectionCandidate> CandidatesFor(
        SoldierView self,
        AiSoldier brain,
        Vector3d playerFeet,
        Vector3d playerMark,
        bool playerAlive,
        bool playerCrouching)
    {
        List<AiDetectionCandidate> humans =
        [
            new(AiDetectionCandidate.PlayerId, brain.PlayerIsEnemy, playerAlive, playerMark, playerFeet,
                playerCrouching, PlayerTarget),
        ];
        foreach ((SoldierView view, AiSoldier other) in Brains)
        {
            if (ReferenceEquals(view, self) || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d soldierFeet = new(
                view.Position.X,
                view.Position.Y,
                SnapToGround(view.Position.X, view.Position.Y, view.Position.Z));
            Vector3d soldierMark = new(
                soldierFeet.X, soldierFeet.Y, soldierFeet.Z + AiSoldier.DetectionMarkAboveFeet(crouching: false));
            AiCombatTarget record = _npcTargets.GetValueOrDefault(view)
                ?? (_npcTargets[view] = new AiCombatTarget($"Soldier{view.Soldier.Id}"));
            humans.Add(new AiDetectionCandidate(
                view.Soldier.Id,
                view.Soldier.Team != self.Soldier.Team,
                !view.IsDead && other.Activity != AiSoldierActivity.Dead,
                soldierMark,
                soldierFeet,
                Crouching: false,
                record));
        }

        return humans;
    }

    private SoldierModelGroup? FindRoster(SoldierView view)
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent agent in group.Agents)
            {
                if (ReferenceEquals(agent.View, view))
                {
                    return group;
                }
            }
        }

        return null;
    }

    private SoldierAgent? FindAgent(SoldierView view)
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent agent in group.Agents)
            {
                if (ReferenceEquals(agent.View, view))
                {
                    return agent;
                }
            }
        }

        return null;
    }

    /// <summary>Gives every soldier the combat row its own <c>AIType</c> names.</summary>
    /// <remarks>
    /// A soldier with no <c>HumanAI</c>, or one naming a type the engine does not register, is left off
    /// the table: the original never configures such a body either, so its maximum health stays the 1.0
    /// its constructor set.
    /// </remarks>
    private void BindCombatRows(HumanAiConfigRegistry? config, AiDifficulty difficulty)
    {
        if (config is null)
        {
            return;
        }

        int bound = 0;
        var arms = new Dictionary<AiCombatArm, int>();

        // The resolved accuracy per AI type actually in the level, so "I die too fast even on Easy" can be
        // read straight off the log: point-blank / cone-end hit chance is the row's HitProbability pair, and
        // a guard that shows 1.000 / 0.250 got the built-in default -- settings.qsc did not supply a row for
        // this (type, difficulty) and the difficulty is doing nothing. A genuinely lower number is the data.
        var accuracyByType = new Dictionary<AiType, (int Count, AiCombatParameters Row)>();

        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (FindMind(member.View)?.ResolvedType is not { } type)
                {
                    continue;
                }

                AiCombatParameters row = config.Parameters(type, difficulty);
                Combat[member.View] = row;
                bound++;

                (int count, AiCombatParameters _) = accuracyByType.GetValueOrDefault(type);
                accuracyByType[type] = (count + 1, row);

                if (Brains.TryGetValue(member.View, out AiSoldier? brain))
                {
                    SoldierAgent held = member;
                    brain.SetCombat(type, row, () => CombatWeaponState(brain, held));
                    arms[brain.Arm] = arms.GetValueOrDefault(brain.Arm) + 1;
                }
            }
        }

        // The arm decides whether a soldier can chase at all: only the patrol (move-and-fight) arm hunts and
        // advances; the guard arm stands its ground. Logged so a "they don't chase" report can be told apart.
        string split = string.Join(", ", arms.OrderBy(a => a.Key).Select(a => $"{a.Key}:{a.Value}"));
        _logger.Information(
            LogCategory,
            $"AI: {bound} soldier(s) configured at {HumanAiConfigRegistry.NameOf(difficulty)} ({difficulty}); arms {split}.");

        // One line per AI type: the hit chance a guard of that type actually fires with, at this difficulty.
        // "hit@0m" is point-blank (where you die), "hit@far" is the cone's end; the round handler rolls a
        // uniform against the range-lerped value between them. clip% is the fraction of a magazine a burst
        // spends. 1.000/0.250 = the default, i.e. settings.qsc gave no row for this pair.
        foreach ((AiType type, (int count, AiCombatParameters row)) in accuracyByType.OrderBy(e => e.Key))
        {
            string name = HumanAiConfigRegistry.NameOf(difficulty);
            _logger.Information(
                LogCategory,
                FormattableString.Invariant(
                    $"AI accuracy [{type}] x{count} @ {name}: hit@0m={row.HitProbabilityAtMinimumRange:F3} hit@far={row.HitProbabilityAtMaximumRange:F3} clip%={row.MinimumClipFraction:F2}+{row.RandomClipFraction:F2} aimPrec={row.AimPrecision:F2}"));
        }
    }

    /// <summary>The <c>HumanAI</c> child a soldier's body carries, if the level gave it one.</summary>
    /// <summary>A level task's position by id — what the activate walk aims at.</summary>
    /// <param name="task">The subtree to search.</param>
    /// <param name="id">The task id the script named.</param>
    /// <param name="position">Receives the task position when present.</param>
    /// <returns>The position, or default when the id names nothing with one.</returns>
    private static bool TryGetTaskPosition(GameTask task, int id, out Vector3d position)
    {
        if (task.Id == id)
        {
            if (task is DeclaredTask declared
                && declared.TryGetValue("Position", out object? pos)
                && pos is Vector3d p)
            {
                position = p;
                return true;
            }

            position = default;
            return false;
        }

        foreach (GameTask child in task.Children)
        {
            if (TryGetTaskPosition(child, id, out position))
            {
                return true;
            }
        }

        position = default;
        return false;
    }

    private static HumanAiTask? FindMind(SoldierView view)
    {
        foreach (GameTask child in view.Soldier.Children)
        {
            if (child is HumanAiTask mind)
            {
                return mind;
            }
        }

        return null;
    }

    private QvmProgram? LoadScript(ResourceRegistry scripts, Dictionary<string, QvmProgram?> cache, string qscPath)
    {
        string path = qscPath.Replace(".qsc", ".qvm", StringComparison.Ordinal);
        if (cache.TryGetValue(path, out QvmProgram? cached))
        {
            return cached;
        }

        QvmProgram? program = null;
        try
        {
            if (scripts.TryLoad(path, out ReadOnlyMemory<byte> bytes))
            {
                program = QvmProgram.Read(bytes);
            }
            else
            {
                // Most HumanAI tasks have an override, but it is not required: an absent one runs the
                // archetype selected by AIType instead.
                _logger.Debug(LogCategory, $"No behaviour script at '{path}'.");
            }
        }
        catch (Exception ex) when (ex is IOException or QvmFormatException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"Could not read behaviour script at '{path}': {ex.Message}");
        }

        cache[path] = program;
        return program;
    }

    /// <summary>The prepared rig for an animation set, built once and shared by every corpse wearing it.</summary>
    /// <remarks>
    /// Keyed by the SET rather than the skeleton, because the joint limits are measured from the set's
    /// own clips — the animators' demonstration of how far each joint is meant to move, which is what
    /// gives a knee a hinge without any bone being named. Building it sweeps every rotation key in the
    /// set twice, so it must happen once per mission and not once per death.
    /// </remarks>
    internal RagdollSkeleton? RagdollRigFor(BoneAnimationSet set, MefModel? model)
    {
        if (RagdollRigs.TryGetValue(set, out RagdollSkeleton? rig))
        {
            return rig;
        }

        try
        {
            rig = RagdollSkeleton.Build(set.Skeleton, RagdollLimits.Measure(set), model);
        }
        catch (ArgumentException ex)
        {
            // A rig the solver cannot use is not fatal: the retail death clip is still playing
            // underneath, so the body falls the way it always did and only the remaster is skipped.
            _logger.Warning(LogCategory, $"Skeleton {set.Skeleton.Id} cannot drive a ragdoll: {ex.Message}");
            return null;
        }

        RagdollRigs[set] = rig;
        return rig;
    }

    /// <summary>What corpses collide against — the same two queries a living body uses.</summary>
    internal RagdollWorld RagdollWorldForCorpses()
    {
        if (RagdollWorld is { } world)
        {
            return world;
        }

        AiSoldier.GroundQuery? ground = MakeGroundQuery();
        RagdollWorld = new RagdollWorld(
            Cast,
            ground is null ? null : (double x, double y, double z, out double groundZ) => ground(x, y, z, out groundZ))
        {
            Triangles = _dropTriangles,
            Trace = _dropTrace,
        };
        return RagdollWorld;
    }

    internal AiSoldier.GroundQuery? MakeGroundQuery()
    {
        IGroundHeightField? terrain = Terrain;
        WorldRayCast? cast = Cast;
        if (terrain is null && cast is null)
        {
            return null;
        }

        return (x, y, z, out groundZ) =>
        {
            groundZ = double.MinValue;
            bool found = false;

            if (cast is not null
                && cast(
                    new Vector3d(x, y, z),
                    new Vector3d(x, y, z - ProbeBelowInUnits - ProbeAboveInUnits),
                    out WorldRayHit hit))
            {
                groundZ = hit.Position.Z;
                found = true;
            }

            if (terrain is not null
                && terrain.TryGetGround(x, y, z, out TerrainGround ground)
                && ground.Height >= z - ProbeBelowInUnits - ProbeAboveInUnits
                && ground.Height <= z)
            {
                if (ground.Height > groundZ)
                {
                    groundZ = ground.Height;
                }

                found = true;
            }

            return found;
        };
    }

    internal static float GammaOf(SoldierView view) => view.Soldier.Gamma;

    /// <summary>A round a soldier has just fired.</summary>
    /// <param name="origin">Where it leaves, in world units — the soldier's eye.</param>
    /// <param name="aim">Where it is aimed, in world units.</param>
    /// <param name="hitProbability">
    /// The chance the AI gave it of being on target — <c>Human+1236</c>, which the weapon reads with
    /// every round.
    /// </param>
    /// <param name="weapon">What fired it.</param>
    /// <param name="shooter">The soldier that fired, or <see langword="null"/> for a player round.</param>
    public delegate void SoldierRound(
        Vector3d origin,
        Vector3d aim,
        float hitProbability,
        WeaponType weapon,
        SoldierView? shooter);

    /// <summary>Sets where a soldier's rounds go.</summary>
    /// <param name="handler">The handler, or <see langword="null"/> to let rounds vanish.</param>
    /// <remarks>
    /// The soldier raises the trigger; this is where the round becomes a cast. Keeping the two apart is
    /// the original's own split — the AI writes an input slot and the body's weapon path does the rest.
    /// </remarks>
    public void SetRoundHandler(SoldierRound? handler) => OnRound = handler;

    /// <summary>Gets where soldiers' rounds go, so another AI gun (a vehicle's) can fire through the same cast.</summary>
    public SoldierRound? RoundHandler => OnRound;

    /// <summary>Gets how many rounds the level's soldiers have fired.</summary>
    public int RoundsFired { get; internal set; }

    /// <summary>Connects each retail GuardGenerator to the one authored soldier subtree it owns.</summary>
    private void PrepareGuardGenerators(GameTask? levelRoot)
    {
        GuardGenerators.Clear();
        if (levelRoot is null)
        {
            return;
        }

        foreach (GuardGeneratorTask generator in FindGuardGenerators(levelRoot))
        {
            HumanSoldierTask? authored = generator.Children.OfType<HumanSoldierTask>().FirstOrDefault();
            SoldierView? view = authored is null
                ? null
                : Brains.Keys.FirstOrDefault(candidate => ReferenceEquals(candidate.Soldier, authored));
            if (view is null)
            {
                _logger.Warning(
                    LogCategory,
                    $"{generator.Name} has no resolved HumanSoldier child; its authored spawn cannot run.");
                continue;
            }

            GuardGenerators[generator] = view;
        }

        if (GuardGenerators.Count > 0)
        {
            _logger.Information(
                LogCategory,
                $"{GuardGenerators.Count} GuardGenerator(s) own bounded authored soldier templates.");
        }
    }

    internal void BindViewTracking(AiSoldier brain, SoldierView view)
    {
        brain.SetScriptSound((name, attached) => ScriptSound?.Invoke(name, view.Position, attached));
        brain.SetEyeTransform(
            () =>
            {
                BonePose pose = view.Pose();
                if (HumanAttachPoints.AiEye >= pose.BoneCount)
                {
                    return new Vector3d(
                        view.Position.X,
                        view.Position.Y,
                        view.Position.Z + AiSoldier.HumanSeparationEyeHeight);
                }

                // The bone offset is from the model's origin, which is drawn above the feet by the model's
                // lowest vertex -- as IGI.exe's Human+32 stands above them. Adding it to the feet put the
                // eye at waist height, below every parapet a guard looks over.
                Vector3f eye = pose.Positions[HumanAttachPoints.AiEye];
                double originLift = FindRoster(view) is { } group ? -group.Body.LowestVertexZ * WorldScene.UnitsPerMetre : 0.0;
                return new Vector3d(
                    view.Position.X + eye.X,
                    view.Position.Y + eye.Y,
                    view.Position.Z + originLift + eye.Z);
            },
            // The attachment supplies the animated eye POSITION only; its local rotation differs by
            // skeleton and clip. The snapshot (0x489960) tests against the view matrix: the body's heading
            // tipped by the view's own pitch.
            //
            // But only while the soldier is DRAWN. 0x44F410 copies the eye's matrix out of the bone palette
            // ([Human+0x70]+0x2B8), and the palette is filled by the skinning in the draw callback
            // (0x49F700), which runs only for a soldier inside the render camera's frustum (0x4D06C0). A
            // guard off the player's screen keeps looking the way it last did on it -- and one never drawn
            // looks along its rest pose, about five degrees off world +Y whatever its body faces. Measured
            // on the retail game with the probe: seventeen of twenty-one mission-1 guards shared that exact
            // rest matrix, and turning the view onto one made its eye follow its body within two ticks.
            () =>
            {
                SoldierAgent? member = FindAgent(view);
                if (member is null)
                {
                    return brain.ViewOrientation;
                }

                if (member.InView)
                {
                    member.EyeOrientation = brain.ViewOrientation;
                }

                return member.EyeOrientation ?? RestEyeOrientation;
            });
    }

    /// <summary>
    /// The eye matrix of a guard the player has never drawn -- bone 5's rest orientation, unrotated by the
    /// body, read off every undrawn guard of mission 1 by the probe (the cone test's snapshot, +24).
    /// </summary>
    internal static readonly Matrix3f RestEyeOrientation = new(
        0.9956f, -0.0927f, 0.0076f,
        0.0919f, 0.9926f, 0.0784f,
        -0.0148f, -0.0774f, 0.9968f);

    private static IEnumerable<GuardGeneratorTask> FindGuardGenerators(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is GuardGeneratorTask generator)
            {
                yield return generator;
            }

            foreach (GuardGeneratorTask nested in FindGuardGenerators(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Opens a tick: the tick count, and where the enemy (the player) is read from.</summary>
    /// <param name="enemy">
    /// Reads the player as a soldier sees it at the moment the soldier updates -- live, so a soldier ahead of the
    /// player in the tree sees last tick's pose.
    /// </param>
    public void BeginTick(Func<SoldierEnemy> enemy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(enemy);
        TickCount++;
        Enemy = enemy;
        Ticked.Clear();
        CollectDiagnostics = _logger.IsEnabled(LogLevel.Information);
        bool collectDiagnostics = CollectDiagnostics;

        // The liveness half of the [aiflow] diff: proof the AI tick is running at all, ten seconds apart.
        // The patrol/activity counts ride the same line so a capture can show WHEN patrols engage after
        // the intro cutscene, without a separate per-tick flood.
        if (collectDiagnostics && TickCount % 300 == 1)
        {
            int onPatrol = Brains.Values.Count(b => b.Route is not null);
            Dictionary<string, int> activity = [];
            foreach (AiSoldier brain in Brains.Values)
            {
                string key = brain.Activity.ToString();
                activity[key] = activity.GetValueOrDefault(key) + 1;
            }

            string activityText = string.Join(", ", activity.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
            _logger.Information(
                LogCategory + ".Flow", $"heartbeat tick={TickCount} brains={Brains.Count} onPatrol={onPatrol} [{activityText}]");

            // Diagnostic-only: the Soldier.Trace category at Debug adds per-soldier detail for the friendlies
            // (ids 400-405, plus the heli crews by their mind ids), so a long lockstep run shows what they
            // actually do. At Trace it extends the same record to every active human, which is useful for
            // checking first-base guard grounding without changing the normal diagnostic volume.
            if (_logger.IsEnabled(LogLevel.Debug, TraceLogCategory))
            {
                bool traceAll = _logger.IsEnabled(LogLevel.Trace, TraceLogCategory);
                foreach (KeyValuePair<SoldierView, AiSoldier> pair in Brains)
                {
                    HumanSoldierTask soldier = pair.Key.Soldier;
                    HumanAiTask? mind = soldier.Children.OfType<HumanAiTask>().FirstOrDefault();
                    int mindId = mind?.Id ?? -1;
                    bool friendly = soldier.Id is >= 400 and <= 405
                        || soldier.Id == 428
                        || mindId is >= 540 and <= 544
                        || mindId is >= 1234 and <= 1238
                        || soldier.Id == 666;
                    if (!friendly && !traceAll)
                    {
                        continue;
                    }

                    AiSoldier brain = pair.Value;
                    string traceTag = friendly ? "friendly" : "guard";
                    string route = brain.Route is null ? "-" : $"path {brain.Route.Path.Id} cmd={brain.Route.Current?.Kind}";
                    string last = brain.LastActions.Count == 0
                        ? "none"
                        : string.Join(",", brain.LastActions.Select(a => $"{a.Name.Split('_')[^1]}({string.Join(",", a.Arguments)}) "));
                    string pending = string.Join(",", Enumerable.Range(0, 24).Select(i => (AiEvent)i).Where(t => brain.Events.PendingCount(t) > 0).Select(t => $"{t}x{brain.Events.PendingCount(t)}"));
                    string dispatched = brain.LastDispatchedEvent?.ToString() ?? "-";
                    double groundZ = SnapToGround(pair.Key.Position.X, pair.Key.Position.Y, pair.Key.Position.Z);
                    double groundGap = (pair.Key.Position.Z - groundZ) / WorldScene.UnitsPerMetre;
                    _logger.Debug(
                        TraceLogCategory,
                        $"{traceTag} soldier {soldier.Id} mind {mindId} {brain.Activity} {route} " +
                        $"evt={dispatched} last={last} pending=[{pending}] lastTick={brain.LastTick} " +
                        $"anim={pair.Key.CurrentAnimation} rm={Math.Abs(pair.Key.RootMotion().Y):0.0} " +
                        $"pos=({pair.Key.Position.X / 4096.0:0.0},{pair.Key.Position.Y / 4096.0:0.0}) " +
                        $"bpos=({brain.Position.X / 4096.0:0.0},{brain.Position.Y / 4096.0:0.0}) cur={brain.CurrentNode} dest={brain.DestinationNode} " +
                        $"target={brain.Target?.Target ?? "-"} " +
                        $"ground={groundZ / WorldScene.UnitsPerMetre:0.00} gap={groundGap:0.000}m " +
                        $"fwd={brain.LastSteeringForward:0.0} rmT={brain.LastAdvanceRm:0.0} st={brain.LastSteeringOutcome} " +
                        $"moved={(brain.LastMovedTo is { } mv ? $"({mv.X / 4096.0:0.0},{mv.Y / 4096.0:0.0})" : "-")} facing={brain.LastFacingDeg:0.0} " +
                        $"wp={(brain.DebugWaypoint is { } wpp ? $"{wpp.X / 4096.0:0.000},{wpp.Y / 4096.0:0.000}" : "-")}");

                    foreach ((SoldierView otherView, AiSoldier other) in Brains)
                    {
                        if (ReferenceEquals(other, brain))
                        {
                            continue;
                        }

                        double dx = other.Position.X - brain.Position.X;
                        double dy = other.Position.Y - brain.Position.Y;
                        double h = Math.Sqrt((dx * dx) + (dy * dy)) / 4096.0;
                        if (h < 3.0)
                        {
                            _logger.Debug(
                                TraceLogCategory,
                                $"{traceTag}   near s{soldier.Id}: s{otherView.Soldier.Id} m{otherView.Soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id} {h:0.000} m {other.Activity}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>Publishes the living soldiers' collision bodies where they stand now.</summary>
    /// <remarks>
    /// The player's wall sweep is their only reader, and the player updates after every soldier, so publishing
    /// here before the player moves is what re-registering each soldier as it moves would give.
    /// </remarks>
    public void PublishCollisionBodies()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SyncCollisionBodies();
    }

    /// <summary>A door's collision child follows the same live transform as the drawn door.</summary>
    /// <remarks>
    /// A half-open door remains a blocker at its half-open angle instead of disappearing from sight, bullets and
    /// movement.
    /// </remarks>
    internal void DoorTicked(PlacedDoor door)
    {
        if (_solids is not { } solids)
        {
            return;
        }

            // A fully-open door is passable regardless of how its leaf is authored to clear the
            // opening. Sliding leaves (nonzero Stop*) are moved out of the way by UpdateTransform
            // below, but cab doors that animate inside their model (Stop* == 0, e.g. Elevator_330's
            // leaves) never change placement, so their static mesh would keep the doorway solid for
            // ever; toggling passability on the open state makes both kinds behave like retail.
            solids.SetPassable(door.Task, door.State.IsFullyOpen);

            // Closed/open doors spend nearly all their life stationary, unless they ride a
            // moving elevator: keep collision synchronized with the rendered displacement.
            Vector3d elevatorOffset = _elevators?.GetCurrentDisplacement(door.Task) ?? default;
            if (door.State.TransformChangedThisTick || elevatorOffset != default)
            {
                solids.UpdateTransform(
                    door.Task, door.DrawPosition + elevatorOffset, door.DrawOrientation);
            }

            // Sight treats building portals as solid. Once this leaf is off its shut pose,
            // open the portal it filled so a guard in the doorway can see through.
            solids.SetSightOpening(door.Task, !door.State.IsFullyClosed, elevatorOffset);
    }

    internal void AlarmControlTicked()
    {
        bool alarmIsOn = AlarmControls?.AnyOn ?? false;
        if (alarmIsOn != _alarmWasOn)
        {
            _alarmWasOn = alarmIsOn;
            _logger.Information(
                LogCategory + ".Alarm",
                $"mission alarm {(alarmIsOn ? "ON" : "OFF")} at tick {TickCount}.");
        }
    }

    internal void AttachCutsceneActorToMovingGround(
        SoldierView view,
        Vector3d probePosition,
        Vector3d? attachmentPosition = null,
        WorldTraceMask mask = WorldTraceMask.DynamicObjects,
        bool includeInactiveOwners = false)
    {
        if (_solids is null || Cutscenes is null)
        {
            return;
        }

        // A moving owner that leaves its conditional container is the native
        // Human_GroundCollision inactive-owner branch (0x463550): clear the parent slot first. An
        // empty query is intentionally different and leaves an existing parent in place, so this
        // check is based only on the owner's live registration state.
        if (Cutscenes.TryDetachInactiveAttachment(view.Soldier))
        {
            return;
        }

        if (Cutscenes.TryGetAttachedDrawTransform(view.Soldier, out _, out _))
        {
            // Retail 0x4626E8 only calls Human_SetParent when Human+1900 is null. Once attached,
            // the parent owns movement until a separate detach path clears it; another ground ray
            // must not silently recapture or switch the local frame.
            return;
        }

        // Retail IGI.exe 0x4623C3 samples BoneDynCubeObj's animated root, then 0x462403 forms the
        // standing ground-check Z as Human.Position.Z + 1638.4 - RootSample.Z. Querying from the task origin
        // instead hits geometry above the feet (Mission 1's helicopter underside) and parents Jones
        // to the wrong vehicle. DynamicObject membership alone is NOT the moving-platform flag
        // tested at 0x4626E1: a hovering airframe is dynamic but never captures (its riders are
        // direct QVM children); only a platform contact reaches Human_SetParent at 0x463310.
        double checkZ = probePosition.Z + HumanGroundCheckLiftInUnits - view.RootSample.Z;
        WorldTraceRequest request = new(
            new Vector3d(probePosition.X, probePosition.Y, checkZ),
            new Vector3d(probePosition.X, probePosition.Y, checkZ - HumanGroundProbeDownInUnits),
            mask,
            IgnoredOwner: view.Soldier,
            IncludeInactiveOwners: includeInactiveOwners);
        if (_solids.TryTrace(request, out WorldTraceHit hit)
            && hit.Owner is { } parent
            && CinematicActor.IsMovingPlatform(parent))
        {
            // The retail probe selects the supporting task from the live foot query, but the first
            // Human_SetParent packet still contains the staged Human+32. Our actor mover may have
            // consumed a root delta while the vehicle's conditional container was dormant, so keep
            // those two inputs distinct instead of baking that scheduling error into the local frame.
            Cutscenes.TryAttachActorToTask(
                view.Soldier,
                parent,
                attachmentPosition ?? probePosition,
                view.Orientation);
        }
    }

    internal void AttachStagedActorsToVehicles()
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (member.IsCutsceneActor && !Brains.ContainsKey(member.View))
                {
                    AttachStagedActor(member);
                }
            }
        }
    }

    /// <summary>Retries a live cutscene actor's attachment to the moving ground under it.</summary>
    internal void AttachStagedActor(SoldierAgent member)
    {
        if (_solids is null
            || Cutscenes is null
            || !ConditionalContainerGate.IsTaskActive(member.View.Soldier))
        {
            return;
        }

        AttachCutsceneActorToMovingGround(
            member.View,
            member.Position * WorldScene.UnitsPerMetre,
            attachmentPosition: null,
            mask: WorldTraceMask.DynamicObjects,
            includeInactiveOwners: false);
    }

    /// <summary>A body's heading out of its world 3×3 — the inverse of <c>Z(facing)</c>.</summary>
    /// <remarks>
    /// The second column is the body's forward, and a soldier's forward is
    /// <c>(-sin f, cos f)</c> (see <see cref="AiSoldier"/>), so the angle reads straight back out.
    /// Orientations are pure Z rotations for every body this renderer draws.
    /// </remarks>
    internal static float FacingOf(in Matrix3f orientation) => MathF.Atan2(-orientation.M12, orientation.M22);

    /// <summary>Gets whether the player's third-person body is being shown climbing a ladder.</summary>
    public bool IsPlayerBodyClimbing => PlayerBodyClimbing;

    /// <summary>Stops drawing the standing body — the view has reached the head.</summary>
    public void HidePlayerBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PlayerBodyShown = false;
    }

    /// <summary>Gets whether the third-person view is currently placing and animating the body.</summary>
    public bool IsPlayerBodyFollowing => PlayerBodyFollowing;

    /// <summary>Stops following the player — the view has gone back inside their head.</summary>
    /// <remarks>
    /// <para>
    /// <b>And gives the rig back.</b> The torso clip on bone 1 and the aim bend belong to the follow and
    /// to nothing else: while the player is on a ladder, at a keypad, cutting a wire or dead, an authored
    /// full-body clip owns the whole character, and a weapon stance left in its shoulders replaces the
    /// half of that clip above the waist. <see cref="SoldierView.StartLadderAnimation"/> and
    /// <see cref="SoldierView.Kill"/> release it too, so no ordering between this and them can leave the
    /// override behind — but doing it here as well is what makes the invariant a property of the follow
    /// rather than of a list of call sites.
    /// </para>
    /// <para>
    /// Cheap to repeat: this is called on every tick the follow is not running, and both releases return
    /// at once when there is nothing to release.
    /// </para>
    /// </remarks>
    public void HidePlayerBodyFollowing()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PlayerBodyFollowing)
        {
            return;
        }

        PlayerBodyFollowing = false;
        PlayerBodyUpperBodySequence = -1;
        PlayerBodyView?.ReleaseUpperBody();
    }

    internal static bool IsWireAnimation(int animation) =>
        animation == HumanLocomotionStates.For(42).AnimationId
        || animation == HumanLocomotionStates.For(43).AnimationId;

    internal void StartPlayerTraversalAnimation(
        SoldierView view,
        int animation,
        bool loops,
        bool backwards,
        bool paused,
        bool suppressRootHeight,
        double visualLift)
    {
        PlayerBodyClimbing = true;
        PlayerBodySuppressRootHeight = suppressRootHeight;
        PlayerBodyVisualLift = visualLift;
        view.StartLadderAnimation(animation, loops, backwards, paused);
        PlayerBodyRootHeightAtStart = view.RootSample.Z;
    }

    /// <summary>Changes animation 168's playback direction without restarting or snapping its pose.</summary>
    /// <param name="direction">−1 down, 0 paused at a boundary, or +1 up.</param>
    public void SetPlayerLadderPlaybackDirection(int direction) =>
        PlayerBodyView?.SetLadderPlaybackDirection(direction);

    /// <summary>Gets the interaction task position adjusted by the clip's visible root-height change.</summary>
    /// <remarks>
    /// The body renderer adds the current root Z above the task, while the third-person camera normally
    /// follows the task itself. Following the delta from the clip's entry height keeps an airborne vault
    /// framed without treating the standing pelvis height as an extra metre of world elevation.
    /// </remarks>
    public Vector3d PlayerInteractionCameraFeet(Vector3d taskPosition)
    {
        if (PlayerBodyView is not { } view || PlayerBodySuppressRootHeight)
        {
            return taskPosition;
        }

        return new Vector3d(
            taskPosition.X,
            taskPosition.Y,
            taskPosition.Z + view.RootSample.Z - PlayerBodyRootHeightAtStart + PlayerBodyVisualLift);
    }

    /// <summary>Stops showing the climbing body — the player has left the ladder.</summary>
    public void HidePlayerBodyClimbing()
    {
        PlayerBodyClimbing = false;
        PlayerBodyVisualLift = 0.0;
        PlayerBodyRootHeightAtStart = 0.0;
    }

    /// <summary>Gives the soldiers the grenade simulation their throws land in.</summary>
    /// <param name="grenades">The shared pool — the same one the player's own throws fly in.</param>
    public void SetGrenades(GrenadeSimulation? grenades) => Grenades = grenades;

    // The lob: reach the target's feet in the settings' own fly time, and cut the fuse to the
    // settings' explode time. The velocity is the closed-form ballistic answer for the port's
    // per-tick integrator: v = d/T + g(T+1)/2 upward, which lands EXACTLY on the target after T
    // ticks of `v.z -= g; p += v`.
    internal void ThrowAiGrenade(AiSoldier brain, Vector3d target, GrenadeSimulation grenades)
    {
        AiCombatParameters? parameters = brain.CombatParameters;
        if (parameters is null)
        {
            return;
        }

        float flySeconds = parameters.GrenadeMinimumFlyTimeSeconds
            + ((float)AiRandom.NextDouble() * parameters.GrenadeRandomFlyTimeSeconds);
        int flyTicks = Math.Max(1, (int)(flySeconds * 30f));

        Vector3d origin = brain.EyePosition();
        Vector3f velocity = new(
            (float)((target.X - origin.X) / flyTicks),
            (float)((target.Y - origin.Y) / flyTicks),
            (float)((target.Z - origin.Z) / flyTicks) + (GrenadePhysics.GravityPerTick * (flyTicks + 1) * 0.5f));

        float fuseSeconds = parameters.GrenadeMinimumExplodeTimeSeconds
            + ((float)AiRandom.NextDouble() * parameters.GrenadeRandomExplodeTimeSeconds);
        int fuseTicks = fuseSeconds > 0f ? Math.Max(1, (int)(fuseSeconds * 30f)) : GrenadePhysics.FuseTicks;

        grenades.SpawnWithVelocity(origin, velocity, GrenadeExplosionType.Normal, fuseTicks);
    }

    /// <summary>Evaluates a soldier's current animation at the heading actually displayed for this frame.</summary>
    /// <remarks>
    /// A <see cref="SoldierView"/> caches a faced pose, whereas drawing applies the r_smooth orientation
    /// after the identical unrotated animation. Re-evaluating only the cached pose here gives the hit mesh
    /// the same animated limbs and presented heading without changing the authoritative AI transform.
    /// </remarks>
    internal static BonePose PoseAtPresentationOrientation(SoldierView soldier, in Matrix3f orientation)
    {
        Matrix3f simulationOrientation = soldier.Orientation;
        if (simulationOrientation == orientation)
        {
            return soldier.Pose();
        }

        Vector3d simulationPosition = soldier.Position;
        try
        {
            soldier.PlaceAt(simulationPosition, orientation);
            return soldier.Pose();
        }
        finally
        {
            soldier.PlaceAt(simulationPosition, simulationOrientation);
        }
    }

    /// <summary>Gets a soldier's damage state, creating it on the first hit.</summary>
    internal HumanHealth HealthOf(SoldierView soldier)
    {
        if (!Health.TryGetValue(soldier, out HumanHealth? health))
        {
            health = NewHealth(soldier);
            Health[soldier] = health;
        }

        return health;
    }

    /// <summary>A non-lethal hit: the soldier's class decides whether it flinches, and it grunts.</summary>
    /// <remarks>
    /// Every hit handler plays the grunt on the non-lethal branch, and an explosion only when it made the
    /// soldier react (<c>0x45A3D0</c>). The flinch is the bit the handlers raise for the body machine.
    /// </remarks>
    internal void ReactToHit(SoldierView soldier, AiSoldier? brain, int zone, float dealt, bool explosion)
    {
        if (!_recoil.TryGetValue(soldier, out HumanRecoil? recoil))
        {
            float? configured = Combat.TryGetValue(soldier, out AiCombatParameters? row) ? row.RecoilThreshold : null;
            recoil = soldier.Soldier.Class.CreateRecoil(configured);
            _recoil[soldier] = recoil;
        }

        HumanRecoilOutcome outcome = recoil.Absorb(dealt);
        if (outcome.Flinches)
        {
            brain?.Flinch(zone);
        }

        if (!explosion)
        {
            ReportHitSound(soldier);
        }
        else if (outcome.Reacted)
        {
            ReportHitSound(soldier, HumanVariantSound.AiExplosionHitPrefix, HumanVariantSound.AiExplosionHitVariants);
        }
    }

    /// <summary>
    /// A fresh damage state — <c>0x489C30</c> over the class's hit-zone table, with the settings row's
    /// maximum health once <c>0x406F70</c> has configured the soldier.
    /// </summary>
    internal HumanHealth NewHealth(SoldierView soldier) => new(
        soldier.Soldier.Class.HitZones,
        Combat.TryGetValue(soldier, out AiCombatParameters? row) ? row.MaximumHealth : HumanHitZoneTable.MaximumHealth);

    /// <summary>What a gun leaves a dying hand with: the body's own motion plus a share of the round's knock.</summary>
    /// <param name="carried">The body's motion, units per tick, in world space.</param>
    /// <param name="travel">The killing round's line.</param>
    /// <returns>The launch velocity, units per tick.</returns>
    /// <remarks>
    /// The grip opens as the round lands, so the gun keeps what the body was doing and takes a small
    /// push along the shot -- sixty centimetres a second, a sideways stumble rather than a throw -- with a
    /// little lift from the arm flinching. Only the realistic drop reads it; the original's ignores it.
    /// </remarks>
    internal static Vector3d DropVelocity(Vector3d carried, Vector3d travel)
    {
        const double knock = 0.6 * 4096.0 / 30.0;
        const double lift = 0.3 * 4096.0 / 30.0;
        Vector3d flat = new(travel.X, travel.Y, 0.0);
        double length = flat.Length;
        Vector3d push = length > 1e-6 ? flat * (knock / length) : default;
        return carried + push + new Vector3d(0.0, 0.0, lift);
    }

    /// <summary>Gets the player's team, resolved once per level from the declared HumanPlayer task.</summary>
    /// <remarks>
    /// This is what the round handler compares a shooter's team against to decide whether a round may
    /// strike the player: a guard (opposing team) may, a friendly (same team) never does.
    /// </remarks>
    public int PlayerTeam => _playerTeam;

    /// <summary>Gets the level's player task, which the player's rounds name as their shooter; <see langword="null"/> for none.</summary>
    public HumanPlayerTask? PlayerTask { get; private set; }

    /// <summary>Pushes every living, active soldier's body into the shared collision index.</summary>
    /// <remarks>
    /// Called once per tick after the roster has moved. A body is the analytic
    /// <see cref="RayCapsule"/> the player's wall sweep collides with; only soldiers that are alive
    /// and whose conditional container is live get one, so a dormant sentry or a corpse is no
    /// obstacle. The same <see cref="ObjectCollisionIndex"/> instance is the one the player camera
    /// uses for its walls, so a soldier presents a real wall in retail's spirit.
    /// </remarks>
    internal void SyncCollisionBodies()
    {
        if (_solids is not { } solids)
        {
            return;
        }

        int count = 0;
        foreach (SoldierView view in Brains.Keys)
        {
            if (!view.IsDead && ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                count++;
            }
        }

        if (count == 0)
        {
            solids.SetHumanBodies([]);
            return;
        }

        HumanBody[] bodies = new HumanBody[count];
        int index = 0;
        foreach (SoldierView view in Brains.Keys)
        {
            if (view.IsDead || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d pos = view.Position;
            double radius = 0.55 * WorldScene.UnitsPerMetre;
            bodies[index++] = new HumanBody(
                new Vector3d(pos.X, pos.Y, pos.Z),
                new Vector3d(pos.X, pos.Y, pos.Z + (1.8 * WorldScene.UnitsPerMetre)),
                radius);
        }

        solids.SetHumanBodies(bodies);
    }

    /// <summary>Every soldier where it stands at this moment, for an area counting as the level walk reaches it.</summary>
    public IEnumerable<HumanOccupant> LiveOccupants()
    {
        foreach (SoldierView view in Brains.Keys)
        {
            yield return new HumanOccupant(view.Soldier.Id, view.Soldier.Team, view.Position, view.IsDead);
        }
    }

    /// <summary>Reports every soldier's current presence for mission queries.</summary>
    /// <remarks>
    /// One occupant per placed soldier (dormant or active): its task id, team, current position and
    /// death. The host feeds this to the mission runtime on every tick so AreaActivate volumes count
    /// the friendly squad's advance and <c>HumanSoldier_N.isDead</c> tracks the guards the squad kills.
    /// </remarks>
    public IReadOnlyCollection<HumanOccupant> Occupants()
    {
        if (Brains.Count == 0)
        {
            return Array.Empty<HumanOccupant>();
        }

        HumanOccupant[] occupants = new HumanOccupant[Brains.Count];
        int index = 0;
        foreach (SoldierView view in Brains.Keys)
        {
            occupants[index++] = new HumanOccupant(
                view.Soldier.Id,
                view.Soldier.Team,
                view.Position,
                view.IsDead);
        }
        return occupants;
    }

    /// <summary>A nearest-human intersection that does not apply damage or AI events.</summary>
    public HumanRayCast Probe() => TryIntersect;

    /// <summary>A living-human proximity query for armed mines.</summary>
    public HumanProximityCast ProximityProbe() => TryProximity;

    /// <summary>Finds the nearest living soldier inside a mine's activation radius.</summary>
    public bool TryProximity(Vector3d center, float radiusUnits, out HumanProximityTarget target)
    {
        target = default;
        double radiusSquared = radiusUnits * radiusUnits;
        bool found = false;

        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || soldier.IsDead
                    || (Health.TryGetValue(soldier, out HumanHealth? health) && health is { IsDead: true }))
                {
                    continue;
                }

                Vector3d position = soldier.Position;
                double distanceSquared = Vector3d.DistanceSquared(center, position);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                double distance = Math.Sqrt(distanceSquared);
                if (!found || distance < target.Distance)
                {
                    target = new HumanProximityTarget(position, distance);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Finds the nearest living soldier on a segment without scoring the hit.</summary>
    public bool TryIntersect(Vector3d origin, Vector3d end, out HumanHit hit)
    {
        hit = default;
        bool found = false;

        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || soldier.IsDead
                    || (Health.TryGetValue(soldier, out HumanHealth? health) && health is { IsDead: true }))
                {
                    continue;
                }

                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                double snappedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                Vector3d feet = new(soldier.Position.X, soldier.Position.Y, snappedZ);
                bool met = group.Body.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.Pose(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.Pose(), out candidate, soldier.RootSample.Z);
                if (met && (!found || candidate.Distance < hit.Distance))
                {
                    hit = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Gets how many soldiers have been killed.</summary>
    public int Killed
    {
        get
        {
            int dead = 0;
            foreach (HumanHealth health in Health.Values)
            {
                if (health.IsDead)
                {
                    dead++;
                }
            }

            return dead;
        }
    }

    /// <summary>
    /// Offers an AI-fired round to the soldiers and scores it against the nearest one it meets.
    /// This is the NPC-vs-NPC counterpart to <c>TryShoot</c>: a guard's round can strike a
    /// friendly, and a friendly's can strike a guard, so Harrison's team at the gate trades fire with
    /// the guards instead of only ever being aimed at the player.
    /// </summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where the round stops.</param>
    /// <param name="hitProbability">
    /// The firing AI's chance the round is on target (its <c>Human+1236</c>). Retail rolls it only against the
    /// player, so a soldier the round meets never consults it; it is kept for the callers' symmetry.
    /// </param>
    /// <param name="weapon">The firing weapon, whose damage factor and category apply on a hit.</param>
    /// <param name="shooter">The soldier that fired; its own body is never offered the round.</param>
    /// <param name="worldHit">
    /// The nearest world surface ahead, when the world blocks the ray; a soldier behind it is never
    /// offered the round (the wall already took it). <see langword="null"/> when no surface stopped it.
    /// </param>
    /// <param name="hit">The soldier struck, when one is.</param>
    /// <returns>
    /// <see langword="true"/> when a soldier consumed the round (damaged, or a same-team/invulnerable
    /// body the round met and stopped at); <see langword="false"/> when it met nobody and flies on.
    /// </returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, exactly as in <c>TryShoot</c>. The Human's evaluate
    /// (<c>0x45E3E0</c>) decides each body the round meets, nearest first: one on the shooter's own side
    /// (<c>Human+252</c>) lets it pass through to the next, any other takes it -- with no probability roll,
    /// which retail makes only for the player. Scripted invulnerability (500.qvm sets
    /// <c>SetInvulnerability(TRUE)</c> on the friendly squad, so they cannot die) makes the round
    /// <em>meet</em> the body but do nothing; the body is still a physical obstacle that consumes it.
    /// </remarks>
    public bool TryShootAiRound(
        Vector3d origin,
        Vector3d end,
        float hitProbability,
        WeaponType weapon,
        SoldierView? shooter,
        WorldRayHit? worldHit,
        out HumanHit hit)
    {
        hit = default;

        List<(HumanHealth health, SoldierView view, HumanHit candidate, double distance)> candidates = [];

        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || ReferenceEquals(soldier, shooter))
                {
                    continue;
                }
                HumanHealth health = HealthOf(soldier);
                if (health.IsDead)
                {
                    continue;
                }

                // Same facing/feet/pose resolution as TryShoot: cast at the skinned collision mesh when
                // the model ships one, else the cylinder-and-nearest-bone stand-in, both on the grounded
                // feet the body is actually drawn on.
                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                double snappedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                Vector3d feet = new(soldier.Position.X, soldier.Position.Y, snappedZ);

                bool met = group.Body.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.Pose(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.Pose(), out candidate, soldier.RootSample.Z);
                if (!met)
                {
                    continue;
                }

                // A wall between the muzzle and this soldier stops the round before it arrives.
                double distance = candidate.Distance;
                if (worldHit is { } surface && surface.Distance < distance - 40.0)
                {
                    continue;
                }
                candidates.Add((health, soldier, candidate, distance));
            }
        }

        // Where the round stops if nobody takes it: the surface in its way, or the end of its reach.
        Vector3d unstopped = worldHit is { } wall ? wall.Position : end;
        if (candidates.Count == 0)
        {
            PassedBy(unstopped, null);
            return false;
        }

        // Nearest first: retail's cast meets the nearest human and rolls the probability there; a
        // failed roll flies the round on to the next one behind.
        candidates.Sort(static(a, b) => a.distance.CompareTo(b.distance));

        foreach ((HumanHealth health, SoldierView struckView, HumanHit candidate, _) in candidates)
        {
            // No probability roll against a soldier: the Human's evaluate (0x45E3E0) rolls the round's
            // probability only when the struck human is player 0. Any other body is hit outright unless
            // the shooter is on its side (Human+252), and then the round passes THROUGH it and flies on.
            if (!struckView.Soldier.EvaluateWeaponCollision(new WeaponCollisionQuery(shooter?.Soldier, null, hitProbability)))
            {
                continue;
            }

            hit = candidate;

            // The shot's WeaponHit to the struck soldier (0x47BA54), which its own handler takes.
            struckView.Soldier.WeaponHit(
                new Igi1WeaponHit(candidate, origin, end, weapon.DamageFactor, weapon.Category, shooter, FromPlayer: false).ToShared());
            PassedBy(candidate.Position, struckView);
            return true;
        }

        // Every candidate rolled a miss -- the round flew past them all and continues beyond.
        PassedBy(unstopped, null);
        return false;

        void PassedBy(Vector3d stop, SoldierView? struck)
        {
            if (shooter is not null)
            {
                NotifyRoundPassed(origin, stop, shooter, struck);
            }
        }
    }

    /// <summary>Applies one explosion to every living soldier in reach.</summary>
    /// <param name="position">The blast's centre, in world units.</param>
    /// <param name="radiusUnits">The full-damage radius.</param>
    /// <param name="falloffUnits">The taper past it — zero for the shipped grenade.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    /// <remarks>
    /// The retail rules through the port's own pieces: the dose is
    /// <see cref="ExplosionDamage.Dose"/> with the body radius at a third, and it lands on
    /// <b>zone 13</b> — an explosion never headshots, however the target stands. A kill goes through
    /// the same death path a bullet's does, with the blast direction as the round.
    /// </remarks>
    public void ApplyExplosion(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Every soldier on the explosion list is told (0x45A3D0 is HumanSoldier's ExplosionHit); each decides for itself.
        ExplosionHit blast = new Igi1ExplosionHit(position, radiusUnits, falloffUnits, damageFactor).ToShared();
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                member.View.Soldier.ExplosionHit(blast);
            }
        }
    }

    /// <summary>Gets or sets where a wounded body's blood is thrown: the body, where the round entered, and its direction.</summary>
    public Action<object, Vector3d, Vector3f>? BloodSpray { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human</c>'s evaluate (<c>0x45E3E0</c>), for a soldier: never the shooter or what the shot ignores, and a round from
    /// someone on the soldier's side passes through it. The probability roll is the player's alone.
    /// </remarks>
    bool IHumanEvents.EvaluateWeaponCollision(HumanTask soldier, in WeaponCollisionQuery query)
    {
        if (ReferenceEquals(soldier, query.Shooter) || ReferenceEquals(soldier, query.Ignore))
        {
            return false;
        }

        return query.Shooter is not HumanTask shooter || shooter.Team != soldier.Team;
    }

    /// <inheritdoc/>
    void IHumanEvents.WeaponHit(HumanTask soldier, in WeaponHit hit)
    {
        if (!_viewsByTask.TryGetValue(soldier, out SoldierView? view))
        {
            return;
        }

        // Another game's round: where it meets the soldier's body is this game's to find.
        Matrix3f m = view.Orientation;
        TakeRound(view, hit.Context as Igi1WeaponHit
            ?? Igi1WeaponHit.FromShared(hit, Igi1WeaponHit.Meet(hit, view.Position, new Vector3f(m.M12, m.M22, m.M32))));
    }

    /// <inheritdoc/>
    void IHumanEvents.ExplosionHit(HumanTask soldier, in ExplosionHit hit)
    {
        if (!_viewsByTask.TryGetValue(soldier, out SoldierView? view))
        {
            return;
        }

        Igi1ExplosionHit blast = hit.Context as Igi1ExplosionHit ?? Igi1ExplosionHit.FromShared(hit);

        TakeExplosion(view, blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
    }

    /// <summary>Gets the soldier a task stands for.</summary>
    /// <param name="task">The soldier's task.</param>
    /// <returns>The soldier, or <see langword="null"/> when the task is not one of these soldiers.</returns>
    public SoldierView? ViewOf(HumanTask task) => _viewsByTask.TryGetValue(task, out SoldierView? view) ? view : null;

    // Gives a soldier's task this simulation's handlers for its hit events, and its live body for any game's AI to perceive.
    private void TakeEvents(SoldierView view)
    {
        _viewsByTask[view.Soldier] = view;
        view.Soldier.Events = this;
        view.Soldier.Body = new Player.Igi1SoldierBody(view, () => Brains.TryGetValue(view, out AiSoldier? brain) && brain.IsCrouching);
    }

    /// <summary>A round or a blade on a soldier, whoever fired it: <c>HumanSoldier</c>'s <c>WeaponHit</c>, <c>0x45D3C0</c>.</summary>
    /// <remarks>
    /// <para>
    /// Invulnerability (<c>HumanAI+15036</c>) skips the whole damage block -- the round still meets the body, it just does
    /// nothing; instant death disabled (<c>+15037</c>) makes a head or neck hit take the plain damage. The blood follows the
    /// damage (<c>0x45D45C</c>), then the death or the flinch and the grunt, and the blade's own sound for a knife.
    /// </para>
    /// <para>
    /// Whatever the round did, the brain is told last: the <c>TAKINGDAMAGE</c> event (type 15) goes out at <c>0x45D577</c>
    /// to an invulnerable soldier as much as to a wounded one.
    /// </para>
    /// </remarks>
    private void TakeRound(SoldierView struckView, Igi1WeaponHit round)
    {
        HumanHealth health = HealthOf(struckView);
        HumanHit hit = round.Hit;
        AiSoldier? brain = Brains.TryGetValue(struckView, out AiSoldier? b) ? b : null;
        bool invulnerable = brain?.IsInvulnerable ?? false;
        if (!invulnerable)
        {
            float dealt = health.Apply(hit.Zone, round.DamageFactor, brain?.IsInstantDeathDisabled ?? false);
            if (BloodSpray is { } bleed)
            {
                Vector3d line = round.End - round.Origin;
                double length = line.Length;
                bleed(
                    struckView,
                    hit.Position,
                    length > 1e-6
                        ? new Vector3f((float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length))
                        : default);
            }

            if (health.IsDead)
            {
                Die(struckView, hit, round.Origin, round.End, round.Category, SoldierDeath.ModeFor(round.Category));
            }
            else
            {
                ReactToHit(struckView, brain, hit.Zone, dealt, explosion: false);
                if (round.Category == WeaponCategory.Knife && struckView.Soldier.Class.SoundsKnifeStab)
                {
                    ScriptSound?.Invoke(KnifeStabSound, struckView.Position, true);
                }
            }
        }

        brain?.ReceiveDamage(round.Origin, round.Shooter?.Soldier.Id ?? AiDetectionCandidate.PlayerId);

        // Deliberately the same shape as the probe's [dmg] line from the retail game (probes.cpp
        // OnDamageHandler), so a run of the port and a capture of the original diff directly.
        _logger.Information(
            LogCategory + ".Damage",
            $"port zone={hit.Zone} ({HumanDamage.ZoneName(hit.Zone)}) bone={hit.Bone} dmgFactor={round.DamageFactor:0.000} "
                + $"invuln={(invulnerable ? 1 : 0)} instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
                + $"accum={health.Damage:0.000} max={health.MaximumHealth:0.000} dead={(health.IsDead ? 1 : 0)}");
    }

    // One blast on one soldier.
    private void TakeExplosion(SoldierView soldier, Vector3d position, float radiusUnits, float falloffUnits, float damageFactor)
    {
        if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier))
        {
            return;
        }
        HumanHealth health = HealthOf(soldier);

        if (health.IsDead)
        {
            return;
        }

        Vector3d feet = soldier.Position;
        double distance = Math.Sqrt(
            ((feet.X - position.X) * (feet.X - position.X))
            + ((feet.Y - position.Y) * (feet.Y - position.Y))
            + ((feet.Z - position.Z) * (feet.Z - position.Z)));

        float dose = ExplosionDamage.Dose(
            (float)distance,
            ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
            radiusUnits,
            falloffUnits,
            damageFactor);
        if (dose <= 0f)
        {
            return;
        }

        AiSoldier? brain = Brains.TryGetValue(soldier, out AiSoldier? b) ? b : null;
        if (brain?.IsInvulnerable ?? false)
        {
            return;
        }

        // 0x45A3D0 posts no AI event: what a soldier makes of a blast it hears through NotifyExplosion.
        float dealt = health.Apply(ExplosionDamage.Zone, dose, brain?.IsInstantDeathDisabled ?? false);

        if (health.IsDead)
        {
            // 0x45A3D0 records death mode 0 for a blast: the body is thrown, turned to it.
            HumanHit hit = new(feet, distance, ExplosionDamage.Zone, 0f, 0f);
            Die(soldier, hit, position, feet, WeaponCategory.Grenade, SoldierDeathMode.Untouched);
        }
        else
        {
            ReactToHit(soldier, brain, ExplosionDamage.Zone, dealt, explosion: true);
        }
    }

    /// <summary>Gets or sets what prepares a generator's replacement guard to be drawn, and says whether it could.</summary>
    internal Func<GuardGeneratorTask, SoldierModelGroup, SoldierAgent, SoldierAgent, bool>? SpawnedBody { get; set; }

    /// <summary>Clones a fresh Human/HumanAI subtree while leaving the killed body in the world.</summary>
    /// <remarks>
    /// The replacement is made only once its renderer, when there is one, has its drawing ready (<see cref="SpawnedBody"/>):
    /// a transient upload failure leaves the generator free to try again next tick.
    /// </remarks>
    private SoldierView? SpawnGeneratedGuard(GuardGeneratorTask generator, SoldierView deadView)
    {
        SoldierAgent? deadAgent = FindAgent(deadView);
        if (deadAgent is null || !Brains.TryGetValue(deadView, out AiSoldier? deadBrain))
        {
            _logger.Warning(LogCategory, $"{generator.Name} could not clone its missing rendered Human subtree.");
            return null;
        }

        SoldierModelGroup? group = Roster.FirstOrDefault(candidate => candidate.Agents.Contains(deadAgent));
        if (group is null)
        {
            _logger.Warning(LogCategory, $"{generator.Name} could not find its rendered Human group.");
            return null;
        }

        SoldierView freshView = new(deadView.Soldier, deadView.Bones.Set);
        Vector3d spawn = new(
            deadAgent.Position.X * WorldScene.UnitsPerMetre,
            deadAgent.Position.Y * WorldScene.UnitsPerMetre,
            deadAgent.Position.Z * WorldScene.UnitsPerMetre);
        freshView.PlaceAt(spawn, Matrix3f.FromEngineEulerAngles(0f, 0f, deadView.Soldier.Gamma));

        SoldierAgent freshAgent = new(freshView, deadAgent.Position)
        {
            IsCutsceneActor = false,
            WeaponType = deadAgent.WeaponType,
            AmmoInClip = deadAgent.WeaponType?.RoundsPerClip ?? 0,
        };
        if (SpawnedBody is { } drawing && !drawing(generator, group, freshAgent, deadAgent))
        {
            return null;
        }

        group.Add(freshAgent);

        if (DetectorMarkerScaleMetres.TryGetValue(deadView, out float markerScale))
        {
            DetectorMarkerScaleMetres[freshView] = markerScale;
        }
        if (Combat.TryGetValue(deadView, out AiCombatParameters? combat))
        {
            Combat[freshView] = combat;
        }
        Health[freshView] = NewHealth(freshView);

        AiSoldier freshBrain = deadBrain.Respawned(
            freshView.Position,
            freshView.Soldier.Gamma,
            brain => CombatWeaponState(brain, freshAgent));
        // The corpse remains registered: sharing its runtime ID would advance one weapon sound
        // twice per tick and send replies to the old guard's alerts to its replacement instead.
        // Keep the authored Soldier.Id for mission queries, but give each live instance its own ID.
        freshBrain.RuntimeId = checked(BrainsByRuntimeId.Keys.DefaultIfEmpty(0).Max() + 1);
        BindViewTracking(freshBrain, freshView);
        freshBrain.SetDefaultTarget(PlayerTarget);
        Brains[freshView] = freshBrain;
        BrainsByRuntimeId[freshBrain.RuntimeId] = freshBrain;
        TakeEvents(freshView);
        if (Humans.Count > 0)
        {
            Humans.Add(freshView.Soldier);
        }

        _logger.Information(
            LogCategory,
            $"{generator.Name} spawned {generator.SpawnCount}/{generator.MaxSpawns}: "
                + $"{freshView.Soldier.Name} cloned at its authored position; the previous body remains.");
        return freshView;
    }

    // The level walk this renderer's tasks update from, once AttachTo has put them in it; null when standalone.
    private LevelUpdateWalker? _walker;

    private readonly HashSet<GameTask> _unwalkedSoldiers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Runs the native generator decision before any live soldier is advanced this tick.</summary>
    /// <summary>A GuardGenerator's update (<c>0x487EE0</c>): its expression, and a replacement for a dead guard.</summary>
    internal void TickGuardGenerator(GuardGeneratorTask generator)
    {
        if (!GuardGenerators.TryGetValue(generator, out SoldierView? view))
        {
            return;
        }

        bool isOn = GuardExpressions?.EvaluateCondition(generator.GenerateGuards, emptyValue: false, generator)
            ?? generator.GenerateGuards.Trim() is "1" or "TRUE" or "true";
        // Publish the first authored life immediately, but do not consume a bounded replacement
        // until its mesh and runtime subtree have actually been created. A transient upload failure
        // must leave the generator eligible to retry on the next tick.
        GuardSpawnDecision decision = generator.Advance(isOn, childIsDead: false);
        if (decision != GuardSpawnDecision.FirstSpawn
            && isOn
            && view.IsDead
            && generator.SpawnCount < generator.MaxSpawns
            && SpawnGeneratedGuard(generator, view) is { } fresh)
        {
            _ = generator.Advance(isOn: true, childIsDead: true);
            GuardGenerators[generator] = fresh;
        }
    }

    /// <summary>Updates every body a soldier task owns -- a generator's dead guard and its replacement share one.</summary>
    private void TickSoldierTask(GameTask task)
    {
        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            if (ReferenceEquals(view.Soldier, task))
            {
                TickSoldier(view, brain);
            }
        }
    }

    /// <summary>Puts the soldiers, their guard generators, the doors and the alarm controls into the level's walk.</summary>
    /// <param name="walker">The level walk.</param>
    /// <remarks>
    /// <para>
    /// The original has no soldier pass: each <c>HumanSoldier</c> updates where the walk reaches it
    ///, its children -- HumanView, Shadow, the gun, then the HumanAI
    /// whose CHANNELS packet moves it -- before the next soldier starts. A soldier therefore sees the soldiers
    /// before it in the tree where they are this tick, and those after it (and the player, late in
    /// <c>ConditionalContainer_560</c>) where they were last tick.
    /// </para>
    /// <para>
    /// A <c>GuardGenerator</c> (<c>0x487EE0</c>) decides and clones first, then walks its soldier; a <c>Door</c>
    /// (<c>0x446D10</c>) and an <c>AlarmControl</c> (<c>0x4408A0</c>) walk their children first, then update.
    /// Call <see cref="BeginTick"/> before the walk and <c>EndTick</c> after it.
    /// </para>
    /// </remarks>
    public void AttachTo(LevelUpdateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _walker = walker;
        HashSet<GameTask> soldiers = new(ReferenceEqualityComparer.Instance);
        foreach (SoldierView view in Brains.Keys)
        {
            if (soldiers.Add(view.Soldier))
            {
                walker.Bind(view.Soldier, task =>
                {
                    TickSoldierTask(task);
                    walker.RunChildren(task);
                });
            }
        }

        foreach (GuardGeneratorTask generator in GuardGenerators.Keys)
        {
            walker.Bind(generator, task =>
            {
                TickGuardGenerator((GuardGeneratorTask)task);
                walker.RunChildren(task);
            });
        }

        Doors?.AttachTo(walker, DoorTicked);
        AlarmControls?.AttachTo(walker, () => TickCount, _ => AlarmControlTicked());
    }

    /// <summary>Runs one tick outside a level walk: the same per-task updates, in one pass.</summary>
    /// <param name="enemy">The player as the soldiers see it this tick.</param>
    internal void TickStandalone(SoldierEnemy enemy)
    {
        BeginTick(() => enemy);
        foreach (GuardGeneratorTask generator in GuardGenerators.Keys.ToArray())
        {
            TickGuardGenerator(generator);
        }

        if (Doors is { } doors)
        {
            foreach (PlacedDoor door in doors.Doors)
            {
                doors.Tick(door);
                DoorTicked(door);
            }
        }

        if (AlarmControls is { } alarmControls)
        {
            alarmControls.Tick(TickCount);
            AlarmControlTicked();
        }

        foreach ((SoldierView view, AiSoldier brain) in Brains)
        {
            TickSoldier(view, brain);
        }

    }

    /// <summary>Closes the tick's simulation before the corpses move: the bodies the walk missed, the staged actors, the collision.</summary>
    internal void FinishTick()
    {
        bool collectDiagnostics = CollectDiagnostics;
        if (_walker is not null)
        {
            // A registered, live soldier the walk never reached sits under a task the port does not walk the way
            // the original does; tick it so it cannot freeze, and say so once.
            foreach ((SoldierView view, AiSoldier brain) in Brains)
            {
                if (Ticked.Contains(view) || !ConditionalContainerGate.IsTaskActive(view.Soldier))
                {
                    continue;
                }

                if (_unwalkedSoldiers.Add(view.Soldier))
                {
                    _logger.Warning(
                        LogCategory,
                        $"{view.Soldier.Name} is live but the level walk did not reach it; ticking it after the walk.");
                }

                TickSoldier(view, brain);
            }
        }

        // Un-brained cutscene actors ride their vehicles the same way; the brained ones did as they updated.
        AttachStagedActorsToVehicles();

        // Every soldier has moved; re-publish their bodies so the player's wall sweep treats each
        // living, active soldier as solid.
        SyncCollisionBodies();

        // This scans the complete roster. Calling it from inside the roster loop made diagnostics O(N²)
        // every tick, including when the logger discarded the result. One final-state scan is sufficient.
        if (collectDiagnostics)
        {
            ReportCombat();
        }

        // Simulated corpses advance on the tick, alongside the rigs they replaced -- and only until they
        // settle, because nothing in this game ever removes a body and a corpse that kept stepping would
        // be a cost paid for the rest of the mission.
    }

    /// <summary>After the corpses moved: the dropped weapons, and the poses of the bodies no brain drives.</summary>
    internal void TickAfterCorpses()
    {
        DroppedWeaponPickups?.Tick(RagdollWorldForCorpses());
        foreach (SoldierModelGroup group in Roster)
        {
            if (group.Body.Model is null)
            {
                continue;
            }

            foreach (SoldierAgent agent in group.Agents)
            {
                if (!Brains.ContainsKey(agent.View))
                {
                    // Un-brained soldiers still need their own rig advanced.
                    agent.View.Tick();
                }
            }
        }
    }

    /// <summary>Scores a guard's round on the <b>player's own body</b> — its skinned collision mesh.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <returns><see langword="false"/> when the round misses the player's body, or no body is loaded.</returns>
    /// <remarks>
    /// The original resolves a shot at the player exactly as at a soldier — against the human's skinned
    /// collision mesh — so a round through a limb scores that limb (×0.5–0.75), not the torso. The port
    /// had cast at a 0.55 m cylinder and scored every hit as the trunk, which made the player both a
    /// fatter target and one that took maximum damage from every round. This poses the player's own
    /// (otherwise idle) third-person body at the live position and heading and traces the real mesh.
    /// </remarks>
    public bool TryHitPlayerBody(
        Vector3d origin, Vector3d end, Vector3d playerFeetUnits, float playerYaw, out HumanHit hit)
    {
        hit = default;
        if (PlayerBodyView is not { } view || PlayerBodyModel?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        return HumanHitTest.TryCastMesh(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z, out hit);
    }

    /// <summary>Every face of the player's own body a round's line crosses, nearest first.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="crossings">The crossings, entries and exits, in order along the ray.</param>
    /// <returns><see langword="false"/> when no body is loaded; an empty list when the round misses it.</returns>
    /// <remarks>
    /// <see cref="TryHitPlayerBody"/>'s cast, carried on through the body: a round that penetrates it meets its
    /// exit, and may meet another part behind (<c>0x47B750</c>).
    /// </remarks>
    public bool TryCastPlayerBody(
        Vector3d origin,
        Vector3d end,
        Vector3d playerFeetUnits,
        float playerYaw,
        out IReadOnlyList<HumanBodyCrossing> crossings)
    {
        crossings = [];
        if (PlayerBodyView is not { } view || PlayerBodyModel?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        crossings = HumanHitTest.CastMeshCrossings(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z);
        return true;
    }
}

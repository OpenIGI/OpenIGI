using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// One pass of Project I.G.I.'s mission: the lifts, the level's walk, the player, the cameras, the spinning objects and
/// the soldiers, in the order the port runs them each tick.
/// </summary>
/// <param name="mission">The mission's world.</param>
/// <param name="harm">What a vehicle that runs the player down does to them.</param>
/// <param name="blasts">What resolves the blasts the player's update set off.</param>
/// <param name="audio">The mission heard, whose steps run at their points in the tick.</param>
/// <param name="host">What shows and plays it.</param>
public sealed partial class Igi1MissionTick(MissionWorld mission, Igi1PlayerHarm harm, Igi1Blasts blasts, Igi1MissionAudio audio, IMissionTickHost host) : IMissionTick
{
    private bool _reportedShot;
    private int _reportedHumanHits;

    // Whether the player's ladder climb or world interaction has begun its body animation, and whether the
    // press that mounted the ladder has been let go, so that it does not become the slide on the next tick.
    private bool _climbActive;
    private bool _climbSlideArmed;

    // Animation 170's end, seen on the tick it ends and acted on the tick after (see the top transitions below).
    private bool _ladderTopOffEnded;
    private bool _worldInteractionActive;

    /// <summary>Forgets a climb or an interaction in progress, as a new mission starts.</summary>
    public void ResetClimbs()
    {
        _climbActive = false;
        _climbSlideArmed = false;
        _ladderTopOffEnded = false;
        _worldInteractionActive = false;
    }

    /// <summary>Runs one pass of the loop.</summary>
    /// <param name="tick">Whether the pass is a simulation tick; a render pass only keeps the player's jump gate current.</param>
    public void Run(bool tick)
    {
        if (tick)
        {
            BeginTick();
        }

        // The player cannot jump while standing inside an elevator cab. The elevator registry knows
        // the player's position (fed above), so it can report whether they're inside.
        if (mission.Player is { } playerWithJump)
        {
            playerWithJump.CanJump = mission.Elevators is { } jumpElevators
                ? (Func<bool>)(() => !jumpElevators.IsPlayerInsideElevator(
                    playerWithJump.Body.Position, out _))
                : null;
        }

        if (!tick)
        {
            return;
        }


        // A level with no player task in its walk updates the player after the walk instead.
        if (!mission.PlayerInWalk)
        {
            UpdatePlayer();
        }

        if (mission.Cutscenes?.IsPlaying != true && host.PlayerMode && mission.Player is not null)
        {
            ResolvePlayerTick();
            ReportPlayerShots();
        }
        else if (mission.Cutscenes?.IsPlaying != true && host.PlayerMode && mission.PlayerStandIn is { } standIn)
        {
            ResolveStandInTick(standIn);
        }
        else
        {
            // A cut-scene or the free camera: nobody's update steps the world's effects, yet in the original they are tasks
            // that run whatever is watching. An explosion under a cut-scene burns and smokes, and a chimney keeps smoking.
            host.StepWorldEffects();
            mission.SmokePlumes?.Tick();
        }

        // PlayerCamera owns the normal player tick, including interaction pulses. Other fixed-tick
        // modes (free camera, cutscenes, map computer and a dead player) still need their level
        // interactions to advance, but never more than once per simulation tick.
        if (!mission.InteractionsTickedByPlayer)
        {
            mission.Interactions?.Tick();
        }

        EndTick();

        // What the tick changed, told once it is over; nothing in it reads what a listener does.
        mission.EventWatch?.Observe();
    }

    /// <summary>The player as a soldier sees it at the moment the soldier updates.</summary>
    /// <returns>The player as an enemy.</returns>
    /// <remarks>
    /// In the fly-through the eye is the CAMERA's: guards otherwise watch an immobile body at the spawn. The feet
    /// are what combat aims at (sub_451380). A dead player is no longer a target (the detection scan
    /// 0x489750 unregisters it), and neither is one hidden behind a cinematic.
    /// </remarks>
    public SoldierEnemy SoldierEnemyNow()
    {
        bool body = Walking();
        Vector3d eye = body ? PlayerEye : host.FreeCameraPosition ?? PlayerEyeIfAny ?? default;
        Vector3d feet = body ? PlayerFeet : eye;
        SoldierEnemy own = new(eye, !mission.IsPlayerDead && mission.Cutscenes?.IsPlaying != true, feet, body && PlayerCrouched);
        return mission.NetworkPlayers.Count == 0 ? own : NearestEnemy(own);
    }

    // A co-op session: the original's soldiers know one enemy, so each is given the living player nearest to it as it updates.
    private SoldierEnemy NearestEnemy(SoldierEnemy own)
    {
        if (mission.Soldiers is not { } soldiers || mission.Cutscenes?.IsPlaying == true)
        {
            return own;
        }

        Vector3d at = soldiers.TickingSoldierPosition;
        SoldierEnemy best = own;
        double bestDistance = own.Alive ? Vector3d.DistanceSquared(own.Feet ?? own.Eye, at) : double.PositiveInfinity;
        foreach (Multiplayer.Igi1NetworkPlayer other in mission.NetworkPlayers)
        {
            if (other.IsDead)
            {
                continue;
            }

            double distance = Vector3d.DistanceSquared(other.Player.Body.Position, at);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new SoldierEnemy(other.Player.Body.Eye, true, other.Player.Body.Position, other.Player.IsCrouched);
            }
        }

        return best;
    }

    /// <summary>The player as an area counts it at the moment the area updates.</summary>
    /// <returns>The player as an area's occupant.</returns>
    /// <remarks>
    /// During an authored cinematic the occupant is the cutscene camera rather than the stationary player body; a
    /// diagnostic free camera must not impersonate a player that exists (Mission 4's AreaActivate_156 would start
    /// the outro as the camera follows Bell 800).
    /// </remarks>
    public AreaPlayer AreaPlayerNow()
    {
        Vector3d? cutsceneView = mission.Cutscenes?.Frame?.Camera.Position;
        Vector3d? freeView = !host.PlayerMode ? host.FreeCameraPosition : null;
        return new AreaPlayer(
            AreaOccupancy.AreaActivationOccupant(PlayerOrigin, cutsceneView, freeView),
            mission.Soldiers?.PlayerTeam ?? 0,
            AreaOccupancy.AreaActivationExtraOccupant(PlayerOrigin, cutsceneView),
            !mission.IsPlayerDead);
    }

    // Whether the player's body is what the world sees, asked afresh each time as the tick may change it: the rig's, or another
    // game's player standing in its stead.
    private bool Walking() => host.PlayerMode && (mission.Player is not null || mission.PlayerStandIn is not null);

    // Where the player's eyes and feet are, whether it crouches, and the middle of its body an area counts: the rig's, else the
    // player standing in its stead.
    private Vector3d PlayerEye => PlayerEyeIfAny ?? default;

    private Vector3d? PlayerEyeIfAny => mission.Player?.Body.Eye ?? mission.PlayerStandIn?.Eyes;

    private Vector3d PlayerFeet => mission.Player?.Body.Position ?? mission.PlayerStandIn?.Feet ?? default;

    private bool PlayerCrouched => mission.Player?.IsCrouched ?? mission.PlayerStandIn?.IsKeepingLow ?? false;

    private Vector3d? PlayerOrigin => mission.Player?.Body.Origin
        ?? (mission.PlayerStandIn is { } standIn
            ? new Vector3d(standIn.Feet.X, standIn.Feet.Y, (standIn.Feet.Z + standIn.Eyes.Z) * 0.5)
            : null);

    private void BeginTick()
    {
        // The storm steps here rather than inside the player's own block, where the impact
        // particles step: weather is ambient, and it has to keep falling in the free camera and
        // while the player is down.
        host.StepWeather();

        if (mission.Elevators is { } elevators)
        {
            // The player's position is for carrying a passenger, not for starting a car: the original
            // starts one on its own script alone.
            elevators.SetPlayerPositionProvider(mission.Player is not null || mission.PlayerStandIn is not null ? () => PlayerFeet : null);

            // A level the walk does not reach steps its lifts here, ahead of everything else.
            if (!mission.ElevatorsInWalk)
            {
                foreach (PlacedElevator elevator in elevators.Elevators)
                {
                    StepElevator(elevators, elevator);
                }
            }
        }

        // The level's one walk (the retail task tree, in authored order). Soldiers, doors, alarms, areas and
        // the player all update inside it where the tree places them: a task reads this tick's state from
        // everything before it and last tick's from everything after, so the soldiers -- all ahead of
        // ConditionalContainer_560 -- see the player where it stood last tick.
        mission.Objectives?.SetSoldierOccupants(mission.Soldiers?.Occupants() ?? []);
        Vector3d? areaCutsceneView = mission.Cutscenes?.Frame?.Camera.Position;
        Vector3d? areaFreeView = !host.PlayerMode ? host.FreeCameraPosition : null;
        mission.Objectives?.SetLevelStateInputs(
            AreaOccupancy.AreaActivationOccupant(PlayerOrigin, areaCutsceneView, areaFreeView),
            mission.Soldiers?.PlayerTeam ?? 0,
            AreaOccupancy.AreaActivationExtraOccupant(PlayerOrigin, areaCutsceneView),
            playerAlive: !mission.IsPlayerDead);
        mission.PlayerTicked = false;
        mission.InteractionsTickedByPlayer = false;
        if (mission.SoldiersInWalk)
        {
            mission.Soldiers?.BeginTick(mission.CurrentEnemy ??= SoldierEnemyNow);
        }

        mission.Cutscenes?.Tick();
        if (mission.Cutscenes is { } soundingVehicles)
        {
            host.SoundVehicles(soundingVehicles);
        }

        if (mission.Cutscenes is { } liveCutscenes && host.Scene is { } cinematicScene && mission.Solids is { } cinematicSolids)
        {
            foreach (WorldPlacement placement in cinematicScene.Placements)
            {
                if (liveCutscenes.TryGetPlacementDrawTransform(
                    placement.Owner,
                    placement.Position,
                    placement.Orientation,
                    out Vector3d position,
                    out Matrix3f orientation))
                {
                    cinematicSolids.UpdateTransform(placement, position, orientation);
                }
            }
        }
    }

    private void EndTick()
    {
        // The damage wash fades a forty-fifth a tick whatever the frame rate -- decayed here, on the
        // fixed tick, for the same determinism reason the bodies are.
        mission.DamageFlash.Tick();
        mission.FlashbangFlash.Tick();
        host.StepScreen();

        // The player's eye is what the guards' view cones are tested against -- and in the
        // fly-through it is the CAMERA's, not the body's. A shell affordance, not engine behaviour.
        Vector3d watched = Walking()
            ? PlayerEye
            : host.FreeCameraPosition ?? PlayerEyeIfAny ?? default;

        // A dead player is no longer a target: the guards lose it and stop firing at the corpse, the
        // way the retail detection scan (0x489750) unregisters a human that has died.
        //
        // The eye is what vision watches; the FEET are what combat aims at (sub_451380 raises the
        // target task's position 0.5 m, 0.25 m crouched) -- in the fly-through there is no body, so
        // the camera stands in for both.
        Vector3d watchedFeet = Walking() ? PlayerFeet : watched;

        // The areas and edit variables ran inside the level walk above, before the AlarmControls that
        // consume them (Mission 1's alarm latch depends on that order).
        host.ApplyTestWrites();
        mission.Cutscenes?.FinishExpressionTick();

        // Where the ear is for the next tick's ambient areas: the camera as this tick left it (0x416D40's view, +96) --
        // the cinematic's while one plays, the player's eye otherwise. On the probe the areas were outside on tick 0,
        // before any camera was placed, and inside from tick 1, while the skipped intro was still closing.
        mission.AmbienceListener = mission.Cutscenes?.Frame?.Camera.Position * WorldScene.UnitsPerMetre
            ?? (mission.Player is { } ear ? ear.EyeUnits : mission.AmbienceListener);
        if (mission.IntroSkipPending)
        {
            host.StopCutsceneSounds();
            mission.Objectives?.ClearCutsceneMessages();
            mission.IntroSkipPending = mission.Cutscenes?.IsPlaying == true;
        }

        // The level's fans and radar heads, one tick further round. Unconditional and cheap: it
        // is a counter, and the renderer turns the transform from it when it draws.
        mission.Spins?.Advance();
        // A level the walk does not reach updates its cameras here, after everything else.
        if (!mission.CamerasInWalk && mission.SecurityCameras is { } cameras)
        {
            SecurityCameraScan scan = CameraScanNow();
            cameras.Tick(scan.TargetFeet, scan.TargetCrouching, scan.TargetAlive, scan.PlayerOnCamerasSide, scan.Soldiers);
            foreach (SecurityCamera camera in cameras.Cameras)
            {
                CarryCameraHead(camera);
            }
        }

        if (mission.RotatingObjects is { } rotatingObjects)
        {
            rotatingObjects.Tick();
            if (mission.Solids is { } rotatingSolids)
            {
                rotatingObjects.ApplyCollision(rotatingSolids);
            }
        }

        ISoldierPresenter? soldiers = mission.SoldierPresenter;
        if (mission.SoldiersInWalk)
        {
            // The soldiers updated inside the level walk; this closes their tick (corpses, skinning).
            soldiers?.EndTick();
        }
        else
        {
            soldiers?.Tick(
                watched,
                enemyAlive: !mission.IsPlayerDead && mission.Cutscenes?.IsPlaying != true,
                enemyFeet: watchedFeet,
                enemyCrouching: Walking() && PlayerCrouched);
        }

        // The player's own body advances its death fall on the tick, the same as a soldier's corpse --
        // it does nothing until the player has died and the body was posed.
        soldiers?.TickPlayerBody();

        // A simulated corpse decides for itself where it ends up, so the death camera has to
        // follow it. Retail needs no such thing: its fall is authored, and aiming at the spot
        // you were shot frames the whole of it.
        // The death camera eases and collides on this tick too (HumanCamera mode 5), so it is fed every tick: the body's
        // own eye height over where it fell when nothing simulates the corpse.
        if (mission.IsPlayerDead && mission.Player is { } fallen)
        {
            Vector3d feet = fallen.Body.Position;
            host.FollowCorpse(soldiers?.PlayerRagdollCentre
                ?? new Vector3d(feet.X, feet.Y, feet.Z + (PlayerBody.StandingEyeHeight * 0.5)));
        }

        TickClimbsAndInteractions();
        FollowPlayerBody();
        host.CloseTick();
    }

    /// <summary>Advances the ladder and the world interactions on the fixed tick, and the camera that frames them.</summary>
    private void TickClimbsAndInteractions()
    {
        ISoldierPresenter? soldiers = mission.SoldierPresenter;
        // The complete IGI1 ladder graph: 37 pauses animation 168 at authored event 8, 38/39 play
        // animation 170 backwards/forwards at the top, and 40 loops 169 while its damped velocity
        // falls to a ground collision. Every advance happens here, on the fixed 30 Hz tick.
        if (mission.Player is not null && mission.Player.IsOnLadder)
        {
            mission.Player.InputProfile = host.InputProfile;
            bool climbUp = mission.Player.IsActionDown(host.Input, PlayerAction.MoveUp);
            bool climbDown = mission.Player.IsActionDown(host.Input, PlayerAction.MoveDown);
            bool climbUse = mission.Player.IsActionDown(host.Input, PlayerAction.Activate);

            // Mount and use share E. The release gate is a shell detail that prevents the mounting press
            // from becoming state 40 on the immediately following tick.
            if (!_climbActive)
            {
                _climbActive = true;
                _climbSlideArmed = false;
                if (mission.Player.LadderPhase == LadderTraversalPhase.GettingOnTop)
                {
                    soldiers?.StartPlayerLadderAnimation(
                        PlayerCamera.LadderTopAnimation, loops: false, backwards: true);
                }
                else
                {
                    StartLadderClimbAnimation(soldiers);
                }
            }

            if (!climbUse)
            {
                _climbSlideArmed = true;
            }

            bool climbSlide = climbUse && _climbSlideArmed;
            LadderStepResult decision = mission.Player.DecideLadderStep(climbUp, climbDown, climbSlide);
            switch (decision)
            {
                case LadderStepResult.SteppingUp:
                case LadderStepResult.SteppingDown:
                    mission.Soldiers?.SetPlayerLadderPlaybackDirection(mission.Player.LadderDirection);
                    break;

                case LadderStepResult.ReachedTop:
                    soldiers?.StartPlayerLadderAnimation(
                        PlayerCamera.LadderTopAnimation, loops: false);
                    break;

                case LadderStepResult.Sliding:
                    soldiers?.StartPlayerLadderAnimation(
                        PlayerCamera.LadderSlideAnimation, loops: true);
                    break;
            }

            if (mission.Player.IsOnLadder && soldiers is not null)
            {
                switch (mission.Player.LadderPhase)
                {
                    case LadderTraversalPhase.Climbing when mission.Player.LadderDirection != 0:
                        soldiers.AdvancePlayerLadderAnimation(
                            mission.Player.LadderFacing,
                            out Vector3d climbDelta,
                            out float? rungEventTime,
                            out _);
                        mission.Player.MoveLadder(climbDelta);
                        if (TakeLadderRungEvent(rungEventTime))
                        {
                            mission.Player.CompleteLadderStep();
                            soldiers.Simulation.SetPlayerLadderPlaybackDirection(0);
                        }

                        break;

                    case LadderTraversalPhase.GettingOffTop when _ladderTopOffEnded:
                        // State 39 (0x40DA80) reads the clip-done bit the previous tick's advance set, in the state machine
                        // ahead of this tick's advance: it lets go of the ladder a tick after animation 170 ends, the clip
                        // held on its last frame meanwhile.
                        _ladderTopOffEnded = false;
                        mission.Player.CompleteLadderTopTransition();
                        break;

                    case LadderTraversalPhase.GettingOnTop:
                    case LadderTraversalPhase.GettingOffTop:
                        soldiers.AdvancePlayerLadderAnimation(
                            mission.Player.LadderFacing,
                            out Vector3d topDelta,
                            out float? topEventTime,
                            out bool topAnimationEnded);
                        mission.Player.MoveLadder(topDelta);
                        TakeLadderRungEvent(topEventTime);

                        if (topAnimationEnded && mission.Player.LadderPhase == LadderTraversalPhase.GettingOffTop)
                        {
                            _ladderTopOffEnded = true;
                        }
                        else if (topAnimationEnded)
                        {
                            mission.Player.CompleteLadderTopTransition();
                            if (mission.Player.LadderPhase == LadderTraversalPhase.Climbing)
                            {
                                StartLadderClimbAnimation(soldiers);
                            }
                        }

                        break;

                    case LadderTraversalPhase.SlidingDown:
                        soldiers.AdvancePlayerLadderAnimation(
                            mission.Player.LadderFacing,
                            out Vector3d slideDelta,
                            out _,
                            out _);
                        // State 40's entry only installs animation 169 and the sound. Its damped
                        // velocity function first runs on the following tick.
                        if (decision != LadderStepResult.Sliding)
                        {
                            mission.Player.TickLadderSlide(slideDelta, host.Ground!);
                        }

                        break;
                }
            }

            // All four states stay in third person until their actual deinitialization signal.
            if (mission.Player.IsOnLadder)
            {
                soldiers?.PlacePlayerBodyClimbing(mission.Player.LadderFeet, mission.Player.LadderFacing);
                if (!host.IsInteractionViewActive)
                {
                    WorldRayCast? cameraCast = mission.Solids?.Caster();
                    host.BeginInteractionView(mission.Player.LadderFeet, mission.Player.LadderFacing, cameraCast);
                }
                else
                {
                    host.FollowInteractionView(mission.Player.LadderFeet);
                }

                host.TickInteractionView();
            }
        }

        // Outside the ladder block on purpose: the tick the slide reaches the ground has already left the ladder, and that
        // tick is the one that must stop the sound.
        audio.TickLadderSlideSound(mission.Player);

        if (mission.Player is { IsInWorldInteraction: true } interacting)
        {
            if (!_worldInteractionActive)
            {
                _worldInteractionActive = true;
                if (interacting.InteractionAnimation >= 0)
                {
                    soldiers?.StartPlayerInteractionAnimation(
                        interacting.InteractionAnimation,
                        loops: interacting.InteractionAnimationLoops);
                }
            }

            if (interacting.IsMountedStationaryGun)
            {
                // The gun's sight sits just in front of the gunner's own chest, and retail draws
                // no body in that view (captured in mission 4: barrel, stand and scenery only).
                mission.Soldiers?.HidePlayerBodyClimbing();
            }
            else if (soldiers is not null)
            {
                // State 42 plays its drag clip only while a step is under way (0x40DDA0).
                if (interacting.IsOnLevelWire)
                {
                    mission.Soldiers?.SetPlayerLadderPlaybackDirection(interacting.WireDragDirection);
                    if (interacting.TakeWireDragSound() is { } dragSound)
                    {
                        audio.PlayLadderStepSound(dragSound, interacting.InteractionFeet);
                    }
                }

                soldiers.AdvancePlayerLadderAnimation(
                    interacting.InteractionFacing,
                    out Vector3d interactionRootDelta,
                    out float? interactionStepTime,
                    out bool interactionClipEnded);
                interacting.ApplyInteractionRootMotion(interactionRootDelta, interactionClipEnded, interactionStepTime);
                soldiers.PlacePlayerBodyClimbing(
                    interacting.InteractionFeet, interacting.InteractionFacing);
            }

            Vector3d interactionCameraFeet = mission.Soldiers?.PlayerInteractionCameraFeet(
                interacting.InteractionFeet) ?? interacting.InteractionFeet;

            // State 33 is the one interaction that keeps a first-person camera: the gun hands
            // the view its own sight (0x474420), so no orbit is started behind the body.
            if (interacting.IsMountedStationaryGun)
            {
                host.ResetInteractionView();
            }
            else if (!host.IsInteractionViewActive)
            {
                WorldRayCast? cameraCast = mission.Solids?.Caster();
                host.BeginInteractionView(interactionCameraFeet, interacting.InteractionFacing, cameraCast);
            }
            else
            {
                host.FollowInteractionView(interactionCameraFeet);
            }

            if (host.IsInteractionViewActive)
            {
                host.TickInteractionView();
            }
        }

        if ((mission.Player is null || (!mission.Player.IsOnLadder && !mission.Player.IsInWorldInteraction))
            && (_climbActive || _worldInteractionActive || host.IsInteractionViewActive))
        {
            mission.Soldiers?.HidePlayerBodyClimbing();
            host.ResetInteractionView();
            _climbActive = false;
            _ladderTopOffEnded = false;
            _worldInteractionActive = false;
        }
    }

    /// <summary>The player's update: input, movement and the weapon, once per tick.</summary>
    /// <remarks>
    /// A dead player is in locomotion state 47, whose per-tick is not the walking one -- so the body stops being
    /// stepped rather than being stepped with no input. Without this a corpse walks around. What the update set
    /// in motion -- sounds, effects, detonations -- is resolved after the walk while <see cref="MissionState.PlayerTicked"/> holds.
    /// </remarks>
    public void UpdatePlayer()
    {
        if (host.TickTitlePlayer())
        {
            return;
        }

        if (mission.PlayerTicked
            || mission.Cutscenes?.IsPlaying == true
            || !host.PlayerMode
            || mission.Player is not { } player
            || mission.IsPlayerDead
            || host.FailedMenu)
        {
            return;
        }

        mission.PlayerTicked = true;

        // The soldiers ahead of the player in the tree have moved this tick; the wall sweep meets them there.
        mission.Soldiers?.PublishCollisionBodies();
        player.InputProfile = host.InputProfile;
        GrantCheatWeapons(player);
        if (host.ComputerOpen)
        {
            TickMapComputer(player);
        }
        else
        {
            player.Tick(host.ViewInput, host.Ground);

            // PlayerCamera advances its interaction registry before it resolves this tick's activate target.
            // Do not advance the same registry again after the walk.
            mission.InteractionsTickedByPlayer = true;
            OpenMapComputer(player);
        }

        // A co-op session's other players, each on its client's command for this tick, after the mission's own.
        foreach (Multiplayer.Igi1NetworkPlayer other in mission.NetworkPlayers)
        {
            other.Run(mission.PlayerTicks, host.Ground);
        }

        mission.PlayerTicks++;
        mission.TickVehicleMissiles();
        CheckRunOver();

        // Grenade physics runs inside PlayerCamera.Tick. The grenade's AI events reach the soldiers' next
        // update, as retail's grenade task -- a runtime task at the end of Dynamic, after every soldier -- does.
        NotifyGrenadeAiEvents();
    }

    /// <summary>Kills the player when a moving vehicle's death zone reaches them (<c>0x45FA80</c>).</summary>
    private void CheckRunOver()
    {
        if (mission.Player is null || mission.IsPlayerDead || mission.Cutscenes is not { } runtime)
        {
            return;
        }

        Vector3d origin = mission.Player.Body.Position + new Vector3d(0d, 0d, Igi1HumanAi.HumanOriginStandingInUnits);
        if (!runtime.TryRunOver(origin, out GameTask? vehicle) || vehicle is null)
        {
            return;
        }

        mission.PlayerHealth.Apply(VehicleDeathZone.Zone, VehicleDeathZone.Damage);
        host.Logger.Information("Ai", $"The player was run over by {vehicle.Type.Name} {vehicle.Id}.");
        if (mission.PlayerHealth.IsDead)
        {
            Vector3d from = runtime.TryGetDrawTransform(vehicle, out Vector3d metres, out _)
                ? metres * WorldScene.UnitsPerMetre
                : origin;
            harm.BeginPlayerDeath(from, VehicleDeathZone.Zone, bone: -1, WeaponCategory.Knife);
        }
    }

    /// <summary>Delivers native grenade/explosion AI events before the soldier tick.</summary>
    /// <remarks>
    /// IGI1 broadcasts these from the task update itself: type 17 when a grenade is created, type 18 on
    /// the debounced landing sound, type 19 for a flashbang, and type 21 for an explosion. They are
    /// deliberately separate from gunshot/walk noise. The physics lists stay available until the effect
    /// pass below, so this method only consumes the one-shot throw list.
    /// </remarks>
    private void NotifyGrenadeAiEvents()
    {
        if (mission.Player is null || mission.Soldiers is not { } soldiers)
        {
            return;
        }

        foreach (Vector3d origin in mission.Player.Grenades.Thrown)
        {
            soldiers.NotifyGrenadeThrown(origin);
        }

        mission.Player.Grenades.ClearThrownEvents();

        foreach (Vector3d origin in mission.Player.Grenades.Bounces)
        {
            soldiers.NotifyGrenadeLand(origin);
        }

        // IGI1's proximity task emits proxy_arm on the first human entering its activation radius.
        // This is a one-shot rising-edge cue, not the mine's placement/arming sound.
        foreach (Vector3d origin in mission.Player.Grenades.MineTriggers)
        {
            host.PlayMineTrigger(origin);
        }

        mission.Player.Grenades.ClearMineTriggerEvents();

        foreach (GrenadeDetonation blast in mission.Player.Grenades.Detonations)
        {
            if (blast.Type == GrenadeExplosionType.Flashbang)
            {
                soldiers.NotifyFlashbang(blast.Position);
            }
            else
            {
                // The grenade's Explosion0 is heard over 10 m (0x476AC0's 40960).
                soldiers.NotifyExplosion(blast.Position, Igi1HumanAi.GrenadeExplosionEventRadiusInUnits);
            }
        }

        // A mine's is its ExplodeObject's (0x481A60), heard over 25 m.
        foreach (PlacedMine mine in mission.Player.Grenades.MineBlasts)
        {
            soldiers.NotifyExplosion(mine.BlastPoint, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        }

        foreach (MissileDetonation blast in mission.Player.Missiles.Detonations)
        {
            soldiers.NotifyExplosion(blast.Position, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        }
        foreach (MissileDetonation blast in mission.VehicleMissiles.Detonations)
        {
            soldiers.NotifyExplosion(blast.Position, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        }

        foreach (M203Detonation blast in mission.Player.M203.Detonations)
        {
            soldiers.NotifyExplosion(blast.Position, blast.RadiusUnits);
        }
    }

    /// <summary>Puts the soldiers (with their doors and alarms) and the player into the level's walk.</summary>
    /// <remarks>
    /// Retail updates <c>HumanPlayer_0</c> where the walk reaches it inside <c>ConditionalContainer_560</c>, late in
    /// <c>Dynamic</c>: its HumanView, Shadow, then HumanPlayerInput, whose CHANNELS packet moves it, then the
    /// binoculars and the gun. A level without a player task keeps updating the player after the walk.
    /// </remarks>
    /// <param name="levelRoot">The level's task tree.</param>
    public void AttachToLevelWalk(GameTask levelRoot)
    {
        mission.SoldiersInWalk = false;
        mission.PlayerInWalk = false;
        mission.ElevatorsInWalk = false;
        mission.CamerasInWalk = false;
        if (mission.Cutscenes is not { } cutscenes)
        {
            return;
        }

        LevelUpdateWalker walker = cutscenes.Walker;

        // A lift updates where the walk reaches it (Elevator's UPDATE, 0x43D280): its children first -- the
        // doors, switches and indicators that ride it -- then its own floor expressions and its move, after
        // which it carries what stands on it. One under a Static, or under a container that is not
        // running, does not move at all, as in the original.
        if (mission.Elevators is { } elevators)
        {
            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                walker.Bind(elevator.Task, task =>
                {
                    walker.RunChildren(task);
                    StepElevator(elevators, elevator);
                });
            }

            mission.ElevatorsInWalk = true;
        }

        // The switches, terminals, cabinets, fences, wires, GenericTBAs, stationary guns and generators update where the
        // walk reaches them, each in its own update's order -- before the cameras bind, whose controls extend a terminal's.
        if (mission.Interactions is { } interactions)
        {
            interactions.AttachTo(walker);
        }

        // A camera updates where the walk reaches it (SCamera's UPDATE, 0x442610): its lens sweeps and scans the
        // humans as they stand at that moment, then the holder takes its power for the next tick. Its controls
        // (0x440D70) read the isDetection of the cameras ahead of them this tick.
        if (mission.SecurityCameras is { } cameras)
        {
            cameras.AttachTo(walker, CameraScanNow, CarryCameraHead);
            mission.CamerasInWalk = true;
        }

        // A RotatingObject updates where the walk reaches it (0x439090), reading its On expression then.
        if (mission.RotatingObjects is { } rotators)
        {
            rotators.AttachTo(walker, rotator =>
            {
                if (mission.Solids is { } rotatorSolids)
                {
                    RotatingObjectRegistry.ApplyCollision(rotatorSolids, rotator);
                }
            });
        }
        if (mission.Soldiers is { } soldiers)
        {
            soldiers.AttachTo(walker);
            mission.SoldiersInWalk = true;
        }

        if (PlayerSpawn.FindGameplayTask(levelRoot) is { } playerTask)
        {
            walker.Bind(playerTask, task =>
            {
                UpdatePlayer();
                walker.RunChildren(task);
            });
            mission.PlayerInWalk = true;
        }
    }

    // What a lens scans as it updates (0x489750): every human and every camera, so a guard's body or a destroyed camera
    // in view raises the alarm at once, as the player does after 90 ticks. In the fly-through the free camera stands in
    // for the player's feet.
    private SecurityCameraScan CameraScanNow()
    {
        bool walking = Walking();
        Vector3d feet = walking
            ? PlayerFeet
            : host.FreeCameraPosition ?? PlayerEyeIfAny ?? default;
        return new SecurityCameraScan(
            feet,
            walking && PlayerCrouched,
            TargetAlive: !mission.IsPlayerDead,
            PlayerOnCamerasSide: (mission.Soldiers?.PlayerTeam ?? 0) == 1,
            mission.Soldiers?.CameraSubjects() ?? []);
    }

    // A camera's head collides where its update left it.
    /// <summary>Enters state 37's clip: animation 168's first frame, then held at the mount event.</summary>
    /// <param name="soldiers">The bodies, which draw the player's.</param>
    /// <remarks>
    /// <para>
    /// The entry <c>0x40D660</c> starts animation 168 as a new clip node: a full frame step forward
    /// (<c>0x4D4F60</c>), flagged <c>0x8000</c> so its first tick also reports the event at its start
    /// time (<c>0x4D4B60</c>). <c>HumanPlayer_Update</c> ticks that clip and moves the human by it in
    /// the same update, and the next update's state 37 finds event 8 at zero against the latch its entry
    /// cleared: it pauses the clip, commits nothing, and plays a rung sound. That is the sound of getting
    /// on — from the foot, or at the end of the top mount.
    /// </para>
    /// <para>
    /// The port takes all of it here, in one go, so the pause is in place before any input can start a
    /// rung; otherwise the mount event would land on the first rung's tick and commit it.
    /// </para>
    /// </remarks>
    private void StartLadderClimbAnimation(ISoldierPresenter? soldiers)
    {
        if (soldiers is null || mission.Player is not { } player)
        {
            return;
        }

        soldiers.StartPlayerLadderAnimation(PlayerCamera.LadderClimbAnimation, loops: true);
        soldiers.AdvancePlayerLadderAnimation(player.LadderFacing, out Vector3d entryDelta, out float? mountEventTime, out _);
        player.MoveLadderEntryFrame(entryDelta);
        TakeLadderRungEvent(mountEventTime);
        soldiers.Simulation.SetPlayerLadderPlaybackDirection(0);
    }

    /// <summary>Sounds a ladder state's crossing of event 8, when the latch lets it count.</summary>
    /// <param name="eventTime">The crossed event's time, or null when the clip crossed none.</param>
    /// <returns><see langword="true"/> when the crossing counted, so state 37 should commit its rung.</returns>
    private bool TakeLadderRungEvent(float? eventTime)
    {
        if (mission.Player is not { } player || eventTime is not { } time || !player.AcceptLadderRungEvent(time))
        {
            return false;
        }

        audio.PlayLadderStepSound(player.TakeLadderStep(), player.LadderFeet);
        return true;
    }

    private void CarryCameraHead(SecurityCamera camera)
    {
        if (mission.Solids is { } cameraSolids)
        {
            Vector3d headMetres = new(
                camera.HeadPositionUnits.X / WorldScene.UnitsPerMetre,
                camera.HeadPositionUnits.Y / WorldScene.UnitsPerMetre,
                camera.HeadPositionUnits.Z / WorldScene.UnitsPerMetre);
            cameraSolids.UpdateTransform(camera.Task, camera.HeadModelName, headMetres, camera.Orientation);
        }
    }

    // One lift's tick: its floor expressions and its move, then what the move carries -- its sound, its collision and the
    // player standing in it. Per lift, because a lift the walk has not reached yet this tick still holds last tick's move.
    private void StepElevator(ElevatorRegistry elevators, PlacedElevator elevator)
    {
        elevators.TickElevator(elevator);
        audio.UpdateElevatorSoundPositions();
        audio.UpdateScriptSoundPositions();

        // A staged soldier standing in a car rides it through the cutscene runtime's attachments, so the runtime is
        // told where the car now is.
        mission.Cutscenes?.SetPlatformPose(elevator.Task, elevator.State.Position, elevator.Orientation);
        if (mission.Solids is { } elevatorSolids)
        {
            elevators.ApplyCollision(elevatorSolids, elevator);
        }

        // The soldiers grounded on a graph authored in the cab ride it with their graph (0x4F9770, 0x460CF0).
        mission.Soldiers?.CarryOnPlatform(elevator.Task, elevator.State.Position - elevator.State.PreviousPosition);

        if (host.PlayerMode && mission.Player is { } passenger
            && ElevatorRegistry.TryGetPassengerDisplacement(elevator, passenger.Body.Position, mission.Solids, out Vector3d displacement))
        {
            passenger.Body.Position += displacement;
        }
    }

    // What the player's update set in motion this tick -- its sounds, effects and detonations -- in the order the port has
    // always resolved them, the host's sounds and pictures at their places among the simulation's own steps: the sounds
    // draw from the same generator the blasts and the shatters do.
    private void ResolvePlayerTick()
    {
        PlayerCamera player = mission.Player!;
        if (mission.PlayerTicked)
        {
            audio.SoundPlayerUpdate();

            // Hearing is emitted once per round, not once per projectile intersection. The weapon
            // row carries the exact retail radius (for example 4096 for suppressed fire and
            // 122880 for an ordinary rifle), so shotguns do not post duplicate noise events and
            // silenced weapons no longer wake the whole 30-metre neighbourhood.
            if (player.FiredThisTick && player.HeldWeaponType is { } audibleWeapon)
            {
                // Along the view, as far as the gun reaches: the line another game's humans see the shot by.
                Matrix3f view = player.Camera.Orientation;
                float reach = audibleWeapon.EffectiveRange * (float)WorldScene.UnitsPerMetre;
                mission.Soldiers?.NotifyGunshot(
                    player.Body.Eye,
                    audibleWeapon.AiDetectionRange,
                    line: new Vector3f(view.M31 * reach, view.M32 * reach, view.M33 * reach));
            }

            audio.SoundPlayerWeaponAndBody();

            // And the player's own feet. Positional like anyone else's, and placed where the
            // foot landed rather than on the listener, because the sound belongs to the ground.
            if (player.TakeFootstep() is { } step)
            {
                audio.PlayFootstep(step);

                // The step is also a NOISE the AI hears: 0x463920's tail posts AIEVENT_WALK
                // (type 10, filter 0x46) with the ground material's own radius, unless the
                // walker is crouching -- sneaking really is silent to the AI. guard.qvm's WALK
                // branch is AIAction_SetCombat, so a guard that hears footsteps it cannot see
                // starts hunting.
                if (!player.IsCrouched)
                {
                    mission.Soldiers?.NotifyWalk(
                        step.PositionInUnits, player.Body.Position, step.HearingRadiusInUnits);
                }
            }

            host.PresentPlayerImpacts();

            // Whatever grenades ran out of fuse this tick -- the blast, the flash, the noise
            // and the casualties.
            blasts.ResolveGrenadeDetonations();
            blasts.ResolveMissileDetonations();
            blasts.ResolveM203Detonations();
            audio.PlayGrenadeBounces();
            audio.SoundMissiles();

            // The alarm speakers, following their own level expressions -- the siren the
            // room hears when a guard reaches a switch.
            blasts.TickDestructibles();
            blasts.TickMineFields();
            mission.SmokePlumes?.Tick();
            audio.SoundAlarms();
            mission.AlarmSpeakers?.RunLights();

            // And whether one of this tick's rounds landed ON a speaker, pane or barrel. The
            // impact list carries both the instant rays and the ballistic arrivals, so either
            // mode can shoot the siren off the wall.
            blasts.ApplyPlayerRounds();

            // The level's ambience, which is keyed off where the ear is rather than where the
            // body stands: an area is a box you listen from the inside of.
            audio.TickAmbience(player.EyeUnits);
        }
        else if (mission.IsPlayerDead)
        {
            host.StepWorldEffects();
        }
    }

    // Tops the player's inventory up to the whole roster while the all-weapons cheat is in force. Runs
    // before every player tick, so it covers both mission start and switching the cheat on mid-mission;
    // a weapon already carried is left exactly as it is. Switching the cheat off takes nothing away.
    private void GrantCheatWeapons(PlayerCamera player)
    {
        if (!PlayerCheatOptions.Current.AllWeapons
            || mission.WeaponTypes is not { } roster
            || mission.AmmoTypes is not { } ammunition)
        {
            return;
        }

        int added = player.EnsureAllWeapons(roster.Weapons, ammunition);
        if (added == 0)
        {
            return;
        }

        host.Logger.Information("Player", $"All-weapons cheat added {added} weapon(s).");
        host.CheatWeaponsGranted();
    }

    /// <summary>Logs the player's first shot and every round that lands on a guard.</summary>
    private void ReportPlayerShots()
    {
        PlayerCamera player = mission.Player!;

        // went somewhere.
        if (!_reportedShot && player.FiredThisTick && player.HeldWeaponType is { } fired)
        {
            _reportedShot = true;
            string where = player.Impacts.Count == 0
                ? "nowhere"
                : player.Impacts[0].HitSomething
                    ? $"{player.Impacts[0].Position}"
                    : $"nothing inside {fired.EffectiveRange} m";

            host.Logger.Information(
                "Weapon",
                $"Fired {fired.ScriptId} — {GunTiming.TicksPerRound(fired.RoundsPerMinute)} tick(s) "
                    + $"a round at {fired.RoundsPerMinute} rpm, "
                    + $"{player.Weapons?.InHand?.Gun.RoundsInClip} left in the magazine, into {where}.");
        }

        // Every round that lands on a guard, reported once each so the zone and the kill are
        // visible without a per-shot log.
        if (player.HumanHits != _reportedHumanHits)
        {
            _reportedHumanHits = player.HumanHits;
            host.Logger.Information(
                "Weapon",
                $"Hit a guard in zone {player.LastHumanZone} "
                    + $"({_reportedHumanHits} hit(s), {mission.Soldiers?.Killed ?? 0} killed).");
        }
    }

    /// <summary>Walks the character the third-person view looks at with the player, on the same tick as their legs.</summary>
    private void FollowPlayerBody()
    {
        // The character the third-person view looks at, walked with the player on the same tick
        // their own legs are -- see SoldierRenderer.FollowPlayerBody for why it must be the tick
        // and not the frame. Kept running for the whole of the move back as well as out, so the
        // body is already in step whenever the camera is far enough away to show it.
        //
        // The states with a body of their own are left alone: the ladder and the world
        // interactions place and pose the same rig on their own schedule, and the death path owns
        // it outright once the player has fallen.
        if (mission.SoldierPresenter is { } followedBody)
        {
            // The same conditions the camera itself is eligible under, and deliberately so: the
            // follow owns the torso clip and the aim bend, and every state excluded here has an
            // authored full-body clip or a camera of its own that must own the whole character.
            if (host.BodyFollowAllowed
                    && !mission.IsPlayerDead
                    && mission.Cutscenes?.IsPlaying != true
                    && mission.Player is { IsOnLadder: false, IsInWorldInteraction: false } walkingBody)

                // ...and in FIRST person too, unconditionally, which it did not used to be.
                //
                // The follow is what POSES the body; the separate visibility latch below is what
                // draws it. Those were both held shut in first person, so the body did not exist
                // at all -- and geometry that does not exist cannot be struck by a reflected ray,
                // whatever the reflection is then able to do with the hit. A window could not
                // show the player because there was no player to show.
                //
                // So it is posed always and drawn as before. What that costs is one character
                // skinned per frame; what it buys is the player's own reflection and shadow,
                // through DrawPlayerBody's traced-only path.
            {
                followedBody.FollowPlayerBody(
                    walkingBody.Body.Position,

                    // MINUS the yaw. A body's heading is a GAMMA and the look is a YAW, and the
                    // two run opposite ways: PlayerBody.FacingBasis builds the body's own basis
                    // as RotationZ(-yaw), and every other caller in the port converts the same
                    // way (PlayerCamera writes `_body.Yaw = -facing` on entering an interaction
                    // and `_interactionFacing = -_body.Yaw` on leaving one). Passing the yaw
                    // straight through mirrors the character about the world's X axis, which is
                    // a body that faces the camera at exactly the moment it should face away.
                    -walkingBody.Body.Yaw,
                    walkingBody.LocomotionAnimation,
                    walkingBody.LocomotionAnimationLoops,
                    new PlayerBodyPose(
                        walkingBody.UpperBodyAnimation,
                        walkingBody.UpperBodyAnimationLoops,
                        walkingBody.UpperBodyBlendSeconds,
                        walkingBody.UpperBodySequence,
                        walkingBody.AimPitch));
            }
            else
            {
                followedBody.Simulation.HidePlayerBodyFollowing();
            }

            // A co-op session's partners, followed on the same tick the player's body is.
            if (mission.PartnerBodies is { } partners)
            {
                followedBody.FollowPartnerBodies(partners());
            }
        }
    }

    /// <summary>Forgets the shots already reported, as the player starts afresh.</summary>
    public void ResetReports()
    {
        _reportedShot = false;
        _reportedHumanHits = 0;
    }
}

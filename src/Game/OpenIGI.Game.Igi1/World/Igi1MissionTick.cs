using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Input;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What the mission's tick asks of whatever shows and plays it: the camera it may be looking through, and the steps that are picture and sound.</summary>
public interface IIgi1MissionHost
{
    /// <summary>Gets a value indicating whether the player walks the level, rather than the free camera flying it.</summary>
    bool PlayerMode { get; }

    /// <summary>Gets where the free camera is, in metres, or <see langword="null"/> when there is none.</summary>
    Vector3d? FreeCameraPosition { get; }

    /// <summary>Gets the level's built scene, whose placements the cutscenes move.</summary>
    WorldScene? Scene { get; }

    /// <summary>Gets the soldiers as they are drawn, whose rigs close each tick.</summary>
    ISoldierPresenter? SoldierPresenter { get; }

    /// <summary>Steps the weather, which falls whatever the player is doing.</summary>
    void StepWeather();

    /// <summary>Moves the lifts' sounds to where the lifts now are.</summary>
    void ElevatorsMoved();

    /// <summary>Plays the vehicles' engines and guns as the cutscene runtime now has them.</summary>
    /// <param name="cutscenes">The cutscene runtime.</param>
    void SoundVehicles(CutsceneRuntime cutscenes);

    /// <summary>Runs the player's update, for a level whose walk holds no player task.</summary>
    void UpdatePlayer();

    /// <summary>Plays and reports what the player's update set in motion this tick.</summary>
    void PresentPlayerTick();

    /// <summary>Steps the screen's own effects: the damage wash, the flashbang's white-out and the HUD.</summary>
    void StepScreen();

    /// <summary>Applies the test-only writes that hold an outro's gates open.</summary>
    void ApplyTestWrites();

    /// <summary>Points the death camera at where the player's body has fallen.</summary>
    /// <param name="centre">The body's centre.</param>
    void FollowCorpse(Vector3d centre);

    /// <summary>Gets the keys and buttons held this tick.</summary>
    InputState Input { get; }

    /// <summary>Gets the active profile, whose bindings the player's keys are read through.</summary>
    PlayerProfile? InputProfile { get; }

    /// <summary>Gets the ground the player's feet stand on.</summary>
    IGroundHeightField? Ground { get; }

    /// <summary>Gets a value indicating whether the orbit behind a climbing or interacting body has the view.</summary>
    bool IsInteractionViewActive { get; }

    /// <summary>Hands the view to the orbit behind a climbing or interacting body.</summary>
    /// <param name="feet">The body's feet, in world units.</param>
    /// <param name="facing">The yaw the body faces.</param>
    /// <param name="walls">What pulls the orbit in, or <see langword="null"/>.</param>
    void BeginInteractionView(Vector3d feet, float facing, WorldRayCast? walls);

    /// <summary>Moves the orbit's pivot after the body.</summary>
    /// <param name="feet">The body's feet, in world units.</param>
    void FollowInteractionView(Vector3d feet);

    /// <summary>Eases the orbit once on the tick.</summary>
    void TickInteractionView();

    /// <summary>Takes the view back from the orbit.</summary>
    void ResetInteractionView();

    /// <summary>Runs what closes the tick: the body the camera follows and the mission's outcome.</summary>
    void CloseTick();
}

/// <summary>
/// One pass of Project I.G.I.'s mission: the lifts, the level's walk, the player, the cameras, the spinning objects and
/// the soldiers, in the order the port runs them each tick.
/// </summary>
/// <param name="mission">The mission's world.</param>
/// <param name="host">What shows and plays it.</param>
public sealed class Igi1MissionTick(MissionWorld mission, IIgi1MissionHost host)
{
    // Whether the player's ladder climb or world interaction has begun its body animation, and whether the
    // press that mounted the ladder has been let go, so that it does not become the slide on the next tick.
    private bool _climbActive;
    private bool _climbSlideArmed;
    private bool _worldInteractionActive;

    /// <summary>Forgets a climb or an interaction in progress, as a new mission starts.</summary>
    public void ResetClimbs()
    {
        _climbActive = false;
        _climbSlideArmed = false;
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
            host.UpdatePlayer();
        }

        if (mission.Cutscenes?.IsPlaying != true && host.PlayerMode && mission.Player is not null)
        {
            host.PresentPlayerTick();
        }

        // PlayerCamera owns the normal player tick, including interaction pulses. Other fixed-tick
        // modes (free camera, cutscenes, map computer and a dead player) still need their level
        // interactions to advance, but never more than once per simulation tick.
        if (!mission.InteractionsTickedByPlayer)
        {
            mission.Interactions?.Tick();
        }

        EndTick();
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
        bool body = host.PlayerMode && mission.Player is not null;
        Vector3d eye = body ? mission.Player!.Body.Eye : host.FreeCameraPosition ?? mission.Player?.Body.Eye ?? default;
        Vector3d feet = body ? mission.Player!.Body.Position : eye;
        return new SoldierEnemy(eye, !mission.IsPlayerDead && mission.Cutscenes?.IsPlaying != true, feet, body && mission.Player!.IsCrouched);
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
            AreaOccupancy.AreaActivationOccupant(mission.Player?.Body.Origin, cutsceneView, freeView),
            mission.Soldiers?.PlayerTeam ?? 0,
            AreaOccupancy.AreaActivationExtraOccupant(mission.Player?.Body.Origin, cutsceneView),
            !mission.IsPlayerDead);
    }

    // Whether the player's body is what the world sees, asked afresh each time as the tick may change it.
    private bool Walking() => host.PlayerMode && mission.Player is not null;

    private void BeginTick()
    {
        // The storm steps here rather than inside the player's own block, where the impact
        // particles step: weather is ambient, and it has to keep falling in the free camera and
        // while the player is down.
        host.StepWeather();

        // The lifts step ahead of the walk, so they still move before the doors that ride them and before
        // the player they carry; they do not yet update at their own place in the tree.
        if (mission.Elevators is { } elevators)
        {
            // Feed the player's current position to the elevators so their start gate can refuse
            // to move an empty car or one with an open door. Provider is read every tick.
            elevators.SetPlayerPositionProvider(mission.Player is { } p ? () => p.Body.Position : null);
            elevators.Tick();
            host.ElevatorsMoved();
            if (mission.Solids is { } elevatorSolids)
            {
                elevators.ApplyCollision(elevatorSolids);
            }

            if (host.PlayerMode && mission.Player is { } passenger
                && elevators.TryGetPassengerDisplacement(
                    passenger.Body.Position, mission.Solids, out Vector3d displacement))
            {
                passenger.Body.Position += displacement;
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
            AreaOccupancy.AreaActivationOccupant(mission.Player?.Body.Origin, areaCutsceneView, areaFreeView),
            mission.Soldiers?.PlayerTeam ?? 0,
            AreaOccupancy.AreaActivationExtraOccupant(mission.Player?.Body.Origin, areaCutsceneView),
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
        host.StepScreen();

        // The player's eye is what the guards' view cones are tested against -- and in the
        // fly-through it is the CAMERA's, not the body's. A shell affordance, not engine behaviour.
        Vector3d watched = Walking()
            ? mission.Player!.Body.Eye
            : host.FreeCameraPosition ?? mission.Player?.Body.Eye ?? default;

        // A dead player is no longer a target: the guards lose it and stop firing at the corpse, the
        // way the retail detection scan (0x489750) unregisters a human that has died.
        //
        // The eye is what vision watches; the FEET are what combat aims at (sub_451380 raises the
        // target task's position 0.5 m, 0.25 m crouched) -- in the fly-through there is no body, so
        // the camera stands in for both.
        Vector3d watchedFeet = Walking() ? mission.Player!.Body.Position : watched;

        // The areas and edit variables ran inside the level walk above, before the AlarmControls that
        // consume them (Mission 1's alarm latch depends on that order).
        host.ApplyTestWrites();
        mission.Cutscenes?.FinishExpressionTick();

        // The level's fans and radar heads, one tick further round. Unconditional and cheap: it
        // is a counter, and the renderer turns the transform from it when it draws.
        mission.Spins?.Advance();
        if (mission.SecurityCameras is { } cameras)
        {
            bool crouching = Walking() && mission.Player!.IsCrouched;

            // Retail's lenses scan every human and every camera (0x489750): a guard's body or a
            // destroyed camera in view raises the alarm at once, as the player does after 90 ticks.
            cameras.Tick(
                watchedFeet,
                crouching,
                targetAlive: !mission.IsPlayerDead,
                playerOnCamerasSide: (mission.Soldiers?.PlayerTeam ?? 0) == 1,
                mission.Soldiers?.CameraSubjects() ?? []);
            if (mission.Solids is { } cameraSolids)
            {
                foreach (SecurityCamera camera in cameras.Cameras)
                {
                    Vector3d headMetres = new(
                        camera.HeadPositionUnits.X / WorldScene.UnitsPerMetre,
                        camera.HeadPositionUnits.Y / WorldScene.UnitsPerMetre,
                        camera.HeadPositionUnits.Z / WorldScene.UnitsPerMetre);
                    cameraSolids.UpdateTransform(
                        camera.Task,
                        camera.HeadModelName,
                        headMetres,
                        camera.Orientation);
                }
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

        ISoldierPresenter? soldiers = host.SoldierPresenter;
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
                enemyCrouching: Walking() && mission.Player!.IsCrouched);
        }

        // The player's own body advances its death fall on the tick, the same as a soldier's corpse --
        // it does nothing until the player has died and the body was posed.
        soldiers?.TickPlayerBody();

        // A simulated corpse decides for itself where it ends up, so the death camera has to
        // follow it. Retail needs no such thing: its fall is authored, and aiming at the spot
        // you were shot frames the whole of it.
        if (mission.IsPlayerDead && soldiers?.PlayerRagdollCentre is { } corpseCentre)
        {
            host.FollowCorpse(corpseCentre);
        }

        TickClimbsAndInteractions();
        host.CloseTick();
    }

    /// <summary>Advances the ladder and the world interactions on the fixed tick, and the camera that frames them.</summary>
    private void TickClimbsAndInteractions()
    {
        ISoldierPresenter? soldiers = host.SoldierPresenter;
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
                    soldiers?.StartPlayerLadderAnimation(
                        PlayerCamera.LadderClimbAnimation, loops: true, paused: true);
                }
            }

            if (!climbUse)
            {
                _climbSlideArmed = true;
            }

            bool climbSlide = climbUse && _climbSlideArmed;
            LadderStepResult decision = mission.Player.DecideLadderStep(
                climbUp, climbDown, climbSlide, host.Ground!);
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
                            out bool rungBoundary,
                            out _);
                        mission.Player.MoveLadder(climbDelta);
                        if (rungBoundary)
                        {
                            mission.Player.CompleteLadderStep();
                            soldiers.Simulation.SetPlayerLadderPlaybackDirection(0);
                        }

                        break;

                    case LadderTraversalPhase.GettingOnTop:
                    case LadderTraversalPhase.GettingOffTop:
                        soldiers.AdvancePlayerLadderAnimation(
                            mission.Player.LadderFacing,
                            out Vector3d topDelta,
                            out _,
                            out bool topAnimationEnded);
                        mission.Player.MoveLadder(topDelta);
                        if (topAnimationEnded)
                        {
                            mission.Player.CompleteLadderTopTransition(host.Ground!);
                            if (mission.Player.LadderPhase == LadderTraversalPhase.Climbing)
                            {
                                soldiers.StartPlayerLadderAnimation(
                                    PlayerCamera.LadderClimbAnimation, loops: true, paused: true);
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
                soldiers.AdvancePlayerLadderAnimation(
                    interacting.InteractionFacing,
                    out Vector3d interactionRootDelta,
                    out _,
                    out _);
                interacting.ApplyInteractionRootMotion(interactionRootDelta);
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
            _worldInteractionActive = false;
        }
    }
}

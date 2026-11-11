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
/// <param name="harm">What hurts a player of the mission and how it dies.</param>
/// <param name="blasts">What resolves the blasts the player's update set off.</param>
/// <param name="audio">The mission heard, whose steps run at their points in the tick.</param>
/// <param name="host">What shows and plays it.</param>
public sealed partial class Igi1MissionTick(MissionWorld mission, Igi1PlayerHarm harm, Igi1Blasts blasts, Igi1MissionAudio audio, IMissionTickHost host) : IMissionTick,
    OpenIGI.Game.Level.ITaskRun<HumanTask>, OpenIGI.Game.Level.ITaskRun<ElevatorTask>
{
    // Partners' guns are voiced below every soldier's runtime id, which counts up from zero.
    private const int PartnerVoiceBase = -1_000_000;

    private bool _reportedShot;
    private int _reportedHumanHits;

    // The mission's own player's ladder, climbed on the body the frame draws.
    private readonly LadderClimb _ownClimb = new();

    // Whether the player's world interaction has begun its body animation.
    private readonly InteractionMotion _ownInteraction = new();

    /// <summary>Forgets a climb or an interaction in progress, as a new mission starts.</summary>
    public void ResetClimbs()
    {
        _ownClimb.Reset();
        _ownInteraction.Reset();
    }

    /// <inheritdoc/>
    public void BreakIntoOutro()
    {
        if (mission.Cutscenes is not { } cutscenes || mission.Objectives is not { } objectives)
        {
            throw new InvalidOperationException("The mission's cut-scenes and objectives are not built yet.");
        }

        cutscenes.RequestBreak();
        cutscenes.Tick();
        objectives.TickLevelState();
        objectives.TickAfterLevelState();
        cutscenes.FinishExpressionTick();
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

        // The partners, when the mission's own player did not run them this tick.
        RunPartners();

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
                best = new SoldierEnemy(other.Player.Body.Eye, true, other.Player.Body.Position, other.Player.IsCrouched, other);
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

    /// <summary>Gets the level's other players as its areas count them: where each stands, and whether it is up.</summary>
    /// <returns>One per partner.</returns>
    public IEnumerable<AreaPlayer> PartnerAreaPlayers()
    {
        int team = mission.Soldiers?.PlayerTeam ?? 0;
        foreach (Multiplayer.Igi1NetworkPlayer partner in mission.Partners)
        {
            yield return new AreaPlayer(partner.Player.Body.Origin, team, null, !partner.IsDead);
        }
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
        mission.PartnersTicked = false;

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
            playerAlive: !mission.IsPlayerDead || mission.CoopClient || mission.NetworkPlayers.Any(static p => !p.IsDead));
        mission.PlayerTicked = false;
        mission.InteractionsTickedByPlayer = false;
        if (mission.SoldiersInWalk)
        {
            mission.Soldiers?.BeginTick(mission.CurrentEnemy ??= SoldierEnemyNow);
        }

        mission.Cutscenes?.Tick();
        if (mission.Cutscenes is { } soundingVehicles)
        {
            host.SoundVehicles(
                soundingVehicles.Vehicles.VehicleSoundSources().Concat(mission.RotatingObjects?.SoundSources() ?? []),
                soundingVehicles.Vehicles.VehicleGunSounds());
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
        RunModsAtTickEnd();

        // The damage wash fades a forty-fifth a tick whatever the frame rate -- decayed here, on the
        // fixed tick, for the same determinism reason the bodies are.
        mission.PlayerLife.DamageFlash.Tick();
        mission.PlayerLife.FlashbangFlash.Tick();
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
            cameras.Tick(CameraScanNow());
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

        // The death camera eases and collides on this tick too (HumanCamera mode 5), so it is fed every tick. It orbits the
        // human's own eye -- Human_GetEyePos (0x465820), the origin raised 2826.24, read through HumanView_GetEye -- not a
        // simulated corpse: a ragdoll wedged against a tunnel wall carried the orbit out of the room, and from there the
        // sector cull drew nothing but fog.
        // A co-op player who is down sees through a teammate still standing instead: its eye, where it looks.
        if (mission.IsPlayerDead && mission.Player is { } fallen)
        {
            if (WatchedPartner(fallen) is { } teammate)
            {
                host.FollowTeammate(teammate.Player.Body.Eye, teammate.Player.Body.Yaw, teammate.Player.AimPitch);
            }
            else
            {
                Vector3d feet = fallen.Body.Position;
                host.FollowCorpse(new Vector3d(feet.X, feet.Y, feet.Z + (PlayerBody.StandingEyeHeight * 0.5)));
            }
        }

        TickClimbsAndInteractions();
        FollowPlayerBody();
        host.CloseTick();
    }

    // Which of the teammates still standing a downed co-op player watches, fire stepping on to the next.
    private int _watching;
    private bool _watchStepHeld;

    // Whom a downed co-op player watches: a teammate still standing, or nobody when none is.
    private Multiplayer.Igi1NetworkPlayer? WatchedPartner(PlayerCamera fallen)
    {
        List<Multiplayer.Igi1NetworkPlayer> standing = [.. mission.Partners.Where(static p => !p.IsDead)];
        if (standing.Count == 0)
        {
            return null;
        }

        bool step = fallen.IsActionDown(host.Input, PlayerAction.Fire);
        if (step && !_watchStepHeld)
        {
            _watching++;
        }

        _watchStepHeld = step;
        return standing[(_watching % standing.Count + standing.Count) % standing.Count];
    }

    // A co-op session's other players, once a tick: after the mission's own when it runs, and by themselves when it is down,
    // in a cut-scene or at the computer, so a partner never stands frozen because the host's player is.
    private void RunPartners()
    {
        if (mission.PartnersTicked)
        {
            return;
        }

        mission.PartnersTicked = true;
        foreach (Multiplayer.Igi1NetworkPlayer other in mission.NetworkPlayers)
        {
            // The same jump rule as the mission's own player, so the partner does what its client predicted in a lift.
            other.Player.CanJump = mission.Elevators is { } lifts
                ? () => !lifts.IsPlayerInsideElevator(other.Player.Body.Position, out _)
                : null;
            // Its rounds are its own in the server's record, so the client that fired them doesn't show them twice.
            mission.ImpactSource = other.Entity;
            other.CommandRan ??= ResolvePartnerCommand;
            other.Run(mission.PlayerTicks, host.Ground, LadderRung);
            mission.PartnerRan?.Invoke(other);
            mission.ImpactSource = 0;
        }

        mission.PlayerTicks++;
    }

    // What one of a partner's commands set off, resolved as the mission's own player's is: the soldiers hear its throws and
    // blasts, its grenades, mines, rockets and launcher rounds go off, and its rounds reach the panes, barrels, speakers,
    // cameras and switches they struck. Each command's, since the next clears them; the soldiers stand where they are now.
    private void ResolvePartnerCommand(Multiplayer.Igi1NetworkPlayer partner)
    {
        PlayerCamera rig = partner.Player;
        if (mission.Soldiers is { } soldiers)
        {
            NotifyGrenadeAiEvents(rig, soldiers);
        }

        blasts.ResolveDetonations(rig);
        blasts.ApplyRounds(rig);
        audio.PlayGrenadeBounces(rig);
        blasts.RecordBounces(rig);
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

        // A mod's prompt lasts the tick it is offered on: dead, in a cutscene or at the map computer, there is none.
        if (!mission.PlayerTicked)
        {
            mission.OfferedPrompt = null;
        }

        if (mission.PlayerTicked || mission.Cutscenes?.IsPlaying == true || !host.PlayerMode || !CanStepPlayer)
        {
            return;
        }

        mission.PlayerTicked = true;
        StepPlayer(player => player.Tick(host.ViewInput, host.Ground));
    }

    /// <summary>
    /// The mission's part of its player's tick, around the rig's own (<paramref name="tick"/>): what the rig meets is published
    /// first, and what its walk set in motion -- the map computer, the vehicles' rockets, being run over, the grenades' AI
    /// events -- follows it, whether the mission ticks its player or the host does on its title path.
    /// </summary>
    /// <param name="tick">The rig's tick, with the input it is given.</param>
    /// <returns><see langword="false"/> where the player does not tick: none stands, it is dead, or the mission has failed.</returns>
    /// <remarks>
    /// Whoever drives the tick -- the mission, or the host on its title path -- has taken it for this tick already
    /// (<see cref="MissionState.PlayerTicked"/>), in player mode and out of a cutscene.
    /// </remarks>
    public bool StepPlayer(Action<PlayerCamera> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (!CanStepPlayer || mission.Player is not { } player)
        {
            return false;
        }

        // The soldiers ahead of the player in the tree have moved this tick; the wall sweep meets them there.
        mission.Soldiers?.PublishCollisionBodies();
        player.InputProfile = host.InputProfile;
        GrantCheatWeapons(player);
        if (host.ComputerOpen)
        {
            TickMapComputer(player);

            // The computer is a task under the player that pauses nothing (Computer_Create, 0x46BB10): what its weapons put
            // in the world is the level's own (Level_GetDynamic), and flies, rests and goes off while the map is up.
            player.TickProjectilesAlone();
        }
        else
        {
            RunModsBeforePlayer(player);
            tick(player);

            // PlayerCamera advances its interaction registry before it resolves this tick's activate target.
            // Do not advance the same registry again after the walk.
            mission.InteractionsTickedByPlayer = true;
            OpenMapComputer(player);
        }

        // A co-op session's other players, each on its client's command for this tick, after the mission's own.
        RunPartners();
        mission.TickVehicleMissiles();
        CheckRunOver();

        // Grenade physics runs inside PlayerCamera.Tick. The grenade's AI events reach the soldiers' next
        // update, as retail's grenade task -- a runtime task at the end of Dynamic, after every soldier -- does.
        NotifyGrenadeAiEvents();
        return true;
    }

    // Whether there is a living player to step, and no failure screen over it.
    private bool CanStepPlayer => mission.Player is not null && !mission.IsPlayerDead && !host.FailedMenu;

    /// <summary>Kills the player when a moving vehicle's death zone reaches them (<c>0x45FA80</c>).</summary>
    // Every player a vehicle drives through is run over, a co-op partner as the player is.
    private void CheckRunOver()
    {
        if (mission.Cutscenes is not { } runtime)
        {
            return;
        }

        foreach (IHarmedPlayer player in harm.Players)
        {
            if (player.Rig is not { } rig || player.IsDead)
            {
                continue;
            }

            Vector3d origin = rig.Body.Position + new Vector3d(0d, 0d, Igi1HumanAi.HumanOriginStandingInUnits);
            if (!runtime.Vehicles.TryRunOver(origin, out GameTask? vehicle) || vehicle is null)
            {
                continue;
            }

            player.Health.Apply(VehicleDeathZone.Zone, VehicleDeathZone.Damage);
            host.Logger.Information("Ai", $"A player was run over by {vehicle.Type.Name} {vehicle.Id}.");
            if (player.Health.IsDead)
            {
                Vector3d from = runtime.TryGetDrawTransform(vehicle, out Vector3d metres, out _)
                    ? metres * WorldScene.UnitsPerMetre
                    : origin;
                harm.BeginPlayerDeath(player, from, VehicleDeathZone.Zone, bone: -1, WeaponCategory.Knife);
            }
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

        NotifyGrenadeAiEvents(mission.Player, soldiers);
        foreach (MissileDetonation blast in mission.VehicleMissiles.Detonations)
        {
            soldiers.NotifyExplosion(blast.Position, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        }
    }

    // One player's throws, bounces, mine triggers and blasts, as the soldiers hear them.
    private void NotifyGrenadeAiEvents(PlayerCamera rig, SoldierSimulation soldiers)
    {
        foreach (Vector3d origin in rig.Grenades.Thrown)
        {
            soldiers.NotifyGrenadeThrown(origin);
        }

        rig.Grenades.ClearThrownEvents();

        foreach (Vector3d origin in rig.Grenades.Bounces)
        {
            soldiers.NotifyGrenadeLand(origin);
        }

        // IGI1's proximity task emits proxy_arm on the first human entering its activation radius.
        // This is a one-shot rising-edge cue, not the mine's placement/arming sound.
        foreach (Vector3d origin in rig.Grenades.MineTriggers)
        {
            audio.PlayMineArm(origin);
            blasts.RecordMineArmed(origin);
        }

        rig.Grenades.ClearMineTriggerEvents();

        foreach (GrenadeDetonation blast in rig.Grenades.Detonations)
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
        foreach (PlacedMine mine in rig.Grenades.MineBlasts)
        {
            soldiers.NotifyExplosion(mine.BlastPoint, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        }

        foreach (MissileDetonation blast in rig.Missiles.Detonations)
        {
            soldiers.NotifyExplosion(blast.Position, Igi1HumanAi.LargeExplosionEventRadiusInUnits);
        }
        foreach (M203Detonation blast in rig.M203.Detonations)
        {
            soldiers.NotifyExplosion(blast.Position, blast.RadiusUnits);
        }
    }

    /// <inheritdoc/>
    /// <remarks>The player's update, then its children, where the level's walk reaches the player's task.</remarks>
    void OpenIGI.Game.Level.ITaskRun<HumanTask>.Run(HumanTask task, OpenIGI.Game.Level.LevelRun level)
    {
        UpdatePlayer();
        level.RunChildren(task);
    }

    /// <inheritdoc/>
    /// <remarks>A lift's update (<c>0x43D280</c>): its children first, then its own floor expressions and its move.</remarks>
    void OpenIGI.Game.Level.ITaskRun<ElevatorTask>.Run(ElevatorTask task, OpenIGI.Game.Level.LevelRun level)
    {
        level.RunChildren(task);
        if (mission.Elevators is { } elevators && task.Elevator is { } elevator)
        {
            StepElevator(elevators, elevator);
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
                if (OpenIGI.Game.Level.LevelTaskClass.TryAs(elevator.Task, walker, out ElevatorTask? lift))
                {
                    lift.Runner = this;
                    lift.Elevator = elevator;
                }
            }

            // Lifts of a level this walk only passes over are stepped ahead of it, as for a level the walk does not reach.
            mission.ElevatorsInWalk = walker.OwnsLevel;
        }

        // The switches, terminals, cabinets, fences, wires, GenericTBAs, stationary guns and generators update where the
        // walk reaches them, each in its own update's order -- before the cameras bind, whose controls extend a terminal's.
        if (mission.Interactions is { } interactions)
        {
            // The radios chatter from the walk, so their sounds have somewhere to go from its first tick.
            interactions.RadioSounds = audio.RadioSounds;
            interactions.AttachTo(walker);
        }

        // An ExplodeObject, and the types built on one, read their explosion where the walk reaches them (0x481970).
        if (walker.OwnsLevel)
        {
            mission.Destructibles?.AttachToWalk();
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
            // A level this walk only passes over is its own title's to run, and so is its player's task: the mission's own
            // per-tick player work is then done after the walk, as for a level with no player task in it.
            if (walker.OwnsLevel && OpenIGI.Game.Level.LevelTaskClass.TryAs(playerTask, walker, out HumanTask? human))
            {
                human.Runner = this;
                mission.PlayerInWalk = true;
            }
        }
    }

    // What a lens scans as it updates (0x489750): every human and every camera, so a guard's body or a destroyed camera
    // in view raises the alarm at once, as the player does after 90 ticks. In the fly-through the free camera stands in
    // for the player's feet. A co-op session's other players are humans too, seen as the player is: the detector
    // registers every human, and a camera that knew only the host's player let a joiner walk past it.
    private SecurityCameraScan CameraScanNow()
    {
        bool walking = Walking();
        Vector3d feet = walking
            ? PlayerFeet
            : host.FreeCameraPosition ?? PlayerEyeIfAny ?? default;
        bool camerasSide = (mission.Soldiers?.PlayerTeam ?? 0) == 1;
        return new SecurityCameraScan(
            feet,
            walking && PlayerCrouched,
            TargetAlive: !mission.IsPlayerDead,
            PlayerOnCamerasSide: camerasSide,
            mission.Soldiers?.CameraSubjects() ?? [],
            PartnerCameraSubjects(camerasSide));
    }

    // The partners as a lens scans them, each where its body stands now.
    private List<SecurityCameraSubject>? PartnerCameraSubjects(bool camerasSide)
    {
        if (mission.NetworkPlayers.Count == 0)
        {
            return null;
        }

        _partnerSubjects.Clear();
        foreach (Multiplayer.Igi1NetworkPlayer partner in mission.NetworkPlayers)
        {
            _partnerSubjects.Add(SecurityCameraScan.PlayerSubject(
                partner, partner.Player.Body.Position, partner.Player.IsCrouched, camerasSide, alive: !partner.IsDead));
        }

        return _partnerSubjects;
    }

    private readonly List<SecurityCameraSubject> _partnerSubjects = [];

    // A camera's head collides where its update left it.
    // A rung the mission's own player crossed, sounded where it stands.
    private void LadderRung(PlayerCamera player) => audio.PlayLadderStepSound(player.TakeLadderStep(), player.LadderFeet);
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

    // What the player's update set in motion this tick -- its sounds, effects and detonations -- in the order the port has
    // always resolved them, the host's sounds and pictures at their places among the simulation's own steps: the sounds
    // draw from the same generator the blasts and the shatters do.
    private void ResolvePlayerTick()
    {
        PlayerCamera player = mission.Player!;
        if (mission.PlayerTicked)
        {
            audio.SoundPlayerUpdate();

            Igi1PlayerHearing.Gunshot(player, mission.Soldiers);

            audio.SoundPlayerWeaponAndBody();

            // And the player's own feet. Positional like anyone else's, and placed where the
            // foot landed rather than on the listener, because the sound belongs to the ground.
            if (player.TakeFootstep() is { } step)
            {
                audio.PlayFootstep(step);

                // The step is also a NOISE the AI hears (Igi1PlayerHearing.Step).
                Igi1PlayerHearing.Step(player, step, mission.Soldiers);
            }

            host.PresentPlayerImpacts();

            // Whatever grenades ran out of fuse this tick -- the blast, the flash, the noise
            // and the casualties.
            blasts.ResolveGrenadeDetonations();
            blasts.ResolveMissileDetonations();
            blasts.ResolveM203Detonations();
            audio.PlayGrenadeBounces();
            blasts.RecordBounces(player);
            audio.SoundMissiles();

            // The alarm speakers, following their own level expressions -- the siren the
            // room hears when a guard reaches a switch.
            blasts.TickDestructibles();
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
            StepLevelWhileDown(player);
        }
    }

    // The level runs on while the mission's own player lies dead -- through the death camera alone, for a teammate's revive
    // in co-op: the grenades, mines, rockets and launcher rounds are the level's own tasks (Grenade_Throw, Mine_New,
    // Missile_New and M203_New all make them under Level_GetDynamic), the soldiers' lobs among them, which fly in this
    // player's pool; then the vehicles' rockets, the fuses and wrecks of what is burning, the minefields, the smoke, the
    // sirens and the alarm lights.
    private void StepLevelWhileDown(PlayerCamera player)
    {
        player.TickProjectilesAlone();
        mission.TickVehicleMissiles();
        NotifyGrenadeAiEvents();
        blasts.ResolveGrenadeDetonations();
        blasts.ResolveMissileDetonations();
        blasts.ResolveM203Detonations();
        audio.PlayGrenadeBounces();
        blasts.RecordBounces(player);
        audio.SoundMissiles();
        blasts.TickDestructibles();
        mission.SmokePlumes?.Tick();
        audio.SoundAlarms();
        mission.AlarmSpeakers?.RunLights();
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

            // A co-op session's partners, followed on the same tick the player's body is -- and kept out of sight while a
            // scene plays, whose own actors stand for the players as they stand for the player alone: the outro's Jones at
            // the wheel, not a partner running at the truck.
            if (mission.PartnerBodies is { } partners)
            {
                followedBody.HidePartnerBodies(mission.Cutscenes?.IsPlaying == true);
                IReadOnlyList<PartnerBody> bodies = partners();
                followedBody.FollowPartnerBodies(bodies);

                // Their guns heard as a soldier's are, each on a voice of its own.
                foreach (PartnerBody partner in bodies)
                {
                    ShowPartnerShot(partner);
                    audio.TickSoldierWeaponSound(PartnerVoiceBase - (partner.Key.GetHashCode() & 0xFFFFFF), partner.Fired, partner.Weapon, partner.Feet);
                }
            }

            // A client's soldiers fire on the server; here their rounds are only lines through the air.
            if (mission.CoopClient)
            {
                ShowPuppetShots();
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

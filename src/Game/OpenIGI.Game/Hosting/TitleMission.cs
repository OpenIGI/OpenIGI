using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>
/// The mission of a title whose level runs itself behind its player: the host's steps around that player's tick, and none of
/// another game's systems.
/// </summary>
/// <remarks>
/// A title that keeps its whole simulation behind <see cref="ITitleGameplay.CreatePlayer"/> needs of the host only what
/// falls whatever is played -- the weather, the world's effects, the screen, the tick's close. It brings no level systems,
/// no player of the mission's own, no voices and no impact effects of its own; its player draws and sounds what it does.
/// </remarks>
/// <param name="host">What the host gives a mission.</param>
/// <param name="gameplay">The title's own gameplay, which makes its player.</param>
public sealed class TitleMission(IMissionHostServices host, ITitleGameplay gameplay) : IHostedMission
{
    private readonly ITitleGameplay _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
    private readonly Parts _parts = new(host ?? throw new ArgumentNullException(nameof(host)));

    /// <inheritdoc/>
    public MissionState World => _parts.State;

    /// <inheritdoc/>
    public IMissionTick Tick => _parts;

    /// <inheritdoc/>
    public IMissionSetup Setup => _parts;

    /// <inheritdoc/>
    public IMissionAudio Audio => _parts;

    /// <inheritdoc/>
    public IMissionCries Sounds => _parts;

    /// <inheritdoc/>
    public IMissionImpacts Impacts => _parts;

    /// <inheritdoc/>
    public IReadOnlyList<TitleInputBinding> InputBindings => _gameplay.InputBindings;

    /// <inheritdoc/>
    public ITitlePlayer? CreatePlayer(in TitlePlayerRequest request) => _gameplay.CreatePlayer(request);

    /// <inheritdoc/>
    public void Prefetch()
    {
    }

    /// <inheritdoc/>
    public IPlayerBringUp NewPlayerSetup() => new NoPlayer(_parts.Logger);

    /// <inheritdoc/>
    /// <remarks>
    /// One step, at the place a game's cut-scenes are brought up: the level the mission holds, and the mission's text,
    /// which the host's own steps after it read.
    /// </remarks>
    public void AddMissionSystems(LevelSystems<MissionLevelBuild> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);
        systems.InsertBefore(MissionSystemNames.TestCaptures, new LevelSystem<MissionLevelBuild>(MissionSystemNames.Cutscenes, b =>
        {
            World.LevelRoot = b.LevelRoot;
            IMissionSetupHost setup = _parts.Host;
            b.MissionText = new MissionStringResolver(setup.Resources, new MenuStringTable(setup.Resources));
        }));
    }

    // The tick and the parts a title of this kind has nothing of.
    private sealed class Parts(IMissionHostServices host) : IMissionTick, IMissionSetup, IMissionAudio, IMissionCries, IMissionImpacts
    {
        public MissionState State { get; } = new();

        public IMissionHostServices Host => host;

        public IOpenIgiLogger Logger => ((IMissionSetupHost)host).Logger;

        public bool HasSoldiers => false;

        public void Run(bool tick)
        {
            if (!tick)
            {
                return;
            }

            State.PlayerTicked = false;
            State.InteractionsTickedByPlayer = false;
            host.StepWeather();
            bool stands = host.TickTitlePlayer();

            // The world's effects are tasks that run whatever is watching: stepped with the player's tick, under its death
            // camera, in a cut-scene and under the free camera, and held only while a living player's own tick is held.
            bool walking = stands && host.PlayerMode && State.Scenes?.IsPlaying != true;
            if (!walking || State.PlayerTicked || State.IsPlayerDead)
            {
                host.StepWorldEffects();
            }

            host.StepScreen();
            host.ApplyTestWrites();
            State.Spins?.Advance();
            host.CloseTick();
        }

        public void ResetReports()
        {
        }

        public void ResetClimbs()
        {
        }

        public void BreakIntoOutro()
        {
        }

        public void OnDeviceOpened()
        {
        }

        public void ResetForMission()
        {
        }

        public void ForgetCameras()
        {
        }

        public void ForgetSirens()
        {
        }

        public void PlayPlayerImpacts()
        {
        }

        public void PlayRicochet(Vector3d position)
        {
        }

        public void Reset(int mistFrames)
        {
        }

        public void ConnectSoldiers()
        {
        }

        public void Tick()
        {
        }

        public void SpawnPlayerImpacts()
        {
        }

        public void Register(ConsoleSystem console, Func<Camera3D> eye, IOpenIgiLogger logger)
        {
        }
    }

    // The bring-up of a player the mission does not have: the title's own stands instead.
    private sealed class NoPlayer(IOpenIgiLogger logger) : IPlayerBringUp
    {
        public WeaponTypeRegistry Weapons { get; } = new(logger);

        public BoneAnimationSet? BodyAnimations => null;

        public void Begin(LoadedMission loaded, Engine.Tasks.LevelTask levelRoot, ObjectCollisionIndex solids, MissionStringResolver missionText)
        {
        }

        public void ArmVehicles(LoadedMission loaded, Engine.Tasks.LevelTask levelRoot)
        {
        }

        public void Configure()
        {
        }

        public bool UseViewRig() => false;

        public bool CarryDeclaredLoadout(Engine.Tasks.LevelTask levelRoot, out bool carried)
        {
            carried = false;
            return false;
        }

        public void ConnectSoldiers(Action<Vector3d> deflected)
        {
        }

        public bool Stand(
            Engine.Tasks.LevelTask levelRoot,
            IGroundHeightField? ground,
            ObjectCollisionIndex solids,
            Vector3d? opening,
            float openingYaw,
            out PlayerSpawnPoint spawn)
        {
            spawn = default;
            return false;
        }
    }
}

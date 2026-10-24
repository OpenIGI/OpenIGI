using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>The host's side of <see cref="IHostedMission"/>: the picture, the sound, the files, the menus and the log of a title's mission.</summary>
    /// <param name="host">The host.</param>
    private sealed class MissionHostServices(GameHost host) : IMissionHostServices
    {

        public void SpawnImpactBurst(in WorldRayHit worldHit)
        {
            if (host._effects.Particles is { } bursts
                && CombatEffectCVars.ImpactParticles.Value is > 0f and var intensity)
            {
                string material = host._mission.Materials is { } names
                    && names.TryGet(worldHit.Material, out GameMaterial struck)
                    ? struck.Name
                    : string.Empty;
                bursts.SpawnBurst(
                    worldHit.Position,
                    worldHit.Normal,
                    ImpactParticleRenderer.KindOf(material),
                    intensity);
            }
        }

        /// <summary>Gets or sets the frame loop's state for the pass being run.</summary>
        public FrameLoopState State { get; set; } = null!;

        public bool PlayerMode => host._playerMode;

        public Vector3d? FreeCameraPosition => host._camera?.Position;

        public WorldScene? Scene => host._scene;


        // Its spawn column follows the eye, so it is handed the position the last frame drew from -- one frame stale
        // is nothing to a column forty metres across, and the tick has no camera of its own.
        public void StepWeather() => host._storm?.Tick(host._stormEyeMetres);


        public void SoundVehicles(CutsceneRuntime cutscenes)
        {
            if (host._audioOut.Sounds is not { } vehicleSoundSystem)
            {
                return;
            }

            host._vehicleSounds ??= new VehicleSoundPlayer(vehicleSoundSystem, new Random(0x49474931));
            host._vehicleSounds.Tick(
                cutscenes.VehicleSoundSources(),
                cutscenes.VehicleGunSounds(),
                host._audioOut.Device.Listener.Position * OpenIGI.Engine.Audio.SoundSystem.UnitsPerMetre);
        }

        public IOpenIgiLogger Logger => host._logger;

        public OpenIGI.Input.InputState ViewInput => host.ViewInput;

        public bool FailedMenu => host._session.FailedMenu;

        public bool ComputerOpen => host._computerOpen;

        public bool TickTitlePlayer() => host.TickTitlePlayer();

        public void CheatWeaponsGranted() => host.ShowCheatWeapons();

        public void PlayMineTrigger(Vector3d origin) =>
            host._audioOut.Sounds?.PlayDefinition("proxy_arm", origin, priority: 180);




        public void PresentPlayerImpacts() => host.PresentPlayerImpacts();




        public void StepWorldEffects() => host.StepWorldEffects();

        public void StepScreen() => host._hud?.Tick();

        // Test-only outro forcing: re-apply the env-specified gate writes every tick so gates whose variables the level
        // scripts reset stay satisfied while the scene plays.
        public void ApplyTestWrites()
        {
            foreach ((string writeName, string writeValue) in host._testOutroTickWrites)
            {
                host._loop.Variables.TryWrite(
                    writeName,
                    QvmValue.FromInt32((int)double.Parse(writeValue, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        public void FollowCorpse(Vector3d centre) => host.Viewpoint?.FollowDeathView(centre);

        public OpenIGI.Input.InputState Input => host._window.Input;

        public OpenIGI.Game.Config.PlayerProfile? InputProfile => host._configuration.Active;

        public IGroundHeightField? Ground => host._mission.Ground;

        public bool IsInteractionViewActive => host.Viewpoint?.IsInteractionViewActive == true;

        public void BeginInteractionView(Vector3d feet, float facing, WorldRayCast? walls) =>
            host.Viewpoint?.BeginInteractionView(feet, facing, walls);

        public void FollowInteractionView(Vector3d feet) => host.Viewpoint?.FollowInteractionView(feet);

        public void TickInteractionView() => host.Viewpoint?.TickInteractionView();

        public void ResetInteractionView() => host.Viewpoint?.ResetInteractionView();

        public bool BodyFollowAllowed =>
            host._playerMode
            && !host._session.FailedMenu
            && !host._computerOpen
            && !host._entry.IsCameraActive
            && !host._menuFlow.OverShowcase;

        public void CloseTick() => host._outcome.Settle(State);

        public GameLoop Game => host._loop;

        public VirtualFileSystem FileSystem => host._fileSystem;

        public OpenIGI.Engine.Resources.ResourceRegistry Resources => host._resources;

        public TerrainData? Terrain => host._mission.Terrain;

        public Formats.Animation.BoneAnimationSet? ReadSharedAnimations()
        {
            host.ReadSharedAnimations();
            return host._playerBodyAnimations;
        }

        public void EndStage(string what) => host.EndStage(what);



        public void WeaponDropped(LoadedMission loaded, GunPickup pickup) => host._gunPickupRenderer?.Add(loaded, pickup);

        public void TerrainRowRead(int? row) => host._mission.TerrainMaterialRow = row;

        public SoundSystem? SoundOutput => host._audioOut.Sounds;


        public void PlayDefinition(string name, Vector3d? positionInUnits = null, int priority = 0) =>
            host._audioOut.Sounds?.PlayDefinition(name, positionInUnits, priority: priority);



        public void BeginDeathCamera(Vector3d position, float facing, WorldRayCast walls) =>
            host.Viewpoint?.BeginDeathView(position, facing, walls);

        public int ScriptDifficulty => (int?)host._options.Difficulty ?? host._configuration.Active?.Difficulty ?? 1;

        public AiDifficulty Difficulty => host.EffectiveDifficulty();

        public string DifficultySource => host.DifficultySource;

        public string GamePath => host._gamePath;

        public bool PlayCutscenes => host._options.PlayCutscenes;

        public QvmValue? HostNative(string name, OpenIGI.Scripting.Expressions.ScriptArguments arguments)
        {
        if (name == GameMusicNatives.EnableNative)
        {
            if (host._audioOut.Music is not null)
            {
                host._audioOut.Music.Enabled = true;
            }
            return QvmValue.FromInt32(1);
        }
        if (name == GameMusicNatives.DisableNative)
        {
            if (host._audioOut.Music is not null)
            {
                host._audioOut.Music.Enabled = false;
            }
            return QvmValue.FromInt32(1);
        }
            return null;
        }

        public void CutsceneSound(CutsceneSoundEvent sound) => host._cutsceneSounds.Set(sound);

        public void StopCutsceneSounds() => host._cutsceneSounds.StopAll();

        public bool IsRaining => host._rain?.IsWeatherEnabled == true;

        public bool ImpactEffectsWanted => CombatEffectCVars.ImpactEffects.Value;

        public bool BloodWanted => host._configuration.Active?.BloodEnabled ?? true;

        public bool IsKeyDown(OpenIGI.Input.KeyCode key) => host._window.Input.Keyboard.IsDown(key);

        public void PlayDefinition(string name, Vector3d positionInUnits, int priority) =>
            host._audioOut.Sounds?.PlayDefinition(name, positionInUnits, priority: priority);

        public void FlashbangSmoke(Vector3d positionInUnits) => host._effects.SpawnFlashbangSmoke(positionInUnits);

        public void Fireball(Vector3d positionInUnits) =>
            host._effects.SpawnBlast(positionInUnits, Math.Max(1f, CombatEffectCVars.ImpactParticles.Value));


        public void Explosion(Vector3d positionInUnits, float fullRangeUnits, int fireballs, int fragments) =>
            host._effects.Particles?.SpawnRetailExplosion(positionInUnits, new RetailExplosionShape(fullRangeUnits, fireballs, fragments));

        public void SpeakerExplosion(Vector3d positionInUnits, bool isCameraLens) =>
            host._effects.Particles?.SpawnRetailExplosion(
                positionInUnits,
                isCameraLens ? RetailExplosionShape.CameraLens : RetailExplosionShape.AlarmLight);

        public void GlassShatter(WorldPlacement placement, Vector3d atUnits) =>
            host._effects.Particles?.SpawnGlassShatter(placement, atUnits);
    }
}

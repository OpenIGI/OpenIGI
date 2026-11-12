using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>
/// The mission machinery the host runs a level with: its shared state, the tick, the bring-up of each level's systems,
/// the player's bring-up, and the sounds and effects that are the mission's own.
/// </summary>
/// <remarks>
/// Made by the composition root, which is the one place that names a title, over the host's <see cref="IMissionHostServices"/>.
/// The host calls it in the order its frame loop and its mission systems run, and knows nothing of the title behind it.
/// </remarks>
public interface IHostedMission : IMissionGameplay, ITitleGameplay
{
    /// <summary>Gets the mission's state, which the host reads and the level systems fill.</summary>
    MissionState World { get; }

    /// <summary>
    /// Gets a value indicating whether the host's walk of the level runs it: <see langword="false"/> for a game whose level
    /// runs itself behind its player, which the walk only passes over.
    /// </summary>
    bool OwnsLevel => true;

    /// <summary>Gets the mission's fixed tick.</summary>
    IMissionTick Tick { get; }

    /// <summary>Gets the bring-up of the level's own systems.</summary>
    IMissionSetup Setup { get; }

    /// <summary>Gets the mission heard: every voice its things hold.</summary>
    IMissionAudio Audio { get; }

    /// <summary>Gets the humans' cries and the sounds of rounds landing.</summary>
    IMissionCries Sounds { get; }

    /// <summary>Gets the mission's own impact effects.</summary>
    IMissionImpacts Impacts { get; }

    /// <summary>Gets the save games of the mission that is up, or <see langword="null"/> when it keeps none.</summary>
    ITitleSaveGames? SaveGames => null;

    /// <summary>
    /// Starts reading, on a worker, what every mission of the game stands its player with -- the animation sets under its
    /// installation's common data -- so the first mission does not wait on it.
    /// </summary>
    void Prefetch();

    /// <summary>Starts bringing up a level's player.</summary>
    /// <returns>The bring-up, whose rig is made by <see cref="IPlayerBringUp.Begin"/>.</returns>
    IPlayerBringUp NewPlayerSetup();

    /// <summary>Sets the game's own steps in bringing a mission up among the host's, by the names in <see cref="MissionSystemNames"/>.</summary>
    /// <param name="systems">The host's systems, a fresh list for the mission being brought up.</param>
    void AddMissionSystems(LevelSystems<MissionLevelBuild> systems);
}

/// <summary>The mission's fixed tick, and what it keeps between ticks.</summary>
public interface IMissionTick
{
    /// <summary>Runs one frame of the mission: a whole tick when one is due, and the per-frame steps otherwise.</summary>
    /// <param name="tick">Whether a fixed tick is due this frame.</param>
    void Run(bool tick);

    /// <summary>Forgets the shots already reported, as the player starts afresh.</summary>
    void ResetReports();

    /// <summary>Forgets any climb in progress, as the player starts afresh.</summary>
    void ResetClimbs();

    /// <summary>Breaks the scene that is playing and runs the level's state once, so a gate the caller has just written is seen.</summary>
    /// <remarks>For the test captures that force an authored outro through its own script gate.</remarks>
    void BreakIntoOutro();
}

/// <summary>What the host asks of a level's bring-up; the title's own systems make the rest (<see cref="IHostedMission.AddMissionSystems"/>).</summary>
public interface IMissionSetup
{
    /// <summary>Gets a value indicating whether the level has soldiers whose brains are to be animated.</summary>
    bool HasSoldiers { get; }
}

/// <summary>The bring-up of a level's player: its rig made, armed, tuned and stood where the level places it.</summary>
public interface IPlayerBringUp
{
    /// <summary>Gets the weapon table, once <see cref="Begin"/> has read it.</summary>
    WeaponTypeRegistry Weapons { get; }


    /// <summary>Makes the player's rig and reads the tables it and the soldiers use.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="missionText">The mission's text.</param>
    void Begin(LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids, MissionStringResolver missionText);

    /// <summary>Arms the level's vehicles from the weapon table.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    void ArmVehicles(LoadedMission loaded, LevelTask levelRoot);

    /// <summary>Tunes the rig from the title's player script.</summary>
    void Configure();

    /// <summary>Gets the animation set the player's body walks on, which its third-person body is built from; none without it.</summary>
    BoneAnimationSet? BodyAnimations { get; }

    /// <summary>Gives the rig its first-person rig, where the game has one to give.</summary>
    /// <returns>Whether it has one: what the arms are drawn on and a weapon hangs off.</returns>
    bool UseViewRig();

    /// <summary>Hands the rig what the level declares it carries.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="carried">Whether a weapon was put in its hands.</param>
    /// <returns>Whether the level declares a loadout at all.</returns>
    bool CarryDeclaredLoadout(LevelTask levelRoot, out bool carried);

    /// <summary>Offers the rig's rounds to the level's soldiers, when it has any.</summary>
    /// <param name="deflected">Where a glancing round whizzes.</param>
    void ConnectSoldiers(Action<Vector3d> deflected);

    /// <summary>Stands the rig where the level places its player, and makes it the mission's player.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="ground">The ground it stands on.</param>
    /// <param name="solids">The collision index, whose floors it may stand on.</param>
    /// <param name="opening">Where the opening camera looks from, for a level that declares no spawn.</param>
    /// <param name="openingYaw">The opening camera's heading.</param>
    /// <param name="spawn">The level's declared spawn, when it has one.</param>
    /// <returns>Whether it found something to stand on.</returns>
    bool Stand(
        LevelTask levelRoot,
        IGroundHeightField? ground,
        ObjectCollisionIndex solids,
        Vector3d? opening,
        float openingYaw,
        out PlayerSpawnPoint spawn);
}

/// <summary>The mission heard: the voices its things hold, kept across the frame.</summary>
public interface IMissionAudio
{
    /// <summary>Makes the player's weapon voice afresh on the device just opened.</summary>
    void OnDeviceOpened();

    /// <summary>Stops the player's held voices and the weapons' loops, as the bank is emptied for a mission.</summary>
    void ResetForMission();

    /// <summary>Forgets the security cameras' voices.</summary>
    void ForgetCameras();

    /// <summary>Forgets the sirens' voices and the failures already reported.</summary>
    void ForgetSirens();
}

/// <summary>The humans' cries and the sounds of rounds landing.</summary>
public interface IMissionCries
{
    /// <summary>Plays what the player's rounds struck this tick.</summary>
    void PlayPlayerImpacts();

    /// <summary>Plays the whizz a round makes coming off a surface.</summary>
    /// <param name="position">Where it glanced off, in world units.</param>
    void PlayRicochet(Vector3d position);
}

/// <summary>The mission's own impact effects: the dust and chips where rounds land, and the blood.</summary>
public interface IMissionImpacts
{
    /// <summary>Empties the effects, and tells them how many frames the blood mist has.</summary>
    /// <param name="mistFrames">The mist's frame count.</param>
    void Reset(int mistFrames);

    /// <summary>Lets the level's soldiers bleed.</summary>
    void ConnectSoldiers();

    /// <summary>Steps the effects one tick.</summary>
    void Tick();

    /// <summary>Spawns what the player's rounds struck this tick.</summary>
    void SpawnPlayerImpacts();

    /// <summary>Registers the effects' test commands.</summary>
    /// <param name="console">The console to register with.</param>
    /// <param name="eye">The camera the frame is drawn from, which the commands aim with.</param>
    /// <param name="logger">Where the commands report.</param>
    void Register(ConsoleSystem console, Func<Camera3D> eye, IOpenIgiLogger logger);
}

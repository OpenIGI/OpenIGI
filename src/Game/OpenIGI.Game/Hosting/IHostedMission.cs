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
public interface IHostedMission : IMissionGameplay
{
    /// <summary>Gets the mission's state, which the host reads and the level systems fill.</summary>
    MissionState World { get; }

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

    /// <summary>Hands the level's walk the soldiers and the player, so each updates at its place in the tree.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    void AttachToLevelWalk(GameTask levelRoot);

    /// <summary>Gets the player as the level's areas count it, now.</summary>
    /// <returns>Where the player is, its team and whether it lives.</returns>
    AreaPlayer AreaPlayerNow();
}

/// <summary>The bring-up of a level's own systems, in the order the host's mission systems run them.</summary>
public interface IMissionSetup
{
    /// <summary>Gets a value indicating whether the level has soldiers whose brains are to be animated.</summary>
    bool HasSoldiers { get; }

    /// <summary>Makes the mission's text and its cutscene runtime.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="walker">The level's walk.</param>
    /// <param name="objectives">The objective runtime being built, which is made after this one.</param>
    /// <returns>The mission's text.</returns>
    MissionStringResolver BuildCutscenes(
        LoadedMission loaded,
        LevelTask levelRoot,
        LevelUpdateWalker walker,
        Func<MissionObjectiveRuntime?> objectives);

    /// <summary>Makes the objective runtime.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="missionText">The mission's text.</param>
    /// <param name="areaPlayer">The player as the level's areas count it.</param>
    /// <param name="made">Told the runtime as soon as it exists.</param>
    /// <returns>The runtime.</returns>
    MissionObjectiveRuntime BuildObjectives(
        LevelTask levelRoot,
        MissionStringResolver missionText,
        Func<AreaPlayer> areaPlayer,
        Action<MissionObjectiveRuntime> made);

    /// <summary>Builds the doors, lifts, pickups and world interactions.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="placements">The level's placements, whose models size each pickup's reach.</param>
    void BuildInteractions(
        LoadedMission loaded, LevelTask levelRoot, ObjectCollisionIndex solids, IReadOnlyList<WorldPlacement>? placements = null);

    /// <summary>Builds the security cameras and the trace the soldiers' sight and rounds share.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="scene">The level's built scene.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    /// <param name="terrain">The level's terrain, or <see langword="null"/>.</param>
    /// <returns>The trace of the placed objects and the terrain together.</returns>
    WorldTraceCast BuildCameras(LevelTask levelRoot, WorldScene scene, ObjectCollisionIndex solids, TerrainData? terrain);

    /// <summary>Reads the level's navigation graphs.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="gamePath">Where the game is installed.</param>
    /// <param name="placements">The level's placements, which place a graph on a train where its car stands.</param>
    /// <returns>The graphs, by their task's id.</returns>
    Dictionary<int, GraphFile> ReadNavGraphs(LoadedMission loaded, LevelTask levelRoot, string gamePath, IReadOnlyList<WorldPlacement>? placements = null);

    /// <summary>Animates the soldiers' brains, and builds the alarm speakers and the guards' rounds.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="scene">The level's built scene.</param>
    /// <param name="worldTrace">What the soldiers' sight and rounds trace.</param>
    /// <param name="graphs">The level's nav graphs, by their task's id.</param>
    /// <param name="difficulty">The difficulty every guard reads its combat row at.</param>
    /// <param name="difficultySource">Where the difficulty came from, for the log.</param>
    void AnimateSoldiers(
        LevelTask levelRoot,
        WorldScene scene,
        WorldTraceCast worldTrace,
        Dictionary<int, GraphFile> graphs,
        AiDifficulty difficulty,
        string difficultySource);

    /// <summary>Builds the level's vehicles.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="id">The mission's id.</param>
    /// <param name="scene">The level's built scene.</param>
    /// <param name="solids">The collision index over the placed objects.</param>
    void BuildVehicles(LoadedMission loaded, LevelTask levelRoot, int id, WorldScene scene, ObjectCollisionIndex solids);
}

/// <summary>The bring-up of a level's player: its rig made, armed, tuned and stood where the level places it.</summary>
public interface IPlayerBringUp
{
    /// <summary>Gets the weapon table, once <see cref="Begin"/> has read it.</summary>
    WeaponTypeRegistry Weapons { get; }

    /// <summary>Gets the rig being brought up, as the host steers it.</summary>
    IHostedPlayer Player { get; }

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

    /// <summary>Gives the rig its first-person rig.</summary>
    /// <param name="animations">The first-person rig's animation set.</param>
    void UseViewRig(BoneAnimationSet animations);

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

    /// <summary>Forgets the ambience voices, as a level's areas are rebuilt.</summary>
    void ForgetAmbience();

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

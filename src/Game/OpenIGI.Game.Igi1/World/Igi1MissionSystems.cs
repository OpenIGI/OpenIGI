using OpenIGI.Core.Titles;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Events;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Project I.G.I.'s own steps in bringing a mission up, each set among the host's by the names they stand before.</summary>
/// <remarks>
/// <para>
/// What the game is made of -- the cutscenes and objectives, the vehicles, the interactions, the cameras, the nav graphs, the
/// soldiers' brains and the level's walk -- and nothing of how it is drawn: the host's own systems, which hand what these leave
/// to the picture, run right after each. The order is the one the mission has always come up in.
/// </para>
/// <para>
/// A mod finds them by <see cref="MissionSystemNames"/> like any other, so it can replace the game's half of a step and keep
/// the host's.
/// </para>
/// </remarks>
public static class Igi1MissionSystems
{
    /// <summary>Inserts the game's steps among the host's.</summary>
    /// <param name="systems">The host's systems, the game's to be set among them.</param>
    /// <param name="game">The game the steps bring up.</param>
    /// <param name="host">What the host tells the steps.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void AddTo(LevelSystems<MissionLevelBuild> systems, Igi1Gameplay game, IMissionSetupHost host)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(host);

        systems
            .InsertBefore(MissionSystemNames.TestCaptures, new LevelSystem<MissionLevelBuild>(MissionSystemNames.Cutscenes, b =>
            {
                game.World.LevelRoot = b.LevelRoot;
                game.World.MissionId = b.Id;
                b.MissionText = game.Setup.BuildCutscenes(b.Loaded, b.LevelRoot, b.Walker, () => b.Objectives);
            }))
            .InsertBefore(MissionSystemNames.TestCaptures, new LevelSystem<MissionLevelBuild>(MissionSystemNames.Objectives, b =>
                game.Setup.BuildObjectives(
                    b.LevelRoot,
                    b.MissionText ?? throw Missing(MissionSystemNames.Cutscenes),
                    game.Tick.AreaPlayerNow,
                    made =>
                    {
                        made.PartnerPlayers = game.Tick.PartnerAreaPlayers;
                        b.Objectives = made;
                        BringComputerUp(game, b, made, host);
                    })))
            .InsertBefore(MissionSystemNames.SceneView, new LevelSystem<MissionLevelBuild>(MissionSystemNames.Vehicles, b =>
                game.Setup.BuildVehicles(
                    b.Loaded,
                    b.LevelRoot,
                    b.Id,
                    b.Scene ?? throw Missing(MissionSystemNames.Scene),
                    b.Solids ?? throw Missing(MissionSystemNames.Scene))))
            .InsertBefore(MissionSystemNames.InteractionsView, new LevelSystem<MissionLevelBuild>(MissionSystemNames.Interactions, b =>
            {
                game.Audio.ForgetAmbience();
                game.Setup.BuildInteractions(
                    b.Loaded, b.LevelRoot, b.Solids ?? throw Missing(MissionSystemNames.Scene), b.Scene?.Placements);
                game.Impacts.ConnectSoldiers();
            }))
            .InsertBefore(MissionSystemNames.CamerasView, new LevelSystem<MissionLevelBuild>(MissionSystemNames.Cameras, b =>
            {
                game.Audio.ForgetCameras();
                host.StopCutsceneSounds();

                // Mission 14 has no live terrain: its cameras see through the placed models alone.
                b.WorldTrace = game.Setup.BuildCameras(
                    b.LevelRoot,
                    b.Scene ?? throw Missing(MissionSystemNames.Scene),
                    b.Solids ?? throw Missing(MissionSystemNames.Scene),
                    b.Loaded.Terrain is { IsUsable: true } terrain ? terrain : null);
            }))
            .InsertBefore(MissionSystemNames.LightBakes, new LevelSystem<MissionLevelBuild>(MissionSystemNames.NavGraphs, b =>
            {
                b.Graphs = game.Setup.ReadNavGraphs(b.Loaded, b.LevelRoot, host.GamePath, b.Scene?.Placements);
                host.EndStage("reading the nav graphs");
            }))
            .InsertBefore(MissionSystemNames.AlarmsView, new LevelSystem<MissionLevelBuild>(MissionSystemNames.NavigationAi, b =>
            {
                if (b.Scene is null)
                {
                    throw Missing(MissionSystemNames.Scene);
                }

                if (!game.Setup.HasSoldiers)
                {
                    // A title without this game's soldiers has no brains to animate here.
                    return;
                }

                Dictionary<int, GraphFile> graphs = b.Graphs ?? throw Missing(MissionSystemNames.NavGraphs);
                game.Audio.ForgetSirens();
                game.Setup.AnimateSoldiers(
                    b.LevelRoot,
                    b.Scene,
                    b.WorldTrace ?? throw Missing(MissionSystemNames.Cameras),
                    graphs,
                    host.Difficulty,
                    host.DifficultySource);
            }))
            .InsertAfter(MissionSystemNames.AlarmsView, new LevelSystem<MissionLevelBuild>(MissionSystemNames.LevelWalk, b =>
                game.Tick.AttachToLevelWalk(b.LevelRoot)))
            .Add(new LevelSystem<MissionLevelBuild>(MissionSystemNames.Events, b =>
            {
                game.World.EventWatch = null;
                if (b.Services.TryGet(out GameEvents? events))
                {
                    game.World.EventWatch = new Igi1EventWatch(game.World, events);
                    events.Raise(new LevelLoaded(GameTitle.Igi1, b.Id));
                }
            }));
    }

    // A step ran before the one it depends on.
    private static InvalidOperationException Missing(string name) => new($"The mission system '{name}' has not run.");

    /// <summary>Gives the computer the level's text, variables, objectives and limit, closed, as each level comes up.</summary>
    private static void BringComputerUp(Igi1Gameplay game, MissionLevelBuild b, OpenIGI.Game.World.MissionObjectiveRuntime objectives, IMissionSetupHost host)
    {
        game.ComputerWorld.Strings = b.MissionText;
        game.ComputerWorld.Variables = host.Game.Variables;
        if (game.World.Computer is not { } computer)
        {
            return;
        }

        computer.Close();
        computer.Objectives = objectives;
        computer.Limit = null;
        computer.MapPosition = default;

        // 0x466F60: the last DefineComputerLimit made is the computer's.
        foreach (GameTask task in b.LevelRoot.Descendants())
        {
            if (task is DeclaredTask { Type.Name: "DefineComputerLimit" } limit
                && limit.TryGetValue("Position", out object? at) && at is OpenIGI.Core.Numerics.Vector3d centre)
            {
                float size = limit.TryGetValue("Quadrant Size (m)", out object? raw) && raw is float metres ? metres : 0f;
                computer.Limit = new OpenIGI.Game.Computer.ComputerMapLimit(centre, size);
            }
        }
    }
}

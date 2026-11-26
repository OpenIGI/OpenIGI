using OpenIGI.Engine.Flow;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

// What the suites read of a running host. Each accessor only reads, or asks what the host itself asks, so a
// field moving to another part of the host breaks a build rather than a test that only runs with the game's data.
public sealed partial class GameHost
{
    /// <summary>Gets the script loop the host runs, for the suites that read or write its variables.</summary>
    internal GameLoop LoopForTesting => _loop;

    /// <summary>Gets the played mission's registries, which the host keeps together.</summary>
    internal MissionState MissionForTesting => _mission;

    /// <summary>Gets the save games of the mission that is up, for a test.</summary>
    internal OpenIGI.Game.Player.ITitleSaveGames? SaveGamesForTesting => MissionSaveGames;

    /// <summary>Gets the soldiers' bodies the title brought up, or <see langword="null"/> before a mission has them.</summary>
    internal ISoldierBodies? SoldiersForTesting => _soldiers;

    /// <summary>Gets the level's placed scene, or <see langword="null"/> before one is built.</summary>
    internal WorldScene? SceneForTesting => _scene;

    /// <summary>Gets what draws the world, or <see langword="null"/> before one is built.</summary>
    internal IWorldPresenter? WorldForTesting => _world;

    /// <summary>Gets the title's gameplay, which the suites cast to the title's own type.</summary>
    internal OpenIGI.Game.Hosting.IHostedMission HostedForTesting => _hosted;

    /// <summary>Gets the editor's cutscene preview, as the editor would have it.</summary>
    internal Editor.IEditorCutscenePreview? CutscenePreviewForTesting => CutscenePreview;

    /// <summary>Gets the title's event bus, which reports a mission's end, or <see langword="null"/> before a title has one.</summary>
    internal OpenIGI.Engine.Events.GameEvents? EventsForTesting =>
        _title is { } title && title.Services.TryGet(out OpenIGI.Engine.Events.GameEvents? events) ? events : null;

    /// <summary>Whether the level lets the player read the map computer's map right now, as the host decides it.</summary>
    /// <returns><see langword="true"/> when the map is there.</returns>
    internal bool MapComputerAvailableForTesting() => MapComputerAvailable();

    /// <summary>Gets the title's own player, or <see langword="null"/> before one is up.</summary>
    internal OpenIGI.Game.Player.ITitlePlayer? TitlePlayerForTesting => TitlePlayer;

    /// <summary>Gets the game path of the mission that is up's level script, or null with none up.</summary>
    internal string? ObjectsPathForTesting => _session.Mission is { } mission
        ? $"{mission.MissionDirectory.TrimEnd('/', '\\')}/{Engine.Level.LevelLoader.ObjectsScriptName}"
        : null;

    /// <summary>Lays a folder over the game as a content layer, the way a played map is, so the next load reads it.</summary>
    /// <param name="name">The layer's name.</param>
    /// <param name="folder">The folder, laid out as the installation is.</param>
    internal void MountForTesting(string name, string folder)
    {
        _overrides.Mount(new Engine.Resources.OverrideLayer(name, name, folder));
        ApplyOverrideChange(out _);
    }

    /// <summary>Loads the mission that is up again, as the editor's own reload does.</summary>
    internal void ReloadLevelForTesting() => ReloadLevelInPlace();

    /// <summary>Sets what the title's player is held to, as <c>title_hold</c> does.</summary>
    internal OpenIGI.Game.Player.PlayerAction TitleHeldForTesting
    {
        set => _titleScriptHeld = value;
    }
}

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

    /// <summary>Gets the soldiers' bodies the title brought up, or <see langword="null"/> before a mission has them.</summary>
    internal ISoldierBodies? SoldiersForTesting => _soldiers;

    /// <summary>Gets the level's placed scene, or <see langword="null"/> before one is built.</summary>
    internal WorldScene? SceneForTesting => _scene;

    /// <summary>Gets what draws the world, or <see langword="null"/> before one is built.</summary>
    internal IWorldPresenter? WorldForTesting => _world;

    /// <summary>Whether the level lets the player read the map computer's map right now, as the host decides it.</summary>
    /// <returns><see langword="true"/> when the map is there.</returns>
    internal bool MapComputerAvailableForTesting() => MapComputerAvailable();
}

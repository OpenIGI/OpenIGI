using OpenIGI.Engine.Flow;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Igi1;

namespace OpenIGI.Desktop.Tests;

/// <summary>What the suites read of a running Project I.G.I. host, through the host's own testing accessors.</summary>
internal static class GameHostProbe
{
    /// <summary>The played mission's registries.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The mission, as Project I.G.I. keeps it.</returns>
    public static MissionWorld Mission(this GameHost host) => (MissionWorld)host.MissionForTesting;

    /// <summary>The save games of the mission that is up.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The save games, or <see langword="null"/> for a mission that keeps none.</returns>
    public static OpenIGI.Game.Player.ITitleSaveGames? SaveGames(this GameHost host) => host.SaveGamesForTesting;

    /// <summary>The title's gameplay: its blasts, harm and the rest of the mission's systems.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The gameplay, as Project I.G.I. keeps it.</returns>
    public static Igi1Gameplay Gameplay(this GameHost host) => (Igi1Gameplay)host.HostedForTesting;

    /// <summary>The player, once the mission has brought one up.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The player, or <see langword="null"/>.</returns>
    public static PlayerCamera? Player(this GameHost host) => host.Mission().Player;

    /// <summary>The soldiers' renderer, once the mission has its soldiers.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The renderer, or <see langword="null"/>.</returns>
    public static SoldierRenderer? Soldiers(this GameHost host) => host.SoldiersForTesting as SoldierRenderer;

    /// <summary>The script loop the host runs.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The loop.</returns>
    public static GameLoop Loop(this GameHost host) => host.LoopForTesting;

    /// <summary>The level's placed scene.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The scene, or <see langword="null"/>.</returns>
    public static WorldScene? Scene(this GameHost host) => host.SceneForTesting;

    /// <summary>What draws the world.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The presenter, or <see langword="null"/>.</returns>
    public static IWorldPresenter? World(this GameHost host) => host.WorldForTesting;

    /// <summary>How many rounds the guards have fired.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The count, or 0 before the mission's guards are armed.</returns>
    public static int GuardRoundsFired(this GameHost host) => host.Mission().GuardRounds?.Fired ?? 0;

    /// <summary>How many of the guards' rounds hit the player.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The count, or 0 before the mission's guards are armed.</returns>
    public static int GuardHitsOnPlayer(this GameHost host) => host.Mission().GuardRounds?.HitsOnPlayer ?? 0;
}

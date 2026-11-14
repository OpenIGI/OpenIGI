using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Modding.Testing;

/// <summary>
/// An I.G.I. 2 mission loaded headless as the game loads it (<see cref="MissionFixture.Igi2"/>): its session, its level and its
/// world, and the request its player is stood by -- which brings the level up through its systems, a mod's among them.
/// </summary>
/// <param name="Guest">The game's session, with the mods the fixture was given.</param>
/// <param name="Loaded">The mission's descriptor and packs.</param>
/// <param name="Level">The level's task tree.</param>
/// <param name="Built">What the game's world builder made of the level: its scene, ground and placed collision.</param>
/// <param name="Request">The request the player is stood by.</param>
public sealed record Igi2Mission(GuestTitle Guest, LoadedMission Loaded, LevelTask Level, WorldBuildResult Built, TitlePlayerRequest Request)
{
    /// <summary>Gets the level's own player task.</summary>
    public HumanPlayerTask? Player => PlayerSpawn.FindGameplayTask(Level);

    /// <summary>
    /// Stands the mission's player as the host does: the player a mod's gameplay provides, when one does and does not decline
    /// the mission, else I.G.I. 2's own -- either brings the level up through its systems.
    /// </summary>
    /// <returns>The player; <see langword="null"/> when the level places none.</returns>
    public ITitlePlayer? Stand() =>
        Request.Services.TryGet(out ITitleGameplay? provided) && provided.CreatePlayer(Request) is { } stood
            ? stood
            : new Igi2Gameplay().CreatePlayer(Request);
}

namespace OpenIGI.Game;

/// <summary>When a grenade's fuse starts to burn in the player's hand (<see cref="GameplayCVars.GrenadeCook"/>).</summary>
public enum GrenadeCook
{
    /// <summary>
    /// As each game shipped: Project I.G.I.'s fuse only starts once the grenade leaves the hand; I.G.I. 2's pin comes out
    /// as the throw winds up, and the fuse burns from there.
    /// </summary>
    Original,

    /// <summary>The fire key only winds the throw up; the reload key, pressed while it is held, pulls the pin.</summary>
    Manual,
}

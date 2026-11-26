namespace OpenIGI.Game;

/// <summary>What I.G.I. 2's player may carry of its primary weapons (<see cref="GameplayCVars.Igi2Loadout"/>).</summary>
public enum Igi2Loadout
{
    /// <summary>One primary weapon, as the game shipped: another is only swapped for it.</summary>
    Original,

    /// <summary>A long gun, a submachine gun and a launcher side by side, each swapped only for another of its own kind.</summary>
    Tactical,
}

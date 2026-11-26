namespace OpenIGI.Game.Igi2.Player;

/// <content>The player taken out of the world and put back, as the conditional container it stands under says.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>
    /// Takes the player out of the world: the player's own remove handler (<c>humanplayer.c</c>, linked for
    /// <c>DynCubeObj_eDynCubeObjRemoveQTaskEvent</c>).
    /// </summary>
    /// <remarks>
    /// The computer is deleted, then the body leaves the world -- its in-world byte cleared, so it is no longer drawn and no human
    /// bumps it. Unlike a soldier's (<c>Human_RemoveDynCubeObjHandler</c>) it stays on the humans' list, which reads the byte.
    /// The levels keep their player under a container that shuts while a cutscene plays (location1/level3's "Human Player and AI
    /// disabled during cutscenes"), whose actors stand in for the player meanwhile.
    /// </remarks>
    public void RemoveFromWorld()
    {
        Computer?.Close();
        SetInWorld(false);
    }

    /// <summary>Puts the player back in the world, <c>Human_RegisterDynCubeObjHandler</c>, which the player's type inherits.</summary>
    public void PutInWorld() => SetInWorld(true);
}

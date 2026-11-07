namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// Tells when this player takes the bomb up from where it lay, so its pickup message is posted as a gun's is: the mode, not a
/// pickup, puts the bomb in hand, so no pickup event posts it.
/// </summary>
/// <remarks>Coming to carry a bomb last seen loose (on the ground, or thrown and flying) is a pickup; a round handing it out is not.</remarks>
public sealed class BombPickupWatch
{
    /// <summary>The message a bomb taken up posts.</summary>
    public const string Message = "Picked up the C4";

    private bool _wasLoose;

    /// <summary>Looks at the match as this player sees it now.</summary>
    /// <param name="hud">The match as shown, or <see langword="null"/> outside one.</param>
    /// <returns><see langword="true"/> when this player has just taken the bomb up.</returns>
    public bool Saw(MatchHud? hud)
    {
        bool picked = hud is { CarriesBomb: true } && _wasLoose;
        _wasLoose = hud is { BombIsLoose: true };
        return picked;
    }
}

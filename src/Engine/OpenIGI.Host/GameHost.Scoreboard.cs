using OpenIGI.Game.Config;

namespace OpenIGI.Host;

/// <summary>The multiplayer scoreboard's key.</summary>
public sealed partial class GameHost
{
    // Whether the scoreboard's key is held: Tab unless rebound. A profile saved before the board existed has no line for it
    // and is read against the shipped defaults for this one action, so the key works without a reset.
    private bool ScoreboardHeld()
    {
        PlayerProfile? profile = _configuration.Active;
        if (PlayerProfileInputBindings.BoundKeyName(profile, PlayerProfile.ScoreboardAction) is null)
        {
            profile = null;
        }

        return PlayerProfileInputBindings.IsDown(profile, _window.Input, PlayerProfile.ScoreboardAction);
    }
}

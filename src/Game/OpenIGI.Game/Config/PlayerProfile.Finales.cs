namespace OpenIGI.Game.Config;

/// <content>The last mission of each game the player has completed, which ends its campaign.</content>
/// <remarks>
/// Retail never moves <see cref="ActiveMission"/> past the last mission: its completion has no successor to unlock, so
/// <c>Game_RunHandler</c> (0x416260) goes straight to the outro (flow request 7) and writes nothing. The mission reached alone
/// therefore cannot tell a campaign finished from one standing at its last mission; this is OpenIGI's own record of that.
/// </remarks>
public sealed partial class PlayerProfile
{
    // By game: the mission whose completion ended its campaign.
    private readonly SortedDictionary<string, int> _finales = new(StringComparer.Ordinal);

    /// <summary>Gets the games whose campaign the player has finished, and the last mission completed in each.</summary>
    public IReadOnlyDictionary<string, int> Finales => _finales;

    /// <summary>Gets the mission whose completion ended a game's campaign.</summary>
    /// <param name="titleId">The game, by title id.</param>
    /// <returns>The mission's id; 0 for a campaign not finished.</returns>
    public int FinaleOf(string titleId) => _finales.GetValueOrDefault(titleId);

    /// <summary>Keeps a game's campaign as finished by completing a mission.</summary>
    /// <param name="titleId">The game, by title id.</param>
    /// <param name="missionId">The last mission completed.</param>
    /// <returns>Whether that changed what the profile keeps.</returns>
    public bool SetFinale(string titleId, int missionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        if (missionId <= 0 || _finales.GetValueOrDefault(titleId) == missionId)
        {
            return false;
        }

        _finales[titleId] = missionId;
        return true;
    }
}

using OpenIGI.Game.Igi1.Flow;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <content>Each player's mission score, for the squad's rank at the mission's end.</content>
public sealed partial class Igi1ReplicatedWorld
{
    /// <inheritdoc/>
    /// <remarks>Project I.G.I.'s soldiers fight every player as one target, so a sighting is the squad's, counted once.</remarks>
    public int? SquadTimesSpotted => _game.World.PlayerTask?.TimesSpotted ?? 0;

    /// <inheritdoc/>
    /// <remarks>
    /// The host's own from the mission's player, every partner's from its own rig and health; counted as a single player's is
    /// (<see cref="Igi1MissionScorer.ScoreOf"/>). The times spotted are the squad's, the same for each.
    /// </remarks>
    public MissionScore? ScoreOf(int slot)
    {
        if (slot == ServerSession.HostSlot)
        {
            return IncludesHost && _game.World.Player is { } own
                ? Igi1MissionScorer.ScoreOf(_game.World, own.Shots, _game.World.PlayerLife.Health)
                : null;
        }

        return _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? partner)
            ? Igi1MissionScorer.ScoreOf(_game.World, partner.Player.Shots, partner.Health)
            : null;
    }
}

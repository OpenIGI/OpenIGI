using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// The vote for the next map: while a decided match's card is up, the maps offered (<see cref="OfferMaps"/>), each player's pick
/// of them by its number key, a click, or <c>/1</c> in chat, and every player told the running count and its own pick
/// (<see cref="NextMapVote"/>). Once everyone has picked, the card's clock is cut to a few seconds; when it runs out the map
/// picked most is taken (<see cref="TakeChosenMap"/>).
/// </content>
public sealed partial class ServerSession
{
    // How long the card stays once every player has picked: long enough to see the result.
    private const double AllPickedSeconds = 3;

    // The maps a decided match offers for the next, and each player's pick of them.
    private MapChoice[] _mapChoices = [];
    private readonly Dictionary<int, int> _mapPicks = [];

    /// <summary>Gets the vote as the listen host's own player sees it, or <see langword="null"/> while none is open.</summary>
    public NextMapVote? MapVote => VoteSeenBy(HostSlot);

    /// <summary>Decides the match now on the scores as they stand, its card and the vote for the next map following.</summary>
    public void EndMatch() => Mode.EndNow(this, _tick);

    /// <summary>Offers the players the next map to vote for: the first by 1, the second by 2.</summary>
    /// <param name="choices">The maps, at most <see cref="NextMapVote.MostChoices"/>; none closes the vote.</param>
    public void OfferMaps(IReadOnlyList<MapChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _mapChoices = [.. choices.Take(NextMapVote.MostChoices)];
        _mapPicks.Clear();
        TellMapVote();
    }

    /// <summary>Takes the map the players picked most (the first offered on a tie), or <see langword="null"/> when none picked, and closes the vote.</summary>
    /// <returns>The map.</returns>
    public MapChoice? TakeChosenMap()
    {
        NextMapVote? vote = VoteSeenBy(HostSlot);
        MapChoice? chosen = vote is { Leading: >= 0 and var leading } ? _mapChoices[leading] : null;
        _mapChoices = [];
        _mapPicks.Clear();
        TellMapVote();
        return chosen;
    }

    // A player's pick, by the index of the map: counted, told to everyone, and the card cut short once all have picked.
    private void PickMap(int slot, int index)
    {
        if (index < 0 || index >= _mapChoices.Length || !Match.Players.ContainsKey(slot))
        {
            return;
        }

        if (_mapPicks.TryGetValue(slot, out int was) && was == index)
        {
            return;
        }

        _mapPicks[slot] = index;
        TellMapVote();
        if (Match.Players.Keys.Where(static voter => voter >= 0).All(_mapPicks.ContainsKey))
        {
            Mode.Flow.Hasten(_tick, AllPickedSeconds);
        }
    }

    // The vote as one player sees it: the count among those still playing, and that player's own pick.
    private NextMapVote? VoteSeenBy(int slot)
    {
        if (_mapChoices.Length == 0)
        {
            return null;
        }

        int[] counts = new int[_mapChoices.Length];
        foreach ((int voter, int pick) in _mapPicks)
        {
            if (Match.Players.ContainsKey(voter))
            {
                counts[pick]++;
            }
        }

        return new NextMapVote(_mapChoices, counts, Match.Players.Keys.Count(static voter => voter >= 0), _mapPicks.TryGetValue(slot, out int own) ? own : -1);
    }

    // Every joined player told the vote as it sees it -- or that there is none.
    private void TellMapVote()
    {
        foreach (int slot in _clients.Keys)
        {
            _writer.Reset();
            _writer.WriteBits((uint)SessionMessage.MapVote, 8);
            NextMapVote.Write(_writer, VoteSeenBy(slot));
            _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
        }
    }
}

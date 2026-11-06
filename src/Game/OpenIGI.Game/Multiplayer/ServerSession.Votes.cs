using System.Globalization;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// The players' say over the server: votes called and carried in chat (<see cref="MatchVote"/>), and a player who stands idle
/// too long put off so a slot is not held by nobody.
/// </content>
public sealed partial class ServerSession
{
    private readonly MatchVote _vote = new();

    // The maps a decided match offers for the next, and each player's pick of them.
    private (int Mission, string Mode)[] _mapChoices = [];
    private readonly Dictionary<int, int> _mapPicks = [];

    /// <summary>Offers the players the next map to vote for, each by its number in chat (<c>/1</c>, <c>/2</c>...).</summary>
    /// <param name="choices">The maps, as missions and their modes (empty to keep the one played).</param>
    public void OfferMaps(IReadOnlyList<(int Mission, string Mode)> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _mapChoices = [.. choices.Take(9)];
        _mapPicks.Clear();
        if (_mapChoices.Length == 0)
        {
            return;
        }

        IEnumerable<string> listed = _mapChoices.Select((c, i) => string.Create(
            CultureInfo.InvariantCulture, $"/{i + 1} mission {c.Mission}{(c.Mode.Length > 0 ? " " + c.Mode.ToUpperInvariant() : string.Empty)}"));
        Say($"Vote the next map: {string.Join("  ·  ", listed)}");
    }

    /// <summary>Takes the map the players picked most (the first offered on a tie), or <see langword="null"/> when none picked.</summary>
    /// <returns>The mission and its mode.</returns>
    public (int Mission, string Mode)? TakeChosenMap()
    {
        (int Mission, string Mode)? chosen = _mapPicks.Count == 0
            ? null
            : _mapChoices[_mapPicks.Values.GroupBy(static p => p).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key).First().Key];
        _mapChoices = [];
        _mapPicks.Clear();
        return chosen;
    }

    /// <summary>Gets or sets what changes the map a vote carried: the mission and mode (or <see langword="null"/> to keep it), and
    /// the reason it can't, or <see langword="null"/> once it is under way. Without it map votes are refused.</summary>
    public Func<int, string?, string?>? ChangeMap { get; set; }

    /// <summary>Gets or sets how long a player may send nothing but the same input before being put off, in seconds; 0 never.</summary>
    /// <remarks>Only joined players are timed: the listen host's own player is never put off its own server.</remarks>
    public double IdleKickSeconds { get; set; }

    /// <summary>Gets the votes, for a HUD or console to show the running one.</summary>
    public MatchVote Votes => _vote;

    /// <summary>Takes a chat line of the listen host's own player as a command when it is one.</summary>
    /// <param name="text">The line.</param>
    /// <returns><see langword="true"/> when it was a command, and so said to no one.</returns>
    public bool TryHostCommand(string text) => TryCommand(HostSlot, text);

    // A chat line beginning with / or ! that names a command: acted on, and said to no one.
    private bool TryCommand(int slot, string text)
    {
        if (text is not ['/' or '!', ..])
        {
            return false;
        }

        string[] words = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return false;
        }

        switch (words[0].ToLowerInvariant())
        {
            case "yes" or "y" or "f1":
                Tell(slot, _vote.Say(slot, yes: true) ? $"You voted yes. {Tally()}" : "There is no vote to say yes to.");
                return true;
            case "no" or "n" or "f2":
                Tell(slot, _vote.Say(slot, yes: false) ? $"You voted no. {Tally()}" : "There is no vote to say no to.");
                return true;
            case [>= '1' and <= '9'] pick when _mapChoices.Length > 0:
                int index = pick[0] - '1';
                if (index < _mapChoices.Length && Match.Players.ContainsKey(slot))
                {
                    _mapPicks[slot] = index;
                    Tell(slot, string.Create(CultureInfo.InvariantCulture, $"You picked mission {_mapChoices[index].Mission}."));
                }

                return true;
            case "buy" or "b":
                Tell(slot, Mode.Buy(this, slot, words.Length > 1 ? words[1] : string.Empty, _tick));
                return true;
            case "vote" or "callvote":
                CallVote(slot, words[1..]);
                return true;
            default:
                return false;
        }
    }

    private void CallVote(int slot, string[] words)
    {
        if (!Match.Players.ContainsKey(slot))
        {
            Tell(slot, "Only a player can call a vote.");
            return;
        }

        (VoteResult Vote, string Question)? asked = words switch
        {
            ["kick", .. var who] when who.Length > 0 => PlayerNamed(string.Join(' ', who)) is { } target
                ? (new VoteResult(VoteKind.Kick, Slot: target.Slot), $"put {target.Name} off the server")
                : null,
            ["map", var number, .. var rest] when int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mission) && mission > 0
                => (new VoteResult(VoteKind.Map, Mission: mission, Mode: rest is [var mode, ..] ? mode.ToLowerInvariant() : string.Empty),
                    rest is [var named, ..] ? $"go on to mission {mission}, {named.ToUpperInvariant()}" : $"go on to mission {mission}"),
            ["scramble"] => (new VoteResult(VoteKind.Scramble), "deal the teams again and start over"),
            ["restart"] => (new VoteResult(VoteKind.Restart), "start the match over"),
            _ => null,
        };

        if (asked is not { } call)
        {
            Tell(slot, "Votes: /vote kick <name>, /vote map <mission> [mode], /vote scramble, /vote restart; then /yes or /no.");
            return;
        }

        if (call.Vote.Kind == VoteKind.Map && ChangeMap is null)
        {
            Tell(slot, "This server doesn't change map by vote.");
            return;
        }

        if (call.Vote.Kind == VoteKind.Map && call.Vote.Mode.Length > 0 && !GameModes.All.Contains(call.Vote.Mode))
        {
            Tell(slot, $"'{call.Vote.Mode}' is not a mode: {string.Join(", ", GameModes.All)}.");
            return;
        }

        if (_vote.Call(slot, call.Vote, call.Question, _now) is { } refused)
        {
            Tell(slot, $"Can't call that vote: {refused}.");
            return;
        }

        string caller = Match.Players[slot].Name;
        Say(string.Create(CultureInfo.InvariantCulture, $"{caller} calls a vote to {call.Question}. /yes or /no -- {MatchVote.Seconds:0} s. {Tally()}"));
    }

    // Each tick: a vote carried or lost, and a player idle too long.
    private void TickSay(double now)
    {
        if (_vote.Settle([.. Match.Players.Keys], now, out bool passed) is { } settled)
        {
            string question = settled.Kind switch
            {
                VoteKind.Kick => "the kick",
                VoteKind.Map => "the change of map",
                VoteKind.Scramble => "the scramble",
                _ => "the restart",
            };
            Say(passed ? $"The vote for {question} passed." : $"The vote for {question} failed.");
            if (passed)
            {
                Carry(settled);
            }
        }

        if (IdleKickSeconds <= 0)
        {
            return;
        }

        foreach ((int slot, ClientState client) in _clients.ToArray())
        {
            if (now - client.ActiveAt > IdleKickSeconds)
            {
                string name = client.Name;
                Remove(slot, "idle too long");
                _barred.Remove(client.PlayerId);
                Say($"{name} was put off for standing idle.");
            }
        }
    }

    private void Carry(VoteResult vote)
    {
        switch (vote.Kind)
        {
            case VoteKind.Kick when _clients.ContainsKey(vote.Slot):
                Remove(vote.Slot, "voted off by the other players");
                break;
            case VoteKind.Map when ChangeMap is { } change:
                if (change(vote.Mission, vote.Mode.Length > 0 ? vote.Mode : null) is { } problem)
                {
                    Say($"The map can't change: {problem}");
                }

                break;
            case VoteKind.Scramble:
                Scramble();
                RestartMode();
                break;
            case VoteKind.Restart:
                RestartMode();
                break;
        }
    }

    // The teams dealt again at random, as even as the players allow.
    private void Scramble()
    {
        int[] slots = [.. Match.Players.Keys];
        Random deal = new(unchecked((int)(_now * 1000)));
        deal.Shuffle(slots);
        for (int i = 0; i < slots.Length; i++)
        {
            Match.Players[slots[i]].Team = i % 2 == 0 ? TeamRoster.Good : TeamRoster.Bad;
        }
    }

    // A player by its name, or an unambiguous start of it.
    private MatchPlayer? PlayerNamed(string name)
    {
        MatchPlayer? exact = Match.Players.Values.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        MatchPlayer[] starting = [.. Match.Players.Values.Where(p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))];
        return starting.Length == 1 ? starting[0] : null;
    }

    private string Tally() => string.Create(CultureInfo.InvariantCulture, $"Yes {_vote.Yes}, no {_vote.No}.");

    // A line to one player only: a joined one by its connection, the listen host's own as a line of the room.
    private void Tell(int slot, string text)
    {
        _writer.Reset();
        SessionTalk.WriteChat(_writer, HostEntity, text);
        if (slot == HostSlot)
        {
            _heard.Add(new ChatLine(HostEntity, text));
            return;
        }

        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }

    // A command that differs from the last one: its player is at the keys.
    private static bool Differs(float[] channels, float[]? last)
    {
        if (last is null || last.Length != channels.Length)
        {
            return true;
        }

        for (int i = 0; i < channels.Length; i++)
        {
            if (Math.Abs(channels[i] - last[i]) > 1e-3f)
            {
                return true;
            }
        }

        return false;
    }
}

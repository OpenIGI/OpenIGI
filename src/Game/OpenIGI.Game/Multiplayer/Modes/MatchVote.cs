using System.Globalization;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>What a vote is called on.</summary>
public enum VoteKind
{
    /// <summary>A player put off the server.</summary>
    Kick,

    /// <summary>Another mission, and perhaps another mode.</summary>
    Map,

    /// <summary>The teams dealt again, and the match started over.</summary>
    Scramble,

    /// <summary>The match started over as it is.</summary>
    Restart,
}

/// <summary>A vote passed: what it was on.</summary>
/// <param name="Kind">What it was on.</param>
/// <param name="Slot">The player a kick names; -1 otherwise.</param>
/// <param name="Mission">The mission a map vote names; 0 otherwise.</param>
/// <param name="Mode">The mode a map vote names, or empty to keep the one played.</param>
public readonly record struct VoteResult(VoteKind Kind, int Slot = -1, int Mission = 0, string Mode = "");

/// <summary>
/// The players' votes on a server: one at a time, called by a player and carried by more than half of those playing, each
/// player one say. The caller counts as yes; the one a kick names has no say; a vote not carried in time fails.
/// </summary>
/// <remarks>
/// Every rule a vote needs is here and nothing of the network, so the server's chat commands are a thin reading of it:
/// <c>/vote kick name</c>, <c>/vote map 3 [mode]</c>, <c>/vote scramble</c>, <c>/vote restart</c>, <c>/yes</c>, <c>/no</c>.
/// </remarks>
public sealed class MatchVote
{
    /// <summary>How long a vote runs, in seconds.</summary>
    public const double Seconds = 30;

    /// <summary>How long a player waits after calling one before calling another, in seconds.</summary>
    public const double CallerCooldownSeconds = 60;

    private readonly Dictionary<int, bool> _says = [];
    private readonly Dictionary<int, double> _calledAt = [];

    /// <summary>Gets the vote running, or <see langword="null"/> for none.</summary>
    public VoteResult? Running { get; private set; }

    /// <summary>Gets what the running vote is on, in words, or empty.</summary>
    public string Question { get; private set; } = string.Empty;

    /// <summary>Gets when the running vote ends, on the session's clock.</summary>
    public double EndsAt { get; private set; }

    /// <summary>Gets how many said yes to the running vote.</summary>
    public int Yes => _says.Count(static s => s.Value);

    /// <summary>Gets how many said no to the running vote.</summary>
    public int No => _says.Count(static s => !s.Value);

    /// <summary>Calls a vote.</summary>
    /// <param name="caller">The calling player's slot.</param>
    /// <param name="vote">What it is on.</param>
    /// <param name="question">What it is on, in words, as everyone is asked.</param>
    /// <param name="now">The session's clock.</param>
    /// <returns><see langword="null"/> when it is called; otherwise why not, to tell the caller.</returns>
    public string? Call(int caller, VoteResult vote, string question, double now)
    {
        if (Running is not null)
        {
            return "a vote is already running";
        }

        if (_calledAt.TryGetValue(caller, out double last) && now - last < CallerCooldownSeconds)
        {
            return string.Create(CultureInfo.InvariantCulture, $"you can call another vote in {CallerCooldownSeconds - (now - last):0} s");
        }

        if (vote.Kind == VoteKind.Kick && vote.Slot == caller)
        {
            return "you can't vote yourself off";
        }

        _calledAt[caller] = now;
        _says.Clear();
        _says[caller] = true;
        Running = vote;
        Question = question;
        EndsAt = now + Seconds;
        return null;
    }

    /// <summary>Gives a player's say on the running vote; a second say replaces the first.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="yes">Whether it is yes.</param>
    /// <returns><see langword="false"/> when there is no vote to say anything to, or the player is the one it would kick.</returns>
    public bool Say(int slot, bool yes)
    {
        if (Running is not { } vote || (vote.Kind == VoteKind.Kick && vote.Slot == slot))
        {
            return false;
        }

        _says[slot] = yes;
        return true;
    }

    /// <summary>Settles the running vote once it is carried, lost or out of time.</summary>
    /// <param name="players">The slots of everyone playing now.</param>
    /// <param name="now">The session's clock.</param>
    /// <param name="passed">Whether it was carried, when it was settled.</param>
    /// <returns>The vote settled, or <see langword="null"/> while it runs on (or none runs).</returns>
    public VoteResult? Settle(IReadOnlyCollection<int> players, double now, out bool passed)
    {
        ArgumentNullException.ThrowIfNull(players);
        passed = false;
        if (Running is not { } vote)
        {
            return null;
        }

        // A player who left has no say; the one a kick names has none at all.
        foreach (int gone in _says.Keys.Where(s => !players.Contains(s)).ToList())
        {
            _says.Remove(gone);
        }

        int voters = players.Count(p => vote.Kind != VoteKind.Kick || p != vote.Slot);
        int needed = (voters / 2) + 1;
        bool kickedGone = vote.Kind == VoteKind.Kick && !players.Contains(vote.Slot);
        if (Yes >= needed && !kickedGone)
        {
            passed = true;
        }
        else if (!kickedGone && voters - No >= needed && now < EndsAt)
        {
            return null;
        }

        Running = null;
        Question = string.Empty;
        _says.Clear();
        return vote;
    }

    /// <summary>Forgets a player who left: its cooldown goes with it.</summary>
    /// <param name="slot">The player's slot.</param>
    public void Forget(int slot)
    {
        _calledAt.Remove(slot);
        _says.Remove(slot);
    }
}

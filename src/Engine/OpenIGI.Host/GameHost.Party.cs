using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Configuration;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net.Browsing;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host;

/// <content>
/// Parties: friends who play together. One makes a party and hands its code round; whatever server or room its leader then
/// joins, the others' games join too.
/// </content>
public sealed partial class GameHost
{
    private const string PartyCategory = "Party";

    private readonly System.Diagnostics.Stopwatch _partyClock = System.Diagnostics.Stopwatch.StartNew();

    private Party? _party;

    // What the party looked like when last reported, so only a change is told.
    private string _partyReported = string.Empty;

    private void RegisterPartyCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "party",
            "Shows the party you are in: its code, who leads it, who is in it, and where it plays.",
            context =>
            {
                foreach (string line in PartyLines())
                {
                    context.Print(line);
                }
            });
        console.Commands.Add(
            "party_create",
            "Makes a party you lead; its code is copied for friends to join with party_join. Wherever you play, they follow.",
            context =>
            {
                LeaveParty();
                _party = Party.Create(RoomTransport.DefaultMaster, PartyName());
                context.Print("Making a party...");
            });
        console.Commands.Add(
            "party_join",
            "Joins a friend's party by its code; your game then follows its leader to every server and room.",
            context =>
            {
                if (context.Count == 0)
                {
                    context.Error("Name the party's code: party_join <code>.");
                    return;
                }

                LeaveParty();
                _party = Party.Join(RoomTransport.DefaultMaster, context[0], PartyName());
                context.Print($"Joining party {context[0].ToUpperInvariant()}...");
            },
            "<code>");
        console.Commands.Add(
            "party_leave",
            "Leaves the party; the next oldest member leads it.",
            context =>
            {
                context.Print(_party is null ? "You are in no party." : "Left the party.");
                LeaveParty();
            });
    }

    // Each frame: the party told where this game plays, and its leader followed.
    private void UpdateParty()
    {
        if (_party is not { } party)
        {
            return;
        }

        string? follow = party.Update(_partyClock.Elapsed.TotalSeconds, PartyPlace());
        ReportParty(party);
        if (party.Failure is { } failure)
        {
            _logger.Warning(PartyCategory, $"The party is lost: {failure}.");
            _party = null;
            _partyReported = string.Empty;
            party.Dispose();
            return;
        }

        if (follow is null)
        {
            return;
        }

        if (_coop is null)
        {
            _logger.Warning(PartyCategory, $"The party's leader went to {follow}, but this build can't join it from here.");
            return;
        }

        _logger.Information(PartyCategory, $"Following the party's leader to {follow}.");
        _coop.Follow(follow);
    }

    // Where this game plays, as a party says it: the room hosted or joined, or the server joined by address; a demo or
    // a server hosted on this machine without a room is somewhere nobody else can be sent.
    private string? PartyPlace() => TickObserver switch
    {
        HostedSession hosted => hosted.RoomCode,
        SessionClient { Playback: null } joined => joined.RoomCode ?? BrowserServer.KeyOf(joined.Server),
        _ => null,
    };

    private string PartyName() => _configuration.Active?.Name is { Length: > 0 } name ? name : "Jones";

    private void LeaveParty()
    {
        _party?.Dispose();
        _party = null;
        _partyReported = string.Empty;
    }

    // Who leads, who is in, and where the party plays, logged when that changes; the code copied the moment a party is made.
    private void ReportParty(Party party)
    {
        if (party.State is not { } state)
        {
            return;
        }

        string now = string.Join('|', PartyLines());
        if (now == _partyReported)
        {
            return;
        }

        if (_partyReported.Length == 0 && party.IsLeader)
        {
            _window.ClipboardText = state.Code;
        }

        _partyReported = now;
        foreach (string line in PartyLines())
        {
            _logger.Information(PartyCategory, line);
        }
    }

    private IEnumerable<string> PartyLines()
    {
        if (_party is not { } party)
        {
            yield return "You are in no party: party_create makes one, party_join <code> joins a friend's.";
            yield break;
        }

        if (party.State is not { } state)
        {
            yield return party.Failure is { } failure ? $"The party is lost: {failure}." : "Reaching the party...";
            yield break;
        }

        string leader = state.Members.FirstOrDefault(m => m.Id == state.Leader)?.Name ?? "nobody";
        yield return $"Party {state.Code} ({state.Members.Count}): led by {leader}{(party.IsLeader ? " (you)" : string.Empty)}.";
        yield return $"In it: {string.Join(", ", state.Members.Select(m => m.Name))}.";
        yield return state.Target is { } target ? $"Plays at {target}." : "Plays nowhere yet.";
    }
}

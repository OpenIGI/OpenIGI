using System.Globalization;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The party, in the page's header: friends who play together. A square for each member -- its initial, the leader's
/// underlined -- and open places, which invite a friend; the party's code with COPY; JOIN A PARTY to join a friend's by its
/// code, in a box ENTER joins from and ESC shuts. A member's square picks it, and its line opens under the header: where it plays, and for the leader LEAD and KICK.
/// </content>
public sealed partial class MultiplayerScreen
{
    // How many places the header shows at least, and how many a party takes.
    private const int ShownPlaces = 4;
    private const int PartyPlaces = 8;

    private string _partyCode = string.Empty;

    // What the party showed when last built, so the page is rebuilt only when that changes.
    private string _shownParty = string.Empty;

    // The link last copied, said until the page is left; whether the box to join a party by its code is open; the member picked.
    private string? _copied;
    private bool _joiningParty;
    private int? _picked;

    private IPartyPanel? Parties => Context.Host.Party;

    private string PartyKey => Parties is { } parties ? $"{parties.Party?.Key}|{parties.Failure}|{parties.OrderError}|{_copied}" : string.Empty;

    private StackPanel BuildPartyCluster(UiTheme theme, IPartyPanel parties)
    {
        _shownParty = PartyKey;
        PartyView? party = parties.Party;
        var cluster = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24f };

        var about = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, VerticalAlignment = VerticalAlignment.Center };
        string count = party is { Code: not null }
            ? string.Create(CultureInfo.InvariantCulture, $"PARTY · {party.Members.Count}/{PartyPlaces}")
            : "PARTY";
        about.AddChild(new Eyebrow(theme, count) { HorizontalAlignment = HorizontalAlignment.Right });
        about.AddChild(new Label(theme.Body, "Wherever the leader plays, everyone follows.", 14f, theme.TextSecondary) { HorizontalAlignment = HorizontalAlignment.Right });
        cluster.AddChild(about);

        var places = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
        List<PartyMemberView> members = party?.Members is { Count: > 0 } known ? [.. known] : [new PartyMemberView(-1, ProfileName(), true, true, string.Empty)];
        foreach (PartyMemberView member in members)
        {
            int id = member.Id;
            var slot = new PartySlot(theme, Initial(member.Name))
            {
                Leads = member.Leads,
                IsYou = member.IsYou,
                Selected = _picked == id,
                Tooltip = member.Leads ? $"{member.Name} · leader" : member.Name,
            };
            slot.Activated += _ =>
            {
                _picked = _picked == id || id < 0 ? null : id;
                Invalidate();
            };
            places.AddChild(slot);
        }

        for (int i = members.Count; i < Math.Max(ShownPlaces, Math.Min(members.Count + 1, PartyPlaces)); i++)
        {
            var open = new PartySlot(theme, null) { Tooltip = party is null ? "Invite a friend: makes a party and copies its link." : "Invite a friend: copies the party's link." };
            open.Activated += _ => Invite(parties);
            places.AddChild(open);
        }

        cluster.AddChild(places);

        var code = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
        code.AddChild(new Label(theme.Mono, "INVITE CODE", 10f, theme.TextMuted));
        var codeLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f };
        codeLine.AddChild(new Label(theme.Mono, party?.Code ?? (party is null ? "------" : "…"), 18f, theme.Text) { VerticalAlignment = VerticalAlignment.Center });
        var copy = new Button(theme, _copied is null ? (party is null ? "CREATE" : "COPY") : "COPIED")
        {
            Accent = ButtonAccent.Ghost,
            TextSize = 13f,
            Tracking = 0.25f,
            Padding = new Thickness(10f, 4f),
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = party is null ? "Make a party you lead, and copy its invite link for friends." : "Copy a link friends open to join the party, in the browser or the game.",
        };
        copy.Activated += _ => Invite(parties);
        codeLine.AddChild(copy);
        code.AddChild(codeLine);
        cluster.AddChild(new Rule { Orientation = Orientation.Vertical, Length = 48f, Color = UiPalette.White(0.1f), VerticalAlignment = VerticalAlignment.Center });
        cluster.AddChild(code);

        if (party is not null)
        {
            var leave = new Button(theme, "LEAVE PARTY") { Accent = ButtonAccent.Ghost, TextSize = 15f, Tracking = 0.3f, Padding = new Thickness(18f, 14f), VerticalAlignment = VerticalAlignment.Center };
            leave.Activated += _ =>
            {
                parties.Leave();
                _copied = null;
                _picked = null;
                Invalidate();
            };
            cluster.AddChild(leave);
        }
        else if (_joiningParty)
        {
            var join = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox(theme) { Id = PartyCodeId, Placeholder = "PARTY CODE", MaxLength = 6, Width = 170f, VerticalAlignment = VerticalAlignment.Center };
            box.Text = _partyCode;
            var go = new Button(theme, "JOIN") { Accent = ButtonAccent.Ghost, TextSize = 15f, Tracking = 0.3f, Padding = new Thickness(18f, 14f), IsEnabled = HasCode(_partyCode), Tooltip = "Join a friend's party by its code." };
            box.TextChanged += text =>
            {
                _partyCode = text;
                go.IsEnabled = HasCode(text);
            };
            go.Activated += _ => JoinParty(parties);
            join.AddChild(box);
            join.AddChild(go);
            cluster.AddChild(join);
        }
        else
        {
            var open = new Button(theme, "JOIN A PARTY") { Accent = ButtonAccent.Ghost, TextSize = 15f, Tracking = 0.3f, Padding = new Thickness(18f, 14f), VerticalAlignment = VerticalAlignment.Center };
            open.Activated += _ =>
            {
                // The box opens with the keys already in it, so the code is typed straight away.
                _joiningParty = true;
                _focusNext = PartyCodeId;
                Invalidate();
            };
            cluster.AddChild(open);
        }

        return cluster;
    }

    // Under the header: why a party was lost or an order refused, where the party plays, and the member picked with LEAD and KICK.
    private StackPanel? BuildPartyNote(UiTheme theme, IPartyPanel parties)
    {
        PartyView? party = parties.Party;
        var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16f, HorizontalAlignment = HorizontalAlignment.Right };
        if (party is null)
        {
            if (parties.Failure is not { } lost)
            {
                return null;
            }

            note.AddChild(StatusText(theme, $"The party was lost: {lost}.", theme.Danger, 15f));
            return note;
        }

        if (parties.OrderError is { } refused)
        {
            note.AddChild(StatusText(theme, $"Couldn't do that: {refused}.", theme.Danger, 15f));
        }

        if (_copied is { } link)
        {
            note.AddChild(StatusText(theme, $"Copied {link}", theme.Accent, 15f));
        }

        if (party.Members.FirstOrDefault(m => m.Id == _picked) is not { } member)
        {
            string where = party.Code is null ? "Reaching the party…"
                : party.Playing is { } playing ? party.Leading ? $"You lead it, in {playing}." : $"Following {party.Leader} to {playing}."
                : party.Leading ? "You lead it: host or join a game and the party follows." : $"Waiting for {party.Leader} to play somewhere.";
            note.AddChild(StatusText(theme, where, theme.TextMuted, 15f));
            return note;
        }

        note.AddChild(new Label(theme.Body, member.Name, 17f, theme.TextBright) { VerticalAlignment = VerticalAlignment.Center });
        if (member.Leads || member.IsYou)
        {
            note.AddChild(new Label(theme.Mono, member.Leads ? (member.IsYou ? "LEADER · YOU" : "LEADER") : "YOU", 11f, theme.Accent) { VerticalAlignment = VerticalAlignment.Center });
        }

        if (member.Where.Length > 0)
        {
            note.AddChild(new Label(theme.Body, member.Where, 15f, member.Where == "with the leader" ? theme.Success : theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center });
        }

        if (party.Leading && !member.IsYou)
        {
            int id = member.Id;
            var lead = new Button(theme, "MAKE LEADER") { Accent = ButtonAccent.Ghost, TextSize = 13f, Tracking = 0.2f, Padding = new Thickness(12f, 6f), VerticalAlignment = VerticalAlignment.Center, Tooltip = $"Hand the party's lead to {member.Name}: the party then follows them." };
            lead.Activated += _ =>
            {
                parties.Promote(id);
                Invalidate();
            };
            var kick = new Button(theme, "KICK") { Accent = ButtonAccent.Ghost, TextSize = 13f, Tracking = 0.2f, Padding = new Thickness(12f, 6f), VerticalAlignment = VerticalAlignment.Center, Tooltip = $"Put {member.Name} out of the party." };
            kick.Activated += _ =>
            {
                parties.Kick(id);
                _picked = null;
                Invalidate();
            };
            note.AddChild(lead);
            note.AddChild(kick);
        }

        return note;
    }

    // An open place, CREATE or COPY: make a party when there is none, and copy its link once there is.
    private void Invite(IPartyPanel parties)
    {
        if (parties.Party is null)
        {
            parties.Create();
        }
        else
        {
            _copied = parties.CopyInvite();
        }

        Invalidate();
    }

    // Joins the party whose code is in the box, when one is, and shuts the box.
    private void JoinParty(IPartyPanel parties)
    {
        if (!HasCode(_partyCode))
        {
            return;
        }

        parties.Join(_partyCode.Trim());
        _joiningParty = false;
        Invalidate();
    }

    private string ProfileName() => Context.Config.Active?.Name is { Length: > 0 } name ? name : "You";

    private static string Initial(string name) => name.Trim() is { Length: > 0 } trimmed ? trimmed[..1].ToUpperInvariant() : "?";
}

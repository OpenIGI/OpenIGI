using System.Globalization;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The party, in the page's header: friends who play together. A square for each member -- its initial, the leader's
/// underlined -- and open places, which invite a friend, as INVITE does; the party's code under its count; ENTER CODE, which
/// opens a box a code is typed in to join a friend's room (ENTER, or ROOM) or their party (PARTY), and which ESC shuts. A
/// member's square picks it, and its line opens under the header: where it plays, and for the leader LEAD and KICK.
/// </content>
public sealed partial class MultiplayerScreen
{
    // How many places a party takes: the wide page shows them all, a narrower one the members and one open place.
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
        var cluster = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16f };

        List<PartyMemberView> members = party?.Members is { Count: > 0 } known ? [.. known] : [new PartyMemberView(-1, ProfileName(), true, true, string.Empty)];
        var about = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f, VerticalAlignment = VerticalAlignment.Center };
        about.AddChild(new Eyebrow(theme, string.Create(CultureInfo.InvariantCulture, $"Party · {members.Count} / {PartyPlaces}")) { TextSize = 14f, HorizontalAlignment = HorizontalAlignment.Right });
        if (party is not null)
        {
            // The party's code, for reading out to a friend; INVITE copies the link that carries it.
            about.AddChild(new Label(theme.Mono, party.Code ?? "…", 13f, theme.Text) { HorizontalAlignment = HorizontalAlignment.Right });
        }

        cluster.AddChild(about);

        var places = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
        foreach (PartyMemberView member in members)
        {
            int id = member.Id;
            var slot = new PartySlot(theme, Initial(member.Name))
            {
                Leads = member.Leads && members.Count > 1,
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

        int shown = Size == ShellSize.Wide ? PartyPlaces : Math.Min(members.Count + 1, PartyPlaces);
        for (int i = members.Count; i < shown; i++)
        {
            var open = new PartySlot(theme, null) { Tooltip = party is null ? "Invite a friend: makes a party and copies its link." : "Invite a friend: copies the party's link." };
            open.Activated += _ => Invite(parties);
            places.AddChild(open);
        }

        cluster.AddChild(places);

        var invite = new Button(theme, _copied is null ? "INVITE" : "COPIED")
        {
            Accent = ButtonAccent.Ghost,
            LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.People),
            GlyphSize = 18f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(20f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = party is null ? "Make a party you lead, and copy its invite link for friends." : "Copy a link friends open to join the party, in the browser or the game.",
        };
        invite.Activated += _ => Invite(parties);
        cluster.AddChild(invite);

        if (_joiningParty)
        {
            cluster.AddChild(BuildCodeEntry(theme, parties, inParty: party is not null));
        }
        else
        {
            var enter = new Button(theme, "ENTER CODE")
            {
                Accent = ButtonAccent.Ghost,
                LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Keyboard),
                GlyphSize = 18f,
                TextSize = ButtonText,
                Tracking = ButtonTracking,
                Padding = new Thickness(20f, 0f),
                Height = 44f,
                VerticalAlignment = VerticalAlignment.Center,
                Tooltip = "Join a friend's room or party by its code.",
            };
            enter.Activated += _ =>
            {
                // The box opens with the keys already in it, so the code is typed straight away.
                _joiningParty = true;
                _focusNext = PartyCodeId;
                Invalidate();
            };
            cluster.AddChild(enter);
        }

        if (party is not null)
        {
            var leave = new Button(theme, "LEAVE") { Accent = ButtonAccent.Ghost, TextSize = ButtonText, Tracking = ButtonTracking, Padding = new Thickness(16f, 0f), Height = 44f, VerticalAlignment = VerticalAlignment.Center, Tooltip = "Leave the party." };
            leave.Activated += _ =>
            {
                parties.Leave();
                _copied = null;
                _picked = null;
                Invalidate();
            };
            cluster.AddChild(leave);
        }

        return cluster;
    }

    // The box ENTER CODE opens: a code, and what to join by it -- a friend's room, or their party when this player is in none.
    private StackPanel BuildCodeEntry(UiTheme theme, IPartyPanel parties, bool inParty)
    {
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox(theme) { Id = PartyCodeId, Placeholder = "CODE", MaxLength = 6, Width = 150f, VerticalAlignment = VerticalAlignment.Center, Tooltip = "A friend's room or party code: six letters and digits." };
        box.Text = _partyCode;
        var room = new Button(theme, "ROOM") { Accent = ButtonAccent.Ghost, TextSize = ButtonText, Tracking = ButtonTracking, Padding = new Thickness(14f, 0f), Height = 44f, IsEnabled = HasCode(_partyCode), Tooltip = "Join the room with this code." };
        var joinParty = new Button(theme, "PARTY") { Accent = ButtonAccent.Ghost, TextSize = ButtonText, Tracking = ButtonTracking, Padding = new Thickness(14f, 0f), Height = 44f, IsEnabled = HasCode(_partyCode), Tooltip = "Join the party with this code." };
        box.TextChanged += text =>
        {
            _partyCode = text;
            room.IsEnabled = HasCode(text);
            joinParty.IsEnabled = HasCode(text);
        };
        room.Activated += _ => JoinRoomByEnteredCode();
        joinParty.Activated += _ => JoinParty(parties);
        entry.AddChild(box);
        entry.AddChild(room);
        if (!inParty)
        {
            entry.AddChild(joinParty);
        }

        return entry;
    }

    // Joins the room whose code is in ENTER CODE's box, when one is, and shuts the box.
    private void JoinRoomByEnteredCode()
    {
        if (!HasCode(_partyCode) || Coop is not { } coop)
        {
            return;
        }

        coop.Join(_partyCode.Trim());
        _joiningParty = false;
        Invalidate();
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
            // A leader with nowhere to be yet has nothing to be told: the page is where the leader picks.
            string? where = party.Code is null ? "Reaching the party…"
                : party.Playing is { } playing ? party.Leading ? $"You lead it, in {playing}." : $"Following {party.Leader} to {playing}."
                : party.Leading ? null : $"Waiting for {party.Leader} to play somewhere.";
            if (where is not null)
            {
                note.AddChild(StatusText(theme, where, theme.TextMuted, 15f));
            }

            return note.Children.Count > 0 ? note : null;
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

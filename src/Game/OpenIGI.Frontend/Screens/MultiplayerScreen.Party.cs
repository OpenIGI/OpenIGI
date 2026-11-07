using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The party: friends who play together. Out of one, a button to make one and a box to join a friend's by its code; in one,
/// its code, who leads and who is in it, where its leader plays, and the invite link to hand round.
/// </content>
public sealed partial class MultiplayerScreen
{
    private string _partyCode = string.Empty;

    // What the party panel showed when last built, so the page is rebuilt only when that changes.
    private string _shownParty = string.Empty;

    // The link last copied, said under the party until the page is left.
    private string? _copied;

    private IPartyPanel? Parties => Context.Host.Party;

    private string PartyKey => Parties is { } parties ? $"{parties.Party?.Key}|{parties.Failure}|{_copied}" : string.Empty;

    private StackPanel BuildParty(UiTheme theme, IPartyPanel parties)
    {
        _shownParty = PartyKey;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        column.AddChild(new GroupHeader(theme, "Party"));
        if (parties.Party is not { } party)
        {
            column.AddChild(Body(
                theme,
                parties.Failure is { } lost ? $"The party was lost: {lost}." : "Play as a group: wherever the party's leader plays, everyone in it follows.",
                parties.Failure is null ? theme.TextMuted : theme.Danger));
            var line = new Grid { ColumnSpacing = 12f };
            line.RowDefinitions.Add(GridLength.Auto);
            line.ColumnDefinitions.Add(GridLength.Auto);
            line.ColumnDefinitions.Add(GridLength.Star);
            line.ColumnDefinitions.Add(GridLength.Auto);
            line.Margin = new Thickness(0f, 0f, 0f, 4f);
            var create = new Button(theme, "NEW PARTY") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), Tooltip = "Make a party you lead, and copy its invite link for friends." };
            create.Activated += _ =>
            {
                parties.Create();
                Invalidate();
            };
            var code = new TextBox(theme) { Id = "party-code", Placeholder = "PARTY CODE", MaxLength = 6, VerticalAlignment = VerticalAlignment.Center };
            code.Text = _partyCode;
            code.TextChanged += text => _partyCode = text;
            var join = new Button(theme, "JOIN") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), Tooltip = "Join a friend's party by its code." };
            join.Activated += _ =>
            {
                parties.Join(_partyCode);
                Invalidate();
            };
            line.Add(create, 0, 0);
            line.Add(code, 0, 1);
            line.Add(join, 0, 2);
            column.AddChild(line);
            return column;
        }

        if (party.Code is not { } partyCode)
        {
            column.AddChild(Body(theme, "Reaching the party…", theme.TextMuted));
            return column;
        }

        var (panel, content) = Group(theme, $"Party {partyCode}");
        string members = string.Join(", ", party.Members.Select(m => m == party.Leader ? $"{m} (leads)" : m));
        content.AddChild(Body(theme, members, UiPalette.TextBody));
        string where = party.Playing is { } playing
            ? party.Leading ? $"You lead it, in {playing}." : $"Following {party.Leader} to {playing}."
            : party.Leading ? "You lead it: host or join a game and the party follows." : $"Waiting for {party.Leader} to play somewhere.";
        content.AddChild(Body(theme, where, theme.TextMuted));
        if (_copied is { } link)
        {
            content.AddChild(Body(theme, $"Copied {link}", theme.Accent));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f };
        var invite = new Button(theme, "COPY INVITE") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), Tooltip = "Copy a link friends open to join the party, in the browser or the game." };
        invite.Activated += _ =>
        {
            _copied = parties.CopyInvite();
            Invalidate();
        };
        var leave = new Button(theme, "LEAVE PARTY") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f) };
        leave.Activated += _ =>
        {
            parties.Leave();
            _copied = null;
            Invalidate();
        };
        buttons.AddChild(invite);
        buttons.AddChild(leave);
        content.AddChild(buttons);
        column.AddChild(panel);
        return column;
    }
}

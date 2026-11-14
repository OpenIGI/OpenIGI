using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The page on a phone held sideways, or a small window: the list the width of the screen, its lines two deep and a
/// thumb tall, and the game picked shown on a sheet over it with Join at its foot.
/// </content>
/// <remarks>
/// The monitor's page has the list and the picked game side by side under a row of filters, some 1,500 units across.
/// A phone has 760. So the picked game moves onto a sheet a tap raises and Back lowers, the filters become two buttons
/// that step through their choices, and hosting is one column that scrolls with Host at its end.
/// </remarks>
public sealed partial class MultiplayerScreen
{
    // Whether the picked game's sheet is up, on the compact page.
    private bool _sheet;

    // Whether the compact page's search box is open.
    private bool _searching;

    private Grid BuildCompact(UiTheme theme, ICoopRooms coop)
    {
        var page = new Grid { RowSpacing = 6f, Margin = new Thickness(SideMargin, 6f, SideMargin, 0f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);

        var tabs = new TabStrip(theme, ["Browse", "Host"]) { SegmentHeight = 36f, TabPadding = 22f, TextSize = 15f, VerticalAlignment = VerticalAlignment.Center };
        tabs.SelectedIndex = _tab;
        tabs.SelectionChanged += index =>
        {
            _tab = index;
            _sheet = false;
            ShowContent();
            RequestLayout();
            Focus?.Validate();
            RefreshHints();
        };
        _tabs = tabs;

        var header = new FlexPanel { Orientation = Orientation.Horizontal };
        header.Add(new SectionTitle(theme, string.Empty, "Multiplayer") { TitleSize = 24f, VerticalAlignment = VerticalAlignment.Center });
        header.Add(new Panel(), grow: 1f);
        header.Add(tabs);
        page.Add(header, 0, 0);

        if (BuildStatus(theme, coop.Status) is { } status)
        {
            page.Add(status, 1, 0);
        }

        IReadOnlyList<CoopRoomInfo>? all = coop.Listing;
        List<CoopRoomInfo> passing = Passing(all);
        List<CoopRoomInfo> shown = Order([.. passing.Where(r => KindShown(r, _kind))]);
        CoopRoomInfo? picked = shown.FirstOrDefault(r => r.Code == _selectedCode);

        _browseTab = BuildCompactBrowse(theme, coop, all, passing, shown);
        _hostTab = BuildCompactHost(theme, coop);
        page.Add(_browseTab, 2, 0);
        page.Add(_hostTab, 2, 0);
        ShowContent();

        if (_sheet && picked is not null && _tab == 0)
        {
            page.Add(BuildSheet(theme, coop, picked), 2, 0);
        }
        else
        {
            _sheet = false;
        }

        return page;
    }

    private Grid BuildCompactBrowse(UiTheme theme, ICoopRooms coop, IReadOnlyList<CoopRoomInfo>? all, List<CoopRoomInfo> passing, List<CoopRoomInfo> shown)
    {
        var layout = new Grid { RowSpacing = 8f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        // What there is to do before picking a line: the party, the last game, a friend's code.
        var bar = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 8f };
        if (Parties is { } parties)
        {
            PartyView? party = parties.Party;
            int members = party?.Members.Count ?? 1;
            Button invite = CompactButton(theme, _copied is null ? string.Create(CultureInfo.InvariantCulture, $"Party {members}/{PartyPlaces}") : "Link copied", ButtonAccent.Ghost);
            invite.LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.People);
            invite.Tooltip = party is null ? "Make a party you lead, and copy its invite link for friends." : "Copy a link friends open to join the party.";
            invite.Activated += _ => Invite(parties);
            bar.Add(invite);
        }

        if (coop.LastPlayed is not null)
        {
            Button reconnect = CompactButton(theme, "Reconnect", ButtonAccent.Ghost);
            reconnect.LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.History);
            reconnect.Activated += _ => coop.Reconnect();
            bar.Add(reconnect);
        }

        if (Parties is { } forCode)
        {
            Button enter = CompactButton(theme, "Enter code", ButtonAccent.Ghost);
            enter.LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Keyboard);
            enter.Selected = _joiningParty;
            enter.Activated += _ =>
            {
                _joiningParty = !_joiningParty;
                _focusNext = _joiningParty ? PartyCodeId : null;
                Invalidate();
            };
            bar.Add(enter);
            if (_joiningParty)
            {
                layout.Add(BuildCodeEntry(theme, forCode, inParty: forCode.Party is not null), 2, 0);
            }
        }

        layout.Add(bar, 0, 0);

        // Which lines show -- each steps to its next choice when pressed: a strip of six kinds has no room here -- and at
        // the other end the box to search them, the best game listed, and the list asked for again.
        var filters = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 8f };
        int ofKind = passing.Count(r => KindShown(r, _kind));
        Button kind = CompactButton(theme, string.Create(CultureInfo.InvariantCulture, $"{Kinds[_kind]} · {ofKind}"), ButtonAccent.Ghost);
        kind.TrailingGlyph = ChevronDown;
        kind.GlyphSize = 12f;
        kind.Activated += _ =>
        {
            _kind = (_kind + 1) % Kinds.Length;
            Invalidate();
        };
        filters.Add(kind);
        string modeName = _modeFilter == 0 ? "Any" : GameModes.Title(FilterModes[_modeFilter]);
        Button mode = CompactButton(theme, $"Mode: {modeName}", ButtonAccent.Ghost);
        mode.TrailingGlyph = ChevronDown;
        mode.GlyphSize = 12f;
        mode.Activated += _ =>
        {
            _modeFilter = (_modeFilter + 1) % FilterModes.Length;
            Invalidate();
        };
        filters.Add(mode);
        if (_searching || _search.Length > 0)
        {
            var search = new TextBox(theme) { Id = SearchId, Placeholder = "Search", MaxLength = 32, Width = 150f, Height = 44f, VerticalAlignment = VerticalAlignment.Center, LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Search) };
            search.Text = _search;
            search.TextChanged += text =>
            {
                _search = text;
                _focusNext = SearchId;
                Invalidate();
            };
            filters.Add(search);
        }

        filters.Add(new Panel(), grow: 1f);
        if (!_searching && _search.Length == 0)
        {
            Button find = CompactIcon(theme, OpenIGI.UI.Kit.KitIcon.Search, "Search the games by name or map.");
            find.Activated += _ =>
            {
                _searching = true;
                _focusNext = SearchId;
                Invalidate();
            };
            filters.Add(find);
        }

        Button quick = CompactIcon(theme, OpenIGI.UI.Kit.KitIcon.Play, "Quick play: join the best open game listed.");
        quick.Activated += _ => coop.QuickPlay();
        filters.Add(quick);
        Button refresh = CompactIcon(theme, OpenIGI.UI.Kit.KitIcon.Refresh, "Ask for the list again.");
        refresh.Activated += _ => Refresh(coop);
        filters.Add(refresh);
        layout.Add(filters, 1, 0);

        _rows.Clear();
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        if (all is null)
        {
            for (int i = 0; i < SkeletonRows; i++)
            {
                rows.AddChild(new ServerRowSkeleton(i, SkeletonRows) { Compact = true });
            }
        }
        else if (shown.Count == 0)
        {
            string nothing = coop.ListingError is { } error ? $"Couldn't list servers: {error}."
                : all.Count == 0 ? "No games are open right now. Host one, and it shows here for everyone."
                : "No games match these filters.";
            rows.AddChild(new Label(theme.Body, nothing, 16f, coop.ListingError is null ? theme.TextSecondary : theme.Danger) { Wrap = true, Margin = new Thickness(16f, 20f) });
        }

        foreach (CoopRoomInfo room in shown)
        {
            ServerRow row = BuildRow(theme, room);
            row.Compact = true;
            rows.AddChild(row);
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        _listScroll = scroll;
        layout.Add(Panel(theme, scroll), 3, 0);
        return layout;
    }

    private ScrollView BuildCompactHost(UiTheme theme, ICoopRooms coop)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, Margin = new Thickness(0f, 0f, 10f, 16f) };
        column.AddChild(BuildGameSettings(theme, coop));
        column.AddChild(HostingCoop ? BuildCoopRules(theme) : BuildMatchRules(theme));
        column.AddChild(BuildHostSide(theme, coop));
        var scroll = new ScrollView(theme);
        scroll.SetContent(column);
        return scroll;
    }

    // The game picked, over the list: what it is, who is in it, why it cannot be joined when it cannot, and Join.
    private Panel BuildSheet(UiTheme theme, ICoopRooms coop, CoopRoomInfo room)
    {
        ServerLine line = LineOf(room);
        string code = room.Code;
        string map = MapName(room.MissionId);

        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, Margin = new Thickness(16f, 12f, 16f, 14f) };

        var top = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        top.Add(new Label(theme.Heading, ModeLook.Code(room.Mode), 15f, ModeLook.Colour(room.Mode)) { VerticalAlignment = VerticalAlignment.Center });
        top.Add(new Label(theme.Display, map.ToUpperInvariant(), 22f, theme.Text) { VerticalAlignment = VerticalAlignment.Center });
        top.Add(new Panel(), grow: 1f);
        bool favourite = room.Favourite;
        Button save = CompactButton(theme, favourite ? "Saved" : "Save", favourite ? ButtonAccent.Solid : ButtonAccent.Ghost);
        save.Activated += _ =>
        {
            coop.SetFavourite(code, !favourite);
            Invalidate();
        };
        top.Add(save);
        Button close = CompactButton(theme, "Close", ButtonAccent.Ghost);
        close.Activated += _ => CloseSheet();
        top.Add(close);
        body.AddChild(top);

        body.AddChild(new Label(theme.Body, room.Name, 20f, theme.Text) { Wrap = true });

        var stats = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 28f };
        stats.Add(new Stat(theme, "Players") { Value = string.Create(CultureInfo.InvariantCulture, $"{room.Players} / {room.MaxPlayers}"), ValueColor = line.Full ? theme.Danger : null, ValueSize = 18f });
        stats.Add(new Stat(theme, "Ping") { Value = room.PingMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms} ms") : room.Dedicated ? "—" : "P2P", ValueSize = 18f });
        stats.Add(new Stat(theme, "Mode") { Value = line.Mode, ValueSize = 18f });
        stats.Add(new Stat(theme, line.Kind) { Value = line.Region.Length > 0 ? line.Region : "—", ValueSize = 18f });
        body.AddChild(stats);

        string? about = !room.Reachable ? "This server is listed, but it isn't answering. It can't be joined from here."
            : !room.Compatible ? "This server runs another version of the game, so it can't be joined from this one."
            : line.Full ? "This game is full. Wait for a place, or pick another."
            : room.Passworded ? "It asks for a password."
            : room.Players == 0 ? "Nobody here yet. You and your party would be first in."
            : null;
        if (about is not null)
        {
            body.AddChild(new Label(theme.Body, about, 15f, Joinable(room) ? theme.TextSecondary : theme.Warning) { Wrap = true });
        }

        var join = new PrimaryAction(theme, !room.Reachable ? "No answer" : !room.Compatible ? "Other version" : line.Full ? "Full" : "Join")
        {
            Subtitle = $"{map.ToUpperInvariant()} · {line.Mode.ToUpperInvariant()}",
            IsEnabled = Joinable(room),
            Height = 52f,
            TitleSize = 20f,
            SubtitleSize = 13f,
            SidePadding = 20f,
            CutCorner = 10f,
        };
        join.Activated += _ => coop.Join(code);
        body.AddChild(join);

        var scroll = new ScrollView(theme);
        scroll.SetContent(body);
        var sheet = new GlassPanel(theme)
        {
            Tint = Bgra32.FromRgba(12, 14, 15, 250),
            Border = UiPalette.White(0.28f),
            Blur = 0f,
            Margin = new Thickness(120f, 0f, 0f, 8f),
        };
        sheet.AddChild(scroll);

        // The list behind is dimmed, and a press on it is taken here rather than picking another line through the sheet.
        var layer = new Dimmer(theme);
        layer.Activated += _ => CloseSheet();
        var over = new Panel();
        over.AddChild(layer);
        over.AddChild(sheet);
        return over;
    }

    private void CloseSheet()
    {
        if (_sheet)
        {
            _sheet = false;
            Invalidate();
        }
    }

    // A button a thumb tall, its caption in the page's small label size.
    // A square button that is only its mark, a thumb wide.
    private static Button CompactIcon(UiTheme theme, OpenIGI.UI.Kit.KitIcon icon, string tooltip) => new(theme, string.Empty)
    {
        Accent = ButtonAccent.Ghost,
        LeadingGlyph = Glyph(icon),
        GlyphSize = 18f,
        Padding = new Thickness(13f, 0f),
        Height = 44f,
        VerticalAlignment = VerticalAlignment.Center,
        Tooltip = tooltip,
    };

    private static Button CompactButton(UiTheme theme, string text, ButtonAccent accent) => new(theme, text)
    {
        Accent = accent,
        GlyphSize = 16f,
        TextSize = 15f,
        Tracking = 0.1f,
        Padding = new Thickness(14f, 0f),
        Height = 44f,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What stands between a sheet and the page under it: a wash, and somewhere a press outside the sheet lands.</summary>
    private sealed class Dimmer(UiTheme theme) : Control(theme)
    {
        protected override bool IsFocusable => false;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void OnDraw(UiPainter painter) => painter.FillRect(Bounds, UiPalette.InkAt(0.6f));
    }
}

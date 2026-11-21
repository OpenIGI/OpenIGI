using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// A team match's team menu, in the played title's look, set out as the sides face each other: a panel for each -- a band of its
/// ink across its head with its name large, its number key and how many play on it; its roster as numbered slots, the open
/// ones left showing; and a bar along its foot that says what choosing it does (JOIN, YOUR TEAM, TEAM FULL) -- and between the
/// two the VS, how the sides stand, and the button that puts this player on the fairer side.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // The fewest slots a side's roster shows, so an empty side still reads as a side waiting to be filled; and the most.
    private const int TeamSlotsShown = 6;
    private const int TeamSlotsMost = 10;

    private readonly List<(int Index, float X, float Y, float Width, float Height)> _teamCards = [];

    /// <inheritdoc/>
    public IReadOnlyList<(int Index, float X, float Y, float Width, float Height)> TeamMenuCards => _teamCards;

    /// <inheritdoc/>
    public void DrawTeamMenu(SpriteBatch batch, TeamPick pick, string modeTitle, string key, (float X, float Y) pointer, int screenWidth, int screenHeight)
    {
        _teamCards.Clear();
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(pick);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        UiRect safe = o.Safe;
        float centre = safe.X + (safe.Width * 0.5f);
        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float large = o.Px(HudOverlay.Large);

        // The two sides' panels either side of a middle column, as wide as the screen lets them be.
        float middleWidth = o.Px(190f);
        float panelWidth = Math.Min(o.Px(440f), (safe.Width - o.Px(48f) - middleWidth) * 0.5f);
        int slots = Math.Clamp(Math.Max(pick.Igi.Count, pick.Conspiracy.Count) + 1, TeamSlotsShown, TeamSlotsMost);
        float slotHeight = o.Text.LineHeightAt(body) + o.Px(12f);
        float headHeight = o.Px(92f);
        float footHeight = o.KeyCapHeight + o.Px(22f);
        float panelHeight = headHeight + o.Px(10f) + (slots * slotHeight) + o.Px(10f) + footHeight;
        float rowWidth = (panelWidth * 2f) + middleWidth;
        float left = centre - (rowWidth * 0.5f);
        float titleHeight = o.Text.LineHeightAt(large) + o.Px(18f);
        float top = safe.Y + Math.Max(o.Px(130f), (safe.Height - panelHeight - titleHeight) * 0.45f) + titleHeight;

        // The ground it all stands on: the title's glass band across the screen.
        o.GlassBand(new UiRect(safe.X, top - titleHeight - o.Px(26f), safe.Width, panelHeight + titleHeight + o.Px(70f)), HudOverlay.Fade(o.Ink.Primary, 0.5f));
        o.Text.DrawAt("CHOOSE YOUR TEAM", left, top - titleHeight, large, o.Ink.Highlight, shadow: true);
        o.Text.DrawRightAt(OverlayText.Capitals(modeTitle), left + rowWidth, top - titleHeight + o.Text.LineHeightAt(large) - o.Text.LineHeightAt(small), small, o.Ink.Accent);

        // The two sides, and the middle between them.
        var igi = new UiRect(left, top, panelWidth, panelHeight);
        var conspiracy = new UiRect(left + panelWidth + middleWidth, top, panelWidth, panelHeight);
        bool Over(UiRect r) => pointer.X >= r.X && pointer.X <= r.Right && pointer.Y >= r.Y && pointer.Y <= r.Bottom;
        var layout = new TeamPanelLayout(slots, slotHeight, headHeight, footHeight);
        SidePanel(o, igi, pick, TeamRoster.Good, 1, layout, Over(igi), fromLeft: true);
        SidePanel(o, conspiracy, pick, TeamRoster.Bad, 2, layout, Over(conspiracy), fromLeft: false);
        _teamCards.Add((0, igi.X, igi.Y, igi.Width, igi.Height));
        _teamCards.Add((1, conspiracy.X, conspiracy.Y, conspiracy.Width, conspiracy.Height));

        var middle = new UiRect(igi.Right, top, middleWidth, panelHeight);
        UiRect auto = MiddleColumn(o, middle, pick, headHeight, pointer);
        _teamCards.Add((2, auto.X, auto.Y, auto.Width, auto.Height));

        string how = string.Create(
            CultureInfo.InvariantCulture,
            $"PRESS 1-3 OR CLICK A TEAM - {key.ToUpperInvariant()} OR ESC TO CLOSE - {key.ToUpperInvariant()} OPENS THIS AGAIN TO SWITCH");
        o.Text.DrawCentredAt(how, centre, top + panelHeight + o.Px(18f), small, o.Quiet);
        UiCursor.Draw(o.Painter, new UiVec2(pointer.X, pointer.Y));
    }

    // One side's panel: its head, its roster, and what choosing it does.
    private static void SidePanel(HudOverlay o, UiRect panel, TeamPick pick, int team, int number, TeamPanelLayout layout, bool over, bool fromLeft)
    {
        bool own = pick.OwnTeam == team;
        bool open = pick.Open(team);
        Bgra32 ink = o.SideInk(team, pick.OwnTeam, TeamRoster.Good);
        IReadOnlyList<string> names = pick.On(team);
        string name = OverlayText.Capitals(MatchState.TeamName(team));
        o.Sheet(panel);
        SideHead(o, new UiRect(panel.X, panel.Y, panel.Width, layout.Head), ink, name, names.Count, number, own, open, over, fromLeft);

        // The roster: a numbered slot a player, the open ones after them; the last shown slot says how many more there are.
        float body = o.Px(HudOverlay.Body);
        float y = panel.Y + layout.Head + o.Px(10f);
        float textOffset = (layout.Slot - o.Text.LineHeightAt(body)) * 0.5f;
        for (int i = 0; i < layout.Slots; i++)
        {
            var slot = new UiRect(panel.X + o.Px(10f), y, panel.Width - o.Px(20f), layout.Slot - o.Px(3f));
            if (i % 2 == 0)
            {
                o.Plate(slot, HudOverlay.Fade(o.Ink.Primary, 0.05f));
            }

            bool filled = i < names.Count;
            bool more = i == layout.Slots - 1 && names.Count > layout.Slots;
            o.Text.DrawAt((i + 1).ToString("00", CultureInfo.InvariantCulture), slot.X + o.Px(10f), slot.Y + textOffset, body, HudOverlay.Fade(o.Quiet, filled ? 1f : 0.45f));
            float nameX = slot.X + o.Px(10f) + o.Text.MeasureAt("00", body) + o.Px(16f);
            if (more)
            {
                o.Text.DrawAt(string.Create(CultureInfo.InvariantCulture, $"+{names.Count - layout.Slots + 1} MORE"), nameX, slot.Y + textOffset, body, o.Quiet);
            }
            else if (filled)
            {
                o.Text.DrawAt(o.Text.ClipAt(names[i], body, slot.Right - nameX - o.Px(10f)), nameX, slot.Y + textOffset, body, o.Ink.Plain);
            }
            else
            {
                o.Text.DrawAt("OPEN SLOT", nameX, slot.Y + textOffset, body, HudOverlay.Fade(o.Quiet, 0.35f));
            }

            y += layout.Slot;
        }

        // The foot: what choosing it does -- lit in its ink under the pointer, quiet while this player is on it, red when full.
        var foot = new UiRect(panel.X + o.Px(10f), panel.Bottom - layout.Foot + o.Px(4f), panel.Width - o.Px(20f), layout.Foot - o.Px(14f));
        (string said, Bgra32 face, Bgra32 text) = own ? ("YOUR TEAM", HudOverlay.Fade(ink, 0.22f), ink)
            : !open ? ("TEAM FULL", HudOverlay.Fade(o.Ink.Danger, 0.18f), o.Ink.Danger)
            : over ? ("JOIN " + name, HudOverlay.Fade(ink, 0.9f), Bgra32.FromRgb(8, 10, 8))
            : ("JOIN " + name, HudOverlay.Fade(ink, 0.3f), o.Ink.Highlight);
        o.Plate(foot, face);
        o.Text.DrawCentredAt(said, foot.X + (foot.Width * 0.5f), foot.Y + ((foot.Height - o.Text.LineHeightAt(body)) * 0.5f), body, text);
    }

    // A side's head: a band of its ink, strongest at its outer edge and fading towards the middle, its key, its name large and how
    // many play on it -- set from the left on the left side and from the right on the right, so the two face each other.
    private static void SideHead(HudOverlay o, UiRect head, Bgra32 ink, string name, int players, int number, bool own, bool open, bool over, bool fromLeft)
    {
        float strength = !open && !own ? 0.25f : over && open && !own ? 0.75f : 0.55f;
        Bgra32 strong = HudOverlay.Fade(ink, strength);
        Bgra32 faint = HudOverlay.Fade(ink, strength * 0.18f);
        Span<UiGradientStop> stops = [new(0f, fromLeft ? strong : faint), new(1f, fromLeft ? faint : strong)];
        o.Painter.FillGradient(head, stops, UiGradientDirection.Horizontal);

        float pad = o.Px(18f);
        float huge = o.Px(40f);
        float small = o.Px(HudOverlay.Small);
        float nameY = head.Y + o.Px(14f);
        float countY = nameY + o.Text.LineHeightAt(huge) + o.Px(2f);
        string count = players == 1 ? "1 PLAYER" : string.Create(CultureInfo.InvariantCulture, $"{players} PLAYERS");
        string keyName = number.ToString(CultureInfo.InvariantCulture);
        float capY = nameY + ((o.Text.LineHeightAt(huge) - o.KeyCapHeight) * 0.5f);
        Bgra32 soft = HudOverlay.Fade(o.Ink.Highlight, 0.75f);
        if (fromLeft)
        {
            UiRect cap = o.KeyCap(keyName, head.X + pad, capY, lit: own);
            o.Text.DrawAt(name, cap.Right + o.Px(12f), nameY, huge, o.Ink.Highlight, shadow: true);
            o.Text.DrawAt(count, cap.Right + o.Px(12f), countY, small, soft, shadow: true);
        }
        else
        {
            UiRect cap = o.KeyCap(keyName, head.Right - pad - o.KeyCapWidth(keyName), capY, lit: own);
            o.Text.DrawRightAt(name, cap.X - o.Px(12f), nameY, huge, o.Ink.Highlight, shadow: true);
            o.Text.DrawRightAt(count, cap.X - o.Px(12f), countY, small, soft, shadow: true);
        }
    }

    // The middle between the sides: VS, how many each has, and the button for the fairer side; returns that button.
    private static UiRect MiddleColumn(HudOverlay o, UiRect middle, TeamPick pick, float headHeight, (float X, float Y) pointer)
    {
        float centre = middle.X + (middle.Width * 0.5f);
        float huge = o.Px(HudOverlay.Huge);
        float large = o.Px(HudOverlay.Large);
        float small = o.Px(HudOverlay.Small);
        float body = o.Px(HudOverlay.Body);
        o.Text.DrawCentredAt("VS", centre, middle.Y + ((headHeight - o.Text.LineHeightAt(huge)) * 0.5f), huge, HudOverlay.Fade(o.Ink.Highlight, 0.85f), shadow: true);

        // How the sides stand, each count in its ink.
        float countsY = middle.Y + headHeight + o.Px(24f);
        float dash = o.Text.MeasureAt(" - ", large);
        o.Text.DrawCentredAt(" - ", centre, countsY, large, o.Quiet);
        o.Text.DrawRightAt(pick.Igi.Count.ToString(CultureInfo.InvariantCulture), centre - (dash * 0.5f), countsY, large, o.SideInk(TeamRoster.Good, pick.OwnTeam, TeamRoster.Good));
        o.Text.DrawAt(pick.Conspiracy.Count.ToString(CultureInfo.InvariantCulture), centre + (dash * 0.5f), countsY, large, o.SideInk(TeamRoster.Bad, pick.OwnTeam, TeamRoster.Good));
        o.Text.DrawCentredAt("PLAYERS", centre, countsY + o.Text.LineHeightAt(large) + o.Px(2f), small, o.Quiet);

        // The fairer side: a button low in the column, its key, what it does, and why.
        float buttonHeight = o.KeyCapHeight + o.Px(56f);
        var button = new UiRect(middle.X + o.Px(14f), middle.Bottom - buttonHeight - o.Px(10f), middle.Width - o.Px(28f), buttonHeight);
        bool over = pointer.X >= button.X && pointer.X <= button.Right && pointer.Y >= button.Y && pointer.Y <= button.Bottom;
        o.Plate(button, HudOverlay.Fade(o.Ink.Primary, over ? 0.35f : 0.14f));
        UiRect cap = o.KeyCap("3", centre - (o.KeyCapWidth("3") * 0.5f), button.Y + o.Px(10f), lit: over);
        o.Text.DrawCentredAt("AUTO", centre, cap.Bottom + o.Px(6f), body, o.Ink.Highlight);
        o.Text.DrawCentredAt("FEWER PLAYERS", centre, cap.Bottom + o.Px(8f) + o.Text.LineHeightAt(body), small, o.Quiet);
        return button;
    }

    // How a side's panel is cut up: how many roster slots, and the heights of a slot, its head and its foot, in pixels.
    private readonly record struct TeamPanelLayout(int Slots, float Slot, float Head, float Foot);
}

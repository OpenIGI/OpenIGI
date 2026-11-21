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
/// A team match's team menu, in the played title's look and kept quiet: the picture dimmed behind it; the mode over SELECT TEAM
/// centred at the top; a card for each side -- a thin line of its ink along its top, its number and state, its name large in its
/// ink, its roster on hairlines with the open places left showing, and the button that joins it -- with VS between them on a
/// fading rule; under them the button that puts this player on the fairer side; and at the foot the keys that work.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // The fewest places a side's roster shows, so an empty side still reads as one waiting to be filled; and the most.
    private const int TeamSlotsShown = 5;
    private const int TeamSlotsMost = 10;

    // A side's card, and the room between the two for the VS, in layout pixels.
    private const float TeamCardWidth = 420f;
    private const float TeamCardGap = 110f;
    private const float TeamCardPad = 28f;

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
        float title = o.Px(40f);
        bool Over(UiRect r) => pointer.X >= r.X && pointer.X <= r.Right && pointer.Y >= r.Y && pointer.Y <= r.Bottom;

        // The picture dimmed behind it all, so the menu is what the eye is on.
        o.Painter.FillRect(new UiRect(0f, 0f, screenWidth, screenHeight), Bgra32.FromRgba(0, 0, 0, 150));

        // The cards' measures.
        int slots = Math.Clamp(Math.Max(pick.Igi.Count, pick.Conspiracy.Count) + 1, TeamSlotsShown, TeamSlotsMost);
        float gap = o.Px(TeamCardGap);
        float cardWidth = Math.Min(o.Px(TeamCardWidth), (safe.Width - o.Px(48f) - gap) * 0.5f);
        var layout = new TeamCardLayout(
            slots,
            o.Text.LineHeightAt(body) + o.Px(14f),
            o.Px(TeamCardPad) + o.Text.LineHeightAt(small) + o.Px(10f) + o.Text.LineHeightAt(o.Px(44f)) + o.Px(4f) + o.Text.LineHeightAt(small) + o.Px(18f),
            o.Px(56f));
        float cardHeight = layout.Head + (slots * layout.Slot) + o.Px(18f) + layout.Button + o.Px(TeamCardPad);
        float autoHeight = o.KeyCapHeight + o.Px(22f);
        float headHeight = o.Text.LineHeightAt(small) + o.Px(6f) + o.Text.LineHeightAt(title) + o.Px(28f);
        float whole = headHeight + cardHeight + o.Px(22f) + autoHeight + o.Px(30f) + o.KeyCapHeight;
        float top = safe.Y + Math.Max(o.Px(110f), (safe.Height - whole) * 0.5f);

        // The heading, centred: the mode quiet over SELECT TEAM, a short rule of the accent under it.
        o.Text.DrawCentredAt(OverlayText.Capitals(modeTitle), centre, top, small, o.Ink.Accent);
        float titleY = top + o.Text.LineHeightAt(small) + o.Px(6f);
        o.Text.DrawCentredAt("SELECT TEAM", centre, titleY, title, o.Ink.Highlight, shadow: true);
        o.Painter.FillRect(new UiRect(centre - o.Px(28f), titleY + o.Text.LineHeightAt(title) + o.Px(10f), o.Px(56f), MathF.Max(2f, o.Px(2f))), o.Ink.Accent);

        // The two sides either side of the middle, and the VS between them.
        float cardsTop = top + headHeight;
        var igi = new UiRect(centre - (gap * 0.5f) - cardWidth, cardsTop, cardWidth, cardHeight);
        var conspiracy = new UiRect(centre + (gap * 0.5f), cardsTop, cardWidth, cardHeight);
        TeamCard(o, igi, pick, TeamRoster.Good, 1, layout, Over(igi));
        TeamCard(o, conspiracy, pick, TeamRoster.Bad, 2, layout, Over(conspiracy));
        _teamCards.Add((0, igi.X, igi.Y, igi.Width, igi.Height));
        _teamCards.Add((1, conspiracy.X, conspiracy.Y, conspiracy.Width, conspiracy.Height));
        Versus(o, centre, cardsTop, cardHeight);

        // The fairer side, under the cards.
        float autoWidth = Math.Min(o.Px(400f), safe.Width - o.Px(48f));
        var auto = new UiRect(centre - (autoWidth * 0.5f), cardsTop + cardHeight + o.Px(22f), autoWidth, autoHeight);
        AutoButton(o, auto, Over(auto));
        _teamCards.Add((2, auto.X, auto.Y, auto.Width, auto.Height));

        // The foot: the keys that work.
        KeyLegend(o, centre, auto.Bottom + o.Px(30f), [("1", "I.G.I."), ("2", "CONSPIRACY"), ("3", "AUTO"), (key, "CLOSE")]);
        UiCursor.Draw(o.Painter, new UiVec2(pointer.X, pointer.Y));
    }

    // One side's card: its line, its number and state, its name, its roster, and the button that joins it.
    private static void TeamCard(HudOverlay o, UiRect card, TeamPick pick, int team, int number, TeamCardLayout layout, bool over)
    {
        bool own = pick.OwnTeam == team;
        bool open = pick.Open(team);
        bool joinable = open && !own;
        Bgra32 ink = o.SideInk(team, pick.OwnTeam, TeamRoster.Good);
        IReadOnlyList<string> names = pick.On(team);
        float pad = o.Px(TeamCardPad);
        float small = o.Px(HudOverlay.Small);
        float body = o.Px(HudOverlay.Body);
        float big = o.Px(44f);

        // The card: the title's dark glass, a touch lighter under the pointer, a line of the side's ink along its top.
        o.Sheet(card);
        if (over && joinable)
        {
            o.Plate(card, HudOverlay.Fade(o.Ink.Highlight, 0.04f));
        }

        float line = MathF.Max(2f, o.Px(over && joinable || own ? 4f : 3f));
        o.Painter.FillRect(new UiRect(card.X, card.Y, card.Width, line), HudOverlay.Fade(ink, open || own ? 1f : 0.4f));

        // Its number and state on one line, its name large under it, how many play on it under that.
        float x = card.X + pad;
        float right = card.Right - pad;
        float y = card.Y + pad;
        o.Text.DrawAt(string.Create(CultureInfo.InvariantCulture, $"TEAM {number}"), x, y, small, o.Quiet);
        if (own)
        {
            o.Text.DrawRightAt("YOUR TEAM", right, y, small, ink);
        }
        else if (!open)
        {
            o.Text.DrawRightAt("FULL", right, y, small, o.Ink.Danger);
        }

        y += o.Text.LineHeightAt(small) + o.Px(10f);
        string name = OverlayText.Capitals(MatchState.TeamName(team));
        o.Text.DrawAt(o.Text.ClipAt(name, big, right - x), x, y, big, open || own ? ink : HudOverlay.Fade(ink, 0.5f), shadow: true);
        y += o.Text.LineHeightAt(big) + o.Px(4f);
        o.Text.DrawAt(names.Count == 1 ? "1 PLAYER" : string.Create(CultureInfo.InvariantCulture, $"{names.Count} PLAYERS"), x, y, small, o.Quiet);

        // The roster on hairlines; the open places a quiet dash; the last place says how many more there are.
        y = card.Y + layout.Head;
        Bgra32 hair = HudOverlay.Fade(o.Ink.Primary, 0.18f);
        float textOffset = (layout.Slot - o.Text.LineHeightAt(body)) * 0.5f;
        for (int i = 0; i < layout.Slots; i++)
        {
            o.Painter.FillRect(new UiRect(x, y, right - x, MathF.Max(1f, o.Px(1f))), hair);
            bool more = i == layout.Slots - 1 && names.Count > layout.Slots;
            string shown = more ? string.Create(CultureInfo.InvariantCulture, $"+{names.Count - layout.Slots + 1} MORE")
                : i < names.Count ? o.Text.ClipAt(names[i], body, right - x)
                : "-";
            Bgra32 shade = more ? o.Quiet : i < names.Count ? o.Ink.Plain : HudOverlay.Fade(o.Quiet, 0.35f);
            o.Text.DrawAt(shown, x, y + textOffset, body, shade);
            y += layout.Slot;
        }

        o.Painter.FillRect(new UiRect(x, y, right - x, MathF.Max(1f, o.Px(1f))), hair);

        // The button: what choosing this side does, filled in its ink under the pointer.
        var button = new UiRect(x, card.Bottom - pad - layout.Button, right - x, layout.Button);
        (string said, Bgra32 face, Bgra32 text) = own ? ("CURRENT TEAM", HudOverlay.Fade(ink, 0.12f), HudOverlay.Fade(ink, 0.9f))
            : !open ? ("TEAM FULL", HudOverlay.Fade(o.Ink.Danger, 0.1f), HudOverlay.Fade(o.Ink.Danger, 0.8f))
            : over ? ("JOIN TEAM", ink, Bgra32.FromRgb(8, 10, 8))
            : ("JOIN TEAM", HudOverlay.Fade(o.Ink.Primary, 0.1f), o.Ink.Highlight);
        o.Plate(button, face);
        float textY = button.Y + ((button.Height - o.Text.LineHeightAt(body)) * 0.5f);
        if (joinable)
        {
            string keyName = number.ToString(CultureInfo.InvariantCulture);
            float width = o.KeyCapWidth(keyName) + o.Px(12f) + o.Text.MeasureAt(said, body);
            float start = button.X + ((button.Width - width) * 0.5f);
            UiRect cap = o.KeyCap(keyName, start, button.Y + ((button.Height - o.KeyCapHeight) * 0.5f), lit: over);
            o.Text.DrawAt(said, cap.Right + o.Px(12f), textY, body, text);
        }
        else
        {
            o.Text.DrawCentredAt(said, button.X + (button.Width * 0.5f), textY, body, text);
        }
    }

    // VS between the sides, on a rule that fades out above and below it.
    private static void Versus(HudOverlay o, float centre, float top, float height)
    {
        float large = o.Px(HudOverlay.Large);
        float middle = top + (height * 0.42f);
        float word = o.Text.LineHeightAt(large);
        float thick = MathF.Max(1f, o.Px(1f));
        Bgra32 rule = HudOverlay.Fade(o.Ink.Primary, 0.35f);
        Span<UiGradientStop> down = [new(0f, rule.WithAlpha(0)), new(1f, rule)];
        Span<UiGradientStop> up = [new(0f, rule), new(1f, rule.WithAlpha(0))];
        o.Painter.FillGradient(new UiRect(centre - (thick * 0.5f), top + o.Px(20f), thick, middle - top - word - o.Px(20f)), down, UiGradientDirection.Vertical);
        o.Painter.FillGradient(new UiRect(centre - (thick * 0.5f), middle + word, thick, top + height - middle - word - o.Px(20f)), up, UiGradientDirection.Vertical);
        o.Text.DrawCentredAt("VS", centre, middle - (word * 0.5f), large, o.Quiet);
    }

    // The fairer side: its key, AUTO-ASSIGN, and why, on a quiet plate lit under the pointer.
    private static void AutoButton(HudOverlay o, UiRect button, bool over)
    {
        o.Plate(button, HudOverlay.Fade(o.Ink.Primary, over ? 0.22f : 0.08f));
        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        const string Said = "AUTO-ASSIGN";
        const string Why = "JOIN THE SMALLER TEAM";
        float gap = o.Px(12f);
        float width = o.KeyCapWidth("3") + gap + o.Text.MeasureAt(Said, body) + gap + o.Text.MeasureAt(Why, small);
        float x = button.X + ((button.Width - width) * 0.5f);
        UiRect cap = o.KeyCap("3", x, button.Y + ((button.Height - o.KeyCapHeight) * 0.5f), lit: over);
        x = cap.Right + gap;
        x += o.Text.DrawAt(Said, x, button.Y + ((button.Height - o.Text.LineHeightAt(body)) * 0.5f), body, o.Ink.Highlight) + gap;
        o.Text.DrawAt(Why, x, button.Y + ((button.Height - o.Text.LineHeightAt(small)) * 0.5f), small, o.Quiet);
    }

    // A row of keys and what each does, centred.
    private static void KeyLegend(HudOverlay o, float centre, float y, ReadOnlySpan<(string Key, string Does)> keys)
    {
        float small = o.Px(HudOverlay.Small);
        float gap = o.Px(8f);
        float spacing = o.Px(26f);
        float width = 0f;
        foreach ((string k, string does) in keys)
        {
            width += o.KeyCapWidth(k) + gap + o.Text.MeasureAt(does, small) + spacing;
        }

        float x = centre - ((width - spacing) * 0.5f);
        foreach ((string k, string does) in keys)
        {
            UiRect cap = o.KeyCap(k, x, y);
            x = cap.Right + gap;
            x += o.Text.DrawAt(does, x, y + ((cap.Height - o.Text.LineHeightAt(small)) * 0.5f), small, o.Quiet) + spacing;
        }
    }

    // How a side's card is cut up: how many roster places, and the heights of a place, its head and its button, in pixels.
    private readonly record struct TeamCardLayout(int Slots, float Slot, float Head, float Button);
}

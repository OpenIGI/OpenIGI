using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// A team match's team menu, in the played title's look: on the title's glass band across the middle of the screen, a sheet for
/// each side -- its number key, its name in its ink, how many play on it and who -- this player's own bracketed and marked, a side
/// it can't join dimmed and marked full; then a narrower sheet to be put on the fairer side, and how to choose below.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // The most names a side's sheet lists before it says how many more.
    private const int TeamCardNames = 8;

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
        float gap = o.Px(18f);
        float sideWidth = Math.Min(o.Px(300f), (safe.Width - o.Px(48f) - (gap * 2)) * 0.38f);
        float autoWidth = Math.Min(o.Px(200f), (safe.Width - o.Px(48f) - (gap * 2)) * 0.24f);
        float cardHeight = o.Px(330f);
        float rowWidth = (sideWidth * 2) + autoWidth + (gap * 2);
        float left = centre - (rowWidth * 0.5f);
        float top = safe.Y + Math.Max(o.Px(120f), (safe.Height - cardHeight) * 0.42f);
        o.GlassBand(new UiRect(safe.X, top - o.Px(70f), safe.Width, cardHeight + o.Px(124f)), HudOverlay.Fade(o.Ink.Primary, 0.6f));

        float large = o.Px(HudOverlay.Large);
        float headY = top - o.Px(18f) - o.Text.LineHeightAt(large);
        o.Text.DrawAt("CHOOSE YOUR TEAM", left, headY, large, o.Ink.Highlight);
        float small = o.Px(HudOverlay.Small);
        o.Text.DrawRightAt(o.Text.Capitals(modeTitle), left + rowWidth, headY + o.Text.LineHeightAt(large) - o.Text.LineHeightAt(small), small, o.Ink.Accent);

        float x = left;
        for (int i = 0; i < TeamPick.Choices.Count; i++)
        {
            float width = i < 2 ? sideWidth : autoWidth;
            var card = new UiRect(x, top, width, cardHeight);
            bool over = pointer.X >= card.X && pointer.X <= card.Right && pointer.Y >= card.Y && pointer.Y <= card.Bottom;
            if (i < 2)
            {
                SideCard(o, card, pick, i == 0 ? TeamRoster.Good : TeamRoster.Bad, i + 1, over);
            }
            else
            {
                AutoCard(o, card, over);
            }

            _teamCards.Add((i, card.X, card.Y, card.Width, card.Height));
            x += width + gap;
        }

        string how = string.Create(
            CultureInfo.InvariantCulture,
            $"PRESS 1-3 OR CLICK A TEAM - {key.ToUpperInvariant()} OR ESC TO CLOSE - {key.ToUpperInvariant()} OPENS THIS AGAIN TO SWITCH");
        o.Text.DrawCentredAt(how, centre, top + cardHeight + o.Px(20f), small, o.Quiet);
        UiCursor.Draw(o.Painter, new UiVec2(pointer.X, pointer.Y));
    }

    // One side: its key and name in its ink, how many play on it, and who.
    private static void SideCard(HudOverlay o, UiRect card, TeamPick pick, int team, int number, bool over)
    {
        bool own = pick.OwnTeam == team;
        bool open = pick.Open(team);
        Bgra32 ink = o.SideInk(team, pick.OwnTeam, TeamRoster.Good);
        IReadOnlyList<string> names = pick.On(team);
        CardFrame(o, card, own, over && open, open ? ink : HudOverlay.Fade(ink, 0.45f));

        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float pad = o.Px(16f);
        float y = card.Y + o.Px(18f);
        UiRect cap = o.KeyCap(number.ToString(CultureInfo.InvariantCulture), card.X + pad, y, lit: own);
        o.Text.DrawAt(o.Text.Capitals(MatchState.TeamName(team)), cap.Right + o.Px(10f), cap.Y + ((cap.Height - o.Text.LineHeightAt(o.Px(HudOverlay.Large))) * 0.5f), o.Px(HudOverlay.Large), open ? ink : HudOverlay.Fade(ink, 0.45f));

        y = cap.Bottom + o.Px(14f);
        string count = names.Count == 1 ? "1 PLAYER" : string.Create(CultureInfo.InvariantCulture, $"{names.Count} PLAYERS");
        o.Text.DrawAt(count, card.X + pad, y, small, o.Quiet);
        string state = own ? "YOUR TEAM" : open ? string.Empty : "FULL";
        if (state.Length > 0)
        {
            o.Text.DrawRightAt(state, card.Right - pad, y, small, own ? o.Ink.Accent : o.Ink.Danger);
        }

        y += o.Text.LineHeightAt(small) + o.Px(8f);
        o.Rule(card.X + pad, y, card.Width - (pad * 2), HudOverlay.Fade(o.Ink.Primary, 0.4f));
        y += o.Px(12f);

        // Who plays on it, a line each; an empty side says so.
        if (names.Count == 0)
        {
            o.Text.DrawAt("NOBODY YET", card.X + pad, y, body, o.Quiet);
        }

        for (int i = 0; i < names.Count && i < TeamCardNames; i++)
        {
            string name = i == TeamCardNames - 1 && names.Count > TeamCardNames
                ? string.Create(CultureInfo.InvariantCulture, $"+{names.Count - TeamCardNames + 1} MORE")
                : o.Text.ClipAt(o.Text.Prose(names[i]), body, card.Width - (pad * 2f));
            o.Text.DrawAt(name, card.X + pad, y, body, o.Ink.Plain);
            y += o.Text.LineHeightAt(body) + o.Px(6f);
        }
    }

    // The fairer side, whichever has fewer.
    private static void AutoCard(HudOverlay o, UiRect card, bool over)
    {
        CardFrame(o, card, own: false, over, o.Ink.Primary);
        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float pad = o.Px(16f);
        float y = card.Y + o.Px(18f);
        UiRect cap = o.KeyCap("3", card.X + pad, y);
        o.Text.DrawAt("AUTO", cap.Right + o.Px(10f), cap.Y + ((cap.Height - o.Text.LineHeightAt(o.Px(HudOverlay.Large))) * 0.5f), o.Px(HudOverlay.Large), o.Ink.Highlight);
        y = cap.Bottom + o.Px(14f);
        o.Text.DrawAt("FEWER PLAYERS", card.X + pad, y, small, o.Quiet);
        y += o.Text.LineHeightAt(small) + o.Px(8f);
        o.Rule(card.X + pad, y, card.Width - (pad * 2), HudOverlay.Fade(o.Ink.Primary, 0.4f));
        y += o.Px(12f);
        o.Text.DrawAt("JOINS THE SIDE", card.X + pad, y, body, o.Quiet);
        o.Text.DrawAt("THAT NEEDS YOU", card.X + pad, y + o.Text.LineHeightAt(body) + o.Px(6f), body, o.Quiet);
    }

    // A choice's sheet on the menu: the title's glass, lit under the pointer, this player's own in the accent's brackets; a bar of its
    // ink along its top.
    private static void CardFrame(HudOverlay o, UiRect card, bool own, bool over, Bgra32 ink)
    {
        o.Sheet(card);
        if (over && !own)
        {
            o.Plate(card, HudOverlay.Fade(ink, 0.1f));
        }

        o.Painter.FillRect(new UiRect(card.X + o.Px(8f), card.Y, card.Width - o.Px(16f), MathF.Max(2f, o.Px(3f))), ink);
        if (own)
        {
            o.Brackets(card, o.Ink.Accent, 12f);
        }
        else if (over)
        {
            o.Brackets(card, HudOverlay.Fade(ink, 0.9f), 10f);
        }
    }
}

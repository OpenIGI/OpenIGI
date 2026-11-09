using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>
/// A team match's team menu: across the middle of the screen, a card for each side -- its number key, its name in its colour,
/// how many play on it and who -- this player's own edged and marked, a side it can't join greyed and marked full; then a
/// narrower card to be put on the fairer side, and how to choose below.
/// </content>
internal sealed partial class RemasteredHudRenderer
{
    // The most names a side's card lists before it says how many more.
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
        if (_theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        UiRect safe = context.SafeRect;
        float centre = safe.X + (safe.Width * 0.5f);

        float gap = Px(18f);
        float sideWidth = Math.Min(Px(300f), (safe.Width - Px(48f) - (gap * 2)) * 0.38f);
        float autoWidth = Math.Min(Px(200f), (safe.Width - Px(48f) - (gap * 2)) * 0.24f);
        float cardHeight = Px(330f);
        float rowWidth = (sideWidth * 2) + autoWidth + (gap * 2);
        float left = centre - (rowWidth * 0.5f);
        float top = safe.Y + Math.Max(Px(120f), (safe.Height - cardHeight) * 0.42f);

        // A dark band behind it all, so it reads over any scene, fading out at its ends.
        Band(painter, new UiRect(safe.X, top - Px(70f), safe.Width, cardHeight + Px(124f)), Bgra32.FromRgba(6, 8, 11, 210));

        float headY = top - Px(44f);
        painter.DrawLabel(type.Heading, "CHOOSE YOUR TEAM", new UiVec2(left, headY - Px(4f)), Px(22f), UiPalette.TextBright, 0.12f);
        string mode = modeTitle.ToUpperInvariant();
        float modeSize = Px(13f);
        painter.DrawLabel(type.Mono, mode, new UiVec2(left + rowWidth - UiPainter.MeasureLabel(type.Mono, mode, modeSize, 0.2f), headY + Px(4f)), modeSize, VoteAccent, 0.2f);
        painter.FillRect(new UiRect(left, top - Px(12f), rowWidth, Px(1f)), Bgra32.FromRgba(255, 255, 255, 30));

        float x = left;
        for (int i = 0; i < TeamPick.Choices.Count; i++)
        {
            float width = i < 2 ? sideWidth : autoWidth;
            var card = new UiRect(x, top, width, cardHeight);
            bool over = pointer.X >= card.X && pointer.X <= card.Right && pointer.Y >= card.Y && pointer.Y <= card.Bottom;
            if (i < 2)
            {
                SideCard(painter, type, card, pick, i == 0 ? TeamRoster.Good : TeamRoster.Bad, i + 1, over, Px);
            }
            else
            {
                AutoCard(painter, type, card, over, Px);
            }

            _teamCards.Add((i, card.X, card.Y, card.Width, card.Height));
            x += width + gap;
        }

        string how = string.Create(
            CultureInfo.InvariantCulture,
            $"PRESS 1–3 OR CLICK A TEAM  ·  {key.ToUpperInvariant()} OR ESC TO CLOSE  ·  {key.ToUpperInvariant()} OPENS THIS AGAIN TO SWITCH");
        Centred(painter, type.Mono, how, centre, top + cardHeight + Px(20f), Px(12f), UiPalette.TextMuted, 0.18f);
        UiCursor.Draw(painter, new UiVec2(pointer.X, pointer.Y));
    }

    // One side: its key and name in its colour, how many play on it, and who.
    private static void SideCard(UiPainter painter, UiTheme type, UiRect card, TeamPick pick, int team, int number, bool over, Func<float, float> Px)
    {
        bool own = pick.OwnTeam == team;
        bool open = pick.Open(team);
        Bgra32 colour = SideColour(team);
        IReadOnlyList<string> names = pick.On(team);

        painter.FillRect(card, CardFace);
        if (over && open && !own)
        {
            painter.FillRect(card, colour.WithAlpha(26));
        }

        // A bar of the side's colour along the top, the name under it.
        painter.FillRect(new UiRect(card.X, card.Y, card.Width, Px(4f)), open ? colour : colour.WithAlpha(90));
        float pad = Px(16f);
        float y = card.Y + Px(18f);
        painter.DrawLabel(type.Mono, number.ToString(CultureInfo.InvariantCulture), new UiVec2(card.X + pad, y + Px(4f)), Px(15f), open ? VoteAccent : UiPalette.TextMuted, 0f);
        painter.DrawLabel(type.Heading, MatchState.TeamName(team).ToUpperInvariant(), new UiVec2(card.X + pad + Px(24f), y), Px(24f), open ? colour : colour.WithAlpha(120), 0.1f);

        string count = names.Count == 1 ? "1 PLAYER" : string.Create(CultureInfo.InvariantCulture, $"{names.Count} PLAYERS");
        string state = own ? "YOUR TEAM" : open ? string.Empty : "FULL";
        y += Px(40f);
        painter.DrawLabel(type.Mono, count, new UiVec2(card.X + pad, y), Px(12f), UiPalette.TextSecondary, 0.2f);
        if (state.Length > 0)
        {
            float size = Px(12f);
            painter.DrawLabel(type.Mono, state, new UiVec2(card.Right - pad - UiPainter.MeasureLabel(type.Mono, state, size, 0.2f), y), size, own ? Victory : Enemy, 0.2f);
        }

        y += Px(24f);
        painter.FillRect(new UiRect(card.X + pad, y, card.Width - (pad * 2), Px(1f)), CardEdge);
        y += Px(12f);

        // Who plays on it, a line each; an empty side says so.
        float nameSize = Px(15f);
        if (names.Count == 0)
        {
            painter.DrawLabel(type.Body, "Nobody yet", new UiVec2(card.X + pad, y), nameSize, UiPalette.TextMuted, 0f);
        }

        for (int i = 0; i < names.Count && i < TeamCardNames; i++)
        {
            string name = i == TeamCardNames - 1 && names.Count > TeamCardNames
                ? string.Create(CultureInfo.InvariantCulture, $"+{names.Count - TeamCardNames + 1} more")
                : names[i];
            painter.DrawLabel(type.Body, name, new UiVec2(card.X + pad, y), nameSize, UiPalette.TextBright, 0f);
            y += Px(24f);
        }

        // This player's own side edged in amber, as its pick on the map vote is.
        if (own)
        {
            painter.StrokeRect(card, Px(2f), Victory);
        }
        else if (over && open)
        {
            painter.StrokeRect(card, Px(1f), colour.WithAlpha(160));
        }
        else
        {
            painter.StrokeRect(card, Px(1f), CardEdge);
        }
    }

    // The fairer side, whichever has fewer.
    private static void AutoCard(UiPainter painter, UiTheme type, UiRect card, bool over, Func<float, float> Px)
    {
        painter.FillRect(card, CardFace);
        if (over)
        {
            painter.FillRect(card, Bgra32.FromRgba(255, 255, 255, 12));
        }

        float pad = Px(16f);
        float y = card.Y + Px(18f);
        painter.DrawLabel(type.Mono, "3", new UiVec2(card.X + pad, y + Px(4f)), Px(15f), VoteAccent, 0f);
        painter.DrawLabel(type.Heading, "AUTO", new UiVec2(card.X + pad + Px(24f), y), Px(24f), UiPalette.TextBright, 0.1f);
        y += Px(40f);
        painter.DrawLabel(type.Mono, "FEWER PLAYERS", new UiVec2(card.X + pad, y), Px(12f), UiPalette.TextSecondary, 0.2f);
        y += Px(24f);
        painter.FillRect(new UiRect(card.X + pad, y, card.Width - (pad * 2), Px(1f)), CardEdge);
        y += Px(12f);
        painter.DrawLabel(type.Body, "Joins the side", new UiVec2(card.X + pad, y), Px(15f), UiPalette.TextMuted, 0f);
        painter.DrawLabel(type.Body, "that needs you", new UiVec2(card.X + pad, y + Px(24f)), Px(15f), UiPalette.TextMuted, 0f);
        painter.StrokeRect(card, Px(1f), over ? Bgra32.FromRgba(255, 255, 255, 120) : CardEdge);
    }
}

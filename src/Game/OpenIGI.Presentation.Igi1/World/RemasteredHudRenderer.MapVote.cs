using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>
/// The vote for the next map, under a decided match's card: a card for each map offered -- its number key, its mission's plate,
/// its name and mode, and a bar of the votes it has -- the player's own pick edged in amber and the one leading marked, the time
/// left and how many have voted above them, and how to vote below.
/// </content>
internal sealed partial class RemasteredHudRenderer
{
    // The amber the front end draws its choices in, and the cards' faces.
    private static Bgra32 VoteAccent => Bgra32.FromRgb(232, 163, 59);
    private static Bgra32 CardFace => Bgra32.FromRgba(14, 16, 20, 232);
    private static Bgra32 CardEdge => Bgra32.FromRgba(255, 255, 255, 34);

    private readonly List<(int Index, float X, float Y, float Width, float Height)> _voteCards = [];

    /// <inheritdoc/>
    public IReadOnlyList<(int Index, float X, float Y, float Width, float Height)> MapVoteCards => _voteCards;

    /// <inheritdoc/>
    public void DrawMapVote(
        SpriteBatch batch, NextMapVote vote, string? clock, (float X, float Y) pointer, int screenWidth, int screenHeight, bool besideBoard = false)
    {
        _voteCards.Clear();
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(vote);
        if (_theme is not { } type || vote.Choices.Count == 0)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        UiRect safe = context.SafeRect;
        float centre = safe.X + (safe.Width * 0.5f);

        // Alone, one row of cards across the middle; beside the board, the right-hand area, two cards a row.
        int count = vote.Choices.Count;
        UiRect? area = besideBoard ? IntermissionAreas(context).Vote : null;
        int columns = besideBoard ? Math.Min(count, 2) : count;
        int rows = (count + columns - 1) / columns;
        float gap = Px(16f);
        float room = area?.Width ?? (safe.Width - Px(48f));
        float cardWidth = Math.Min(Px(272f), (room - (gap * (columns - 1))) / columns);
        float cardHeight = Px(212f);
        float rowWidth = (cardWidth * columns) + (gap * (columns - 1));
        if (area is { } shared)
        {
            centre = shared.X + (shared.Width * 0.5f);
        }

        float left = centre - (rowWidth * 0.5f);
        float top = area is { } within ? within.Y + Px(56f) : safe.Y + (safe.Height * 0.2f) + Px(150f) + Px(44f);
        float block = (cardHeight * rows) + (gap * (rows - 1));

        // A dark band behind it all, so it reads over any scene, fading out at its ends as the card above does; beside the board,
        // a panel behind the maps' half alone.
        if (area is { } behind)
        {
            painter.FillRect(new UiRect(behind.X - Px(16f), top - Px(56f), behind.Width + Px(32f), block + Px(100f)), Bgra32.FromRgba(6, 8, 11, 196));
        }
        else
        {
            Band(painter, new UiRect(safe.X, top - Px(56f), safe.Width, cardHeight + Px(100f)), Bgra32.FromRgba(6, 8, 11, 196));
        }

        // Above the cards: what this is, and how many have voted (the card above has the clock).
        float headY = top - Px(34f);
        painter.DrawLabel(type.Mono, "VOTE FOR THE NEXT MAP", new UiVec2(left, headY), Px(13f), VoteAccent, 0.3f);
        string tally = string.Create(CultureInfo.InvariantCulture, $"{vote.Picked} OF {Math.Max(vote.Voters, vote.Picked)} VOTED");
        string right = clock is { } c && vote.Picked >= vote.Voters && vote.Voters > 0 ? $"{tally}   ·   GOING ON IN {c}" : tally;
        float rightSize = Px(13f);
        painter.DrawLabel(type.Mono, right, new UiVec2(left + rowWidth - UiPainter.MeasureLabel(type.Mono, right, rightSize, 0.2f), headY), rightSize, UiPalette.TextSecondary, 0.2f);
        painter.FillRect(new UiRect(left, top - Px(12f), rowWidth, Px(1f)), Bgra32.FromRgba(255, 255, 255, 30));

        int leading = vote.Leading;
        for (int i = 0; i < count; i++)
        {
            var card = new UiRect(left + (i % columns * (cardWidth + gap)), top + (i / columns * (cardHeight + gap)), cardWidth, cardHeight);
            bool over = pointer.X >= card.X && pointer.X <= card.Right && pointer.Y >= card.Y && pointer.Y <= card.Bottom;
            VoteCard(painter, type, card, vote, i, own: vote.OwnPick == i, lead: leading == i, over, Px);
            _voteCards.Add((i, card.X, card.Y, card.Width, card.Height));
        }

        string how = count > 1
            ? besideBoard
                ? string.Create(CultureInfo.InvariantCulture, $"PRESS 1–{count} OR CLICK A MAP  ·  CHANGE IT UNTIL THE CLOCK RUNS OUT")
                : string.Create(CultureInfo.InvariantCulture, $"PRESS 1–{count} OR CLICK A MAP TO VOTE  ·  YOU CAN CHANGE YOUR VOTE UNTIL THE CLOCK RUNS OUT")
            : "PRESS 1 OR CLICK TO VOTE";
        Centred(painter, type.Mono, how, centre, top + block + Px(18f), Px(12f), UiPalette.TextMuted, 0.18f);
        UiCursor.Draw(painter, new UiVec2(pointer.X, pointer.Y));
    }

    // How far below the safe area's top a decided match's card stands while the vote is open: under the score strip.
    private const float IntermissionCardTop = 76f;

    // The match card's height, as ResultBand draws a match's.
    private const float MatchCardHeight = 150f;

    // While a decided match's vote is open, the screen under its card is shared: the scoreboard on the left, the maps on the right,
    // the two side by side and centred together, down to the foot of the safe area.
    private static (UiRect Board, UiRect Vote) IntermissionAreas(LayoutContext context)
    {
        UiRect safe = context.SafeRect;
        float top = safe.Y + context.Px(IntermissionCardTop + MatchCardHeight + 28f);
        float bottom = safe.Bottom - context.Px(24f);
        float gap = context.Px(40f);
        float voteWidth = Math.Min(context.Px(600f), safe.Width * 0.4f);
        float boardWidth = Math.Min(context.Px(BoardWidth), safe.Width - voteWidth - gap - context.Px(48f));
        float left = safe.X + ((safe.Width - (boardWidth + gap + voteWidth)) * 0.5f);
        float height = Math.Max(context.Px(200f), bottom - top);
        return (new UiRect(left, top, boardWidth, height), new UiRect(left + boardWidth + gap, top, voteWidth, height));
    }

    // One map: its key, its plate with the mission's number, its name and mode, and its votes.
    private static void VoteCard(UiPainter painter, UiTheme type, UiRect card, NextMapVote vote, int index, bool own, bool lead, bool over, Func<float, float> Px)
    {
        MapChoice choice = vote.Choices[index];
        int votes = index < vote.Counts.Count ? vote.Counts[index] : 0;

        painter.FillRect(card, CardFace);
        if (over && !own)
        {
            painter.FillRect(card, Bgra32.FromRgba(255, 255, 255, 10));
        }

        // The plate: hatched, the mission's number large and faint at its right.
        var plate = new UiRect(card.X + Px(10f), card.Y + Px(10f), card.Width - Px(20f), Px(84f));
        painter.FillRect(plate, Bgra32.FromRgba(255, 255, 255, 8));
        painter.PushClip(plate);
        for (float x = plate.X - plate.Height; x < plate.Right; x += Px(14f))
        {
            painter.StrokeLine(new UiVec2(x, plate.Bottom), new UiVec2(x + plate.Height, plate.Y), Px(5f), Bgra32.FromRgba(255, 255, 255, 9));
        }

        string number = choice.Mission.ToString("00", CultureInfo.InvariantCulture);
        float numberSize = Px(72f);
        painter.DrawLabel(type.Display, number, new UiVec2(plate.Right - Px(8f) - UiPainter.MeasureLabel(type.Display, number, numberSize, 0f), plate.Y - Px(4f)), numberSize, Bgra32.FromRgba(255, 255, 255, own ? (byte)46 : (byte)28), 0f);
        painter.PopClip();

        // The key cap at the plate's corner, amber once picked.
        string key = (index + 1).ToString(CultureInfo.InvariantCulture);
        float keySize = Px(13f);
        var cap = new UiRect(plate.X + Px(8f), plate.Y + Px(8f), Px(24f), Px(24f));
        if (own)
        {
            painter.FillRect(cap, VoteAccent);
        }
        else
        {
            painter.FillRect(cap, Bgra32.FromRgba(0, 0, 0, 140));
            painter.StrokeRect(cap, Px(1f), Bgra32.FromRgba(255, 255, 255, 90));
        }

        Centred(painter, type.Mono, key, cap.X + (cap.Width * 0.5f), cap.Y + ((cap.Height - keySize) * 0.5f), keySize, own ? Bgra32.FromRgb(16, 14, 10) : UiPalette.TextBright, 0f);

        // A tag on the plate: this map again, or the one ahead.
        string? tag = choice.Again ? "PLAY AGAIN" : null;
        if (tag is not null)
        {
            float tagSize = Px(10f);
            float tagWidth = UiPainter.MeasureLabel(type.Mono, tag, tagSize, 0.2f) + Px(12f);
            var tagBox = new UiRect(plate.X + Px(8f), plate.Bottom - Px(8f) - Px(18f), tagWidth, Px(18f));
            painter.FillRect(tagBox, Bgra32.FromRgba(0, 0, 0, 150));
            painter.StrokeRect(tagBox, Px(1f), Bgra32.FromRgba(255, 255, 255, 60));
            painter.DrawLabel(type.Mono, tag, new UiVec2(tagBox.X + Px(6f), tagBox.Y + Px(4f)), tagSize, UiPalette.TextSecondary, 0.2f);
        }

        // The map's name and its mode.
        float textX = card.X + Px(14f);
        float textWidth = card.Width - Px(28f);
        string name = (choice.Name.Length > 0 ? choice.Name : string.Create(CultureInfo.InvariantCulture, $"Mission {choice.Mission}")).ToUpperInvariant();
        float nameSize = Px(21f);
        painter.DrawLabel(type.Heading, Clip(type.Heading, name, nameSize, textWidth), new UiVec2(textX, plate.Bottom + Px(12f)), nameSize, UiPalette.TextBright, 0.06f);
        string mode = GameModes.Title(choice.Mode);
        painter.DrawLabel(type.Mono, mode, new UiVec2(textX, plate.Bottom + Px(42f)), Px(12f), own ? VoteAccent : UiPalette.TextSecondary, 0.22f);

        // The votes: a bar of the share of those who may vote, and the count.
        float barY = card.Bottom - Px(22f);
        float share = vote.Voters <= 0 ? 0f : Math.Clamp(votes / (float)vote.Voters, 0f, 1f);
        var track = new UiRect(textX, barY, textWidth - Px(70f), Px(4f));
        painter.FillRect(track, Bgra32.FromRgba(255, 255, 255, 26));
        if (share > 0f)
        {
            painter.FillRect(new UiRect(track.X, track.Y, track.Width * share, track.Height), lead ? VoteAccent : Bgra32.FromRgba(255, 255, 255, 150));
        }

        string counted = votes == 1 ? "1 VOTE" : string.Create(CultureInfo.InvariantCulture, $"{votes} VOTES");
        float countSize = Px(11f);
        painter.DrawLabel(type.Mono, counted, new UiVec2(card.Right - Px(14f) - UiPainter.MeasureLabel(type.Mono, counted, countSize, 0.12f), barY - Px(5f)), countSize, lead ? VoteAccent : UiPalette.TextMuted, 0.12f);
        if (lead)
        {
            painter.DrawLabel(type.Mono, "LEADING", new UiVec2(textX, barY - Px(18f)), Px(10f), VoteAccent, 0.25f);
        }

        // The edge: amber and heavier for the player's own pick, with its word on the top edge.
        if (own)
        {
            painter.StrokeRect(card, Px(2f), VoteAccent);
            painter.FillRect(new UiRect(card.X, card.Y, card.Width, Px(4f)), VoteAccent);
            string yours = "YOUR VOTE";
            float yoursSize = Px(10f);
            float yoursWidth = UiPainter.MeasureLabel(type.Mono, yours, yoursSize, 0.2f) + Px(14f);
            var badge = new UiRect(card.Right - yoursWidth, card.Y, yoursWidth, Px(20f));
            painter.FillRect(badge, VoteAccent);
            painter.DrawLabel(type.Mono, yours, new UiVec2(badge.X + Px(7f), badge.Y + Px(5f)), yoursSize, Bgra32.FromRgb(16, 14, 10), 0.2f);
        }
        else
        {
            painter.StrokeRect(card, Px(1f), over ? VoteAccent.WithAlpha(160) : CardEdge);
        }
    }
}

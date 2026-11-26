using OpenIGI.Core.Titles;
using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// The vote for the next map, under a decided match's card, in the played title's look: a sheet for each map offered -- its
/// number key, its mission's number large on a plate, its name and mode, and the title's meter of the votes it has -- the
/// player's own pick washed in the accent and the one leading marked, how many have voted above them, and how to vote below.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // How far below the safe area's top a decided match's card stands while the vote is open: under the score strip.
    private const float IntermissionCardTop = 76f;

    // The match card's height, as ResultBand draws a match's.
    private const float MatchCardHeight = 196f;

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
        if (vote.Choices.Count == 0 || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        UiRect safe = o.Safe;
        float centre = safe.X + (safe.Width * 0.5f);

        // Alone, one row of sheets across the middle; beside the board, the right-hand area, two a row.
        int count = vote.Choices.Count;
        UiRect? area = besideBoard ? IntermissionAreas(o.Context).Vote : null;
        int columns = besideBoard ? Math.Min(count, 2) : count;
        int rows = (count + columns - 1) / columns;
        float gap = o.Px(16f);
        float room = area?.Width ?? (safe.Width - o.Px(48f));
        float cardWidth = Math.Min(o.Px(272f), (room - (gap * (columns - 1))) / columns);
        float cardHeight = o.Px(212f);
        float rowWidth = (cardWidth * columns) + (gap * (columns - 1));
        if (area is { } shared)
        {
            centre = shared.X + (shared.Width * 0.5f);
        }

        float left = centre - (rowWidth * 0.5f);
        float top = area is { } within ? within.Y + o.Px(56f) : safe.Y + (safe.Height * 0.2f) + o.Px(194f);
        float block = (cardHeight * rows) + (gap * (rows - 1));
        if (area is null)
        {
            o.GlassBand(new UiRect(safe.X, top - o.Px(56f), safe.Width, cardHeight + o.Px(100f)), HudOverlay.Fade(o.Ink.Primary, 0.6f));
        }

        // Above the sheets: what this is, and how many have voted (the card above has the clock).
        float small = o.Px(HudOverlay.Small);
        float headY = top - o.Px(18f) - o.Text.LineHeightAt(small);
        o.Text.DrawAt("VOTE FOR THE NEXT MAP", left, headY, small, o.Ink.Accent);
        string tally = string.Create(CultureInfo.InvariantCulture, $"{vote.Picked} OF {Math.Max(vote.Voters, vote.Picked)} VOTED");
        string right = clock is { } c && vote.Picked >= vote.Voters && vote.Voters > 0 ? $"{tally} - GOING ON IN {c}" : tally;
        o.Text.DrawRightAt(right, left + rowWidth, headY, small, o.Quiet);

        int leading = vote.Leading;
        for (int i = 0; i < count; i++)
        {
            var card = new UiRect(left + (i % columns * (cardWidth + gap)), top + (i / columns * (cardHeight + gap)), cardWidth, cardHeight);
            bool over = pointer.X >= card.X && pointer.X <= card.Right && pointer.Y >= card.Y && pointer.Y <= card.Bottom;
            VoteCard(o, card, vote, i, own: vote.OwnPick == i, lead: leading == i, over);
            _voteCards.Add((i, card.X, card.Y, card.Width, card.Height));
        }

        string how = count > 1
            ? besideBoard
                ? string.Create(CultureInfo.InvariantCulture, $"PRESS 1-{count} OR CLICK A MAP - CHANGE IT UNTIL THE CLOCK RUNS OUT")
                : string.Create(CultureInfo.InvariantCulture, $"PRESS 1-{count} OR CLICK A MAP TO VOTE - YOU CAN CHANGE YOUR VOTE UNTIL THE CLOCK RUNS OUT")
            : "PRESS 1 OR CLICK TO VOTE";
        o.Text.DrawCentredAt(how, centre, top + block + o.Px(18f), small, o.Quiet);
        UiCursor.Draw(o.Painter, new UiVec2(pointer.X, pointer.Y));
    }

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
    private static void VoteCard(HudOverlay o, UiRect card, NextMapVote vote, int index, bool own, bool lead, bool over)
    {
        MapChoice choice = vote.Choices[index];
        int votes = index < vote.Counts.Count ? vote.Counts[index] : 0;
        CardFrame(o, card, own, over, own ? o.Ink.Accent : o.Ink.Primary);

        // The plate: the title's colour faint, the mission's number large at its right.
        var plate = new UiRect(card.X + o.Px(10f), card.Y + o.Px(12f), card.Width - o.Px(20f), o.Px(84f));
        o.Plate(plate, HudOverlay.Fade(o.Ink.Primary, 0.08f));
        string number = choice.Mission.ToString("00", CultureInfo.InvariantCulture);
        float figure = o.Px(64f);
        o.Text.DrawRightAt(number, plate.Right - o.Px(8f), plate.Bottom - o.Text.LineHeightAt(figure) - o.Px(4f), figure, HudOverlay.Fade(o.Ink.Primary, own ? 0.5f : 0.3f));
        o.KeyCap((index + 1).ToString(CultureInfo.InvariantCulture), plate.X + o.Px(8f), plate.Y + o.Px(8f), lit: own);
        float small = o.Px(HudOverlay.Small);
        if (choice.Again)
        {
            o.Tag("PLAY AGAIN", plate.X + o.Px(8f), plate.Bottom - o.Text.LineHeightAt(small) - o.Px(10f), o.Quiet);
        }

        // The map's name and its mode.
        float body = o.Px(HudOverlay.Body);
        float textX = card.X + o.Px(14f);
        float textWidth = card.Width - o.Px(28f);
        string name = choice.Name.Length > 0 ? choice.Name : string.Create(CultureInfo.InvariantCulture, $"Mission {choice.Mission}");
        float y = plate.Bottom + o.Px(10f);
        o.Text.DrawAt(o.Text.ClipAt(OverlayText.Capitals(name), body, textWidth), textX, y, body, o.Ink.Highlight);
        y += o.Text.LineHeightAt(body) + o.Px(4f);
        o.Text.DrawAt(OverlayText.Capitals(MatchModes.Title(choice.Mode)), textX, y, small, own ? o.Ink.Accent : o.Quiet);

        // The votes: the share of those who may vote, and the count; the one ahead marked.
        float barY = card.Bottom - o.Px(20f);
        float share = vote.Voters <= 0 ? 0f : Math.Clamp(votes / (float)vote.Voters, 0f, 1f);
        o.Meter(new UiRect(textX, barY, textWidth - o.Px(80f), MathF.Max(2f, o.Px(4f))), share, lead ? o.Ink.Accent : o.Ink.Primary);
        string counted = votes == 1 ? "1 VOTE" : string.Create(CultureInfo.InvariantCulture, $"{votes} VOTES");
        o.Text.DrawRightAt(counted, card.Right - o.Px(14f), barY - (o.Text.LineHeightAt(small) * 0.5f), small, lead ? o.Ink.Accent : o.Quiet);
        if (lead)
        {
            o.Text.DrawAt("LEADING", textX, barY - o.Text.LineHeightAt(small) - o.Px(6f), small, o.Ink.Accent);
        }

        if (own)
        {
            o.Tag("YOUR VOTE", card.Right - o.Text.MeasureAt("YOUR VOTE", small) - o.Px(20f), card.Y + o.Px(16f), o.Ink.Accent, filled: true);
        }
    }

    // A map's sheet on the vote: the title's flat glass, washed in its ink -- faintly under the pointer, more for this player's pick.
    private static void CardFrame(HudOverlay o, UiRect card, bool own, bool over, Bgra32 ink)
    {
        o.Sheet(card);
        if (own)
        {
            o.Plate(card, HudOverlay.Fade(ink, 0.2f));
        }
        else if (over)
        {
            o.Plate(card, HudOverlay.Fade(ink, 0.1f));
        }
    }
}

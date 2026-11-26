using OpenIGI.Core.Graphics;
using OpenIGI.Game.Scoring;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The best the player has done in the mission the campaign page shows, as retail's Select Mission screen shows its stats beside the
/// list (<c>MenuStatsScreen</c>, its active mission the list's): the rank earned with the ranks marked beside it, then what was
/// achieved beside what the next rank asks, a figure that falls short of it in the danger's ink. A mission begun but never
/// completed shows a dash for each figure beside what the first rank asks. Collapsed while the mission has no record.
/// </summary>
/// <param name="theme">The theme it draws from.</param>
/// <param name="shell">The page's arrangement, which sizes its type.</param>
internal sealed class MissionBestPanel(UiTheme theme, ShellSize shell) : UiElement
{
    private static readonly Bgra32 Glass = Bgra32.FromRgba(12, 14, 15, 199);

    // What a figure of a mission never completed reads.
    private const string NotPlayed = "—";

    private MissionScoreCard? _card;

    private bool Compact => shell == ShellSize.Compact;

    private float Pad => Compact ? 12f : 22f;

    private float HeadUnits => Compact ? 12f : 13f;

    private float RankUnits => Compact ? 18f : 26f;

    private float RowUnits => Compact ? 13f : 16f;

    private float RowHeight => Compact ? 20f : 28f;

    private float ColumnGap => Compact ? 16f : 32f;

    /// <summary>Shows a mission's best, or nothing.</summary>
    /// <param name="card">The card, or <see langword="null"/> for a mission with no record.</param>
    public void Show(MissionScoreCard? card)
    {
        _card = card;
        Visibility = card is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        if (_card is not { } card)
        {
            return UiVec2.Zero;
        }

        float height = (Pad * 2f) + (RankUnits * 1.3f) + 12f + (HeadUnits * 1.4f) + 6f + (card.Lines.Count * RowHeight);
        return new UiVec2(available.X, context.Px(height));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (_card is not { } card)
        {
            return;
        }

        LayoutContext context = painter.Context;
        painter.FillRect(Bounds, Glass);
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.White(0.12f));

        float pad = context.Px(Pad);
        float left = Bounds.X + pad;
        float right = Bounds.Right - pad;
        float y = Bounds.Y + pad;

        // The rank earned, large, and the ranks to earn as the HUD's diamonds at the right: as many lit as it is high.
        float rankSize = context.Px(RankUnits);
        string rank = card.RankName.ToUpperInvariant();
        float rankLine = rankSize * 1.3f;
        var rankBox = new UiRect(left, y, right - left, rankLine);
        painter.DrawLabel(theme.Heading, rank, new UiVec2(left, UiRowStyle.CapsCenterY(theme.Heading, rankSize, rankBox)), rankSize, card.Rank >= 0 ? theme.Accent : theme.TextSecondary, 0.06f);
        float pip = context.Px(Compact ? 4f : 6f);
        for (int i = 0; i < card.RankCount; i++)
        {
            float x = right - pip - ((card.RankCount - 1 - i) * pip * 3f);
            painter.FillDiamond(new UiVec2(x, rankBox.Center.Y), pip, i <= card.Rank ? theme.Accent : UiPalette.White(0.18f));
        }

        y += rankLine + context.Px(12f);

        // The columns as wide as their widest word.
        float headSize = context.Px(HeadUnits);
        float rowSize = context.Px(RowUnits);
        float labelWidth = Measure(card.CategoryHeading.ToUpperInvariant(), headSize, theme.Heading);
        float achievedWidth = Measure(card.AchievedHeading.ToUpperInvariant(), headSize, theme.Heading);
        foreach (MissionScoreLine line in card.Lines)
        {
            labelWidth = MathF.Max(labelWidth, Measure(line.Label, rowSize, theme.Body));
            achievedWidth = MathF.Max(achievedWidth, Measure(line.Achieved, rowSize, theme.BodyStrong));
        }

        float gap = context.Px(ColumnGap);
        float achievedX = left + labelWidth + gap;
        float targetX = achievedX + achievedWidth + gap;

        var headBox = new UiRect(left, y, right - left, headSize * 1.4f);
        float headY = UiRowStyle.CapsCenterY(theme.Heading, headSize, headBox);
        painter.DrawLabel(theme.Heading, card.CategoryHeading.ToUpperInvariant(), new UiVec2(left, headY), headSize, theme.TextMuted, 0.14f);
        painter.DrawLabel(theme.Heading, card.AchievedHeading.ToUpperInvariant(), new UiVec2(achievedX, headY), headSize, theme.TextMuted, 0.14f);
        if (card.TargetHeading is { } target)
        {
            painter.DrawLabel(theme.Heading, target.ToUpperInvariant(), new UiVec2(targetX, headY), headSize, theme.TextMuted, 0.14f);
        }

        y = headBox.Bottom + context.Px(2f);
        painter.FillRect(new UiRect(left, y, right - left, context.Px(1f)), UiPalette.White(0.14f));
        y += context.Px(4f);

        float rowHeight = context.Px(RowHeight);
        foreach (MissionScoreLine line in card.Lines)
        {
            float textY = y + ((rowHeight - theme.Body.Measure("0", rowSize).Y) * 0.5f);
            painter.DrawText(theme.Body, line.Label, new UiVec2(left, textY), rowSize, theme.TextSecondary);
            if (card.IsCompleted)
            {
                painter.DrawText(theme.BodyStrong, line.Achieved, new UiVec2(achievedX, textY), rowSize, line.Met ? theme.Text : theme.Danger);
            }
            else
            {
                painter.DrawText(theme.BodyStrong, NotPlayed, new UiVec2(achievedX, textY), rowSize, theme.TextMuted);
            }
            if (line.Target is { } aim)
            {
                painter.DrawText(theme.Body, aim, new UiVec2(targetX, textY), rowSize, theme.TextMuted);
            }

            y += rowHeight;
        }
    }

    private static float Measure(string text, float size, UiFont font) => font.Measure(text, size).X;
}

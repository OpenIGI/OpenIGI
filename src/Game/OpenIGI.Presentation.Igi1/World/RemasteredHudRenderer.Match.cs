using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>The match's banner: its mode and clock across the top, a decided match's result, and a warning to get back.</content>
internal sealed partial class RemasteredHudRenderer
{
    /// <inheritdoc/>
    public void DrawMatchBanner(SpriteBatch batch, MatchBanner banner, string? warning, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(banner);
        if (_theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);

        // The pill across the top: the mode, and its clock.
        string line = banner.Clock is { } clock ? $"{banner.Title}  ·  {clock}" : banner.Title;
        float size = Px(18f);
        float width = type.Heading.MeasureWidth(line, size) + Px(40f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float y = context.SafeRect.Y + Px(14f);
        var pill = new UiRect(x, y, width, Px(36f));
        painter.FillRoundedRect(pill, _surfaces?.Rounded ?? default, BoardFace, Px(18f));
        painter.DrawLabel(type.Heading, line, new UiVec2(x + Px(20f), y + Px(8f)), size, UiPalette.TextBright, 0.04f);

        // A decided match's result, large, below it; or a line explaining the phase.
        if (banner.Headline is { } headline)
        {
            float big = Px(44f);
            float headWidth = type.Heading.MeasureWidth(headline, big) * 1.06f;
            painter.DrawLabel(type.Heading, headline, new UiVec2(context.SafeRect.X + ((context.SafeRect.Width - headWidth) * 0.5f), y + Px(60f)), big, Accent, 0.06f);
        }

        if (warning is not null)
        {
            float warn = Px(30f);
            float warnWidth = type.Heading.MeasureWidth(warning, warn) * 1.06f;
            painter.DrawLabel(
                type.Heading, warning, new UiVec2(context.SafeRect.X + ((context.SafeRect.Width - warnWidth) * 0.5f), context.SafeRect.Y + (context.SafeRect.Height * 0.3f)), warn, Bad, 0.06f);
        }
    }

    /// <inheritdoc/>
    public void DrawKillFeed(SpriteBatch batch, IReadOnlyList<KillNote> kills, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(kills);
        if (_theme is not { } type || kills.Count == 0)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        float size = Px(16f);
        float gap = Px(10f);
        float right = context.SafeRect.X + context.SafeRect.Width - Px(16f);
        float y = context.SafeRect.Y + Px(64f);
        foreach (KillNote kill in kills)
        {
            // Killer, then what happened, then victim; a death with no killer reads as the victim's own: "Joe died".
            bool own = kill.Killer.Length == 0;
            (string Text, Bgra32 Colour)[] parts = own
                ? [(kill.Victim, SideColour(kill.VictimTeam)), ("died", UiPalette.TextBody)]
                : [(kill.Killer, SideColour(kill.KillerTeam)), ("killed", UiPalette.TextBody), (kill.Victim, SideColour(kill.VictimTeam))];
            float width = gap;
            foreach ((string text, _) in parts)
            {
                width += type.Body.MeasureWidth(text, size) + gap;
            }

            var row = new UiRect(right - width, y, width, Px(28f));
            painter.FillRoundedRect(row, _surfaces?.Rounded ?? default, BoardFace, Px(6f));
            float x = row.X + gap;
            foreach ((string text, Bgra32 colour) in parts)
            {
                painter.DrawLabel(type.Body, text, new UiVec2(x, y + Px(5f)), size, colour, 0f);
                x += type.Body.MeasureWidth(text, size) + gap;
            }

            y += Px(32f);
        }
    }

    // A team's colour in the feed: the accent for one side, the warning red for the other, white for nobody's.
    private static Bgra32 SideColour(int team) =>
        team == Game.Multiplayer.TeamRoster.Good ? Accent : team == Game.Multiplayer.TeamRoster.Bad ? Bad : UiPalette.TextBright;
}

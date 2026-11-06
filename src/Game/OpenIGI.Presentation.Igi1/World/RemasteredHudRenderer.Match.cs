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
}

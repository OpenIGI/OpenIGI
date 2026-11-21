using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>The match's banner: its mode and clock across the top, a decided match's result, and a warning to get back.</content>
public sealed partial class RemasteredHudRenderer
{
    /// <inheritdoc/>
    public void DrawMatchBanner(SpriteBatch batch, MatchBanner banner, string? warning, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(banner);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        // The plate across the top: the mode, and its clock.
        float body = o.Px(HudOverlay.Body);
        float centre = o.Safe.X + (o.Safe.Width * 0.5f);
        string title = OverlayText.Capitals(banner.Title);
        string line = banner.Clock is { } clock ? $"{title} - {clock}" : title;
        float width = o.Text.MeasureAt(line, body) + o.Px(36f);
        var plate = new UiRect(centre - (width * 0.5f), o.Safe.Y + o.Px(14f), width, o.Text.LineHeightAt(body) + o.Px(14f));
        o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 190));
        o.Brackets(plate, HudOverlay.Fade(o.Ink.Primary, 0.8f), 5f);
        o.Text.DrawCentredAt(line, centre, plate.Y + o.Px(7f), body, o.Ink.Highlight);

        // A decided match's result, large, below it.
        if (banner.Headline is { } headline)
        {
            o.Text.DrawCentredAt(OverlayText.Capitals(headline), centre, plate.Bottom + o.Px(20f), o.Px(HudOverlay.Huge), o.Ink.Accent, shadow: true);
        }

        if (warning is not null)
        {
            o.Text.DrawCentredAt(OverlayText.Capitals(warning), centre, o.Safe.Y + (o.Safe.Height * 0.3f), o.Px(HudOverlay.Large), o.Ink.Danger, shadow: true);
        }
    }
}

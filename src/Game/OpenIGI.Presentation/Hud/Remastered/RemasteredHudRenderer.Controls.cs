using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// The controls sheet held up on its key, in the played title's look: the scoreboard's sheet, a column for each kind of action --
/// its title on the title's rule, then each action with its keys as the title's key caps at the right -- and the hint that says
/// how to hold it up.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // The controls sheet's measures, in layout pixels.
    private const float ControlsWidth = 1240f;
    private const float ControlsGutter = 30f;

    /// <inheritdoc/>
    public void DrawControlsHint(SpriteBatch batch, string holdKey, float opacity, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (opacity <= 0f || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        // The top left, clear of the match's strip in the middle, the kill feed on the right and the status at the foot.
        o.Painter.PushOpacity(Math.Clamp(opacity, 0f, 1f));
        const string Label = "HOLD FOR CONTROLS";
        float small = o.Px(HudOverlay.Small);
        float capWidth = o.KeyCapWidth(holdKey);
        var plate = new UiRect(o.Safe.X + o.Px(24f), o.Safe.Y + o.Px(24f), capWidth + o.Text.MeasureAt(Label, small) + o.Px(30f), o.KeyCapHeight + o.Px(12f));
        o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 170));
        UiRect cap = o.KeyCap(holdKey, plate.X + o.Px(6f), plate.Y + o.Px(6f));
        o.Text.DrawAt(Label, cap.Right + o.Px(12f), cap.Y + ((cap.Height - o.Text.LineHeightAt(small)) * 0.5f), small, o.Quiet);
        o.Painter.PopOpacity();
    }

    /// <inheritdoc/>
    public void DrawControls(SpriteBatch batch, ControlsHelp help, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(help);
        if (help.Sections.Count == 0 || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float pad = o.Px(BoardPad);
        int columns = help.Sections.Count;
        int rows = help.Sections.Max(static s => s.Lines.Count);
        float rowHeight = MathF.Max(o.KeyCapHeight, o.Text.LineHeightAt(body)) + o.Px(6f);
        float header = o.Text.LineHeightAt(body) + o.Px(22f);
        float sectionHead = o.Text.LineHeightAt(body) + o.Px(16f);
        float footer = o.KeyCapHeight + o.Px(20f);
        float width = MathF.Min(o.Px(ControlsWidth), o.Safe.Width * 0.94f);
        float height = header + o.Px(10f) + sectionHead + (rows * rowHeight) + o.Px(10f) + footer;
        float x = o.Safe.X + ((o.Safe.Width - width) * 0.5f);
        float top = o.Safe.Y + MathF.Max(o.Px(20f), (o.Safe.Height - height) * 0.4f);
        var sheet = new UiRect(x, top, width, height);
        o.Sheet(sheet);

        // The header: what this is, and the match it is for.
        var strip = new UiRect(x, top, width, header);
        o.HeaderStrip(strip);
        float stripY = strip.Y + ((strip.Height - o.Text.LineHeightAt(body)) * 0.5f);
        o.Diamond(new UiVec2(x + pad + o.Px(4f), stripY + (o.Text.LineHeightAt(body) * 0.5f)), o.Px(5f), o.Ink.Accent);
        float after = x + pad + o.Px(16f) + o.Text.DrawAt("CONTROLS", x + pad + o.Px(16f), stripY, body, o.Ink.Highlight) + o.Px(18f);
        if (help.Mode.Length > 0)
        {
            o.Text.DrawAt(OverlayText.Capitals(help.Mode), after, stripY, body, o.Quiet);
        }

        // The columns: a title over each on the theme's rule, then its actions, the action at the left and its keys at the right.
        float gutter = o.Px(ControlsGutter);
        float columnWidth = (width - (pad * 2f) - (gutter * (columns - 1))) / columns;
        for (int c = 0; c < columns; c++)
        {
            ControlsSection section = help.Sections[c];
            float left = x + pad + (c * (columnWidth + gutter));
            float right = left + columnWidth;
            float y = strip.Bottom + o.Px(10f);
            o.Text.DrawAt(OverlayText.Capitals(section.Title), left, y, body, o.Ink.Accent);
            o.Rule(left, y + o.Text.LineHeightAt(body) + o.Px(4f), columnWidth, HudOverlay.Fade(o.Ink.Primary, 0.55f));
            y += sectionHead;
            for (int r = 0; r < section.Lines.Count; r++)
            {
                ControlsLine line = section.Lines[r];
                var row = new UiRect(left - o.Px(6f), y, columnWidth + o.Px(12f), rowHeight);
                if (r % 2 == 0)
                {
                    o.Plate(row, HudOverlay.Fade(o.Ink.Primary, 0.05f));
                }

                // The keys from the right, each its own cap, so a long one never crowds the action out.
                float capRight = right;
                float capTop = row.Y + ((row.Height - o.KeyCapHeight) * 0.5f);
                for (int k = line.Keys.Count - 1; k >= 0; k--)
                {
                    float capWidth = o.KeyCapWidth(line.Keys[k]);
                    o.KeyCap(line.Keys[k], capRight - capWidth, capTop);
                    capRight -= capWidth + o.Px(5f);
                }

                string action = o.Text.ClipAt(OverlayText.Prose(line.Action), body, capRight - left - o.Px(8f));
                o.Text.DrawAt(action, left, row.Y + ((row.Height - o.Text.LineHeightAt(body)) * 0.5f), body, o.Ink.Plain);
                y += rowHeight;
            }
        }

        // The foot: where the keys are changed, and how the sheet is kept up.
        float capY = sheet.Bottom - footer + o.Px(8f);
        float footY = capY + ((o.KeyCapHeight - o.Text.LineHeightAt(small)) * 0.5f);
        o.Rule(x + pad, capY - o.Px(6f), width - (pad * 2f), HudOverlay.Fade(o.Ink.Primary, 0.3f));
        o.Text.DrawAt("CHANGE ANY KEY IN SETTINGS > CONTROLS", x + pad, footY, small, o.Quiet);
        float holdCap = o.KeyCapWidth(help.HoldKey);
        float capX = x + width - pad - holdCap;
        o.KeyCap(help.HoldKey, capX, capY);
        o.Text.DrawRightAt("HOLD", capX - o.Px(10f), footY, small, o.Quiet);
    }
}

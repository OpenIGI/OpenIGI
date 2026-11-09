using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

internal sealed partial class RemasteredHudRenderer
{
    // The controls panel's measures, in design units: the scoreboard's card, its columns side by side.
    private const float ControlsWidth = 1240f;
    private const float ControlsHeader = 84f;
    private const float ControlsRow = 40f;
    private const float ControlsSectionHead = 40f;
    private const float ControlsFooter = 50f;
    private const float KeyCapHeight = 28f;

    private static Bgra32 CapFace => Bgra32.FromRgba(255, 255, 255, 18);
    private static Bgra32 CapEdge => Bgra32.FromRgba(255, 255, 255, 70);

    /// <inheritdoc/>
    public void DrawControlsHint(SpriteBatch batch, string holdKey, float opacity, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_theme is not { } type || opacity <= 0f)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);

        // The top left, clear of the match's strip in the middle, the kill feed on the right and the status at the foot.
        painter.PushOpacity(Math.Clamp(opacity, 0f, 1f));
        float size = Px(LabelText);
        string label = "HOLD FOR CONTROLS";
        float capWidth = KeyCapWidth(type, holdKey, context);
        float labelWidth = UiPainter.MeasureLabel(type.Heading, label, size, 0.12f);
        var plate = new UiRect(context.SafeRect.X + Px(24f), context.SafeRect.Y + Px(24f), capWidth + labelWidth + Px(36f), Px(KeyCapHeight + 14f));
        painter.FillRoundedRect(plate, _surfaces?.Rounded ?? default, BoardFace.WithAlpha(190), Px(6f));
        float capX = plate.X + Px(8f);
        KeyCap(painter, type, context, holdKey, capX, plate.Y + Px(7f));
        painter.DrawLabel(type.Heading, label, new UiVec2(capX + capWidth + Px(12f), UiRowStyle.CapsCenterY(type.Heading, size, plate)), size, UiPalette.TextMuted, 0.12f);
        painter.PopOpacity();
    }

    /// <inheritdoc/>
    public void DrawControls(SpriteBatch batch, ControlsHelp help, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(help);
        if (_theme is not { } type || help.Sections.Count == 0)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);

        int columns = help.Sections.Count;
        int rows = help.Sections.Max(static s => s.Lines.Count);
        float width = MathF.Min(Px(ControlsWidth), context.SafeRect.Width * 0.94f);
        float height = Px(ControlsHeader) + Px(ControlsSectionHead) + (rows * Px(ControlsRow)) + Px(24f) + Px(ControlsFooter);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float top = context.SafeRect.Y + MathF.Max(Px(20f), (context.SafeRect.Height - height) * 0.4f);
        float pad = Px(BoardPad);
        var panel = new UiRect(x, top, width, height);
        float hair = MathF.Max(1f, Px(1f));
        painter.FillRoundedRect(panel, _surfaces?.Rounded ?? default, BoardEdge, Px(10f));
        painter.FillRoundedRect(new UiRect(panel.X + hair, panel.Y + hair, panel.Width - (hair * 2f), panel.Height - (hair * 2f)), _surfaces?.Rounded ?? default, BoardFace, Px(10f) - hair);

        // The header: the chip, and the match it is for.
        var header = new UiRect(x, top, width, Px(ControlsHeader));
        float chipText = Px(LabelText + 2f);
        const string Tag = "CONTROLS";
        float chipWidth = UiPainter.MeasureLabel(type.Heading, Tag, chipText, 0.1f) + Px(24f);
        var chip = new UiRect(x + pad, header.Y + ((header.Height - Px(36f)) * 0.5f), chipWidth, Px(36f));
        painter.FillRect(chip, Accent);
        painter.DrawLabel(type.Heading, Tag, new UiVec2(chip.X + Px(12f), UiRowStyle.CapsCenterY(type.Heading, chipText, chip)), chipText, Bgra32.FromRgb(20, 16, 10), 0.1f);
        if (help.Mode.Length > 0)
        {
            Stat(painter, type, context, chip.Right + Px(22f), header, "MATCH", help.Mode, Text, leftAligned: true, Px(26f));
        }

        painter.FillRect(new UiRect(x, header.Bottom, width, 1f), Hairline);

        // The columns: a heading over each, then its actions, the action at the left and its keys at the right.
        float gutter = Px(28f);
        float columnWidth = (width - (pad * 2f) - (gutter * (columns - 1))) / columns;
        float headSize = Px(LabelText);
        float actionSize = Px(18f);
        for (int c = 0; c < columns; c++)
        {
            ControlsSection section = help.Sections[c];
            float left = x + pad + (c * (columnWidth + gutter));
            float right = left + columnWidth;
            var head = new UiRect(left, header.Bottom + Px(8f), columnWidth, Px(ControlsSectionHead));
            float headY = UiRowStyle.CapsCenterY(type.Heading, headSize, head);
            painter.DrawLabel(type.Heading, section.Title, new UiVec2(left, headY), headSize, Accent, 0.14f);
            painter.FillRect(new UiRect(left, head.Bottom - Px(4f), columnWidth, 1f), Hairline);

            float y = head.Bottom;
            for (int r = 0; r < section.Lines.Count; r++)
            {
                ControlsLine line = section.Lines[r];
                var row = new UiRect(left - Px(8f), y, columnWidth + Px(16f), Px(ControlsRow));
                if (r % 2 == 0)
                {
                    painter.FillRoundedRect(row, _surfaces?.Rounded ?? default, RowFace.WithAlpha(8), Px(4f));
                }

                // The keys from the right, each its own cap, so a long one never crowds the action out.
                float capRight = right;
                float capTop = row.Y + ((row.Height - Px(KeyCapHeight)) * 0.5f);
                for (int k = line.Keys.Count - 1; k >= 0; k--)
                {
                    float capWidth = KeyCapWidth(type, line.Keys[k], context);
                    KeyCap(painter, type, context, line.Keys[k], capRight - capWidth, capTop);
                    capRight -= capWidth + Px(5f);
                }

                float room = capRight - left - Px(8f);
                string action = Fit(type.Body, line.Action, actionSize, room);
                painter.DrawLabel(type.Body, action, new UiVec2(left, UiRowStyle.CapsCenterY(type.Body, actionSize, row)), actionSize, UiPalette.TextBright, 0f);
                y += Px(ControlsRow);
            }
        }

        // The foot: how it is kept up, and where the keys are changed.
        float footerTop = top + height - Px(ControlsFooter);
        painter.FillRect(new UiRect(x, footerTop, width, 1f), Hairline);
        var footer = new UiRect(x, footerTop, width, Px(ControlsFooter));
        float footText = Px(LabelText);
        float footY = UiRowStyle.CapsCenterY(type.Heading, footText, footer);
        painter.DrawLabel(type.Heading, "CHANGE ANY KEY IN SETTINGS  ›  CONTROLS", new UiVec2(x + pad, footY), footText, UiPalette.TextMuted, 0.12f);
        float holdCap = KeyCapWidth(type, help.HoldKey, context);
        float capX = x + width - pad - holdCap;
        KeyCap(painter, type, context, help.HoldKey, capX, footer.Y + ((footer.Height - Px(KeyCapHeight)) * 0.5f));
        RightLabel(painter, type.Heading, "HOLD", capX - Px(12f), footY, footText, UiPalette.TextMuted);
    }

    // A key's cap: its name in an outlined box, as wide as the name needs.
    private static void KeyCap(UiPainter painter, UiTheme type, LayoutContext context, string key, float left, float top)
    {
        float width = KeyCapWidth(type, key, context);
        float height = context.Px(KeyCapHeight);
        var cap = new UiRect(left, top, width, height);
        painter.FillRect(cap, CapFace);
        Outline(painter, cap, CapEdge, MathF.Max(1f, context.Px(1f)));
        float size = context.Px(LabelText);
        float textWidth = UiPainter.MeasureLabel(type.Heading, key, size, 0.06f);
        painter.DrawLabel(type.Heading, key, new UiVec2(left + ((width - textWidth) * 0.5f), UiRowStyle.CapsCenterY(type.Heading, size, cap)), size, UiPalette.TextBright, 0.06f);
    }

    private static float KeyCapWidth(UiTheme type, string key, LayoutContext context) =>
        MathF.Max(context.Px(KeyCapHeight), UiPainter.MeasureLabel(type.Heading, key, context.Px(LabelText), 0.06f) + context.Px(16f));

    // A line cut to the room it has, with an ellipsis where it was cut.
    private static string Fit(UiFont font, string text, float size, float room)
    {
        if (room <= 0f || font.MeasureWidth(text, size) <= room)
        {
            return text;
        }

        for (int length = text.Length - 1; length > 0; length--)
        {
            string cut = string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
            if (font.MeasureWidth(cut, size) <= room)
            {
                return cut;
            }
        }

        return string.Empty;
    }
}

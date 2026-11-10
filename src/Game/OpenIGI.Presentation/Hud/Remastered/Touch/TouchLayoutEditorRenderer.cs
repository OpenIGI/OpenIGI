using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>Draws the layout editor (<see cref="TouchLayoutEditor"/>): the dimmed screen and its grid under the controls, and over them the items' outlines, the guides, the bar and the question on leaving.</summary>
/// <remarks>
/// In the touch controls' own look: smoked glass and white line pictures, the title's accent on what is selected or lit, and
/// red only where one control laps another.
/// </remarks>
public static class TouchLayoutEditorRenderer
{
    private static readonly Bgra32 Scrim = Bgra32.FromRgba(0, 0, 0, 110);
    private static readonly Bgra32 GridLine = Bgra32.FromRgba(255, 255, 255, 14);
    private static readonly Bgra32 Glass = Bgra32.FromRgba(14, 16, 18, 215);
    private static readonly Bgra32 Button = Bgra32.FromRgba(255, 255, 255, 22);
    private static readonly Bgra32 Rim = Bgra32.FromRgba(255, 255, 255, 60);
    private static readonly Bgra32 Ink = Bgra32.FromRgba(255, 255, 255, 230);
    private static readonly Bgra32 InkFaint = Bgra32.FromRgba(255, 255, 255, 90);
    private static readonly Bgra32 Outline = Bgra32.FromRgba(255, 255, 255, 120);
    private static readonly Bgra32 Clash = Bgra32.FromRgba(255, 84, 72, 235);
    private static readonly Bgra32 DefaultAccent = Bgra32.FromRgba(255, 182, 64, 255);
    private static readonly Bgra32 Dark = Bgra32.FromRgba(12, 12, 12, 255);

    /// <summary>Draws what lies under the controls: the screen dimmed, and a faint grid.</summary>
    /// <param name="batch">The batch to draw into, begun.</param>
    /// <param name="context">The screen's layout.</param>
    public static void DrawBackdrop(SpriteBatch batch, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        UiPainter painter = UiPainter.Create(batch, context);
        painter.FillRect(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y), Scrim);

        // The grid is the screen's own, from its middle outwards, so its lines are the lines an item falls to there.
        UiRect safe = context.SafeRect;
        float step = context.Px(100f);
        float line = MathF.Max(1f, context.Px(1f));
        UiVec2 middle = safe.Center;
        for (float x = middle.X % step; x < context.Viewport.X; x += step)
        {
            painter.FillRect(new UiRect(x - (line * 0.5f), 0f, line, context.Viewport.Y), GridLine);
        }

        for (float y = middle.Y % step; y < context.Viewport.Y; y += step)
        {
            painter.FillRect(new UiRect(0f, y - (line * 0.5f), context.Viewport.X, line), GridLine);
        }
    }

    /// <summary>Draws what lies over the controls: the items' outlines, the guides, the bar and the question on leaving.</summary>
    /// <param name="batch">The batch to draw into, begun.</param>
    /// <param name="context">The screen's layout.</param>
    /// <param name="editor">The editor, read this frame.</param>
    /// <param name="theme">The title's theme, for its accent, or null.</param>
    /// <param name="font">The face the bar is written in, or null to leave its words out.</param>
    public static void DrawOverlay(SpriteBatch batch, LayoutContext context, TouchLayoutEditor editor, HudTheme? theme, UiFont? font)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(editor);
        if (!editor.IsOpen)
        {
            return;
        }

        UiPainter painter = UiPainter.Create(batch, context);
        Bgra32 accent = theme?.Palette.Accent ?? DefaultAccent;
        float u = context.Px(1f);
        float thin = MathF.Max(1f, 2f * u);
        float thick = MathF.Max(2f, 4f * u);

        foreach (TouchEditorItem item in editor.Items)
        {
            bool selected = editor.Selected == item.Item;
            if (selected)
            {
                continue;
            }

            if (item.Overlapped)
            {
                DrawOutline(painter, item, thin * 1.5f, Clash);
            }
            else if (item.Item.IsReadout)
            {
                // A readout has no rim of its own to say it can be taken hold of.
                DrawCorners(painter, item.Box, 14f * u, thin, item.Hidden ? InkFaint : Outline);
                DrawEmptyName(painter, item, font, u);
            }
            else if (item.Hidden)
            {
                DrawOutline(painter, item, thin, InkFaint);
            }
        }

        // The guides a dragged item has caught on, across the whole screen.
        if (editor.GuideX is { } guideX)
        {
            painter.FillRect(new UiRect(guideX - (thin * 0.5f), 0f, thin, context.Viewport.Y), accent);
        }

        if (editor.GuideY is { } guideY)
        {
            painter.FillRect(new UiRect(0f, guideY - (thin * 0.5f), context.Viewport.X, thin), accent);
        }

        foreach (TouchEditorItem item in editor.Items)
        {
            if (editor.Selected != item.Item)
            {
                continue;
            }

            DrawOutline(painter, item, thick, item.Overlapped ? Clash : accent);
            if (!item.Round)
            {
                DrawCorners(painter, item.Box.Inflate(new Thickness(6f * u)), 18f * u, thick, accent);
            }

            if (font is not null && !editor.Dragging)
            {
                // Its name over it, or under where it stands at the screen's top.
                float size = 24f * u;
                UiVec2 extent = font.Measure(item.Item.Name, size);
                float y = item.Box.Y - extent.Y - (12f * u);
                if (y < context.SafeRect.Y)
                {
                    y = item.Box.Bottom + (10f * u);
                }

                float x = Math.Clamp(item.Box.Center.X - (extent.X * 0.5f), context.SafeRect.X + (6f * u), context.SafeRect.Right - extent.X - (6f * u));
                painter.FillRect(new UiRect(x - (8f * u), y - (4f * u), extent.X + (16f * u), extent.Y + (8f * u)), Glass);
                painter.DrawText(font, item.Item.Name, new UiVec2(x, y), size, Ink);
            }
        }

        DrawBar(painter, context, editor, accent, font, u, thin);
        if (editor.Prompting)
        {
            DrawPrompt(painter, context, editor, accent, font, u, thin);
        }
    }

    private static void DrawOutline(UiPainter painter, in TouchEditorItem item, float thickness, Bgra32 colour)
    {
        if (item.Round)
        {
            float radius = item.Box.Width * 0.5f;
            painter.FillArc(item.Box.Center, radius, radius + thickness, 0f, MathF.Tau, colour);
        }
        else
        {
            painter.StrokeRect(item.Box, thickness, colour);
        }
    }

    // A readout with nothing to show now is an empty box: its name says what will stand there.
    private static void DrawEmptyName(UiPainter painter, in TouchEditorItem item, UiFont? font, float u)
    {
        if (!item.Empty || font is null)
        {
            return;
        }

        float size = MathF.Min(18f * u, item.Box.Height * 0.7f);
        string name = item.Item.Name.ToUpperInvariant();
        UiVec2 extent = font.Measure(name, size);
        painter.FillRect(item.Box, Button);
        painter.DrawText(font, name, new UiVec2(item.Box.Center.X - (extent.X * 0.5f), item.Box.Center.Y - (extent.Y * 0.5f)), size, InkFaint);
    }

    // A box said by its four corners alone, so what is inside it still reads.
    private static void DrawCorners(UiPainter painter, UiRect box, float length, float thickness, Bgra32 colour)
    {
        length = MathF.Min(length, MathF.Min(box.Width, box.Height) * 0.5f);
        painter.FillRect(new UiRect(box.X, box.Y, length, thickness), colour);
        painter.FillRect(new UiRect(box.X, box.Y, thickness, length), colour);
        painter.FillRect(new UiRect(box.Right - length, box.Y, length, thickness), colour);
        painter.FillRect(new UiRect(box.Right - thickness, box.Y, thickness, length), colour);
        painter.FillRect(new UiRect(box.X, box.Bottom - thickness, length, thickness), colour);
        painter.FillRect(new UiRect(box.X, box.Bottom - length, thickness, length), colour);
        painter.FillRect(new UiRect(box.Right - length, box.Bottom - thickness, length, thickness), colour);
        painter.FillRect(new UiRect(box.Right - thickness, box.Bottom - length, thickness, length), colour);
    }

    private static void Panel(UiPainter painter, UiRect box, float u, float thin)
    {
        float cut = 16f * u;
        painter.FillCutRect(box, cut, UiCorners.TopLeft | UiCorners.BottomRight, Glass);
        painter.StrokeCutRect(box, cut, UiCorners.TopLeft | UiCorners.BottomRight, thin, Rim);
    }

    private static void DrawBar(UiPainter painter, LayoutContext context, TouchLayoutEditor editor, Bgra32 accent, UiFont? font, float u, float thin)
    {
        Panel(painter, editor.BarBox, u, thin);
        if (!editor.SelectionBox.IsEmpty)
        {
            Panel(painter, editor.SelectionBox, u, thin);
            if (font is not null && editor.Selected is { } selected)
            {
                // The selected item's name at the row's left, in the room the row leaves for it.
                float size = 26f * u;
                float room = FirstSecondRowX(editor) - editor.SelectionBox.X - (30f * u);
                string name = Fit(font, selected.Name.ToUpperInvariant(), size, room);
                UiVec2 extent = font.Measure(name, size);
                painter.DrawText(font, name, new UiVec2(editor.SelectionBox.X + (22f * u), editor.SelectionBox.Center.Y - (extent.Y * 0.5f)), size, accent);
            }
        }
        else if (font is not null && !editor.Prompting && !editor.Adding)
        {
            // With nothing selected, what there is to do, on the side of the bar that faces the screen's middle.
            const string Hint = "Drag anything to move it. Tap it for its size, opacity and visibility. Pinch to resize. ADD puts another button on the screen.";
            float size = 22f * u;
            UiVec2 extent = font.Measure(Hint, size);
            float y = editor.BarAtBottom ? editor.BarBox.Y - extent.Y - (14f * u) : editor.BarBox.Bottom + (14f * u);
            float x = context.SafeRect.Center.X - (extent.X * 0.5f);
            painter.FillRect(new UiRect(x - (12f * u), y - (6f * u), extent.X + (24f * u), extent.Y + (12f * u)), Glass);
            painter.DrawText(font, Hint, new UiVec2(x, y), size, Ink);
        }

        foreach (TouchEditorControl control in editor.Bar)
        {
            if (!control.IsPrompt && control.Widget != TouchEditorWidget.PaletteItem)
            {
                DrawWidget(painter, editor, control, accent, font, u, thin);
            }
        }

        if (editor.Adding)
        {
            DrawPalette(painter, context, editor, font, u, thin);
        }
    }

    // The list of buttons to add: each as it is drawn in play, with its name under it.
    private static void DrawPalette(UiPainter painter, LayoutContext context, TouchLayoutEditor editor, UiFont? font, float u, float thin)
    {
        painter.FillRect(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y), Scrim);
        Panel(painter, editor.PaletteBox, u, thin);
        if (font is not null)
        {
            painter.DrawText(font, "ADD A BUTTON", new UiVec2(editor.PaletteBox.X + (24f * u), editor.PaletteBox.Y + (18f * u)), 28f * u, Ink);
        }

        float stroke = MathF.Max(1.5f, 4f * u);
        foreach (TouchEditorControl control in editor.Bar)
        {
            if (control.Widget != TouchEditorWidget.PaletteItem || TouchLayout.ButtonOf(control.Kind) is not { } button)
            {
                continue;
            }

            float radius = 40f * u;
            UiVec2 centre = new(control.Box.Center.X, control.Box.Y + (48f * u));
            painter.FillArc(centre, 0f, radius, 0f, MathF.Tau, Button);
            painter.FillArc(centre, radius - thin, radius, 0f, MathF.Tau, Rim);
            TouchControlsRenderer.DrawGlyph(painter, button, centre, radius * 0.46f, stroke, Ink);
            if (font is not null)
            {
                float size = 19f * u;
                string name = Fit(font, TouchLayout.Name(control.Kind), size, control.Box.Width - (6f * u));
                UiVec2 extent = font.Measure(name, size);
                painter.DrawText(font, name, new UiVec2(control.Box.Center.X - (extent.X * 0.5f), control.Box.Y + (98f * u)), size, Ink);
            }
        }
    }

    // Where the second row's widgets start, for the name to stop short of.
    private static float FirstSecondRowX(TouchLayoutEditor editor)
    {
        foreach (TouchEditorControl control in editor.Bar)
        {
            if (control.Widget == TouchEditorWidget.Size)
            {
                return control.Box.X;
            }
        }

        return editor.SelectionBox.Right;
    }

    private static string Fit(UiFont font, string text, float size, float room)
    {
        while (text.Length > 3 && font.Measure(text, size).X > room)
        {
            text = text[..^1];
        }

        return text;
    }

    private static void DrawPrompt(UiPainter painter, LayoutContext context, TouchLayoutEditor editor, Bgra32 accent, UiFont? font, float u, float thin)
    {
        painter.FillRect(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y), Scrim);
        Panel(painter, editor.PromptBox, u, thin);
        if (font is not null)
        {
            const string Question = "SAVE YOUR CHANGES?";
            float size = 34f * u;
            UiVec2 extent = font.Measure(Question, size);
            painter.DrawText(font, Question, new UiVec2(editor.PromptBox.Center.X - (extent.X * 0.5f), editor.PromptBox.Y + (44f * u)), size, Ink);
        }

        foreach (TouchEditorControl control in editor.Bar)
        {
            if (control.IsPrompt)
            {
                DrawWidget(painter, editor, control, accent, font, u, thin);
            }
        }
    }

    private static void DrawWidget(UiPainter painter, TouchLayoutEditor editor, TouchEditorControl control, Bgra32 accent, UiFont? font, float u, float thin)
    {
        UiRect box = control.Box;
        Bgra32 ink = control.Enabled ? Ink : InkFaint;
        if (control.Widget is TouchEditorWidget.Size or TouchEditorWidget.Opacity)
        {
            DrawSlider(painter, control, accent, font, u, thin);
            return;
        }

        // Lit, a button is the accent with dark words on it; else glass with a thin rim.
        bool lit = control.Active && control.Enabled && control.Widget is not TouchEditorWidget.Show;
        painter.FillRect(box, lit ? accent : Button);
        painter.StrokeRect(box, thin, lit ? accent : Rim);
        if (lit)
        {
            ink = Dark;
        }

        UiVec2 c = box.Center;
        float glyph = box.Height * 0.22f;
        float stroke = MathF.Max(1.5f, 4f * u);
        switch (control.Widget)
        {
            case TouchEditorWidget.Slot1 or TouchEditorWidget.Slot2 or TouchEditorWidget.Slot3:
                Words(painter, font, ((control.Widget - TouchEditorWidget.Slot1) + 1).ToString(CultureInfo.InvariantCulture), box, 34f * u, ink);
                break;
            case TouchEditorWidget.Add:
                Words(painter, font, "+ ADD", box, 24f * u, ink);
                break;
            case TouchEditorWidget.Undo:
                KitIcons.Draw(painter, KitIcon.Undo, c, box.Height * 0.42f, ink);
                break;
            case TouchEditorWidget.ResetAll:
                Words(painter, font, "RESET ALL", box, 22f * u, ink);
                break;
            case TouchEditorWidget.StickMode:
                Words(painter, font, editor.Working.StickMode == TouchStickMode.Fixed ? "STICK: FIXED" : "STICK: FLOATING", box, 22f * u, ink);
                break;
            case TouchEditorWidget.Flip:
                // To the screen's other edge: an arrow the way the bar will go.
                painter.DrawChevron(c, glyph, editor.BarAtBottom ? UiDirection.Up : UiDirection.Down, stroke, ink);
                painter.FillRect(new UiRect(c.X - glyph, editor.BarAtBottom ? c.Y - (glyph * 1.5f) : c.Y + (glyph * 1.5f) - stroke, glyph * 2f, stroke), ink);
                break;
            case TouchEditorWidget.Save:
                Words(painter, font, "SAVE", box, 24f * u, ink);
                break;
            case TouchEditorWidget.Exit:
                Words(painter, font, "EXIT", box, 24f * u, ink);
                break;
            case TouchEditorWidget.Show:
                // An eye, open while the item is on the screen and struck through while it is not.
                Bgra32 eye = control.Enabled ? (control.Active ? accent : Ink) : InkFaint;
                painter.FillArc(c, (glyph * 1.3f) - (stroke * 0.5f), (glyph * 1.3f) + (stroke * 0.5f), 0f, MathF.Tau, eye);
                painter.FillDot(c, glyph * 0.5f, eye);
                if (!control.Active)
                {
                    painter.StrokeLine(new UiVec2(c.X - (glyph * 1.6f), c.Y + (glyph * 1.6f)), new UiVec2(c.X + (glyph * 1.6f), c.Y - (glyph * 1.6f)), stroke, eye);
                }

                break;
            case TouchEditorWidget.ResetItem:
                Words(painter, font, editor.Selected is { IsExtra: true } ? "REMOVE" : "RESET", box, 22f * u, ink);
                break;
            case TouchEditorWidget.PromptSave:
                Words(painter, font, "SAVE", box, 24f * u, ink);
                break;
            case TouchEditorWidget.PromptDiscard:
                Words(painter, font, "DISCARD", box, 24f * u, ink);
                break;
            case TouchEditorWidget.PromptKeep:
                Words(painter, font, "KEEP EDITING", box, 24f * u, ink);
                break;
        }
    }

    private static void DrawSlider(UiPainter painter, TouchEditorControl control, Bgra32 accent, UiFont? font, float u, float thin)
    {
        UiRect box = control.Box;
        painter.FillRect(box, Button);
        painter.StrokeRect(box, thin, Rim);

        // The words above, the track below: what it is and what it stands at, in whole percent.
        bool size = control.Widget == TouchEditorWidget.Size;
        float value = Math.Clamp(control.Value, 0f, 1f);
        float percent = size
            ? (TouchLayout.MinScale + (value * (TouchLayout.MaxScale - TouchLayout.MinScale))) * 100f
            : (TouchLayout.MinOpacity + (value * (1f - TouchLayout.MinOpacity))) * 100f;
        if (font is not null)
        {
            string text = string.Create(CultureInfo.InvariantCulture, $"{(size ? "SIZE" : "OPACITY")}  {MathF.Round(percent):0}%");
            float textSize = 19f * u;
            UiVec2 extent = font.Measure(text, textSize);
            painter.DrawText(font, text, new UiVec2(box.Center.X - (extent.X * 0.5f), box.Y + (9f * u)), textSize, Ink);
        }

        (float from, float to) = TouchLayoutEditor.SliderTrack(box);
        float y = box.Bottom - (box.Height * 0.3f);
        float track = MathF.Max(2f, 5f * u);
        float knobX = from + ((to - from) * value);
        painter.FillRect(new UiRect(from, y - (track * 0.5f), to - from, track), Rim);
        painter.FillRect(new UiRect(from, y - (track * 0.5f), knobX - from, track), accent);
        painter.FillDot(new UiVec2(knobX, y), 13f * u, Ink);
    }

    private static void Words(UiPainter painter, UiFont? font, string text, UiRect box, float size, Bgra32 ink)
    {
        if (font is null)
        {
            return;
        }

        UiVec2 extent = font.Measure(text, size);
        painter.DrawText(font, text, new UiVec2(box.Center.X - (extent.X * 0.5f), box.Center.Y - (extent.Y * 0.5f)), size, ink);
    }
}

using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>The weapons carried, column by column from the anchor rightwards, on glass.</summary>
/// <remarks>
/// Each column heads with its key on a small chip, the weapon's picture and the rounds carried for it; under the head go the
/// names of every weapon in the column. The weapon in hand is lit: its name in the highlight colour with a bar beside it, and its
/// column's glass a shade brighter.
/// </remarks>
public static class HudInventory
{
    /// <summary>Draws the strip.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void Draw(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Inventory) == 0 || model.Slots.Count == 0 || context.Place(HudElement.Inventory) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float columnWidth = context.Px(96f);
        float headHeight = context.Px(58f);
        float nameHeight = context.Px(19f);
        float gap = context.Px(4f);
        float opacity = context.Reactive.Appear;

        // How many columns, and how deep the deepest.
        int columns = 0;
        int lastColumn = int.MinValue;
        int deepest = 0;
        int depth = 0;
        foreach (HudSlotView slot in model.Slots)
        {
            if (slot.Column != lastColumn)
            {
                columns++;
                lastColumn = slot.Column;
                depth = 0;
            }

            deepest = Math.Max(deepest, ++depth);
        }

        float width = (columns * columnWidth) + ((columns - 1) * gap) + context.Px(12f);
        float height = context.Px(8f) + headHeight + (deepest * nameHeight) + context.Px(8f);
        HudPanel.Draw(context, new UiRect(anchor.X, anchor.Y, width, height), opacity);

        float x = anchor.X + context.Px(6f) - columnWidth - gap;
        float y = 0f;
        lastColumn = int.MinValue;
        foreach (HudSlotView slot in model.Slots)
        {
            if (slot.Column != lastColumn)
            {
                lastColumn = slot.Column;
                x += columnWidth + gap;
                y = anchor.Y + context.Px(8f);
            }

            if (slot.Head)
            {
                DrawHead(context, slot, new UiRect(x, y, columnWidth, headHeight), opacity);
                y += headHeight;
            }

            Bgra32 colour = slot.Selected ? palette.Highlight : palette.Primary;
            if (slot.Selected)
            {
                context.Painter.FillRect(new UiRect(x, y + context.Px(2f), MathF.Max(1f, context.Px(2f)), nameHeight - context.Px(4f)), HudDrawContext.Fade(palette.Highlight, opacity));
            }

            context.Painter.DrawLabel(context.Fonts.Name, slot.Name, new UiVec2(x + context.Px(7f), y + context.Px(1f)), context.Px(15f), HudDrawContext.Fade(colour, (slot.Selected ? 1f : 0.75f) * opacity), 0.04f);
            y += nameHeight;
        }
    }

    // A column's head: its key on a chip, its weapon's picture and its count.
    private static void DrawHead(HudDrawContext context, HudSlotView slot, UiRect rect, float opacity)
    {
        HudPalette palette = context.Palette;
        if (slot.Selected)
        {
            context.Painter.FillRect(rect, HudDrawContext.Fade(palette.Primary, 0.12f * opacity));
            HudPanel.Brackets(context, rect, context.Px(6f), HudDrawContext.Fade(palette.Highlight, opacity));
        }

        if (slot.KeyLabel.Length > 0)
        {
            UiRect chip = new(rect.X + context.Px(3f), rect.Y + context.Px(3f), context.Px(15f), context.Px(15f));
            context.Painter.FillRect(chip, HudDrawContext.Fade(slot.Selected ? palette.Highlight : palette.Dim, opacity));
            context.Painter.DrawLabel(context.Fonts.Value, slot.KeyLabel, new UiVec2(chip.X + context.Px(4f), chip.Y + context.Px(0.5f)), context.Px(14f), HudDrawContext.Fade(palette.Glass.WithAlpha(255), opacity), 0f);
        }

        if (slot.Icon.IsSome && context.Icons.TryMeasure(slot.Icon, out float w, out float h))
        {
            float s = MathF.Min((rect.Width - context.Px(10f)) / MathF.Max(1f, w), context.Px(30f) / MathF.Max(1f, h));
            context.DrawIcon(slot.Icon, rect.X + ((rect.Width - (w * s)) * 0.5f), rect.Y + context.Px(18f) + ((context.Px(30f) - (h * s)) * 0.5f), s, HudDrawContext.Fade(Bgra32.White, opacity));
        }

        if (slot.Count.Length > 0)
        {
            float size = context.Px(15f);
            float cw = UiPainter.MeasureLabel(context.Fonts.Value, slot.Count, size, 0.02f);
            context.Painter.DrawLabel(context.Fonts.Value, slot.Count, new UiVec2(rect.Right - cw - context.Px(3f), rect.Bottom - size - context.Px(1f)), size, HudDrawContext.Fade(palette.Primary, opacity), 0.02f);
        }
    }
}

/// <summary>What may be done, as a row of badges from the anchor rightwards, the first framed and pulsing.</summary>
public static class HudActions
{
    /// <summary>The space each badge takes across, in design pixels.</summary>
    public const float Spacing = 64f;

    /// <summary>Draws the row.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void Draw(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Actions) == 0 || model.Actions.Count == 0 || context.Place(HudElement.Actions) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float x = anchor.X;
        for (int i = 0; i < model.Actions.Count; i++)
        {
            HudActionView action = model.Actions[i];
            float appear = i == 0 ? context.Reactive.ActionAppear : 1f;
            float tall = Badge(context, action.Icon, HudIcon.None, x, anchor.Y, appear);
            if (action.Second.IsSome)
            {
                float below = tall > 0f ? tall + context.Px(10f) : 0f;
                tall = below + Badge(context, action.Second, action.SecondOverlay, x, anchor.Y + below, appear);
            }

            if (i == 0 && tall > 0f)
            {
                // The one the key takes: a frame that breathes, and brackets that close in as it appears.
                float pulse = 0.55f + (0.45f * MathF.Sin(context.Seconds * 5.5f));
                UiRect frame = new(x - context.Px(34f), anchor.Y - context.Px(4f), context.Px(68f), tall + context.Px(8f));
                context.Painter.FillRect(frame, HudDrawContext.Fade(palette.Primary, 0.14f * pulse * appear));
                float close = context.Px(10f) * (1f - appear);
                HudPanel.Brackets(
                    context,
                    new UiRect(frame.X - close, frame.Y - close, frame.Width + (close * 2f), frame.Height + (close * 2f)),
                    context.Px(8f),
                    HudDrawContext.Fade(palette.Highlight, appear));
            }

            x += context.Px(Spacing);
        }
    }

    // A badge centred across on a place, with an overlay under it; its height, or nought.
    private static float Badge(HudDrawContext context, HudIcon icon, HudIcon overlay, float x, float y, float opacity)
    {
        if (!icon.IsSome || !context.Icons.TryMeasure(icon, out float w, out float h))
        {
            return 0f;
        }

        float s = context.DesignScale;
        float left = x - (w * s * 0.5f);
        if (overlay.IsSome)
        {
            context.DrawIcon(overlay, left, y, s, HudDrawContext.Fade(Bgra32.White, opacity));
        }

        context.DrawIcon(icon, left, y, s, HudDrawContext.Fade(Bgra32.White, opacity));
        return h * s;
    }
}

/// <summary>The timed action's bar, the mission's clock, the messages and the aiming mark.</summary>
public static class HudReadouts
{
    /// <summary>Draws the timed action's bar: a glass track, filling, with a bright head and ticks every tenth.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void DrawProgress(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Progress) == 0 || model.Progress is not { } progress || context.Place(HudElement.Progress) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float width = context.Px(360f);
        float height = context.Px(10f);
        UiRect track = new(anchor.X - (width * 0.5f), anchor.Y, width, height);
        HudPanel.Draw(context, new UiRect(track.X - context.Px(6f), track.Y - context.Px(6f), track.Width + context.Px(12f), track.Height + context.Px(12f)));
        float p = Math.Clamp(progress, 0f, 1f);
        context.Painter.FillRect(new UiRect(track.X, track.Y, track.Width * p, track.Height), palette.Primary);
        context.Painter.FillRect(new UiRect(track.X + (track.Width * p) - context.Px(2f), track.Y - context.Px(3f), context.Px(3f), track.Height + context.Px(6f)), palette.Highlight);
        for (int i = 1; i < 10; i++)
        {
            context.Painter.FillRect(new UiRect(track.X + (track.Width * i / 10f), track.Bottom + context.Px(2f), MathF.Max(1f, context.Px(1f)), context.Px(3f)), palette.Dim);
        }
    }

    /// <summary>Draws the mission's clock: minutes and seconds in the accent colour, blinking its colon.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void DrawTimer(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Timer) == 0 || model.TimerSeconds is not { } seconds || context.Place(HudElement.Timer) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        int s = Math.Max(0, seconds);
        bool colon = (context.Seconds % 1f) < 0.6f;
        string text = string.Create(CultureInfo.InvariantCulture, $"{s / 60:00}{(colon ? ':' : ' ')}{s % 60:00}");
        float size = context.Px(22f);
        float width = UiPainter.MeasureLabel(context.Fonts.Value, text, size, 0.06f);
        UiRect panel = new(anchor.X - (width * 0.5f) - context.Px(12f), anchor.Y, width + context.Px(24f), size + context.Px(10f));
        HudPanel.Draw(context, panel, context.Reactive.Appear);
        Bgra32 colour = s <= 30 ? palette.Danger : palette.Accent;
        context.Painter.DrawLabel(context.Fonts.Value, text, new UiVec2(panel.X + context.Px(12f), panel.Y + context.Px(4f)), size, colour, 0.06f);
    }

    /// <summary>
    /// Draws the messages, each on its own strip of glass with a bar at its left, fading as they fade: from the anchor rightwards,
    /// or centred on it where the theme centres them.
    /// </summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void DrawMessages(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Messages) == 0 || model.Messages.Count == 0 || context.Place(HudElement.Messages) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float size = context.Px(14f);
        float y = anchor.Y;
        bool centred = context.Theme.Anchors.TryGetValue(HudElement.Messages, out HudAnchor placed) && placed.EdgeX == HudEdgeX.Centre;
        foreach (HudMessageView message in model.Messages)
        {
            if (message.Text.Length == 0 || message.Opacity <= 0f)
            {
                continue;
            }

            float width = UiPainter.MeasureLabel(context.Fonts.Body, message.Text, size, 0.01f);
            float stripWidth = width + context.Px(28f);
            UiRect strip = new(centred ? anchor.X - (stripWidth * 0.5f) : anchor.X, y, stripWidth, size + context.Px(12f));
            HudPanel.Draw(context, strip, message.Opacity);
            context.Painter.FillRect(new UiRect(strip.X, strip.Y, MathF.Max(2f, context.Px(3f)), strip.Height), HudDrawContext.Fade(palette.Primary, message.Opacity));
            context.Painter.DrawLabel(context.Fonts.Body, message.Text, new UiVec2(strip.X + context.Px(14f), strip.Y + context.Px(5f)), size, HudDrawContext.Fade(palette.Highlight, message.Opacity), 0.01f);
            y += strip.Height + context.Px(6f);
        }
    }

    /// <summary>Draws the aiming mark: four short strokes round a centre dot, opened by how far the shot will wander.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    /// <param name="spread">How far the strokes stand from the centre, in design pixels.</param>
    public static void DrawCrosshair(HudDrawContext context, HudModel model, float spread)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Crosshair) == 0)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float cx = context.Width * 0.5f;
        float cy = context.Height * 0.5f;
        float gap = context.Px(spread);
        float length = context.Px(7f);
        float t = MathF.Max(1f, context.Px(1.5f));
        Bgra32 colour = HudDrawContext.Fade(palette.Primary, 0.9f);
        context.Painter.FillRect(new UiRect(cx - gap - length, cy - (t * 0.5f), length, t), colour);
        context.Painter.FillRect(new UiRect(cx + gap, cy - (t * 0.5f), length, t), colour);
        context.Painter.FillRect(new UiRect(cx - (t * 0.5f), cy - gap - length, t, length), colour);
        context.Painter.FillRect(new UiRect(cx - (t * 0.5f), cy + gap, t, length), colour);
        context.Painter.FillRect(new UiRect(cx - t, cy - t, t * 2f, t * 2f), HudDrawContext.Fade(palette.Highlight, 0.9f));
    }
}

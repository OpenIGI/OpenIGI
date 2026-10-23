using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The weapon in hand at the lower right, where both games keep it: its picture, its name and fire mode over a rule, and its
/// rounds, all on one pane of glass.
/// </summary>
/// <remarks>
/// The rounds loaded are a large figure that rolls to each new count as an odometer does, the rounds carried beside it smaller;
/// under them the magazine is a row of pips, lit for each round still in it. A change of weapon slides the block in from the
/// right and sweeps its rule across; a reload runs an amber bar along the rule.
/// </remarks>
public static class HudWeapon
{
    /// <summary>The most pips the magazine row shows; a larger magazine is shown as a bar.</summary>
    public const int MaximumPips = 40;

    /// <summary>Draws the block, standing on its anchor's corner.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void Draw(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Weapon) == 0 || model.Weapon is not { } weapon || context.Place(HudElement.Weapon) is not { } anchor)
        {
            return;
        }

        HudPalette palette = context.Palette;
        float slide = Easing.Evaluate(Ease.OutCubic, context.Reactive.Switch);
        float opacity = context.Reactive.Appear * slide;
        float offset = context.Px(48f) * (1f - slide);

        float width = context.Px(232f);
        float pad = context.Px(10f);
        bool counted = weapon.Magazine is not null || weapon.Reserve is not null;
        float height = pad + context.Px(44f) + context.Px(26f) + (counted ? context.Px(44f) + context.Px(12f) : 0f) + pad;
        UiRect panel = new(anchor.X - width + offset, anchor.Y - height, width, height);
        HudPanel.Draw(context, panel, opacity);

        float right = panel.Right - pad;
        float y = panel.Y + pad;

        // The picture, at the block's right, as large as fits.
        if (weapon.Icon.IsSome && context.Icons.TryMeasure(weapon.Icon, out float w, out float h))
        {
            float s = MathF.Min(context.Px(150f) / MathF.Max(1f, w), context.Px(40f) / MathF.Max(1f, h));
            context.DrawIcon(weapon.Icon, right - (w * s), y + ((context.Px(40f) - (h * s)) * 0.5f), s, HudDrawContext.Fade(Bgra32.White, opacity));
        }

        y += context.Px(44f);

        // The name, and the fire mode after it in the quieter colour.
        float nameSize = context.Px(22f);
        string name = weapon.Name.ToUpperInvariant();
        float nameWidth = UiPainter.MeasureLabel(context.Fonts.Name, name, nameSize, 0.04f);
        string mode = weapon.FireMode.ToUpperInvariant();
        float modeSize = context.Px(13f);
        float modeWidth = mode.Length > 0 ? UiPainter.MeasureLabel(context.Fonts.Value, mode, modeSize, 0.12f) + context.Px(10f) : 0f;
        context.Painter.DrawLabel(context.Fonts.Name, name, new UiVec2(right - modeWidth - nameWidth, y), nameSize, HudDrawContext.Fade(palette.Highlight, opacity), 0.04f);
        if (mode.Length > 0)
        {
            context.Painter.DrawLabel(context.Fonts.Value, mode, new UiVec2(right - modeWidth + context.Px(10f), y + context.Px(6f)), modeSize, HudDrawContext.Fade(palette.Primary, 0.8f * opacity), 0.12f);
        }

        // The rule under it, swept across from the right when the weapon changes; a reload runs along it in amber.
        float ruleY = y + context.Px(24f);
        float ruleWidth = (panel.Width - (pad * 2f)) * slide;
        HudPanel.Rule(context, right - ruleWidth, ruleY, ruleWidth, HudDrawContext.Fade(palette.Primary, 0.75f * opacity));
        if (weapon.State == HudWeaponState.Reloading)
        {
            float run = (context.Seconds * 1.6f) % 1f;
            context.Painter.FillRect(new UiRect(panel.X + pad + ((ruleWidth - context.Px(30f)) * run), ruleY - context.Px(1f), context.Px(30f), MathF.Max(2f, context.Px(2.5f))), HudDrawContext.Fade(palette.Accent, opacity));
        }

        if (!counted)
        {
            return;
        }

        y = ruleY + context.Px(8f);
        DrawRounds(context, weapon, right, y, opacity);
        DrawPips(context, weapon, new UiRect(panel.X + pad, y + context.Px(46f), panel.Width - (pad * 2f), context.Px(6f)), opacity);
    }

    // The loaded rounds, rolling, and the carried rounds beside them; the second weapon's to the left.
    private static void DrawRounds(HudDrawContext context, HudWeaponView weapon, float right, float y, float opacity)
    {
        HudPalette palette = context.Palette;
        float big = context.Px(38f);
        float small = context.Px(16f);
        float x = right;
        if (weapon.Reserve is { } reserve)
        {
            string text = reserve.ToString(CultureInfo.InvariantCulture);
            float rw = UiPainter.MeasureLabel(context.Fonts.Value, text, small, 0.02f);
            x -= rw;
            context.Painter.DrawLabel(context.Fonts.Value, text, new UiVec2(x, y + big - small - context.Px(4f)), small, HudDrawContext.Fade(palette.Primary, 0.8f * opacity), 0.02f);
            context.Painter.DrawLabel(context.Fonts.Label, "/", new UiVec2(x - context.Px(10f), y + big - small - context.Px(2f)), small * 0.8f, HudDrawContext.Fade(palette.Dim, opacity), 0f);
            x -= context.Px(16f);
        }

        if (weapon.Magazine is { } loaded)
        {
            Bgra32 colour = loaded == 0 ? palette.Danger
                : weapon.Capacity > 0 && loaded <= Math.Max(1, weapon.Capacity / 5) ? palette.Accent
                : palette.Highlight;
            x = Odometer(context, context.Reactive.Rounds, right: x, y, big, HudDrawContext.Fade(colour, opacity));
        }

        // Low or spent: not the colour alone -- the word beside the count.
        if (weapon.Magazine is { } count && (count == 0 || HudReactiveState.LowRounds(weapon)))
        {
            string warning = count == 0 ? "RELOAD" : "LOW";
            Bgra32 warn = count == 0 ? palette.Danger : palette.Accent;
            float blink = count == 0 ? 0.6f + (0.4f * MathF.Sin(context.Seconds * 7f)) : 1f;
            float ww = UiPainter.MeasureLabel(context.Fonts.Value, warning, small, 0.1f);
            context.Painter.DrawLabel(context.Fonts.Value, warning, new UiVec2(x - context.Px(12f) - ww, y + context.Px(2f)), small, HudDrawContext.Fade(warn, blink * opacity), 0.1f);
        }

        if (weapon.Secondary is { } secondary)
        {
            string text = secondary.ToString(CultureInfo.InvariantCulture);
            float sw = UiPainter.MeasureLabel(context.Fonts.Value, text, small, 0.02f);
            context.Painter.DrawLabel(context.Fonts.Value, text, new UiVec2(x - context.Px(18f) - sw, y + big - small - context.Px(4f)), small, HudDrawContext.Fade(palette.Accent, opacity), 0.02f);
        }
    }

    /// <summary>
    /// Draws a count as an odometer: the ones turning with the count's fraction, and each higher wheel turning with the one below
    /// it as that one passes nine.
    /// </summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="value">The count, rolling.</param>
    /// <param name="right">The right edge, in pixels.</param>
    /// <param name="y">The top, in pixels.</param>
    /// <param name="size">The figures' size, in pixels.</param>
    /// <param name="colour">Their colour.</param>
    /// <returns>The left edge drawn to.</returns>
    public static float Odometer(HudDrawContext context, float value, float right, float y, float size, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(context);
        float v = MathF.Max(0f, value);
        int whole = (int)MathF.Floor(v + 0.0001f);
        float turn = v - whole;
        int digits = Math.Max(1, whole.ToString(CultureInfo.InvariantCulture).Length);
        float digitWidth = UiPainter.MeasureLabel(context.Fonts.Value, "0", size, 0.02f);
        float x = right - (digitWidth * digits);
        context.Painter.PushClip(new UiRect(x, y, digitWidth * digits, size * 1.1f));
        int place = 1;
        for (int i = digits - 1; i >= 0; i--)
        {
            int digit = (whole / place) % 10;

            // A wheel turns while every wheel below it reads nine.
            bool carrying = place == 1 || (whole % place) == place - 1;
            float roll = carrying ? turn : 0f;
            float dx = x + (i * digitWidth);
            float lift = roll * size;
            context.Painter.DrawLabel(context.Fonts.Value, Digit(digit), new UiVec2(dx, y - lift), size, colour, 0.02f);
            if (roll > 0f)
            {
                context.Painter.DrawLabel(context.Fonts.Value, Digit((digit + 1) % 10), new UiVec2(dx, y + size - lift), size, colour, 0.02f);
            }

            place *= 10;
        }

        context.Painter.PopClip();
        return x;
    }

    // The magazine: a pip for each round it holds, lit for each still in it; a bar for a large one.
    private static void DrawPips(HudDrawContext context, HudWeaponView weapon, UiRect rect, float opacity)
    {
        if (weapon.Capacity <= 0 || weapon.Magazine is not { } loaded)
        {
            return;
        }

        HudPalette palette = context.Palette;
        int capacity = weapon.Capacity;
        if (capacity > MaximumPips)
        {
            context.Painter.FillRect(rect, HudDrawContext.Fade(palette.Dim, 0.5f * opacity));
            context.Painter.FillRect(new UiRect(rect.Right - (rect.Width * loaded / capacity), rect.Y, rect.Width * loaded / capacity, rect.Height), HudDrawContext.Fade(palette.Primary, opacity));
            return;
        }

        float gap = MathF.Max(1f, context.Px(2f));
        float pip = MathF.Min(context.Px(weapon.Gauge == HudGauge.Shell ? 9f : 5f), (rect.Width - (gap * (capacity - 1))) / capacity);
        for (int i = 0; i < capacity; i++)
        {
            float x = rect.Right - ((i + 1) * pip) - (i * gap);
            bool lit = i < loaded;
            context.Painter.FillRect(new UiRect(x, rect.Y, pip, rect.Height), lit ? HudDrawContext.Fade(palette.Primary, opacity) : HudDrawContext.Fade(palette.Dim, 0.45f * opacity));
        }
    }

    private static string Digit(int digit) => digit switch
    {
        0 => "0", 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", 7 => "7", 8 => "8", _ => "9",
    };
}

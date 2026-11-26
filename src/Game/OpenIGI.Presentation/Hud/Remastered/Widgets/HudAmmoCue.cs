using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// A word under the crosshair when the weapon in hand runs low, where the eye already is: LOW AMMO, quiet, as the magazine
/// nears its end; RELOAD, pulsing amber, when it is empty with rounds to put in; NO AMMO, pulsing red, when there are none.
/// Nothing while it reloads -- the weapon's readout says that -- or while it is full enough. <b>Not retail.</b>
/// </summary>
public static class HudAmmoCue
{
    /// <summary>What the cue says of a weapon, or <see langword="null"/> for nothing.</summary>
    /// <param name="weapon">The weapon in hand.</param>
    /// <returns>The word and how urgent it is: 0 quiet, 1 to reload, 2 out.</returns>
    public static (string Word, int Urgency)? Of(HudWeaponView? weapon)
    {
        if (weapon is not { Magazine: { } loaded } held || held.State is HudWeaponState.Reloading or HudWeaponState.Switching)
        {
            return null;
        }

        if (loaded <= 0)
        {
            return held.Reserve is > 0 or null ? ("RELOAD", 1) : ("NO AMMO", 2);
        }

        return HudReactiveState.LowRounds(weapon) ? ("LOW AMMO", 0) : null;
    }

    /// <summary>Draws the cue under the crosshair, when there is one.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void Draw(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if (!HudCVars.AmmoCue.Value || (model.Show & HudParts.Crosshair) == 0 || Of(model.Weapon) is not { } cue)
        {
            return;
        }

        // Urgent words pulse; the quiet one holds, dimmed.
        float pulse = cue.Urgency == 0 ? 0.7f : 0.55f + (0.45f * (0.5f + (0.5f * MathF.Sin(context.Seconds * MathF.PI * 2.4f))));
        Bgra32 colour = cue.Urgency switch
        {
            2 => context.Theme.Palette.Danger,
            1 => context.Theme.Palette.Accent,
            _ => context.Theme.Palette.Highlight,
        };

        float size = context.Px(13f);
        const float Tracking = 0.24f;
        float width = UiPainter.MeasureLabel(context.Fonts.Name, cue.Word, size, Tracking);
        var at = new UiVec2((context.Width * 0.5f) - (width * 0.5f), (context.Height * 0.5f) + context.Px(42f));
        Bgra32 ink = HudDrawContext.Fade(colour, pulse);
        float offset = MathF.Max(1f, context.Px(1f));
        context.Painter.DrawLabel(context.Fonts.Name, cue.Word, new UiVec2(at.X + offset, at.Y + offset), size, Bgra32.FromRgba(0, 0, 0, (byte)(210 * ink.A / 255)), Tracking);
        context.Painter.DrawLabel(context.Fonts.Name, cue.Word, at, size, ink, Tracking);
    }
}

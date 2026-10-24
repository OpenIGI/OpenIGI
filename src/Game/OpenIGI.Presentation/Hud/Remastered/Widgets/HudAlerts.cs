using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>What the display says where the eye already is: the magazine at the aim.</summary>
/// <remarks>
/// <para>
/// Who is noticing the player is not drawn here: the visor's rim lights on their side (<c>Ui/HudFx.slang</c>).
/// </para>
/// <para>
/// The magazine is a short arc of pips beside the aiming mark, where a shooter looks: it shows while the rounds change -- firing,
/// reloading -- and stays while they are low or spent, a reload sweeping it full.
/// </para>
/// </remarks>
public static class HudAlerts
{
    /// <summary>The most pips the arc at the aim shows; a larger magazine is shown as a share of them.</summary>
    public const int MaximumArcPips = 30;

    /// <summary>Draws the magazine beside the aiming mark, while its rounds change or are low.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    /// <param name="spread">How far the aiming mark stands open, in design pixels.</param>
    public static void DrawAmmoArc(HudDrawContext context, HudModel model, float spread)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Crosshair) == 0 || model.Weapon is not { Magazine: { } loaded, Capacity: > 0 } weapon)
        {
            return;
        }

        // Shown as the rounds change and for a while after, and for as long as they are low, spent or being reloaded.
        float show = context.Reactive.Presence(HudParts.Weapon, HudDensity.Minimal, model);
        if (show <= 0.01f)
        {
            return;
        }

        HudPalette palette = context.Palette;
        bool reloading = weapon.State == HudWeaponState.Reloading;
        bool low = HudReactiveState.LowRounds(weapon) || loaded == 0;
        int pips = Math.Min(weapon.Capacity, MaximumArcPips);
        int lit = weapon.Capacity <= MaximumArcPips ? loaded : (int)MathF.Ceiling((float)loaded * pips / weapon.Capacity);
        if (reloading)
        {
            // A reload sweeps the arc full and round again.
            lit = (int)((context.Seconds * 2.2f % 1f) * pips);
        }

        float cx = context.Width * 0.5f;
        float cy = context.Height * 0.5f;
        float radius = context.Px(spread + 22f);
        float size = MathF.Max(2f, context.Px(3f));
        const float From = -0.95f;
        const float To = 0.95f;
        Bgra32 on = reloading ? palette.Accent : low ? palette.Danger : palette.Primary;
        for (int i = 0; i < pips; i++)
        {
            // From the bottom of the arc up, the last round at the top, on the right of the mark.
            float t = pips == 1 ? 0.5f : (float)i / (pips - 1);
            float angle = To + ((From - To) * t);
            float x = cx + (MathF.Cos(angle) * radius);
            float y = cy + (MathF.Sin(angle) * radius);
            bool isLit = i < lit;
            Bgra32 colour = isLit ? HudDrawContext.Fade(on, show) : HudDrawContext.Fade(palette.Dim, 0.5f * show);
            context.Painter.FillRect(new UiRect(x - (size * 0.5f), y - (size * 0.5f), size, size), colour);
        }
    }
}

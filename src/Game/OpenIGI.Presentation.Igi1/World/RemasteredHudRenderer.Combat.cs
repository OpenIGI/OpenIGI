using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>
/// The local player's combat feedback round the crosshair: the hit marker's four ticks as its rounds strike (red for a kill,
/// wider for the head), and a red glow on a ring round the middle pointing where each shot that struck it came from.
/// </content>
internal sealed partial class RemasteredHudRenderer
{
    private static Bgra32 HitWhite => Bgra32.FromRgb(255, 255, 255);
    private static Bgra32 HitKill => Bgra32.FromRgb(255, 72, 60);
    private static Bgra32 DamageRed => Bgra32.FromRgb(232, 40, 32);

    /// <inheritdoc/>
    public void DrawSpectating(SpriteBatch batch, string name, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        UiRect safe = context.SafeRect;
        float centre = safe.X + (safe.Width * 0.5f);
        string watched = $"SPECTATING  {name.ToUpperInvariant()}";
        const string next = "FIRE: NEXT PLAYER";
        float big = Px(22f);
        float small = Px(12f);
        float width = Math.Max(UiPainter.MeasureLabel(type.Heading, watched, big, 0.08f), UiPainter.MeasureLabel(type.Mono, next, small, 0.14f));
        var plate = new UiRect(centre - (width * 0.5f) - Px(22f), safe.Bottom - Px(150f), width + Px(44f), Px(64f));
        Band(painter, plate, StripFace);
        painter.DrawLabel(type.Heading, watched, new UiVec2(centre - (UiPainter.MeasureLabel(type.Heading, watched, big, 0.08f) * 0.5f), plate.Y + Px(9f)), big, UiPalette.TextBright, 0.08f);
        painter.DrawLabel(type.Mono, next, new UiVec2(centre - (UiPainter.MeasureLabel(type.Mono, next, small, 0.14f) * 0.5f), plate.Y + Px(40f)), small, UiPalette.TextMuted, 0.14f);
    }

    /// <inheritdoc/>
    public void DrawNetGraph(SpriteBatch batch, IReadOnlyList<string> lines, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || _theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        UiRect safe = context.SafeRect;
        float size = Px(13f);
        float step = Px(18f);
        float width = lines.Max(l => UiPainter.MeasureLabel(type.Mono, l, size, 0.04f)) + Px(24f);
        var plate = new UiRect(safe.Right - width - Px(16f), safe.Y + (safe.Height * 0.62f), width, (step * lines.Count) + Px(16f));
        Band(painter, plate, StripFace);
        for (int i = 0; i < lines.Count; i++)
        {
            painter.DrawLabel(type.Mono, lines[i], new UiVec2(plate.X + Px(12f), plate.Y + Px(8f) + (step * i)), size, UiPalette.TextBody, 0.04f);
        }
    }

    /// <inheritdoc/>
    public void DrawCombat(SpriteBatch batch, CombatHud hud, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(hud);
        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        UiRect safe = context.SafeRect;
        UiVec2 middle = new(safe.X + (safe.Width * 0.5f), safe.Y + (safe.Height * 0.5f));

        // Each mark a short band of the ring, brightest at its inner edge and gone at its outer, with a hard line along it.
        float inner = Px(118f);
        foreach (DamageMark mark in hud.Marks)
        {
            byte alpha = (byte)(220 * Math.Clamp(mark.Strength, 0f, 1f));
            const float half = 0.34f;
            painter.FillArc(middle, inner, inner + Px(30f), mark.Angle - half, mark.Angle + half, DamageRed.WithAlpha(alpha), DamageRed.WithAlpha(0));
            painter.FillArc(middle, inner, inner + Px(3f), mark.Angle - (half * 0.8f), mark.Angle + (half * 0.8f), DamageRed.WithAlpha((byte)Math.Min(255, alpha + 30)));
        }

        // The round's numbers, under the round's card.
        if (hud.Report is { } report && _theme is { } type)
        {
            string line = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"DAMAGE DEALT {report.Dealt * 100:0}   ·   TAKEN {report.Taken * 100:0}   ·   HITS {report.Hits}");
            float size = Px(17f);
            float width = UiPainter.MeasureLabel(type.Mono, line, size, 0.12f);
            var plate = new UiRect(middle.X - (width * 0.5f) - Px(18f), safe.Y + (safe.Height * 0.66f), width + Px(36f), Px(38f));
            Band(painter, plate, StripFace);
            painter.DrawLabel(type.Mono, line, new UiVec2(middle.X - (width * 0.5f), plate.Y + Px(10f)), size, UiPalette.TextBright, 0.12f);
        }

        if (hud.Marker is { } marker)
        {
            Bgra32 colour = (marker.Killed ? HitKill : HitWhite).WithAlpha((byte)(255 * Math.Clamp(marker.Strength, 0f, 1f)));
            float near = Px(marker.Head ? 9f : 7f);
            float far = Px(marker.Head ? 22f : 17f) + (Px(4f) * (1f - marker.Strength));
            float width = Px(marker.Killed ? 3f : 2.2f);
            for (int corner = 0; corner < 4; corner++)
            {
                float angle = (MathF.PI * 0.25f) + (corner * MathF.PI * 0.5f);
                var along = new UiVec2(MathF.Sin(angle), -MathF.Cos(angle));
                painter.StrokeSoft(
                    new UiVec2(middle.X + (along.X * near), middle.Y + (along.Y * near)),
                    new UiVec2(middle.X + (along.X * far), middle.Y + (along.Y * far)),
                    width,
                    colour);
            }
        }
    }
}

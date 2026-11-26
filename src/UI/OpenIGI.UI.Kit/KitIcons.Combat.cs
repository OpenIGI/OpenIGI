using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Kit;

/// <content>
/// The kill feed's marks: solid silhouettes rather than line icons, so they read at a feed row's height over any sky, as a
/// weapon's own picture beside them does.
/// </content>
public static partial class KitIcons
{
    /// <summary>Whether an icon is one of the kill feed's solid marks, drawn by <see cref="DrawCombatIcon"/>.</summary>
    /// <param name="icon">The icon.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsCombatIcon(KitIcon icon) =>
        icon is KitIcon.Headshot or KitIcon.Skull or KitIcon.Explosion or KitIcon.Fall or KitIcon.Knife or KitIcon.TeamKill;

    // A kill feed mark, centred in a box of the size given.
    private static void DrawCombatIcon(UiPainter painter, KitIcon icon, UiVec2 center, float size, float stroke, Bgra32 color)
    {
        float h = size * 0.5f;
        float cx = center.X;
        float cy = center.Y;
        switch (icon)
        {
            case KitIcon.Headshot:
            {
                // A head and shoulders, the head in a sight: a thin ring round it and a tick out of it on each side.
                UiVec2 head = new(cx, cy - (h * 0.3f));
                painter.FillDot(head, h * 0.3f, color);
                painter.FillArc(new UiVec2(cx, cy + h), 0f, h * 0.72f, -MathF.PI * 0.5f, MathF.PI * 0.5f, color);
                float ring = h * 0.56f;
                float line = MathF.Max(1f, stroke * 0.8f);
                painter.FillArc(head, ring - (line * 0.5f), ring + (line * 0.5f), 0f, MathF.Tau, color);
                painter.StrokeLine(new UiVec2(head.X - ring - (h * 0.32f), head.Y), new UiVec2(head.X - ring + (h * 0.08f), head.Y), line, color);
                painter.StrokeLine(new UiVec2(head.X + ring - (h * 0.08f), head.Y), new UiVec2(head.X + ring + (h * 0.32f), head.Y), line, color);
                painter.StrokeLine(new UiVec2(head.X, head.Y - ring - (h * 0.14f)), new UiVec2(head.X, head.Y - ring + (h * 0.08f)), line, color);
                break;
            }

            case KitIcon.Skull:
            {
                // A solid skull: the cranium and the jaw in the colour, the eyes, the nose and the gaps between the teeth cut
                // out of it in black at the colour's own strength.
                Bgra32 hole = Bgra32.FromRgba(0, 0, 0, color.A);
                painter.FillDot(new UiVec2(cx, cy - (h * 0.18f)), h * 0.72f, color);
                painter.FillRect(UiRect.FromEdges(cx - (h * 0.42f), cy + (h * 0.3f), cx + (h * 0.42f), cy + (h * 0.9f)), color);
                painter.FillDot(new UiVec2(cx - (h * 0.3f), cy - (h * 0.1f)), h * 0.2f, hole);
                painter.FillDot(new UiVec2(cx + (h * 0.3f), cy - (h * 0.1f)), h * 0.2f, hole);
                painter.FillTriangle(new UiVec2(cx, cy + (h * 0.14f)), new UiVec2(cx - (h * 0.1f), cy + (h * 0.34f)), new UiVec2(cx + (h * 0.1f), cy + (h * 0.34f)), hole);
                float gap = MathF.Max(1f, stroke * 0.7f);
                painter.FillRect(new UiRect(cx - (h * 0.14f) - (gap * 0.5f), cy + (h * 0.56f), gap, h * 0.34f), hole);
                painter.FillRect(new UiRect(cx + (h * 0.14f) - (gap * 0.5f), cy + (h * 0.56f), gap, h * 0.34f), hole);
                break;
            }

            case KitIcon.Explosion:
            {
                // A burst of ten points, uneven as a blast is, filled as a fan from its heart.
                const int Points = 10;
                Span<UiVec2> burst = stackalloc UiVec2[Points * 2];
                for (int i = 0; i < burst.Length; i++)
                {
                    float r = i % 2 == 0 ? h * (i % 4 == 0 ? 1f : 0.8f) : h * 0.45f;
                    (float s, float c) = MathF.SinCos((i * MathF.PI / Points) - (MathF.PI * 0.5f));
                    burst[i] = new UiVec2(cx + (c * r), cy + (s * r));
                }

                for (int i = 0; i < burst.Length; i++)
                {
                    painter.FillTriangle(center, burst[i], burst[(i + 1) % burst.Length], color);
                }

                break;
            }

            case KitIcon.Fall:
            {
                // An arrow down onto the ground.
                float thick = stroke * 1.4f;
                painter.StrokeLine(new UiVec2(cx, cy - h), new UiVec2(cx, cy + (h * 0.25f)), thick, color);
                painter.FillTriangle(new UiVec2(cx - (h * 0.45f), cy + (h * 0.05f)), new UiVec2(cx + (h * 0.45f), cy + (h * 0.05f)), new UiVec2(cx, cy + (h * 0.6f)), color);
                painter.FillRect(UiRect.FromEdges(cx - h, cy + (h * 0.8f), cx + h, cy + (h * 0.8f) + thick), color);
                break;
            }

            case KitIcon.Knife:
            {
                // Pointing right, as a feed's weapons point at whom they killed: the grip, the guard, the blade to its tip.
                float grip = h * 0.16f;
                painter.FillRect(UiRect.FromEdges(cx - h, cy - grip, cx - (h * 0.32f), cy + grip), color);
                painter.FillRect(UiRect.FromEdges(cx - (h * 0.32f), cy - (h * 0.36f), cx - (h * 0.2f), cy + (h * 0.36f)), color);
                Span<UiVec2> blade =
                [
                    new(cx - (h * 0.2f), cy - (h * 0.2f)),
                    new(cx + (h * 0.62f), cy - (h * 0.2f)),
                    new(cx + h, cy + (h * 0.12f)),
                    new(cx - (h * 0.2f), cy + (h * 0.12f)),
                ];
                painter.FillPolygon(blade, color);
                break;
            }

            case KitIcon.TeamKill:
            {
                // One of one's own, struck through.
                painter.FillDot(new UiVec2(cx, cy - (h * 0.38f)), h * 0.3f, color);
                painter.FillArc(new UiVec2(cx, cy + (h * 0.62f)), 0f, h * 0.62f, -MathF.PI * 0.5f, MathF.PI * 0.5f, color);
                painter.StrokeLine(new UiVec2(cx - h, cy + h), new UiVec2(cx + h, cy - h), stroke * 1.3f, color);
                break;
            }
        }
    }
}

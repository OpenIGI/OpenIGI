using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Kit;

/// <summary>The small line icons the design uses, drawn as strokes so they are crisp at any size on any renderer.</summary>
public enum KitIcon
{
    /// <summary>No icon.</summary>
    None,

    /// <summary>A tick: verify, done.</summary>
    Check,

    /// <summary>A folder: open the install.</summary>
    Folder,

    /// <summary>A cog: options.</summary>
    Gear,

    /// <summary>A cross: close, cancel.</summary>
    Close,

    /// <summary>A bar: minimise.</summary>
    Minimize,

    /// <summary>A square: maximise.</summary>
    Maximize,

    /// <summary>A magnifier: search.</summary>
    Search,

    /// <summary>A plus: add, new.</summary>
    Plus,

    /// <summary>An arrow up: update, load earlier.</summary>
    ArrowUp,

    /// <summary>An arrow down: download, load later.</summary>
    ArrowDown,

    /// <summary>An arrow right: go.</summary>
    ArrowRight,

    /// <summary>A chevron right: more.</summary>
    ChevronRight,

    /// <summary>A microphone: talk, a voice heard.</summary>
    Microphone,
}

/// <summary>Draws <see cref="KitIcon"/>s.</summary>
public static class KitIcons
{
    /// <summary>Draws an icon centred in a box.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="center">The centre, in pixels.</param>
    /// <param name="size">The icon's size, in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    public static void Draw(UiPainter painter, KitIcon icon, UiVec2 center, float size, Bgra32 color)
    {
        ArgumentNullException.ThrowIfNull(painter);
        float h = size * 0.5f;
        float stroke = MathF.Max(1f, size * 0.09f);
        float cx = center.X;
        float cy = center.Y;

        switch (icon)
        {
            case KitIcon.Check:
                painter.StrokeLine(new UiVec2(cx - h, cy), new UiVec2(cx - (h * 0.25f), cy + (h * 0.7f)), stroke, color);
                painter.StrokeLine(new UiVec2(cx - (h * 0.25f), cy + (h * 0.7f)), new UiVec2(cx + h, cy - (h * 0.7f)), stroke, color);
                break;

            case KitIcon.Folder:
            {
                var body = UiRect.FromEdges(cx - h, cy - (h * 0.55f), cx + h, cy + (h * 0.75f));
                painter.StrokeRect(body, stroke, color);
                painter.FillRect(UiRect.FromEdges(cx - h, cy - (h * 0.8f), cx - (h * 0.15f), cy - (h * 0.55f) + stroke), color);
                break;
            }

            case KitIcon.Gear:
            {
                const int Teeth = 8;
                for (int i = 0; i < Teeth; i++)
                {
                    float a = i * MathF.Tau / Teeth;
                    (float s, float c) = MathF.SinCos(a);
                    painter.StrokeLine(new UiVec2(cx + (c * h * 0.55f), cy + (s * h * 0.55f)), new UiVec2(cx + (c * h), cy + (s * h)), stroke * 1.6f, color);
                }

                Ring(painter, center, h * 0.62f, stroke, color);
                break;
            }

            case KitIcon.Close:
                painter.StrokeLine(new UiVec2(cx - h, cy - h), new UiVec2(cx + h, cy + h), stroke, color);
                painter.StrokeLine(new UiVec2(cx + h, cy - h), new UiVec2(cx - h, cy + h), stroke, color);
                break;

            case KitIcon.Minimize:
                painter.FillRect(new UiRect(cx - h, cy - (stroke * 0.5f), size, stroke), color);
                break;

            case KitIcon.Maximize:
                painter.StrokeRect(UiRect.FromEdges(cx - h, cy - h, cx + h, cy + h), stroke, color);
                break;

            case KitIcon.Search:
                Ring(painter, new UiVec2(cx - (h * 0.2f), cy - (h * 0.2f)), h * 0.6f, stroke, color);
                painter.StrokeLine(new UiVec2(cx + (h * 0.25f), cy + (h * 0.25f)), new UiVec2(cx + h, cy + h), stroke * 1.2f, color);
                break;

            case KitIcon.Plus:
                painter.FillRect(new UiRect(cx - h, cy - (stroke * 0.5f), size, stroke), color);
                painter.FillRect(new UiRect(cx - (stroke * 0.5f), cy - h, stroke, size), color);
                break;

            case KitIcon.ArrowUp:
                painter.StrokeLine(new UiVec2(cx, cy + h), new UiVec2(cx, cy - h + stroke), stroke, color);
                painter.DrawChevron(new UiVec2(cx, cy - (h * 0.45f)), size * 0.9f, UiDirection.Up, stroke, color);
                break;

            case KitIcon.ArrowDown:
                painter.StrokeLine(new UiVec2(cx, cy - h), new UiVec2(cx, cy + h - stroke), stroke, color);
                painter.DrawChevron(new UiVec2(cx, cy + (h * 0.45f)), size * 0.9f, UiDirection.Down, stroke, color);
                break;

            case KitIcon.ArrowRight:
                painter.StrokeLine(new UiVec2(cx - h, cy), new UiVec2(cx + h - stroke, cy), stroke, color);
                painter.DrawChevron(new UiVec2(cx + (h * 0.45f), cy), size * 0.9f, UiDirection.Right, stroke, color);
                break;

            case KitIcon.ChevronRight:
                painter.DrawChevron(center, size, UiDirection.Right, stroke, color);
                break;

            case KitIcon.Microphone:
            {
                // On the usual 24-unit grid: a capsule 6 wide from 2 to 15, a cradle of radius 7 round its foot, the stem
                // down to 22 and a foot across it. Arcs as bands, so the curves are smooth at any size.
                float u = size / 24f;
                UiVec2 At(float x, float y) => new(cx + ((x - 12f) * u), cy + ((y - 12f) * u));
                float half = stroke * 0.5f;
                painter.FillArc(At(12f, 5f), (3f * u) - half, (3f * u) + half, -MathF.PI * 0.5f, MathF.PI * 0.5f, color);
                painter.FillArc(At(12f, 12f), (3f * u) - half, (3f * u) + half, MathF.PI * 0.5f, MathF.PI * 1.5f, color);
                painter.StrokeLine(At(9f, 5f), At(9f, 12f), stroke, color);
                painter.StrokeLine(At(15f, 5f), At(15f, 12f), stroke, color);
                painter.FillArc(At(12f, 11f), (7f * u) - half, (7f * u) + half, MathF.PI * 0.5f, MathF.PI * 1.5f, color);
                painter.StrokeLine(At(5f, 9f), At(5f, 11f), stroke, color);
                painter.StrokeLine(At(19f, 9f), At(19f, 11f), stroke, color);
                painter.StrokeLine(At(12f, 18f), At(12f, 22f), stroke, color);
                painter.StrokeLine(At(8f, 22f), At(16f, 22f), stroke, color);
                break;
            }

            default:
                break;
        }
    }

    private static void Ring(UiPainter painter, UiVec2 center, float radius, float stroke, Bgra32 color)
    {
        const int Segments = 20;
        for (int i = 0; i < Segments; i++)
        {
            (float s0, float c0) = MathF.SinCos(i * MathF.Tau / Segments);
            (float s1, float c1) = MathF.SinCos((i + 1) * MathF.Tau / Segments);
            painter.StrokeLine(
                new UiVec2(center.X + (c0 * radius), center.Y + (s0 * radius)),
                new UiVec2(center.X + (c1 * radius), center.Y + (s1 * radius)),
                stroke,
                color);
        }
    }
}

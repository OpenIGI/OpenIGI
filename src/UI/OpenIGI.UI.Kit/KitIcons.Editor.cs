using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Kit;

/// <content>
/// The editor's icons, drawn on the same 24-unit grid and with the same stroke as the rest of the set, so a
/// toolbar of them reads as one family with the shell's.
/// </content>
public static partial class KitIcons
{
    private static void DrawEditorIcon(UiPainter painter, KitIcon icon, UiVec2 center, float size, float stroke, Bgra32 color)
    {
        float u = size / 24f;
        UiVec2 At(float x, float y) => new(center.X + ((x - 12f) * u), center.Y + ((y - 12f) * u));
        void Line(float x0, float y0, float x1, float y1) => painter.StrokeLine(At(x0, y0), At(x1, y1), stroke, color);
        void Box(float x0, float y0, float x1, float y1) => painter.StrokeRect(new UiRect(At(x0, y0).X, At(x0, y0).Y, (x1 - x0) * u, (y1 - y0) * u), stroke, color);
        void Fill(float x0, float y0, float x1, float y1) => painter.FillRect(new UiRect(At(x0, y0).X, At(x0, y0).Y, (x1 - x0) * u, (y1 - y0) * u), color);
        void Arc(float x, float y, float radius, float from, float to)
        {
            float half = stroke * 0.5f;
            painter.FillArc(At(x, y), (radius * u) - half, (radius * u) + half, from, to, color);
        }

        switch (icon)
        {
            case KitIcon.Undo:
                Arc(13f, 13f, 6f, -MathF.PI * 0.5f, MathF.PI * 0.75f);
                Line(13f, 7f, 5f, 7f);
                painter.DrawChevron(At(6.5f, 7f), size * 0.4f, UiDirection.Left, stroke, color);
                break;
            case KitIcon.Redo:
                Arc(11f, 13f, 6f, MathF.PI * 0.25f, MathF.PI * 1.5f);
                Line(11f, 7f, 19f, 7f);
                painter.DrawChevron(At(17.5f, 7f), size * 0.4f, UiDirection.Right, stroke, color);
                break;
            case KitIcon.Save:
                Box(4f, 4f, 20f, 20f);
                Fill(8f, 4f, 16f, 9f);
                Box(7f, 13f, 17f, 20f);
                break;
            case KitIcon.Play:
                painter.FillTriangle(At(7f, 4f), At(20f, 12f), At(7f, 20f), color);
                break;
            case KitIcon.Pause:
                Fill(6f, 5f, 10f, 19f);
                Fill(14f, 5f, 18f, 19f);
                break;
            case KitIcon.Stop:
                Fill(6f, 6f, 18f, 18f);
                break;
            case KitIcon.Select:
                painter.FillTriangle(At(6f, 3f), At(18f, 14f), At(6f, 19f), color);
                Line(11f, 14f, 15f, 21f);
                break;
            case KitIcon.Move:
                Line(12f, 3f, 12f, 21f);
                Line(3f, 12f, 21f, 12f);
                painter.DrawChevron(At(12f, 5f), size * 0.35f, UiDirection.Up, stroke, color);
                painter.DrawChevron(At(12f, 19f), size * 0.35f, UiDirection.Down, stroke, color);
                painter.DrawChevron(At(5f, 12f), size * 0.35f, UiDirection.Left, stroke, color);
                painter.DrawChevron(At(19f, 12f), size * 0.35f, UiDirection.Right, stroke, color);
                break;
            case KitIcon.Rotate:
                Arc(12f, 12f, 8f, -MathF.PI * 0.35f, MathF.PI * 1.35f);
                painter.DrawChevron(At(18.5f, 6.5f), size * 0.35f, UiDirection.Up, stroke, color);
                break;
            case KitIcon.Scale:
                Box(4f, 8f, 16f, 20f);
                Line(10f, 14f, 20f, 4f);
                Line(14f, 4f, 20f, 4f);
                Line(20f, 4f, 20f, 10f);
                break;
            case KitIcon.Cube:
                Line(12f, 3f, 20f, 7.5f);
                Line(20f, 7.5f, 20f, 16.5f);
                Line(20f, 16.5f, 12f, 21f);
                Line(12f, 21f, 4f, 16.5f);
                Line(4f, 16.5f, 4f, 7.5f);
                Line(4f, 7.5f, 12f, 3f);
                Line(4f, 7.5f, 12f, 12f);
                Line(20f, 7.5f, 12f, 12f);
                Line(12f, 12f, 12f, 21f);
                break;
            case KitIcon.Trash:
                Line(4f, 6f, 20f, 6f);
                Line(9f, 6f, 9f, 3.5f);
                Line(9f, 3.5f, 15f, 3.5f);
                Line(15f, 3.5f, 15f, 6f);
                Box(6f, 6f, 18f, 21f);
                Line(10f, 10f, 10f, 17f);
                Line(14f, 10f, 14f, 17f);
                break;
            case KitIcon.Copy:
                Box(8f, 8f, 20f, 20f);
                Line(4f, 16f, 4f, 4f);
                Line(4f, 4f, 16f, 4f);
                break;
            case KitIcon.Eye:
                Arc(12f, 20f, 11f, -MathF.PI * 0.85f, -MathF.PI * 0.15f);
                Arc(12f, 4f, 11f, MathF.PI * 0.15f, MathF.PI * 0.85f);
                Arc(12f, 12f, 2.5f, 0f, MathF.Tau);
                break;
            case KitIcon.Script:
                painter.DrawChevron(At(7f, 12f), size * 0.5f, UiDirection.Left, stroke, color);
                painter.DrawChevron(At(17f, 12f), size * 0.5f, UiDirection.Right, stroke, color);
                Line(14f, 5f, 10f, 19f);
                break;
            case KitIcon.Image:
                Box(3f, 5f, 21f, 19f);
                Line(5f, 17f, 10f, 11f);
                Line(10f, 11f, 14f, 15f);
                Line(14f, 15f, 16f, 13f);
                Line(16f, 13f, 19f, 17f);
                Arc(16f, 9f, 1.5f, 0f, MathF.Tau);
                break;
            case KitIcon.Sound:
                Fill(4f, 9f, 8f, 15f);
                painter.FillTriangle(At(8f, 9f), At(13f, 4f), At(13f, 20f), color);
                painter.FillTriangle(At(8f, 9f), At(13f, 20f), At(8f, 15f), color);
                Arc(13f, 12f, 4f, -MathF.PI * 0.35f, MathF.PI * 0.35f);
                Arc(13f, 12f, 7.5f, -MathF.PI * 0.35f, MathF.PI * 0.35f);
                break;
            case KitIcon.Person:
                Arc(12f, 7f, 3.5f, 0f, MathF.Tau);
                Arc(12f, 21f, 7.5f, -MathF.PI, 0f);
                break;
            case KitIcon.Light:
                Arc(12f, 10f, 6f, MathF.PI * 0.75f, MathF.PI * 2.25f);
                Line(9f, 15f, 9f, 18f);
                Line(15f, 15f, 15f, 18f);
                Line(9f, 18f, 15f, 18f);
                Line(10f, 21f, 14f, 21f);
                break;
            case KitIcon.Camera:
                Box(3f, 7f, 16f, 18f);
                painter.FillTriangle(At(16f, 12.5f), At(21f, 8f), At(21f, 17f), color);
                break;
            case KitIcon.Grid:
                Box(4f, 4f, 20f, 20f);
                Line(9.3f, 4f, 9.3f, 20f);
                Line(14.7f, 4f, 14.7f, 20f);
                Line(4f, 9.3f, 20f, 9.3f);
                Line(4f, 14.7f, 20f, 14.7f);
                break;
            case KitIcon.Globe:
                Arc(12f, 12f, 8.5f, 0f, MathF.Tau);
                Line(3.5f, 12f, 20.5f, 12f);
                Arc(5f, 12f, 9.5f, -MathF.PI * 0.27f, MathF.PI * 0.27f);
                Arc(19f, 12f, 9.5f, MathF.PI * 0.73f, MathF.PI * 1.27f);
                break;
            case KitIcon.Document:
                Line(5f, 3f, 14f, 3f);
                Line(14f, 3f, 19f, 8f);
                Line(19f, 8f, 19f, 21f);
                Line(19f, 21f, 5f, 21f);
                Line(5f, 21f, 5f, 3f);
                Line(14f, 3f, 14f, 8f);
                Line(14f, 8f, 19f, 8f);
                break;
            case KitIcon.Hierarchy:
                Box(3f, 3f, 10f, 8f);
                Box(12f, 10.5f, 21f, 15.5f);
                Box(12f, 17f, 21f, 22f);
                Line(6.5f, 8f, 6.5f, 19.5f);
                Line(6.5f, 13f, 12f, 13f);
                Line(6.5f, 19.5f, 12f, 19.5f);
                break;
            case KitIcon.History:
                Arc(12f, 12f, 8.5f, 0f, MathF.Tau);
                Line(12f, 7f, 12f, 12f);
                Line(12f, 12f, 15.5f, 14f);
                break;
            case KitIcon.Terrain:
                Line(2f, 19f, 9f, 8f);
                Line(9f, 8f, 13f, 14f);
                Line(13f, 14f, 16f, 10f);
                Line(16f, 10f, 22f, 19f);
                Line(2f, 19f, 22f, 19f);
                break;
            case KitIcon.Graph:
                Arc(5f, 6f, 2.5f, 0f, MathF.Tau);
                Arc(19f, 9f, 2.5f, 0f, MathF.Tau);
                Arc(10f, 19f, 2.5f, 0f, MathF.Tau);
                Line(7.4f, 6.6f, 16.6f, 8.4f);
                Line(17.6f, 11f, 11.5f, 17f);
                Line(6f, 8.3f, 9f, 16.6f);
                break;
            default:
                break;
        }
    }
}

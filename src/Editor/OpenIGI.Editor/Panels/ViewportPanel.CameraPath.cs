using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>A cutscene's camera as the viewport draws it, in world metres.</summary>
/// <param name="Line">Points along the path the camera flies, in order.</param>
/// <param name="Keys">Each shot's place and the way it looks.</param>
/// <param name="Playhead">Where the camera is at the playhead.</param>
/// <param name="Selected">The shot selected, or -1.</param>
internal sealed record CameraPathView(IReadOnlyList<Vector3d> Line, IReadOnlyList<(Vector3d At, Vector3d Looking)> Keys, Vector3d Playhead, int Selected);

/// <summary>The viewport's drawing of the cutscene the sequencer has open.</summary>
internal sealed partial class ViewportPanel
{
    /// <summary>Gets or sets what gives the camera path to draw, when there is one.</summary>
    public Func<CameraPathView?>? CameraPath { get; set; }

    // The path, its shots numbered with the way each looks, and where the playhead has the camera.
    private void DrawCameraPath(UiPainter painter)
    {
        if (CameraPath?.Invoke() is not { } path)
        {
            return;
        }

        Bgra32 colour = Bgra32.FromRgb(0xE8, 0xB0, 0x30);
        for (int i = 1; i < path.Line.Count; i++)
        {
            if (Projector.TryProject(path.Line[i - 1], out UiVec2 from) && Projector.TryProject(path.Line[i], out UiVec2 to))
            {
                painter.StrokeSoft(from, to, Theme.Px(2f), colour.WithAlpha(210));
            }
        }

        for (int i = 0; i < path.Keys.Count; i++)
        {
            (Vector3d at, Vector3d looking) = path.Keys[i];
            if (!Projector.TryProject(at, out UiVec2 screen))
            {
                continue;
            }

            // The way it looks, a tick of the same length on screen however far off the shot is.
            if (Projector.TryProject(at + looking, out UiVec2 ahead))
            {
                UiVec2 along = new(ahead.X - screen.X, ahead.Y - screen.Y);
                float reach = MathF.Sqrt((along.X * along.X) + (along.Y * along.Y));
                if (reach > 1e-3f)
                {
                    float scale = Theme.Px(28f) / reach;
                    painter.StrokeSoft(screen, new UiVec2(screen.X + (along.X * scale), screen.Y + (along.Y * scale)), Theme.Px(2f), colour.WithAlpha(180));
                }
            }

            bool selected = i == path.Selected;
            float radius = Theme.Px(selected ? 10f : 8f);
            painter.FillDot(screen, radius + Theme.Px(1.5f), Theme.Background);
            painter.FillDot(screen, radius, selected ? Theme.Accent : colour);
            string number = (i + 1).ToString(CultureInfo.InvariantCulture);
            float width = Theme.SmallFont.MeasureWidth(number, Theme.SmallTextSize);
            painter.DrawText(Theme.SmallFont, number, new UiVec2(screen.X - (width / 2), screen.Y - (Theme.SmallTextSize * 0.62f)), Theme.SmallTextSize, Bgra32.Black);
        }

        if (Projector.TryProject(path.Playhead, out UiVec2 head))
        {
            painter.FillDot(head, Theme.Px(6f), Theme.Background);
            painter.FillDot(head, Theme.Px(4.5f), Theme.Error);
        }
    }
}

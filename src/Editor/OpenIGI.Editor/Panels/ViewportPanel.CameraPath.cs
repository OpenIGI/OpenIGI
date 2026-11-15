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

    /// <summary>
    /// Gets or sets what gives the share of the picture's height a cutscene played live leaves between its letterbox bars, or
    /// null when none is shown through its camera.
    /// </summary>
    public Func<float?>? Letterbox { get; set; }

    /// <summary>Gets or sets what the banner over the view says while a cutscene key is piloted, or null for none.</summary>
    public Func<string?>? PilotBanner { get; set; }

    /// <summary>Gets or sets what Alt+wheel does while a key is piloted: true when it took the notches.</summary>
    public Func<float, bool>? PilotWheel { get; set; }

    // The piloting banner, along the view's top, accent-edged so the view plainly belongs to the key.
    private void DrawPilotBanner(UiPainter painter)
    {
        if (PilotBanner?.Invoke() is not { Length: > 0 } text)
        {
            return;
        }

        UiRect world = WorldRect;
        float width = MathF.Min(world.Width - Theme.Px(16f), Theme.Font.MeasureWidth(text, Theme.TextSize) + Theme.Px(20f));
        UiRect box = new(world.X + ((world.Width - width) * 0.5f), world.Y + Theme.Px(8f), width, Theme.RowHeight + Theme.Px(6f));
        painter.FillRect(box, Theme.Chrome.WithAlpha(230));
        painter.FillRect(new UiRect(box.X, box.Bottom - Theme.Px(2f), box.Width, Theme.Px(2f)), Theme.Accent);
        painter.PushClip(box);
        painter.DrawText(Theme.Font, text, new UiVec2(box.X + Theme.Px(10f), box.Y + Theme.Px(3f)), Theme.TextSize, Theme.Text);
        painter.PopClip();
    }

    // A scene's letterbox, over the view as the player sees it over the screen.
    private void DrawLetterbox(UiPainter painter)
    {
        if (Letterbox?.Invoke() is not { } share || share >= 0.999f)
        {
            return;
        }

        UiRect world = WorldRect;
        float bar = world.Height * (1f - Math.Clamp(share, 0f, 1f)) * 0.5f;
        painter.FillRect(new UiRect(world.X, world.Y, world.Width, bar), Bgra32.Black);
        painter.FillRect(new UiRect(world.X, world.Bottom - bar, world.Width, bar), Bgra32.Black);
    }

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

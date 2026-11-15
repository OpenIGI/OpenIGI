using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>One cutscene camera key as the viewport draws it.</summary>
/// <param name="At">Where it is seen, in world metres.</param>
/// <param name="View">How it looks, as a camera takes it (rows right, up, forward).</param>
/// <param name="Lens">Its lens: the horizontal half-angle tangent.</param>
/// <param name="Rides">Whether it rides a task.</param>
/// <param name="Watching">Where what it watches stands, in world metres; null for none.</param>
/// <param name="Number">Its number among the scene's keys of their own; 0 for an ease's helper, which is not drawn.</param>
internal sealed record CameraKeyGlyph(Vector3d At, Matrix3f View, float Lens, bool Rides, Vector3d? Watching, int Number);

/// <summary>A cutscene's camera rig as the viewport draws it, in world metres.</summary>
/// <param name="Line">Points along the path the camera flies, in order, each with whether it is drawn relative to a ride and whether it is flown to from the point before (false across a cut).</param>
/// <param name="Ticks">Where the camera is every half second, for its speed.</param>
/// <param name="Keys">Each key.</param>
/// <param name="Playhead">Where the camera is at the playhead.</param>
/// <param name="Selected">The key selected, or -1.</param>
/// <param name="Note">What the drawing of riding keys means, or null.</param>
internal sealed record CameraPathView(
    IReadOnlyList<(Vector3d At, bool Riding, bool Joined)> Line,
    IReadOnlyList<Vector3d> Ticks,
    IReadOnlyList<CameraKeyGlyph> Keys,
    Vector3d Playhead,
    int Selected,
    string? Note);

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

    /// <summary>Gets or sets where a cutscene key is seen and how it is turned (world metres, engine storage); null for a task that is not one.</summary>
    public Func<MissionTask, (Vector3d PositionMetres, Matrix3f Orientation)?>? CameraKeyPose { get; set; }

    /// <summary>Gets or sets what moves or turns a cutscene key from the handles: where it is to be seen, how it is to be turned.</summary>
    public Action<MissionTask, Vector3d?, Matrix3f?>? MoveCameraKey { get; set; }

    /// <summary>Gets or sets what a click on a key's camera in the viewport does, by its index.</summary>
    public Action<int>? CameraKeyPicked { get; set; }

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
        // Under the view's own chips (the mission's name, the camera's speed), never over them.
        UiRect box = new(world.X + ((world.Width - width) * 0.5f), world.Y + Theme.Px(16f) + Theme.ControlHeight, width, Theme.RowHeight + Theme.Px(6f));
        text = UI.Editor.Theme.EditorDraw.Fit(Theme.Font, text, Theme.TextSize, width - Theme.Px(20f));
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

    // The rig: the path (teal where it rides, drawn in its ride's space), a tick every half second, each key a camera with its lens,
    // numbered, a line to what it watches, and where the playhead has the camera.
    private void DrawCameraPath(UiPainter painter)
    {
        if (CameraPath?.Invoke() is not { } path)
        {
            return;
        }

        Bgra32 colour = Bgra32.FromRgb(0xE8, 0xB0, 0x30);
        Bgra32 riding = Bgra32.FromRgb(0x3C, 0xC8, 0xC0);
        for (int i = 1; i < path.Line.Count; i++)
        {
            if (path.Line[i].Joined && Projector.TryProject(path.Line[i - 1].At, out UiVec2 from) && Projector.TryProject(path.Line[i].At, out UiVec2 to))
            {
                painter.StrokeSoft(from, to, Theme.Px(2f), (path.Line[i].Riding ? riding : colour).WithAlpha(210));
            }
        }

        foreach (Vector3d tick in path.Ticks)
        {
            if (Projector.TryProject(tick, out UiVec2 at))
            {
                painter.FillDot(at, Theme.Px(2f), colour.WithAlpha(230));
            }
        }

        // Numbered dots never drawn over each other: one that would cover another already drawn is a small plain dot (the selected
        // key is drawn first, so it always keeps its number).
        List<UiVec2> numbered = [];
        IEnumerable<int> order = path.Selected >= 0 ? [path.Selected, .. Enumerable.Range(0, path.Keys.Count).Where(i => i != path.Selected)] : Enumerable.Range(0, path.Keys.Count);
        foreach (int i in order)
        {
            CameraKeyGlyph key = path.Keys[i];
            if (key.Number == 0)
            {
                continue;
            }

            bool selected = i == path.Selected;
            Bgra32 ink = selected ? Theme.Accent : key.Rides ? riding : colour;
            if (key.Watching is { } target && Projector.TryProject(key.At, out UiVec2 eye) && Projector.TryProject(target, out UiVec2 there))
            {
                painter.StrokeSoft(eye, there, Theme.Px(1f), ink.WithAlpha(120));
            }

            DrawFrustum(painter, key, ink, selected);
            if (!Projector.TryProject(key.At, out UiVec2 screen))
            {
                continue;
            }

            float radius = Theme.Px(selected ? 9f : 7.5f);
            if (numbered.Any(n => MathF.Abs(n.X - screen.X) < radius * 2 && MathF.Abs(n.Y - screen.Y) < radius * 2))
            {
                painter.FillDot(screen, Theme.Px(3f), ink);
                continue;
            }

            numbered.Add(screen);
            painter.FillDot(screen, radius + Theme.Px(1.5f), Theme.Background);
            painter.FillDot(screen, radius, ink);
            string number = key.Number.ToString(CultureInfo.InvariantCulture);
            float width = Theme.SmallFont.MeasureWidth(number, Theme.SmallTextSize);
            painter.DrawText(Theme.SmallFont, number, new UiVec2(screen.X - (width / 2), screen.Y - (Theme.SmallTextSize * 0.62f)), Theme.SmallTextSize, Bgra32.Black);
        }

        if (Projector.TryProject(path.Playhead, out UiVec2 head))
        {
            painter.FillDot(head, Theme.Px(6f), Theme.Background);
            painter.FillDot(head, Theme.Px(4.5f), Theme.Error);
        }

        if (path.Note is { Length: > 0 } note)
        {
            // On a chip, as the view's other notes are, so it reads over any ground.
            UiRect world = WorldRect;
            float width = Theme.SmallFont.MeasureWidth(note, Theme.SmallTextSize) + Theme.Px(16f);
            UiRect chip = new(world.X + Theme.Px(10f), world.Bottom - Theme.ControlHeight - Theme.Px(10f), width, Theme.ControlHeight);
            UI.Editor.Theme.EditorDraw.Fill(painter, Theme, chip, Theme.Background.WithAlpha(200));
            painter.DrawText(Theme.SmallFont, note, new UiVec2(chip.X + Theme.Px(8f), MathF.Round(chip.Y + ((chip.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f))), Theme.SmallTextSize, riding);
        }
    }

    // A key's camera as a frustum: its lens across the game's 4:3 picture, a metre and a half deep, the top edge marked.
    private void DrawFrustum(UiPainter painter, CameraKeyGlyph key, Bgra32 ink, bool selected)
    {
        const double Depth = 1.5;
        static Vector3d Axis(Vector3f v) => new(v.X, v.Y, v.Z);
        Vector3d forward = Axis(key.View.Row3) * Depth;
        Vector3d across = Axis(key.View.Row1) * (key.Lens * Depth);
        Vector3d up = Axis(key.View.Row2) * (key.Lens * 0.75 * Depth);
        Vector3d[] corners = [key.At + forward - across + up, key.At + forward + across + up, key.At + forward + across - up, key.At + forward - across - up];
        if (!Projector.TryProject(key.At, out UiVec2 apex))
        {
            return;
        }

        UiVec2[] screen = new UiVec2[4];
        for (int c = 0; c < 4; c++)
        {
            if (!Projector.TryProject(corners[c], out screen[c]))
            {
                return;
            }
        }

        float width = Theme.Px(selected ? 1.6f : 1.1f);
        for (int c = 0; c < 4; c++)
        {
            painter.StrokeSoft(apex, screen[c], width, ink.WithAlpha(150));
            painter.StrokeSoft(screen[c], screen[(c + 1) % 4], c == 0 ? width * 2f : width, ink.WithAlpha(c == 0 ? (byte)230 : (byte)170));
        }
    }

    // A press on a key's camera: that key picked. True when it was one.
    private bool PickCameraKey(UiVec2 point)
    {
        if (CameraKeyPicked is not { } picked || CameraPath?.Invoke() is not { } path)
        {
            return false;
        }

        int nearest = -1;
        float closest = Theme.Px(11f);
        for (int i = 0; i < path.Keys.Count; i++)
        {
            if (path.Keys[i].Number > 0 && Projector.TryProject(path.Keys[i].At, out UiVec2 at))
            {
                float distance = MathF.Sqrt(((at.X - point.X) * (at.X - point.X)) + ((at.Y - point.Y) * (at.Y - point.Y)));
                if (distance <= closest)
                {
                    (nearest, closest) = (i, distance);
                }
            }
        }

        if (nearest < 0)
        {
            return false;
        }

        picked(nearest);
        return true;
    }
}
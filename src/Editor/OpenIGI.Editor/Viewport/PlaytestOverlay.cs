using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The guards' minds over a play-test (F10): each living guard's sight laid on the ground as the cone it sees with -- yellow,
/// orange once alerted, red while it sees its target -- the route it has still to walk, and what it is doing over its head.
/// </summary>
/// <remarks>
/// Read from the running soldiers each frame (<see cref="IEditorHost.SnapshotSoldiers"/>), as the game's own sight sweep and
/// script left them, and drawn over the frame through the game camera's view; only those near enough to matter are drawn.
/// </remarks>
internal sealed class PlaytestOverlay
{
    // How far from the eye a guard is drawn, and how many are named.
    private const double ReachMetres = 150;
    private const int MostNamed = 12;

    private static readonly Bgra32 Calm = Bgra32.FromRgb(0xF2, 0xD0, 0x4C);
    private static readonly Bgra32 Alert = Bgra32.FromRgb(0xF0, 0x8A, 0x2E);
    private static readonly Bgra32 Sees = Bgra32.FromRgb(0xF0, 0x40, 0x3A);
    private static readonly Bgra32 Route = Bgra32.FromRgb(0x6F, 0xB8, 0xFF);

    private readonly List<SoldierSnapshot> _soldiers = [];
    private readonly List<(UiRect Box, string Text, Bgra32 Colour)> _tags = [];

    /// <summary>Draws the guards near the eye.</summary>
    /// <param name="painter">The painter, over the frame at one unit a pixel.</param>
    /// <param name="theme">The theme, for the labels.</param>
    /// <param name="host">The host, for the soldiers and the view.</param>
    public void Draw(UiPainter painter, EditorTheme theme, IEditorHost host)
    {
        ViewportProjector view = new(host.View);
        if (!view.IsValid)
        {
            return;
        }

        host.SnapshotSoldiers(_soldiers);
        Span<UiVec2> fan = stackalloc UiVec2[14];
        int named = 0;
        _tags.Clear();

        // The play-test's banner sits along the top.
        float keepClearBelow = theme.Px(14f) + theme.ControlHeight + theme.Px(10f);
        foreach (SoldierSnapshot soldier in _soldiers.Where(s => s is { HasMind: true, IsDead: false, IsCutsceneActor: false }).OrderBy(s => (s.Position - view.Eye).Length))
        {
            if ((soldier.Position - view.Eye).Length > ReachMetres)
            {
                break;
            }

            Bgra32 colour = soldier.SeesTarget ? Sees : soldier.Alerted ? Alert : Calm;
            Vector3d feet = soldier.Position + new Vector3d(0, 0, 0.1);
            // Its outline as plain lines: a cone seen nearly edge-on is a sliver, whose mitred stroke would spike.
            if (Fan(fan, view, feet, soldier.Facing, soldier.SightHalfAngle, soldier.SightMetres, host.View))
            {
                painter.FillPolygon(fan, colour.WithAlpha(soldier.SeesTarget ? (byte)70 : (byte)40), smoothEdges: false);
                for (int i = 0; i < fan.Length; i++)
                {
                    painter.StrokeLine(fan[i], fan[(i + 1) % fan.Length], 1.5f, colour.WithAlpha(200));
                }
            }

            // The route on from where he stands.
            Vector3d from = feet;
            foreach (Vector3d point in soldier.RouteMetres)
            {
                Vector3d to = point + new Vector3d(0, 0, 0.1);
                if (view.TryProject(from, out UiVec2 a) && view.TryProject(to, out UiVec2 b))
                {
                    painter.StrokeLine(a, b, theme.Px(2f), Route.WithAlpha(220));
                    painter.FillDot(b, theme.Px(3f), Route);
                }

                from = to;
            }

            // What he is doing over his head, nearest first; a tag that would cover another, or the banner, is left out.
            if (named < MostNamed && view.TryProject(soldier.Position + new Vector3d(0, 0, 2.1), out UiVec2 head))
            {
                string text = soldier.SeesTarget ? $"{soldier.Doing} - sees you" : soldier.Doing;
                float width = theme.Font.MeasureWidth(text, theme.TextSize) + theme.Px(10f);
                UiRect tag = new(head.X - (width * 0.5f), head.Y - theme.RowHeight, width, theme.RowHeight);
                if (tag.Y > keepClearBelow && !_tags.Any(t => t.Box.X < tag.Right && tag.X < t.Box.Right && t.Box.Y < tag.Bottom && tag.Y < t.Box.Bottom))
                {
                    named++;
                    _tags.Add((tag, text, colour));
                }
            }
        }

        // The tags after every shape, their boxes and then their words: the shapes and the text each go as one run.
        foreach ((UiRect box, _, _) in _tags)
        {
            painter.FillRect(box, Bgra32.FromRgb(16, 18, 21).WithAlpha(200));
        }

        foreach ((UiRect box, string text, Bgra32 colour) in _tags)
        {
            EditorDraw.Label(painter, theme, text, box with { X = box.X + theme.Px(5f) }, colour);
        }
    }

    // The cone on the ground from a point, a heading, half its width and its reach, projected; false when any of it is behind the
    // eye or lands far off the screen (a point just in front of the eye projects to thousands of screens away).
    private static bool Fan(Span<UiVec2> points, ViewportProjector view, Vector3d at, double heading, double half, double metres, EditorView frame)
    {
        if (metres <= 0)
        {
            return false;
        }

        float slackX = frame.Width * 2f, slackY = frame.Height * 2f;
        for (int i = 0; i < points.Length; i++)
        {
            double angle = heading - half + (2 * half * (i - 1) / (points.Length - 2));
            Vector3d point = i == 0 ? at : at + (new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0) * metres);
            if (!view.TryProject(point, out points[i]) || points[i].X < -slackX || points[i].X > frame.Width + slackX || points[i].Y < -slackY || points[i].Y > frame.Height + slackY)
            {
                return false;
            }
        }

        return true;
    }
}

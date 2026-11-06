using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The viewport's patrol tool: a soldier's patrol laid on the ground (<see cref="Viewport.PatrolTool"/>).</summary>
internal sealed partial class ViewportPanel
{
    /// <summary>Gets or sets the patrol tool, which the session makes.</summary>
    public Viewport.PatrolTool? Patrol { get; set; }

    private bool Patrolling => Tool == ViewportTool.Patrol && Patrol is { Soldier: not null };

    // A left press with the patrol tool on: whether the tool took it.
    private bool PatrolPress(in PointerEvent e) => Patrolling && Patrol!.Press(Projector, e.Position, Theme);

    // A right press on a waypoint: its menu, rather than the camera's look.
    private bool PatrolMenu(in PointerEvent e) =>
        e.Button == MouseButton.Right && Patrolling && Root is { } root && Patrol!.OpenMenu(root, Projector, e.Position, Theme);

    // A drag of a waypoint: whether the tool had one.
    private bool PatrolDrag(in PointerEvent e) => Patrolling && Patrol!.Drag(Projector, e.Position);

    // Backspace takes the last waypoint (or the one selected) away; Escape puts the tool down.
    private bool PatrolKey(in KeyEvent e)
    {
        if (!Patrolling || e.Modifiers != KeyModifiers.None)
        {
            return false;
        }

        switch (e.Key)
        {
            case KeyCode.Backspace or KeyCode.Delete:
                Patrol!.RemoveWaypoint(Patrol.Selected);
                return true;

            case KeyCode.Escape:
                Patrol!.End();
                Tool = ViewportTool.Move;
                return true;

            default:
                return false;
        }
    }

    // The route and the tool's hint, over the world.
    private void DrawPatrol(UiPainter painter)
    {
        if (!Patrolling)
        {
            return;
        }

        Patrol!.Draw(painter, Theme, Projector);
        UiRect world = WorldRect;
        string hint = Patrol.Hint;
        float width = Theme.SmallFont.MeasureWidth(hint, Theme.SmallTextSize) + Theme.Px(20f);
        UiRect box = new(world.X + ((world.Width - width) / 2), world.Bottom - Theme.Px(44f), width, Theme.Px(24f));
        UI.Editor.Theme.EditorDraw.Fill(painter, Theme, box, Theme.Panel.WithAlpha(225), Theme.Px(4f));
        painter.DrawText(Theme.SmallFont, hint, new UiVec2(box.X + Theme.Px(10f), box.Y + Theme.Px(5f)), Theme.SmallTextSize, Theme.Text);
    }
}

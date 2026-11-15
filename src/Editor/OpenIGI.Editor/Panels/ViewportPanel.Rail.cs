using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>Laying a cutscene rail on the ground: a click a point, the track drawn as it goes, Enter to make it.</content>
internal sealed partial class ViewportPanel
{
    /// <summary>Gets or sets the rail being laid, in world metres on the ground; null when none is.</summary>
    public Func<IReadOnlyList<Vector3d>?>? RailDraft { get; set; }

    /// <summary>Gets or sets what the rail's hint says.</summary>
    public Func<string>? RailHint { get; set; }

    /// <summary>Gets or sets what a click on the ground adds to the rail.</summary>
    public Action<Vector3d>? RailPoint { get; set; }

    /// <summary>Gets or sets what takes the rail's last point away.</summary>
    public Action? RailBack { get; set; }

    /// <summary>Gets or sets what ends the rail: true to make it.</summary>
    public Action<bool>? RailEnd { get; set; }

    private bool LayingRail => RailDraft?.Invoke() is not null;

    // A left press while a rail is laid: the ground there added. Taken whether or not it hit, so nothing under it is picked.
    private bool RailPress(in PointerEvent e)
    {
        if (!LayingRail || e.Button != MouseButton.Left)
        {
            return false;
        }

        if (Projector.TryRay(e.Position, out Vector3d ray) && _host.Terrain is { } terrain && terrain.TryRaycast(Projector.Eye, ray, 2000, out Vector3d hit))
        {
            RailPoint?.Invoke(hit);
        }

        return true;
    }

    // Enter makes the rail, Backspace takes its last point, Escape drops it.
    private bool RailKey(in KeyEvent e)
    {
        if (!LayingRail || e.Modifiers != KeyModifiers.None)
        {
            return false;
        }

        switch (e.Key)
        {
            case KeyCode.Return:
                RailEnd?.Invoke(true);
                return true;
            case KeyCode.Backspace or KeyCode.Delete:
                RailBack?.Invoke();
                return true;
            case KeyCode.Escape:
                RailEnd?.Invoke(false);
                return true;
            default:
                return false;
        }
    }

    // The track laid so far, its points, and the hint.
    private void DrawRail(UiPainter painter)
    {
        if (RailDraft?.Invoke() is not { } points)
        {
            return;
        }

        Bgra32 ink = Bgra32.FromRgb(0xE8, 0xB0, 0x30);
        UiVec2? last = null;
        foreach (Vector3d point in points)
        {
            if (!Projector.TryProject(point, out UiVec2 at))
            {
                last = null;
                continue;
            }

            if (last is { } from)
            {
                painter.StrokeSoft(from, at, Theme.Px(2.5f), ink);
            }

            painter.FillDot(at, Theme.Px(6f), Theme.Background);
            painter.FillDot(at, Theme.Px(4.5f), ink);
            last = at;
        }

        string hint = RailHint?.Invoke() ?? string.Empty;
        UiRect world = WorldRect;
        float width = Theme.SmallFont.MeasureWidth(hint, Theme.SmallTextSize) + Theme.Px(20f);
        // Above the row of notes along the view's foot (the rig's own note sits there), never over it.
        UiRect box = new(world.X + ((world.Width - width) / 2), world.Bottom - Theme.ControlHeight - Theme.Px(48f), width, Theme.Px(24f));
        UI.Editor.Theme.EditorDraw.Fill(painter, Theme, box, Theme.Panel.WithAlpha(225), Theme.Px(4f));
        painter.DrawText(Theme.SmallFont, hint, new UiVec2(box.X + Theme.Px(10f), box.Y + Theme.Px(5f)), Theme.SmallTextSize, Theme.Text);
    }
}

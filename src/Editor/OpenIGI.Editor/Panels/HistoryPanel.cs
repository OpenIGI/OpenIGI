using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The edit history as a list: click any entry to undo or redo to just after it.</summary>
internal sealed class HistoryPanel(EditorTheme theme, MissionWorkspace workspace) : EditorElement(theme)
{
    private readonly ScrollModel _scroll = new();

    private int Count => workspace.History.Done.Count + workspace.History.Undone.Count + 1;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect) =>
        _scroll.Measure(Count * Theme.RowHeight, contentRect.Height);

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        if (_scroll.TryGrab(Theme, Bounds, e.Position))
        {
            return true;
        }

        // Row 0 is the opened document; row n is the state after the n-th command.
        int row = (int)((e.Position.Y - Bounds.Y + _scroll.Offset) / Theme.RowHeight);
        int target = Math.Clamp(row, 0, Count - 1);
        while (workspace.History.Done.Count > target && workspace.History.Undo())
        {
        }

        while (workspace.History.Done.Count < target && workspace.History.Redo())
        {
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e) => _scroll.FollowGrab(Theme, Bounds, e.Position);

    /// <inheritdoc/>
    protected override void OnPointerUp(in PointerEvent e) => _scroll.ReleaseGrab();

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers) =>
        _scroll.Wheel(notches, Theme.RowHeight * 3f);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        painter.PushClip(Bounds);
        int current = workspace.History.Done.Count;
        for (int i = 0; i < Count; i++)
        {
            UiRect row = new(Bounds.X, Bounds.Y + (i * Theme.RowHeight) - _scroll.Offset, Bounds.Width, Theme.RowHeight);
            if (row.Bottom < Bounds.Y || row.Y > Bounds.Bottom)
            {
                continue;
            }

            string name = i == 0
                ? workspace.Mission?.Name ?? "Opened"
                : i <= current ? workspace.History.Done[i - 1].Name : workspace.History.Undone[^(i - current)].Name;
            if (i == current)
            {
                painter.FillRect(row, Theme.Selection);
            }

            Bgra32 ink = i <= current ? Theme.Text : Theme.TextDisabled;
            EditorDraw.Icon(painter, Theme, i == 0 ? KitIcon.Document : KitIcon.History, new UiRect(row.X + Theme.Px(6f), row.Y, Theme.IconSize, row.Height), i <= current ? Theme.Icon : Theme.TextDisabled);
            EditorDraw.Label(painter, Theme, name, new UiRect(row.X + Theme.Px(28f), row.Y, row.Width - Theme.Px(34f), row.Height), ink);
        }

        painter.PopClip();
        _scroll.DrawVertical(painter, Theme, Bounds, IsPointerOver ? Root?.PointerPosition : null);
    }
}

using OpenIGI.Core.Configuration;
using OpenIGI.Input;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// The developer console's mouse: the context menu, the floating lists, the scrollbar, text selection,
/// window dragging, clicks into the panels and the wheel — offered the pointer in that order.
/// </summary>
/// <remarks>
/// The order is the whole of the design. Whatever floats highest is asked first, so a press meant for
/// a menu item or a dropdown entry is never taken by the window drawn beneath it, and a drag that has
/// started keeps the pointer until the button is let go.
/// </remarks>
internal sealed class DevConsolePointer
{
    private readonly DevConsolePanels _panels;
    private readonly PromptEditor _editor;
    private readonly ContextMenu _menu;

    /// <summary>Initialises a new instance of the <see cref="DevConsolePointer"/> class.</summary>
    /// <param name="panels">The panels the pointer acts on.</param>
    /// <param name="editor">The typing shared by every panel, for the caret and the clipboard.</param>
    /// <param name="menu">The right-click menu.</param>
    public DevConsolePointer(DevConsolePanels panels, PromptEditor editor, ContextMenu menu)
    {
        _panels = panels;
        _editor = editor;
        _menu = menu;
    }

    /// <summary>Gets where the pointer was last seen, in viewport pixels.</summary>
    public UiVec2 Position { get; private set; }

    /// <summary>Applies a frame's mouse.</summary>
    /// <param name="input">The frame's input, read before it is captured.</param>
    /// <param name="viewport">The back buffer's size in whole pixels.</param>
    public void Update(InputState input, UiVec2 viewport)
    {
        WindowLayer? layer = _panels.Layer;
        if (layer is null)
        {
            return;
        }

        MouseState mouse = input.Mouse;
        var point = new UiVec2(mouse.X, mouse.Y);
        bool pressed = mouse.WasPressed(MouseButton.Left);
        Position = point;

        // The menu is above everything, so it gets first refusal on every press -- otherwise a click
        // meant for an item would be taken by whatever window happens to be under it.
        if (_menu.IsOpen)
        {
            _menu.HoveredIndex = _menu.ItemAt(point, _panels.Theme!.RowHeight);

            if (pressed)
            {
                if (!_menu.Invoke(_menu.HoveredIndex))
                {
                    _menu.Close();
                }

                return;
            }

            if (mouse.WasPressed(MouseButton.Right))
            {
                _menu.Close();
            }
        }

        if (mouse.WasPressed(MouseButton.Right))
        {
            OpenContextMenu(point);
            return;
        }

        // The palette's list and the console's completion dropdown both float above the windows they
        // belong to, so they are offered the press first; the window beneath would otherwise take it
        // and neither list could ever be clicked.
        if (pressed && HandleFloatingClick(point))
        {
            return;
        }

        if (HandleScrollBar(point, mouse, pressed) || HandleLogSelection(point, mouse, pressed))
        {
            return;
        }

        layer.UpdatePointer(mouse, viewport);

        // Dragging the watch panel's top or bottom edge is the reader deciding its height, so it stops
        // deciding its own.
        if (ReferenceEquals(layer.Active, _panels.WatchWindow) &&
            (layer.ActiveRegion & (WindowRegion.Top | WindowRegion.Bottom)) != 0)
        {
            _panels.WatchAutoHeight = false;
        }

        if (pressed)
        {
            HandlePanelClick(point);
        }

        if (mouse.WheelDelta != 0f)
        {
            HandleWheel(point, mouse.WheelDelta);
        }
    }

    /// <summary>Runs the scrollbar's thumb drag, which has to be offered the pointer before selection.</summary>
    /// <param name="point">Where the pointer is.</param>
    /// <param name="mouse">The frame's mouse, read before capture.</param>
    /// <param name="pressed">Whether the left button went down this frame.</param>
    /// <returns><see langword="true"/> when the bar owned the pointer.</returns>
    private bool HandleScrollBar(UiVec2 point, MouseState mouse, bool pressed)
    {
        ConsoleBody? body = _panels.Body;
        if (body is null || _panels.ConsoleWindow is not { IsOpen: true })
        {
            return false;
        }

        if (body.IsScrollDragging)
        {
            if (mouse.IsDown(MouseButton.Left))
            {
                body.DragScroll(point);
            }
            else
            {
                body.EndScrollDrag();
            }

            return true;
        }

        return pressed && body.BeginScrollDrag(point);
    }

    /// <summary>
    /// Runs the scrollback's click-and-drag text selection.
    /// </summary>
    /// <param name="point">Where the pointer is.</param>
    /// <param name="mouse">The frame's mouse, read before capture.</param>
    /// <param name="pressed">Whether the left button went down this frame.</param>
    /// <returns><see langword="true"/> when the pointer belonged to a selection.</returns>
    /// <remarks>
    /// A drag continues while the button is held even after the pointer leaves the log — the row is
    /// clamped rather than dropped — so running off the top or bottom keeps extending the selection
    /// instead of stopping dead where the viewport ended.
    /// </remarks>
    private bool HandleLogSelection(UiVec2 point, MouseState mouse, bool pressed)
    {
        ConsoleBody? body = _panels.Body;
        if (body is null || _panels.ConsoleWindow is not { IsOpen: true } consoleWindow)
        {
            return false;
        }

        if (body.Selection.IsDragging)
        {
            if (mouse.IsDown(MouseButton.Left))
            {
                body.ExtendSelect(point);
            }
            else
            {
                body.EndSelect();
            }

            return true;
        }

        if (pressed && consoleWindow.Frame.Contains(point) && body.BeginSelect(point))
        {
            _panels.Layer?.Raise(consoleWindow);
            return true;
        }

        // A press anywhere else drops the selection, which is what makes it feel like a selection
        // rather than a highlight that has to be dismissed.
        if (pressed)
        {
            body.ClearSelection();
        }

        return false;
    }

    /// <summary>
    /// Opens the right-click menu with whatever is possible where the pointer is.
    /// </summary>
    /// <param name="point">Where the pointer was.</param>
    private void OpenContextMenu(UiVec2 point)
    {
        ConsoleBody? body = _panels.Body;
        if (body is null || _panels.ConsoleWindow is not { IsOpen: true } consoleWindow || !consoleWindow.Frame.Contains(point))
        {
            return;
        }

        ConsoleModel model = _panels.Model;
        bool hasSelection = body.Selection.IsActive;
        bool canWrite = _editor.WriteClipboard is not null;

        // Copy is offered whether or not anything is selected, and greyed when it is not. Hiding it
        // would move every item below it, so the same entry would sit in a different place depending
        // on what the reader had done a moment earlier.
        _menu.Open(
            point,
            [
                new ContextMenuItem("Copy", () => _editor.WriteClipboard?.Invoke(body.SelectedText()), hasSelection && canWrite),
                new ContextMenuItem("Copy all", () => _editor.WriteClipboard?.Invoke(body.AllVisibleText()), canWrite),
                new ContextMenuItem("Select all", body.SelectAllLines),
                new ContextMenuItem("Paste", () => _editor.PasteFromClipboard(model), _editor.ReadClipboard is not null),
                new ContextMenuItem("Clear", () =>
                {
                    body.ClearSelection();
                    model.ClearScrollback();
                }),
            ]);
    }

    private bool HandleFloatingClick(UiVec2 point)
    {
        if (_panels.PaletteWindow is { IsOpen: true } && _panels.Palette is { } palette && palette.EntryAt(point) is >= 0 and var entry)
        {
            palette.SelectedIndex = entry;
            _panels.RunPaletteSelection();
            return true;
        }

        ConsoleModel model = _panels.Model;
        if (_panels.ConsoleWindow is { IsOpen: true } && _panels.Body is { } body && model.SelectSuggestion(body.SuggestionAt(point)))
        {
            model.AcceptSuggestion();
            _editor.ResetBlink();
            return true;
        }

        return false;
    }

    private void HandlePanelClick(UiVec2 point)
    {
        if (_panels.Inspector is { } inspector && _panels.InspectorWindow is { IsOpen: true } inspectorWindow && inspectorWindow.Frame.Contains(point))
        {
            int entity = inspector.RowAt(point);
            if (entity >= 0)
            {
                inspector.SelectedIndex = entity;
            }

            return;
        }

        if (_panels.Body is { } body && _panels.ConsoleWindow is { IsOpen: true } consoleWindow && consoleWindow.Frame.Contains(point))
        {
            ConsoleModel model = _panels.Model;
            for (int i = 0; i < body.SeverityToggles.Count; i++)
            {
                if (body.SeverityToggles[i].Contains(point))
                {
                    ConsoleSeverity severity = ConsoleBody.SeverityAt(i);
                    model.Filter.SetSeverity(severity, !model.Filter.IsSeverityShown(severity));
                    return;
                }
            }

            if (body.FindRect.Contains(point))
            {
                _panels.FindFocused = true;
                return;
            }

            _panels.FindFocused = false;
        }

        CVarBrowserBody? cvars = _panels.CVars;
        if (cvars is null || _panels.CVarWindow is not { IsOpen: true } cvarWindow || !cvarWindow.Frame.Contains(point))
        {
            return;
        }

        int row = cvars.RowAt(point);
        if (row >= 0)
        {
            // A second click on the row already chosen opens the value for editing, which is the
            // gesture a table has trained everyone to expect.
            bool again = row == cvars.SelectedIndex;
            cvars.SelectedIndex = row;

            if (again && !cvars.IsEditing && !cvars.CycleSelected())
            {
                cvars.BeginEdit();
            }

            _editor.ResetBlink();
        }
    }

    private void HandleWheel(UiVec2 point, float wheel)
    {
        if (_panels.Inspector is { } inspector && _panels.InspectorWindow is { IsOpen: true } && inspector.Rows.Bounds.Contains(point))
        {
            inspector.Rows.ScrollByRows(-wheel * 3f);
            return;
        }

        if (_panels.CVars is { } cvars && _panels.CVarWindow is { IsOpen: true } && cvars.Rows.Bounds.Contains(point))
        {
            cvars.Rows.ScrollByRows(-wheel * 3f);
            return;
        }

        if (_panels.Body is { } body && _panels.ConsoleWindow is { IsOpen: true } && body.Log.Bounds.Contains(point))
        {
            _panels.Model.Scroll((int)(wheel * 3f), _panels.VisibleLines);
        }
    }
}

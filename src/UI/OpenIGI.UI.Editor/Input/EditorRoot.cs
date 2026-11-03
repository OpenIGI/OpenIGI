using OpenIGI.Input;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Editor.Input;

/// <summary>
/// The top of an editor interface: lays out its content and popups, and turns the frame's raw input into
/// desktop events — hover, capture, focus, double clicks, key repeat, light-dismiss popups, tooltips and
/// shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// The host calls <see cref="Frame"/> once per frame with the window size and the input, then <see cref="UiElement.Draw"/>.
/// The root lays out at one design unit per pixel: the editor's sizes come from <see cref="EditorTheme"/>, which
/// carries its own scale, so the game's 1080p-relative layout scale never applies here.
/// </para>
/// <para>
/// Pointer positions are read, never deltas, because a host that captures the mouse zeroes the deltas; and the
/// root never captures the mouse itself, so the game's cursor lock must be released while the editor is up.
/// </para>
/// </remarks>
public sealed class EditorRoot : EditorElement
{
    private const float DoubleClickSeconds = 0.4f;
    private const float TooltipDelaySeconds = 0.6f;

    private static readonly MouseButton[] Buttons = [MouseButton.Left, MouseButton.Right, MouseButton.Middle];

    private static readonly KeyCode[] RepeatingKeys =
    [
        KeyCode.Left, KeyCode.Right, KeyCode.Up, KeyCode.Down, KeyCode.Backspace, KeyCode.Delete,
        KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home, KeyCode.End, KeyCode.Z, KeyCode.Y,
    ];

    private readonly List<Popup> _popups = [];
    private readonly KeyRepeater _repeater = new();
    private UiElement? _content;
    private EditorElement? _hovered;
    private EditorElement? _captured;
    private MouseButton _captureButton;
    private EditorElement? _focused;
    private UiVec2 _pointer;
    private double _clock;
    private double _lastPressTime = double.NegativeInfinity;
    private UiVec2 _lastPressPoint;
    private MouseButton _lastPressButton;
    private int _clickCount;
    private KeyCode? _repeatKey;
    private float _stillSeconds;
    private string? _tooltip;
    private DragDropData? _drag;
    private IDropTarget? _dropTarget;

    /// <summary>Initializes a new instance of the <see cref="EditorRoot"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public EditorRoot(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Gets the actions and the shortcut table keys fall through to.</summary>
    public ActionTable Actions { get; } = new();

    /// <summary>Gets or sets the clipboard text fields copy to and paste from; the host sets the system one.</summary>
    public IUiClipboard Clipboard { get; set; } = new MemoryClipboard();

    /// <summary>Gets or sets the content filling the root, below the popups.</summary>
    public UiElement? Content
    {
        get => _content;
        set
        {
            if (_content is not null)
            {
                RemoveChild(_content);
            }

            _content = value;
            if (value is not null)
            {
                InsertFirst(value);
            }
        }
    }

    /// <summary>Gets the element with keyboard focus.</summary>
    public EditorElement? Focused => _focused;

    /// <summary>Gets the element under the pointer.</summary>
    public EditorElement? Hovered => _hovered;

    /// <summary>Gets the open popups, bottom first.</summary>
    public IReadOnlyList<Popup> Popups => _popups;

    /// <summary>Gets the pointer shape the host should show.</summary>
    public EditorCursor CursorShape => (_captured ?? _hovered)?.Cursor ?? EditorCursor.Arrow;

    /// <summary>Gets the pointer position this frame.</summary>
    public UiVec2 PointerPosition => _pointer;

    /// <summary>Gets the tooltip showing now, or <see langword="null"/>.</summary>
    public string? VisibleTooltip => _tooltip;

    /// <summary>Lays out the interface and handles one frame of input.</summary>
    /// <param name="input">The frame's input.</param>
    /// <param name="width">The window width in pixels.</param>
    /// <param name="height">The window height in pixels.</param>
    /// <param name="elapsedSeconds">The real time since the last frame.</param>
    public void Frame(InputState input, float width, float height, float elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        _clock += elapsedSeconds;
        Layout(width, height);
        RoutePointer(input.Mouse, Modifiers(input.Keyboard), elapsedSeconds);
        RouteKeys(input.Keyboard, elapsedSeconds);
        Update(elapsedSeconds);
        Toasts.Update(elapsedSeconds);

        // Handlers may have changed the tree; lay out again so this frame draws what it now is.
        Layout(width, height);
    }

    /// <summary>Lays out the interface without handling input, as a capture or a test does.</summary>
    /// <param name="width">The window width in pixels.</param>
    /// <param name="height">The window height in pixels.</param>
    public void Layout(float width, float height)
    {
        LayoutContext context = new(MathF.Max(1f, width), MathF.Max(1f, height), new UiVec2(MathF.Max(1f, width), MathF.Max(1f, height)));
        Measure(context, context.Viewport);
        Arrange(new UiRect(0f, 0f, context.Viewport.X, context.Viewport.Y));
    }

    /// <summary>Gives keyboard focus to an element, or takes it away from everything.</summary>
    /// <param name="element">The element, or <see langword="null"/>.</param>
    public void SetFocus(EditorElement? element)
    {
        if (ReferenceEquals(element, _focused))
        {
            return;
        }

        EditorElement? previous = _focused;
        _focused = element;
        previous?.OnFocusChanged(false);
        element?.OnFocusChanged(true);
    }

    /// <summary>Opens a popup above everything.</summary>
    /// <param name="popup">The popup, with its anchor and placement set.</param>
    public void ShowPopup(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        if (_popups.Contains(popup))
        {
            return;
        }

        _popups.Add(popup);
        AddChild(popup);
    }

    /// <summary>Closes a popup and every popup it opened.</summary>
    /// <param name="popup">The popup.</param>
    public void ClosePopup(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        if (!_popups.Contains(popup))
        {
            return;
        }

        foreach (Popup child in _popups.Where(p => ReferenceEquals(p.Owner, popup)).ToArray())
        {
            ClosePopup(child);
        }

        _popups.Remove(popup);
        RemoveChild(popup);
        if (_focused is not null && _focused.IsWithin(popup))
        {
            SetFocus(null);
        }

        popup.RaiseClosed();
    }

    /// <summary>Gets the notifications in the window's corner.</summary>
    public Toasts Toasts { get; } = new();

    /// <summary>Gets what is being dragged between widgets, or <see langword="null"/>.</summary>
    public DragDropData? Dragging => _drag;

    /// <summary>
    /// Turns the press a widget holds into a drag of something to another widget: from now until the button is let go, the
    /// pointer carries <paramref name="drag"/>, and an <see cref="IDropTarget"/> under it when let go takes it.
    /// </summary>
    /// <param name="drag">What is dragged.</param>
    public void BeginDragDrop(DragDropData drag)
    {
        ArgumentNullException.ThrowIfNull(drag);
        _drag = drag;
        _tooltip = null;
    }

    /// <summary>Closes every popup.</summary>
    public void CloseAllPopups()
    {
        while (_popups.Count > 0)
        {
            ClosePopup(_popups[0]);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _content?.Measure(context, available);
        foreach (Popup popup in _popups)
        {
            popup.Measure(context, available);
        }

        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _content?.Arrange(contentRect);
        foreach (Popup popup in _popups)
        {
            popup.Arrange(popup.Place(popup.DesiredSize, contentRect.Size));
        }
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        Toasts.Draw(painter, Theme, Bounds);
        if (_drag is { } drag)
        {
            DrawDragLabel(painter, drag);
            return;
        }

        if (_tooltip is not { } text)
        {
            return;
        }

        float pad = Theme.Px(6f);
        Text.UiFont font = Theme.SmallFont;
        float size = Theme.SmallTextSize;
        UiVec2 extent = new(font.MeasureWidth(text, size) + (pad * 2f), font.LineHeight(size) + (pad * 1.4f));
        float x = MathF.Min(_pointer.X + Theme.Px(12f), Bounds.Width - extent.X);
        float y = _pointer.Y + Theme.Px(20f);
        if (y + extent.Y > Bounds.Height)
        {
            y = _pointer.Y - extent.Y - Theme.Px(6f);
        }

        UiRect box = new(MathF.Round(MathF.Max(0f, x)), MathF.Round(MathF.Max(0f, y)), extent.X, extent.Y);
        EditorDraw.Shadow(painter, Theme, box);
        EditorDraw.Frame(painter, Theme, box, Theme.Popup, Theme.BorderStrong);
        painter.DrawText(font, text, new UiVec2(box.X + pad, MathF.Round(box.Y + (pad * 0.7f))), size, Theme.Text);
    }

    // The dragged thing's name by the pointer: accented where it can be dropped, muted where it cannot.
    private void DrawDragLabel(UiPainter painter, DragDropData drag)
    {
        float pad = Theme.Px(6f);
        UiVec2 extent = new(Theme.Font.MeasureWidth(drag.Label, Theme.TextSize) + (pad * 2f), Theme.ControlHeight);
        UiRect box = new(MathF.Round(_pointer.X + Theme.Px(14f)), MathF.Round(_pointer.Y + Theme.Px(14f)), extent.X, extent.Y);
        EditorDraw.Shadow(painter, Theme, box);
        EditorDraw.Frame(painter, Theme, box, Theme.Popup, _dropTarget is null ? Theme.BorderStrong : Theme.Accent);
        EditorDraw.Label(painter, Theme, drag.Label, box.Deflate(new Layout.Thickness(pad, 0f)), _dropTarget is null ? Theme.TextMuted : Theme.Text);
    }

    private static KeyModifiers Modifiers(KeyboardState keyboard)
    {
        KeyModifiers modifiers = KeyModifiers.None;
        if (keyboard.IsDown(KeyCode.LeftShift) || keyboard.IsDown(KeyCode.RightShift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (keyboard.IsDown(KeyCode.LeftControl) || keyboard.IsDown(KeyCode.RightControl))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (keyboard.IsDown(KeyCode.LeftAlt) || keyboard.IsDown(KeyCode.RightAlt))
        {
            modifiers |= KeyModifiers.Alt;
        }

        return modifiers;
    }

    private static EditorElement? EditorTarget(UiElement? element)
    {
        for (; element is not null; element = element.Parent)
        {
            if (element is EditorElement editor)
            {
                return editor;
            }
        }

        return null;
    }

    private static EditorElement? NextTarget(EditorElement element) => EditorTarget(element.Parent);

    private void RoutePointer(MouseState mouse, KeyModifiers modifiers, float elapsedSeconds)
    {
        UiVec2 point = new(mouse.X, mouse.Y);
        bool moved = point != _pointer;
        _pointer = point;

        if (_captured is { } held)
        {
            if (_drag is { } drag)
            {
                // A drag between widgets: whatever under the pointer would take it follows it, and takes it on release.
                _dropTarget = DropTargetAt(point, drag);
                if (moved)
                {
                    _dropTarget?.DragOver(drag, point);
                }

                if (!mouse.IsDown(_captureButton))
                {
                    IDropTarget? target = _dropTarget;
                    _drag = null;
                    _dropTarget = null;
                    EndCapture();
                    held.OnPointerUp(new PointerEvent(point, _captureButton, _clickCount, modifiers));
                    target?.Drop(drag, point);
                }

                return;
            }

            if (moved)
            {
                held.OnPointerMove(new PointerEvent(point, _captureButton, _clickCount, modifiers));
            }

            if (!mouse.IsDown(_captureButton))
            {
                EndCapture();
                held.OnPointerUp(new PointerEvent(point, _captureButton, _clickCount, modifiers));
            }

            return;
        }

        UpdateHover(point, moved, modifiers);
        UpdateTooltip(moved, elapsedSeconds);

        foreach (MouseButton button in Buttons)
        {
            if (mouse.WasPressed(button))
            {
                Press(point, button, modifiers);
                if (_captured is not null)
                {
                    return;
                }
            }
        }

        if (mouse.WheelDelta != 0f)
        {
            for (EditorElement? target = _hovered; target is not null; target = NextTarget(target))
            {
                if (target.IsEnabled && target.OnWheel(point, mouse.WheelDelta, modifiers))
                {
                    break;
                }
            }
        }
    }

    private IDropTarget? DropTargetAt(UiVec2 point, DragDropData drag)
    {
        for (UiElement? element = HitTest(point); element is not null; element = element.Parent)
        {
            if (element is IDropTarget target && element is EditorElement { IsEnabled: true } && target.CanDrop(drag))
            {
                return target;
            }
        }

        return null;
    }

    private void UpdateHover(UiVec2 point, bool moved, KeyModifiers modifiers)
    {
        EditorElement? hovered = EditorTarget(HitTest(point));
        if (!ReferenceEquals(hovered, _hovered))
        {
            if (_hovered is { } left)
            {
                left.IsPointerOver = false;
                left.OnPointerOverChanged(false);
            }

            _hovered = hovered;
            _stillSeconds = 0f;
            _tooltip = null;
            if (hovered is not null)
            {
                hovered.IsPointerOver = true;
                hovered.OnPointerOverChanged(true);
            }
        }

        if (moved)
        {
            hovered?.OnPointerMove(new PointerEvent(point, MouseButton.Left, 0, modifiers));
        }
    }

    private void UpdateTooltip(bool moved, float elapsedSeconds)
    {
        if (moved)
        {
            _stillSeconds = 0f;
            _tooltip = null;
            return;
        }

        _stillSeconds += elapsedSeconds;
        if (_tooltip is null && _stillSeconds >= TooltipDelaySeconds && _hovered?.Tooltip is { Length: > 0 } text)
        {
            _tooltip = text;
        }
    }

    private void Press(UiVec2 point, MouseButton button, KeyModifiers modifiers)
    {
        _tooltip = null;
        _stillSeconds = 0f;

        bool repeat = button == _lastPressButton
            && _clock - _lastPressTime <= DoubleClickSeconds
            && MathF.Abs(point.X - _lastPressPoint.X) <= Theme.Px(4f)
            && MathF.Abs(point.Y - _lastPressPoint.Y) <= Theme.Px(4f);
        _clickCount = repeat ? _clickCount + 1 : 1;
        _lastPressTime = _clock;
        _lastPressPoint = point;
        _lastPressButton = button;

        if (_popups.Count > 0)
        {
            // The popups above the one pressed in close (all of them, pressed outside every one), but never a modal one:
            // a press beside a dialog is taken and does nothing, and one inside it closes only what was opened over it.
            int pressed = _popups.FindLastIndex(p => p.Bounds.Contains(point));
            int modal = _popups.FindLastIndex(static p => p.IsModal);
            int keep = Math.Max(pressed, modal);
            if (keep < _popups.Count - 1)
            {
                bool swallow = _popups[^1].SwallowsDismissingPress;
                while (_popups.Count - 1 > keep)
                {
                    ClosePopup(_popups[^1]);
                }

                if (swallow)
                {
                    return;
                }

                UpdateHover(point, moved: false, modifiers);
            }

            if (pressed < 0 && modal >= 0)
            {
                return;
            }
        }

        if (button == MouseButton.Left)
        {
            EditorElement? focusable = _hovered;
            while (focusable is not null && !(focusable.Focusable && focusable.IsEnabled))
            {
                focusable = NextTarget(focusable);
            }

            if (focusable is null || focusable.FocusOnPress)
            {
                SetFocus(focusable);
            }
        }

        PointerEvent e = new(point, button, _clickCount, modifiers);
        for (EditorElement? target = _hovered; target is not null; target = NextTarget(target))
        {
            if (target.IsEnabled && target.OnPointerDown(e))
            {
                _captured = target;
                _captureButton = button;
                target.IsCapturing = true;
                return;
            }
        }
    }

    private void EndCapture()
    {
        if (_captured is { } held)
        {
            held.IsCapturing = false;
            _captured = null;
        }
    }

    private void RouteKeys(KeyboardState keyboard, float elapsedSeconds)
    {
        KeyModifiers modifiers = Modifiers(keyboard);
        while (keyboard.TryDequeuePress(out KeyCode key))
        {
            if (Array.IndexOf(RepeatingKeys, key) >= 0)
            {
                _repeatKey = key;
                _repeater.Reset();
                _repeater.Poll(keyboard, key, 0f);
            }

            DispatchKey(new KeyEvent(key, modifiers));
        }

        if (_repeatKey is { } held && !keyboard.WasPressed(held))
        {
            if (_repeater.Poll(keyboard, held, elapsedSeconds))
            {
                DispatchKey(new KeyEvent(held, modifiers, IsRepeat: true));
            }
            else if (!keyboard.IsDown(held))
            {
                _repeatKey = null;
            }
        }

        while (keyboard.TryDequeueCharacter(out char character))
        {
            if (character < ' ' || character == '\u007F' || (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == KeyModifiers.Control)
            {
                continue;
            }

            for (EditorElement? target = _focused; target is not null; target = NextTarget(target))
            {
                if (target.IsEnabled && target.OnText(character))
                {
                    break;
                }
            }
        }
    }

    private void DispatchKey(in KeyEvent e)
    {
        if (e.Key == KeyCode.Escape && _captured is { } held)
        {
            EndCapture();
            held.OnCaptureLost();
            return;
        }

        // An open popup takes the keyboard: menus are walked with the arrows, and Escape closes the top one.
        EditorElement? start = _focused ?? (_popups.Count > 0 ? _popups[^1] : null);
        if (_popups.Count > 0 && (start is null || !start.IsWithin(_popups[^1])))
        {
            start = _popups[^1];
        }

        for (EditorElement? target = start; target is not null; target = NextTarget(target))
        {
            if (target.IsEnabled && target.OnKeyDown(e))
            {
                return;
            }
        }

        if (e.Key == KeyCode.Escape && _popups.Count > 0)
        {
            ClosePopup(_popups[^1]);
            return;
        }

        // While a field has the keyboard, a plain key is typing: only chords with Ctrl or Alt, and the function keys,
        // are still shortcuts.
        bool typing = _focused?.AcceptsText == true
            && (e.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0
            && e.Key is not (>= KeyCode.F1 and <= KeyCode.F10) and not (KeyCode.F11 or KeyCode.F12);
        if (!typing && (!e.IsRepeat || e.Key is KeyCode.Z or KeyCode.Y))
        {
            Actions.TryShortcut(e);
        }
    }

    private void InsertFirst(UiElement element)
    {
        // The content draws beneath the popups and is hit-tested after them: it is the first child.
        UiElement[] rest = [.. Children];
        ClearChildren();
        AddChild(element);
        foreach (UiElement child in rest)
        {
            AddChild(child);
        }
    }
}

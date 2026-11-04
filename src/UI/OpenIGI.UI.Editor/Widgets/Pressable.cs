using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// The press-and-release behaviour every clickable widget shares: a press captures, and the click happens on a
/// release over the element, so dragging off cancels it — the desktop rule.
/// </summary>
public abstract class Pressable : EditorElement
{
    /// <summary>Initializes a new instance of the <see cref="Pressable"/> class.</summary>
    /// <param name="theme">The theme.</param>
    protected Pressable(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised when the element is clicked, or activated from the keyboard.</summary>
    public event Action? Clicked;

    /// <summary>Gets whether the element is held down with the pointer over it.</summary>
    public bool IsPressed => IsCapturing && _over;

    /// <inheritdoc/>
    public override EditorCursor Cursor => EditorCursor.Hand;

    private bool _over;

    /// <summary>Clicks the element as the user would.</summary>
    public void PerformClick()
    {
        if (IsEnabled)
        {
            OnClick();
            Clicked?.Invoke();
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        _over = true;
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e) => _over = Bounds.Contains(e.Position);

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        if (Bounds.Contains(e.Position))
        {
            PerformClick();
        }

        _over = false;
    }

    /// <inheritdoc/>
    protected internal override void OnCaptureLost() => _over = false;

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        if (e.Key is KeyCode.Space or KeyCode.Return && !e.IsRepeat)
        {
            PerformClick();
            return true;
        }

        return false;
    }

    /// <summary>Called on a click, before <see cref="Clicked"/> is raised: a toggle flips itself here.</summary>
    protected virtual void OnClick()
    {
    }

    /// <summary>Draws the hover and press wash over a rectangle, the feedback every pressable shares.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The rectangle.</param>
    protected void DrawFeedback(UiPainter painter, UiRect rect)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (IsPressed)
        {
            EditorDraw.Fill(painter, Theme, rect, Theme.Pressed);
        }
        else if (IsPointerOver && !IsCapturing)
        {
            EditorDraw.Fill(painter, Theme, rect, Theme.Hover);
        }
    }
}

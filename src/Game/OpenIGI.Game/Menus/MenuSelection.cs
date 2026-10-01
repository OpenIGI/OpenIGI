namespace OpenIGI.Game.Menus;

/// <summary>
/// The manager's selected item: the one thing the arrow keys move and the pointer follows.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>Menu_SetSelected</c> (<c>0x4186E0</c>), <c>Menu_SelectItem</c>
/// (<c>0x418FE0</c>) and the key handling in <c>Menu_HandleKeys</c> (<c>0x418580</c>). The manager
/// keeps a flat array of the screen's selectable items at <c>+60</c>, its length at <c>+188</c>, the
/// selected index at <c>+192</c> and the selected item at <c>+56</c>.
/// </para>
/// <para>
/// The point worth naming: <b>the mouse and the keyboard drive the same selection.</b> Hovering an
/// item calls the same setter the arrow keys do and plays the same <c>menu_high</c> sound, so there is
/// never a keyboard highlight in one place and a mouse highlight in another. Exactly one item carries
/// the highlight flag at <c>+77</c>, which is what <c>MenuText_Render</c> tests before drawing an
/// entry lit and putting a glow behind it.
/// </para>
/// </remarks>
public sealed class MenuSelection
{
    private readonly List<MenuWindowTask> _items = [];

    /// <summary>Occurs when the selection moves, which the host answers by playing <c>menu_high</c>.</summary>
    public event EventHandler? Moved;

    /// <summary>Gets the selectable items, in tree order.</summary>
    public IReadOnlyList<MenuWindowTask> Items => _items;

    /// <summary>Gets the selected index, or <c>-1</c> when there is nothing to select.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Gets the selected item, or <see langword="null"/>.</summary>
    public MenuWindowTask? Selected =>
        SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

    /// <summary>Collects a screen's selectable items and selects the first.</summary>
    /// <param name="root">The screen, or any window to gather from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Only items that take part in interaction are collected — the same <c>+76</c> flag the hit-test
    /// gates on — so a caption or a decorative frame is never landed on by the arrow keys.
    /// </remarks>
    public void Rebuild(MenuWindowTask root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (MenuWindowTask item in _items)
        {
            item.IsHighlighted = false;
        }

        _items.Clear();
        Collect(root);

        SelectedIndex = -1;
        if (_items.Count > 0)
        {
            SetSelected(0, raise: false);
        }
    }

    /// <summary>Moves the selection to an index.</summary>
    /// <param name="index">The index to select.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the item list.</exception>
    public void SetSelected(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _items.Count);
        SetSelected(index, raise: true);
    }

    /// <summary>Moves the selection to a specific item, if it is selectable.</summary>
    /// <param name="item">The item to select.</param>
    /// <returns><see langword="true"/> when the selection moved.</returns>
    /// <remarks>
    /// This is what a pointer hover calls. Selecting the item that is already selected does nothing —
    /// no event, no sound — which is why sliding the mouse within one entry stays quiet.
    /// </remarks>
    public bool SelectItem(MenuWindowTask? item)
    {
        if (item is null)
        {
            return false;
        }

        int index = _items.IndexOf(item);
        if (index < 0 || index == SelectedIndex)
        {
            return false;
        }

        SetSelected(index, raise: true);
        return true;
    }

    /// <summary>Moves the selection to the next item, wrapping to the first.</summary>
    /// <returns><see langword="true"/> when there was something to move to.</returns>
    public bool MoveNext()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        int index = SelectedIndex + 1;
        if (index >= _items.Count)
        {
            index = 0;
        }

        SetSelected(index, raise: true);
        return true;
    }

    /// <summary>Moves the selection to the previous item, wrapping to the last.</summary>
    /// <returns><see langword="true"/> when there was something to move to.</returns>
    public bool MovePrevious()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        int index = SelectedIndex - 1;
        if (index < 0)
        {
            index = _items.Count - 1;
        }

        SetSelected(index, raise: true);
        return true;
    }

    /// <summary>Applies one input event.</summary>
    /// <param name="input">The event.</param>
    /// <returns>The event when it should be forwarded to the selected item, otherwise <see cref="MenuInputEvent.None"/>.</returns>
    /// <remarks>
    /// The manager handles the two movement codes itself and passes <see cref="MenuInputEvent.Left"/>,
    /// <see cref="MenuInputEvent.Right"/> and <see cref="MenuInputEvent.Enter"/> to whatever is
    /// selected. <see cref="MenuInputEvent.Enter"/> and <see cref="MenuInputEvent.Escape"/> are latched
    /// by the caller rather than dispatched here.
    /// </remarks>
    public MenuInputEvent Handle(MenuInputEvent input)
    {
        switch (input)
        {
            case MenuInputEvent.Next:
                MoveNext();
                return MenuInputEvent.None;
            case MenuInputEvent.Previous:
                MovePrevious();
                return MenuInputEvent.None;
            case MenuInputEvent.Left or MenuInputEvent.Right or MenuInputEvent.Enter:
                return Selected is null ? MenuInputEvent.None : input;
            default:
                return MenuInputEvent.None;
        }
    }

    /// <summary>Swaps the two direction pairs, as a dialog with <c>SwapKeys</c> set does.</summary>
    /// <param name="input">The event to map.</param>
    /// <returns>The mapped event.</returns>
    /// <remarks>
    /// <c>Menu_HandleKeys</c> exchanges up/down with left/right when the active popup asks for it, so a
    /// horizontal OK/Cancel dialog reads the arrow keys the way it is laid out.
    /// </remarks>
    public static MenuInputEvent SwapKeys(MenuInputEvent input) => input switch
    {
        MenuInputEvent.Next => MenuInputEvent.Right,
        MenuInputEvent.Previous => MenuInputEvent.Left,
        MenuInputEvent.Right => MenuInputEvent.Next,
        MenuInputEvent.Left => MenuInputEvent.Previous,
        _ => input,
    };

    private void SetSelected(int index, bool raise)
    {
        if (Selected is { } previous)
        {
            previous.IsHighlighted = false;
        }

        SelectedIndex = index;

        if (Selected is { } current)
        {
            current.IsHighlighted = true;
        }

        if (raise)
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Collect(MenuWindowTask window)
    {
        foreach (MenuWindowTask child in window.Children.OfType<MenuWindowTask>())
        {
            if (child.IsInteractive)
            {
                _items.Add(child);
            }

            Collect(child);
        }
    }
}

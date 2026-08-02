using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Focus;

/// <summary>
/// Tracks which widget holds focus and moves it by directional traversal — the one model that mouse,
/// keyboard and gamepad all drive.
/// </summary>
/// <remarks>
/// <para>
/// Directional traversal is spatial, not order-based: pressing a direction moves focus to the nearest
/// focusable widget that lies that way, measured from the current one. That is what lets a gamepad reach
/// a grid of cards or a two-column settings form as naturally as it reaches a single list, and it is why
/// the model is shared — the mouse sets focus by pointing, the keyboard and pad by pressing a direction,
/// but there is only ever one focused widget and one way it looks focused.
/// </para>
/// <para>
/// Scoring favours candidates directly ahead: the cost is the distance travelled in the pressed
/// direction plus twice the sideways offset, so a widget dead ahead beats one that is nearer but off to
/// the side. Widgets are gathered fresh from the tree each move, so the traversal always reflects the
/// current layout — collapsed or disabled widgets simply are not candidates.
/// </para>
/// </remarks>
public sealed class FocusManager
{
    private readonly UiElement _root;

    /// <summary>Initializes a new instance of the <see cref="FocusManager"/> class.</summary>
    /// <param name="root">The root of the tree focus moves within.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public FocusManager(UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
    }

    /// <summary>Gets the widget that currently holds focus, or <see langword="null"/> when none does.</summary>
    public IFocusable? Focused { get; private set; }

    /// <summary>Gives focus to a specific widget, or clears it with <see langword="null"/>.</summary>
    /// <param name="target">The widget to focus, or <see langword="null"/>.</param>
    public void Focus(IFocusable? target)
    {
        if (ReferenceEquals(target, Focused))
        {
            return;
        }

        if (target is not null && !target.CanFocus)
        {
            return;
        }

        Focused?.SetFocused(false);
        Focused = target;
        Focused?.SetFocused(true);
    }

    /// <summary>Focuses the first focusable widget in tree order, if focus is not already set.</summary>
    /// <returns><see langword="true"/> when a widget ended up focused.</returns>
    public bool FocusFirst()
    {
        if (Focused is { CanFocus: true })
        {
            return true;
        }

        foreach (IFocusable candidate in Candidates())
        {
            Focus(candidate);
            return true;
        }

        return false;
    }

    /// <summary>Drops focus if the focused widget can no longer hold it, e.g. after a relayout.</summary>
    public void Validate()
    {
        if (Focused is not null && !Focused.CanFocus)
        {
            Focused.SetFocused(false);
            Focused = null;
        }
    }

    /// <summary>Moves focus to the nearest focusable widget in a direction.</summary>
    /// <param name="direction">The direction to move.</param>
    /// <returns><see langword="true"/> when focus moved.</returns>
    public bool Move(NavDirection direction)
    {
        if (Focused is null)
        {
            return FocusFirst();
        }

        UiRect from = Focused.FocusBounds;
        UiVec2 origin = from.Center;

        IFocusable? best = null;
        float bestCost = float.PositiveInfinity;

        foreach (IFocusable candidate in Candidates())
        {
            if (ReferenceEquals(candidate, Focused))
            {
                continue;
            }

            UiVec2 target = candidate.FocusBounds.Center;
            float dx = target.X - origin.X;
            float dy = target.Y - origin.Y;

            float primary;
            float secondary;
            switch (direction)
            {
                case NavDirection.Up:
                    primary = -dy;
                    secondary = MathF.Abs(dx);
                    break;
                case NavDirection.Down:
                    primary = dy;
                    secondary = MathF.Abs(dx);
                    break;
                case NavDirection.Left:
                    primary = -dx;
                    secondary = MathF.Abs(dy);
                    break;
                default:
                    primary = dx;
                    secondary = MathF.Abs(dy);
                    break;
            }

            // Must actually lie in the pressed direction; a hair of tolerance so two widgets sharing an
            // edge still count as ahead of one another.
            if (primary <= 0.5f)
            {
                continue;
            }

            float cost = primary + (secondary * 2f);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        if (best is null)
        {
            return false;
        }

        Focus(best);
        return true;
    }

    /// <summary>Sets focus from a pointer position, focusing the topmost focusable widget under it.</summary>
    /// <param name="point">The pointer position in pixels.</param>
    /// <returns>The widget focused, or <see langword="null"/> when the pointer is over none.</returns>
    public IFocusable? FocusAt(UiVec2 point)
    {
        IFocusable? hit = null;
        foreach (IFocusable candidate in Candidates())
        {
            if (candidate.FocusBounds.Contains(point))
            {
                hit = candidate; // later widgets are drawn on top, so keep the last match
            }
        }

        if (hit is not null)
        {
            Focus(hit);
        }

        return hit;
    }

    private IEnumerable<IFocusable> Candidates()
    {
        foreach (UiElement element in _root.Descendants())
        {
            if (element is IFocusable focusable && focusable.CanFocus)
            {
                yield return focusable;
            }
        }
    }
}

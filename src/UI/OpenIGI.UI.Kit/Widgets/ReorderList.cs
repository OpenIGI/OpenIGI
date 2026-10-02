using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>An item of a <see cref="ReorderList"/> that draws itself picked up.</summary>
public interface IReorderItem
{
    /// <summary>Sets how far the item is lifted out of the list, eased: 0 resting, 1 carried.</summary>
    /// <param name="amount">The lift, from 0 to 1.</param>
    void SetLift(float amount);
}

/// <summary>
/// A vertical list the player puts in order by dragging, or with the keyboard and pad through a
/// <see cref="ReorderHandle"/>.
/// </summary>
/// <remarks>
/// <para>
/// The carried item follows the pointer and the others slide aside to show where it will land, eased over
/// <see cref="SettleSeconds"/> rather than jumping, so the eye can follow which row went where. The order
/// changes live as the item crosses its neighbours' middles; <see cref="Moved"/> is raised once, when it is
/// dropped somewhere other than where it started. Abandoning the drag (Back) puts it back.
/// </para>
/// <para>
/// The list owns the order it shows from the moment of the drop: the owner hears <see cref="Moved"/>,
/// records the new order, and finds this list already showing it, so nothing has to be rebuilt.
/// </para>
/// </remarks>
public sealed class ReorderList : UiElement
{
    private const float EdgeScroll = 56f;
    private const float EdgeScrollSpeed = 900f;

    private readonly List<UiElement> _order = [];
    private readonly Dictionary<UiElement, float> _shown = [];
    private readonly Dictionary<UiElement, float> _lift = [];
    private UiRect _content;
    private float _spacing;
    private bool _arranged;

    private UiElement? _carried;
    private int _from;
    private float _grab;
    private float _pointerY;
    private bool _byPointer;

    /// <summary>Raised when an item is dropped at a new position: its old index, then its new one.</summary>
    public event Action<int, int>? Moved;

    /// <summary>Gets or sets the gap between items, in design units.</summary>
    public float Spacing { get; set; }

    /// <summary>Gets or sets how long the other items take to slide into their new places.</summary>
    public float SettleSeconds { get; set; } = 0.14f;

    /// <summary>Gets the items in the order shown.</summary>
    public IReadOnlyList<UiElement> Items => _order;

    /// <summary>Gets the item being carried, or <see langword="null"/>.</summary>
    public UiElement? Carried => _carried;

    /// <summary>Gets a value indicating whether an item is picked up with the keyboard or pad.</summary>
    public bool IsCarriedByKeys => _carried is not null && !_byPointer;

    /// <summary>Adds an item at the end.</summary>
    /// <param name="item">The item.</param>
    public void Add(UiElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _order.Add(item);
        AddChild(item);
    }

    /// <summary>Removes every item, dropping anything being carried where it is.</summary>
    public void Clear()
    {
        _carried = null;
        _order.Clear();
        _shown.Clear();
        _lift.Clear();
        ClearChildren();
    }

    /// <summary>Works out which slot a carried item belongs in.</summary>
    /// <param name="centre">The carried item's centre, measured from the top of the list.</param>
    /// <param name="heights">The other items' heights, in order.</param>
    /// <param name="spacing">The gap between items.</param>
    /// <returns>The index the item would take among them.</returns>
    /// <remarks>An item passes a neighbour once its centre crosses that neighbour's middle.</remarks>
    public static int SlotFor(float centre, IReadOnlyList<float> heights, float spacing)
    {
        ArgumentNullException.ThrowIfNull(heights);

        float cursor = 0f;
        int slot = 0;
        foreach (float height in heights)
        {
            if (centre <= cursor + (height * 0.5f))
            {
                break;
            }

            cursor += height + spacing;
            slot++;
        }

        return slot;
    }

    /// <summary>Picks an item up under the pointer.</summary>
    /// <param name="item">The item, or any element inside it.</param>
    /// <param name="point">The pointer, in pixels.</param>
    /// <returns><see langword="true"/> when it was picked up.</returns>
    public bool BeginDrag(UiElement item, UiVec2 point)
    {
        if (ItemOf(item) is not { } found || _carried is not null || !_arranged)
        {
            return false;
        }

        _carried = found;
        _from = _order.IndexOf(found);
        _byPointer = true;
        _pointerY = point.Y;
        _grab = point.Y - found.Bounds.Y;

        // Drawn last, so it passes over the others.
        RemoveChild(found);
        AddChild(found);
        return true;
    }

    /// <summary>Moves the carried item with the pointer.</summary>
    /// <param name="point">The pointer, in pixels.</param>
    public void DragTo(UiVec2 point)
    {
        if (_carried is null || !_byPointer)
        {
            return;
        }

        _pointerY = point.Y;
        Reslot();
    }

    /// <summary>Puts the carried item down.</summary>
    /// <param name="cancel">Whether to put it back where it was picked up from instead.</param>
    public void Drop(bool cancel)
    {
        if (_carried is not { } carried)
        {
            return;
        }

        if (cancel)
        {
            _order.Remove(carried);
            _order.Insert(Math.Clamp(_from, 0, _order.Count), carried);
        }

        int to = _order.IndexOf(carried);
        _carried = null;
        if (!cancel && to != _from)
        {
            Moved?.Invoke(_from, to);
        }
    }

    /// <summary>Picks an item up to move it with the keyboard or pad.</summary>
    /// <param name="item">The item, or any element inside it.</param>
    /// <returns><see langword="true"/> when it was picked up.</returns>
    public bool BeginKeyboardMove(UiElement item)
    {
        if (ItemOf(item) is not { } found || _carried is not null)
        {
            return false;
        }

        _carried = found;
        _from = _order.IndexOf(found);
        _byPointer = false;
        RemoveChild(found);
        AddChild(found);
        return true;
    }

    /// <summary>Moves the item picked up with the keyboard one place.</summary>
    /// <param name="step">-1 for up, +1 for down.</param>
    /// <returns><see langword="true"/> when it moved; false at either end.</returns>
    public bool Step(int step)
    {
        if (_carried is not { } carried || _byPointer)
        {
            return false;
        }

        int at = _order.IndexOf(carried);
        int to = Math.Clamp(at + step, 0, _order.Count - 1);
        if (to == at)
        {
            return false;
        }

        _order.RemoveAt(at);
        _order.Insert(to, carried);
        return true;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float spacing = context.Px(Spacing);
        float height = 0f;
        float width = 0f;
        int counted = 0;

        foreach (UiElement item in _order)
        {
            if (item.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            item.Measure(context, new UiVec2(available.X, float.PositiveInfinity));
            height += item.DesiredSize.Y + (counted > 0 ? spacing : 0f);
            width = MathF.Max(width, item.DesiredSize.X);
            counted++;
        }

        return new UiVec2(width, height);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _content = contentRect;
        _spacing = context.Px(Spacing);
        _arranged = true;
        Place(snapNew: true);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (!_arranged)
        {
            return;
        }

        if (_carried is not null && _byPointer)
        {
            AutoScroll(elapsedSeconds);
            Reslot();
        }

        float settle = 1f - MathF.Exp(-elapsedSeconds / MathF.Max(0.001f, SettleSeconds / 3f));
        bool moving = false;

        foreach (UiElement item in _order)
        {
            float target = Target(item);
            float shown = _shown.GetValueOrDefault(item, target);
            float next = item == _carried && _byPointer ? target : shown + ((target - shown) * settle);
            if (MathF.Abs(next - target) < 0.25f)
            {
                next = target;
            }

            moving |= next != shown;
            _shown[item] = next;

            float wanted = item == _carried ? 1f : 0f;
            float lift = _lift.GetValueOrDefault(item);
            float eased = lift + ((wanted - lift) * settle);
            eased = MathF.Abs(eased - wanted) < 0.01f ? wanted : eased;
            if (eased != lift)
            {
                _lift[item] = eased;
                (item as IReorderItem)?.SetLift(eased);
            }
        }

        if (moving)
        {
            Place(snapNew: false);
        }
    }

    private void Place(bool snapNew)
    {
        foreach (UiElement item in _order)
        {
            if (item.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            float target = Target(item);
            if (snapNew && !_shown.ContainsKey(item))
            {
                _shown[item] = target;
            }

            float top = _shown.GetValueOrDefault(item, target);
            item.Arrange(new UiRect(_content.X, _content.Y + top, _content.Width, item.DesiredSize.Y));
        }
    }

    // Where an item belongs now, from the top of the list: the carried one under the pointer, the rest
    // stacked in the current order around the gap it leaves.
    private float Target(UiElement item)
    {
        if (item == _carried && _byPointer)
        {
            float total = Total();
            return Math.Clamp(_pointerY - _grab - _content.Y, 0f, MathF.Max(0f, total - item.DesiredSize.Y));
        }

        float cursor = 0f;
        foreach (UiElement other in _order)
        {
            if (other.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (other == item)
            {
                return cursor;
            }

            cursor += other.DesiredSize.Y + _spacing;
        }

        return cursor;
    }

    private float Total()
    {
        float total = 0f;
        int counted = 0;
        foreach (UiElement item in _order.Where(static item => item.Visibility != Visibility.Collapsed))
        {
            total += item.DesiredSize.Y + (counted++ > 0 ? _spacing : 0f);
        }

        return total;
    }

    // Moves the carried item to the slot its centre is over.
    private void Reslot()
    {
        if (_carried is not { } carried)
        {
            return;
        }

        float centre = Target(carried) + (carried.DesiredSize.Y * 0.5f);
        List<UiElement> others = [.. _order.Where(item => item != carried)];
        int slot = SlotFor(centre, [.. others.Select(static item => item.DesiredSize.Y)], _spacing);

        if (_order.IndexOf(carried) != slot)
        {
            _order.Remove(carried);
            _order.Insert(slot, carried);
        }
    }

    // Near the top or bottom of the viewport the list scrolls, so an item can be carried past what fits.
    private void AutoScroll(float elapsedSeconds)
    {
        if (FindAncestor<ScrollView>() is not { } viewport)
        {
            return;
        }

        float edge = (Context?.Px(EdgeScroll) ?? EdgeScroll);
        UiRect view = viewport.Bounds;
        float speed = 0f;
        if (_pointerY < view.Y + edge)
        {
            speed = -Math.Clamp((view.Y + edge - _pointerY) / edge, 0f, 1f);
        }
        else if (_pointerY > view.Bottom - edge)
        {
            speed = Math.Clamp((_pointerY - (view.Bottom - edge)) / edge, 0f, 1f);
        }

        if (speed != 0f)
        {
            viewport.ScrollBy(speed * (Context?.Px(EdgeScrollSpeed) ?? EdgeScrollSpeed) * elapsedSeconds);
        }
    }

    private UiElement? ItemOf(UiElement element)
    {
        for (UiElement? at = element; at is not null; at = at.Parent)
        {
            if (at.Parent == this)
            {
                return at;
            }
        }

        return null;
    }
}

/// <summary>
/// The grip a <see cref="ReorderList"/> item is moved by: six dots over the item's position, which the
/// pointer drags and accept picks up for the arrows to move.
/// </summary>
public sealed class ReorderHandle : Control
{
    private float _hot;

    /// <summary>Initializes a new instance of the <see cref="ReorderHandle"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public ReorderHandle(UiTheme theme)
        : base(theme)
    {
        Tooltip = "Drag to change the load order\nor press Enter, then the arrows";
    }

    /// <summary>Gets or sets the label under the grip: the item's position, as the player counts.</summary>
    public string Label { get; set; } = string.Empty;

    private ReorderList? List => FindAncestor<ReorderList>();

    /// <summary>Gets a value indicating whether this handle's item is picked up with the keyboard or pad.</summary>
    public bool IsCarriedByKeys => List is { IsCarriedByKeys: true } list && IsWithin(list.Carried);

    /// <inheritdoc/>
    public override bool CapturesPointer(UiVec2 point) => List is { } list && list.BeginDrag(this, point);

    /// <inheritdoc/>
    public override void PointerDragged(UiVec2 point) => List?.DragTo(point);

    /// <inheritdoc/>
    public override void PointerReleased(UiVec2 point, bool cancelled) => List?.Drop(cancelled);

    /// <summary>Puts down an item picked up with the keyboard back where it came from.</summary>
    /// <returns><see langword="true"/> when one was being carried.</returns>
    public bool CancelKeyboardMove()
    {
        if (!IsCarriedByKeys)
        {
            return false;
        }

        List!.Drop(cancel: true);
        return true;
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        if (!IsCarriedByKeys)
        {
            return false;
        }

        // Carried: up and down move the item, and left and right are swallowed so focus stays with it.
        if (direction is NavDirection.Up or NavDirection.Down)
        {
            if (List!.Step(direction == NavDirection.Up ? -1 : 1))
            {
                UiSounds.Moved();
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (List is not { } list)
        {
            return;
        }

        if (IsCarriedByKeys)
        {
            list.Drop(cancel: false);
        }
        else
        {
            list.BeginKeyboardMove(this);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(44f), context.Px(56f));

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        float wanted = IsPointerOver || Visual.Focus > 0.5f || IsWithin(List?.Carried) ? 1f : 0f;
        _hot += (wanted - _hot) * (1f - MathF.Exp(-elapsedSeconds * 18f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float warm = IsCarriedByKeys ? 1f : _hot;
        var muted = Bgra32.FromRgb(110, 112, 115);
        Bgra32 grip = Interpolate.Lerp(muted, Theme.Accent, warm);
        Bgra32 number = Interpolate.Lerp(muted, Theme.Text, warm * 0.8f);

        float dot = context.Px(3f);
        float gap = context.Px(5f);
        float numberSize = context.Px(16f);
        float labelHeight = Label.Length > 0 ? Theme.Mono.LineHeight(numberSize) + context.Px(6f) : 0f;
        float gripHeight = (dot * 3f) + (gap * 2f);
        float top = Bounds.Center.Y - ((gripHeight + labelHeight) * 0.5f);
        float left = Bounds.Center.X - ((dot * 2f) + gap) * 0.5f;

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                var at = new UiRect(left + (column * (dot + gap)), top + (row * (dot + gap)), dot, dot);
                painter.FillRect(at, UiPalette.Fade(grip, appear));
            }
        }

        if (Label.Length > 0)
        {
            float width = UiPainter.MeasureLabel(Theme.Mono, Label, numberSize, 0.08f);
            var at = new UiVec2(Bounds.Center.X - (width * 0.5f), top + gripHeight + context.Px(6f));
            painter.DrawLabel(Theme.Mono, Label, at, numberSize, UiPalette.Fade(number, appear), 0.08f);
        }
    }
}

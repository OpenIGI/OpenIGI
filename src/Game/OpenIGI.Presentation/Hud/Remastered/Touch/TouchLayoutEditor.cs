using OpenIGI.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>A thing the layout's editor moves: a control, or a readout of the display.</summary>
/// <param name="IsReadout">Whether it is a readout.</param>
/// <param name="Control">The control, when it is not.</param>
/// <param name="Readout">The readout, when it is.</param>
/// <param name="Extra">For a button the player added, its place among those; <c>-1</c> for one of the game's own.</param>
public readonly record struct TouchLayoutItem(bool IsReadout, TouchControlId Control, HudElement Readout, int Extra = -1)
{
    /// <summary>Gets its name, as the editor says it.</summary>
    public string Name => IsReadout ? TouchLayout.Name(Readout) : Extra >= 0 ? TouchLayout.Name(Control) + " (added)" : TouchLayout.Name(Control);

    /// <summary>Gets a value indicating whether it is a button the player added.</summary>
    public bool IsExtra => !IsReadout && Extra >= 0;

    /// <summary>A control as an item.</summary>
    /// <param name="id">The control.</param>
    /// <returns>The item.</returns>
    /// <param name="extra">For a button the player added, its place among those.</param>
    public static TouchLayoutItem Of(TouchControlId id, int extra = -1) => new(false, id, default, extra);

    /// <summary>A readout as an item.</summary>
    /// <param name="element">The readout.</param>
    /// <returns>The item.</returns>
    public static TouchLayoutItem Of(HudElement element) => new(true, default, element);
}

/// <summary>A thing on the editor's bar.</summary>
public enum TouchEditorWidget
{
    /// <summary>The first saved layout.</summary>
    Slot1,

    /// <summary>The second.</summary>
    Slot2,

    /// <summary>The third.</summary>
    Slot3,

    /// <summary>Takes back the last change.</summary>
    Undo,

    /// <summary>Puts the whole layout back as the game ships it.</summary>
    ResetAll,

    /// <summary>The stick, floating under the thumb or fixed where it is drawn.</summary>
    StickMode,

    /// <summary>Moves the bar to the screen's other edge, off what it covers.</summary>
    Flip,

    /// <summary>Saves the layouts.</summary>
    Save,

    /// <summary>Leaves the editor.</summary>
    Exit,

    /// <summary>The selected item's size, a slider.</summary>
    Size,

    /// <summary>The selected item's opacity, a slider.</summary>
    Opacity,

    /// <summary>Whether the selected item is on the screen.</summary>
    Show,

    /// <summary>Puts the selected item back as the game ships it.</summary>
    ResetItem,

    /// <summary>On leaving with changes: save them and leave.</summary>
    PromptSave,

    /// <summary>On leaving with changes: leave without them.</summary>
    PromptDiscard,

    /// <summary>On leaving with changes: stay.</summary>
    PromptKeep,

    /// <summary>Opens the list of buttons, to put another on the screen.</summary>
    Add,

    /// <summary>One of the list's buttons: a tap adds it.</summary>
    PaletteItem,
}

/// <summary>A thing on the editor's bar, where it is and how it stands.</summary>
/// <param name="Widget">Which.</param>
/// <param name="Box">Its box, in pixels.</param>
/// <param name="Enabled">Whether a tap does anything.</param>
/// <param name="Active">Whether it is lit: the layout in use, an item shown, the stick fixed.</param>
/// <param name="Value">A slider's place along its track, nought to one.</param>
/// <param name="Kind">For one of the list's buttons, the control a tap adds.</param>
public readonly record struct TouchEditorControl(
    TouchEditorWidget Widget, UiRect Box, bool Enabled = true, bool Active = false, float Value = 0f, TouchControlId Kind = default)
{
    /// <summary>Gets a value indicating whether it is one of the question's answers on leaving.</summary>
    public bool IsPrompt => Widget is TouchEditorWidget.PromptSave or TouchEditorWidget.PromptDiscard or TouchEditorWidget.PromptKeep;
}

/// <summary>A thing the editor moves, where it stands this frame.</summary>
/// <param name="Item">Which.</param>
/// <param name="Box">Its box, in pixels.</param>
/// <param name="Round">Whether it is round: its box is the square about its circle.</param>
/// <param name="Hidden">Whether the layout leaves it off the screen.</param>
/// <param name="Overlapped">Whether it laps over another control.</param>
/// <param name="Empty">Whether it is a readout with nothing to show now, its box standing in for it.</param>
public readonly record struct TouchEditorItem(TouchLayoutItem Item, UiRect Box, bool Round, bool Hidden, bool Overlapped, bool Empty = false);

/// <summary>
/// The editor of a touch screen's layout: the fingers move, resize, fade, hide and bring back every control and every readout of
/// the display, over three saved layouts.
/// </summary>
/// <remarks>
/// <para>
/// A finger on an item selects it and drags it; a second finger pinches its size. Dragged, an item keeps to the screen, falls to
/// a grid, and catches on the lines through the screen's middle and the other items' centres. The bar holds the rest: the three
/// layouts, undo, both resets, the stick's manner, and for the selected item its size, opacity and whether it is shown.
/// </para>
/// <para>
/// Nothing here draws or saves. The editor works on its own copies of the layouts, hands the one being edited to the controls
/// and the display each frame so the screen under it is the layout as it will be, and tells its host when the player saves
/// (<see cref="Saved"/>) or leaves (<see cref="Closed"/>).
/// </para>
/// </remarks>
public sealed class TouchLayoutEditor
{
    /// <summary>How many layouts a player keeps.</summary>
    public const int Slots = 3;

    private const float GridUnits = 5f;
    private const float GuideUnits = 10f;
    private const float DragSlopUnits = 8f;
    private const int UndoDepth = 64;

    private readonly TouchLayout[] _working = [new(), new(), new()];
    private readonly string[] _saved = [string.Empty, string.Empty, string.Empty];
    private readonly List<TouchLayout> _undo = [];
    private readonly List<TouchEditorItem> _items = [];
    private readonly List<TouchEditorControl> _bar = [];
    private int _savedSlot;
    private Drag? _drag;
    private Pinch? _pinch;
    private (int Finger, TouchEditorWidget Widget)? _slider;

    /// <summary>Raised as the player saves: the three layouts as text, and which is in use (nought to two).</summary>
    public event Action<IReadOnlyList<string>, int>? Saved;

    /// <summary>Raised as the player leaves.</summary>
    public event Action? Closed;

    /// <summary>Gets a value indicating whether the editor is up.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets which layout is being edited, nought to two.</summary>
    public int Slot { get; private set; }

    /// <summary>Gets the layout being edited.</summary>
    public TouchLayout Working => _working[Slot];

    /// <summary>Gets the selected item, or <see langword="null"/>.</summary>
    public TouchLayoutItem? Selected { get; private set; }

    /// <summary>Gets a value indicating whether the bar is at the bottom of the screen rather than the top.</summary>
    public bool BarAtBottom { get; private set; }

    /// <summary>Gets a value indicating whether the player is being asked what to do with unsaved changes.</summary>
    public bool Prompting { get; private set; }

    /// <summary>Gets a value indicating whether the list of buttons to add is up.</summary>
    public bool Adding { get; private set; }

    /// <summary>Gets the list's box, in pixels, while it is up.</summary>
    public UiRect PaletteBox { get; private set; }

    /// <summary>Gets the items, where they stand this frame.</summary>
    public IReadOnlyList<TouchEditorItem> Items => _items;

    /// <summary>Gets the bar's widgets, and the prompt's while it is up.</summary>
    public IReadOnlyList<TouchEditorControl> Bar => _bar;

    /// <summary>Gets the bar's box, in pixels: a finger on it moves nothing under it.</summary>
    public UiRect BarBox { get; private set; }

    /// <summary>Gets the box of the bar's second row, the selected item's, or an empty one with nothing selected.</summary>
    public UiRect SelectionBox { get; private set; }

    /// <summary>Gets the prompt's box, in pixels, while it is up.</summary>
    public UiRect PromptBox { get; private set; }

    /// <summary>Gets the upright line a dragged item has caught on, in pixels across, or <see langword="null"/>.</summary>
    public float? GuideX { get; private set; }

    /// <summary>Gets the level line a dragged item has caught on, in pixels down, or <see langword="null"/>.</summary>
    public float? GuideY { get; private set; }

    /// <summary>Gets a value indicating whether an item is being dragged or pinched.</summary>
    public bool Dragging => _drag is { Moved: true } || _pinch is not null;

    /// <summary>Gets a value indicating whether anything differs from what was last saved.</summary>
    public bool Dirty
    {
        get
        {
            if (Slot != _savedSlot)
            {
                return true;
            }

            for (int i = 0; i < Slots; i++)
            {
                if (TouchLayoutCodec.Encode(_working[i]) != _saved[i])
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets a value indicating whether there is a change to take back.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Opens the editor on the saved layouts.</summary>
    /// <param name="saved">The three layouts as text; a missing or empty one is the shipped layout.</param>
    /// <param name="slot">Which is in use, nought to two.</param>
    public void Open(IReadOnlyList<string> saved, int slot)
    {
        ArgumentNullException.ThrowIfNull(saved);
        for (int i = 0; i < Slots; i++)
        {
            _working[i] = TouchLayoutCodec.Decode(i < saved.Count ? saved[i] : null);

            // What is kept is the text as this build writes it, so a layout only re-spelt is not a change to save.
            _saved[i] = TouchLayoutCodec.Encode(_working[i]);
        }

        Slot = Math.Clamp(slot, 0, Slots - 1);
        _savedSlot = Slot;
        _undo.Clear();
        _drag = null;
        _pinch = null;
        _slider = null;
        Selected = null;
        Prompting = false;
        Adding = false;
        GuideX = GuideY = null;
        IsOpen = true;
    }

    /// <summary>Reads a frame's fingers and lays the screen out as the layout being edited has it.</summary>
    /// <param name="touch">The fingers.</param>
    /// <param name="context">The screen's layout, in back-buffer pixels.</param>
    /// <param name="controls">The controls, which are handed the layout being edited and laid out by it.</param>
    /// <param name="readouts">The display's arrangement, handed the same layout, or <see langword="null"/> where the display's readouts cannot be moved.</param>
    public void Update(TouchState touch, LayoutContext context, TouchControls controls, HudLayoutOverrides? readouts)
    {
        ArgumentNullException.ThrowIfNull(touch);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(controls);
        if (!IsOpen)
        {
            return;
        }

        Arrange(context, controls, readouts);
        foreach (TouchPoint point in touch.Points)
        {
            if (point.Began)
            {
                Down(point, context);
            }

            if (!IsOpen)
            {
                return;
            }

            Move(point, context);
            if (point.Ended)
            {
                Up(point);
            }
        }

        // The screen as the fingers left it, for this frame's drawing.
        Arrange(context, controls, readouts);
    }

    /// <summary>Saves the layouts: the host is told, and nothing then differs from what is saved.</summary>
    public void Save()
    {
        for (int i = 0; i < Slots; i++)
        {
            _saved[i] = TouchLayoutCodec.Encode(_working[i]);
        }

        _savedSlot = Slot;
        Saved?.Invoke(_saved, Slot);
    }

    /// <summary>Asks to leave: at once with nothing to save, else by the question of what to do with the changes.</summary>
    public void RequestExit()
    {
        if (!IsOpen)
        {
            return;
        }

        if (Dirty && !Prompting)
        {
            Prompting = true;
        }
        else if (!Dirty)
        {
            Close();
        }
    }

    /// <summary>Leaves the editor, with whatever was not saved dropped.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Prompting = false;
        _drag = null;
        _pinch = null;
        _slider = null;
        Closed?.Invoke();
    }

    /// <summary>Takes back the last change.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _working[Slot] = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
    }

    private void Arrange(LayoutContext context, TouchControls controls, HudLayoutOverrides? readouts)
    {
        // A step back or a reset may have taken the selected added button with it.
        if (Selected is { IsExtra: true } gone && gone.Extra >= Working.Extras.Count)
        {
            Selected = null;
        }

        controls.Editing = true;
        controls.Arrangement = Working;
        controls.Arrange(context);
        if (readouts is not null)
        {
            readouts.Layout = Working;
            readouts.Editing = true;
        }

        BuildItems(controls, readouts);
        BuildBar(context);
    }

    private void BuildItems(TouchControls controls, HudLayoutOverrides? readouts)
    {
        _items.Clear();
        float reach = controls.StickReachPixels;
        _items.Add(new TouchEditorItem(
            TouchLayoutItem.Of(TouchControlId.Stick),
            new UiRect(controls.StickRest.X - reach, controls.StickRest.Y - reach, reach * 2f, reach * 2f),
            Round: true,
            Hidden: !controls.StickShown,
            Overlapped: false));
        foreach (TouchButtonSpot spot in controls.Layout)
        {
            UiRect box = spot.Card
                ? spot.Box
                : new UiRect(spot.Centre.X - spot.Radius, spot.Centre.Y - spot.Radius, spot.Radius * 2f, spot.Radius * 2f);
            _items.Add(new TouchEditorItem(TouchLayoutItem.Of(spot.Id, spot.Extra), box, !spot.Card, spot.Hidden, Overlapped: false));
        }

        // A control lapping over another is told, not stopped: some players stack them on purpose.
        int controlCount = _items.Count;
        for (int i = 0; i < controlCount; i++)
        {
            for (int j = i + 1; j < controlCount; j++)
            {
                if (!_items[i].Hidden && !_items[j].Hidden && Laps(_items[i], _items[j]))
                {
                    _items[i] = _items[i] with { Overlapped = true };
                    _items[j] = _items[j] with { Overlapped = true };
                }
            }
        }

        if (readouts is null)
        {
            return;
        }

        foreach (HudElement element in TouchLayout.Readouts)
        {
            TouchLayoutItem item = TouchLayoutItem.Of(element);

            // The display reports where a readout stood when it was last drawn, a frame behind the finger: the one being
            // dragged stands where the drag has put it.
            UiRect? box = _drag is { Moved: true, Pinched: false } drag && _pinch is null && drag.Item == item ? drag.Box
                : readouts.Boxes.TryGetValue(element, out UiRect reported) ? reported : null;
            if (box is { IsEmpty: false } at)
            {
                _items.Add(new TouchEditorItem(
                    item, at, Round: false, Hidden: !Working.Resolve(element).Visible, Overlapped: false, Empty: readouts.Unshown.Contains(element)));
            }
        }
    }

    private static bool Laps(in TouchEditorItem a, in TouchEditorItem b)
    {
        if (a.Round && b.Round)
        {
            float reach = (a.Box.Width + b.Box.Width) * 0.5f * 0.92f;
            return UiVec2.Distance(a.Box.Center, b.Box.Center) < reach;
        }

        return !a.Box.Intersect(b.Box).IsEmpty;
    }

    private void BuildBar(LayoutContext context)
    {
        _bar.Clear();
        float u = context.Px(1f);
        float tall = 84f * u;
        float gap = 10f * u;
        float pad = 14f * u;
        UiRect safe = context.SafeRect;

        // The first row: the layouts, then what acts on the whole of one, then the way out.
        (TouchEditorWidget Widget, float Width)[] first =
        [
            (TouchEditorWidget.Slot1, 84f), (TouchEditorWidget.Slot2, 84f), (TouchEditorWidget.Slot3, 84f),
            (TouchEditorWidget.Add, 130f), (TouchEditorWidget.Undo, 84f), (TouchEditorWidget.ResetAll, 170f), (TouchEditorWidget.StickMode, 230f),
            (TouchEditorWidget.Flip, 84f), (TouchEditorWidget.Save, 150f), (TouchEditorWidget.Exit, 130f),
        ];
        (TouchEditorWidget Widget, float Width)[] second =
        [
            (TouchEditorWidget.Size, 330f), (TouchEditorWidget.Opacity, 330f), (TouchEditorWidget.Show, 84f), (TouchEditorWidget.ResetItem, 150f),
        ];
        const float NameUnits = 250f;

        static float Wide((TouchEditorWidget Widget, float Width)[] row, float u, float gap)
        {
            float width = -gap;
            foreach ((_, float w) in row)
            {
                width += (w * u) + gap;
            }

            return width;
        }

        float firstWide = Wide(first, u, gap);
        float secondWide = Wide(second, u, gap) + (NameUnits * u) + gap;
        float barWide = MathF.Min(MathF.Max(firstWide, secondWide) + (pad * 2f), safe.Width);
        float barTall = tall + (pad * 2f);
        float barX = safe.X + ((safe.Width - barWide) * 0.5f);
        float edge = 12f * u;
        float barY = BarAtBottom ? safe.Bottom - edge - barTall : safe.Y + edge;
        BarBox = new UiRect(barX, barY, barWide, barTall);

        float x = barX + ((barWide - firstWide) * 0.5f);
        foreach ((TouchEditorWidget widget, float w) in first)
        {
            UiRect box = new(x, barY + pad, w * u, tall);
            _bar.Add(widget switch
            {
                TouchEditorWidget.Slot1 or TouchEditorWidget.Slot2 or TouchEditorWidget.Slot3 =>
                    new TouchEditorControl(widget, box, Active: Slot == widget - TouchEditorWidget.Slot1),
                TouchEditorWidget.Add => new TouchEditorControl(widget, box, Enabled: Working.Extras.Count < TouchLayout.MaxExtras, Active: Adding),
                TouchEditorWidget.Undo => new TouchEditorControl(widget, box, Enabled: CanUndo),
                TouchEditorWidget.ResetAll => new TouchEditorControl(widget, box, Enabled: !Working.IsDefault),
                TouchEditorWidget.StickMode => new TouchEditorControl(widget, box, Active: Working.StickMode == TouchStickMode.Fixed),
                TouchEditorWidget.Save => new TouchEditorControl(widget, box, Enabled: Dirty, Active: Dirty),
                _ => new TouchEditorControl(widget, box),
            });
            x += (w * u) + gap;
        }

        // The second row, the selected item's, on the side of the first that faces the screen's middle.
        SelectionBox = default;
        if (Selected is { } selected)
        {
            float rowY = BarAtBottom ? barY - gap - barTall : barY + barTall + gap;
            SelectionBox = new UiRect(barX, rowY, barWide, barTall);
            (float scale, float opacity, bool visible, bool canHide, bool changed) = Describe(selected);
            x = barX + ((barWide - secondWide) * 0.5f) + (NameUnits * u) + gap;
            foreach ((TouchEditorWidget widget, float w) in second)
            {
                UiRect box = new(x, rowY + pad, w * u, tall);
                _bar.Add(widget switch
                {
                    TouchEditorWidget.Size => new TouchEditorControl(
                        widget, box, Value: (scale - TouchLayout.MinScale) / (TouchLayout.MaxScale - TouchLayout.MinScale)),
                    TouchEditorWidget.Opacity => new TouchEditorControl(
                        widget, box, Value: (opacity - TouchLayout.MinOpacity) / (1f - TouchLayout.MinOpacity)),
                    TouchEditorWidget.Show => new TouchEditorControl(widget, box, Enabled: canHide, Active: visible),
                    _ => new TouchEditorControl(widget, box, Enabled: changed),
                });
                x += (w * u) + gap;
            }
        }

        // The list of buttons to add: every control but the stick, of which there is one.
        PaletteBox = default;
        if (Adding)
        {
            const int Columns = 7;
            float cellTall = 132f * u, head = 64f * u;
            int count = TouchLayout.AllControls.Count - 1;
            int rows = (count + Columns - 1) / Columns;
            float wide = MathF.Min((Columns * 124f * u) + (pad * 2f), safe.Width);
            float high = head + (rows * cellTall) + pad;
            PaletteBox = new UiRect(safe.X + ((safe.Width - wide) * 0.5f), safe.Y + ((safe.Height - high) * 0.5f), wide, high);
            float cell = (wide - (pad * 2f)) / Columns;
            int at = 0;
            foreach (TouchControlId kind in TouchLayout.AllControls)
            {
                if (kind == TouchControlId.Stick)
                {
                    continue;
                }

                UiRect box = new(PaletteBox.X + pad + ((at % Columns) * cell), PaletteBox.Y + head + ((at / Columns) * cellTall), cell, cellTall);
                _bar.Add(new TouchEditorControl(TouchEditorWidget.PaletteItem, box, Kind: kind));
                at++;
            }
        }

        PromptBox = default;
        if (Prompting)
        {
            float wide = MathF.Min(760f * u, safe.Width);
            float high = 250f * u;
            PromptBox = new UiRect(safe.X + ((safe.Width - wide) * 0.5f), safe.Y + ((safe.Height - high) * 0.5f), wide, high);
            float buttonWide = (wide - (pad * 2f) - (gap * 2f)) / 3f;
            float buttonY = PromptBox.Bottom - pad - tall;
            _bar.Add(new TouchEditorControl(TouchEditorWidget.PromptSave, new UiRect(PromptBox.X + pad, buttonY, buttonWide, tall), Active: true));
            _bar.Add(new TouchEditorControl(TouchEditorWidget.PromptDiscard, new UiRect(PromptBox.X + pad + buttonWide + gap, buttonY, buttonWide, tall)));
            _bar.Add(new TouchEditorControl(TouchEditorWidget.PromptKeep, new UiRect(PromptBox.X + pad + ((buttonWide + gap) * 2f), buttonY, buttonWide, tall)));
        }
    }

    // The selected item as the bar's second row shows it.
    private (float Scale, float Opacity, bool Visible, bool CanHide, bool Changed) Describe(TouchLayoutItem item)
    {
        if (item.IsReadout)
        {
            HudPlacement readout = Working.Resolve(item.Readout);
            return (readout.Scale, readout.Opacity, readout.Visible, true, Working.Hud.ContainsKey(item.Readout));
        }

        TouchPlacement control = PlacementOf(item);
        return item.IsExtra
            ? (control.Scale, control.Opacity, control.Visible, true, true)
            : (control.Scale, control.Opacity, control.Visible, TouchLayout.CanHide(item.Control), Working.Controls.ContainsKey(item.Control));
    }

    // Where a control stands: an added button by its own placement, one of the game's by the layout's or the game's.
    private TouchPlacement PlacementOf(TouchLayoutItem item) =>
        item.IsExtra && item.Extra < Working.Extras.Count ? Working.Extras[item.Extra].Placement : Working.Resolve(item.Control);

    private void Place(TouchLayoutItem item, TouchPlacement placement)
    {
        if (item.IsExtra)
        {
            Working.SetExtra(item.Extra, placement);
        }
        else
        {
            Working.Set(item.Control, placement);
        }
    }

    // Puts a button on the screen: one the layout hides is shown where it stands; any other gets a second of itself in the
    // middle of the screen, to be dragged to its place.
    private void AddButton(TouchControlId kind, LayoutContext context)
    {
        PushUndo();
        Adding = false;
        TouchPlacement own = Working.Resolve(kind);
        if (!own.Visible)
        {
            Working.Set(kind, own with { Visible = true });
            Selected = TouchLayoutItem.Of(kind);
            return;
        }

        // Each a little further along than the last, so several added in a row do not hide one another.
        UiRect safe = context.SafeRect;
        float step = context.Px(70f) * (Working.Extras.Count % 5);
        UiVec2 centre = new(safe.Center.X - context.Px(140f) + step, safe.Y + (safe.Height * 0.45f));
        int index = Working.Add(kind, TouchLayout.MovedTo(new TouchPlacement(HudEdgeX.Centre, 0f, HudEdgeY.Middle, 0f), centre, context));
        Selected = index >= 0 ? TouchLayoutItem.Of(kind, index) : null;
    }

    private void Down(in TouchPoint point, LayoutContext context)
    {
        float x = point.StartX, y = point.StartY;

        // While the question is up it is all there is.
        if (Prompting)
        {
            foreach (TouchEditorControl control in _bar)
            {
                if (control.IsPrompt && control.Box.Contains(x, y))
                {
                    Press(control, point, context);
                    return;
                }
            }

            return;
        }

        foreach (TouchEditorControl control in _bar)
        {
            if (control.Box.Contains(x, y))
            {
                if (control.Enabled)
                {
                    Press(control, point, context);
                }

                return;
            }
        }

        if (BarBox.Contains(x, y) || SelectionBox.Contains(x, y))
        {
            return;
        }

        // A tap off the list of buttons puts it away, and does nothing else.
        if (Adding)
        {
            Adding = false;
            return;
        }

        // A second finger while one drags: the two pinch the dragged item's size.
        if (_drag is { } held && _pinch is null && held.Finger != point.Id)
        {
            PushUndoOnce(ref held);
            _drag = held;
            _pinch = new Pinch(point.Id, new UiVec2(x, y), MathF.Max(1f, UiVec2.Distance(new UiVec2(x, y), held.At)), Describe(held.Item).Scale);
            return;
        }

        if (_drag is not null)
        {
            return;
        }

        // The smallest item under the finger is the one meant: a button on a readout is taken before the readout.
        float slack = 6f * context.Px(1f);
        TouchEditorItem? hit = null;
        foreach (TouchEditorItem item in _items)
        {
            UiRect box = item.Box.Inflate(new Thickness(slack));
            bool under = item.Round
                ? UiVec2.Distance(item.Box.Center, new UiVec2(x, y)) <= (item.Box.Width * 0.5f) + slack
                : box.Contains(x, y);
            if (under && (hit is not { } best || (item.Box.Width * item.Box.Height) < (best.Box.Width * best.Box.Height)))
            {
                hit = item;
            }
        }

        if (hit is not { } taken)
        {
            Selected = null;
            return;
        }

        Selected = taken.Item;
        _drag = new Drag(point.Id, taken.Item, taken.Box, taken.Box, taken.Item.IsReadout ? Working.Resolve(taken.Item.Readout) : default, false, false)
        {
            At = new UiVec2(x, y),
        };
    }

    private void Move(in TouchPoint point, LayoutContext context)
    {
        if (_slider is { } slider && slider.Finger == point.Id)
        {
            Slide(slider.Widget, point.X);
            return;
        }

        if (_pinch is { } pinch && _drag is { } pinched)
        {
            if (point.Id != pinch.Finger && point.Id != pinched.Finger)
            {
                return;
            }

            // The spread of the two fingers against their spread as the second came down.
            UiVec2 other = point.Id == pinch.Finger ? pinched.At : pinch.At;
            float spread = UiVec2.Distance(new UiVec2(point.X, point.Y), other);
            if (point.Id == pinch.Finger)
            {
                pinch = pinch with { At = new UiVec2(point.X, point.Y) };
            }
            else
            {
                _drag = pinched with { At = new UiVec2(point.X, point.Y) };
            }

            _pinch = pinch;
            SetScale(pinched.Item, pinch.StartScale * spread / pinch.StartSpread);
            return;
        }

        if (_drag is not { } drag || drag.Finger != point.Id || drag.Pinched)
        {
            return;
        }

        float dx = point.X - point.StartX, dy = point.Y - point.StartY;
        if (!drag.Moved)
        {
            float slop = DragSlopUnits * context.Px(1f);
            if ((dx * dx) + (dy * dy) < slop * slop)
            {
                _drag = drag with { At = new UiVec2(point.X, point.Y) };
                return;
            }

            PushUndoOnce(ref drag);
            drag = drag with { Moved = true };
        }

        UiRect box = Settle(drag.StartBox.Offset(dx, dy), drag.Item, context);
        _drag = drag with { Box = box, At = new UiVec2(point.X, point.Y) };
        if (drag.Item.IsReadout)
        {
            float unit = HudLayoutOverrides.Unit(context.Viewport.Y);
            Working.Set(drag.Item.Readout, drag.StartReadout with
            {
                Dx = drag.StartReadout.Dx + ((box.X - drag.StartBox.X) / unit),
                Dy = drag.StartReadout.Dy + ((box.Y - drag.StartBox.Y) / unit),
            });
        }
        else
        {
            TouchPlacement moved = TouchLayout.MovedTo(PlacementOf(drag.Item), box.Center, context);
            bool caughtX = GuideX is not null, caughtY = GuideY is not null;
            Place(drag.Item, moved with
            {
                X = caughtX ? moved.X : MathF.Round(moved.X / GridUnits) * GridUnits,
                Y = caughtY ? moved.Y : MathF.Round(moved.Y / GridUnits) * GridUnits,
            });
        }
    }

    private void Up(in TouchPoint point)
    {
        if (_slider is { } slider && slider.Finger == point.Id)
        {
            _slider = null;
        }

        if (_pinch is { } pinch && (pinch.Finger == point.Id || _drag?.Finger == point.Id))
        {
            // A pinch over, the finger left on the screen does not go on to drag: it would jump from where the drag began.
            _pinch = null;
            if (_drag is { } rest)
            {
                _drag = rest.Finger == point.Id ? null : rest with { Pinched = true };
            }

            return;
        }

        if (_drag is { } drag && drag.Finger == point.Id)
        {
            _drag = null;
            GuideX = GuideY = null;
        }
    }

    // Where a dragged box comes to rest: on a guide where it is near one, and never off the screen.
    private UiRect Settle(UiRect box, TouchLayoutItem item, LayoutContext context)
    {
        UiRect safe = context.SafeRect;
        float near = GuideUnits * context.Px(1f);
        UiVec2 centre = box.Center;
        float? guideX = null, guideY = null;
        float bestX = near, bestY = near;

        void Try(UiVec2 at)
        {
            float offX = MathF.Abs(centre.X - at.X), offY = MathF.Abs(centre.Y - at.Y);
            if (offX < bestX)
            {
                bestX = offX;
                guideX = at.X;
            }

            if (offY < bestY)
            {
                bestY = offY;
                guideY = at.Y;
            }
        }

        Try(safe.Center);
        foreach (TouchEditorItem other in _items)
        {
            if (other.Item != item && !other.Hidden)
            {
                Try(other.Box.Center);
            }
        }

        float x = (guideX ?? centre.X) - (box.Width * 0.5f);
        float y = (guideY ?? centre.Y) - (box.Height * 0.5f);
        float clampedX = box.Width >= safe.Width ? safe.X + ((safe.Width - box.Width) * 0.5f) : Math.Clamp(x, safe.X, safe.Right - box.Width);
        float clampedY = box.Height >= safe.Height ? safe.Y + ((safe.Height - box.Height) * 0.5f) : Math.Clamp(y, safe.Y, safe.Bottom - box.Height);
        GuideX = guideX is not null && clampedX == x ? guideX : null;
        GuideY = guideY is not null && clampedY == y ? guideY : null;
        return new UiRect(clampedX, clampedY, box.Width, box.Height);
    }

    private void Press(TouchEditorControl control, in TouchPoint point, LayoutContext context)
    {
        switch (control.Widget)
        {
            case TouchEditorWidget.Add:
                Adding = !Adding;
                Selected = null;
                break;
            case TouchEditorWidget.PaletteItem:
                AddButton(control.Kind, context);
                break;
            case TouchEditorWidget.Slot1 or TouchEditorWidget.Slot2 or TouchEditorWidget.Slot3:
                int slot = control.Widget - TouchEditorWidget.Slot1;
                if (slot != Slot)
                {
                    // Each layout keeps its own changes until they are saved; what could be taken back was the other's.
                    Slot = slot;
                    _undo.Clear();
                    Selected = null;
                }

                break;
            case TouchEditorWidget.Undo:
                Undo();
                break;
            case TouchEditorWidget.ResetAll:
                PushUndo();
                _working[Slot] = new TouchLayout();
                break;
            case TouchEditorWidget.StickMode:
                PushUndo();
                Working.StickMode = Working.StickMode == TouchStickMode.Fixed ? TouchStickMode.Floating : TouchStickMode.Fixed;
                break;
            case TouchEditorWidget.Flip:
                BarAtBottom = !BarAtBottom;
                break;
            case TouchEditorWidget.Save:
                Save();
                break;
            case TouchEditorWidget.Exit:
                RequestExit();
                break;
            case TouchEditorWidget.Size or TouchEditorWidget.Opacity:
                PushUndo();
                _slider = (point.Id, control.Widget);
                Slide(control.Widget, point.X);
                break;
            case TouchEditorWidget.Show when Selected is { } shown:
                PushUndo();
                if (shown.IsReadout)
                {
                    HudPlacement readout = Working.Resolve(shown.Readout);
                    Working.Set(shown.Readout, readout with { Visible = !readout.Visible });
                }
                else
                {
                    TouchPlacement placed = PlacementOf(shown);
                    Place(shown, placed with { Visible = !placed.Visible });
                }

                break;
            case TouchEditorWidget.ResetItem when Selected is { } reset:
                PushUndo();
                if (reset.IsReadout)
                {
                    Working.Hud.Remove(reset.Readout);
                }
                else if (reset.IsExtra)
                {
                    // An added button has no place of the game's to go back to: it is taken away.
                    if (reset.Extra < Working.Extras.Count)
                    {
                        Working.Extras.RemoveAt(reset.Extra);
                    }

                    Selected = null;
                }
                else
                {
                    Working.Controls.Remove(reset.Control);
                }

                break;
            case TouchEditorWidget.PromptSave:
                Save();
                Close();
                break;
            case TouchEditorWidget.PromptDiscard:
                Close();
                break;
            case TouchEditorWidget.PromptKeep:
                Prompting = false;
                break;
        }
    }

    // A slider's finger at a place across the screen: the selected item's size or opacity from where that is along the track.
    private void Slide(TouchEditorWidget widget, float x)
    {
        if (Selected is not { } item)
        {
            return;
        }

        foreach (TouchEditorControl control in _bar)
        {
            if (control.Widget != widget)
            {
                continue;
            }

            (float from, float to) = SliderTrack(control.Box);
            float along = Math.Clamp((x - from) / MathF.Max(1f, to - from), 0f, 1f);
            if (widget == TouchEditorWidget.Size)
            {
                // Whole steps of five percent, so the same size can be found again.
                float scale = TouchLayout.MinScale + (along * (TouchLayout.MaxScale - TouchLayout.MinScale));
                SetScale(item, MathF.Round(scale * 20f) / 20f);
            }
            else
            {
                float opacity = TouchLayout.MinOpacity + (along * (1f - TouchLayout.MinOpacity));
                opacity = MathF.Round(opacity * 20f) / 20f;
                if (item.IsReadout)
                {
                    Working.Set(item.Readout, Working.Resolve(item.Readout) with { Opacity = opacity });
                }
                else
                {
                    Place(item, PlacementOf(item) with { Opacity = opacity });
                }
            }

            return;
        }
    }

    /// <summary>The ends of a slider's track across the screen, inside its box: where its knob runs.</summary>
    /// <param name="box">The slider's box.</param>
    /// <returns>The track's left and right ends, in pixels.</returns>
    public static (float From, float To) SliderTrack(UiRect box)
    {
        float inset = box.Height * 0.4f;
        return (box.X + inset, box.Right - inset);
    }

    private void SetScale(TouchLayoutItem item, float scale)
    {
        if (!float.IsFinite(scale))
        {
            return;
        }

        if (item.IsReadout)
        {
            Working.Set(item.Readout, Working.Resolve(item.Readout) with { Scale = scale });
        }
        else
        {
            Place(item, PlacementOf(item) with { Scale = scale });
        }
    }

    private void PushUndo()
    {
        _undo.Add(Working.Clone());
        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }
    }

    // One step back for one gesture, however a drag and a pinch run into each other.
    private void PushUndoOnce(ref Drag drag)
    {
        if (!drag.Undoable)
        {
            PushUndo();
            drag = drag with { Undoable = true };
        }
    }

    private readonly record struct Drag(
        int Finger, TouchLayoutItem Item, UiRect StartBox, UiRect Box, HudPlacement StartReadout, bool Moved, bool Pinched)
    {
        // Where the drag's finger is now, for a pinch to measure from.
        public UiVec2 At { get; init; } = StartBox.Center;

        // Whether the step back for this gesture has been kept already.
        public bool Undoable { get; init; }
    }

    private readonly record struct Pinch(int Finger, UiVec2 At, float StartSpread, float StartScale);
}

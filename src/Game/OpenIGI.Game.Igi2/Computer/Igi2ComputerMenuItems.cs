namespace OpenIGI.Game.Igi2.Computer;

/// <summary>The buttons a computer screen reads, <c>ComputerMenu_t.bCurrentButton</c>'s bits.</summary>
[Flags]
public enum Igi2ComputerButtons
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>The up arrow.</summary>
    Up = 0x1,

    /// <summary>The down arrow.</summary>
    Down = 0x2,

    /// <summary>The left arrow.</summary>
    Left = 0x4,

    /// <summary>The right arrow.</summary>
    Right = 0x8,

    /// <summary>The left mouse button or Enter, which selects.</summary>
    Select = 0x10,

    /// <summary>The right mouse button.</summary>
    Back = 0x20,

    /// <summary>The arrows.</summary>
    Arrows = Up | Down | Left | Right,
}

/// <summary>
/// What a menu item is told when an item is activated, <c>MenuItemActivateContext_s</c>: which item, and the pointer and the buttons
/// as the menu's run saw them.
/// </summary>
/// <param name="MenuItem">The item activated, nought for none.</param>
/// <param name="MouseX">The pointer's X, in the screen's pixels.</param>
/// <param name="MouseY">The pointer's Y.</param>
/// <param name="CurrentButtons">The buttons down.</param>
/// <param name="EdgeButtons">The buttons that went down this run.</param>
public readonly record struct Igi2ComputerActivation(
    int MenuItem,
    int MouseX = 0,
    int MouseY = 0,
    Igi2ComputerButtons CurrentButtons = Igi2ComputerButtons.None,
    Igi2ComputerButtons EdgeButtons = Igi2ComputerButtons.None);

/// <summary>
/// One item of a computer screen's menu, <c>MenuItem_s</c>: an id, the items the arrows lead to, whether it is lit (the item the
/// pointer or the arrows are on) and whether it was activated this run, and its hit box. A text item draws a string in one colour,
/// another when lit (<c>ComputerTextItem</c>); a button draws one of three sprites (<c>ComputerButtonItem</c>).
/// </summary>
public sealed class Igi2ComputerMenuItem
{
    /// <summary>Initializes a new instance of the <see cref="Igi2ComputerMenuItem"/> class.</summary>
    /// <param name="id">Its id, <c>eMenuItem</c>.</param>
    /// <param name="activated">Called on every activation its menu passes it (<c>pfActivateCallback</c>).</param>
    public Igi2ComputerMenuItem(int id, Action<Igi2ComputerMenuItem, Igi2ComputerActivation>? activated = null)
    {
        Id = id;
        Activated = activated;
    }

    /// <summary>Gets its id.</summary>
    public int Id { get; }

    /// <summary>Gets or sets the item the up arrow leads to, nought for none.</summary>
    public int LinkUp { get; set; }

    /// <summary>Gets or sets the item the down arrow leads to.</summary>
    public int LinkDown { get; set; }

    /// <summary>Gets or sets the item the left arrow leads to.</summary>
    public int LinkLeft { get; set; }

    /// <summary>Gets or sets the item the right arrow leads to.</summary>
    public int LinkRight { get; set; }

    /// <summary>Gets or sets a value indicating whether it cannot be activated.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>Gets or sets a value indicating whether it is lit, <c>isActive</c>.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets a value indicating whether it was activated since the last run, <c>isActivated</c>.</summary>
    public bool IsActivated { get; set; }

    /// <summary>Gets or sets its hit box's left, in the screen's pixels.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets its hit box's top.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets its hit box's width.</summary>
    public float Width { get; set; }

    /// <summary>Gets or sets its hit box's height.</summary>
    public float Height { get; set; }

    /// <summary>Gets or sets the string a text item draws, or a button draws beside its sprite.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the font it draws with.</summary>
    public string Font { get; set; } = Igi2ComputerDisplay.LabelFont;

    /// <summary>Gets or sets its colour.</summary>
    public Igi2ComputerColour Colour { get; set; }

    /// <summary>Gets or sets its colour while lit.</summary>
    public Igi2ComputerColour HighlightColour { get; set; }

    /// <summary>Gets or sets the sprites a button draws: plain, lit, and lit while pressed; <see langword="null"/> for a text item.</summary>
    public string[]? Sprites { get; set; }

    /// <summary>Gets or sets a value indicating whether a button is pressed in, which it shows while lit.</summary>
    public bool IsPressed { get; set; }

    /// <summary>Gets or sets the button its press was seen on last run (the objectives' button keeps it).</summary>
    public bool WasPressed { get; set; }

    /// <summary>Gets what it does on an activation.</summary>
    public Action<Igi2ComputerMenuItem, Igi2ComputerActivation>? Activated { get; }

    /// <summary>Whether a point lies in its hit box, edges included (<c>MenuItemQTask_RunHandler</c>'s clamp test).</summary>
    /// <param name="x">The point's X.</param>
    /// <param name="y">The point's Y.</param>
    /// <returns>Whether it does.</returns>
    public bool Contains(int x, int y)
    {
        int left = (int)X, top = (int)Y, right = (int)(Width + X), bottom = (int)(Height + Y);
        return x >= left && x <= right && y >= top && y <= bottom;
    }
}

/// <summary>
/// A computer screen's menu, <c>MenuItemQTask</c> over its items (<c>menuitemtask.c</c>, <c>menuitem.c</c>): the pointer lights the
/// item under it, the arrows move the light along the items' links past disabled ones, and every activation is passed to every
/// item, which acts on its own id.
/// </summary>
public sealed class Igi2ComputerMenuItems
{
    private readonly List<Igi2ComputerMenuItem> _items = [];
    private Igi2ComputerButtons _buttons;
    private Igi2ComputerButtons _previousButtons;
    private int _mouseX;
    private int _mouseY;
    private int _previousMouseX;
    private int _previousMouseY;
    private bool _isForced;

    /// <summary>Gets the items, in the order they were made.</summary>
    public IReadOnlyList<Igi2ComputerMenuItem> Items => _items;

    /// <summary>Adds an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The item.</returns>
    public Igi2ComputerMenuItem Add(Igi2ComputerMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return item;
    }

    /// <summary>Gets the lit item, <c>MenuItem_GetActiveMenuItem</c>, or <see langword="null"/>.</summary>
    public Igi2ComputerMenuItem? Active => _items.Find(static i => i.IsActive);

    /// <summary>Finds an item by id, <c>MenuItem_GetMenuItem</c>.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Igi2ComputerMenuItem? Find(int id) => _items.Find(i => i.Id == id);

    /// <summary>The pointer and the buttons for the next run, <c>MenuItemQTask_MenuItemInputHandler</c>: the last become the previous.</summary>
    /// <param name="mouseX">The pointer's X.</param>
    /// <param name="mouseY">The pointer's Y.</param>
    /// <param name="buttons">The buttons down.</param>
    public void SetInput(int mouseX, int mouseY, Igi2ComputerButtons buttons)
    {
        _previousMouseX = _mouseX;
        _previousMouseY = _mouseY;
        _previousButtons = _buttons;
        _mouseX = mouseX;
        _mouseY = mouseY;
        _buttons = buttons;
    }

    /// <summary>
    /// Activates an item by id, <c>MenuItem_Activate</c> through <c>MenuItemQTask_ActivateHandler</c>: the lights settle, every item
    /// hears it, and the next run passes the lit item on rather than reading the pointer.
    /// </summary>
    /// <param name="id">The id.</param>
    public void Activate(int id)
    {
        Settle();
        Pass(new Igi2ComputerActivation(id));
        _isForced = true;
    }

    /// <summary>
    /// One run, <c>MenuItemQTask_RunHandler</c>: the lights settle (an item activated stays lit). Then, unless an activation was forced
    /// since the last, the pointer moved or a button down activates the item under the pointer (every item hit, or nought for
    /// none), or the lit one when the pointer is still; otherwise the lit item is activated again.
    /// </summary>
    public void Run()
    {
        Settle();
        Igi2ComputerButtons edge = _buttons & ~_previousButtons;
        int dx = _mouseX - _previousMouseX, dy = _mouseY - _previousMouseY;
        if (!_isForced && (dx != 0 || dy != 0 || (_buttons & (Igi2ComputerButtons.Select | Igi2ComputerButtons.Back | (Igi2ComputerButtons)0x40)) != 0))
        {
            Igi2ComputerMenuItem? active = Active;
            if (dx != 0 || dy != 0 || active is null)
            {
                int hit = 0;
                foreach (Igi2ComputerMenuItem item in _items)
                {
                    if (item.Contains(_mouseX, _mouseY))
                    {
                        hit = item.Id;
                        Pass(new Igi2ComputerActivation(hit, _mouseX, _mouseY, _buttons, edge));
                    }
                }

                if (hit == 0)
                {
                    Pass(new Igi2ComputerActivation(0, _mouseX, _mouseY, _buttons, edge));
                }
            }
            else
            {
                Pass(new Igi2ComputerActivation(active.Id, _mouseX, _mouseY, _buttons, edge));
            }

            return;
        }

        _isForced = false;
        if (Active is { } lit)
        {
            Pass(new Igi2ComputerActivation(lit.Id, _mouseX, _mouseY, _buttons, edge));
        }
    }

    // MenuItem_UpdateActiveHandler on every item: activated since, it is lit.
    private void Settle()
    {
        foreach (Igi2ComputerMenuItem item in _items)
        {
            item.IsActive = item.IsActivated || item.IsActive;
            item.IsActivated = false;
        }
    }

    /// <summary>Moves the light along a link from the lit item, <c>MenuItem_Move</c>, or to the first item with none lit.</summary>
    /// <param name="link">Which link: the arrow's bit.</param>
    /// <returns>Whether it moved.</returns>
    public bool Move(Igi2ComputerButtons link)
    {
        Igi2ComputerMenuItem? from = Active;
        Igi2ComputerMenuItem? to;
        if (from is null)
        {
            to = _items.Count > 0 ? _items[0] : null;
        }
        else
        {
            to = from;
            do
            {
                int next = link switch
                {
                    Igi2ComputerButtons.Up => to.LinkUp,
                    Igi2ComputerButtons.Down => to.LinkDown,
                    Igi2ComputerButtons.Left => to.LinkLeft,
                    _ => to.LinkRight,
                };
                if (next == 0)
                {
                    return false;
                }

                to = Find(next);
                if (to is null)
                {
                    return false;
                }
            }
            while (to.IsDisabled);
        }

        if (to is null)
        {
            return false;
        }

        Igi2ComputerActivation activation = new(to.Id);
        Deliver(to, activation);
        if (from is not null)
        {
            Deliver(from, activation);
        }

        return true;
    }

    // MenuItem_eActivateQTaskEvent to every item.
    private void Pass(in Igi2ComputerActivation activation)
    {
        foreach (Igi2ComputerMenuItem item in _items.ToArray())
        {
            Deliver(item, activation);
        }
    }

    // MenuItem_ActivateMenuItemHandler: another item's activation, or its own while disabled, unlights it; its own, with an arrow
    // just pressed, moves the light along that link; otherwise it is activated. Its callback hears every activation either way.
    private void Deliver(Igi2ComputerMenuItem item, in Igi2ComputerActivation activation)
    {
        if (activation.MenuItem != item.Id || item.IsDisabled)
        {
            item.IsActive = false;
        }
        else
        {
            Igi2ComputerButtons edge = activation.EdgeButtons;
            if ((edge & Igi2ComputerButtons.Up) != 0)
            {
                Move(Igi2ComputerButtons.Up);
                return;
            }

            if ((edge & Igi2ComputerButtons.Down) != 0)
            {
                Move(Igi2ComputerButtons.Down);
                return;
            }

            if ((edge & Igi2ComputerButtons.Left) != 0)
            {
                Move(Igi2ComputerButtons.Left);
                return;
            }

            if ((edge & Igi2ComputerButtons.Right) != 0)
            {
                Move(Igi2ComputerButtons.Right);
                return;
            }

            item.IsActivated = true;
        }

        item.Activated?.Invoke(item, activation);
    }
}

/// <summary>The computer's colours, <c>ComputerMenuColor_e</c> over <c>ComputerMenu_atColor</c>.</summary>
public enum Igi2ComputerColour
{
    /// <summary>A failed objective's red.</summary>
    DarkRed = 0,

    /// <summary>The screen's own blue, which the original calls green.</summary>
    Green = 1,

    /// <summary>Its lit colour.</summary>
    LightGreen = 2,

    /// <summary>White.</summary>
    White = 3,

    /// <summary>Grey.</summary>
    LightGrey = 4,
}

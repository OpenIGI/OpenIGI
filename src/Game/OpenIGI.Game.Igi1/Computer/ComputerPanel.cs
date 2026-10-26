namespace OpenIGI.Game.Igi1.Computer;

/// <summary>
/// One of the computer's controls, a <c>MenuItem</c>: an id, the rectangle the pointer finds it by, and what it does when
/// the panel tells it which item the pointer is over.
/// </summary>
public abstract class ComputerItem
{
    /// <summary>Initializes a new instance of the <see cref="ComputerItem"/> class.</summary>
    /// <param name="id">Its id, which the panel names it by.</param>
    /// <param name="onEvent">What it does when told which item is under the pointer, or <see langword="null"/>.</param>
    protected ComputerItem(int id, Action<ComputerItem, int>? onEvent)
    {
        Id = id;
        OnEvent = onEvent;
    }

    /// <summary>Gets its id.</summary>
    public int Id { get; }

    /// <summary>Gets or sets its left edge, display pixels.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets its top edge, display pixels.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets its width, display pixels.</summary>
    public float Width { get; set; }

    /// <summary>Gets or sets its height, display pixels.</summary>
    public float Height { get; set; }

    /// <summary>Gets or sets a value indicating whether the pointer is over it, as the last commit left it (<c>selected</c>).</summary>
    public bool IsSelected { get; set; }

    /// <summary>Gets or sets a value indicating whether the panel named it this tick (<c>pressed</c>), which the next commit selects.</summary>
    public bool IsPicked { get; set; }

    /// <summary>Gets what it does when told which item is under the pointer.</summary>
    public Action<ComputerItem, int>? OnEvent { get; }
}

/// <summary>A picture button: three sprites, normal, under the pointer and held (<c>ComputerButtonItem</c>, <c>0x46B660</c>).</summary>
public sealed class ComputerButtonItem : ComputerItem
{
    /// <summary>Initializes a new instance of the <see cref="ComputerButtonItem"/> class.</summary>
    /// <param name="id">Its id.</param>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="sprites">Its normal, pointed-at and held sprites, by name in the computer archive.</param>
    /// <param name="size">The normal sprite's size, display pixels, which its rectangle is.</param>
    /// <param name="onEvent">What it does.</param>
    public ComputerButtonItem(int id, float x, float y, ComputerSprites sprites, (float Width, float Height) size, Action<ComputerItem, int>? onEvent)
        : base(id, onEvent)
    {
        Sprites = sprites;
        X = x;
        Y = y;
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>Gets its sprites.</summary>
    public ComputerSprites Sprites { get; }

    /// <summary>Gets or sets a value indicating whether the left button is down on it (<c>pressed</c> of the button).</summary>
    public bool IsDown { get; set; }

    /// <summary>Gets or sets whether it was down the tick before.</summary>
    public bool WasDown { get; set; }

    /// <summary>Gets the sprite it is drawn with (<c>0x46B5C0</c>): held when pointed at and down, else pointed at, else normal.</summary>
    public string Sprite => IsSelected && IsDown ? Sprites.Down : IsSelected ? Sprites.Hover : Sprites.Normal;
}

/// <summary>A line of text that is a link (<c>ComputerTextItem</c>, <c>0x46B970</c>).</summary>
public sealed class ComputerTextItem : ComputerItem
{
    /// <summary>Initializes a new instance of the <see cref="ComputerTextItem"/> class.</summary>
    /// <param name="id">Its id.</param>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="font">Its font, by name in the computer archive.</param>
    /// <param name="text">Its text, as drawn.</param>
    /// <param name="size">The text's size, display pixels.</param>
    /// <param name="color">Its colour, an index of <see cref="ComputerColors.Items"/>.</param>
    /// <param name="onEvent">What it does.</param>
    public ComputerTextItem(int id, float x, float y, string font, string text, (float Width, float Height) size, int color, Action<ComputerItem, int>? onEvent)
        : base(id, onEvent)
    {
        Font = font;
        Text = text;
        Color = color;
        X = x;
        Y = y;
        Width = size.Width;
        Height = size.Height;
        DrawColor = ComputerColors.Items[color];
    }

    /// <summary>Gets its font.</summary>
    public string Font { get; }

    /// <summary>Gets its text.</summary>
    public string Text { get; }

    /// <summary>Gets its colour index.</summary>
    public int Color { get; }

    /// <summary>
    /// Gets the colour it is drawn in, 0 to 128 a channel. <c>0x46B890</c> draws the text and only then sets its colour for
    /// the next frame, so this is what the last draw left.
    /// </summary>
    public (float R, float G, float B) DrawColor { get; private set; }

    /// <summary>Draws it: returns the colour drawn with and sets the next one, half way to white while pointed at.</summary>
    /// <returns>The colour this draw uses.</returns>
    public (float R, float G, float B) Draw()
    {
        (float R, float G, float B) drawn = DrawColor;
        (float r, float g, float b) = ComputerColors.Items[Color];
        DrawColor = IsSelected ? ((r * 0.5f) + 64f, (g * 0.5f) + 64f, (b * 0.5f) + 64f) : (r, g, b);
        return drawn;
    }
}

/// <summary>A button's three sprites.</summary>
/// <param name="Normal">Not pointed at.</param>
/// <param name="Hover">Pointed at.</param>
/// <param name="Down">Pointed at and held.</param>
public readonly record struct ComputerSprites(string Normal, string Hover, string Down);

/// <summary>
/// A screen's controls and the pointer over them, <c>MenuItemTask</c> (<c>0x504EF0</c>): each tick the item under the pointer
/// is told it is picked, every other one that it is not, and the pick becomes the selection a tick later.
/// </summary>
public sealed class ComputerPanel
{
    private readonly List<ComputerItem> _items = [];
    private int _x;
    private int _y;
    private int _oldX;
    private int _oldY;

    /// <summary>Gets the items, in the order they were made.</summary>
    public IReadOnlyList<ComputerItem> Items => _items;

    /// <summary>Adds an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The item.</returns>
    public T Add<T>(T item)
        where T : ComputerItem
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return item;
    }

    /// <summary>Gets the selected item, the last of them (<c>0x505A30</c>), or <see langword="null"/>.</summary>
    public ComputerItem? Selected => _items.LastOrDefault(static item => item.IsSelected);

    /// <summary>Hands it the pointer for the coming run (<c>0x5053B0</c>).</summary>
    /// <param name="x">The pointer across, whole display pixels.</param>
    /// <param name="y">The pointer down.</param>
    public void SetPointer(int x, int y)
    {
        _oldX = _x;
        _oldY = _y;
        _x = x;
        _y = y;
    }

    /// <summary>Runs it a tick: commit the last pick, then tell the items which one the pointer is over.</summary>
    public void Run()
    {
        // MenuItem_Commit (0x505710): a pick becomes the selection.
        foreach (ComputerItem item in _items)
        {
            item.IsSelected = item.IsPicked || item.IsSelected;
            item.IsPicked = false;
        }

        // The pointer has not moved and no key is held (the computer sends none): the selection keeps itself.
        if (_x == _oldX && _y == _oldY)
        {
            if (Selected is { } selected)
            {
                Select(selected.Id);
            }

            return;
        }

        bool found = false;
        foreach (ComputerItem item in _items.ToArray())
        {
            if (Contains(item))
            {
                found = true;
                Select(item.Id);
            }
        }

        if (!found)
        {
            Select(0);
        }
    }

    // A rectangle holds the pointer from its left to its right, edges included, each made whole as the chopping FPU does.
    private bool Contains(ComputerItem item)
    {
        int left = (int)item.X;
        int top = (int)item.Y;
        int right = (int)((double)item.Width + item.X);
        int bottom = (int)((double)item.Height + item.Y);
        return _x >= left && _x <= right && _y >= top && _y <= bottom;
    }

    // MenuItem_Select (0x5057C0), sent to every item: the one named is picked, every other loses the selection; each acts.
    private void Select(int id)
    {
        foreach (ComputerItem item in _items.ToArray())
        {
            if (item.Id == id)
            {
                item.IsPicked = true;
            }
            else
            {
                item.IsSelected = false;
            }

            item.OnEvent?.Invoke(item, id);
        }
    }
}

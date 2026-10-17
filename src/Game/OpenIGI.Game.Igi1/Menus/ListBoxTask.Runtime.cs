
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// The horizontal spinner: an arrow at each end and one item's label between them.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>ListBox_Update</c> (<c>0x41EC70</c>) and <c>ListBox_Render</c>
/// (<c>0x41ED70</c>). It is not a drop-down: left and right cycle through the items and only the
/// current one is on screen, which is why it fits on a line beside a label like a slider does.
/// </para>
/// <para>
/// Two values are kept, and confusing them would break the graphics screen. <see cref="Index"/> is
/// the position in the list and wraps at both ends; <see cref="ActiveId"/> is the <em>id</em> stored
/// in the item, which is what the script reads — <c>Config_FillScreenResolutionListBox(
/// ListBox_951.nActiveID)</c> passes the device's id, not its position.
/// </para>
/// <para>
/// The items themselves come from <c>Collect Items Script</c>, which calls a <c>Config_*</c> native
/// to fill the list. Those natives do not exist yet, so a retail list box loads empty and draws its
/// box and arrows and nothing between them — which is the truthful result, not a placeholder.
/// </para>
/// </remarks>
public partial class ListBoxTask
{
    private readonly List<MenuListItem> _items = [];

    /// <summary>Initializes a new instance of the <see cref="ListBoxTask"/> class.</summary>
    /// <remarks>
    /// <c>ListBox_Construct</c> (<c>0x41E8C0</c>) defaults the label field to 100 pixels and sets both
    /// centre flags. The flags are not cosmetic: a list box is placed by its frame's line-up, and the
    /// graphics screen's frames are <c>HorizontalFillOut</c>, which assigns the horizontal position
    /// and leaves the vertical one to the child's own centring. Without them every list box on that
    /// screen sits at the top of the display.
    /// </remarks>
    public ListBoxTask()
    {
        Width = DefaultWidth;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>The label field's width when the script does not give one.</summary>
    public const int DefaultWidth = 100;

    /// <summary>The characters appended to a label too wide for the box.</summary>
    /// <remarks><c>ListBox_Render</c> chops the label down and then <c>strcat</c>s this.</remarks>
    public const string Ellipsis = "...";

    /// <summary>Gets the left arrow, which steps back through the list.</summary>
    /// <remarks>Field <c>+212</c>. As with a slider's, it is owned rather than a child.</remarks>
    public MenuButtonTask LeftButton { get; } = new();

    /// <summary>Gets the right arrow, which steps forward.</summary>
    /// <remarks>Field <c>+216</c>.</remarks>
    public MenuButtonTask RightButton { get; } = new();

    /// <inheritdoc/>
    public override IEnumerable<MenuWindowTask> OwnedWindows => [LeftButton, RightButton];

    /// <summary>Gets the items currently in the list.</summary>
    /// <remarks>
    /// The original holds up to 64 of them inline at <c>+224</c>, 44 bytes each — a four-byte id
    /// followed by a forty-character label.
    /// </remarks>
    public IReadOnlyList<MenuListItem> Items => _items;

    /// <summary>Gets or sets the position in the list, which wraps at both ends.</summary>
    /// <remarks>Field <c>+3044</c>.</remarks>
    public int Index { get; set; }

    /// <summary>Gets the label the box is currently showing, or an empty string.</summary>
    public string ActiveLabel =>
        Index >= 0 && Index < _items.Count ? _items[Index].Label : string.Empty;

    /// <summary>Replaces the list's contents.</summary>
    /// <param name="items">The items, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public void SetItems(IEnumerable<MenuListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
        Index = Math.Clamp(Index, 0, Math.Max(0, _items.Count - 1));
        ActiveId = ActiveIdAt(Index);
    }

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        ApplyPendingCommand();
        base.OnUpdate();
    }

    /// <summary>Steps the list on the key the manager posted.</summary>
    /// <remarks>
    /// <para>
    /// The wrap is asymmetric in the original and is kept that way: stepping back decrements and then
    /// tests for <c>-1</c>, while stepping forward uses <c>(index + 1) % count</c>.
    /// </para>
    /// <para>
    /// Unlike a slider or a switch, a list box <em>clears</em> its pending command — and clears it
    /// whether or not it moved, before deciding whether anything changed.
    /// </para>
    /// </remarks>
    public void ApplyPendingCommand()
    {
        int before = ActiveId;

        if (_items.Count > 0)
        {
            if (LeftButton.WasClicked || PendingCommand == MenuWindowCommand.Left)
            {
                Index--;
                if (Index == -1)
                {
                    Index = _items.Count - 1;
                }
            }
            else if (RightButton.WasClicked || PendingCommand == MenuWindowCommand.Right)
            {
                Index = (Index + 1) % _items.Count;
            }

            ActiveId = ActiveIdAt(Index);
        }

        PendingCommand = MenuWindowCommand.None;

        if (ActiveId != before)
        {
            Manager?.PlaySound(MenuManagerTask.MoveSoundName);
            Run(ModifyDataScript);
        }
    }

    /// <summary>Places the two arrows around the label field.</summary>
    /// <returns>The left edge of the label field.</returns>
    /// <remarks>
    /// <c>ListBox_Render</c>: the left arrow two pixels in, and the right arrow past the arrow's own
    /// width plus the <c>Width</c> parameter plus two. Note that <c>Width</c> is the label field, not
    /// the whole control.
    /// </remarks>
    public int PlaceParts()
    {
        int x = LayoutX + 2;
        int y = LayoutY + 2;

        LeftButton.LayoutX = x;
        LeftButton.LayoutY = y;

        RightButton.LayoutX = x + LeftButton.LayoutWidth + Width + 2;
        RightButton.LayoutY = y;

        return x + LeftButton.LayoutWidth;
    }

    /// <summary>Shortens a label until it fits, then marks it with an ellipsis.</summary>
    /// <param name="label">The label to fit.</param>
    /// <param name="measure">Measures a candidate's width in pixels.</param>
    /// <returns>The label to draw.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A label that already fits in <c>Width - 4</c> is drawn untouched. Otherwise characters come off
    /// the end one at a time until what is left fits in <c>Width - 12</c> — the tighter budget leaves
    /// room for the ellipsis, which is appended without being measured. Dropping to nothing is
    /// allowed: the loop stops at the empty string and the result is just the ellipsis.
    /// </remarks>
    public string Fit(string label, Func<string, int> measure)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(measure);

        if (measure(label) <= Width - 4)
        {
            return label;
        }

        int length = label.Length;
        while (length > 0 && measure(label[..length]) > Width - 12)
        {
            length--;
        }

        return label[..length] + Ellipsis;
    }

    /// <summary>Names the two arrows' sprites.</summary>
    /// <remarks>
    /// <c>ListBox_Prepare</c> (<c>0x41EA70</c>), the same <c>_1</c>/<c>_2</c> naming a slider uses —
    /// but only two, because a list box has no carret.
    /// </remarks>
    protected override void OnPrepare()
    {
        if (SpriteResource.Length > 0)
        {
            LeftButton.SpriteResource = $"{SpriteResource}_1.spr";
            RightButton.SpriteResource = $"{SpriteResource}_2.spr";
        }
    }

    /// <summary>Sizes the control from its two arrows and its label field.</summary>
    /// <remarks>
    /// The tail of <c>ListBox_Prepare</c>. Unlike a slider's, this has no quirk: the height is always
    /// the taller arrow plus four, because there is no carret to guard the branch.
    /// </remarks>
    public void MeasureFromParts()
    {
        LayoutWidth = RightButton.LayoutWidth + Width + LeftButton.LayoutWidth + 6;
        LayoutHeight = Math.Max(LeftButton.LayoutHeight, RightButton.LayoutHeight) + 4;
    }

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        Run(CollectItemsScript);

        if (GetDataScript.Length > 0)
        {
            ActiveId = (int)Evaluate(GetDataScript);
            Index = Math.Max(0, IndexOfId(ActiveId));
        }
    }

    /// <inheritdoc/>
    protected override void OnSaveData() => Run(SetDataScript);

    private int IndexOfId(int id)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int ActiveIdAt(int index) => index >= 0 && index < _items.Count ? _items[index].Id : ActiveId;
}

/// <summary>One entry in a list box.</summary>
/// <param name="Id">The value the script reads back as <c>nActiveID</c>.</param>
/// <param name="Label">The text shown when this entry is current.</param>
/// <remarks>
/// The original stores these inline, 44 bytes each: the id and then a forty-character label. The id
/// is not the position — a resolution list's ids are the engine's device numbers.
/// </remarks>
public readonly record struct MenuListItem(int Id, string Label);

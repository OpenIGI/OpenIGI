namespace OpenIGI.Game.Menus;

/// <summary>
/// The vertical list: several entries visible at once, with the selection moving through them.
/// </summary>
/// <remarks>
/// Shares its item model with <see cref="ListBoxTask"/> — both are filled by a
/// <c>Collect Items Script</c> and both report the selected entry's id as <c>nActiveID</c> — but this
/// one shows the whole list and scrolls, where a list box shows one entry between two arrows.
/// </remarks>
public partial class ScrollListBoxTask
{
    private readonly List<MenuListItem> _items = [];

    /// <summary>Gets the items currently in the list.</summary>
    public IReadOnlyList<MenuListItem> Items => _items;

    /// <summary>Gets or sets the position in the list.</summary>
    public int Index { get; set; }

    /// <summary>Replaces the list's contents.</summary>
    /// <param name="items">The items, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public void SetItems(IEnumerable<MenuListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
        Index = Math.Clamp(Index, 0, Math.Max(0, _items.Count - 1));

        if (Index < _items.Count)
        {
            ActiveId = _items[Index].Id;
        }
    }

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        Run(CollectItemsScript);

        if (GetDataScript.Length > 0)
        {
            ActiveId = (int)Evaluate(GetDataScript);
        }
    }

    /// <inheritdoc/>
    protected override void OnSaveData() => Run(SetDataScript);
}

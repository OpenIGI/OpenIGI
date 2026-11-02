namespace OpenIGI.Editor.Core;

/// <summary>
/// What the user has selected: an ordered set with a primary item, shared by every panel so that picking in
/// the viewport, clicking in the outliner and the inspector's target are one fact.
/// </summary>
/// <typeparam name="T">The kind of item selected.</typeparam>
public sealed class Selection<T>
    where T : class
{
    private readonly List<T> _items = [];

    /// <summary>Raised after the selection changes.</summary>
    public event Action? Changed;

    /// <summary>Gets the selected items, in the order they were added.</summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>Gets the primary item: the one most recently added, which the inspector and gizmo follow.</summary>
    public T? Primary => _items.Count > 0 ? _items[^1] : null;

    /// <summary>Gets how many items are selected.</summary>
    public int Count => _items.Count;

    /// <summary>Gets whether an item is selected.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(T item) => _items.Contains(item);

    /// <summary>Replaces the selection with one item, or clears it.</summary>
    /// <param name="item">The item, or <see langword="null"/> to clear.</param>
    public void Set(T? item) => Set(item is null ? [] : [item]);

    /// <summary>Replaces the selection.</summary>
    /// <param name="items">The new items; the last becomes primary.</param>
    public void Set(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        T[] next = [.. items.Distinct()];
        if (next.SequenceEqual(_items))
        {
            return;
        }

        _items.Clear();
        _items.AddRange(next);
        Changed?.Invoke();
    }

    /// <summary>Adds an item, making it primary.</summary>
    /// <param name="item">The item.</param>
    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Remove(item);
        _items.Add(item);
        Changed?.Invoke();
    }

    /// <summary>Removes an item.</summary>
    /// <param name="item">The item.</param>
    public void Remove(T item)
    {
        if (_items.Remove(item))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Adds an item that is not selected or removes one that is: a Ctrl+click.</summary>
    /// <param name="item">The item.</param>
    public void Toggle(T item)
    {
        if (Contains(item))
        {
            Remove(item);
        }
        else
        {
            Add(item);
        }
    }

    /// <summary>Drops every item the predicate rejects, as after a delete.</summary>
    /// <param name="keep">Whether an item still exists.</param>
    public void Prune(Func<T, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        if (_items.RemoveAll(item => !keep(item)) > 0)
        {
            Changed?.Invoke();
        }
    }
}

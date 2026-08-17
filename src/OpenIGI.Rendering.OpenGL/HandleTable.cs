namespace OpenIGI.Rendering.OpenGL;

/// <summary>A registry of device objects addressed by their handle ids.</summary>
/// <typeparam name="T">The per-object record.</typeparam>
/// <remarks>
/// <para>
/// Replaces <c>Dictionary&lt;uint, T&gt;</c> on the per-draw paths. Handle ids are handed out by an
/// incrementing counter and never recycled, so the id itself can index an array — and the draw loop
/// resolves a mesh and up to two textures per call, thousands of times a frame, where the
/// dictionary's hash-and-probe was a measurable slice of the frame next to a bounds-checked load.
/// </para>
/// <para>
/// Slots are never reused; the arrays only grow. That is the trade the incrementing counter already
/// made, and it is a few megabytes an hour at the rates handles are actually created.
/// </para>
/// </remarks>
internal sealed class HandleTable<T>
    where T : struct
{
    private T[] _items = new T[256];
    private bool[] _live = new bool[256];
    private int _count;

    /// <summary>Gets how many live entries the table holds.</summary>
    public int Count => _count;

    /// <summary>Gets an enumeration of the live entries' values.</summary>
    public ValueEnumerable Values => new(this);

    /// <summary>Sets the entry for an id, adding it when absent.</summary>
    /// <param name="id">The handle id.</param>
    public T this[uint id]
    {
        set
        {
            if (id >= (uint)_items.Length)
            {
                Grow(id);
            }

            if (!_live[id])
            {
                _live[id] = true;
                _count++;
            }

            _items[id] = value;
        }
    }

    /// <summary>Looks up an entry by id.</summary>
    /// <param name="id">The handle id.</param>
    /// <param name="value">Receives the entry when it is live.</param>
    /// <returns><see langword="true"/> when the id names a live entry.</returns>
    public bool TryGetValue(uint id, out T value)
    {
        bool[] live = _live;
        if (id < (uint)live.Length && live[id])
        {
            value = _items[id];
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Removes an entry.</summary>
    /// <param name="id">The handle id.</param>
    /// <returns><see langword="true"/> when the id named a live entry.</returns>
    public bool Remove(uint id) => Remove(id, out _);

    /// <summary>Removes an entry, returning what it held.</summary>
    /// <param name="id">The handle id.</param>
    /// <param name="value">Receives the entry when it was live.</param>
    /// <returns><see langword="true"/> when the id named a live entry.</returns>
    public bool Remove(uint id, out T value)
    {
        if (id < (uint)_live.Length && _live[id])
        {
            value = _items[id];
            _items[id] = default;
            _live[id] = false;
            _count--;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        Array.Clear(_items);
        Array.Clear(_live);
        _count = 0;
    }

    /// <summary>Gets an enumerator over the live entries as <c>(id, value)</c> pairs.</summary>
    public Enumerator GetEnumerator() => new(this);

    private void Grow(uint id)
    {
        int length = _items.Length;
        while (id >= (uint)length)
        {
            length *= 2;
        }

        Array.Resize(ref _items, length);
        Array.Resize(ref _live, length);
    }

    /// <summary>Enumerates live entries as <c>(id, value)</c> pairs.</summary>
    public struct Enumerator(HandleTable<T> table)
    {
        private int _index;

        public (uint Id, T Value) Current { get; private set; }

        public bool MoveNext()
        {
            bool[] live = table._live;
            for (int i = _index; i < live.Length; i++)
            {
                if (live[i])
                {
                    Current = ((uint)i, table._items[i]);
                    _index = i + 1;
                    return true;
                }
            }

            _index = live.Length;
            return false;
        }
    }

    /// <summary>The live entries' values, enumerable without allocating.</summary>
    public readonly struct ValueEnumerable(HandleTable<T> table)
    {
        public ValueEnumerator GetEnumerator() => new(table);
    }

    /// <summary>Enumerates live entries' values.</summary>
    public struct ValueEnumerator(HandleTable<T> table)
    {
        private int _index;

        public T Current { get; private set; }

        public bool MoveNext()
        {
            bool[] live = table._live;
            for (int i = _index; i < live.Length; i++)
            {
                if (live[i])
                {
                    Current = table._items[i];
                    _index = i + 1;
                    return true;
                }
            }

            _index = live.Length;
            return false;
        }
    }
}

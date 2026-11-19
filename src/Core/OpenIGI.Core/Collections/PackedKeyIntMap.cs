namespace OpenIGI.Core.Collections;

/// <summary>
/// A map from packed <see cref="long"/> keys to <see cref="int"/> values, for a lookup that runs thousands of times a frame
/// and is emptied between frames.
/// </summary>
/// <remarks>
/// <para>
/// An open-addressed table probed in a straight line, hashed by <see cref="PackedKeyComparer.Mix"/>. A
/// <see cref="Dictionary{TKey, TValue}"/> with that comparer reaches the same entries through an interface call per hash and
/// per compare and a remainder per bucket, and the browser's ahead-of-time compiler leaves all three slow.
/// </para>
/// <para>
/// <see cref="Clear"/> is constant time: every slot carries the generation it was written in, and clearing starts a new one,
/// so a frame's map is emptied without touching the table.
/// </para>
/// <para>Not generic on purpose: a generic type over a struct value can fall to the browser's interpreter.</para>
/// </remarks>
public sealed class PackedKeyIntMap
{
    private long[] _keys;
    private int[] _values;
    private int[] _generations;
    private int _generation = 1;
    private int _shift;

    /// <summary>Creates an empty map.</summary>
    /// <param name="capacity">How many entries it holds before it first grows.</param>
    public PackedKeyIntMap(int capacity = 16)
    {
        int slots = 16;
        while (slots < capacity * 2)
        {
            slots <<= 1;
        }

        _keys = new long[slots];
        _values = new int[slots];
        _generations = new int[slots];
        _shift = 64 - System.Numerics.BitOperations.Log2((uint)slots);
    }

    /// <summary>Gets how many entries the map holds.</summary>
    public int Count { get; private set; }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        Count = 0;
        if (++_generation == int.MaxValue)
        {
            Array.Clear(_generations);
            _generation = 1;
        }
    }

    /// <summary>Looks a key up.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its value, or zero when it is not in the map.</param>
    /// <returns>Whether the key is in the map.</returns>
    public bool TryGetValue(long key, out int value)
    {
        int mask = _keys.Length - 1;
        for (int slot = SlotOf(key); _generations[slot] == _generation; slot = (slot + 1) & mask)
        {
            if (_keys[slot] == key)
            {
                value = _values[slot];
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Sets a key's value, adding the key when it is not in the map.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set(long key, int value)
    {
        if ((Count + 1) * 2 > _keys.Length)
        {
            Grow();
        }

        int mask = _keys.Length - 1;
        int slot = SlotOf(key);
        for (; _generations[slot] == _generation; slot = (slot + 1) & mask)
        {
            if (_keys[slot] == key)
            {
                _values[slot] = value;
                return;
            }
        }

        _generations[slot] = _generation;
        _keys[slot] = key;
        _values[slot] = value;
        Count++;
    }

    private int SlotOf(long key) => (int)(PackedKeyComparer.Mix(key) >> _shift);

    private void Grow()
    {
        long[] keys = _keys;
        int[] values = _values;
        int[] generations = _generations;
        int live = _generation;

        _keys = new long[keys.Length * 2];
        _values = new int[keys.Length * 2];
        _generations = new int[keys.Length * 2];
        _generation = 1;
        _shift--;
        Count = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            if (generations[i] == live)
            {
                Set(keys[i], values[i]);
            }
        }
    }
}

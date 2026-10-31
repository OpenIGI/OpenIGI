namespace OpenIGI.Net;

/// <summary>A ring of entries indexed by a wrapping 16-bit sequence, each remembering which sequence it holds.</summary>
/// <typeparam name="T">What each entry carries.</typeparam>
/// <remarks>
/// An entry answers only for the sequence it was last written for, so a lookup of an old sequence whose slot has since
/// been reused misses rather than returning the newer entry.
/// </remarks>
public sealed class SequenceBuffer<T>
{
    private const uint Empty = uint.MaxValue;

    private readonly T[] _entries;
    private readonly uint[] _sequences;

    /// <summary>Initializes a new instance of the <see cref="SequenceBuffer{T}"/> class.</summary>
    /// <param name="size">How many sequences it holds at once; a divisor of 65536 so the ring stays aligned.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> does not divide 65536.</exception>
    public SequenceBuffer(int size)
    {
        if (size <= 0 || size > 65536 || 65536 % size != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size must divide 65536.");
        }

        _entries = new T[size];
        _sequences = new uint[size];
        Array.Fill(_sequences, Empty);
    }

    /// <summary>Gets how many sequences the ring holds at once.</summary>
    public int Size => _entries.Length;

    /// <summary>Stores an entry for a sequence, replacing whatever held its slot.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="value">The entry.</param>
    public void Insert(ushort sequence, T value)
    {
        int index = sequence % _entries.Length;
        _sequences[index] = sequence;
        _entries[index] = value;
    }

    /// <summary>Forgets the entry for a sequence, if the ring still holds it.</summary>
    /// <param name="sequence">The sequence.</param>
    public void Remove(ushort sequence)
    {
        int index = sequence % _entries.Length;
        if (_sequences[index] == sequence)
        {
            _sequences[index] = Empty;
            _entries[index] = default!;
        }
    }

    /// <summary>Gets whether the ring holds an entry for a sequence.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <returns><see langword="true"/> when its slot was last written for this sequence.</returns>
    public bool Contains(ushort sequence) => _sequences[sequence % _entries.Length] == sequence;

    /// <summary>Gets the entry for a sequence.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="value">The entry, or the default when the ring does not hold it.</param>
    /// <returns><see langword="true"/> when the ring holds it.</returns>
    public bool TryGet(ushort sequence, out T value)
    {
        int index = sequence % _entries.Length;
        if (_sequences[index] == sequence)
        {
            value = _entries[index];
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Forgets every entry.</summary>
    public void Clear()
    {
        Array.Fill(_sequences, Empty);
        Array.Clear(_entries);
    }
}

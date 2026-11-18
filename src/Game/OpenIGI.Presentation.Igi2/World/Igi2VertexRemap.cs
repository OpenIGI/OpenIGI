namespace OpenIGI.Presentation.Igi2.World;

/// <summary>Which compacted vertex a mesh vertex has become in the chunk being built: a map from vertex index to chunk index.</summary>
/// <remarks>
/// <para>
/// The terrain streams every pass of every tile through one of these as the ground is tessellated, and asks it once for every
/// corner of every triangle. A <see cref="Dictionary{TKey, TValue}"/> did that job: hashing each index, and wiped clean for every
/// chunk -- in the browser, a twentieth of the frame. The indices are dense, so an array indexed by them answers directly.
/// </para>
/// <para>
/// Clearing does not touch the array: each entry carries the generation it was written in, and a clear starts a new one.
/// </para>
/// </remarks>
internal sealed class Igi2VertexRemap
{
    private int[] _written = [];
    private ushort[] _mapped = [];
    private int _generation = 1;

    /// <summary>Forgets every vertex mapped so far.</summary>
    public void Clear()
    {
        if (_generation == int.MaxValue)
        {
            Array.Clear(_written);
            _generation = 0;
        }

        _generation++;
    }

    /// <summary>Finds what a vertex was mapped to since the last clear.</summary>
    /// <param name="index">The vertex's index in the mesh.</param>
    /// <param name="mapped">Its index in the chunk, when it has one.</param>
    /// <returns>Whether it was mapped.</returns>
    public bool TryGetValue(int index, out ushort mapped)
    {
        if ((uint)index < (uint)_written.Length && _written[index] == _generation)
        {
            mapped = _mapped[index];
            return true;
        }

        mapped = 0;
        return false;
    }

    /// <summary>Maps a vertex.</summary>
    /// <param name="index">The vertex's index in the mesh, not negative.</param>
    /// <param name="mapped">Its index in the chunk.</param>
    public void Set(int index, ushort mapped)
    {
        if (index >= _written.Length)
        {
            int size = Math.Max(index + 1, _written.Length * 2);
            Array.Resize(ref _written, size);
            Array.Resize(ref _mapped, size);
        }

        _written[index] = _generation;
        _mapped[index] = mapped;
    }
}

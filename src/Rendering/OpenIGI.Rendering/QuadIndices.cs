namespace OpenIGI.Rendering;

/// <summary>The index buffer of a run of independent quads: quad <c>n</c> is vertices <c>4n</c> to <c>4n + 3</c>.</summary>
public static class QuadIndices
{
    /// <summary>Builds the two triangles of every quad, <c>(0, 1, 2)</c> and <c>(0, 2, 3)</c>.</summary>
    /// <param name="quads">How many quads; at most 16384, the most a 16-bit index reaches.</param>
    /// <returns>Six indices a quad.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quads"/> is negative or past what 16-bit indices reach.</exception>
    public static ushort[] Build(int quads)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quads);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quads, (ushort.MaxValue + 1) / 4);
        ushort[] indices = new ushort[quads * 6];
        for (int quad = 0; quad < quads; quad++)
        {
            int v = quad * 4;
            int i = quad * 6;
            indices[i] = (ushort)v;
            indices[i + 1] = (ushort)(v + 1);
            indices[i + 2] = (ushort)(v + 2);
            indices[i + 3] = (ushort)v;
            indices[i + 4] = (ushort)(v + 2);
            indices[i + 5] = (ushort)(v + 3);
        }

        return indices;
    }
}

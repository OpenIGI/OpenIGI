using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>The textures the subsets of the last few meshes drawn resolve to, so a draw does not look each one up by name.</summary>
/// <remarks>
/// A squad draws the same body and the same few weapons over and over, and each subset's texture was a lookup by string
/// every time -- a measurable share of a browser frame. The answer depends only on the mesh, the model instance naming
/// its materials, the pack holding the names and the cache the textures came from, each compared by identity, and on the
/// cache not having been cleared since.
/// </remarks>
internal sealed class SubsetTextureMemo
{
    private const int Size = 8;
    private readonly Entry[] _entries = new Entry[Size];
    private int _next;

    /// <summary>Gets each subset's texture, <see cref="TextureHandle.None"/> where a subset names none.</summary>
    /// <param name="geometry">The mesh.</param>
    /// <param name="instance">The model instance naming its materials, if it has one.</param>
    /// <param name="pack">The pack the names are read from, if there is one.</param>
    /// <param name="cache">The cache the textures load through.</param>
    /// <returns>One handle per subset, in subset order; shared, so not to be written.</returns>
    public TextureHandle[] For(MeshGeometry geometry, MtpInstance? instance, LoadedPack? pack, TexTextureCache cache)
    {
        for (int i = 0; i < Size; i++)
        {
            ref Entry entry = ref _entries[i];
            if (ReferenceEquals(entry.Geometry, geometry)
                && Same(entry.Instance, instance)
                && ReferenceEquals(entry.Pack, pack)
                && ReferenceEquals(entry.Cache, cache)
                && entry.Generation == cache.Generation)
            {
                return entry.Textures!;
            }
        }

        var textures = new TextureHandle[geometry.Subsets.Length];
        for (int s = 0; s < textures.Length; s++)
        {
            MeshSubset subset = geometry.Subsets[s];
            textures[s] = subset.HasMaterial
                && instance is { } named
                && pack is not null
                && pack.TryGetTextureName(named, subset.MaterialSlot, out string texture)
                    ? cache.Get(texture)
                    : TextureHandle.None;
        }

        _entries[_next] = new Entry(geometry, instance, pack, cache, cache.Generation, textures);
        _next = (_next + 1) % Size;
        return textures;
    }

    // An instance is a value: the same one has the same name string, model and material table.
    private static bool Same(MtpInstance? left, MtpInstance? right) =>
        left is { } a && right is { } b
            ? ReferenceEquals(a.Name, b.Name) && a.Model == b.Model && a.MaterialIndices.Equals(b.MaterialIndices)
            : left is null && right is null;

    private record struct Entry(
        MeshGeometry? Geometry,
        MtpInstance? Instance,
        LoadedPack? Pack,
        TexTextureCache? Cache,
        int Generation,
        TextureHandle[]? Textures);
}

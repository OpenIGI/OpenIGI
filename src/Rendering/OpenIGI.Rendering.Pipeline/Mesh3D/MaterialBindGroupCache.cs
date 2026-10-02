using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// Hands out the bind group that binds a draw's diffuse, lightmap and detail textures, one per distinct
/// triple, kept while all three live.
/// </summary>
/// <remarks>
/// <para>
/// The world shader samples several textures, so a draw binds one group that points at all of them. A
/// model's records pair the same textures over and over, so the group for a triple is built once and
/// kept, keyed by the three handle identifiers together. When any one texture is destroyed, every group
/// that named it goes.
/// </para>
/// <para>
/// There is no pool to run out of here. The Vulkan cache had to chain descriptor pools because a level
/// needs one set per distinct triple it draws and a lightmap is per placement rather than per material,
/// so the count scales with the number of objects in the level and one fixed pool is a ceiling a large
/// enough level walks into — fatally, in the middle of drawing the world. Where the storage comes from
/// is the backend's business now, and the Vulkan one grows its pools.
/// </para>
/// </remarks>
/// <param name="device">The device the groups are made on.</param>
/// <param name="layout">The shape they are built against.</param>
/// <param name="retired">
/// Where a resource goes when it is released. Nothing here may be destroyed where it is released: a
/// frame already recorded may still name it, and destroying it invalidates that command buffer and
/// silently voids every command recorded into it afterwards. Null frees at once, which is only right
/// where nothing is in flight.
/// </param>
public sealed class MaterialBindGroupCache(
    IRhiDevice device,
    IRhiBindGroupLayout layout,
    RetiredResources? retired = null) : IDisposable
{
    private readonly IRhiDevice _device = device ?? throw new ArgumentNullException(nameof(device));
    private readonly IRhiBindGroupLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly Dictionary<MaterialKey, IRhiBindGroup> _groups = [];

    // Every key each texture appears in, so forgetting one texture finds its groups without walking all
    // of them. A level's release forgets every texture it had, and the walk made that quadratic in the
    // size of the level -- paid on the render thread by every menu backdrop swap.
    private readonly Dictionary<uint, List<MaterialKey>> _byTexture = [];
    private bool _disposed;

    /// <summary>Gets how many groups are being kept.</summary>
    public int Count => _groups.Count;

    /// <summary>Gets the group binding a material, building it on first use.</summary>
    /// <param name="diffuse">The diffuse texture.</param>
    /// <param name="lightmap">The baked lightmap.</param>
    /// <param name="detail">
    /// The surface detail. A diffuse with no map of its own is given the neutral one, whose values are
    /// the identity of everything the detail block does, so no draw needs a flag saying which it has.
    /// </param>
    /// <returns>The group.</returns>
    public IRhiBindGroup Get(
        in RhiTextureBinding diffuse,
        in RhiTextureBinding lightmap,
        in RhiTextureBinding detail)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        MaterialKey key = new(diffuse.Id, lightmap.Id, detail.Id);

        if (_groups.TryGetValue(key, out IRhiBindGroup? existing))
        {
            return existing;
        }

        IRhiBindGroup group = _device.CreateBindGroup(new RhiBindGroupDescriptor(
            _layout,
            [
                RhiBindGroupEntry.Of(0, diffuse.View),
                RhiBindGroupEntry.Of(1, diffuse.Sampler),
                RhiBindGroupEntry.Of(2, lightmap.View),
                RhiBindGroupEntry.Of(3, lightmap.Sampler),
                RhiBindGroupEntry.Of(4, detail.View),
                RhiBindGroupEntry.Of(5, detail.Sampler),
            ],
            $"material-{key.Diffuse}-{key.Lightmap}-{key.Detail}"));

        _groups[key] = group;
        Index(key.Diffuse, key);
        Index(key.Lightmap, key);
        Index(key.Detail, key);
        return group;
    }

    /// <summary>Releases every group that named a destroyed texture.</summary>
    /// <param name="textureId">The texture's handle identifier.</param>
    /// <returns>How many groups went.</returns>
    /// <remarks>
    /// Every group it appears in, not one: a texture is in as many triples as it was ever paired into,
    /// and a group left behind points at a view that no longer exists.
    /// </remarks>
    public int Forget(uint textureId)
    {
        if (!_byTexture.Remove(textureId, out List<MaterialKey>? naming))
        {
            return 0;
        }

        int released = 0;
        foreach (MaterialKey key in naming)
        {
            // Already gone with another of its textures, forgotten first.
            if (!_groups.Remove(key, out IRhiBindGroup? group))
            {
                continue;
            }

            released++;
            if (retired is { } queue)
            {
                queue.Retire(group);
            }
            else
            {
                group.Dispose();
            }
        }

        return released;
    }

    /// <summary>Notes that a texture appears in a group's key.</summary>
    private void Index(uint textureId, MaterialKey key)
    {
        if (!_byTexture.TryGetValue(textureId, out List<MaterialKey>? keys))
        {
            keys = [];
            _byTexture[textureId] = keys;
        }

        // A key names the same texture twice when it is its own lightmap or detail, and listing it twice
        // would only make the release skip it the second time.
        if (keys.Count > 0 && keys[^1] == key)
        {
            return;
        }

        // A group released with another of its textures stays listed under this one. A texture that
        // outlives levels -- the neutral detail map is in nearly every key -- would collect those without
        // end, so the list is swept of them whenever it is about to grow: amortised, it costs nothing.
        if (keys.Count >= 64 && keys.Count == keys.Capacity)
        {
            keys.RemoveAll(listed => !_groups.ContainsKey(listed));
        }

        keys.Add(key);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiBindGroup group in _groups.Values)
        {
            group.Dispose();
        }

        _groups.Clear();
        _byTexture.Clear();
    }

    /// <summary>The textures one group binds, which is what it is keyed by.</summary>
    /// <param name="Diffuse">The diffuse texture's handle identifier.</param>
    /// <param name="Lightmap">The lightmap's.</param>
    /// <param name="Detail">The surface detail's.</param>
    private readonly record struct MaterialKey(uint Diffuse, uint Lightmap, uint Detail);
}

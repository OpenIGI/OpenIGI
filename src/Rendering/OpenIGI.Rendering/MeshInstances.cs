namespace OpenIGI.Rendering;

/// <summary>The plain form of <see cref="IGraphicsDevice.DrawMeshInstances"/>: a draw per copy.</summary>
public static class MeshInstances
{
    /// <summary>Draws each copy with its own <see cref="IMeshDrawDevice.DrawMesh"/>, the sun set first where the copies carry one.</summary>
    /// <param name="device">The device.</param>
    /// <param name="mesh">The mesh.</param>
    /// <param name="indexOffset">The range's first index.</param>
    /// <param name="indexCount">How many indices it has.</param>
    /// <param name="diffuse">The texture every copy samples.</param>
    /// <param name="blend">How every copy blends.</param>
    /// <param name="instances">The copies, in order.</param>
    /// <param name="setsVertexSun">Whether each copy brings its own sun.</param>
    /// <remarks>The definition of the call, and what a backend falls back to where it has nothing to share.</remarks>
    public static void DrawEach(
        IGraphicsDevice device,
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        TextureHandle diffuse,
        BlendMode blend,
        ReadOnlySpan<MeshInstance> instances,
        bool setsVertexSun)
    {
        ArgumentNullException.ThrowIfNull(device);

        foreach (ref readonly MeshInstance instance in instances)
        {
            if (setsVertexSun)
            {
                device.SetVertexSun(instance.VertexSun);
            }

            device.DrawMesh(mesh, indexOffset, indexCount, instance.ModelView, diffuse, instance.Lightmap, blend);
        }
    }
}

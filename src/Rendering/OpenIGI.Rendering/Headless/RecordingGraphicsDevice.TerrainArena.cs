using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

/// <summary>No arena: the ground is recorded chunk by chunk, as every recorded trace has it.</summary>
public sealed partial class RecordingGraphicsDevice
{
    /// <inheritdoc/>
    public bool TerrainArenaReady => false;

    /// <inheritdoc/>
    public int AddTerrainChunk(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices, ReadOnlySpan<TerrainArenaPart> parts) => -1;

    /// <inheritdoc/>
    public void RemoveTerrainChunk(int slot)
    {
    }

    /// <inheritdoc/>
    public bool BeginTerrainArena() => false;

    /// <inheritdoc/>
    public void PlaceTerrainChunk(int slot, in Matrix4f modelView)
    {
    }

    /// <inheritdoc/>
    public void DrawTerrainBucket(int bucket, TextureHandle diffuse, BlendMode blend)
    {
    }
}

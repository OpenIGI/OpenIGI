using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The ground drawn the original's way: its chunks kept in one arena and drawn a texture and a pass at a time.</summary>
/// <remarks>
/// <para>
/// Project I.G.I. transforms every visible chunk into one vertex buffer and sorts the triangles by texture, so its
/// ground is a few dozen draws a frame: every base layer, then every overlay, then every lightmap page
/// (TerrainMesh_DrawD3D). Drawn a chunk and a layer at a time the same ground is thousands, which on a slow processor
/// -- a browser above all -- is most of the frame.
/// </para>
/// <para>
/// A chunk is put in once, with each of its layers' triangles filed under a bucket the caller names -- a texture in a
/// pass. Each frame the caller places the chunks it draws, then draws each bucket; a bucket draw covers every placed
/// chunk filed under it, and a chunk not placed this frame is not drawn. A placement takes the chunk's transform and
/// the detail fade set at the time, exactly as a draw of that chunk alone would; a bucket draw takes the rest of the
/// device's state -- depth, culling, the fade's mode, the fog's target -- as that draw would.
/// </para>
/// <para>Part of <see cref="IGraphicsDevice"/>.</para>
/// </remarks>
public interface ITerrainArenaDevice
{
    /// <summary>Gets a value indicating whether the arena draws now.</summary>
    /// <remarks>Only on the frame's own direct path; where it does not, the caller draws its chunks one by one.</remarks>
    bool TerrainArenaReady { get; }

    /// <summary>Puts a chunk in the arena.</summary>
    /// <param name="vertices">The chunk's vertices, in its own frame.</param>
    /// <param name="indices">Its triangles, indexing <paramref name="vertices"/>.</param>
    /// <param name="parts">Where each of its layers' triangles are filed.</param>
    /// <returns>The chunk's slot, or -1 when the arena does not take it and it is to be drawn on its own.</returns>
    int AddTerrainChunk(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices, ReadOnlySpan<TerrainArenaPart> parts);

    /// <summary>Takes a chunk out of the arena.</summary>
    /// <param name="slot">The slot <see cref="AddTerrainChunk"/> returned.</param>
    void RemoveTerrainChunk(int slot);

    /// <summary>Starts placing a frame's chunks.</summary>
    /// <returns>
    /// <see langword="false"/> when the arena has already been drawn this frame: its table holds one view a frame, so
    /// a second view draws its chunks one by one.
    /// </returns>
    bool BeginTerrainArena();

    /// <summary>Places one chunk for this frame's bucket draws.</summary>
    /// <param name="slot">The chunk's slot.</param>
    /// <param name="modelView">The chunk's object-to-view transform.</param>
    void PlaceTerrainChunk(int slot, in Matrix4f modelView);

    /// <summary>Draws every placed chunk's triangles filed under one bucket.</summary>
    /// <param name="bucket">The bucket.</param>
    /// <param name="diffuse">Its texture.</param>
    /// <param name="blend">How it is blended.</param>
    void DrawTerrainBucket(int bucket, TextureHandle diffuse, BlendMode blend);
}

/// <summary>Where one of a chunk's layers is filed in the arena.</summary>
/// <param name="Bucket">The bucket: a texture in a pass, named by the caller.</param>
/// <param name="IndexOffset">The layer's first index in the chunk's indices.</param>
/// <param name="IndexCount">How many indices it has.</param>
public readonly record struct TerrainArenaPart(int Bucket, int IndexOffset, int IndexCount);

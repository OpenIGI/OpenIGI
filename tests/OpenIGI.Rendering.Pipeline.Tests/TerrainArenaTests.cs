using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That the ground's arena survives a level being taken out of it and the next put in.</summary>
public sealed class TerrainArenaTests
{
    private const int Bucket = 7;

    /// <summary>
    /// Leaving a mission takes every chunk out, which compacts a bucket into a shorter list; the next level's first upload
    /// read the old list's dirty range against the new list and threw.
    /// </summary>
    [Fact]
    public void A_level_taken_out_and_the_next_put_in_uploads()
    {
        RecordingRhiDevice device = new();
        using IRhiBindGroupLayout layout = TerrainArena.CreateLayout(device);
        using TerrainArena arena = new(device, layout, retired: null);

        List<int> slots = [];
        for (int i = 0; i < 40; i++)
        {
            slots.Add(AddChunk(arena));
        }

        Assert.True(arena.Begin(1));
        arena.Upload();

        foreach (int slot in slots)
        {
            arena.Remove(slot);
        }

        Assert.True(arena.Begin(2));
        arena.Upload();

        int next = AddChunk(arena);
        Assert.True(arena.Begin(3));
        arena.Upload();
        Assert.True(arena.TryBucket(Bucket, out _, out int count));
        Assert.True(count >= 300);
        Assert.True(next >= 0);
    }

    private static int AddChunk(TerrainArena arena)
    {
        Vertex3D[] vertices = new Vertex3D[3];
        ushort[] indices = new ushort[300];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (ushort)(i % 3);
        }

        return arena.Add(vertices, indices, [new TerrainArenaPart(Bucket, 0, indices.Length)]);
    }
}

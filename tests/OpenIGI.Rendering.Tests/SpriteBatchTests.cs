using OpenIGI.Core.Graphics;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Rendering.Tests;

public sealed class SpriteBatchTests
{
    [Fact]
    public void EmptyBatch_IssuesNoDrawCall()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);

        batch.Begin();
        batch.End();

        Assert.Empty(device.Draws);
        Assert.Equal(0, batch.DrawCallCount);
    }

    [Fact]
    public void ConsecutiveQuadsOnOneTexture_MergeIntoOneDrawCall()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);
        TextureHandle texture = new(1);

        batch.Begin();
        for (int i = 0; i < 5; i++)
        {
            batch.Draw(texture, new RectF(i * 10, 0, 10, 10), RectF.Unit, Bgra32.White);
        }

        batch.End();

        Assert.Single(device.Draws);
        Assert.Equal(5, device.Draws[0].QuadCount);
        Assert.Equal(30, device.Draws[0].Indices.Length);
        Assert.Equal(5, batch.QuadCount);
    }

    [Fact]
    public void ChangingTexture_FlushesSoSubmissionOrderIsPreserved()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);

        batch.Begin();
        batch.Draw(new TextureHandle(1), new RectF(0, 0, 10, 10), RectF.Unit, Bgra32.White);
        batch.Draw(new TextureHandle(2), new RectF(0, 0, 10, 10), RectF.Unit, Bgra32.White);
        batch.Draw(new TextureHandle(1), new RectF(0, 0, 10, 10), RectF.Unit, Bgra32.White);
        batch.End();

        // Three calls, not two: merging the two texture-1 quads would move the second one in front of
        // the texture-2 quad, and the UI depends on painter's order.
        Assert.Equal(3, device.Draws.Count);
        Assert.Equal([1u, 2u, 1u], device.Draws.Select(d => d.Texture.Id));
    }

    [Fact]
    public void ChangingBlendMode_Flushes()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);
        TextureHandle texture = new(1);

        batch.Begin();
        batch.Draw(texture, new RectF(0, 0, 10, 10), RectF.Unit, Bgra32.White, BlendMode.Opaque);
        batch.Draw(texture, new RectF(0, 0, 10, 10), RectF.Unit, Bgra32.White, BlendMode.Additive);
        batch.End();

        Assert.Equal(2, device.Draws.Count);
        Assert.Equal(BlendMode.Opaque, device.Draws[0].Blend);
        Assert.Equal(BlendMode.Additive, device.Draws[1].Blend);
    }

    [Fact]
    public void ExceedingCapacity_FlushesAndKeepsEveryQuad()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device, capacityInQuads: 4);
        TextureHandle texture = new(1);

        batch.Begin();
        for (int i = 0; i < 10; i++)
        {
            batch.Draw(texture, new RectF(i, 0, 1, 1), RectF.Unit, Bgra32.White);
        }

        batch.End();

        Assert.Equal(3, device.Draws.Count);
        Assert.Equal(10, device.Draws.Sum(d => d.QuadCount));
        Assert.Equal(10, batch.QuadCount);
    }

    [Fact]
    public void QuadCorners_FollowTheDestinationAndSourceRectangles()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);

        batch.Begin();
        batch.Draw(
            new TextureHandle(1),
            new RectF(10, 20, 30, 40),
            RectF.FromEdges(0.25f, 0.5f, 0.75f, 1.0f),
            Bgra32.White);
        batch.End();

        Vertex2D[] v = device.Draws[0].Vertices;
        Assert.Equal(4, v.Length);

        Assert.Equal((10.0f, 20.0f), (v[0].X, v[0].Y));
        Assert.Equal((40.0f, 20.0f), (v[1].X, v[1].Y));
        Assert.Equal((40.0f, 60.0f), (v[2].X, v[2].Y));
        Assert.Equal((10.0f, 60.0f), (v[3].X, v[3].Y));

        Assert.Equal((0.25f, 0.5f), (v[0].U, v[0].V));
        Assert.Equal((0.75f, 1.0f), (v[2].U, v[2].V));
    }

    [Fact]
    public void IndicesWindTwoTrianglesPerQuad()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);

        batch.Begin();
        batch.Draw(new TextureHandle(1), new RectF(0, 0, 1, 1), RectF.Unit, Bgra32.White);
        batch.Draw(new TextureHandle(1), new RectF(1, 0, 1, 1), RectF.Unit, Bgra32.White);
        batch.End();

        Assert.Equal<ushort[]>([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7], device.Draws[0].Indices);
    }

    [Fact]
    public void DrawOutsideBeginAndEnd_Throws()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);

        Assert.Throws<InvalidOperationException>(
            () => batch.Draw(new TextureHandle(1), new RectF(0, 0, 1, 1), RectF.Unit, Bgra32.White));
        Assert.Throws<InvalidOperationException>(batch.End);

        batch.Begin();
        Assert.Throws<InvalidOperationException>(batch.Begin);
    }

    [Fact]
    public void FillRect_UsesTheNullTextureSoUntexturedFillsBatchTogether()
    {
        RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);

        batch.Begin();
        batch.FillRect(new RectF(0, 0, 4, 4), Bgra32.Black);
        batch.FillRect(new RectF(4, 0, 4, 4), Bgra32.Black);
        batch.End();

        Assert.Single(device.Draws);
        Assert.Equal(TextureHandle.None, device.Draws[0].Texture);
    }
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Headless;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>I.G.I. 2's weather is built once a frame and drawn per tile, as <c>Rain_UpdateVertexBuffer</c> and <c>Rain_SortRenderCallbackVB</c> do.</summary>
public sealed class RetailPrecipitationTileTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    [InlineData(false, 32)]
    public void The_drops_are_uploaded_once_a_frame_whatever_the_tiles(bool snow, int density)
    {
        RecordingGraphicsDevice device = new();
        using RetailPrecipitationRenderer renderer = new(device, PrecipitationStyle.Igi2) { Copies = Copies(density) };
        RetailPrecipitation weather = Ticked(snow);

        Draw(renderer, weather);

        MeshHandle mesh = Assert.Single(device.MeshDraws.Select(d => d.Mesh).Distinct());
        Assert.Equal(1, device.AnimatedMeshUpdates[mesh.Id]);
        int uploaded = weather.Count * density;
        Assert.All(device.MeshDraws, d =>
        {
            Assert.Equal(0, d.IndexOffset % 6);
            Assert.True(d.IndexOffset + d.IndexCount <= uploaded * 6, $"{d.IndexOffset}+{d.IndexCount} past {uploaded * 6}");
        });
    }

    [Fact]
    public void A_dense_storm_draws_every_tile_rather_than_stopping_at_a_cap()
    {
        RecordingGraphicsDevice device = new();
        using RetailPrecipitationRenderer renderer = new(device, PrecipitationStyle.Igi2) { Copies = Copies(32) };

        Draw(renderer, Ticked(snow: false));

        // Built tile by tile, the frame stopped at its quad cap before the tiles nearest the eye, which draw last.
        long quads = device.MeshDraws.Sum(d => (long)d.IndexCount) / 6;
        Assert.True(quads > RetailPrecipitationRenderer.MaximumQuads, $"{quads} quads");
    }

    private static void Draw(RetailPrecipitationRenderer renderer, RetailPrecipitation weather)
    {
        Camera3D camera = new(new Vector3d(100.0, 200.0, 30.0), Matrix3f.Identity);
        Matrix4f projection = Matrix4f.CreatePerspective(1.2f, 16f / 9f, 0.1f, 1000f);
        renderer.Draw(weather, camera, projection, 1080, ground: null);
    }

    private static Vector3f[] Copies(int density) =>
        [.. Enumerable.Range(0, density).Select(i => new Vector3f(i * 37f - 600f, (i * 91 % 400) - 200f, (i * 53 % 300) - 150f))];

    private static RetailPrecipitation Ticked(bool snow)
    {
        RetailPrecipitation weather = new(snow) { IsFalling = true };
        for (int i = 0; i < 400; i++)
        {
            weather.Tick();
        }

        return weather;
    }
}

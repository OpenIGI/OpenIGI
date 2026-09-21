using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The cloud plane's fade window, its scroll and its grid, shared by both games.
/// </summary>
public sealed class SkyCloudPlaneTests
{
    private const float Distance = 100f * 4096f;

    /// <summary>Fog Amount slides the near edge of the window between the far distance and a tenth of a metre.</summary>
    /// <remarks>
    /// <c>distance - (distance - 409.6) * fogAmount</c> — read the same way out of both binaries. The
    /// two ends are what fix it: no fog puts the whole window at the far distance, so nothing fades at
    /// all, and full fog opens it right back to the engine's near constant.
    /// </remarks>
    [Fact]
    public void Fog_amount_slides_the_near_edge_of_the_window()
    {
        Assert.Equal(Distance, SkyCloudPlane.NearDepthOf(Distance, 0f), 1);
        Assert.Equal(SkyCloudPlane.NearCutoff, SkyCloudPlane.NearDepthOf(Distance, 1f), 1);

        // Half way is half way, which is what makes it a straight slide rather than a curve.
        Assert.Equal((Distance + SkyCloudPlane.NearCutoff) / 2f, SkyCloudPlane.NearDepthOf(Distance, 0.5f), 1);
    }

    /// <summary>The layer is opaque inside the window and gone at the far distance.</summary>
    [Fact]
    public void The_fade_runs_from_opaque_to_nothing_across_the_window()
    {
        const float Fog = 0.5f;
        float near = SkyCloudPlane.NearDepthOf(Distance, Fog);

        // Nearer than the window: no fade, and clamped short of fully opaque nowhere — the near branch
        // returns one outright, which is what the original does before it reaches the ramp.
        Assert.Equal(1f, SkyCloudPlane.FadeAt(near * 0.5f, Distance, Fog));
        Assert.Equal(1f, SkyCloudPlane.FadeAt(1f, Distance, Fog));

        // At the far distance the numerator cancels exactly.
        Assert.Equal(0f, SkyCloudPlane.FadeAt(Distance, Distance, Fog), 5);

        // And beyond it the fade stays clamped rather than going negative.
        Assert.Equal(0f, SkyCloudPlane.FadeAt(Distance * 4f, Distance, Fog), 5);

        // In between it falls, and never above the clamp.
        float previous = 1f;
        for (float depth = near; depth <= Distance; depth += (Distance - near) / 32f)
        {
            float fade = SkyCloudPlane.FadeAt(depth, Distance, Fog);
            Assert.InRange(fade, 0f, SkyCloudPlane.MaximumFade);
            Assert.True(fade <= previous + 1e-5f, $"the fade rose at {depth}");
            previous = fade;
        }
    }

    /// <summary>A sky with almost no fog skips the ramp outright.</summary>
    /// <remarks>The <c>&lt; 0.01</c> branch both binaries take, which keeps a clear sky fully drawn.</remarks>
    [Fact]
    public void Almost_no_fog_leaves_the_layer_alone()
    {
        Assert.Equal(1f, SkyCloudPlane.FadeAt(Distance, Distance, 0.001f));
        Assert.Equal(1f, SkyCloudPlane.FadeAt(Distance * 10f, Distance, 0f));
    }

    /// <summary>The scroll advances by one speed a tick and wraps into a single turn.</summary>
    /// <remarks>
    /// <c>FlatSkyLayer_RunHandler</c> (<c>igi2ed 0x654820</c>) walks the offset back into <c>[0, 1)</c> a
    /// whole turn at a time, in both directions. The wrap is invisible — the texture repeats — and is
    /// what keeps the coordinate from growing without bound across a long session.
    /// </remarks>
    [Fact]
    public void The_scroll_wraps_into_one_turn_in_both_directions()
    {
        Assert.Equal(0.25f, SkyCloudPlane.AdvanceScroll(0f, 0.25f), 5);
        Assert.Equal(0.5f, SkyCloudPlane.AdvanceScroll(0.25f, 0.25f), 5);

        // Past a whole turn it comes back rather than accumulating.
        Assert.Equal(0.1f, SkyCloudPlane.AdvanceScroll(0.9f, 0.2f), 5);

        // A negative speed wraps the other way, which the original does with its own loop.
        Assert.Equal(0.9f, SkyCloudPlane.AdvanceScroll(0.1f, -0.2f), 5);

        // Ten thousand ticks of a slow scroll stay in range, which is the whole reason for the wrap.
        float offset = 0f;
        for (int i = 0; i < 10_000; i++)
        {
            offset = SkyCloudPlane.AdvanceScroll(offset, 0.013f);
            Assert.InRange(offset, 0f, 1f);
        }
    }

    /// <summary>The grid is one flat ring field about the camera, and every triangle is on it.</summary>
    [Fact]
    public void The_grid_is_a_flat_ring_field_about_the_camera()
    {
        Vertex3D[] vertices = new Vertex3D[SkyCloudPlane.VertexCount];
        SkyCloudPlane.Fill(
            vertices,
            new SkyCloudLayerSample(20f * 4096f, Distance, 0.5f, 1f, 0.001f, 0f, 0f, 1f, new Vector3f(1f, 1f, 1f)),
            new Vector3f(0f, 1f, 0f));

        // The plane is one height above the camera, everywhere.
        Assert.All(vertices, vertex => Assert.Equal(20f * 4096f, vertex.Position.Z, 2));

        // It reaches half again past the sky's distance, which is what keeps the rim past the vanishing
        // point at a wide field of view.
        float furthest = vertices.Max(v => MathF.Sqrt((v.Position.X * v.Position.X) + (v.Position.Y * v.Position.Y)));
        Assert.Equal(Distance * SkyCloudPlane.RadiusFactor, furthest, 1);

        // The innermost ring is under the camera, so there is no hole overhead.
        Assert.Equal(0f, vertices[0].Position.X, 3);
        Assert.Equal(0f, vertices[0].Position.Y, 3);

        ushort[] indices = SkyCloudPlane.BuildIndices();
        Assert.Equal(SkyCloudPlane.IndexCount, indices.Length);
        Assert.Equal(0, indices.Length % 3);
        Assert.All(indices, index => Assert.InRange(index, 0, SkyCloudPlane.VertexCount - 1));
    }

    /// <summary>A vertex's texture coordinate is its own position, scaled, plus the scroll.</summary>
    /// <remarks>
    /// Both binaries take the coordinate from the <b>camera-relative</b> point where the eye ray meets
    /// the plane, never from a world position. That is what makes the layer a sky: it does not slide
    /// past as the player walks, and the only thing that moves it is its own scroll.
    /// </remarks>
    [Fact]
    public void A_texture_coordinate_is_the_scaled_position_plus_the_scroll()
    {
        const float Scale = 0.0004f;
        Vertex3D[] plain = new Vertex3D[SkyCloudPlane.VertexCount];
        Vertex3D[] scrolled = new Vertex3D[SkyCloudPlane.VertexCount];

        SkyCloudLayerSample sample = new(20f * 4096f, Distance, 0.5f, 1f, Scale, 0f, 0f, 1f, new Vector3f(1f, 1f, 1f));
        SkyCloudPlane.Fill(plain, sample, new Vector3f(0f, 1f, 0f));
        SkyCloudPlane.Fill(scrolled, sample with { ScrollU = 0.25f, ScrollV = 0.5f }, new Vector3f(0f, 1f, 0f));

        for (int i = 0; i < plain.Length; i++)
        {
            Assert.Equal(plain[i].Position.X * Scale, plain[i].DiffuseU, 4);
            Assert.Equal(plain[i].Position.Y * Scale, plain[i].DiffuseV, 4);

            // The scroll is a shift of the whole field, not a change of its shape.
            Assert.Equal(plain[i].DiffuseU + 0.25f, scrolled[i].DiffuseU, 4);
            Assert.Equal(plain[i].DiffuseV + 0.5f, scrolled[i].DiffuseV, 4);
        }
    }

    /// <summary>Drawing in metres and drawing in engine units give the same sky.</summary>
    /// <remarks>
    /// The one thing that differs between the two games here. I.G.I. 1 draws the plane in metres and
    /// I.G.I. 2 in engine units, but the fade window and the texture scale are authored against engine
    /// units either way — so the same sky described in either space has to come out with the same
    /// coordinates and the same alpha, or the parameter is doing nothing.
    /// </remarks>
    [Fact]
    public void The_same_sky_comes_out_the_same_in_either_space()
    {
        const float Metre = 4096f;
        Vector3f forward = new(0f, 0.8f, 0.6f);

        Vertex3D[] inUnits = new Vertex3D[SkyCloudPlane.VertexCount];
        Vertex3D[] inMetres = new Vertex3D[SkyCloudPlane.VertexCount];

        SkyCloudPlane.Fill(
            inUnits,
            new SkyCloudLayerSample(20f * Metre, Distance, 0.5f, 1f, 0.001f, 0f, 0f, 1f, new Vector3f(1f, 1f, 1f)),
            forward);
        SkyCloudPlane.Fill(
            inMetres,
            new SkyCloudLayerSample(20f * Metre, Distance, 0.5f, Metre, 0.001f, 0f, 0f, 1f, new Vector3f(1f, 1f, 1f)),
            forward);

        for (int i = 0; i < inUnits.Length; i++)
        {
            // The positions differ by exactly the scale, and nothing else does.
            Assert.Equal(inUnits[i].Position.X, inMetres[i].Position.X * Metre, 1);
            Assert.Equal(inUnits[i].Position.Z, inMetres[i].Position.Z * Metre, 1);
            Assert.Equal(inUnits[i].DiffuseU, inMetres[i].DiffuseU, 4);
            Assert.Equal(inUnits[i].Colour, inMetres[i].Colour);
        }
    }

    /// <summary>A grid of the wrong size is refused rather than written past.</summary>
    [Fact]
    public void A_grid_of_the_wrong_size_is_refused()
    {
        Vertex3D[] wrong = new Vertex3D[SkyCloudPlane.VertexCount - 1];
        Assert.Throws<ArgumentException>(() => SkyCloudPlane.Fill(
            wrong,
            new SkyCloudLayerSample(1f, Distance, 0.5f, 1f, 1f, 0f, 0f, 1f, default),
            new Vector3f(0f, 1f, 0f)));
    }
}

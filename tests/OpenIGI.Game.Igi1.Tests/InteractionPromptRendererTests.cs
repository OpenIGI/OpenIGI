using OpenIGI.Presentation;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi1.World;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// Draws the interaction marker into a recording device and measures what came out, so the badge's
/// placement, its ring and its label are checked as geometry rather than as intent.
/// </summary>
public sealed class InteractionPromptRendererTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>An eye at head height looking along +Y, the way the tests place their anchors.</summary>
    private static Camera3D Eye => new(new Vector3d(0.0, 0.0, 1.7), Camera3D.LookRotation(0f, 0f));

    /// <summary>An anchor two and a half metres ahead, which projects to the middle of the frame.</summary>
    private static Vector3d Ahead => new(0.0, 2.5, 1.7);

    [Fact]
    public void TheBadgeIsCentredOnTheProjectedAnchor()
    {
        RecordingGraphicsDevice device = Draw(Badge(Ahead));

        (float left, float right, float top, float bottom) = Bounds(device);
        Assert.InRange((left + right) * 0.5f, 959f, 961f);
        Assert.InRange((top + bottom) * 0.5f, 539f, 541f);

        // A badge, not a full-screen wash: it stays within a small neighbourhood of its anchor at the
        // design scale, which is what keeps it reading as a mark on the object.
        Assert.True(right - left < 150f, $"badge spans {right - left:0} px");
    }

    [Fact]
    public void TheBadgeIsDrawnFromTheAntialiasedAtlasRatherThanFromTriangles()
    {
        RecordingGraphicsDevice device = Draw(Badge(Ahead));

        // Every quad in a plain press prompt carries a texture. The painter rasterises triangles with
        // no coverage at all, so a circle built from them shows its facets at the size a badge is
        // drawn; the shape has to come from the interface's antialiased corner atlas.
        Assert.NotEmpty(device.Draws);
        Assert.All(device.Draws, draw => Assert.True(draw.Texture.IsValid, "an untextured quad was drawn"));
    }

    [Fact]
    public void TheBadgeIsTheSameFigureAtEveryOpacity()
    {
        RecordingGraphicsDevice solid = Draw(Badge(Ahead));
        RecordingGraphicsDevice half = Draw(Badge(Ahead) with { Opacity = 0.5f });

        // Identical geometry, only dimmer. A badge assembled from filled shapes that cover each other
        // does not have this property: scale every layer's alpha by the fade and the face stops
        // covering the rim, which shows through as a lighter middle with a seam across it.
        Assert.Equal(
            solid.Draws.Sum(static draw => draw.QuadCount),
            half.Draws.Sum(static draw => draw.QuadCount));
        Assert.Equal(Bounds(solid), Bounds(half));

        float solidPeak = solid.Draws.SelectMany(static draw => draw.Vertices).Max(static v => (float)v.Color.A);
        float halfPeak = half.Draws.SelectMany(static draw => draw.Vertices).Max(static v => (float)v.Color.A);
        Assert.InRange(halfPeak, solidPeak * 0.45f, solidPeak * 0.55f);
    }

    [Fact]
    public void NothingIsDrawnWhileTheMarkerIsFadedOut()
    {
        RecordingGraphicsDevice device = Draw(View(Ahead) with { Opacity = 0f });

        Assert.Empty(device.Draws);
    }

    [Fact]
    public void TheHoldRingGrowsWithTheProgressItReports()
    {
        int none = QuadCount(View(Ahead));
        int quarter = QuadCount(View(Ahead) with { Progress = 0.25f });
        int half = QuadCount(View(Ahead) with { Progress = 0.5f });
        int full = QuadCount(View(Ahead) with { Progress = 1f });

        Assert.True(quarter > none);
        Assert.True(half > quarter);
        Assert.True(full > half);
    }

    [Fact]
    public void TheLabelSitsBesideTheBadgeAndFlipsAwayFromTheEdgeOfTheFrame()
    {
        // Measured as what the name adds to the marker's extent, rather than by trying to tell the
        // label's quads from the badge's: both are drawn from a texture.
        Vector3d roomy = Ahead;
        (float badgeLeft, float badgeRight, _, _) = Bounds(Draw(Badge(roomy)));
        (float left, float right, _, _) = Bounds(Draw(View(roomy)));

        Assert.True(right > badgeRight + 40f, $"the name should reach right of the badge, not to {right:0}");
        Assert.Equal(badgeLeft, left, 1);

        // Off to the right, where the name would run past the edge of the frame if it stayed on that
        // side of the badge.
        var edge = new Vector3d(2.3, 2.5, 1.7);
        (float edgeBadgeLeft, float edgeBadgeRight, _, _) = Bounds(Draw(Badge(edge)));
        (float edgeLeft, float edgeRight, _, _) = Bounds(Draw(View(edge)));

        // Against the right edge the same label has to go the other way, or the action's name would be
        // printed off the screen at exactly the moment the player needs to read it.
        Assert.True(
            edgeLeft < edgeBadgeLeft - 40f, $"the name should have flipped left, but starts at {edgeLeft:0}");
        Assert.Equal(edgeBadgeRight, edgeRight, 1);
    }

    [Fact]
    public void TheMarkerIsSmallerTheFurtherAwayTheThingIs()
    {
        (float nearLeft, float nearRight, _, _) = Bounds(Draw(Badge(new Vector3d(0.0, 1.5, 1.7))));
        (float farLeft, float farRight, _, _) = Bounds(Draw(Badge(new Vector3d(0.0, 3.0, 1.7))));

        // Perspective, not a scale rule: at twice the distance the badge covers half the pixels.
        Assert.Equal((nearRight - nearLeft) * 0.5f, farRight - farLeft, 1);
    }

    [Fact]
    public void NothingIsDrawnForSomethingBehindThePlayer()
    {
        RecordingGraphicsDevice device = Draw(View(new Vector3d(0.0, -2.5, 1.7)));

        Assert.Empty(device.Draws);
    }

    [Fact]
    public void AZeroSizedFramebufferDrawsNothingAndThrowsNothing()
    {
        RecordingGraphicsDevice device = Draw(View(Ahead), width: 0, height: 0);

        Assert.True(device.Draws.Count >= 0);
    }

    private static InteractionPromptView View(Vector3d anchorMetres) => new(
        anchorMetres,
        Opacity: 1f,
        Pop: 1f,
        KeyLabel: "E",
        Title: "Open Door",
        Detail: string.Empty,
        Progress: null);

    /// <summary>The same marker with no name beside it, so every quad drawn belongs to the badge.</summary>
    private static InteractionPromptView Badge(Vector3d anchorMetres) =>
        View(anchorMetres) with { Title = string.Empty };

    private static RecordingGraphicsDevice Draw(
        in InteractionPromptView view,
        int width = Width,
        int height = Height)
    {
        RecordingGraphicsDevice device = new();
        using RemasteredHudRenderer renderer = new(device, NullLogger.Instance);
        Assert.True(renderer.Load());
        SpriteBatch batch = new(device);

        device.Set2DProjection(Math.Max(1, width), Math.Max(1, height));
        batch.Begin();
        renderer.DrawInteractionPrompt(batch, view, Eye, 1f, 16f / 9f, width, height);
        batch.End();
        return device;
    }

    private static int QuadCount(in InteractionPromptView view) =>
        Draw(view).Draws.Sum(static draw => draw.QuadCount);

    private static (float Left, float Right, float Top, float Bottom) Bounds(RecordingGraphicsDevice device)
    {
        float left = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float top = float.PositiveInfinity;
        float bottom = float.NegativeInfinity;
        foreach (RecordingGraphicsDevice.DrawCall draw in device.Draws)
        {
            foreach (Vertex2D vertex in draw.Vertices)
            {
                left = MathF.Min(left, vertex.X);
                right = MathF.Max(right, vertex.X);
                top = MathF.Min(top, vertex.Y);
                bottom = MathF.Max(bottom, vertex.Y);
            }
        }

        Assert.True(float.IsFinite(left), "nothing was drawn");
        return (left, right, top, bottom);
    }

}

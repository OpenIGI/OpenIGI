using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>Covers <see cref="ViewClip"/>: a part of the view in fractions, taken to the pixels of a viewport.</summary>
public sealed class ViewClipTests
{
    /// <summary>A clip lands on its share of the viewport, measured from the viewport's own corner.</summary>
    [Fact]
    public void A_clip_lands_on_its_share_of_the_viewport()
    {
        Assert.Equal((35, 120, 50, 100), new ViewClip(0.25f, 0.5f, 0.75f, 1f).ToScissor(10, 20, 100, 200));
        Assert.Equal((0, 0, 640, 480), ViewClip.Full.ToScissor(0, 0, 640, 480));
    }

    /// <summary>A clip's edges are widened to whole pixels, and a clip reaching past the view is held to it.</summary>
    [Fact]
    public void Edges_widen_to_whole_pixels_and_stay_in_the_viewport()
    {
        Assert.Equal((3, 0, 5, 10), new ViewClip(0.31f, -0.5f, 0.71f, 1.5f).ToScissor(0, 0, 10, 10));
        (_, _, int width, int height) = new ViewClip(0.5f, 0.5f, 0.4f, 0.4f).ToScissor(0, 0, 10, 10);
        Assert.Equal((0, 0), (width, height));
        Assert.True(new ViewClip(0.5f, 0f, 0.5f, 1f).IsEmpty);
        Assert.False(ViewClip.Full.IsEmpty);
    }
}

using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Which draws are shaded by the remaster's studio key, and which are not — the number the forward push
/// block carries, and the alpha it leaves behind.
/// </summary>
/// <remarks>
/// The rule that matters here is the one the vanilla preset broke: the key light belongs to the dynamic
/// lighting model, and under the baked one there is no light to key with. <c>SceneLighting.Baked</c> is a
/// black sun over a black ambient by design, and the vanilla preset renders no offscreen scene, so
/// nothing ever calls <c>SetLighting</c> — the rig was multiplied by black and drew as a solid
/// silhouette in the corner of every frame.
/// </remarks>
public sealed class ForwardDrawMarkerTests
{
    /// <summary>The rig is studio-lit only where there is a dynamic light to key it with.</summary>
    [Theory]
    [InlineData(LightingModel.Dynamic, ForwardDrawMarker.LitFirstPerson, true)]
    [InlineData(LightingModel.Baked, ForwardDrawMarker.UnlitFirstPerson, false)]
    public void TheRigIsKeyedOnlyUnderDynamicLighting(LightingModel lighting, float expected, bool studio)
    {
        float marker = ForwardDrawMarker.For(BlendMode.Opaque, firstPerson: true, lighting);

        Assert.Equal(expected, marker);
        Assert.Equal(studio, ForwardDrawMarker.IsStudioRig(marker));
    }

    /// <summary>Unlit or not, the rig is still the rig to everything that reads the alpha.</summary>
    /// <remarks>
    /// The alpha is how a pass reading the lit colour back tells the world from what is only in front of
    /// the eye: the eye adaptation must not meter the arms and the indirect gather must not bounce off
    /// them, whichever way they were shaded.
    /// </remarks>
    [Fact]
    public void AnUnlitRigLeavesTheRigsOwnAlpha()
    {
        Assert.Equal(
            ForwardDrawMarker.LitFirstPerson,
            ForwardDrawMarker.AlphaFor(ForwardDrawMarker.UnlitFirstPerson));

        // Every other marker is its own alpha.
        Assert.Equal(ForwardDrawMarker.World, ForwardDrawMarker.AlphaFor(ForwardDrawMarker.World));
        Assert.Equal(ForwardDrawMarker.LitFirstPerson, ForwardDrawMarker.AlphaFor(ForwardDrawMarker.LitFirstPerson));
        Assert.Equal(ForwardDrawMarker.Translucent, ForwardDrawMarker.AlphaFor(ForwardDrawMarker.Translucent));
    }

    /// <summary>The world and the translucent draws are what they always were, under either model.</summary>
    [Theory]
    [InlineData(LightingModel.Dynamic)]
    [InlineData(LightingModel.Baked)]
    public void TheWorldAndTheTranslucentDrawsAreUnchanged(LightingModel lighting)
    {
        Assert.Equal(ForwardDrawMarker.World, ForwardDrawMarker.For(BlendMode.Opaque, firstPerson: false, lighting));
        Assert.Equal(
            ForwardDrawMarker.Translucent,
            ForwardDrawMarker.For(BlendMode.Additive, firstPerson: false, lighting));

        // Including a translucent first-person draw, which is no more the studio rig than a tracer is.
        float translucentRig = ForwardDrawMarker.For(BlendMode.AlphaBlend, firstPerson: true, lighting);
        Assert.Equal(ForwardDrawMarker.Translucent, translucentRig);
        Assert.False(ForwardDrawMarker.IsStudioRig(translucentRig));
    }

    /// <summary>
    /// The fragment stage's own tests have to agree with these values, because it reads the same number.
    /// </summary>
    /// <remarks>
    /// It asks two questions of the marker: <c>abs(marker) &gt; 0.5</c> for "shade this without the key",
    /// and <c>marker &gt; 1.5</c> for "and write the rig's zero to the alpha". A fifth value, or a
    /// renumbering, has to keep both answers.
    /// </remarks>
    [Fact]
    public void TheMarkersStraddleTheThresholdsTheShaderTestsThemWith()
    {
        Assert.True(Math.Abs(ForwardDrawMarker.LitFirstPerson) <= 0.5f);
        Assert.True(Math.Abs(ForwardDrawMarker.World) > 0.5f);
        Assert.True(Math.Abs(ForwardDrawMarker.Translucent) > 0.5f);
        Assert.True(Math.Abs(ForwardDrawMarker.UnlitFirstPerson) > 0.5f);

        Assert.True(ForwardDrawMarker.UnlitFirstPerson > 1.5f);
        Assert.True(ForwardDrawMarker.World < 1.5f);
        Assert.True(ForwardDrawMarker.LitFirstPerson < 1.5f);
        Assert.True(ForwardDrawMarker.Translucent < 1.5f);
    }
}

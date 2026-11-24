using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// A race's ghost's two blocks are laid out as its shaders read them (<c>World/GhostMask.slang</c>, <c>Post/Ghost.slang</c>), and
/// the mask's push data fits every backend.
/// </summary>
public sealed class GhostPassTests
{
    // The least push data any backend offers: Direct3D 12's root constants.
    private const int SmallestPushDataBytes = 128;

    [Fact]
    public void The_mask_push_is_the_shaders_and_fits_every_backend()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.GhostMask);

        Assert.NotNull(reflection.PushBlock);
        Assert.Equal(GhostPass.MaskPushFloats * sizeof(float), reflection.PushBlock.Size);
        Assert.True(reflection.PushBlock.Size <= SmallestPushDataBytes);
        Assert.Empty(reflection.Bindings);
    }

    [Fact]
    public void The_composite_binds_what_the_pass_binds()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Ghost);

        Assert.Equal(
            ["p", "uDepth", "uGhost", "uSource", "uTarget"],
            reflection.Bindings.Select(static b => b.Name).Order(StringComparer.Ordinal));
        Assert.Null(reflection.PushBlock);
    }

    [Fact]
    public void The_mask_carries_the_bodys_transform_depth_row_pose_and_time()
    {
        GhostBodyDraw draw = new(
            new PosedBody(null!, null!, null!, [], Matrix4f.Identity, PoseBlend: 0.25f),
            new System.Numerics.Vector4(0.1f, 0.2f, 0.3f, 4f));
        Span<float> push = stackalloc float[GhostPass.MaskPushFloats];

        GhostPass.WriteMask(push, draw, seconds: 12.5f);

        Assert.Equal(1f, push[0]);
        Assert.Equal(1f, push[15]);
        Assert.Equal([0.1f, 0.2f, 0.3f, 4f], push[16..20].ToArray());
        Assert.Equal(0.25f, push[20]);
        Assert.Equal(12.5f, push[21]);
    }

    [Fact]
    public void The_parameters_follow_the_composites_layout()
    {
        GhostFrame frame = new(1920, 1080, 960, 540, new DepthUnprojection(1.5f, 2f, 0.1f, 100000f), Exposure: 2f);
        Span<float> block = stackalloc float[GhostPass.ParameterFloats];

        GhostPass.WriteParameters(block, frame, new GhostLook(3605f, Glitch: 1.5f, GlitchSeed: 4100f));

        Assert.Equal([1920f, 1080f, 1f / 1920f, 1f / 1080f], block[0..4].ToArray());
        Assert.Equal([960f, 540f, 0.5f, 0.5f], block[4..8].ToArray());
        Assert.Equal([1.5f, 2f, 0.1f, 100000f], block[8..12].ToArray());
        Assert.Equal([5f, 2f, 1f, 4f], block[12..16].ToArray());
        Assert.Equal([GhostPass.Tint.R, GhostPass.Tint.G, GhostPass.Tint.B, GhostPass.Strength], block[16..20].ToArray());
    }

    [Fact]
    public void A_block_of_the_wrong_size_is_refused()
    {
        float[] block = new float[GhostPass.ParameterFloats - 1];

        Assert.Throws<ArgumentException>(() => GhostPass.WriteParameters(block, default, default));
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>That the G-buffer push block's mode slot says what the draw is.</summary>
public sealed class GBufferPushBlockTests
{
    /// <summary>An authored material is pushed as its own mode, and the ground's modes still win over it.</summary>
    [Fact]
    public void An_authored_material_is_its_own_mode_below_the_grounds()
    {
        SurfaceMaterial authored = SurfaceMaterial.Body with { AuthoredDetail = true };
        GBufferDrawState state = new(Matrix4f.Identity, Matrix3f.Identity, authored);

        Assert.Equal(GBufferPushBlock.AuthoredDetailMode, Mode(state, BlendMode.Opaque));
        Assert.Equal(GBufferPushBlock.GroundDetailMode, Mode(state with { GroundDetail = true }, BlendMode.Opaque));
        Assert.Equal(GBufferPushBlock.GroundBakeMode, Mode(state, BlendMode.Modulate2X));
    }

    private static float Mode(in GBufferDrawState state, BlendMode blend)
    {
        Span<float> block = stackalloc float[GBufferPushBlock.Floats];
        GBufferPushBlock.Write(block, state, Matrix4f.Identity, blend);
        return block[19];
    }
}

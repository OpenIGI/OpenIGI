using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// The remastered thermal sensor's two blocks are laid out as its shaders read them (<c>World/ThermalMask.slang</c>,
/// <c>Post/Thermal.slang</c>), and the mask's push data fits every backend.
/// </summary>
public sealed class ThermalPassTests
{
    // The least push data any backend offers: Direct3D 12's root constants.
    private const int SmallestPushDataBytes = 128;

    [Fact]
    public void TheMaskPushIsTheShadersAndFitsEveryBackend()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.ThermalMask);

        Assert.NotNull(reflection.PushBlock);
        Assert.Equal(ThermalPass.MaskPushFloats * sizeof(float), reflection.PushBlock.Size);
        Assert.True(reflection.PushBlock.Size <= SmallestPushDataBytes);
        Assert.Empty(reflection.Bindings);
    }

    [Fact]
    public void TheSensorBindsWhatThePassBinds()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Thermal);

        Assert.Equal(
            ["p", "uDepth", "uHeat", "uSource", "uTarget"],
            reflection.Bindings.Select(static b => b.Name).Order(StringComparer.Ordinal));
        Assert.Null(reflection.PushBlock);
    }

    [Fact]
    public void TheMaskCarriesTheBodysTransformDepthRowPoseAndClampedHeat()
    {
        Matrix4f transform = Matrix4f.Identity;
        ThermalBodyDraw draw = new(
            new PosedBody(null!, null!, null!, [], transform, PoseBlend: 0.25f),
            new System.Numerics.Vector4(0.1f, 0.2f, 0.3f, 4f),
            Heat: 1.5f);
        Span<float> push = stackalloc float[ThermalPass.MaskPushFloats];

        ThermalPass.WriteMask(push, draw);

        Assert.Equal(1f, push[0]);
        Assert.Equal(1f, push[15]);
        Assert.Equal([0.1f, 0.2f, 0.3f, 4f], push[16..20].ToArray());
        Assert.Equal(0.25f, push[20]);
        Assert.Equal(1f, push[21]);
    }

    [Fact]
    public void TheParametersFollowTheSensorsLayout()
    {
        ThermalFrame frame = new(1920, 1080, 960, 540, new DepthUnprojection(1.5f, 2f, 0.1f, 100000f), Exposure: 2f, Frame: 1030);
        ThermalDisplaySettings settings = new(Amount: 0.5f, ThermalPalette.Ironbow, Seconds: 3f, Calibration: 0.25f, Noise: 0.75f);
        Span<float> block = stackalloc float[ThermalPass.ParameterFloats];

        ThermalPass.WriteParameters(block, frame, settings);

        Assert.Equal([1920f, 1080f, 1f / 1920f, 1f / 1080f], block[0..4].ToArray());
        Assert.Equal([960f, 540f, 0.5f, 0.5f], block[4..8].ToArray());
        Assert.Equal([1.5f, 2f, 0.1f, 100000f], block[8..12].ToArray());
        Assert.Equal([0.5f, (float)(int)ThermalPalette.Ironbow, 3f, 0.25f], block[12..16].ToArray());
        Assert.Equal([2f, 0.75f, ThermalPass.SeenThroughReachMetres, 1030f % ThermalPass.GrainCycle], block[16..20].ToArray());
    }

    [Fact]
    public void ABlockOfTheWrongSizeIsRefused()
    {
        float[] block = new float[ThermalPass.ParameterFloats - 1];

        Assert.Throws<ArgumentException>(() => ThermalPass.WriteParameters(block, default, default));
    }
}

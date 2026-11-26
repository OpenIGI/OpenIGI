using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>A bomb's after-blast block is laid out as its shader reads it (<c>Post/BombAfterBlast.slang</c>).</summary>
public sealed class BombAfterBlastPassTests
{
    [Fact]
    public void TheShaderBindsWhatThePassBinds()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.BombAfterBlast);

        Assert.Equal(
            ["p", "uDepth", "uSource", "uTarget"],
            reflection.Bindings.Select(static b => b.Name).Order(StringComparer.Ordinal));
        Assert.Null(reflection.PushBlock);
    }

    [Fact]
    public void TheParametersFollowTheShadersLayout()
    {
        BombAfterBlastFrame frame = new(1920, 1080, 960, 540, new DepthUnprojection(1.5f, 2f, 0.1f, 100000f), Exposure: 2f, Frame: 1030);
        BombAfterBlastSettings settings = new(new Vector3f(1f, 2f, 30f), FrontMetres: 40f, Strength: 1.5f, Haze: 0.1f, Taps: 99);
        Span<float> block = stackalloc float[BombAfterBlastPass.ParameterFloats];

        BombAfterBlastPass.WriteParameters(block, frame, settings);

        Assert.Equal([1920f, 1080f, 1f / 1920f, 1f / 1080f], block[0..4].ToArray());
        Assert.Equal([960f, 540f, 0.5f, 0.5f], block[4..8].ToArray());
        Assert.Equal([1.5f, 2f, 0.1f, 100000f], block[8..12].ToArray());
        Assert.Equal([1f, 2f, 30f, 40f], block[12..16].ToArray());
        Assert.Equal(
            [BombAfterBlastSettings.EdgeMetres, BombAfterBlastSettings.WakeMetres, BombAfterBlastSettings.StreakShare, 1f],
            block[16..20].ToArray());
        Assert.Equal(
            [BombAfterBlastSettings.NearStartMetres, BombAfterBlastSettings.NearFullMetres, BombAfterBlastSettings.FarFullMetres, BombAfterBlastSettings.FarGoneMetres],
            block[20..24].ToArray());

        // The dust's grey, back in linear light and divided by the exposure, so the resolve shows it as it is seen.
        Assert.InRange(block[24], 0.078f / 2f, 0.079f / 2f);
        Assert.Equal(0.1f, block[27]);
        Assert.Equal([2f, 1030f % 1024f, 64f], block[28..31].ToArray());
    }

    [Fact]
    public void ABlockOfTheWrongSizeIsRefused()
    {
        float[] block = new float[BombAfterBlastPass.ParameterFloats - 1];

        Assert.Throws<ArgumentException>(() => BombAfterBlastPass.WriteParameters(block, default, default));
    }
}

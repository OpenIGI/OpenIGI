using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>That the flashlight's shadow dispatches read their blocks where the shaders declare them.</summary>
/// <remarks>The offsets are <c>Params</c> in <c>Deferred/FlashlightShadow.slang</c> and <c>FlashlightShadowFilter.slang</c>.</remarks>
public sealed class FlashlightShadowParametersTests
{
    [Fact]
    public void The_trace_carries_the_lens_the_beam_and_the_records()
    {
        float[] data = new float[FlashlightShadowParameters.TraceFloats];
        FlashlightLight light = FlashlightBlockTests.Torch() with { ViewAxis = new Vector3f(0f, 0f, 2f) };

        FlashlightShadowParameters.WriteTrace(
            data,
            1920,
            1080,
            Matrix3f.Identity,
            new Vector3f(10f, 20f, 30f),
            new DepthUnprojection(1.5f, 2.5f, 0.1f, 900f),
            light,
            frameIndex: 70000,
            cutoutRecords: 0x0000_0001_0000_0002UL);

        Assert.Equal(1f, data[0]);
        Expect(data, 16, 1.5f, 2.5f, 0.1f, 900f);
        Expect(data, 20, 1920f, 1080f, 1f / 1920f, 1f / 1080f);
        Expect(data, 24, 10f, 20f, 30f, 70000 % 65536);
        Expect(data, 28, 0.16f, -0.14f, 0.1f, 0.018f);

        // The axis arrives normalised whatever it was handed.
        Expect(data, 32, 0f, 0f, 1f);
        Assert.Equal(MathF.Cos(40f * MathF.PI / 180f), data[35], 5);

        Expect(data, 36, 40f, 0.12f, FlashlightShadowParameters.ScreenSteps);
        Assert.Equal(2u, BitConverter.SingleToUInt32Bits(data[40]));
        Assert.Equal(1u, BitConverter.SingleToUInt32Bits(data[41]));
    }

    [Fact]
    public void The_filter_carries_the_size_the_unprojection_and_the_lens()
    {
        float[] data = new float[FlashlightShadowParameters.FilterFloats];

        FlashlightShadowParameters.WriteFilter(data, 640, 360, new DepthUnprojection(1f, 2f, 0.1f, 500f), 0.02f);

        Expect(data, 0, 640f, 360f, 1f / 640f, 1f / 360f);
        Expect(data, 4, 1f, 2f, 0.1f, 500f);
        Assert.Equal(0.02f, data[8]);
    }

    [Fact]
    public void A_block_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() => FlashlightShadowParameters.WriteFilter(
            new float[3], 1, 1, new DepthUnprojection(1f, 1f, 0.1f, 1f), 0f));
    }

    private static void Expect(float[] actual, int at, params ReadOnlySpan<float> expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[at + i], 6);
        }
    }
}

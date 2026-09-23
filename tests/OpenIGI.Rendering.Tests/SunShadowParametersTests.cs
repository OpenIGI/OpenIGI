using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That the traced sun shadow's three blocks are filled where the SunShadow, ShadowDenoise and
/// ShadowAccumulate modules read them.
/// </summary>
/// <remarks>
/// Pinned by offset, as the deferred shading's are: both renderers now fill these through the one writer,
/// and a value one slot along traces, denoises or reprojects plausibly and wrongly without a report.
/// </remarks>
public sealed class SunShadowParametersTests
{
    private static readonly DepthUnprojection Unprojection = new(1.5f, 2.5f, 0.25f, 900f);

    [Fact]
    public void Each_block_is_the_size_its_shader_declares()
    {
        Assert.Equal(40, SunShadowParameters.TraceFloats);
        Assert.Equal(12, SunShadowParameters.DenoiseFloats);
        Assert.Equal(56, SunShadowParameters.AccumulateFloats);
    }

    [Fact]
    public void A_block_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SunShadowParameters.WriteDenoise(new float[8], 1, 1, Unprojection, 0.5f, 8f));
    }

    [Fact]
    public void The_record_address_travels_as_its_two_words()
    {
        float[] block = new float[SunShadowParameters.TraceFloats];
        SunShadowParameters.WriteTrace(
            block,
            1280,
            720,
            Matrix3f.Identity,
            new Vector3f(1f, 2f, 3f),
            Vector3f.UnitZ,
            Unprojection,
            0.5f,
            7f,
            0x0000_00AB_1234_5678UL);

        Assert.Equal(0x1234_5678u, BitConverter.SingleToUInt32Bits(block[36]));
        Assert.Equal(0xABu, BitConverter.SingleToUInt32Bits(block[37]));
        Assert.Equal(SunShadowParameters.RayLength, block[27]);
        Assert.Equal(1280f, block[20]);
        Assert.Equal(1f / 720f, block[23]);
        Assert.Equal(3f, block[30]);
        Assert.Equal(7f, block[32]);
    }

    [Fact]
    public void The_sun_disc_arrives_as_the_sine_for_the_trace_and_the_tangent_for_the_denoise()
    {
        float[] trace = new float[SunShadowParameters.TraceFloats];
        float[] denoise = new float[SunShadowParameters.DenoiseFloats];
        float halfAngle = float.DegreesToRadians(2f) * 0.5f;

        SunShadowParameters.WriteTrace(trace, 1, 1, Matrix3f.Identity, Vector3f.Zero, Vector3f.UnitZ, Unprojection, 2f, 0f, 0UL);
        SunShadowParameters.WriteDenoise(denoise, 1, 1, Unprojection, 2f, 16f);

        Assert.Equal(MathF.Sin(halfAngle), trace[31]);
        Assert.Equal(MathF.Tan(halfAngle), denoise[8]);
        Assert.Equal(1f / 16f, denoise[9]);
    }

    [Fact]
    public void The_accumulation_reprojects_through_last_frames_world_to_view()
    {
        Matrix3f previous = new(0f, 1f, 0f, -1f, 0f, 0f, 0f, 0f, 1f);
        TemporalFrameState state = new(
            Matrix3f.Identity,
            previous,
            new Vector3f(4f, 5f, 6f),
            Unprojection,
            new DepthUnprojection(3f, 4f, 0.5f, 800f),
            0.25f,
            -0.25f,
            0.125f,
            -0.125f,
            640,
            360);

        float[] block = new float[SunShadowParameters.AccumulateFloats];
        SunShadowParameters.WriteAccumulate(block, state, 8f, hasHistory: true);

        // Column-major, so the transpose's first column is the original's first row.
        Assert.Equal(previous.M11, block[16]);
        Assert.Equal(previous.M12, block[17]);
        Assert.Equal(previous.M21, block[20]);
        Assert.Equal(640f, block[36]);
        Assert.Equal(6f, block[42]);
        Assert.Equal(800f, block[47]);
        Assert.Equal(8f, block[48]);
        Assert.Equal(1f, block[49]);
        Assert.Equal(SunShadowParameters.DepthTolerance, block[50]);
        Assert.Equal(-0.125f, block[55]);
    }
}

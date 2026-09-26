using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That the lamp trace's block carries what the trace reads where it reads it.
/// </summary>
/// <remarks>
/// Pinned by offset, because the lamps are copied in behind a header and the record address rides inside
/// the copied lamp block: a slot one off shades plausibly and wrongly, and nothing reports it.
/// </remarks>
public sealed class LampShadowParametersTests
{
    private static readonly DepthUnprojection Unprojection = new(1f, 1f, 0.1f, 1000f);

    [Fact]
    public void The_cutout_records_ride_in_the_lamp_blocks_source_vector()
    {
        const ulong address = 0x0000_0456_8000_0010UL;
        float[] lamps = new float[DeferredLampBlock.Floats];
        lamps[4] = 0.25f;

        float[] trace = new float[LampShadowParameters.TraceFloats];
        LampShadowParameters.WriteTrace(
            trace, 8, 8, Matrix3f.Identity, Vector3f.Zero, Unprojection, 3, lamps, address);

        int source = LampShadowParameters.TraceHeaderFloats + 4;
        Assert.Equal(0.25f, trace[source]);
        Assert.Equal(0x8000_0010u, BitConverter.SingleToUInt32Bits(trace[source + 1]));
        Assert.Equal(0x0000_0456u, BitConverter.SingleToUInt32Bits(trace[source + 2]));
    }

    [Fact]
    public void Without_records_every_candidate_is_taken_as_solid()
    {
        float[] trace = new float[LampShadowParameters.TraceFloats];
        LampShadowParameters.WriteTrace(
            trace, 8, 8, Matrix3f.Identity, Vector3f.Zero, Unprojection, 3, new float[DeferredLampBlock.Floats]);

        int source = LampShadowParameters.TraceHeaderFloats + 4;
        Assert.Equal(0u, BitConverter.SingleToUInt32Bits(trace[source + 1]));
        Assert.Equal(0u, BitConverter.SingleToUInt32Bits(trace[source + 2]));
    }

    [Fact]
    public void The_visibility_is_traced_at_the_frames_own_resolution()
    {
        Assert.Equal(1920, LampShadowParameters.TracedSize(1920));
        Assert.Equal(1f, LampShadowParameters.JitterScale);
        Assert.Equal(1, LampShadowParameters.TracedSize(0));
    }
}

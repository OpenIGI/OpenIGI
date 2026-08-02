using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// Proves the compute seam works on the machine it is running on, once, at start-up.
/// </summary>
/// <remarks>
/// <para>
/// Everything from the distance fields onwards is built out of compute dispatches and shader-writable
/// buffers, and none of it produces a picture until several stages are in place. So a fault in the
/// plumbing — a driver that reports 4.3 and mis-binds a buffer, a barrier that does not order what it
/// claims to — would first show up as a wrong image a long way from its cause.
/// </para>
/// <para>
/// This runs one dispatch whose answer is known by construction and reads it back. It costs a
/// millisecond at load and turns "the lighting looks wrong" into "compute does not work here", which
/// is the difference between a day and a minute.
/// </para>
/// <para>
/// <b>It checks ordering as well as arithmetic.</b> The second dispatch reads what the first wrote, so
/// a missing or ineffective barrier gives a wrong answer rather than a lucky one — which is the fault
/// most likely to survive into a stage that cannot be debugged from its output.
/// </para>
/// </remarks>
public static class GLComputeSelfTest
{
    private const string LogCategory = "Compute";

    /// <summary>How many values the test runs over. Deliberately more than one work group.</summary>
    private const int Count = 1024;

    private const string FillSource =
        """
        #version 430 core

        layout(local_size_x = 64) in;

        layout(std430, binding = 0) buffer Values
        {
            uint uValues[];
        };

        // Signed, and it matters: the seam sets scalars with glUniform1i, and GL will not feed an int
        // into a uint uniform -- it leaves it at zero and reports nothing. That made the fill dispatch
        // write nothing at all, which the double dispatch then faithfully doubled.
        uniform int uCount;

        void main()
        {
            int index = int(gl_GlobalInvocationID.x);

            if (index < uCount)
            {
                uValues[index] = uint(index);
            }
        }
        """;

    private const string DoubleSource =
        """
        #version 430 core

        layout(local_size_x = 64) in;

        layout(std430, binding = 0) buffer Values
        {
            uint uValues[];
        };

        uniform int uCount;

        void main()
        {
            int index = int(gl_GlobalInvocationID.x);

            // Reads what the previous dispatch wrote. Without a barrier between the two this is a race
            // rather than a sequence, and on most drivers it happens to work -- which is exactly why it
            // is worth testing deliberately rather than trusting.
            if (index < uCount)
            {
                uValues[index] = uValues[index] * 2u;
            }
        }
        """;

    /// <summary>Runs the test and reports what it found.</summary>
    /// <param name="device">The device to test. Must be a live GL device.</param>
    /// <param name="logger">Where to report the result.</param>
    /// <returns><see langword="true"/> when compute is available and produced the right answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public static bool Run(IGraphicsDevice device, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        IIgiLogger log = logger ?? NullLogger.Instance;

        if (!device.Capabilities.SupportsCompute)
        {
            log.Information(
                LogCategory,
                "No compute on this context; the stages that need it will not run.");
            return false;
        }

        ComputeShaderHandle fill = default;
        ComputeShaderHandle twice = default;
        StorageBufferHandle values = default;

        try
        {
            fill = device.CreateComputeShader(FillSource, "selftest.fill");
            twice = device.CreateComputeShader(DoubleSource, "selftest.double");
            values = device.CreateStorageBuffer(Count * sizeof(uint), "selftest.values");

            device.BindStorageBuffer(0, values);

            const int groupSize = 64;
            int groups = (Count + groupSize - 1) / groupSize;

            device.SetComputeUniform(fill, "uCount", Count);
            device.Dispatch(fill, groups);

            // The whole point of the second dispatch: it must see the first one's writes.
            device.MemoryBarrier(MemoryBarrierScope.StorageBuffer);

            device.SetComputeUniform(twice, "uCount", Count);
            device.Dispatch(twice, groups);
            device.MemoryBarrier(MemoryBarrierScope.StorageBuffer);

            uint[] readback = new uint[Count];
            device.ReadStorageBuffer<uint>(values, readback);

            for (int i = 0; i < Count; i++)
            {
                if (readback[i] != (uint)i * 2u)
                {
                    log.Error(
                        LogCategory,
                        $"Compute is available but wrong: value {i} came back as {readback[i]}, expected {i * 2}.");
                    return false;
                }
            }

            log.Information(LogCategory, $"Compute verified: {Count} values over {groups} work groups.");
            return true;
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The compute self-test did not run: {error.Message}");
            return false;
        }
        finally
        {
            device.BindStorageBuffer(0, StorageBufferHandle.None);
            device.DestroyStorageBuffer(values);
            device.DestroyComputeShader(twice);
            device.DestroyComputeShader(fill);
        }
    }
}

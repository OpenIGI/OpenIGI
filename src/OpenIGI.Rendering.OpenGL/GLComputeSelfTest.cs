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

    /// <summary>How wide the volume the image test writes into is. Not a multiple of four, on purpose.</summary>
    /// <remarks>
    /// A single-channel volume whose width is not a multiple of four is exactly what catches a wrong
    /// row alignment: GL's default is four bytes, so every row after the first slides by the padding
    /// that is not there and the volume shears diagonally. A width of 32 would have passed regardless.
    /// </remarks>
    private const int VolumeWidth = 13;

    private const int VolumeHeight = 5;

    private const int VolumeDepth = 3;

    private const string VolumeSource =
        """
        #version 430 core

        layout(local_size_x = 4, local_size_y = 4, local_size_z = 1) in;

        // r8, matching the volume's own format. An image binding reinterprets the storage through
        // whatever format it is declared with and GL reports no mismatch, so declaring this rgba8 would
        // write a quarter of the volume and leave the rest untouched.
        layout(r8, binding = 0) uniform writeonly image3D uVolume;

        uniform int uWidth;
        uniform int uHeight;
        uniform int uDepth;

        void main()
        {
            ivec3 at = ivec3(gl_GlobalInvocationID);

            if (at.x >= uWidth || at.y >= uHeight || at.z >= uDepth)
            {
                return;
            }

            // A value that depends on all three coordinates, so a transposed axis or a sheared row
            // cannot produce the expected pattern by coincidence.
            int index = ((at.z * uHeight) + at.y) * uWidth + at.x;
            imageStore(uVolume, at, vec4(float(index % 255) / 255.0, 0.0, 0.0, 0.0));
        }
        """;

    private const string VolumeReadSource =
        """
        #version 430 core

        layout(local_size_x = 4, local_size_y = 4, local_size_z = 1) in;

        layout(r8, binding = 0) uniform readonly image3D uVolume;

        layout(std430, binding = 1) buffer Readback
        {
            uint uReadback[];
        };

        uniform int uWidth;
        uniform int uHeight;
        uniform int uDepth;

        void main()
        {
            ivec3 at = ivec3(gl_GlobalInvocationID);

            if (at.x >= uWidth || at.y >= uHeight || at.z >= uDepth)
            {
                return;
            }

            int index = ((at.z * uHeight) + at.y) * uWidth + at.x;

            // Back through the byte range it was stored in, so the comparison is against what was
            // written rather than against a float that rounded on the way out.
            uReadback[index] = uint(round(imageLoad(uVolume, at).r * 255.0));
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

            if (!VerifyVolumes(device, log))
            {
                return false;
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

    /// <summary>Writes a pattern into a volume through an image binding, and reads it back.</summary>
    /// <remarks>
    /// <para>
    /// Volumes are what every stage after this one is built out of — the distance field atlas, the
    /// clipmaps, the radiance caches — and all three of the ways this can go wrong are silent. An image
    /// declared with the wrong format reinterprets the storage and GL reports nothing. A row alignment
    /// left at its default shears a single-channel volume diagonally. An axis transposed in the index
    /// arithmetic produces a full volume of entirely plausible values.
    /// </para>
    /// <para>
    /// So the pattern written depends on all three coordinates, and the volume's width is deliberately
    /// not a multiple of four. A cube of a round size would pass under every one of those faults.
    /// </para>
    /// </remarks>
    private static bool VerifyVolumes(IGraphicsDevice device, IIgiLogger log)
    {
        const int count = VolumeWidth * VolumeHeight * VolumeDepth;

        ComputeShaderHandle write = default;
        ComputeShaderHandle read = default;
        TextureHandle volume = default;
        StorageBufferHandle readback = default;

        try
        {
            write = device.CreateComputeShader(VolumeSource, "selftest.volume.write");
            read = device.CreateComputeShader(VolumeReadSource, "selftest.volume.read");
            volume = device.CreateVolumeTexture(
                VolumeWidth, VolumeHeight, VolumeDepth, VolumeFormat.R8, "selftest.volume");
            readback = device.CreateStorageBuffer(count * sizeof(uint), "selftest.volume.readback");

            int groupsX = (VolumeWidth + 3) / 4;
            int groupsY = (VolumeHeight + 3) / 4;

            device.BindImage(0, volume, ImageAccess.Write);
            device.SetComputeUniform(write, "uWidth", VolumeWidth);
            device.SetComputeUniform(write, "uHeight", VolumeHeight);
            device.SetComputeUniform(write, "uDepth", VolumeDepth);
            device.Dispatch(write, groupsX, groupsY, VolumeDepth);

            device.MemoryBarrier(MemoryBarrierScope.Image);

            device.BindImage(0, volume, ImageAccess.Read);
            device.BindStorageBuffer(1, readback);
            device.SetComputeUniform(read, "uWidth", VolumeWidth);
            device.SetComputeUniform(read, "uHeight", VolumeHeight);
            device.SetComputeUniform(read, "uDepth", VolumeDepth);
            device.Dispatch(read, groupsX, groupsY, VolumeDepth);

            device.MemoryBarrier(MemoryBarrierScope.StorageBuffer);

            uint[] values = new uint[count];
            device.ReadStorageBuffer<uint>(readback, values);

            for (int i = 0; i < count; i++)
            {
                if (values[i] != (uint)(i % 255))
                {
                    log.Error(
                        LogCategory,
                        $"Volume images are wrong: voxel {i} came back as {values[i]}, expected {i % 255}.");
                    return false;
                }
            }

            log.Information(
                LogCategory,
                $"Volume images verified: {VolumeWidth}x{VolumeHeight}x{VolumeDepth} R8 written and read back.");

            return true;
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The volume self-test did not run: {error.Message}");
            return false;
        }
        finally
        {
            device.BindImage(0, TextureHandle.None, ImageAccess.Read);
            device.BindStorageBuffer(1, StorageBufferHandle.None);
            device.DestroyStorageBuffer(readback);
            device.DestroyTexture(volume);
            device.DestroyComputeShader(read);
            device.DestroyComputeShader(write);
        }
    }
}

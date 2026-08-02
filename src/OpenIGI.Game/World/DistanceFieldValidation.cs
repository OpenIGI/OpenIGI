using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Checks a clipmap the GPU built against the exact answer the CPU can compute.
/// </summary>
/// <remarks>
/// <para>
/// A distance field is a volume of plausible-looking numbers, and every way of building one wrongly
/// produces a volume of equally plausible-looking numbers. Nothing downstream fails visibly either — a
/// trace against a bad field does not error, it returns the wrong surface, and the first symptom is
/// lighting that looks subtly off several stages later.
/// </para>
/// <para>
/// <see cref="SceneDistanceField"/> exists precisely so there is something to compare against: it
/// answers the same question exactly, by consulting every object. This reads the clipmap back and asks
/// both the same thing at the same world points.
/// </para>
/// <para>
/// <b>What counts as agreement.</b> The clipmap is a resampling, so it is allowed to differ by about
/// what its own voxels can resolve. What it is not allowed to do is disagree about the <em>sign</em>,
/// or report more free space than exists — the first means a trace cannot tell a wall from the air
/// beside it, and the second means it steps straight through one.
/// </para>
/// </remarks>
public static class DistanceFieldValidation
{
    private const string LogCategory = "DistanceField";

    /// <summary>What the comparison found.</summary>
    /// <param name="Samples">How many points were compared.</param>
    /// <param name="MeanError">The mean absolute difference, in metres.</param>
    /// <param name="WorstError">The largest difference, in metres.</param>
    /// <param name="SignDisagreements">How many points the two disagreed about being inside geometry.</param>
    /// <param name="Overstatements">
    /// How many points the clipmap claimed more free space at than exists, beyond what its resolution
    /// excuses. The number that matters: a sphere trace steps by whatever the field reports.
    /// </param>
    public readonly record struct Report(
        int Samples, float MeanError, float WorstError, int SignDisagreements, int Overstatements)
    {
        /// <summary>Gets a value indicating whether the clipmap is fit to trace against.</summary>
        public bool Passed => Samples > 0 && SignDisagreements == 0 && Overstatements == 0;

        /// <summary>Returns the findings as one line.</summary>
        /// <returns>A short description.</returns>
        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{Samples} sample(s): mean {MeanError:0.###} m, worst {WorstError:0.###} m, "
            + $"{SignDisagreements} sign disagreement(s), {Overstatements} overstatement(s)");
    }

    /// <summary>Reads one clipmap level back, through a compute shader.</summary>
    /// <remarks>
    /// <b>Not through glGetTexImage, and that is the whole point of this method existing.</b> Measured
    /// on the driver this was developed against: glGetTexImage does not observe a compute shader's
    /// imageStore writes at all — not with TEXTURE_UPDATE, not with ALL_BARRIER_BITS, not after a
    /// finish, and with no GL error raised. A volume uploaded from memory reads back through that path
    /// perfectly, so what fails is specifically image-store visibility.
    /// <para>
    /// So the values come back the way the compute self-test already proves works: a shader reads the
    /// image and writes a storage buffer, which reads back correctly. Without this the validator
    /// reported every clipmap as entirely unwritten, and that false negative cost two stages.
    /// </para>
    /// </remarks>
    private static byte[] Read(
        IGraphicsDevice device, GlobalDistanceField clipmaps, int level, IIgiLogger log)
    {
        GlobalDistanceFieldSettings settings = clipmaps.Settings;
        int count = settings.VoxelsPerLevel;

        ComputeShaderHandle shader = default;
        StorageBufferHandle buffer = default;

        try
        {
            shader = device.CreateComputeShader(ReadSource, "sdf.clipmap.read");
            buffer = device.CreateStorageBuffer(count * sizeof(uint), "sdf.clipmap.readback");

            device.BindImage(0, clipmaps.Clipmaps, ImageAccess.Read);
            device.BindStorageBuffer(0, buffer);
            device.SetComputeUniform(shader, "uResolution", settings.Resolution);
            device.SetComputeUniform(shader, "uLevel", level);

            int groups = (settings.Resolution + 3) / 4;
            device.Dispatch(shader, groups, groups, groups);
            device.MemoryBarrier(MemoryBarrierScope.StorageBuffer);

            uint[] values = new uint[count];
            device.ReadStorageBuffer<uint>(buffer, values);

            byte[] bytes = new byte[count];

            for (int i = 0; i < count; i++)
            {
                bytes[i] = (byte)Math.Clamp(values[i], 0u, 255u);
            }

            return bytes;
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The clipmap read-back failed: {error.Message}");
            return [];
        }
        finally
        {
            device.BindImage(0, TextureHandle.None, ImageAccess.Read);
            device.BindStorageBuffer(0, StorageBufferHandle.None);
            device.DestroyStorageBuffer(buffer);
            device.DestroyComputeShader(shader);
        }
    }

    private const string ReadSource =
        """
        #version 430 core

        layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

        layout(r8, binding = 0) uniform readonly image3D uClipmaps;

        layout(std430, binding = 0) buffer Readback
        {
            uint uValues[];
        };

        uniform int uResolution;
        uniform int uLevel;

        void main()
        {
            ivec3 at = ivec3(gl_GlobalInvocationID);

            if (any(greaterThanEqual(at, ivec3(uResolution))))
            {
                return;
            }

            int index = (((at.z * uResolution) + at.y) * uResolution) + at.x;

            // Back through the byte range it was stored in, so the comparison is against what was
            // written rather than a float that rounded on the way out.
            uValues[index] = uint(round(
                imageLoad(uClipmaps, ivec3(at.xy, (uLevel * uResolution) + at.z)).r * 255.0));
        }
        """;

    /// <summary>Compares one clipmap level against the exact field.</summary>
    /// <param name="device">The device holding the clipmaps.</param>
    /// <param name="clipmaps">The built clipmaps.</param>
    /// <param name="scene">The exact field they resample.</param>
    /// <param name="level">Which level to check, zero being the innermost.</param>
    /// <param name="logger">Where to report the findings.</param>
    /// <returns>What the comparison found.</returns>
    /// <remarks>
    /// Every voxel of the level is compared rather than a sample of them. It costs a readback and a few
    /// hundred thousand CPU evaluations, which is seconds — affordable because this runs when asked
    /// rather than every frame, and worth it because a fault confined to one corner of the volume is
    /// exactly the kind a sample would miss.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Report Compare(
        IGraphicsDevice device,
        GlobalDistanceField clipmaps,
        SceneDistanceField scene,
        int level = 0,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(clipmaps);
        ArgumentNullException.ThrowIfNull(scene);

        IIgiLogger log = logger ?? NullLogger.Instance;
        GlobalDistanceFieldSettings settings = clipmaps.Settings;
        level = Math.Clamp(level, 0, settings.Levels - 1);

        int resolution = settings.Resolution;
        int perLevel = settings.VoxelsPerLevel;

        byte[] volume = Read(device, clipmaps, level, log);

        if (volume.Length == 0)
        {
            log.Error(LogCategory, "The clipmap could not be read back; nothing was compared.");
            return default;
        }

        // What actually landed in the volume, before any interpretation. A clipmap that was never
        // written reads as a single value, and saying so directly is the difference between "the
        // approximation disagrees" and "the pass did not run".
        int rawMin = 255;
        int rawMax = 0;
        long rawTotal = 0;

        for (int i = 0; i < perLevel; i++)
        {
            byte raw = volume[i];
            rawMin = Math.Min(rawMin, raw);
            rawMax = Math.Max(rawMax, raw);
            rawTotal += raw;
        }

        log.Information(
            LogCategory,
            $"Clipmap level {level} raw bytes: min {rawMin}, max {rawMax}, mean {rawTotal / (double)perLevel:0.#}.");

        Vector3d origin = clipmaps.Origins[level];
        float voxel = settings.VoxelSizeOf(level);
        // The same bound the build uses. Four voxels: a grid cannot honour a distance far larger than
        // its own resolution, and a ray wanting to step further steps in the next level out.
        float range = voxel * 4f;

        // What the resampling is allowed to be wrong by. A voxel and a half: the clipmap stores one
        // value per voxel and reads back through a trilinear filter, so anything finer than that is
        // asking a grid to know what it did not record.
        float tolerance = voxel * 1.5f;

        double total = 0;
        float worst = 0f;
        int signs = 0;
        int overstated = 0;
        int compared = 0;

        for (int z = 0; z < resolution; z++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int at = (((z * resolution) + y) * resolution) + x;
                    float built = MeshDistanceField.Decode(volume[at], range);

                    Vector3d point = new(
                        origin.X + ((x + 0.5) * voxel),
                        origin.Y + ((y + 0.5) * voxel),
                        origin.Z + ((z + 0.5) * voxel));

                    float exact = scene.Sample(point);

                    // Only the band the clipmap can actually represent is checked, and the test is on
                    // the MAGNITUDE rather than the value. A saturated voxel carries no information
                    // beyond "at least this far", in either direction: deep inside a large model the
                    // exact field reports hundreds of metres of interior while the clipmap clamps to a
                    // couple, and both are right. Testing only the positive side scored every such
                    // voxel as a several-hundred-metre error and reported a correct clipmap as broken.
                    if (MathF.Abs(built) >= range - tolerance || MathF.Abs(exact) >= range - tolerance)
                    {
                        continue;
                    }

                    compared++;
                    float error = MathF.Abs(built - exact);
                    total += error;
                    worst = MathF.Max(worst, error);

                    if (built < 0f != exact < 0f && error > tolerance)
                    {
                        signs++;
                    }

                    if (built > exact + tolerance)
                    {
                        overstated++;
                    }
                }
            }
        }

        Report report = new(
            compared, compared > 0 ? (float)(total / compared) : 0f, worst, signs, overstated);

        if (report.Passed)
        {
            log.Information(LogCategory, $"Clipmap level {level} agrees with the exact field: {report}.");
        }
        else
        {
            log.Error(LogCategory, $"Clipmap level {level} does NOT agree with the exact field: {report}.");
        }

        return report;
    }
}

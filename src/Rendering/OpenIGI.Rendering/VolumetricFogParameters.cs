using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering;

/// <summary>Everything one frame's fog needs to know about the medium and the lights in it.</summary>
/// <param name="Settings">The medium itself.</param>
/// <param name="Volumes">The level's local volumes, already rebased against the camera.</param>
/// <param name="Lamps">The frame's local lights, in view space.</param>
/// <param name="LampCount">How many of <paramref name="Lamps"/> are live.</param>
/// <param name="SunDirection">The view-space direction towards the sun.</param>
/// <param name="SunColour">The sun's colour, already scaled by the frame's light scale.</param>
/// <param name="Ambient">The flat ambient, for a frame whose sky has no harmonics.</param>
/// <param name="SkyValid">Whether the sky's harmonics are worth reading.</param>
/// <param name="BaseHeight">The medium's base height as an offset from the camera, in metres.</param>
/// <param name="Flashlight">The player's flashlight, whose beam the integration lights; off by default.</param>
public readonly record struct VolumetricFogFrame(
    VolumetricFogSettings Settings,
    ReadOnlyMemory<LocalFogVolume> Volumes,
    ReadOnlyMemory<DeferredLocalLight> Lamps,
    int LampCount,
    Vector3f SunDirection,
    Vector3f SunColour,
    Vector3f Ambient,
    bool SkyValid,
    float BaseHeight,
    FlashlightLight Flashlight = default);

/// <summary>Where the camera is this frame and was the last, as the fog's history reprojection needs it.</summary>
/// <param name="ViewToWorld">This frame's view rotation.</param>
/// <param name="PreviousViewToWorld">The previous frame's.</param>
/// <param name="CameraDelta">This frame's camera position minus the previous frame's.</param>
/// <param name="Unprojection">This frame's projection scales and planes.</param>
/// <param name="PreviousUnprojection">The previous frame's, unjittered.</param>
/// <param name="CameraWorld">Where the camera stands in the world, which the roof map is addressed in.</param>
public readonly record struct VolumetricFogCamera(
    Matrix3f ViewToWorld,
    Matrix3f PreviousViewToWorld,
    Vector3f CameraDelta,
    DepthUnprojection Unprojection,
    DepthUnprojection PreviousUnprojection,
    Vector3f CameraWorld);

/// <summary>
/// The volumetric fog's grid and the parameter block its two dispatches read, the same for every backend.
/// </summary>
/// <remarks>
/// <para>
/// The grid is a frustum in disguise -- screen tiles across and down, depth slices deep -- so a lookup from
/// the shading pass is a single fetch at the pixel's own screen position and depth. The FogScatter module
/// lights it, FogIntegrate accumulates it front to back, and the shading samples the result.
/// </para>
/// <para>
/// Written once here so that the renderers cannot drift apart: the parameter block's layout is the
/// shaders' FogParams, and the slice distribution is what the shading must agree on to find the slice the
/// integration wrote.
/// </para>
/// </remarks>
public static class VolumetricFogParameters
{
    /// <summary>How many screen pixels one froxel spans, across and down.</summary>
    /// <remarks>
    /// Sixteen. Every lamp is tested against every cell, so the cost grows with the cell count, and sixteen
    /// keeps that count low for a medium whose whole nature is low frequency.
    /// </remarks>
    public const int TilePixels = 16;

    /// <summary>The fewest depth slices the quality dial can ask for.</summary>
    public const int MinSlices = 16;

    /// <summary>The most depth slices the quality dial can ask for.</summary>
    public const int MaxSlices = 128;

    /// <summary>How much of the previous frame's answer a froxel keeps.</summary>
    /// <remarks>
    /// Nine tenths: the new sample contributes a tenth, so the eight jitter offsets are averaged over
    /// roughly a cycle while a change in the lighting still settles within a few tenths of a second.
    /// </remarks>
    public const float HistoryWeight = 0.9f;

    /// <summary>How far the camera may travel in one frame before the accumulated volume is thrown away, in metres.</summary>
    /// <remarks>
    /// A walk or a sprint moves a fraction of this and the reprojection handles it. A jump this large is a
    /// teleport -- a level change, a spawn, a camera cut -- and reprojecting through it drags the fog of
    /// one place over another for the ten or so frames the blend takes to forget it.
    /// </remarks>
    public const float TeleportMetres = 8f;

    /// <summary>A depth bias on the shadow lookup, in the map's own normalised depth.</summary>
    /// <remarks>
    /// Small, and constant across cascades: a point of air does not shadow itself, and the only thing this
    /// has to clear is the depth quantisation of the map.
    /// </remarks>
    public const float ShadowBias = 0.0008f;

    /// <summary>The number of floats in the parameter block both dispatches read.</summary>
    /// <remarks>
    /// Two rotations and four cascades of matrix, seventeen vectors of scalars, then the volumes, the
    /// lamps and the flashlight.
    /// </remarks>
    public const int Floats =
        (16 * (2 + 4)) + (17 * 4) + (LocalFogVolume.MaxPerLevel * 4 * 2) + (DeferredLocalLight.Maximum * 4 * 2)
        + FlashlightBlock.Floats;

    /// <summary>How quickly the depth slices grow with distance.</summary>
    /// <remarks>
    /// The slice index runs as this many times the base-two logarithm of an affine function of depth, so a
    /// larger value spaces the slices more evenly and a smaller one crowds them towards the eye. Thirty-two
    /// keeps the near slices a fraction of a metre thick while the far ones still reach the fog's distance.
    /// </remarks>
    public const float DepthDistributionScale = 32f;

    /// <summary>How far beyond the near plane the first slice starts, in metres.</summary>
    /// <remarks>
    /// The air between the lens and the near plane plus this is too thin to matter, and starting the grid a
    /// little out keeps the first, thinnest slices from being spent on it.
    /// </remarks>
    public const double NearOffsetMetres = 0.095;


    // The offsets each frame samples its froxels at, a Halton (2, 3, 5) sequence: low discrepancy in all
    // three axes at once, so eight frames of it cover a cell evenly rather than clustering along one.
    private static readonly Vector3f[] JitterOffsets = BuildJitterOffsets(8);

    /// <summary>The grid for a render size and a quality.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <param name="slices">How many depth slices the quality dial asks for.</param>
    /// <returns>The froxel counts across, down and deep.</returns>
    public static (int X, int Y, int Z) Grid(int width, int height, int slices) =>
    (
        Math.Max(1, (width + TilePixels - 1) / TilePixels),
        Math.Max(1, (height + TilePixels - 1) / TilePixels),
        Math.Clamp(slices, MinSlices, MaxSlices));

    /// <summary>
    /// The exponential slice distribution, pinned so that slice 0 lies at the effective near depth and the
    /// last slice's far face at the fog's reach.
    /// </summary>
    /// <remarks>
    /// Depth runs as <c>z(s) = n + (f - n) (2^(s/S) - 1) / (2^(N/S) - 1)</c>, which the shaders evaluate in
    /// the form <c>s = S log2(B z + O)</c>. Solved in double precision: the offset is a small difference of
    /// large terms when the slices are many.
    /// </remarks>
    /// <param name="near">The near plane, in metres.</param>
    /// <param name="far">How far the fog reaches, in metres.</param>
    /// <param name="slices">How many slices the grid is deep.</param>
    /// <returns>B, O and S, with one over the slice count.</returns>
    public static FogSliceDistribution SliceDistribution(float near, float far, int slices)
    {
        int count = Math.Max(1, slices);
        double n = Math.Max(near, 0.01) + NearOffsetMetres;
        double f = Math.Max(far, n + 1.0);
        double scale = DepthDistributionScale;

        double growth = Math.Pow(2.0, count / scale);
        double offset = (f - (n * growth)) / (f - n);
        double b = (1.0 - offset) / n;

        return new FogSliceDistribution((float)b, (float)offset, (float)scale, 1f / count);
    }

    /// <summary>Fills the parameter block both dispatches read.</summary>
    /// <param name="data">The block, <see cref="Floats"/> long.</param>
    /// <param name="fog">The medium and its lights.</param>
    /// <param name="camera">Where the camera is and was.</param>
    /// <param name="grid">The froxel counts, from <see cref="Grid"/>.</param>
    /// <param name="cascadeMatrices">Each cascade's camera-relative world to light clip.</param>
    /// <param name="cascadeSplits">The view depth each cascade stops at.</param>
    /// <param name="cascadeCount">How many cascades are live; nought for none.</param>
    /// <param name="shelter">How to read the roof map, or <see cref="ShelterMapTransform.None"/>.</param>
    /// <param name="frameIndex">Which fog frame this is, which picks the jitter.</param>
    /// <param name="hasHistory">Whether the previous frame's scattering is worth blending.</param>
    /// <returns>The slice distribution, which the shading pass needs to find a pixel's slice.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not the block's length.</exception>
    public static FogSliceDistribution Write(
        Span<float> data,
        in VolumetricFogFrame fog,
        in VolumetricFogCamera camera,
        (int X, int Y, int Z) grid,
        ReadOnlySpan<Matrix4f> cascadeMatrices,
        ReadOnlySpan<float> cascadeSplits,
        int cascadeCount,
        in ShelterMapTransform shelter,
        long frameIndex,
        bool hasHistory)
    {
        if (data.Length != Floats)
        {
            throw new ArgumentException($"The fog block is exactly {Floats} floats.", nameof(data));
        }

        data.Clear();

        VolumetricFogSettings settings = fog.Settings;
        FogSliceDistribution z = SliceDistribution(camera.Unprojection.Near, MathF.Max(settings.Distance, 1f), grid.Z);

        TemporalParameters.WriteRotation(data, 0, camera.ViewToWorld);
        TemporalParameters.WriteRotation(data, 16, camera.PreviousViewToWorld.Transposed());

        for (int i = 0; i < 4; i++)
        {
            Matrix4f cascade = i < cascadeCount ? cascadeMatrices[i] : Matrix4f.Identity;
            cascade.CopyColumnMajorTo(data.Slice(32 + (i * 16), 16));
        }

        for (int i = 0; i < 4; i++)
        {
            data[96 + i] = i < cascadeCount ? cascadeSplits[i] : 0f;
        }

        Vector4(data, 100, grid.X, grid.Y, grid.Z, HistoryWeight);
        Vector4(data, 104, camera.Unprojection.XScale, camera.Unprojection.YScale, camera.Unprojection.Near, camera.Unprojection.Far);
        Vector4(
            data,
            108,
            camera.PreviousUnprojection.XScale,
            camera.PreviousUnprojection.YScale,
            camera.PreviousUnprojection.Near,
            camera.PreviousUnprojection.Far);
        Vector4(data, 112, z.B, z.O, z.Scale, z.InverseSlices);
        Vector4(
            data,
            116,
            MathF.Max(settings.Density, 0f),
            1f / MathF.Max(settings.HeightFalloff, 0.01f),
            fog.BaseHeight,
            Math.Clamp(settings.Anisotropy, -0.95f, 0.95f));
        Vector4(
            data,
            120,
            Math.Clamp(settings.UniformFraction, 0f, 1f),
            Math.Clamp(settings.MultipleScattering, 0f, 1f),
            MathF.Max(settings.SunIntensity, 0f),
            MathF.Max(settings.AmbientIntensity, 0f));

        Vector3f albedo = settings.ScatteringAlbedo;
        Vector4(data, 124, albedo.X, albedo.Y, albedo.Z, cascadeCount);
        Vector4(data, 128, fog.SunDirection.X, fog.SunDirection.Y, fog.SunDirection.Z, 0f);
        Vector4(data, 132, fog.SunColour.X, fog.SunColour.Y, fog.SunColour.Z, ShadowBias);
        Vector4(data, 136, fog.Ambient.X, fog.Ambient.Y, fog.Ambient.Z, fog.SkyValid ? 1f : 0f);

        int lampCount = Math.Clamp(fog.LampCount, 0, Math.Min(DeferredLocalLight.Maximum, fog.Lamps.Length));
        Vector4(data, 140, camera.CameraDelta.X, camera.CameraDelta.Y, camera.CameraDelta.Z, lampCount);

        Vector3f jitter = JitterOffsets[(int)(frameIndex % JitterOffsets.Length)];
        Vector4(data, 144, jitter.X, jitter.Y, jitter.Z, hasHistory ? 1f : 0f);

        // Where the camera stands in the world, and the roof map's own transform. In world metres and not
        // camera-relative, unlike everything around it: the map is a fixed thing in the level, and a
        // froxel has to put the camera back on to address it.
        Vector4(data, 148, camera.CameraWorld.X, camera.CameraWorld.Y, camera.CameraWorld.Z, 0f);
        Vector4(
            data,
            152,
            shelter.OriginX,
            shelter.OriginY,
            shelter.CellMetres > 0f ? 1f / shelter.CellMetres : 0f,
            shelter.LowestZ);
        Vector4(data, 156, shelter.HeightScale, shelter.HeadroomMetres, shelter.ReachMetres, shelter.IsPresent ? 1f : 0f);
        data[160] = shelter.Width;
        data[161] = shelter.Height;

        int centres = 164;
        int extents = centres + (LocalFogVolume.MaxPerLevel * 4);
        ReadOnlySpan<LocalFogVolume> volumes = fog.Volumes.Span;
        for (int i = 0; i < LocalFogVolume.MaxPerLevel && i < volumes.Length; i++)
        {
            LocalFogVolume volume = volumes[i];
            if (!volume.IsMeaningful)
            {
                continue;
            }

            Vector4(
                data,
                centres + (i * 4),
                volume.Centre.X,
                volume.Centre.Y,
                volume.Centre.Z,
                volume.Shape == FogVolumeShape.Sphere ? 1f : 0f);
            Vector4(data, extents + (i * 4), volume.Extent.X, volume.Extent.Y, volume.Extent.Z, volume.DensityScale);
        }

        int positions = extents + (LocalFogVolume.MaxPerLevel * 4);
        int colours = positions + (DeferredLocalLight.Maximum * 4);
        ReadOnlySpan<DeferredLocalLight> lamps = fog.Lamps.Span;
        for (int i = 0; i < lampCount; i++)
        {
            DeferredLocalLight lamp = lamps[i];
            Vector4(data, positions + (i * 4), lamp.ViewPosition.X, lamp.ViewPosition.Y, lamp.ViewPosition.Z, lamp.Radius);
            Vector4(data, colours + (i * 4), lamp.Colour.X, lamp.Colour.Y, lamp.Colour.Z, lamp.Intensity);
        }

        // Last, so nothing before it moved when it arrived. The integration lights the beam; the mask slot
        // is the shading's, and the air has no shadow to read.
        FlashlightBlock.Write(
            data.Slice(colours + (DeferredLocalLight.Maximum * 4), FlashlightBlock.Floats),
            fog.Flashlight,
            shadowMask: false);

        return z;
    }

    private static void Vector4(Span<float> data, int at, float x, float y, float z, float w)
    {
        data[at + 0] = x;
        data[at + 1] = y;
        data[at + 2] = z;
        data[at + 3] = w;
    }

    private static Vector3f[] BuildJitterOffsets(int count)
    {
        Vector3f[] offsets = new Vector3f[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = new Vector3f(Halton(i + 1, 2), Halton(i + 1, 3), Halton(i + 1, 5));
        }

        return offsets;
    }

    // One term of a Halton sequence: a low-discrepancy point in [0, 1). The base must differ per axis or
    // the axes correlate.
    private static float Halton(int index, int radix)
    {
        float result = 0f;
        float fraction = 1f / radix;
        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }

        return result;
    }
}

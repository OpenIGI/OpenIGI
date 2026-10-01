using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering;

/// <summary>Where a reflected ray finds the surface cache: the lookup grid and the atlas it points into.</summary>
/// <param name="CameraToCache">The camera's position in the cache's own frame.</param>
/// <param name="LookupCell">The lookup grid's cell, in metres.</param>
/// <param name="LookupMinimum">The grid's lowest corner.</param>
/// <param name="LookupBuckets">How many buckets the grid hashes into.</param>
/// <param name="AtlasWidth">The card atlas's width in texels.</param>
/// <param name="AtlasHeight">Its height.</param>
/// <param name="BounceScale">How much of a card's light a hit returns.</param>
public readonly record struct ReflectionCacheLookup(
    Vector3f CameraToCache,
    float LookupCell,
    Vector3f LookupMinimum,
    int LookupBuckets,
    int AtlasWidth,
    int AtlasHeight,
    float BounceScale)
{
    /// <summary>The numbers of a surface cache the game handed over.</summary>
    /// <param name="cache">The handover.</param>
    /// <returns>Its lookup.</returns>
    public static ReflectionCacheLookup Of(in SurfaceCacheBinding cache) => new(
        cache.CameraToCache,
        cache.LookupCell,
        cache.LookupMinimum,
        cache.LookupBuckets,
        cache.AtlasWidth,
        cache.AtlasHeight,
        cache.BounceScale);
}

/// <summary>Everything one frame's reflection is told: the camera, the knobs, the water and what it traces into.</summary>
/// <param name="Width">The render width.</param>
/// <param name="Height">The render height.</param>
/// <param name="ViewToWorld">The view-to-world rotation.</param>
/// <param name="Unprojection">How a depth sample becomes a view position.</param>
/// <param name="Settings">The march's knobs: steps, intensity, roughness cutoff, thickness.</param>
/// <param name="Wetness">The whole of the weather, so the march arrives at the water the shading drew.</param>
/// <param name="Shelter">The roof map's transform, or <see cref="ShelterMapTransform.None"/>.</param>
/// <param name="Camera">The camera's world position, which the puddle lattice and roof map are addressed by.</param>
/// <param name="ViewHeightKm">The camera's height, for reading the sky table.</param>
/// <param name="FrameIndex">Which frame this is, so the dither moves.</param>
/// <param name="Reach">How far a marched ray goes, in metres.</param>
/// <param name="Traces">Whether this frame traces rather than marches.</param>
/// <param name="TracedReach">How far a traced ray goes before it counts as having left.</param>
/// <param name="MaxRayIntensity">The ceiling on one traced ray's radiance.</param>
/// <param name="TracedMaxRoughness">The roughest surface a traced ray is fired for.</param>
/// <param name="Cache">Where a hit finds the surface cache.</param>
/// <param name="RigLensX">The first-person lens's x scale, or nought where no rig was drawn.</param>
/// <param name="RigLensY">Its y scale.</param>
/// <param name="Field">The distance field a marched ray walks; nought levels for none.</param>
public readonly record struct ReflectionFrame(
    int Width,
    int Height,
    Matrix3f ViewToWorld,
    DepthUnprojection Unprojection,
    ReflectionSettings Settings,
    WetnessSettings Wetness,
    ShelterMapTransform Shelter,
    Vector3f Camera,
    float ViewHeightKm,
    int FrameIndex,
    float Reach,
    bool Traces,
    float TracedReach,
    float MaxRayIntensity,
    float TracedMaxRoughness,
    ReflectionCacheLookup Cache,
    float RigLensX,
    float RigLensY,
    DistanceFieldBinding Field);

/// <summary>The reflection's parameter block (the Reflect module's Params), the same for every backend.</summary>
/// <remarks>
/// A matrix and twenty-four vectors: the view, the unprojection, the resolution, the march's knobs, the
/// wetness packed exactly as the shading packs it, the roof map's transform, the traced half's block and the
/// distance field's. Declared in full by both forms of the module, so there is one layout rather than two
/// that can drift.
/// </remarks>
public static class ReflectionParameters
{
    /// <summary>The number of floats in the block.</summary>
    public const int Floats = 16 + (24 * 4);

    /// <summary>The number of floats in the compute composite's block: one vector.</summary>
    public const int CompositeFloats = 4;

    // Where the roof map's transform starts: past the mat4 and the seven vec4s above it.
    private const int ShelterOffset = 16 + (7 * 4);

    // Where the traced half's own block starts: past the mat4 and the ten vec4s the screen march uses.
    private const int TraceOffset = 16 + (10 * 4);

    // Where the distance field's block starts, and its two arrays, each spaced four floats apart.
    private const int FieldOffset = 16 + (15 * 4);
    private const int FieldOriginOffset = FieldOffset + 4;
    private const int FieldRangeOffset = FieldOriginOffset + (DistanceFieldBinding.MaxLevels * 4);

    /// <summary>Fills the block.</summary>
    /// <param name="data">The block, <see cref="Floats"/> long.</param>
    /// <param name="frame">The frame.</param>
    public static void Write(Span<float> data, in ReflectionFrame frame)
    {
        if (data.Length != Floats)
        {
            throw new ArgumentException($"The block is exactly {Floats} floats.", nameof(data));
        }

        data.Clear();

        TemporalParameters.WriteRotation(data, 0, frame.ViewToWorld);

        data[16] = frame.Unprojection.XScale;
        data[17] = frame.Unprojection.YScale;
        data[18] = frame.Unprojection.Near;
        data[19] = frame.Unprojection.Far;

        data[20] = frame.Width;
        data[21] = frame.Height;
        data[22] = frame.Width > 0 ? 1f / frame.Width : 0f;
        data[23] = frame.Height > 0 ? 1f / frame.Height : 0f;

        ReflectionSettings settings = frame.Settings;
        data[24] = MathF.Max(settings.Quality, 1);
        data[25] = MathF.Max(settings.Intensity, 0f);
        data[26] = Math.Clamp(settings.MaxRoughness, 0f, 1f);
        data[27] = MathF.Max(settings.Thickness, 0.02f);

        WetnessSettings wetness = frame.Wetness;
        data[28] = MathF.Max(wetness.Amount, 0f);
        data[29] = frame.FrameIndex;
        data[30] = MathF.Max(frame.Reach, 1f);
        data[31] = frame.ViewHeightKm;

        // The wetness, packed exactly as the shading packs it into its own block: both are read back into
        // the same WetParams, which keeps the two descriptions of the water one description.
        data[32] = Math.Clamp(wetness.PuddleCoverage, 0f, 1f);
        data[33] = MathF.Max(wetness.PuddleSizeMetres, 0.05f);
        data[34] = Math.Clamp(wetness.RippleStrength, 0f, 1f);
        data[35] = wetness.Seconds;

        data[36] = MathF.Max(wetness.RippleSizeMetres, 0.02f);
        data[37] = Math.Clamp(wetness.WindRippleStrength, 0f, 1f);
        data[38] = frame.Camera.X;
        data[39] = frame.Camera.Y;

        // The roof map's transform, in world metres; the camera's Z rides with it because this pass has a
        // surface only as a camera-relative vector and the map is addressed in the world.
        ShelterMapTransform shelter = frame.Shelter;
        data[40] = shelter.IsPresent ? 1f : 0f;
        data[41] = frame.Camera.Z;

        data[ShelterOffset] = shelter.OriginX;
        data[ShelterOffset + 1] = shelter.OriginY;
        data[ShelterOffset + 2] = shelter.CellMetres > 0f ? 1f / shelter.CellMetres : 0f;
        data[ShelterOffset + 3] = shelter.LowestZ;

        data[ShelterOffset + 4] = shelter.HeightScale;
        data[ShelterOffset + 5] = shelter.HeadroomMetres;
        data[ShelterOffset + 6] = shelter.ReachMetres;

        data[ShelterOffset + 8] = shelter.Width;
        data[ShelterOffset + 9] = shelter.Height;

        ReflectionCacheLookup cache = frame.Cache;
        data[TraceOffset] = frame.Traces ? 1f : 0f;
        data[TraceOffset + 1] = MathF.Max(frame.TracedReach, 1f);
        data[TraceOffset + 2] = MathF.Max(frame.MaxRayIntensity, 0f);
        data[TraceOffset + 3] = MathF.Max(cache.BounceScale, 0f);

        data[TraceOffset + 4] = cache.CameraToCache.X;
        data[TraceOffset + 5] = cache.CameraToCache.Y;
        data[TraceOffset + 6] = cache.CameraToCache.Z;
        data[TraceOffset + 7] = cache.LookupCell;

        data[TraceOffset + 8] = cache.LookupMinimum.X;
        data[TraceOffset + 9] = cache.LookupMinimum.Y;
        data[TraceOffset + 10] = cache.LookupMinimum.Z;
        data[TraceOffset + 11] = cache.LookupBuckets;

        data[TraceOffset + 12] = cache.AtlasWidth;
        data[TraceOffset + 13] = cache.AtlasHeight;

        data[TraceOffset + 16] = Math.Clamp(frame.TracedMaxRoughness, 0f, 1f);
        data[TraceOffset + 17] = frame.RigLensX;
        data[TraceOffset + 18] = frame.RigLensY;

        // Nought levels is what the marched resolve reads as no field to walk.
        DistanceFieldBinding field = frame.Field;
        data[FieldOffset] = field.Levels;
        data[FieldOffset + 1] = field.Resolution;

        for (int level = 0; level < DistanceFieldBinding.MaxLevels && level < field.Levels; level++)
        {
            DistanceFieldLevel described = field[level];

            int origin = FieldOriginOffset + (level * 4);
            data[origin + 0] = described.Origin.X;
            data[origin + 1] = described.Origin.Y;
            data[origin + 2] = described.Origin.Z;
            data[origin + 3] = described.VoxelSize;

            // A scalar array, which the standard layout still spaces four floats apart.
            data[FieldRangeOffset + (level * 4)] = described.Range;
        }
    }
}

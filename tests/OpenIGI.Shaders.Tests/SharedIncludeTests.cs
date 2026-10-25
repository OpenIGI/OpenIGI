using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The pieces of shader source that more than one module depends on being identical.
/// </summary>
/// <remarks>
/// A shared include that is quietly re-inlined does not fail: the two copies agree on the day they are
/// written and drift afterwards, and a drift in any of these reads as light in the wrong place rather
/// than as a broken shader. Stated as text because that is the only form these invariants have -- the
/// preprocessor has flattened them by the time anything else could look.
/// </remarks>
public sealed class SharedIncludeTests
{
    /// <summary>The orthonormal basis about a normal is written once.</summary>
    /// <remarks>
    /// Every pass that fires rays over a hemisphere lifts its local directions through this, so two
    /// constructions would put two passes' rays in different places while both looked correct alone.
    /// </remarks>
    [Fact]
    public void One_hemisphere_basis_serves_every_gather()
    {
        Assert.Equal(
            ["Common/Hemisphere.slang"],
            SourcesDefining("HemisphereBasis BasisAround(float3 normal)"));

        Assert.Equal(
            ["Common/Hemisphere.slang"],
            SourcesDefining("float3 Orient(HemisphereBasis basis, float3 local)"));
    }

    /// <summary>The visibility a caller asks for without wanting the hit point is written once.</summary>
    /// <remarks>
    /// Over whichever <c>VisibilityAt</c> took the name -- the traced one where the host has an
    /// acceleration structure, the marched one where it does not -- so the sun's own shadow test and the
    /// gather's cannot be answered by two different traces.
    /// </remarks>
    [Fact]
    public void One_visibility_serves_the_traced_and_the_marched_forms()
    {
        Assert.Equal(
            ["Common/SoftwareTrace.slang"],
            SourcesDefining("float Visibility(float3 world, float3 normal, float3 direction)"));
    }

    /// <summary>The surface's response to a light is written once.</summary>
    /// <remarks>
    /// The sun, every lamp and the flashlight are all shaded through it, and the environment's specular
    /// stand-in is the estimate the reflection pass hands over to; a second copy of either would light one
    /// source with a different lobe from the rest while each looked right alone.
    /// </remarks>
    [Fact]
    public void One_surface_response_serves_every_light()
    {
        Assert.Equal(
            ["Common/ShadingBrdf.slang"],
            SourcesDefining("void EvaluateSurface("));

        Assert.Equal(
            ["Common/ShadingBrdf.slang"],
            SourcesDefining("float2 SplitSumApprox(float roughness, float cosNV)"));
    }

    /// <summary>What the lighting pass lifted out of itself is defined in its include and nowhere else.</summary>
    /// <remarks>
    /// The sun's cascades, a lamp's unshadowed light and the air in front of a surface each moved into an
    /// include of their own; one quietly written back into the pass would shadow, light or fog the frame by
    /// a copy that no longer follows the include.
    /// </remarks>
    [Fact]
    public void The_lighting_pass_reads_its_sun_shadow_lamps_and_air_from_one_place()
    {
        Assert.Equal(
            ["Common/SunShadowCascades.slang"],
            SourcesDefining("float SunVisibility(float3 viewPos, float3 worldNormal, float2 pixel)"));

        Assert.Equal(
            ["Common/LampShade.slang"],
            SourcesDefining("float3 LampShade("));

        Assert.Equal(
            ["Common/AerialPerspective.slang"],
            SourcesDefining("float3 AerialPerspective("));
    }

    /// <summary>The software lamp trace's map lookup and its march are each defined once.</summary>
    [Fact]
    public void One_lamp_map_lookup_and_one_lamp_march()
    {
        Assert.Equal(
            ["Common/LampMapVisibility.slang"],
            SourcesDefining("float LampMapVisibility(int lamp, float3 cameraRelative,"));

        Assert.Equal(
            ["Common/LampScreenTrace.slang"],
            SourcesDefining("bool SoftwareBlocked("));
    }

    /// <summary>The surface cache's probes are read back the way they were gathered, from one definition.</summary>
    [Fact]
    public void One_card_probe_grid()
    {
        Assert.Equal(
            ["Common/CardProbes.slang"],
            SourcesDefining("float3 FilteredProbe(int2 probeCoord, int2 pageMin, int2 pageMax,"));

        Assert.Equal(
            ["Common/CardDiagnostics.slang"],
            SourcesDefining("float4 Probed(float3 world, float3 normal, float2 cardUv, int2 texel)"));
    }

    private static IReadOnlyList<string> SourcesDefining(string signature)
    {
        string root = Path.Combine(ShaderRepository.ProjectDirectory, "Source");

        return
        [
            .. Directory
                .EnumerateFiles(root, "*.slang", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains(signature, StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
    }
}

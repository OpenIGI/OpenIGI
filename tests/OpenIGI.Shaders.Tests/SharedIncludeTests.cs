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

using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// The level's distance field as it crosses the renderer seam, and the march a reflection walks it with.
/// </summary>
/// <remarks>
/// Both exist because a graphics backend cannot see the game. Every pass that marched this field used to
/// live on the game's side, beside the code that built it, so the backend's own reflection passes had no
/// way to reach it and glass on a machine without ray tracing reflected the sky and nothing else.
/// </remarks>
public sealed class DistanceFieldBindingTests
{
    /// <summary>A handover with no levels is not one a pass may march.</summary>
    /// <remarks>
    /// All or nothing, as the surface cache's own binding is: a march with a volume and no levels reads
    /// an unbound sampler, which is not a coarser answer but an arbitrary one.
    /// </remarks>
    [Fact]
    public void AFieldWithoutLevelsIsNotPresent()
    {
        Assert.False(DistanceFieldBinding.None.IsPresent);

        DistanceFieldBinding levelless = new(
            new TextureHandle(7), 0, 64, default, default, default, default);
        Assert.False(levelless.IsPresent);

        DistanceFieldBinding unbound = new(
            TextureHandle.None, 4, 64, default, default, default, default);
        Assert.False(unbound.IsPresent);
    }

    /// <summary>A complete handover is, and its levels come back in the order they were given.</summary>
    [Fact]
    public void ACompleteFieldIsPresentAndIndexesItsLevels()
    {
        DistanceFieldLevel[] levels =
        [
            new(new Vector3f(-8f, -8f, -8f), 0.25f, 1f),
            new(new Vector3f(-16f, -16f, -16f), 0.5f, 2f),
            new(new Vector3f(-32f, -32f, -32f), 1f, 4f),
            new(new Vector3f(-64f, -64f, -64f), 2f, 8f),
        ];

        DistanceFieldBinding field = new(
            new TextureHandle(3), levels.Length, 64, levels[0], levels[1], levels[2], levels[3]);

        Assert.True(field.IsPresent);
        for (int level = 0; level < levels.Length; level++)
        {
            Assert.Equal(levels[level], field[level]);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => field[DistanceFieldBinding.MaxLevels]);
    }

    /// <summary>The reflection march is the shadow march's distance lookup and a different hit test.</summary>
    /// <remarks>
    /// <para>
    /// The two must not be confused, and the compiler cannot tell them apart because both are strings. A
    /// shadow ray only has to know that something is in the way, so it stops the moment the field reads
    /// less than half a voxel and never asks where the surface was; on the outer clipmaps that is a metre
    /// of slack. Asked to resolve reflections, it reported a hit for every ray that merely passed near a
    /// façade and placed the ones it got right too far off the surface for anything to say what they
    /// were — a level's worth of glass came back as "struck something nothing can shade".
    /// </para>
    /// <para>
    /// What they share is the lookup, and that is the point of keeping one copy: two marches disagreeing
    /// about where a surface is would not look like a bug, it would look like the window and the wall
    /// beside it disagreeing about where the room is.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReflectionMarchSharesTheLookupAndNotTheHitTest()
    {
        Assert.Contains(DistanceFieldShaders.Distance, DistanceFieldShaders.Trace, StringComparison.Ordinal);
        Assert.Contains(DistanceFieldShaders.Surface, DistanceFieldShaders.Trace, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DistanceFieldShaders.Visibility, DistanceFieldShaders.Trace, StringComparison.Ordinal);

        Assert.Contains(DistanceFieldShaders.Distance, DistanceFieldShaders.March, StringComparison.Ordinal);
        Assert.Contains(DistanceFieldShaders.Visibility, DistanceFieldShaders.March, StringComparison.Ordinal);
    }

    /// <summary>Everything the march calls is declared before the call, as GLSL requires.</summary>
    /// <remarks>
    /// These are spliced strings, so the only thing enforcing declaration order is the order they are
    /// concatenated in — and a function used before it is declared is a compile failure at the first
    /// dispatch of a level rather than at build time.
    /// </remarks>
    [Fact]
    public void TheMarchDeclaresWhatItCallsBeforeItCallsIt()
    {
        string trace = DistanceFieldShaders.Trace;

        int distance = trace.IndexOf("float DistanceAt(", StringComparison.Ordinal);
        int settle = trace.IndexOf("vec3 Settle(", StringComparison.Ordinal);
        int surface = trace.IndexOf("bool SurfaceAt(", StringComparison.Ordinal);

        Assert.True(distance >= 0 && settle > distance && surface > settle);
        Assert.Contains("Settle(at, direction", trace, StringComparison.Ordinal);
    }

    /// <summary>The surface march hands back the voxel that found the hit, not just the hit.</summary>
    /// <remarks>
    /// Every tolerance downstream has to widen by it. A point found in the coarsest clipmap is good to a
    /// couple of metres, and a surface cache asked to match it within a quarter of one refuses every
    /// time — which is how the software glazing came back as a level-sized hole.
    /// </remarks>
    [Fact]
    public void TheSurfaceMarchReportsTheVoxelThatAnsweredWithTheHit()
    {
        Assert.Contains(
            "bool SurfaceAt(vec3 world, vec3 normal, vec3 direction, float reach, out vec3 hit, out float voxel)",
            DistanceFieldShaders.Surface,
            StringComparison.Ordinal);
    }
}

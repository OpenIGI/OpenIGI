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
}

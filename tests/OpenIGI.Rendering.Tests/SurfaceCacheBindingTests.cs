using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// The handover that lets a traced reflection resolve its hit, and the all-or-nothing rule that governs
/// whether a pass may use it at all.
/// </summary>
/// <remarks>
/// The rule is the point of these. A lookup missing one of its pieces does not answer more coarsely — it
/// reads whatever the unbound descriptor happens to hold, which is a reflection of nothing in particular
/// rather than a visibly absent one. So a pass has to be able to ask one question and stand down, and
/// these pin that question to every field it depends on.
/// </remarks>
public sealed class SurfaceCacheBindingTests
{
    /// <summary>Nothing handed over is never traced against.</summary>
    [Fact]
    public void NothingHandedOverIsNotPresent()
    {
        Assert.False(SurfaceCacheBinding.None.IsPresent);
        Assert.False(default(SurfaceCacheBinding).IsPresent);
    }

    /// <summary>A complete handover is.</summary>
    [Fact]
    public void ACompleteHandoverIsPresent()
    {
        Assert.True(Complete().IsPresent);
    }

    /// <summary>Every resource is load-bearing: drop any one and the whole handover stands down.</summary>
    /// <remarks>
    /// Written out one field at a time rather than as a loop over reflection, because the failure this
    /// guards against is a field being ADDED later and left out of the test — which a loop would hide by
    /// passing, and this catches as soon as someone writes the constructor call.
    /// </remarks>
    [Theory]
    [InlineData("pages")]
    [InlineData("cells")]
    [InlineData("cellPages")]
    [InlineData("albedo")]
    [InlineData("depth")]
    [InlineData("lighting")]
    [InlineData("atlasWidth")]
    [InlineData("atlasHeight")]
    [InlineData("buckets")]
    [InlineData("cell")]
    public void AResourceMissingStandsTheWholeHandoverDown(string missing)
    {
        SurfaceCacheBinding binding = Complete();

        binding = missing switch
        {
            "pages" => binding with { Pages = StorageBufferHandle.None },
            "cells" => binding with { Cells = StorageBufferHandle.None },
            "cellPages" => binding with { CellPages = StorageBufferHandle.None },
            "albedo" => binding with { Albedo = TextureHandle.None },
            "depth" => binding with { Depth = TextureHandle.None },
            "lighting" => binding with { Lighting = TextureHandle.None },
            "atlasWidth" => binding with { AtlasWidth = 0 },
            "atlasHeight" => binding with { AtlasHeight = 0 },
            "buckets" => binding with { LookupBuckets = 0 },
            _ => binding with { LookupCell = 0f },
        };

        Assert.False(binding.IsPresent);
    }

    /// <summary>
    /// The camera offset moves every frame and the rest does not, which is why the whole struct is handed
    /// over every frame rather than once when a mission loads.
    /// </summary>
    [Fact]
    public void TheCameraOffsetIsTheOnlyPartThatMoves()
    {
        SurfaceCacheBinding first = Complete();
        SurfaceCacheBinding second = first with { CameraToCache = new Vector3f(9f, 8f, 7f) };

        Assert.NotEqual(first, second);
        Assert.True(second.IsPresent);
        Assert.Equal(first with { CameraToCache = second.CameraToCache }, second);
    }

    private static SurfaceCacheBinding Complete() => new(
        new StorageBufferHandle(1),
        new StorageBufferHandle(2),
        new StorageBufferHandle(3),
        new TextureHandle(4),
        new TextureHandle(5),
        new TextureHandle(6),
        AtlasWidth: 2048,
        AtlasHeight: 2048,
        LookupBuckets: 1 << 21,
        LookupMinimum: new Vector3f(-100f, -100f, -20f),
        LookupCell: 8f,
        CameraToCache: new Vector3f(1f, 2f, 3f),
        BounceScale: 1f);
}

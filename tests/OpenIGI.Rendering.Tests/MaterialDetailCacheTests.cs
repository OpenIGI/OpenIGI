using OpenIGI.Core.Graphics;
using OpenIGI.Core.Threading;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Covers the pack deriving its own maps when it has none: that a miss produces one, that the result
/// lands on disk, and that the next run reads the file instead of deriving it again.
/// </summary>
public sealed class MaterialDetailCacheTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(), "openigi-detail-" + Guid.NewGuid().ToString("N"));

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    /// <summary>A small non-uniform texture, so the generator has gradients to work from.</summary>
    private static Bgra32[] Checker(int size)
    {
        Bgra32[] pixels = new Bgra32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte value = (byte)(((x / 2) + (y / 2)) % 2 == 0 ? 20 : 220);
                pixels[(y * size) + x] = new Bgra32(value, value, value, 255);
            }
        }

        return pixels;
    }

    /// <summary>
    /// A replaced texture's map is kept apart from the shipped texture's, and the shipped name is unchanged,
    /// so a pack's picture never borrows the original's map (or the other way round) across runs.
    /// </summary>
    [Fact]
    public void AReplacedTextureIsCachedApartFromTheShippedOne()
    {
        Assert.Equal("000_04_1", MaterialDetailPack.KeyFor("000_04_1", null));
        Assert.Equal("000_04_1", MaterialDetailPack.KeyFor("000_04_1", string.Empty));
        Assert.Equal("000_04_1.0123456789abcdef", MaterialDetailPack.KeyFor("000_04_1", "0123456789abcdef"));
        Assert.NotEqual(
            MaterialDetailPack.KeyFor("000_04_1", "0123456789abcdef"),
            MaterialDetailPack.KeyFor("000_04_1", "fedcba9876543210"));
    }

    [Fact]
    public void AMissDerivesAMapWritesItToTheCacheAndAttachesIt()
    {
        RecordingGraphicsDevice device = new();
        using MaterialDetailPack pack = new(device, ThreadPoolWorkScheduler.Instance, logger: null, cacheDirectory: _cache);
        pack.DeriveMissing = true;

        TextureHandle diffuse = device.CreateTexture(
            new TextureDescription(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat), Checker(8));

        pack.Attach("wall_01", diffuse, Checker(8), 8, 8);

        // Nothing is attached yet: the derivation is on a worker and the texture stays flat until pumped.
        Assert.Equal(0, pack.Count);

        int uploaded = pack.WaitForDerivations(Patience);

        Assert.Equal(1, uploaded);
        Assert.Equal(1, pack.Count);
        Assert.True(File.Exists(Path.Combine(_cache, "wall_01.png")));
    }

    [Fact]
    public void TheSecondRunReadsTheCachedFileInsteadOfDerivingAgain()
    {
        RecordingGraphicsDevice device = new();
        TextureHandle diffuse = device.CreateTexture(
            new TextureDescription(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat), Checker(8));

        using (MaterialDetailPack first = new(device, ThreadPoolWorkScheduler.Instance, null, _cache))
        {
            first.DeriveMissing = true;
            first.Attach("crate", diffuse, Checker(8), 8, 8);
            Assert.Equal(1, first.WaitForDerivations(Patience));
        }

        // A second pack over the same cache, with derivation switched OFF. It can only succeed by
        // reading the file the first one wrote -- which is the whole point of the cache.
        using MaterialDetailPack second = new(device, ThreadPoolWorkScheduler.Instance, null, _cache);
        second.DeriveMissing = false;

        second.Attach("crate", diffuse, Checker(8), 8, 8);

        Assert.Equal(1, second.Count);
        Assert.Equal(0, second.WaitForDerivations(Patience));
    }

    [Fact]
    public void WithoutDeriveMissingAMissStaysAMissAndNothingIsWritten()
    {
        RecordingGraphicsDevice device = new();
        using MaterialDetailPack pack = new(device, ThreadPoolWorkScheduler.Instance, null, _cache);
        pack.DeriveMissing = false;

        TextureHandle diffuse = device.CreateTexture(
            new TextureDescription(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat), Checker(8));

        pack.Attach("floor", diffuse, Checker(8), 8, 8);

        Assert.Equal(0, pack.Count);
        Assert.Equal(0, pack.WaitForDerivations(Patience));
        Assert.False(Directory.Exists(_cache) && File.Exists(Path.Combine(_cache, "floor.png")));
    }

    [Fact]
    public void DisabledDetailDoesNotSynchronouslyDeriveAnUnnamedGroundTile()
    {
        RecordingGraphicsDevice device = new();
        using MaterialDetailPack pack = new(device, ThreadPoolWorkScheduler.Instance, logger: null);
        pack.DeriveMissing = false;

        TextureHandle diffuse = device.CreateTexture(
            new TextureDescription(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat), Checker(8));

        pack.AttachDerived(diffuse, Checker(8), 8, 8);

        Assert.Equal(0, pack.Count);
        Assert.Empty(device.TextureDetails);
    }

    [Fact]
    public void EnabledDetailStillDerivesAndAttachesAnUnnamedGroundTile()
    {
        RecordingGraphicsDevice device = new();
        using MaterialDetailPack pack = new(device, ThreadPoolWorkScheduler.Instance, logger: null);
        pack.DeriveMissing = true;

        TextureHandle diffuse = device.CreateTexture(
            new TextureDescription(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat), Checker(8));

        pack.AttachDerived(diffuse, Checker(8), 8, 8);

        Assert.Equal(1, pack.Count);
        Assert.Single(device.TextureDetails);
    }

    [Fact]
    public void TwoTexturesAskingForOneNameShareASingleDerivation()
    {
        RecordingGraphicsDevice device = new();
        using MaterialDetailPack pack = new(device, ThreadPoolWorkScheduler.Instance, null, _cache);
        pack.DeriveMissing = true;

        TextureDescription description = new(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat);
        TextureHandle world = device.CreateTexture(description, Checker(8));
        TextureHandle soldier = device.CreateTexture(description, Checker(8));

        pack.Attach("shared", world, Checker(8), 8, 8);
        pack.Attach("shared", soldier, Checker(8), 8, 8);

        // One map, not two: the second caller queued behind the first rather than deriving its own.
        Assert.Equal(1, pack.WaitForDerivations(Patience));
        Assert.Equal(1, pack.Count);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("sub/dir")]
    public void ANameThatCannotBeAFileNameIsNotCached(string name)
    {
        RecordingGraphicsDevice device = new();
        using MaterialDetailPack pack = new(device, ThreadPoolWorkScheduler.Instance, null, _cache);
        pack.DeriveMissing = true;

        TextureHandle diffuse = device.CreateTexture(
            new TextureDescription(8, 8, 1, TextureFilter.Linear, TextureAddressMode.Repeat), Checker(8));

        pack.Attach(name, diffuse, Checker(8), 8, 8);

        Assert.Equal(0, pack.WaitForDerivations(Patience));
        Assert.Equal(0, pack.Count);
    }
}

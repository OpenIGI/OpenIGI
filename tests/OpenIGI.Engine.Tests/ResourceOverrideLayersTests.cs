using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>
/// Pins how a stack of content layers resolves: the highest enabled layer that has a path wins it, the ones
/// below fill the gaps, and switching one off falls through to the next.
/// </summary>
public sealed class ResourceOverrideLayersTests : IDisposable
{
    private const string Texture = "common/textures/a.tex";

    private readonly string _root = Directory.CreateTempSubdirectory("openigi-layers").FullName;

    /// <summary>The higher layer wins a path both carry; the lower still supplies what only it has.</summary>
    [Fact]
    public void TheTopLayerWinsAndTheOnesBelowFillTheGaps()
    {
        Write("top", Texture, [1]);
        Write("bottom", Texture, [2]);
        Write("bottom", "common/textures/b.tex", [3]);

        ResourceOverrides overrides = Stack(("top", true), ("bottom", true));
        _ = overrides.Rescan();

        Assert.Equal(new byte[] { 1 }, Read(overrides, Texture));
        Assert.Equal(new byte[] { 3 }, Read(overrides, "common/textures/b.tex"));
        Assert.Equal(2, overrides.Count);
    }

    /// <summary>Switching the top layer off falls through to the next, and back again, without a rescan.</summary>
    [Fact]
    public void SwitchingALayerOffFallsThroughToTheNext()
    {
        Write("top", Texture, [1]);
        Write("bottom", Texture, [2]);

        ResourceOverrides overrides = Stack(("top", true), ("bottom", true));
        _ = overrides.Rescan();

        Assert.True(overrides.SetEnabled("top", false, out ResourceOverrides.ScanResult off));
        Assert.Equal(1, off.Changed);
        Assert.Equal(new byte[] { 2 }, Read(overrides, Texture));

        Assert.True(overrides.SetEnabled("1", true, out _));
        Assert.Equal(new byte[] { 1 }, Read(overrides, Texture));
    }

    /// <summary>With every layer that has a path switched off, the path is the game's own again.</summary>
    [Fact]
    public void AllLayersOffLeavesTheGamesOwnFile()
    {
        Write("only", Texture, [1]);

        ResourceOverrides overrides = Stack(("only", false));
        _ = overrides.Rescan();

        Assert.False(overrides.TryRead(Texture, out _));
        Assert.Equal(0, overrides.Count);

        // Still known to be overridable, so a reapply can find the entries to put back.
        Assert.True(overrides.Has(Texture));
        Assert.True(overrides.HasContent);
    }

    /// <summary>Inside one layer a loose file beats an archived entry, but a higher layer's archive beats both.</summary>
    [Fact]
    public void LooseBeatsArchiveWithinALayerButNotAcrossLayers()
    {
        WriteArchive("top", "common/textures/textures.res", ("LOCAL:common/textures/a.tex", [9]));
        Write("bottom", Texture, [2]);
        WriteArchive("bottom", "common/textures/textures.res", ("LOCAL:common/textures/a.tex", [8]));

        ResourceOverrides overrides = Stack(("top", true), ("bottom", true));
        _ = overrides.Rescan();
        Assert.Equal(new byte[] { 9 }, Read(overrides, Texture));

        overrides.SetEnabled("top", false, out _);
        Assert.Equal(new byte[] { 2 }, Read(overrides, Texture));
    }

    /// <summary>Two archives in one layer that carry the same path resolve by archive path, whatever order they were written in.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoArchivesWithOnePathResolveAlphabetically(bool alphabeticalFirst)
    {
        if (alphabeticalFirst)
        {
            WriteArchive("pack", "common/textures/a_first.res", ("LOCAL:common/textures/a.tex", [1]));
            WriteArchive("pack", "common/textures/b_second.res", ("LOCAL:common/textures/a.tex", [2]));
        }
        else
        {
            WriteArchive("pack", "common/textures/b_second.res", ("LOCAL:common/textures/a.tex", [2]));
            WriteArchive("pack", "common/textures/a_first.res", ("LOCAL:common/textures/a.tex", [1]));
        }

        ResourceOverrides overrides = Stack(("pack", true));
        _ = overrides.Rescan();

        Assert.Equal(new byte[] { 1 }, Read(overrides, Texture));
    }

    /// <summary>The same path now supplied by another layer counts as changed, even with identical size and time.</summary>
    [Fact]
    public void APathMovingToAnotherLayerCountsAsChanged()
    {
        Write("top", Texture, [1]);
        Write("bottom", Texture, [2]);
        DateTime same = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "top", "common", "textures", "a.tex"), same);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "bottom", "common", "textures", "a.tex"), same);

        ResourceOverrides overrides = Stack(("top", true), ("bottom", true));
        _ = overrides.Rescan();

        File.Delete(Path.Combine(_root, "top", "common", "textures", "a.tex"));
        ResourceOverrides.ScanResult result = overrides.Rescan();

        Assert.Equal(1, result.Changed);
        Assert.Equal(0, result.Removed);
    }

    /// <summary>The layer list counts what each layer carries and wins, and conflicts name the winner and the covered.</summary>
    [Fact]
    public void LayersAndConflictsAreReported()
    {
        Write("top", Texture, [1]);
        Write("middle", Texture, [2]);
        Write("middle", "common/textures/b.tex", [2]);
        Write("bottom", Texture, [3]);

        ResourceOverrides overrides = Stack(("top", true), ("middle", true), ("bottom", true));
        _ = overrides.Rescan();

        IReadOnlyList<ResourceOverrides.LayerInfo> layers = overrides.Layers;
        Assert.Equal((1, 1), (layers[0].Files, layers[0].Wins));
        Assert.Equal((2, 1), (layers[1].Files, layers[1].Wins));
        Assert.Equal((1, 0), (layers[2].Files, layers[2].Wins));

        ResourceOverrides.Conflict conflict = Assert.Single(overrides.Conflicts());
        Assert.Equal(Texture, conflict.Path);
        Assert.Equal(0, conflict.Winner);
        Assert.Equal([1, 2], conflict.Covered);
        Assert.Equal(0, overrides.LayerOf(Texture));

        // A layer that is off covers nothing and is covered by nothing.
        overrides.SetEnabled("middle", false, out _);
        Assert.Equal([2], Assert.Single(overrides.Conflicts()).Covered);
    }

    /// <summary>The read stamp names the supplying file, so it differs between layers and is stable between reads.</summary>
    [Fact]
    public void TheStampFollowsTheSupplyingFile()
    {
        Write("top", Texture, [1]);
        Write("bottom", Texture, [1]);

        ResourceOverrides overrides = Stack(("top", true), ("bottom", true));
        _ = overrides.Rescan();

        Assert.True(overrides.TryRead(Texture, out _, out string top));
        Assert.True(overrides.TryRead(Texture, out _, out string again));
        overrides.SetEnabled("top", false, out _);
        Assert.True(overrides.TryRead(Texture, out _, out string bottom));

        Assert.Equal(16, top.Length);
        Assert.Equal(top, again);
        Assert.NotEqual(top, bottom);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    private static byte[] Read(ResourceOverrides overrides, string path)
    {
        Assert.True(overrides.TryRead(path, out ReadOnlyMemory<byte> data), $"'{path}' is not overridden.");
        return data.ToArray();
    }

    private ResourceOverrides Stack(params (string Name, bool Enabled)[] layers) =>
        new([.. layers.Select(layer => new OverrideLayer(layer.Name, layer.Name, Path.Combine(_root, layer.Name), layer.Enabled))]);

    private void Write(string layer, string relative, byte[] bytes)
    {
        string path = Path.Combine(_root, layer, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private void WriteArchive(string layer, string relative, params (string Name, byte[] Data)[] entries) =>
        Write(
            layer,
            relative,
            IresWriter.Write([.. entries.Select(entry => new IresWriter.Entry(entry.Name, IresEntryKind.Body, entry.Data))]));
}

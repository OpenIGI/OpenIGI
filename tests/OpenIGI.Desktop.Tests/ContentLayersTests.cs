using System.Text.Json;
using OpenIGI.Core.Content;
using OpenIGI.Engine.Resources;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Content;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Pins how the game turns the launcher's content profile, the <c>-content</c> folders and the old
/// <c>OPENIGI_OVERRIDES</c> into one ordered stack of layers for the title it is running.
/// </summary>
public sealed class ContentLayersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-content-profile").FullName;

    /// <summary>The mods' data stacks over the player's packs and the game folder's overrides, under the -content folders.</summary>
    [Fact]
    public void ModsDataSitsOverThePacksAndUnderTheDevelopmentFolders()
    {
        string profile = WriteProfile(new Dictionary<string, IReadOnlyList<ContentLayerSpec>>
        {
            ["igi1"] = [new("hd", "HD Textures", ContentLayerKind.Pack, Path.Combine(_root, "hd"), true), ContentLayerSpec.Local()],
        });

        IReadOnlyList<OverrideLayer> layers = ContentLayers.Resolve(
            "igi1",
            Path.Combine(_root, "game1"),
            profile,
            [Path.Combine(_root, "work")],
            null,
            modFolders: [new("mod-later", "Later", Path.Combine(_root, "later", "data")), new("mod-earlier", "Earlier", Path.Combine(_root, "earlier", "data"))]);

        Assert.Equal(["dev-work", "mod-later", "mod-earlier", "hd", ContentLayerSpec.LocalId], layers.Select(static layer => layer.Id));
        Assert.Equal(Path.Combine(_root, "later", "data"), layers[1].Root);
    }

    /// <summary>Each title takes its own list from the profile, so a title switch picks up the other game's.</summary>
    [Fact]
    public void EachTitleTakesItsOwnListFromTheProfile()
    {
        string profile = WriteProfile(new Dictionary<string, IReadOnlyList<ContentLayerSpec>>
        {
            ["igi1"] =
            [
                new("hd", "HD Textures", ContentLayerKind.Pack, Path.Combine(_root, "hd"), true),
                ContentLayerSpec.Local(enabled: false),
            ],
            ["igi2"] = [ContentLayerSpec.Local()],
        });

        IReadOnlyList<OverrideLayer> first = ContentLayers.Resolve("igi1", Path.Combine(_root, "game1"), profile, [], null);
        IReadOnlyList<OverrideLayer> second = ContentLayers.Resolve("igi2", Path.Combine(_root, "game2"), profile, [], null);

        Assert.Equal(["hd", ContentLayerSpec.LocalId], first.Select(static layer => layer.Id));
        Assert.Equal(Path.Combine(_root, "hd"), first[0].Root);
        Assert.False(first[1].Enabled);
        Assert.Equal(Path.Combine(_root, "game1", ResourceOverrides.DirectoryName), first[1].Root);

        OverrideLayer local = Assert.Single(second);
        Assert.Equal(Path.Combine(_root, "game2", ResourceOverrides.DirectoryName), local.Root);
    }

    /// <summary>Development folders go on top in the order named; with a profile, the legacy variable is ignored.</summary>
    /// <remarks>
    /// The launcher sets OPENIGI_OVERRIDES as well as the profile, for builds from before profiles. A build
    /// that reads the profile stacking that folder too would put one pack above the player's whole order.
    /// </remarks>
    [Fact]
    public void DevelopmentFoldersThenTheProfileWhichOutranksTheLegacyFolder()
    {
        string profile = WriteProfile(new Dictionary<string, IReadOnlyList<ContentLayerSpec>>
        {
            ["igi1"] = [ContentLayerSpec.Local()],
        });

        IReadOnlyList<OverrideLayer> layers = ContentLayers.Resolve(
            "igi1",
            _root,
            profile,
            [Path.Combine(_root, "work"), Path.Combine(_root, "other")],
            Path.Combine(_root, "legacy"));

        Assert.Equal(["dev-work", "dev-other", ContentLayerSpec.LocalId], layers.Select(static layer => layer.Id));
    }

    /// <summary>Without a profile the legacy variable is still a layer, above the local folder.</summary>
    [Fact]
    public void WithoutAProfileTheLegacyFolderIsStillALayer()
    {
        IReadOnlyList<OverrideLayer> layers = ContentLayers.Resolve("igi1", _root, null, [], Path.Combine(_root, "legacy"));

        Assert.Equal(["legacy", ContentLayerSpec.LocalId], layers.Select(static layer => layer.Id));
    }

    /// <summary>With no profile, or none for this title, the game folder's own overrides are the only layer.</summary>
    [Fact]
    public void WithoutAProfileTheLocalFolderIsTheOnlyLayer()
    {
        string profile = WriteProfile(new Dictionary<string, IReadOnlyList<ContentLayerSpec>>
        {
            ["igi2"] = [ContentLayerSpec.Local()],
        });

        OverrideLayer none = Assert.Single(ContentLayers.Resolve("igi1", _root, null, [], null));
        OverrideLayer other = Assert.Single(ContentLayers.Resolve("igi1", _root, profile, [], null));

        Assert.Equal(ContentLayerSpec.LocalId, none.Id);
        Assert.True(none.Enabled);
        Assert.Equal(ContentLayerSpec.LocalId, other.Id);
    }

    /// <summary>A damaged profile is reported and the game carries on with its own overrides folder.</summary>
    [Fact]
    public void AnUnreadableProfileFallsBackToTheLocalFolder()
    {
        string profile = Path.Combine(_root, "broken.json");
        File.WriteAllText(profile, "{ this is not json");

        OverrideLayer layer = Assert.Single(ContentLayers.Resolve("igi1", _root, profile, [], null));
        Assert.Equal(ContentLayerSpec.LocalId, layer.Id);

        OverrideLayer missing = Assert.Single(ContentLayers.Resolve("igi1", _root, Path.Combine(_root, "nope.json"), [], null));
        Assert.Equal(ContentLayerSpec.LocalId, missing.Id);
    }

    /// <summary>A profile survives a write and a read, with kinds spelled as lowercase words.</summary>
    [Fact]
    public void AProfileRoundTripsWithReadableKinds()
    {
        ContentProfile written = new(ContentProfile.CurrentSchema, new Dictionary<string, IReadOnlyList<ContentLayerSpec>>
        {
            ["igi1"] =
            [
                ContentLayerSpec.Local(),
                new("hd-textures", "HD Textures", ContentLayerKind.Pack, "C:/library/content/hd-textures/files", false),
            ],
        });

        string path = Path.Combine(_root, "profile.json");
        ContentProfile.Write(path, written);
        string json = File.ReadAllText(path);
        ContentProfile read = ContentProfile.Read(path);

        Assert.Contains("\"kind\": \"local\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"pack\"", json, StringComparison.Ordinal);
        Assert.Equal(written.For("igi1"), read.For("igi1"));
        Assert.Empty(read.For("igi2"));
    }

    /// <summary>A profile from a newer schema is refused rather than half understood.</summary>
    [Fact]
    public void ANewerSchemaIsRefused() =>
        Assert.Throws<JsonException>(() => ContentProfile.Parse("""{ "schema": 99, "titles": {} }"""));

    /// <summary><c>-content</c> repeats and keeps its order; <c>-contentprofile</c> names the file.</summary>
    [Fact]
    public void TheContentOptionsAreParsed()
    {
        HostOptions options = HostOptions.Parse(["-content", "a", "-content", "b", "-contentprofile", "p.json"]);

        Assert.Equal(["a", "b"], options.ContentDirectories);
        Assert.Equal("p.json", options.ContentProfilePath);
        Assert.Empty(HostOptions.Parse([]).ContentDirectories);
        Assert.Null(HostOptions.Parse([]).ContentProfilePath);
    }

    /// <summary>A content option without its value is an error, not a silent no-op.</summary>
    [Theory]
    [InlineData("-content")]
    [InlineData("-contentprofile")]
    public void AContentOptionWithoutAValueIsRefused(string option) =>
        Assert.Throws<ArgumentException>(() => HostOptions.Parse([option]));

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

    private string WriteProfile(Dictionary<string, IReadOnlyList<ContentLayerSpec>> titles)
    {
        string path = Path.Combine(_root, "profile.json");
        ContentProfile.Write(path, new ContentProfile(ContentProfile.CurrentSchema, titles));
        return path;
    }
}

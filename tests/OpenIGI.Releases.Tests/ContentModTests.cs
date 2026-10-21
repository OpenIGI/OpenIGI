using OpenIGI.Tools.Release;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>Mods as the content store keeps them: a pack described by its <c>mod.json</c>, listed apart.</summary>
public sealed class ContentModTests : IDisposable
{
    private const string CodeManifest = """
        {
          "id": "armoured-guards", "name": "Armoured guards", "version": "1.2.0", "apiVersion": 1,
          "summary": "Guards in armour.", "authors": ["Someone"], "license": "MIT", "titles": ["IGI1"],
          "requiresTitleData": ["igi2"], "dependencies": { "library": ">=2.0" }, "assembly": "Guards.dll",
          "minGameVersion": "0.9.0"
        }
        """;

    private readonly string _folder = Directory.CreateTempSubdirectory("openigi-mod-").FullName;
    private readonly string _cache = Directory.CreateTempSubdirectory("openigi-mod-cache-").FullName;

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        Directory.Delete(_cache, recursive: true);
    }

    [Fact]
    public void A_mod_is_described_by_its_mod_json_and_names_the_source_it_was_built_from()
    {
        Write("mod.json", CodeManifest);
        Write("Guards.dll", "code");

        ContentPublish.Details details = ContentPublish.ForMod(_folder, "https://github.test/someone/guards", "0123abcd");
        ContentManifest manifest = ContentPublish.Chunk(_folder, details, _cache, DateTimeOffset.UnixEpoch).Manifest;

        Assert.Equal(("armoured-guards", "1.2.0", "Armoured guards", "Guards in armour."), (manifest.Id, manifest.Version, manifest.Title, manifest.Summary));
        Assert.Equal(ContentKind.Mod, manifest.Kind);
        Assert.Equal("igi1", manifest.Game);
        Assert.Equal("0.9.0", manifest.MinGameVersion);
        ContentModInfo mod = manifest.Mod!;
        Assert.True(mod.RunsCode);
        Assert.Equal(("https://github.test/someone/guards", "0123abcd"), (mod.Source, mod.Commit));
        Assert.Equal(["Someone"], mod.Authors);
        Assert.Equal(["igi2"], mod.RequiresTitleData!);
        Assert.Equal(">=2.0", mod.Dependencies!["library"]);

        ContentPack listing = ReleaseJson.ReadContent(ReleaseJson.Write(manifest)).Listing;
        Assert.Equal(ContentKind.Mod, listing.Kind);
        Assert.True(listing.IsFor("igi1"));
        Assert.False(listing.IsFor("igi2"));
        Assert.Equal("0123abcd", listing.Mod!.Commit);
    }

    [Fact]
    public void A_mod_with_code_is_not_published_without_its_source()
    {
        Write("mod.json", CodeManifest);

        Assert.Throws<ArgumentException>(() => ContentPublish.ForMod(_folder, null, null));
        Assert.Throws<ArgumentException>(() => ContentPublish.ForMod(_folder, "https://github.test/someone/guards", null));
    }

    [Fact]
    public void A_mod_carrying_one_of_the_games_assemblies_or_missing_its_own_is_refused()
    {
        Write("mod.json", CodeManifest);
        Write("Guards.dll", "code");
        Write("OpenIGI.Engine.dll", "the game's");
        ContentPublish.Details details = ContentPublish.ForMod(_folder, "https://github.test/someone/guards", "0123abcd");

        Assert.Throws<InvalidDataException>(() => ContentPublish.Chunk(_folder, details, _cache, DateTimeOffset.UnixEpoch));

        File.Delete(Path.Combine(_folder, "OpenIGI.Engine.dll"));
        File.Delete(Path.Combine(_folder, "Guards.dll"));
        Assert.Throws<InvalidDataException>(() => ContentPublish.Chunk(_folder, details, _cache, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void A_mod_of_content_alone_for_every_game_carries_no_code_and_needs_no_source()
    {
        Write("mod.json", """{ "id": "voices", "version": "1.0.0", "apiVersion": 1, "summary": "New voices." }""");
        Write("sounds/a.wav", "sound");
        ContentPublish.Details details = ContentPublish.ForMod(_folder, null, null);

        ContentManifest manifest = ContentPublish.Chunk(_folder, details, _cache, DateTimeOffset.UnixEpoch).Manifest;
        Assert.Equal(ModPacks.AnyGame, manifest.Game);
        Assert.False(manifest.Mod!.RunsCode);
        Assert.Null(manifest.Mod.Source);
        Assert.True(manifest.Listing.IsFor("igi1") && manifest.Listing.IsFor("igi2"));

        Write("sneaky.dll", "code");
        Assert.Throws<InvalidDataException>(() => ContentPublish.Chunk(_folder, details, _cache, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void A_mod_without_a_summary_or_a_three_part_version_is_refused()
    {
        Write("mod.json", """{ "id": "voices", "version": "1.0.0", "apiVersion": 1 }""");
        Assert.Throws<ArgumentException>(() => ContentPublish.ForMod(_folder, null, null));

        Write("mod.json", """{ "id": "local", "version": "1.0.0", "apiVersion": 1, "summary": "Named as the launcher's own folder." }""");
        Assert.Throws<ArgumentException>(() => ContentPublish.ForMod(_folder, null, null));

        Write("mod.json", """{ "id": "voices", "version": "1.0", "apiVersion": 1, "summary": "New voices." }""");
        Write("a.wav", "sound");
        Assert.Throws<ArgumentException>(() => ContentPublish.Chunk(_folder, ContentPublish.ForMod(_folder, null, null), _cache, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void An_overlay_manifest_says_nothing_of_mods()
    {
        ContentManifest overlay = new(
            ContentManifest.CurrentSchema, "a-pack", "1.0.0", "A pack", "Summary", "igi1", null, DateTimeOffset.UnixEpoch, 4,
            [new ContentFile("a.res", 4, "ff", [new ContentChunk("aa", 4, 3)])]);

        Assert.DoesNotContain("\"mod\"", ReleaseJson.Write(overlay), StringComparison.Ordinal);
        Assert.True(overlay.Listing.IsFor("igi1"));
        Assert.False(overlay.Listing.IsFor("igi2"));
    }

    private void Write(string path, string text)
    {
        string file = Path.Combine(_folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}

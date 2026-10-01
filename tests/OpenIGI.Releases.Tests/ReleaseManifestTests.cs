using OpenIGI.Releases;
using OpenIGI.Tools.Release;
using Xunit;

namespace OpenIGI.Releases.Tests;

public sealed class ReleaseManifestTests
{
    // Trimmed from real `git cliff --context` output: two releases' worth of commits, a skipped type absent.
    private const string CliffContext = """
        [
          {
            "version": "v0.1.0-beta.2",
            "commits": [
              { "id": "a1", "message": "the traced sun is shadowed by the ground all round", "group": "<!-- 1 -->Fixes", "scope": "render" },
              { "id": "a2", "message": "shared interface kit and a standalone launcher\n\nBody text for reviewers.", "group": "<!-- 0 -->New", "scope": "ui" },
              { "id": "a3", "message": "group src into area folders", "group": "<!-- 3 -->Changes", "scope": null },
              { "id": "a4", "message": "frame pacing", "group": "<!-- 2 -->Performance", "scope": "" }
            ]
          }
        ]
        """;

    [Fact]
    public void Notes_come_out_in_the_configured_group_order_with_subject_lines_only()
    {
        IReadOnlyList<ReleaseNoteSection> notes = ReleaseNotesReader.Read(CliffContext);

        Assert.Equal(["New", "Fixes", "Performance", "Changes"], notes.Select(static s => s.Title));
        Assert.Equal(new ReleaseNote("ui", "Shared interface kit and a standalone launcher"), notes[0].Entries.Single());
        Assert.Equal(new ReleaseNote(null, "Group src into area folders"), notes[3].Entries.Single());
        Assert.Null(notes[2].Entries.Single().Scope);
    }

    [Theory]
    [InlineData("openigi-game-0.2.0-beta.3-win-x64.zip", ReleaseComponent.Game, "win-x64")]
    [InlineData("openigi-launcher-0.2.0-beta.3-osx-arm64.tar.gz", ReleaseComponent.Launcher, "osx-arm64")]
    [InlineData("openigi-web-0.2.0-beta.3-browser-wasm.tar.gz", ReleaseComponent.Web, "browser-wasm")]
    public void An_archive_name_says_what_it_holds(string name, ReleaseComponent component, string runtime)
    {
        Assert.True(AssetCatalog.TryParse(name, "0.2.0-beta.3", out ReleaseComponent parsed, out string rid));
        Assert.Equal(component, parsed);
        Assert.Equal(runtime, rid);
        Assert.Equal(name, AssetCatalog.FileName(component, "0.2.0-beta.3", runtime, name.EndsWith(".zip", StringComparison.Ordinal) ? ".zip" : ".tar.gz"));
    }

    [Theory]
    [InlineData("openigi-game-0.2.0-beta.2-win-x64.zip")]   // another release's archive
    [InlineData("openigi-game-0.2.0-beta.3-.zip")]          // no runtime
    [InlineData("openigi-game-0.2.0-beta.3-win-x64.7z")]    // not an archive the launcher can open
    [InlineData("notes.txt")]
    public void A_stray_file_is_not_taken_for_an_archive(string name) =>
        Assert.False(AssetCatalog.TryParse(name, "0.2.0-beta.3", out _, out _));

    [Fact]
    public void A_scanned_folder_round_trips_through_the_manifest()
    {
        string dir = Directory.CreateTempSubdirectory("openigi-release-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "openigi-game-0.1.0-linux-x64.tar.gz"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(dir, "openigi-launcher-0.1.0-linux-x64.tar.gz"), [4, 5]);
            File.WriteAllText(Path.Combine(dir, "SHA256SUMS"), "left over from a previous run");

            IReadOnlyList<ReleaseAsset> assets = AssetCatalog.Scan(dir, "0.1.0");
            Assert.Equal([ReleaseComponent.Game, ReleaseComponent.Launcher], assets.Select(static a => a.Component));
            Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", assets[0].Sha256);
            Assert.Equal(3, assets[0].Size);

            ReleaseManifest manifest = new(
                ReleaseManifest.CurrentSchema, "0.1.0", ReleaseChannel.Stable,
                new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "abc", ReleaseNotesReader.Read(CliffContext), assets);

            string json = ReleaseJson.Write(manifest);
            Assert.Contains("\"channel\": \"stable\"", json, StringComparison.Ordinal);
            Assert.Contains("\"component\": \"launcher\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"url\"", json, StringComparison.Ordinal);

            ReleaseManifest read = ReleaseJson.ReadManifest(json);
            Assert.Equal(manifest.Version, read.Version);
            Assert.Equal(manifest.Assets, read.Assets);
            Assert.Equal(manifest.Notes.Select(static s => s.Title), read.Notes.Select(static s => s.Title));
            Assert.Same(read.Assets[1], read.Find(ReleaseComponent.Launcher, "LINUX-X64"));
            Assert.Null(read.Find(ReleaseComponent.Game, "win-x64"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("stable", ReleaseChannel.Stable)]
    [InlineData(" Beta ", ReleaseChannel.Beta)]
    public void Channels_have_lowercase_wire_names(string name, ReleaseChannel channel)
    {
        Assert.True(ReleaseJson.TryParseChannel(name, out ReleaseChannel parsed));
        Assert.Equal(channel, parsed);
        Assert.Equal(name.Trim().ToLowerInvariant(), parsed.Name());
        Assert.False(ReleaseJson.TryParseChannel("nightly", out _));
    }
}

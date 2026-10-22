using OpenIGI.Core.Content;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Titles;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>A mod's <c>mod.json</c>.</summary>
public sealed class ModManifestTests
{
    [Fact]
    public void A_manifest_reads_case_insensitively_with_comments_and_defaults()
    {
        ModManifest manifest = ModManifest.Parse("""
            {
              // A comment, as a person writes one.
              "ID": "soldiers",
              "ApiVersion": 1,
              "Titles": ["IGI1"],
            }
            """);

        Assert.Equal("soldiers", manifest.Id);
        Assert.Equal("soldiers", manifest.Name);
        Assert.Equal("0", manifest.Version);
        Assert.Null(manifest.Assembly);
        Assert.True(manifest.AffectsSimulation);
        Assert.True(manifest.AppliesTo(GameTitle.Igi1));
        Assert.False(manifest.AppliesTo(GameTitle.Igi2));
        Assert.False(manifest.RunsCode);
        Assert.Empty(manifest.Authors);
        Assert.Empty(manifest.RequiresTitleData);
        Assert.Empty(manifest.Dependencies);
        Assert.Empty(manifest.Conflicts);
        Assert.Null(manifest.Summary);
        Assert.Null(manifest.MinGameVersion);
    }

    [Fact]
    public void A_manifest_reads_what_the_launcher_lists_and_checks()
    {
        ModManifest manifest = ModManifest.Parse("""
            {
              "id": "soldiers", "version": "1.2", "apiVersion": 1, "assembly": "Soldiers.dll",
              "summary": "Other soldiers.", "authors": ["Someone", "Another"], "homepage": "https://example.org/soldiers",
              "license": "MIT", "titles": ["igi1"], "requiresTitleData": ["igi2"],
              "dependencies": { "library": ">=2.0", "any": "*" }, "conflicts": ["rival"], "minGameVersion": "0.9.0"
            }
            """);

        Assert.True(manifest.RunsCode);
        Assert.Equal(["Someone", "Another"], manifest.Authors);
        Assert.Equal("Other soldiers.", manifest.Summary);
        Assert.Equal("https://example.org/soldiers", manifest.Homepage);
        Assert.Equal("MIT", manifest.License);
        Assert.Equal(["igi2"], manifest.RequiresTitleData);
        Assert.Equal(">=2.0", manifest.Dependencies["library"]);
        Assert.Equal(["rival"], manifest.Conflicts);
        Assert.Equal("0.9.0", manifest.MinGameVersion);
    }

    [Fact]
    public void A_pass_runs_over_the_lit_scene_unless_its_manifest_says_the_finished_picture()
    {
        ModManifest manifest = ModManifest.Parse("""
            {
              "id": "looks", "apiVersion": 1,
              "postPasses": [
                { "name": "Haze", "module": "Haze" },
                { "name": "Lines", "module": "Lines", "stage": "display", "toggle": "mod_looks_lines" }
              ]
            }
            """);

        Assert.Equal(
            [("Haze", PostPassStage.Scene), ("Lines", PostPassStage.Display)],
            manifest.PostPasses.Select(static p => (p.Name, p.Stage)));
        Assert.Equal("mod_looks_lines", manifest.PostPasses[1].Toggle);
    }

    [Theory]
    [InlineData("1.2", "1.2", 0)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("0.9", "1", -1)]
    [InlineData("2", "10", -1)]
    public void Versions_compare_by_their_numbers(string left, string right, int order)
    {
        Assert.Equal(order, ModVersion.Compare(left, right));
        Assert.Equal(-order, ModVersion.Compare(right, left));
    }

    [Theory]
    [InlineData("2.1", ">=2.0", true)]
    [InlineData("2.0", "2.0", true)]
    [InlineData("1.9", "2.0", false)]
    [InlineData("0.1", "*", true)]
    [InlineData("0.1", "", true)]
    public void A_requirement_is_the_oldest_version_that_will_do(string version, string requirement, bool met)
    {
        Assert.Equal(met, ModVersion.Satisfies(version, requirement));
    }

    [Theory]
    [InlineData("{ \"name\": \"no id\", \"apiVersion\": 1 }")]
    [InlineData("[]")]
    [InlineData("{")]
    public void A_manifest_without_an_id_or_not_json_is_refused(string json)
    {
        Assert.Throws<ModLoadException>(() => ModManifest.Parse(json));
    }

    [Fact]
    public void A_set_refuses_two_mods_of_one_id_and_a_mod_of_another_api()
    {
        ModManifest one = new("same", "One", "1", ModApi.Version, []);
        Assert.Throws<ModLoadException>(() => new ModSet([new InstalledMod(one, "a", null), new InstalledMod(one with { Name = "Two" }, "b", null)]));
        Assert.Throws<ModLoadException>(() => new ModSet([new InstalledMod(one with { ApiVersion = ModApi.Version + 1 }, "a", null)]));
    }
}

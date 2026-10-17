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

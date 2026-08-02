using OpenIGI.Game.Profiles;
using Xunit;

namespace OpenIGI.Game.Tests.Profiles;

/// <summary>Covers game-title identity, path-resolution precedence and installation probing.</summary>
public sealed class GameProfileTests
{
    [Fact]
    public void Igi1_is_supported_and_igi2_is_not()
    {
        Assert.Equal(GameAvailability.Available, GameTitle.Igi1.Support);
        Assert.Equal(GameAvailability.Unavailable, GameTitle.Igi2.Support);
        Assert.Equal(2, GameTitle.All.Count);
        Assert.Same(GameTitle.Igi1, GameTitle.All[0]);
    }

    [Theory]
    [InlineData("explicit", "env", "fallback", "explicit")]
    [InlineData(null, "env", "fallback", "env")]
    [InlineData(null, null, "fallback", "fallback")]
    [InlineData("  ", "env", "fallback", "env")]      // blank explicit is skipped
    [InlineData(null, null, null, "")]
    public void Path_resolution_follows_explicit_then_environment_then_fallback(
        string? explicitPath, string? environmentPath, string? fallbackPath, string expected)
    {
        Assert.Equal(expected, GameProfileResolver.ResolvePath(explicitPath, environmentPath, fallbackPath));
    }

    [Fact]
    public void Catalogue_binds_the_active_title_to_the_resolved_path_and_leaves_the_rest_unavailable()
    {
        IReadOnlyList<GameProfile> catalogue = GameProfileResolver.BuildCatalogue(@"C:\Games\IGI", null, null);

        GameProfile igi1 = catalogue[0];
        Assert.Equal("igi1", igi1.Id);
        Assert.Equal(@"C:\Games\IGI", igi1.DataRoot);

        GameProfile igi2 = catalogue[1];
        Assert.Equal("igi2", igi2.Id);
        Assert.Equal(GameAvailability.Unavailable, igi2.Availability); // never playable regardless of disk
        Assert.False(igi2.IsPlayable);
    }

    [Fact]
    public void A_profile_is_installed_only_when_its_signature_file_is_present()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profile = new GameProfile(GameTitle.Igi1, dir);
            Assert.False(profile.IsInstalled); // directory exists but no config.qvm
            Assert.Equal(GameAvailability.Unavailable, profile.Availability);

            File.WriteAllText(Path.Combine(dir, GameTitle.Igi1.SignatureFile), "stub");
            Assert.True(profile.IsInstalled);
            Assert.Equal(GameAvailability.Available, profile.Availability);
            Assert.True(profile.IsPlayable);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_missing_directory_is_not_installed()
    {
        var profile = new GameProfile(GameTitle.Igi1, @"Z:\definitely\not\here");
        Assert.False(profile.IsInstalled);
        Assert.Equal(GameAvailability.Unavailable, profile.Availability);
    }

    [Fact]
    public void Asset_root_paths_are_under_the_data_root()
    {
        var profile = new GameProfile(GameTitle.Igi1, Path.Combine("root", "igi"));
        string[] roots = [.. profile.AssetRootPaths()];
        Assert.Contains(Path.Combine("root", "igi", "missions"), roots);
        Assert.Contains(Path.Combine("root", "igi", "common"), roots);
    }
}

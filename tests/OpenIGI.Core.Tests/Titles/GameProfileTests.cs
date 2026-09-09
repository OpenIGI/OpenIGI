using OpenIGI.Core.Titles;
using Xunit;

namespace OpenIGI.Core.Tests.Titles;

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

    /// <summary>A directory that looks like an installation, because the signature file is in it.</summary>
    private static string MakeInstall(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, GameTitle.Igi1.SignatureFile), [0]);
        return root;
    }

    [Fact]
    public void Discovery_takes_the_first_candidate_that_holds_an_installation()
    {
        string temp = Path.Combine(Path.GetTempPath(), "openigi-discover-" + Guid.NewGuid().ToString("N"));

        try
        {
            string empty = Path.Combine(temp, "empty");
            string beside = MakeInstall(Path.Combine(temp, "beside-the-exe"));
            string working = MakeInstall(Path.Combine(temp, "working-dir"));
            Directory.CreateDirectory(empty);

            // A directory that merely exists is not an installation, so the search walks past it.
            Assert.Equal(beside, GameProfileResolver.DiscoverDataRoot([empty, beside, working], GameTitle.Igi1));

            // Order is preference: the same set, asked in the other order, answers the other one.
            Assert.Equal(working, GameProfileResolver.DiscoverDataRoot([empty, working, beside], GameTitle.Igi1));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Discovery_answers_null_when_nothing_around_is_an_installation()
    {
        string temp = Path.Combine(Path.GetTempPath(), "openigi-discover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            // A blank entry, a directory with nothing in it, and one that is not there at all.
            Assert.Null(GameProfileResolver.DiscoverDataRoot(
                [null, "  ", temp, Path.Combine(temp, "missing")], GameTitle.Igi1));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Discovery_over_no_candidates_is_not_an_error()
    {
        Assert.Null(GameProfileResolver.DiscoverDataRoot([], GameTitle.Igi1));
        Assert.Throws<ArgumentNullException>(() => GameProfileResolver.DiscoverDataRoot(null!, GameTitle.Igi1));
    }

    [Fact]
    public void Catalogue_binds_the_active_title_to_the_resolved_path_and_leaves_the_rest_unavailable()
    {
        IReadOnlyList<GameProfile> catalogue = GameProfileResolver.BuildCatalogue(GameTitle.Igi1, @"C:\Games\IGI", null, null);

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

    /// <summary>Discovery is per title: an I.G.I. 2 folder is not an I.G.I. 1 installation.</summary>
    /// <remarks>
    /// The two games ship different signature files, so the same directory answers differently
    /// depending on what is being looked for. Before this was parameterised the answer was always
    /// "I.G.I. 1", which would have reported a perfectly good I.G.I. 2 install as nothing at all.
    /// </remarks>
    [Fact]
    public void Discovery_answers_for_the_title_it_is_given()
    {
        string temp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(temp, GameTitle.Igi2.SignatureFile), string.Empty);

            Assert.Equal(temp, GameProfileResolver.DiscoverDataRoot([temp], GameTitle.Igi2));
            Assert.Null(GameProfileResolver.DiscoverDataRoot([temp], GameTitle.Igi1));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>Discover tries every title against a candidate before moving to the next candidate.</summary>
    /// <remarks>
    /// Candidate-major, so the folder the port was dropped into wins over a better-matching title
    /// further down the list. That ordering is what the drop-it-in-and-run install depends on.
    /// </remarks>
    [Fact]
    public void Discover_prefers_the_earlier_directory_over_the_earlier_title()
    {
        string first = Directory.CreateTempSubdirectory().FullName;
        string second = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(first, GameTitle.Igi2.SignatureFile), string.Empty);
            File.WriteAllText(Path.Combine(second, GameTitle.Igi1.SignatureFile), string.Empty);

            GameProfile? found = GameProfileResolver.Discover([first, second], GameTitle.All);

            Assert.NotNull(found);
            Assert.Equal(first, found.DataRoot);
            Assert.Equal(GameTitle.Igi2, found.Title);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    /// <summary>Discover reports nothing rather than guessing when no candidate holds a known title.</summary>
    [Fact]
    public void Discover_finds_nothing_in_an_empty_directory()
    {
        string temp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Null(GameProfileResolver.Discover([null, "  ", temp], GameTitle.All));
            Assert.Throws<ArgumentNullException>(() => GameProfileResolver.Discover(null!, GameTitle.All));
            Assert.Throws<ArgumentNullException>(() => GameProfileResolver.Discover([temp], null!));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>The catalogue binds the path to the title the caller says it belongs to.</summary>
    [Fact]
    public void The_catalogue_binds_the_path_to_the_active_title()
    {
        IReadOnlyList<GameProfile> catalogue =
            GameProfileResolver.BuildCatalogue(GameTitle.Igi2, @"C:\Games\IGI2\pc", null, null);

        GameProfile igi2 = catalogue.Single(p => p.Title == GameTitle.Igi2);
        GameProfile igi1 = catalogue.Single(p => p.Title == GameTitle.Igi1);

        Assert.Equal(@"C:\Games\IGI2\pc", igi2.DataRoot);
        Assert.Equal(string.Empty, igi1.DataRoot);
    }

    /// <summary>A profile no longer carries a mission registry, so Core does not depend on gameplay.</summary>
    /// <remarks>
    /// It carried one until the title types moved down into Core. Nothing read it, and it was the only
    /// reason an identity type referenced <c>OpenIGI.Game</c>; the session that scans the missions owns
    /// them. The layering tests would catch the reference, but not a property quietly coming back on a
    /// type that had already been made to depend on the right things.
    /// </remarks>
    [Fact]
    public void A_profile_carries_no_mission_registry()
    {
        Assert.DoesNotContain(
            typeof(GameProfile).GetProperties(),
            p => p.Name.Contains("Mission", StringComparison.Ordinal));
    }
}

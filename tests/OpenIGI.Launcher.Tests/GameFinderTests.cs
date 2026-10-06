using OpenIGI.Launcher.Services;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class GameFinderTests
{
    [Fact]
    public void Every_steam_library_is_read_with_its_backslashes_unescaped()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"apps"
            		{
            			"228980"		"1234"
            		}
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameFinder.SteamLibraries(vdf));
    }

    [Fact]
    public void A_library_file_with_no_libraries_names_none() =>
        Assert.Empty(GameFinder.SteamLibraries("\"libraryfolders\"\n{\n}\n"));

    [Fact]
    public void A_settings_file_from_before_the_guide_has_not_been_through_it()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openigi-onboarded-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "channel": "stable" }""");
            Assert.False(LauncherSettings.Load(path).Onboarded);

            LauncherSettings settings = LauncherSettings.Load(path);
            settings.Onboarded = true;
            settings.Save(path);
            Assert.True(LauncherSettings.Load(path).Onboarded);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

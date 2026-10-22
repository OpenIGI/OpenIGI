using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>The SDK a mod is built with writes the manifest the game's loader accepts.</summary>
public sealed partial class ModdingSdkTests
{
    [Fact]
    public void The_sdk_writes_manifests_for_the_api_this_build_offers()
    {
        string props = File.ReadAllText(Path.Combine(RepositoryRoot(), "sdk", "OpenIGI.Modding.Sdk", "Sdk", "OpenIGI.Modding.props"));

        Match version = ApiVersion().Match(props);

        Assert.True(version.Success, "OpenIGI.Modding.props names the modding API it builds for.");
        Assert.Equal(ModApi.Version, int.Parse(version.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenIGI.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests run outside the repository.");
    }

    [GeneratedRegex("<OpenIgiModApiVersion>([0-9]+)</OpenIgiModApiVersion>")]
    private static partial Regex ApiVersion();
}
